using System;
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

        public void AddLine(Vector2 a, Vector2 b, DebugDrawColor color, float thickness = 0.03f)
        {
            Lines.Add(new DebugDrawLine2D
            {
                A = a,
                B = b,
                Thickness = thickness,
                Color = color
            });
        }

        public void AddCircle(Vector2 center, float radius, DebugDrawColor color, float thickness = 0.03f)
        {
            Circles.Add(new DebugDrawCircle2D
            {
                Center = center,
                Radius = radius,
                Thickness = thickness,
                Color = color
            });
        }

        public void AddBox(Vector2 center, float halfWidth, float halfHeight, float rotationRadians, DebugDrawColor color, float thickness = 0.03f)
        {
            Boxes.Add(new DebugDrawBox2D
            {
                Center = center,
                HalfWidth = halfWidth,
                HalfHeight = halfHeight,
                RotationRadians = rotationRadians,
                Thickness = thickness,
                Color = color
            });
        }

        public void AddPolyline(ReadOnlySpan<Vector2> points, DebugDrawColor color, float thickness = 0.03f, bool closed = false)
        {
            if (points.Length < 2)
            {
                return;
            }

            for (int i = 1; i < points.Length; i++)
            {
                AddLine(points[i - 1], points[i], color, thickness);
            }

            if (closed)
            {
                AddLine(points[^1], points[0], color, thickness);
            }
        }

        public void AddArc(
            Vector2 center,
            float radius,
            float startRadians,
            float endRadians,
            DebugDrawColor color,
            float thickness = 0.03f,
            int segments = 0)
        {
            if (radius <= 0f)
            {
                return;
            }

            float delta = endRadians - startRadians;
            if (MathF.Abs(delta) <= 0.0001f)
            {
                return;
            }

            int segmentCount = segments > 0
                ? segments
                : Math.Clamp((int)MathF.Ceiling(MathF.Abs(delta) * radius / 0.18f), 6, 96);

            Vector2 previous = center + new Vector2(MathF.Cos(startRadians), MathF.Sin(startRadians)) * radius;
            for (int i = 1; i <= segmentCount; i++)
            {
                float t = (float)i / segmentCount;
                float angle = startRadians + delta * t;
                Vector2 current = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                AddLine(previous, current, color, thickness);
                previous = current;
            }
        }

        public void AddCapsule(
            Vector2 start,
            Vector2 end,
            float radius,
            DebugDrawColor color,
            float thickness = 0.03f,
            int arcSegments = 0,
            bool drawCenterLine = false)
        {
            if (radius <= 0f)
            {
                AddLine(start, end, color, thickness);
                return;
            }

            Vector2 axis = end - start;
            float axisLength = axis.Length();
            if (axisLength <= 0.0001f)
            {
                AddCircle(start, radius, color, thickness);
                return;
            }

            Vector2 dir = axis / axisLength;
            Vector2 normal = new Vector2(-dir.Y, dir.X) * radius;

            AddLine(start + normal, end + normal, color, thickness);
            AddLine(start - normal, end - normal, color, thickness);

            if (drawCenterLine)
            {
                AddLine(start, end, color, thickness);
            }

            float normalAngle = MathF.Atan2(normal.Y, normal.X);
            AddArc(start, radius, normalAngle, normalAngle + MathF.PI, color, thickness, arcSegments);
            AddArc(end, radius, normalAngle + MathF.PI, normalAngle + MathF.Tau, color, thickness, arcSegments);
        }

        public void Clear()
        {
            Lines.Clear();
            Circles.Clear();
            Boxes.Clear();
        }
    }
}
