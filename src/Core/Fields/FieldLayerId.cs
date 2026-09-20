using System;

namespace Ludots.Core.Fields
{
    public readonly struct FieldLayerId : IEquatable<FieldLayerId>
    {
        public FieldLayerId(int value)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Field layer id must be positive.");
            }

            Value = value;
        }

        public readonly int Value;

        public bool Equals(FieldLayerId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is FieldLayerId other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(FieldLayerId left, FieldLayerId right) => left.Equals(right);
        public static bool operator !=(FieldLayerId left, FieldLayerId right) => !left.Equals(right);
    }
}
