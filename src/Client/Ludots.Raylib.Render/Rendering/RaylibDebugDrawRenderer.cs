using System;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    public sealed class RaylibDebugDrawRenderer
    {
        public int CircleSegments { get; set; } = 32;
        public float PlaneY { get; set; } = 0f;
        /// <summary>世界米 (x, z) → 地面高度(米);null 时退回固定平面。返回 null 采样失败同样退回。</summary>
        public Func<float, float, float?>? GroundSamplerM { get; set; }

        public void Draw(DebugDrawCommandBuffer buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            for (int i = 0; i < buffer.Lines.Count; i++)
            {
                var line = buffer.Lines[i];
                Rl.DrawLine3D(ToV3(line.A), ToV3(line.B), ToColor(line.Color));
            }

            int segments = Math.Max(8, CircleSegments);
            for (int i = 0; i < buffer.Circles.Count; i++)
            {
                var circle = buffer.Circles[i];
                DrawCircle(circle.Center, circle.Radius, segments, circle.Color);
            }

            for (int i = 0; i < buffer.Boxes.Count; i++)
            {
                var box = buffer.Boxes[i];
                DrawBox(box.Center, box.HalfWidth, box.HalfHeight, box.Color);
            }
        }

        private void DrawCircle(Vector2 center, float radius, int segments, DebugDrawColor color)
        {
            var c = ToV3(center);
            var col = ToColor(color);

            for (int i = 0; i < segments; i++)
            {
                float a0 = (float)i / segments * MathF.Tau;
                float a1 = (float)(i + 1) / segments * MathF.Tau;

                var p0 = c + new Vector3(MathF.Cos(a0) * radius, 0f, MathF.Sin(a0) * radius);
                var p1 = c + new Vector3(MathF.Cos(a1) * radius, 0f, MathF.Sin(a1) * radius);
                Rl.DrawLine3D(p0, p1, col);
            }
        }

        private void DrawBox(Vector2 center, float halfWidth, float halfHeight, DebugDrawColor color)
        {
            var col = ToColor(color);
            var c = ToV3(center);

            var p0 = c + new Vector3(-halfWidth, 0f, -halfHeight);
            var p1 = c + new Vector3(halfWidth, 0f, -halfHeight);
            var p2 = c + new Vector3(halfWidth, 0f, halfHeight);
            var p3 = c + new Vector3(-halfWidth, 0f, halfHeight);

            Rl.DrawLine3D(p0, p1, col);
            Rl.DrawLine3D(p1, p2, col);
            Rl.DrawLine3D(p2, p3, col);
            Rl.DrawLine3D(p3, p0, col);
        }

        private Vector3 ToV3(Vector2 p) => new Vector3(p.X, (GroundSamplerM?.Invoke(p.X, p.Y) ?? 0f) + PlaneY, p.Y);

        private static Color ToColor(DebugDrawColor c) => new Color(c.R, c.G, c.B, c.A);
    }
}
