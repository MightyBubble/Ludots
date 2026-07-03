using System;

namespace Ludots.Core.Fields
{
    public readonly struct FieldCell2D : IEquatable<FieldCell2D>
    {
        public FieldCell2D(int x, int y)
        {
            X = x;
            Y = y;
        }

        public readonly int X;
        public readonly int Y;

        public bool Equals(FieldCell2D other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is FieldCell2D other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public static bool operator ==(FieldCell2D left, FieldCell2D right) => left.Equals(right);
        public static bool operator !=(FieldCell2D left, FieldCell2D right) => !left.Equals(right);
        public override string ToString() => $"({X},{Y})";
    }

    public readonly struct FieldCellValue2D<T>
        where T : struct
    {
        public FieldCellValue2D(FieldCell2D cell, T value)
        {
            Cell = cell;
            Value = value;
        }

        public readonly FieldCell2D Cell;
        public readonly T Value;
    }
}
