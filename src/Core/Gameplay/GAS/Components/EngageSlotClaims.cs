using System;
using Arch.Core;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Per-target engage slot claims (SoA, fixed capacity): the order kernel records which
    /// ring points around this entity are taken by which members so a later engage batch's
    /// EQS pick excludes them. Members are stored as id triplets (no Entity references —
    /// claims are order-kernel scratch on the world, not save-visible state). Stale entries
    /// (dead member) are pruned lazily by the next engage drain on this target.
    /// </summary>
    public unsafe struct EngageSlotClaims
    {
        public const int CAPACITY = 32;

        public int Count;
        public fixed int XCm[CAPACITY];
        public fixed int YCm[CAPACITY];
        public fixed int MemberIds[CAPACITY];
        public fixed int MemberWorldIds[CAPACITY];
        public fixed int MemberVersions[CAPACITY];

        /// <summary>True when an alive claimed member still occupies a point within claimRadiusCm.</summary>
        public bool IsClaimed(World world, int xCm, int yCm, int claimRadiusCm)
        {
            fixed (int* xs = XCm, ys = YCm, ids = MemberIds, worlds = MemberWorldIds, versions = MemberVersions)
            {
                for (int i = 0; i < Count; i++)
                {
                    int dx = xs[i] - xCm;
                    int dy = ys[i] - yCm;
                    if (dx * dx + dy * dy > (long)claimRadiusCm * claimRadiusCm)
                    {
                        continue;
                    }

                    var member = Ludots.Core.Gameplay.GAS.EntityUtil.Reconstruct(ids[i], worlds[i], versions[i]);
                    if (world.IsAlive(member))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Appends a claim; capacity overflow fails loud (fail-closed, no truncation).</summary>
        public void Claim(World world, int xCm, int yCm, Entity member)
        {
            PruneDead(world);
            if (Count >= CAPACITY)
            {
                throw new InvalidOperationException(
                    $"ORDER.ENGAGE.ERR.SlotClaimCapacity: target carries {CAPACITY} live engage claims; raise EngageSlotClaims.CAPACITY.");
            }

            fixed (int* xs = XCm, ys = YCm, ids = MemberIds, worlds = MemberWorldIds, versions = MemberVersions)
            {
                xs[Count] = xCm;
                ys[Count] = yCm;
                ids[Count] = member.Id;
                worlds[Count] = member.WorldId;
                versions[Count] = member.Version;
            }

            Count++;
        }

        private void PruneDead(World world)
        {
            fixed (int* xs = XCm, ys = YCm, ids = MemberIds, worlds = MemberWorldIds, versions = MemberVersions)
            {
                int write = 0;
                for (int i = 0; i < Count; i++)
                {
                    var member = Ludots.Core.Gameplay.GAS.EntityUtil.Reconstruct(ids[i], worlds[i], versions[i]);
                    if (world.IsAlive(member))
                    {
                        if (write != i)
                        {
                            xs[write] = xs[i];
                            ys[write] = ys[i];
                            ids[write] = ids[i];
                            worlds[write] = worlds[i];
                            versions[write] = versions[i];
                        }

                        write++;
                    }
                }

                Count = write;
            }
        }
    }
}
