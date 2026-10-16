using System;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 路线 / 标记视觉渲染器(gameplay 通道,非调试绘制):折线按折点逐段画 3D 线,
    /// 标记画环 / 菱形线框;地面高度经 GroundSamplerM 采样(宿主接线 ContinuousHeightmap),
    /// PlaneY 是离地偏移。
    /// </summary>
    public sealed class RaylibRouteVisualRenderer
    {
        public int MarkerSegments { get; set; } = 24;
        public float PlaneY { get; set; } = 0.45f;
        /// <summary>世界米 (x, z) → 地面高度(米);null 或采样失败退回固定平面。</summary>
        public Func<float, float, float?>? GroundSamplerM { get; set; }

        public void Draw(RouteVisualBuffer buffer)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            for (int i = 0; i < buffer.Polylines.Count; i++)
            {
                var line = buffer.Polylines[i];
                var color = ToColor(line.Color);
                var pts = line.PointsMeters;
                // DrawLine3D 没有线宽:粗线拆成两条近平行线(偏移半宽)近似
                for (int k = 1; k < pts.Length; k++)
                {
                    Rl.DrawLine3D(ToV3(pts[k - 1]), ToV3(pts[k]), color);
                    if (line.ThicknessMeters > 0.6f)
                    {
                        float off = line.ThicknessMeters * 0.35f;
                        Rl.DrawLine3D(ToV3(pts[k - 1], off), ToV3(pts[k], off), color);
                    }
                }
            }

            for (int i = 0; i < buffer.Markers.Count; i++)
            {
                var marker = buffer.Markers[i];
                var color = ToColor(marker.Color);
                switch (marker.Shape)
                {
                    case RouteVisualMarkerShape.Ring:
                        DrawRing(marker.PositionMeters, marker.RadiusMeters, color);
                        break;
                    case RouteVisualMarkerShape.Diamond:
                        DrawDiamond(marker.PositionMeters, marker.RadiusMeters, color);
                        break;
                }
            }
        }

        private void DrawRing(Vector2 center, float radius, Color color)
        {
            for (int i = 0; i < MarkerSegments; i++)
            {
                float a0 = (float)i / MarkerSegments * MathF.Tau;
                float a1 = (float)(i + 1) / MarkerSegments * MathF.Tau;
                var p0 = new Vector2(center.X + MathF.Cos(a0) * radius, center.Y + MathF.Sin(a0) * radius);
                var p1 = new Vector2(center.X + MathF.Cos(a1) * radius, center.Y + MathF.Sin(a1) * radius);
                Rl.DrawLine3D(ToV3(p0), ToV3(p1), color);
            }
        }

        private void DrawDiamond(Vector2 center, float radius, Color color)
        {
            Span<Vector2> corners = stackalloc Vector2[4]
            {
                new(center.X + radius, center.Y),
                new(center.X, center.Y + radius),
                new(center.X - radius, center.Y),
                new(center.X, center.Y - radius),
            };
            for (int i = 0; i < 4; i++)
            {
                Rl.DrawLine3D(ToV3(corners[i]), ToV3(corners[(i + 1) % 4]), color);
            }
        }

        private Vector3 ToV3(Vector2 p, float lift = 0f)
            => new(p.X + lift, (GroundSamplerM?.Invoke(p.X + lift, p.Y) ?? 0f) + PlaneY, p.Y);

        private static Color ToColor(Vector4 v)
            => new(
                (byte)Math.Clamp((int)(v.X * 255f), 0, 255),
                (byte)Math.Clamp((int)(v.Y * 255f), 0, 255),
                (byte)Math.Clamp((int)(v.Z * 255f), 0, 255),
                (byte)Math.Clamp((int)(v.W * 255f), 0, 255));
    }
}
