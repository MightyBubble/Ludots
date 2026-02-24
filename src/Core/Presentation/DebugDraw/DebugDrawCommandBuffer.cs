using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Core.Presentation.DebugDraw
{
    public readonly struct DebugDrawColor
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;
        public readonly byte A;

        public DebugDrawColor(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public static DebugDrawColor White => new DebugDrawColor(255, 255, 255);
        public static DebugDrawColor Red => new DebugDrawColor(255, 0, 0);
        public static DebugDrawColor Green => new DebugDrawColor(0, 255, 0);
        public static DebugDrawColor Blue => new DebugDrawColor(0, 0, 255);
        public static DebugDrawColor Yellow => new DebugDrawColor(255, 255, 0);
        public static DebugDrawColor Gray => new DebugDrawColor(128, 128, 128);
        public static DebugDrawColor Cyan => new DebugDrawColor(0, 255, 255);
    }

    public struct DebugDrawLine2D
    {
        public Vector2 A;
        public Vector2 B;
        public float Thickness;
        public DebugDrawColor Color;
    }

    public struct DebugDrawCircle2D
    {
        public Vector2 Center;
        public float Radius;
        public float Thickness;
        public DebugDrawColor Color;
    }

    public struct DebugDrawBox2D
    {
        public Vector2 Center;
        public float HalfWidth;
        public float HalfHeight;
        public float RotationRadians;
        public float Thickness;
        public DebugDrawColor Color;
    }

    public sealed class DebugDrawCommandBuffer
    {
        public List<DebugDrawLine2D> Lines { get; } = new List<DebugDrawLine2D>(4096);
        public List<DebugDrawCircle2D> Circles { get; } = new List<DebugDrawCircle2D>(2048);
        public List<DebugDrawBox2D> Boxes { get; } = new List<DebugDrawBox2D>(2048);

        public void Clear()
        {
            Lines.Clear();
            Circles.Clear();
            Boxes.Clear();
        }
    }
}
