using System;

namespace Ludots.Core.Mathematics
{
    public readonly struct IntRect : IEquatable<IntRect>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Width;
        public readonly int Height;

        public int Left => X;
        public int Top => Y; // Assuming Y-up or Y-down doesn't matter for struct logic, but usually Top is MinY or MaxY
        public int Right => X + Width;
        public int Bottom => Y + Height;

        public IntRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public bool Contains(IntVector2 point)
        {
            return point.X >= X && point.X < X + Width &&
                   point.Y >= Y && point.Y < Y + Height;
        }

        public bool Intersects(IntRect other)
        {
            return other.X < X + Width &&
                   other.X + other.Width > X &&
                   other.Y < Y + Height &&
                   other.Y + other.Height > Y;
        }

        public bool Equals(IntRect other)
        {
            return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        }

        public override bool Equals(object obj) => obj is IntRect other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
        public static bool operator ==(IntRect left, IntRect right) => left.Equals(right);
        public static bool operator !=(IntRect left, IntRect right) => !left.Equals(right);
        
        public override string ToString() => $"(X:{X}, Y:{Y}, W:{Width}, H:{Height})";
    }
}
