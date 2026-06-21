using System;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Knowledge
{
    /// <summary>
    /// Compact registry-id mask for finite knowledge disclosure aspects.
    /// </summary>
    public readonly struct KnowledgeIdMask256 : IEquatable<KnowledgeIdMask256>
    {
        public readonly ulong U0;
        public readonly ulong U1;
        public readonly ulong U2;
        public readonly ulong U3;

        public KnowledgeIdMask256(ulong u0, ulong u1, ulong u2, ulong u3)
        {
            U0 = u0;
            U1 = u1;
            U2 = u2;
            U3 = u3;
        }

        public static KnowledgeIdMask256 Empty => default;

        public bool IsEmpty => (U0 | U1 | U2 | U3) == 0UL;

        public KnowledgeIdMask256 WithId(int id)
        {
            ValidateId(id);
            ulong bit = 1UL << (id & 63);
            return (id >> 6) switch
            {
                0 => new KnowledgeIdMask256(U0 | bit, U1, U2, U3),
                1 => new KnowledgeIdMask256(U0, U1 | bit, U2, U3),
                2 => new KnowledgeIdMask256(U0, U1, U2 | bit, U3),
                _ => new KnowledgeIdMask256(U0, U1, U2, U3 | bit),
            };
        }

        public bool ContainsId(int id)
        {
            ValidateId(id);
            ulong bit = 1UL << (id & 63);
            return (id >> 6) switch
            {
                0 => (U0 & bit) != 0UL,
                1 => (U1 & bit) != 0UL,
                2 => (U2 & bit) != 0UL,
                _ => (U3 & bit) != 0UL,
            };
        }

        public KnowledgeIdMask256 Union(in KnowledgeIdMask256 other)
        {
            return new KnowledgeIdMask256(
                U0 | other.U0,
                U1 | other.U1,
                U2 | other.U2,
                U3 | other.U3);
        }

        public bool ContainsAll(in KnowledgeIdMask256 required)
        {
            return (U0 & required.U0) == required.U0 &&
                   (U1 & required.U1) == required.U1 &&
                   (U2 & required.U2) == required.U2 &&
                   (U3 & required.U3) == required.U3;
        }

        public bool Intersects(in KnowledgeIdMask256 other)
        {
            return ((U0 & other.U0) |
                    (U1 & other.U1) |
                    (U2 & other.U2) |
                    (U3 & other.U3)) != 0UL;
        }

        public bool Equals(KnowledgeIdMask256 other)
        {
            return U0 == other.U0 &&
                   U1 == other.U1 &&
                   U2 == other.U2 &&
                   U3 == other.U3;
        }

        public override bool Equals(object? obj)
        {
            return obj is KnowledgeIdMask256 other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(U0, U1, U2, U3);
        }

        public static bool operator ==(KnowledgeIdMask256 left, KnowledgeIdMask256 right) => left.Equals(right);

        public static bool operator !=(KnowledgeIdMask256 left, KnowledgeIdMask256 right) => !left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateId(int id)
        {
            if ((uint)id >= 256u)
            {
                throw new ArgumentOutOfRangeException(nameof(id), "Knowledge id masks support registry ids from 0 through 255.");
            }
        }
    }
}
