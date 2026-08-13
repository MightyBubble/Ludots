using System;
using System.Numerics;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Presentation.Rendering;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Adapter.Raylib
{
    internal static class RaylibWorldOverlayRenderer
    {
        public static void DrawGroundOverlays(GroundOverlayBuffer overlays)
        {
            if (overlays == null)
            {
                throw new ArgumentNullException(nameof(overlays));
            }

            ReadOnlySpan<GroundOverlayItem> span = overlays.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly GroundOverlayItem item = ref span[i];
                switch (item.Shape)
                {
                    case GroundOverlayShape.Circle:
                        DrawGroundCircle(in item);
                        break;
                    case GroundOverlayShape.Cone:
                        DrawGroundCone(in item);
                        break;
                    case GroundOverlayShape.Ring:
                        DrawGroundRing(in item);
                        break;
                    case GroundOverlayShape.Line:
                        DrawGroundLine(in item);
                        break;
                }
            }
        }

        public static void DrawSplineRibbons(SplineRibbonBuffer splines)
        {
            if (splines == null)
            {
                throw new ArgumentNullException(nameof(splines));
            }

            ReadOnlySpan<float> p0x = splines.P0X;
            ReadOnlySpan<float> p0y = splines.P0Y;
            ReadOnlySpan<float> p0z = splines.P0Z;
            ReadOnlySpan<float> p1x = splines.P1X;
            ReadOnlySpan<float> p1y = splines.P1Y;
            ReadOnlySpan<float> p1z = splines.P1Z;
            ReadOnlySpan<float> p2x = splines.P2X;
            ReadOnlySpan<float> p2y = splines.P2Y;
            ReadOnlySpan<float> p2z = splines.P2Z;
            ReadOnlySpan<float> p3x = splines.P3X;
            ReadOnlySpan<float> p3y = splines.P3Y;
            ReadOnlySpan<float> p3z = splines.P3Z;
            ReadOnlySpan<float> width = splines.Width;
            ReadOnlySpan<float> borderWidth = splines.BorderWidth;
            ReadOnlySpan<float> fillR = splines.FillR;
            ReadOnlySpan<float> fillG = splines.FillG;
            ReadOnlySpan<float> fillB = splines.FillB;
            ReadOnlySpan<float> fillA = splines.FillA;
            ReadOnlySpan<float> borderR = splines.BorderR;
            ReadOnlySpan<float> borderG = splines.BorderG;
            ReadOnlySpan<float> borderB = splines.BorderB;
            ReadOnlySpan<float> borderA = splines.BorderA;

            for (int i = 0; i < splines.Count; i++)
            {
                Vector3 p0 = new(p0x[i], p0y[i], p0z[i]);
                Vector3 p1 = new(p1x[i], p1y[i], p1z[i]);
                Vector3 p2 = new(p2x[i], p2y[i], p2z[i]);
                Vector3 p3 = new(p3x[i], p3y[i], p3z[i]);
                float drawWidth = MathF.Max(0.02f, width[i]);
                float drawBorder = MathF.Max(0.01f, borderWidth[i]);
                Color fill = ToRaylibColor(new Vector4(fillR[i], fillG[i], fillB[i], fillA[i]));
                Color border = ToRaylibColor(new Vector4(borderR[i], borderG[i], borderB[i], borderA[i]));
                DrawRoadSplineRibbon(p0, p1, p2, p3, drawWidth, fill, border, drawBorder);
            }
        }

        private static void DrawRoadSplineRibbon(
            in Vector3 p0,
            in Vector3 p1,
            in Vector3 p2,
            in Vector3 p3,
            float width,
            Color fill,
            Color border,
            float borderWidth)
        {
            const int samples = 20;
            Span<Vector3> points = stackalloc Vector3[samples + 1];
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                points[i] = EvaluateCubicBezier(p0, p1, p2, p3, t);
            }

            if (fill.a > 0)
            {
                int lanes = Math.Max(1, (int)MathF.Ceiling(width / 0.08f));
                for (int lane = 0; lane < lanes; lane++)
                {
                    float alpha = lanes == 1 ? 0f : lane / (float)(lanes - 1);
                    float offset = (alpha - 0.5f) * width;
                    DrawOffsetPolyline(points, offset, fill);
                }
            }

            if (border.a > 0)
            {
                float edgeOffset = (width * 0.5f) + borderWidth;
                DrawOffsetPolyline(points, edgeOffset, border);
                DrawOffsetPolyline(points, -edgeOffset, border);
            }
        }

        private static void DrawOffsetPolyline(ReadOnlySpan<Vector3> points, float offset, Color color)
        {
            if (points.Length < 2)
            {
                return;
            }

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
            return new Vector3(current.X + lateral.X * offset, current.Y, current.Z + lateral.Y * offset);
        }

        private static Vector3 EvaluateCubicBezier(in Vector3 p0, in Vector3 p1, in Vector3 p2, in Vector3 p3, float t)
        {
            float oneMinusT = 1f - t;
            float a = oneMinusT * oneMinusT * oneMinusT;
            float b = 3f * oneMinusT * oneMinusT * t;
            float c = 3f * oneMinusT * t * t;
            float d = t * t * t;
            return (p0 * a) + (p1 * b) + (p2 * c) + (p3 * d);
        }

        private static void DrawGroundCircle(in GroundOverlayItem item)
        {
            const int segments = 48;
            float step = MathF.PI * 2f / segments;
            Vector3 center = item.Center;

            if (item.FillColor.W > 0.01f)
            {
                Color fillColor = ToRaylibColor(item.FillColor);
                const int fillRings = 4;
                for (int r = 1; r <= fillRings; r++)
                {
                    float radius = item.Radius * r / fillRings;
                    for (int s = 0; s < segments; s++)
                    {
                        float a0 = s * step;
                        float a1 = (s + 1) * step;
                        Vector3 p0 = new(center.X + MathF.Cos(a0) * radius, center.Y, center.Z + MathF.Sin(a0) * radius);
                        Vector3 p1 = new(center.X + MathF.Cos(a1) * radius, center.Y, center.Z + MathF.Sin(a1) * radius);
                        Rl.DrawLine3D(p0, p1, fillColor);
                    }
                }
            }

            if (item.BorderColor.W > 0.01f && item.BorderWidth > 0f)
            {
                Color border = ToRaylibColor(item.BorderColor);
                for (int s = 0; s < segments; s++)
                {
                    float a0 = s * step;
                    float a1 = (s + 1) * step;
                    Vector3 p0 = new(center.X + MathF.Cos(a0) * item.Radius, center.Y, center.Z + MathF.Sin(a0) * item.Radius);
                    Vector3 p1 = new(center.X + MathF.Cos(a1) * item.Radius, center.Y, center.Z + MathF.Sin(a1) * item.Radius);
                    Rl.DrawLine3D(p0, p1, border);
                }
            }
        }

        private static void DrawGroundRing(in GroundOverlayItem item)
        {
            const int segments = 48;
            float innerRadius = Math.Clamp(item.InnerRadius, 0f, item.Radius);
            float outerRadius = MathF.Max(item.Radius, innerRadius);
            Vector3 center = item.Center;

            if (item.FillColor.W > 0.01f && outerRadius > innerRadius)
            {
                Color fillColor = ToRaylibColor(item.FillColor);
                const int bands = 6;
                for (int band = 0; band < bands; band++)
                {
                    float radius = innerRadius + (outerRadius - innerRadius) * (band + 0.5f) / bands;
                    DrawGroundArcLoop(center, radius, 0f, MathF.PI * 2f, segments, fillColor);
                }
            }

            if (item.BorderColor.W > 0.01f && item.BorderWidth > 0f)
            {
                Color border = ToRaylibColor(item.BorderColor);
                DrawGroundArcLoop(center, outerRadius, 0f, MathF.PI * 2f, segments, border);
                if (innerRadius > 0.001f)
                {
                    DrawGroundArcLoop(center, innerRadius, 0f, MathF.PI * 2f, segments, border);
                }
            }
        }

        private static void DrawGroundCone(in GroundOverlayItem item)
        {
            const int segments = 24;
            float radius = MathF.Max(item.Radius, 0f);
            float start = item.Rotation - item.Angle;
            float end = item.Rotation + item.Angle;
            Vector3 center = item.Center;

            if (radius <= 0f)
            {
                return;
            }

            if (item.FillColor.W > 0.01f)
            {
                Color fillColor = ToRaylibColor(item.FillColor);
                const int bands = 6;
                for (int band = 1; band <= bands; band++)
                {
                    float ringRadius = radius * band / bands;
                    DrawGroundArcLoop(center, ringRadius, start, end, segments, fillColor);
                }
            }

            if (item.BorderColor.W > 0.01f && item.BorderWidth > 0f)
            {
                Color border = ToRaylibColor(item.BorderColor);
                DrawGroundArcLoop(center, radius, start, end, segments, border);
                Vector3 left = new(center.X + MathF.Cos(start) * radius, center.Y, center.Z + MathF.Sin(start) * radius);
                Vector3 right = new(center.X + MathF.Cos(end) * radius, center.Y, center.Z + MathF.Sin(end) * radius);
                Rl.DrawLine3D(center, left, border);
                Rl.DrawLine3D(center, right, border);
            }
        }

        private static void DrawGroundLine(in GroundOverlayItem item)
        {
            float length = item.Length > 0f ? item.Length : item.Radius;
            if (length <= 0f)
            {
                return;
            }

            float dx = MathF.Cos(item.Rotation) * length;
            float dz = MathF.Sin(item.Rotation) * length;
            Vector3 a = item.Center;
            Vector3 b = new(a.X + dx, a.Y, a.Z + dz);
            float halfWidth = MathF.Max(0f, item.Width) * 0.5f;
            Vector3 normal = new(-MathF.Sin(item.Rotation), 0f, MathF.Cos(item.Rotation));

            if (item.FillColor.W > 0.01f)
            {
                Color fill = ToRaylibColor(item.FillColor);
                int stripes = halfWidth > 0.001f ? Math.Clamp((int)MathF.Ceiling(halfWidth / 0.12f), 1, 8) : 1;
                for (int stripe = -stripes; stripe <= stripes; stripe++)
                {
                    float offset = stripes == 0 ? 0f : halfWidth * stripe / Math.Max(stripes, 1);
                    Vector3 delta = normal * offset;
                    Rl.DrawLine3D(a + delta, b + delta, fill);
                }
            }

            if (item.BorderColor.W > 0.01f)
            {
                Color border = ToRaylibColor(item.BorderColor);
                Rl.DrawLine3D(a, b, border);
                if (halfWidth > 0.001f)
                {
                    Vector3 delta = normal * halfWidth;
                    Rl.DrawLine3D(a + delta, b + delta, border);
                    Rl.DrawLine3D(a - delta, b - delta, border);
                }
            }
        }

        private static void DrawGroundArcLoop(Vector3 center, float radius, float startAngle, float endAngle, int segments, Color color)
        {
            if (segments <= 0 || radius <= 0f)
            {
                return;
            }

            float step = (endAngle - startAngle) / segments;
            for (int s = 0; s < segments; s++)
            {
                float a0 = startAngle + s * step;
                float a1 = startAngle + (s + 1) * step;
                Vector3 p0 = new(center.X + MathF.Cos(a0) * radius, center.Y, center.Z + MathF.Sin(a0) * radius);
                Vector3 p1 = new(center.X + MathF.Cos(a1) * radius, center.Y, center.Z + MathF.Sin(a1) * radius);
                Rl.DrawLine3D(p0, p1, color);
            }
        }

        private static Color ToRaylibColor(in Vector4 c) => RaylibColorUtil.ToRaylibColor(in c);
    }
}
