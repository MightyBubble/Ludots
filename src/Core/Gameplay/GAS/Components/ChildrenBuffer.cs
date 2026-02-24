using System.Runtime.CompilerServices;
using Arch.Core;

namespace Ludots.Core.Gameplay.GAS.Components
{
    public unsafe struct ChildrenBuffer
    {
        public int Count;
        public fixed int ChildIds[GasConstants.MAX_CHILDREN_BUFFER_CAPACITY];
        public fixed int ChildWorldIds[GasConstants.MAX_CHILDREN_BUFFER_CAPACITY];
        public fixed int ChildVersions[GasConstants.MAX_CHILDREN_BUFFER_CAPACITY];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(in Entity child)
        {
            fixed (int* ids = ChildIds)
            fixed (int* worlds = ChildWorldIds)
            fixed (int* versions = ChildVersions)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (ids[i] != child.Id) continue;
                    if (worlds[i] != child.WorldId) continue;
                    if (versions[i] != child.Version) continue;
                    return true;
                }
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Add(in Entity child)
        {
            if (Count >= GasConstants.MAX_CHILDREN_BUFFER_CAPACITY) return false;
            if (Contains(in child)) return true;

            fixed (int* ids = ChildIds)
            fixed (int* worlds = ChildWorldIds)
            fixed (int* versions = ChildVersions)
            {
                ids[Count] = child.Id;
                worlds[Count] = child.WorldId;
                versions[Count] = child.Version;
                Count++;
                return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(in Entity child)
        {
            fixed (int* ids = ChildIds)
            fixed (int* worlds = ChildWorldIds)
            fixed (int* versions = ChildVersions)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (ids[i] != child.Id) continue;
                    if (worlds[i] != child.WorldId) continue;
                    if (versions[i] != child.Version) continue;

                    int last = Count - 1;
                    ids[i] = ids[last];
                    worlds[i] = worlds[last];
                    versions[i] = versions[last];
                    Count--;
                    return true;
                }
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity Get(int index)
        {
            fixed (int* ids = ChildIds)
            fixed (int* worlds = ChildWorldIds)
            fixed (int* versions = ChildVersions)
            {
                return ReconstructEntity(ids[index], worlds[index], versions[index]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Entity ReconstructEntity(int id, int worldId, int version)
            => EntityUtil.Reconstruct(id, worldId, version);
    }
}
