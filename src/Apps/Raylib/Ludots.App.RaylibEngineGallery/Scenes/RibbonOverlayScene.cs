using System.Numerics;
using Ludots.Platform.Abstractions;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>
    /// 样条带覆盖层：GroundOverlayBuffer 手工填充（圈/扇形/连线），样条带以画廊内等价 SoA 数据
    /// （Core 的 SplineRibbonBuffer 与 Adapter 的 RaylibWorldOverlayRenderer 内核不可被零 Core 依赖的画廊引用，
    /// 故此处以相同三次贝塞尔 + 偏移折线技术等价绘制；收口点见画廊任务报告）。
    /// </summary>
    public sealed class RibbonOverlayScene : IEngineScene
    {
        private readonly GroundOverlayBuffer _overlays = new(capacity: 64);
        private readonly List<SceneRibbon> _ribbons = new(8);

        public string Id => "ribbon_overlay";
        public string Title => "样条带覆盖层";
        public string Summary => "GroundOverlayBuffer + 样条带世界覆盖层";

        public void Load()
        {
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 60f);
            float t = (float)totalTimeSeconds;

            Rl.ClearBackground(new Color(14, 16, 24, 255));
            Rl.BeginMode3D(camera);
            Rl.DrawGrid(30, 3f);
            Rl.DrawCube(new Vector3(-12f, 0.9f, 6f), 2.4f, 1.8f, 2.4f, new Color(74, 110, 168, 255));
            Rl.DrawCube(new Vector3(13f, 0.9f, -8f), 2.4f, 1.8f, 2.4f, new Color(168, 96, 74, 255));

            FillOverlays(t);
            DrawGroundOverlays();

            FillRibbons(t);
            DrawRibbons();

            Rl.EndMode3D();
        }

        private void FillOverlays(float t)
        {
            _overlays.Clear();
            _overlays.Upsert(new GroundOverlayItem
            {
                StableId = 1,
                Shape = GroundOverlayShape.Ring,
                Center = new Vector3(-12f, 0.06f, 6f),
                Radius = 6.5f + (MathF.Sin(t * 1.4f) * 1.1f),
                InnerRadius = 4.2f + (MathF.Sin(t * 1.4f) * 0.8f),
                FillColor = new Vector4(0.30f, 0.62f, 1.00f, 0.34f),
                BorderColor = new Vector4(0.45f, 0.78f, 1.00f, 0.85f),
                BorderWidth = 0.12f,
            });
            _overlays.Upsert(new GroundOverlayItem
            {
                StableId = 2,
                Shape = GroundOverlayShape.Cone,
                Center = new Vector3(-12f, 0.06f, 6f),
                Radius = 12f,
                Angle = 0.42f,
                Rotation = t * 0.9f,
                FillColor = new Vector4(1.00f, 0.72f, 0.25f, 0.22f),
                BorderColor = new Vector4(1.00f, 0.80f, 0.35f, 0.8f),
                BorderWidth = 0.1f,
            });
            _overlays.Upsert(new GroundOverlayItem
            {
                StableId = 3,
                Shape = GroundOverlayShape.Line,
                Center = new Vector3(-12f, 0.06f, 6f),
                Rotation = MathF.Atan2(-8f - 6f, 13f - -12f),
                Length = Vector3.Distance(new Vector3(-12f, 0f, 6f), new Vector3(13f, 0f, -8f)),
                Width = 1.1f,
                FillColor = new Vector4(0.85f, 0.35f, 0.35f, 0.25f),
                BorderColor = new Vector4(0.95f, 0.45f, 0.45f, 0.8f),
                BorderWidth = 0.1f,
            });
            _overlays.Upsert(new GroundOverlayItem
            {
                StableId = 4,
                Shape = GroundOverlayShape.Circle,
                Center = new Vector3(13f, 0.06f, -8f),
                Radius = 5.2f + (MathF.Sin(t * 2f + 1f) * 0.5f),
                FillColor = new Vector4(1.00f, 0.45f, 0.55f, 0.20f),
                BorderColor = new Vector4(1.00f, 0.55f, 0.62f, 0.85f),
                BorderWidth = 0.12f,
            });
        }

        private void FillRibbons(float t)
        {
            _ribbons.Clear();
            _ribbons.Add(new SceneRibbon(
                new Vector3(-12f, 0.1f, 6f),
                new Vector3(-6f + MathF.Sin(t * 0.8f) * 3f, 0.1f, 4f),
                new Vector3(4f, 0.1f, -1f + MathF.Cos(t * 0.7f) * 3f),
                new Vector3(13f, 0.1f, -8f),
                1.4f,
                new Vector4(0.35f, 0.95f, 0.65f, 0.75f),
                new Vector4(0.6f, 1f, 0.8f, 0.9f),
                0.14f));
            _ribbons.Add(new SceneRibbon(
                new Vector3(-18f, 0.1f, -12f),
                new Vector3(-8f, 0.1f, -16f + MathF.Sin(t * 1.1f) * 2.5f),
                new Vector3(4f, 0.1f, -16f - MathF.Sin(t * 0.9f) * 2.5f),
                new Vector3(16f, 0.1f, -12f),
                0.8f,
                new Vector4(0.55f, 0.65f, 1.00f, 0.7f),
                new Vector4(0.75f, 0.82f, 1f, 0.9f),
                0.1f));
            _ribbons.Add(new SceneRibbon(
                new Vector3(-16f, 0.1f, 14f),
                new Vector3(-4f + MathF.Sin(t * 1.3f) * 2f, 0.1f, 17f),
                new Vector3(8f, 0.1f, 16f + MathF.Cos(t * 1.1f) * 2f),
                new Vector3(17f, 0.1f, 13f),
                0.55f,
                new Vector4(1.00f, 0.85f, 0.35f, 0.7f),
                new Vector4(1f, 0.92f, 0.55f, 0.9f),
                0.08f));
        }

        private void DrawGroundOverlays()
        {
            const int segments = 48;
            foreach (GroundOverlayItem item in _overlays.GetSpan())
            {
                switch (item.Shape)
                {
                    case GroundOverlayShape.Circle:
                        DrawArcLoop(item.Center, item.Radius, 0f, MathF.Tau, segments, ToColor(item.FillColor));
                        DrawArcLoop(item.Center, item.Radius, 0f, MathF.Tau, segments, ToColor(item.BorderColor));
                        break;
                    case GroundOverlayShape.Ring:
                        float inner = Math.Clamp(item.InnerRadius, 0f, item.Radius);
                        float outer = MathF.Max(item.Radius, inner);
                        const int bands = 6;
                        for (int band = 0; band < bands; band++)
                        {
                            float radius = inner + ((outer - inner) * (band + 0.5f) / bands);
                            DrawArcLoop(item.Center, radius, 0f, MathF.Tau, segments, ToColor(item.FillColor));
                        }

                        DrawArcLoop(item.Center, outer, 0f, MathF.Tau, segments, ToColor(item.BorderColor));
                        if (inner > 0.001f)
                        {
                            DrawArcLoop(item.Center, inner, 0f, MathF.Tau, segments, ToColor(item.BorderColor));
                        }

                        break;
                    case GroundOverlayShape.Cone:
                        DrawArcLoop(item.Center, item.Radius, item.Rotation - item.Angle, item.Rotation + item.Angle, 24, ToColor(item.BorderColor));
                        DrawArcLoop(item.Center, item.Radius * 0.66f, item.Rotation - item.Angle, item.Rotation + item.Angle, 24, ToColor(item.FillColor));
                        DrawArcLoop(item.Center, item.Radius * 0.33f, item.Rotation - item.Angle, item.Rotation + item.Angle, 24, ToColor(item.FillColor));
                        Rl.DrawLine3D(item.Center, PointOnArc(item.Center, item.Radius, item.Rotation - item.Angle), ToColor(item.BorderColor));
                        Rl.DrawLine3D(item.Center, PointOnArc(item.Center, item.Radius, item.Rotation + item.Angle), ToColor(item.BorderColor));
                        break;
                    case GroundOverlayShape.Line:
                        Vector3 end = item.Center + new Vector3(MathF.Cos(item.Rotation) * item.Length, 0f, MathF.Sin(item.Rotation) * item.Length);
                        Rl.DrawLine3D(item.Center, end, ToColor(item.BorderColor));
                        Vector3 lateral = new(-MathF.Sin(item.Rotation), 0f, MathF.Cos(item.Rotation));
                        Rl.DrawLine3D(item.Center + lateral * (item.Width * 0.5f), end + lateral * (item.Width * 0.5f), ToColor(item.FillColor));
                        Rl.DrawLine3D(item.Center - lateral * (item.Width * 0.5f), end - lateral * (item.Width * 0.5f), ToColor(item.FillColor));
                        break;
                }
            }
        }

        private static Vector3 PointOnArc(Vector3 center, float radius, float angle)
        {
            return center + new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
        }

        private static void DrawArcLoop(Vector3 center, float radius, float start, float end, int segments, Color color)
        {
            if (radius <= 0f)
            {
                return;
            }

            float step = (end - start) / segments;
            for (int s = 0; s < segments; s++)
            {
                Rl.DrawLine3D(
                    PointOnArc(center, radius, start + (s * step)),
                    PointOnArc(center, radius, start + ((s + 1) * step)),
                    color);
            }
        }

        private void DrawRibbons()
        {
            const int samples = 24;
            Span<Vector3> points = stackalloc Vector3[samples + 1];
            foreach (SceneRibbon ribbon in _ribbons)
            {
                for (int i = 0; i <= samples; i++)
                {
                    points[i] = EvaluateBezier(ribbon.P0, ribbon.P1, ribbon.P2, ribbon.P3, i / (float)samples);
                }

                Color fill = ToColor(ribbon.Fill);
                int lanes = Math.Max(1, (int)MathF.Ceiling(ribbon.Width / 0.08f));
                for (int lane = 0; lane < lanes; lane++)
                {
                    float alpha = lanes == 1 ? 0f : lane / (float)(lanes - 1);
                    DrawOffsetPolyline(points, (alpha - 0.5f) * ribbon.Width, fill);
                }

                Color border = ToColor(ribbon.Border);
                float edge = (ribbon.Width * 0.5f) + ribbon.BorderWidth;
                DrawOffsetPolyline(points, edge, border);
                DrawOffsetPolyline(points, -edge, border);
            }
        }

        private static void DrawOffsetPolyline(ReadOnlySpan<Vector3> points, float offset, Color color)
        {
            Vector3 previous = OffsetPoint(points, 0, offset);
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 current = OffsetPoint(points, i, offset);
                Rl.DrawLine3D(previous, current, color);
                previous = current;
            }
        }

        private static Vector3 OffsetPoint(ReadOnlySpan<Vector3> points, int index, float offset)
        {
            Vector3 current = points[index];
            Vector3 forward = index == points.Length - 1
                ? current - points[index - 1]
                : points[index + 1] - current;
            Vector2 lateral = new(-forward.Z, forward.X);
            float length = lateral.Length();
            if (length <= 0.0001f)
            {
                return current;
            }

            lateral /= length;
            return new Vector3(current.X + (lateral.X * offset), current.Y, current.Z + (lateral.Y * offset));
        }

        private static Vector3 EvaluateBezier(in Vector3 p0, in Vector3 p1, in Vector3 p2, in Vector3 p3, float t)
        {
            float oneMinusT = 1f - t;
            float a = oneMinusT * oneMinusT * oneMinusT;
            float b = 3f * oneMinusT * oneMinusT * t;
            float c = 3f * oneMinusT * t * t;
            float d = t * t * t;
            return (p0 * a) + (p1 * b) + (p2 * c) + (p3 * d);
        }

        private static Color ToColor(in Vector4 c)
        {
            return new Color(
                (byte)Math.Clamp((int)(c.X * 255f), 0, 255),
                (byte)Math.Clamp((int)(c.Y * 255f), 0, 255),
                (byte)Math.Clamp((int)(c.Z * 255f), 0, 255),
                (byte)Math.Clamp((int)(c.W * 255f), 0, 255));
        }

        public void Dispose()
        {
        }

        private readonly record struct SceneRibbon(
            Vector3 P0,
            Vector3 P1,
            Vector3 P2,
            Vector3 P3,
            float Width,
            Vector4 Fill,
            Vector4 Border,
            float BorderWidth);
    }
}
