using System;

namespace Ludots.Core.Navigation.GraphCore
{
    public readonly struct TagBits256 : IEquatable<TagBits256>
    {
        public readonly ulong U0;
        public readonly ulong U1;
        public readonly ulong U2;
        public readonly ulong U3;

        public TagBits256(ulong u0, ulong u1, ulong u2, ulong u3)
        {
            U0 = u0;
            U1 = u1;
            U2 = u2;
            U3 = u3;
        }

        public bool Equals(TagBits256 other)
        {
            return U0 == other.U0 && U1 == other.U1 && U2 == other.U2 && U3 == other.U3;
        }

        public override bool Equals(object obj)
        {
            return obj is TagBits256 other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(U0, U1, U2, U3);
        }

        public bool ContainsAll(in TagBits256 required)
        {
            return (U0 & required.U0) == required.U0
                && (U1 & required.U1) == required.U1
                && (U2 & required.U2) == required.U2
                && (U3 & required.U3) == required.U3;
        }

        public bool Intersects(in TagBits256 other)
        {
            return (U0 & other.U0) != 0
                || (U1 & other.U1) != 0
                || (U2 & other.U2) != 0
                || (U3 & other.U3) != 0;
        }
    }
}

