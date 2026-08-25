using System;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 地面覆盖层与样条带的实心渲染：三角扇（Circle/Cone）、三角带（Ring/Line/条带路径）、
    /// 以 borderWidth 为世界宽度的窄条带边框。几何生成全部委托 GroundOverlayGeometry（纯函数）。
    /// </summary>
    public static class RaylibWorldOverlayRenderer
    {
        public static void DrawGroundOverlays(GroundOverlayBuffer overlays)
        {
            if (overlays == null)
            {
                throw new ArgumentNullException(nameof(overlays));
            }

            Span<Vector3> fillVertices = stackalloc Vector3[GroundOverlayGeometry.MaxFillVertices];
            Span<Vector3> borderVertices = stackalloc Vector3[GroundOverlayGeometry.MaxBorderVertices];

            // 与 RaylibSkyEnvironment 同款配对：实心贴地面片禁用背面剔除，画完恢复帧内默认开启态。
            Rl.rlDisableBackfaceCulling();
            try
            {
                ReadOnlySpan<GroundOverlayItem> span = overlays.GetSpan();
                for (int i = 0; i < span.Length; i++)
                {
                    ref readonly GroundOverlayItem item = ref span[i];
                    int fillCount;
                    int borderCount;
                    switch (item.Shape)
                    {
                        case GroundOverlayShape.Circle:
                            fillCount = GroundOverlayGeometry.WriteCircleFill(in item, fillVertices);
                            borderCount = GroundOverlayGeometry.WriteCircleBorder(in item, borderVertices);
                            break;
                        case GroundOverlayShape.Cone:
                            fillCount = GroundOverlayGeometry.WriteConeFill(in item, fillVertices);
                            borderCount = GroundOverlayGeometry.WriteConeBorder(in item, borderVertices);
                            break;
                        case GroundOverlayShape.Ring:
                            fillCount = GroundOverlayGeometry.WriteRingFill(in item, fillVertices);
                            borderCount = GroundOverlayGeometry.WriteRingBorder(in item, borderVertices);
                            break;
                        case GroundOverlayShape.Line:
                            fillCount = GroundOverlayGeometry.WriteLineFill(in item, fillVertices);
                            borderCount = GroundOverlayGeometry.WriteLineBorder(in item, borderVertices);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(overlays), item.Shape, "Unknown GroundOverlayShape.");
                    }

                    if (fillCount > 0 && item.FillColor.W > 0.01f)
                    {
                        DrawTriangles(fillVertices[..fillCount], ToRaylibColor(item.FillColor));
                    }

                    if (borderCount > 0 && item.BorderColor.W > 0.01f)
                    {
                        DrawTriangles(borderVertices[..borderCount], ToRaylibColor(item.BorderColor));
                    }
                }
            }
            finally
            {
                Rl.rlEnableBackfaceCulling();
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

            Span<Vector3> path = stackalloc Vector3[GroundOverlayGeometry.SplineRibbonSamples + 1];
            Span<Vector3> strip = stackalloc Vector3[GroundOverlayGeometry.SplineRibbonStripVertices];

            Rl.rlDisableBackfaceCulling();
            try
            {
                for (int i = 0; i < splines.Count; i++)
                {
                    Vector3 p0 = new(p0x[i], p0y[i], p0z[i]);
                    Vector3 p1 = new(p1x[i], p1y[i], p1z[i]);
                    Vector3 p2 = new(p2x[i], p2y[i], p2z[i]);
                    Vector3 p3 = new(p3x[i], p3y[i], p3z[i]);
                    for (int s = 0; s < path.Length; s++)
                    {
                        path[s] = EvaluateCubicBezier(p0, p1, p2, p3, s / (float)GroundOverlayGeometry.SplineRibbonSamples);
                    }

                    float drawWidth = MathF.Max(0.02f, width[i]);
                    float drawBorder = MathF.Max(0.01f, borderWidth[i]);
                    if (fillA[i] > 0.01f)
                    {
                        Color fill = ToRaylibColor(new Vector4(fillR[i], fillG[i], fillB[i], fillA[i]));
                        int count = GroundOverlayGeometry.WriteRibbonStrip(path, 0f, drawWidth, GroundOverlayGeometry.GroundLiftMeters, strip);
                        DrawTriangles(strip[..count], fill);
                    }

                    if (borderA[i] > 0.01f)
                    {
                        Color border = ToRaylibColor(new Vector4(borderR[i], borderG[i], borderB[i], borderA[i]));
                        float edgeOffset = drawWidth * 0.5f;
                        int left = GroundOverlayGeometry.WriteRibbonStrip(path, edgeOffset, drawBorder, GroundOverlayGeometry.BorderLiftMeters, strip);
                        DrawTriangles(strip[..left], border);
                        int right = GroundOverlayGeometry.WriteRibbonStrip(path, -edgeOffset, drawBorder, GroundOverlayGeometry.BorderLiftMeters, strip);
                        DrawTriangles(strip[..right], border);
                    }
                }
            }
            finally
            {
                Rl.rlEnableBackfaceCulling();
            }
        }

        private static void DrawTriangles(ReadOnlySpan<Vector3> vertices, Color color)
        {
            for (int v = 0; v < vertices.Length; v += 3)
            {
                Rl.DrawTriangle3D(vertices[v], vertices[v + 1], vertices[v + 2], color);
            }
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

        private static Color ToRaylibColor(in Vector4 c) => RaylibColorUtil.ToRaylibColor(in c);
    }
}
