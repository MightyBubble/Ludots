using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.AI.WorldState
{
    public struct WorldStateBits256 : IEquatable<WorldStateBits256>
    {
        public ulong U0;
        public ulong U1;
        public ulong U2;
        public ulong U3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Equals(WorldStateBits256 other)
        {
            return U0 == other.U0 && U1 == other.U1 && U2 == other.U2 && U3 == other.U3;
        }

        public override readonly bool Equals(object? obj)
        {
            return obj is WorldStateBits256 other && Equals(other);
        }

        public override readonly int GetHashCode()
        {
            return HashCode.Combine(U0, U1, U2, U3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            U0 = 0;
            U1 = 0;
            U2 = 0;
            U3 = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool ContainsAll(in WorldStateBits256 required)
        {
            return (U0 & required.U0) == required.U0
                && (U1 & required.U1) == required.U1
                && (U2 & required.U2) == required.U2
                && (U3 & required.U3) == required.U3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Intersects(in WorldStateBits256 other)
        {
            return (U0 & other.U0) != 0
                || (U1 & other.U1) != 0
                || (U2 & other.U2) != 0
                || (U3 & other.U3) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Match(in WorldStateBits256 mask, in WorldStateBits256 values)
        {
            return ((U0 & mask.U0) == (values.U0 & mask.U0))
                && ((U1 & mask.U1) == (values.U1 & mask.U1))
                && ((U2 & mask.U2) == (values.U2 & mask.U2))
                && ((U3 & mask.U3) == (values.U3 & mask.U3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBit(int atomId, bool value)
        {
            if ((uint)atomId >= 256u) return;
            int word = atomId >> 6;
            int bit = atomId & 63;
            ulong mask = 1UL << bit;
            switch (word)
            {
                case 0:
                    U0 = value ? (U0 | mask) : (U0 & ~mask);
                    break;
                case 1:
                    U1 = value ? (U1 | mask) : (U1 & ~mask);
                    break;
                case 2:
                    U2 = value ? (U2 | mask) : (U2 & ~mask);
                    break;
                default:
                    U3 = value ? (U3 | mask) : (U3 & ~mask);
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool GetBit(int atomId)
        {
            if ((uint)atomId >= 256u) return false;
            int word = atomId >> 6;
            int bit = atomId & 63;
            ulong mask = 1UL << bit;
            return word switch
            {
                0 => (U0 & mask) != 0,
                1 => (U1 & mask) != 0,
                2 => (U2 & mask) != 0,
                _ => (U3 & mask) != 0
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int CountOnes()
        {
            return BitOperations.PopCount(U0)
                 + BitOperations.PopCount(U1)
                 + BitOperations.PopCount(U2)
                 + BitOperations.PopCount(U3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WorldStateBits256 operator &(WorldStateBits256 a, WorldStateBits256 b)
        {
            return new WorldStateBits256
            {
                U0 = a.U0 & b.U0,
                U1 = a.U1 & b.U1,
                U2 = a.U2 & b.U2,
                U3 = a.U3 & b.U3
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WorldStateBits256 operator |(WorldStateBits256 a, WorldStateBits256 b)
        {
            return new WorldStateBits256
            {
                U0 = a.U0 | b.U0,
                U1 = a.U1 | b.U1,
                U2 = a.U2 | b.U2,
                U3 = a.U3 | b.U3
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WorldStateBits256 operator ~(WorldStateBits256 a)
        {
            return new WorldStateBits256
            {
                U0 = ~a.U0,
                U1 = ~a.U1,
                U2 = ~a.U2,
                U3 = ~a.U3
            };
        }
    }
}

