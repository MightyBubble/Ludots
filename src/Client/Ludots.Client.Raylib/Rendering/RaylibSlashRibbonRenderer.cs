using System;
using System.Numerics;
using Ludots.Core.Presentation.Rendering;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed class RaylibSlashRibbonRenderer
    {
        public int MaxSegments { get; set; } = 48;
        public int MaxBands { get; set; } = 10;

        public void Draw(SlashRibbonBuffer buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            var span = buffer.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                switch (item.Shape)
                {
                    case SlashRibbonShape.Segment:
                        DrawSegment(in item);
                        break;
                    default:
                        DrawArc(in item);
                        break;
                }
            }
        }

        private void DrawArc(in SlashRibbonItem item)
        {
            float radius = MathF.Max(0.05f, item.Radius);
            float width = MathF.Max(0.01f, item.Width);
            float span = MathF.Max(0.08f, MathF.Abs(item.Span));
            float start = item.Rotation - span * 0.5f;
            float end = item.Rotation + span * 0.5f;
            int segments = Math.Clamp((int)MathF.Ceiling(radius * span / 0.08f), 6, MaxSegments);
            int bands = Math.Clamp((int)MathF.Ceiling(width / 0.05f), 2, MaxBands);
            float halfWidth = width * 0.5f;

            for (int band = 0; band < bands; band++)
            {
                float bandT = bands <= 1 ? 0f : (float)band / (bands - 1);
                float bandRadius = radius - halfWidth + width * bandT;
                if (bandRadius <= 0.01f)
                {
                    continue;
                }

                Color bandColor = ToRaylibColor(ResolveBandColor(in item, bandT));
                Vector3 previous = EvaluateArcPoint(in item, bandRadius, start, 0f);
                for (int segment = 1; segment <= segments; segment++)
                {
                    float t = (float)segment / segments;
                    float angle = start + (end - start) * t;
                    Vector3 current = EvaluateArcPoint(in item, bandRadius, angle, t);
                    Rl.DrawLine3D(previous, current, bandColor);
                    previous = current;
                }
            }

            if (item.EdgeColor.W > 0.01f)
            {
                var edgeColor = ToRaylibColor(item.EdgeColor);
                DrawArcEdge(in item, radius + halfWidth, start, end, edgeColor, segments);
                DrawArcEdge(in item, MathF.Max(0.01f, radius - halfWidth), start, end, edgeColor, segments);
            }
        }

        private void DrawSegment(in SlashRibbonItem item)
        {
            float length = MathF.Max(0.05f, item.Length > 0f ? item.Length : item.Radius);
            float width = MathF.Max(0.01f, item.Width);
            int stripes = Math.Clamp((int)MathF.Ceiling(width / 0.05f), 1, MaxBands);
            Vector3 forward = new Vector3(MathF.Cos(item.Rotation), 0f, MathF.Sin(item.Rotation));
            Vector3 right = new Vector3(-forward.Z, 0f, forward.X);

            for (int stripe = 0; stripe < stripes; stripe++)
            {
                float stripeT = stripes <= 1 ? 0.5f : (float)stripe / (stripes - 1);
                float offset = (stripeT - 0.5f) * width;
                Color stripeColor = ToRaylibColor(ResolveBandColor(in item, stripeT));
                Vector3 previous = item.Origin + right * offset;
                for (int segment = 1; segment <= MaxSegments; segment++)
                {
                    float t = (float)segment / MaxSegments;
                    Vector3 current = item.Origin + forward * (length * t) + right * offset;
                    current.Y += MathF.Sin(t * MathF.PI) * item.Height;
                    Rl.DrawLine3D(previous, current, stripeColor);
                    previous = current;
                }
            }
        }

        private void DrawArcEdge(in SlashRibbonItem item, float radius, float start, float end, Color color, int segments)
        {
            Vector3 previous = EvaluateArcPoint(in item, radius, start, 0f);
            for (int segment = 1; segment <= segments; segment++)
            {
                float t = (float)segment / segments;
                float angle = start + (end - start) * t;
                Vector3 current = EvaluateArcPoint(in item, radius, angle, t);
                Rl.DrawLine3D(previous, current, color);
                previous = current;
            }
        }

        private static Vector3 EvaluateArcPoint(in SlashRibbonItem item, float radius, float angle, float t)
        {
            return new Vector3(
                item.Origin.X + MathF.Cos(angle) * radius,
                item.Origin.Y + MathF.Sin(t * MathF.PI) * item.Height,
                item.Origin.Z + MathF.Sin(angle) * radius);
        }

        private static Vector4 ResolveBandColor(in SlashRibbonItem item, float bandT)
        {
            float edgeWeight = MathF.Abs(bandT - 0.5f) * 2f;
            return Lerp(item.FillColor, item.EdgeColor, Math.Clamp(edgeWeight, 0f, 1f));
        }

        private static Vector4 Lerp(Vector4 from, Vector4 to, float t)
        {
            return new Vector4(
                from.X + (to.X - from.X) * t,
                from.Y + (to.Y - from.Y) * t,
                from.Z + (to.Z - from.Z) * t,
                from.W + (to.W - from.W) * t);
        }

        private static Color ToRaylibColor(in Vector4 color) => RaylibColorUtil.ToRaylibColor(in color);
    }
}
