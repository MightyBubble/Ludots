using System;
using System.Numerics;
using Ludots.Core.Presentation.DebugDraw;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed class RaylibDebugDrawRenderer
    {
        public int CircleSegments { get; set; } = 32;
        public float PlaneY { get; set; } = 0f;

        public void Draw(DebugDrawCommandBuffer buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            for (int i = 0; i < buffer.Lines.Count; i++)
            {
                var line = buffer.Lines[i];
                DrawGroundLine(line.A, line.B, line.Thickness, line.Color);
            }

            int segments = Math.Max(8, CircleSegments);
            for (int i = 0; i < buffer.Circles.Count; i++)
            {
                var circle = buffer.Circles[i];
                DrawCircle(circle.Center, circle.Radius, segments, circle.Thickness, circle.Color);
            }

            for (int i = 0; i < buffer.Boxes.Count; i++)
            {
                var box = buffer.Boxes[i];
                DrawBox(box.Center, box.HalfWidth, box.HalfHeight, box.RotationRadians, box.Thickness, box.Color);
            }
        }

        private void DrawCircle(Vector2 center, float radius, int segments, DebugDrawColor color)
        {
            DrawCircle(center, radius, segments, thickness: 0.03f, color);
        }

        private void DrawCircle(Vector2 center, float radius, int segments, float thickness, DebugDrawColor color)
        {
            var c = ToV3(center);
            var col = ToColor(color);
            int ringCount = thickness > 0.03f
                ? Math.Clamp((int)MathF.Ceiling(thickness / 0.05f), 1, 8)
                : 1;
            float halfThickness = MathF.Max(0f, thickness) * 0.5f;

            for (int ring = 0; ring < ringCount; ring++)
            {
                float ringT = ringCount <= 1 ? 0.5f : (float)ring / (ringCount - 1);
                float ringRadius = MathF.Max(0.005f, radius - halfThickness + thickness * ringT);
                for (int i = 0; i < segments; i++)
                {
                    float a0 = (float)i / segments * MathF.Tau;
                    float a1 = (float)(i + 1) / segments * MathF.Tau;

                    var p0 = c + new Vector3(MathF.Cos(a0) * ringRadius, 0f, MathF.Sin(a0) * ringRadius);
                    var p1 = c + new Vector3(MathF.Cos(a1) * ringRadius, 0f, MathF.Sin(a1) * ringRadius);
                    Rl.DrawLine3D(p0, p1, col);
                }
            }
        }

        private void DrawBox(Vector2 center, float halfWidth, float halfHeight, float rotationRadians, float thickness, DebugDrawColor color)
        {
            var col = ToColor(color);
            int outlineCount = thickness > 0.03f
                ? Math.Clamp((int)MathF.Ceiling(thickness / 0.05f), 1, 8)
                : 1;
            float halfThickness = MathF.Max(0f, thickness) * 0.5f;

            for (int outline = 0; outline < outlineCount; outline++)
            {
                float outlineT = outlineCount <= 1 ? 0.5f : (float)outline / (outlineCount - 1);
                float extentOffset = -halfThickness + thickness * outlineT;
                float currentHalfWidth = halfWidth + extentOffset;
                float currentHalfHeight = halfHeight + extentOffset;
                if (currentHalfWidth <= 0f || currentHalfHeight <= 0f)
                {
                    continue;
                }

                var p0 = Rotate(center, new Vector2(-currentHalfWidth, -currentHalfHeight), rotationRadians);
                var p1 = Rotate(center, new Vector2(currentHalfWidth, -currentHalfHeight), rotationRadians);
                var p2 = Rotate(center, new Vector2(currentHalfWidth, currentHalfHeight), rotationRadians);
                var p3 = Rotate(center, new Vector2(-currentHalfWidth, currentHalfHeight), rotationRadians);

                Rl.DrawLine3D(ToV3(p0), ToV3(p1), col);
                Rl.DrawLine3D(ToV3(p1), ToV3(p2), col);
                Rl.DrawLine3D(ToV3(p2), ToV3(p3), col);
                Rl.DrawLine3D(ToV3(p3), ToV3(p0), col);
            }
        }

        private Vector3 ToV3(Vector2 p) => new Vector3(p.X, PlaneY, p.Y);

        private void DrawGroundLine(Vector2 a, Vector2 b, float thickness, DebugDrawColor color)
        {
            var col = ToColor(color);
            Vector2 delta = b - a;
            float len = delta.Length();
            if (len <= 0.0001f)
            {
                return;
            }

            int stripeCount = thickness > 0.03f
                ? Math.Clamp((int)MathF.Ceiling(thickness / 0.05f), 1, 8)
                : 1;
            Vector2 normal = new Vector2(-delta.Y / len, delta.X / len);

            for (int stripe = 0; stripe < stripeCount; stripe++)
            {
                float stripeT = stripeCount <= 1 ? 0.5f : (float)stripe / (stripeCount - 1);
                float offset = (-thickness * 0.5f) + thickness * stripeT;
                Vector2 deltaOffset = normal * offset;
                Rl.DrawLine3D(ToV3(a + deltaOffset), ToV3(b + deltaOffset), col);
            }
        }

        private static Vector2 Rotate(Vector2 center, Vector2 local, float rotationRadians)
        {
            float sin = MathF.Sin(rotationRadians);
            float cos = MathF.Cos(rotationRadians);
            return center + new Vector2(
                local.X * cos - local.Y * sin,
                local.X * sin + local.Y * cos);
        }

        private static Color ToColor(DebugDrawColor c) => new Color(c.R, c.G, c.B, c.A);
    }
}
