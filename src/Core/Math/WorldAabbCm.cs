using System;

namespace Ludots.Core.Mathematics
{
    public readonly struct WorldAabbCm : IEquatable<WorldAabbCm>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Width;
        public readonly int Height;

        public int Left => X;
        public int Top => Y;
        public int Right => X + Width;
        public int Bottom => Y + Height;

        public WorldAabbCm(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public static WorldAabbCm FromCenterRadius(WorldCmInt2 center, int radiusCm)
        {
            int d = radiusCm * 2;
            return new WorldAabbCm(center.X - radiusCm, center.Y - radiusCm, d, d);
        }

        public bool Equals(WorldAabbCm other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        public override bool Equals(object obj) => obj is WorldAabbCm other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
        public static bool operator ==(WorldAabbCm left, WorldAabbCm right) => left.Equals(right);
        public static bool operator !=(WorldAabbCm left, WorldAabbCm right) => !left.Equals(right);

        public override string ToString() => $"(X:{X}cm, Y:{Y}cm, W:{Width}cm, H:{Height}cm)";
    }
}
