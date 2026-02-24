using System;

namespace Ludots.Core.Mathematics
{
    public readonly struct WorldCmInt2 : IEquatable<WorldCmInt2>
    {
        public readonly int X;
        public readonly int Y;

        public WorldCmInt2(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static WorldCmInt2 Zero => new WorldCmInt2(0, 0);

        public static WorldCmInt2 operator +(WorldCmInt2 a, WorldCmInt2 b) => new WorldCmInt2(a.X + b.X, a.Y + b.Y);
        public static WorldCmInt2 operator -(WorldCmInt2 a, WorldCmInt2 b) => new WorldCmInt2(a.X - b.X, a.Y - b.Y);
        public static WorldCmInt2 operator *(WorldCmInt2 a, int b) => new WorldCmInt2(a.X * b, a.Y * b);
        public static WorldCmInt2 operator /(WorldCmInt2 a, int b) => new WorldCmInt2(a.X / b, a.Y / b);

        public bool Equals(WorldCmInt2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is WorldCmInt2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public static bool operator ==(WorldCmInt2 left, WorldCmInt2 right) => left.Equals(right);
        public static bool operator !=(WorldCmInt2 left, WorldCmInt2 right) => !left.Equals(right);

        public override string ToString() => $"({X}cm, {Y}cm)";
    }
}
