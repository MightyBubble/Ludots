using System;
using System.Numerics;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// Ground overlay 实心填充的三角网格生成（纯函数，无 GL 依赖，供渲染器与测试共用）。
    /// 所有三角形按从上往下看（+Y）逆时针绕序输出，保证背面剔除开启时仍可见。
    /// 深度偏移与 RaylibDecalProjectorRenderer.DecalReceiverDepthBiasMeters 同源：
    /// 贴地绘制统一沿地面法线抬 0.04 visual meters，避免与不透明地面 z-fighting。
    /// </summary>
    public static class GroundOverlayGeometry
    {
        public const float GroundLiftMeters = 0.04f;
        public const float BorderLiftMeters = 0.05f;

        public const int CircleSegments = 48;
        public const int RingSegments = 48;
        public const int ConeSegments = 24;
        public const int SplineRibbonSamples = 20;

        public const int CircleFillVertices = CircleSegments * 3;
        public const int ConeFillVertices = ConeSegments * 3;
        public const int RingFillVertices = RingSegments * 3 * 2;
        public const int LineFillVertices = 6;
        public const int RingBorderVertices = RingSegments * 3 * 4;
        public const int ConeBorderVertices = (ConeSegments * 3 * 2) + (3 * 2 * 2);
        public const int SplineRibbonStripVertices = SplineRibbonSamples * 3 * 2;

        public const int MaxFillVertices = RingFillVertices;
        public const int MaxBorderVertices = RingBorderVertices;

        public static int WriteCircleFill(in GroundOverlayItem item, Span<Vector3> vertices)
        {
            if (item.Radius <= 0f)
            {
                return 0;
            }

            RequireCapacity(vertices, CircleFillVertices, nameof(WriteCircleFill));
            float y = item.Center.Y + GroundLiftMeters;
            Vector3 apex = new(item.Center.X, y, item.Center.Z);
            float step = MathF.PI * 2f / CircleSegments;
            int written = 0;
            for (int s = 0; s < CircleSegments; s++)
            {
                float a0 = s * step;
                float a1 = (s + 1) * step;
                vertices[written++] = apex;
                vertices[written++] = RimPoint(item.Center, y, item.Radius, a1);
                vertices[written++] = RimPoint(item.Center, y, item.Radius, a0);
            }

            return written;
        }

        public static int WriteConeFill(in GroundOverlayItem item, Span<Vector3> vertices)
        {
            if (item.Radius <= 0f)
            {
                return 0;
            }

            // Core 只保证 angle >= 0；超过半圈时按整圈封顶，避免扇形自重叠产生双混合。
            float halfAngle = MathF.Min(item.Angle, MathF.PI);
            RequireCapacity(vertices, ConeFillVertices, nameof(WriteConeFill));
            float y = item.Center.Y + GroundLiftMeters;
            Vector3 apex = new(item.Center.X, y, item.Center.Z);
            float start = item.Rotation - halfAngle;
            float step = (halfAngle * 2f) / ConeSegments;
            int written = 0;
            for (int s = 0; s < ConeSegments; s++)
            {
                float a0 = start + (s * step);
                float a1 = start + ((s + 1) * step);
                vertices[written++] = apex;
                vertices[written++] = RimPoint(item.Center, y, item.Radius, a1);
                vertices[written++] = RimPoint(item.Center, y, item.Radius, a0);
            }

            return written;
        }

        public static int WriteRingFill(in GroundOverlayItem item, Span<Vector3> vertices)
        {
            float innerRadius = Math.Clamp(item.InnerRadius, 0f, item.Radius);
            float outerRadius = MathF.Max(item.Radius, innerRadius);
            if (outerRadius <= innerRadius)
            {
                return 0;
            }

            return WriteArcBand(item.Center, item.Center.Y + GroundLiftMeters, innerRadius, outerRadius, 0f, MathF.PI * 2f, RingSegments, vertices);
        }

        public static int WriteLineFill(in GroundOverlayItem item, Span<Vector3> vertices)
        {
            float length = item.Length > 0f ? item.Length : item.Radius;
            if (length <= 0f)
            {
                return 0;
            }

            RequireCapacity(vertices, LineFillVertices, nameof(WriteLineFill));
            float y = item.Center.Y + GroundLiftMeters;
            Vector3 direction = new(MathF.Cos(item.Rotation), 0f, MathF.Sin(item.Rotation));
            Vector3 lateral = new(-MathF.Sin(item.Rotation), 0f, MathF.Cos(item.Rotation));
            float halfWidth = MathF.Max(0f, item.Width) * 0.5f;
            Vector3 a = WithY(item.Center, y);
            Vector3 b = a + (direction * length);
            Vector3 aPlus = a + (lateral * halfWidth);
            Vector3 aMinus = a - (lateral * halfWidth);
            Vector3 bPlus = b + (lateral * halfWidth);
            Vector3 bMinus = b - (lateral * halfWidth);
            vertices[0] = aPlus;
            vertices[1] = bPlus;
            vertices[2] = aMinus;
            vertices[3] = bPlus;
            vertices[4] = bMinus;
            vertices[5] = aMinus;
            return LineFillVertices;
        }

        public static int WriteCircleBorder(in GroundOverlayItem item, Span<Vector3> vertices)
        {
            if (item.Radius <= 0f || item.BorderWidth <= 0f)
            {
                return 0;
            }

            float halfExtent = MathF.Min(item.BorderWidth * 0.5f, item.Radius);
            return WriteArcBand(
                item.Center,
                item.Center.Y + BorderLiftMeters,
                item.Radius - halfExtent,
                item.Radius + halfExtent,
                0f,
                MathF.PI * 2f,
                CircleSegments,
                vertices);
        }

        public static int WriteRingBorder(in GroundOverlayItem item, Span<Vector3> vertices)
        {
            float innerRadius = Math.Clamp(item.InnerRadius, 0f, item.Radius);
            float outerRadius = MathF.Max(item.Radius, innerRadius);
            if (outerRadius <= innerRadius || item.BorderWidth <= 0f)
            {
                return 0;
            }

            // 内外两条边带各自以环边为中心；半宽夹到环宽的一半，极端 borderWidth 时两条带最多在环中线相遇。
            float halfExtent = MathF.Min(item.BorderWidth * 0.5f, (outerRadius - innerRadius) * 0.5f);
            float y = item.Center.Y + BorderLiftMeters;
            int written = WriteArcBand(item.Center, y, outerRadius - halfExtent, outerRadius + halfExtent, 0f, MathF.PI * 2f, RingSegments, vertices);
            if (innerRadius > halfExtent)
            {
                written += WriteArcBand(
                    item.Center,
                    y,
                    innerRadius - halfExtent,
                    innerRadius + halfExtent,
                    0f,
                    MathF.PI * 2f,
                    RingSegments,
                    vertices.Slice(written));
            }

            return written;
        }

        public static int WriteConeBorder(in GroundOverlayItem item, Span<Vector3> vertices)
        {
            if (item.Radius <= 0f || item.BorderWidth <= 0f)
            {
                return 0;
            }

            float halfAngle = MathF.Min(item.Angle, MathF.PI);
            float halfExtent = MathF.Min(item.BorderWidth * 0.5f, item.Radius);
            float y = item.Center.Y + BorderLiftMeters;
            float start = item.Rotation - halfAngle;
            float step = (halfAngle * 2f) / ConeSegments;
            int written = WriteArcBand(item.Center, y, item.Radius - halfExtent, item.Radius + halfExtent, start, start + (halfAngle * 2f), ConeSegments, vertices);
            written += WriteRadialEdgeBand(item.Center, y, item.Radius + halfExtent, item.BorderWidth, start, vertices.Slice(written));
            written += WriteRadialEdgeBand(item.Center, y, item.Radius + halfExtent, item.BorderWidth, start + (halfAngle * 2f), vertices.Slice(written));
            return written;
        }

        public static int WriteLineBorder(in GroundOverlayItem item, Span<Vector3> vertices)
        {
            float length = item.Length > 0f ? item.Length : item.Radius;
            if (length <= 0f || item.BorderWidth <= 0f)
            {
                return 0;
            }

            RequireCapacity(vertices, 4 * LineFillVertices, nameof(WriteLineBorder));
            float y = item.Center.Y + BorderLiftMeters;
            Vector3 direction = new(MathF.Cos(item.Rotation), 0f, MathF.Sin(item.Rotation));
            Vector3 lateral = new(-MathF.Sin(item.Rotation), 0f, MathF.Cos(item.Rotation));
            float halfWidth = MathF.Max(0f, item.Width) * 0.5f;
            float halfExtent = item.BorderWidth * 0.5f;
            Vector3 a = WithY(item.Center, y);
            Vector3 b = a + (direction * length);

            int written = 0;
            written += WriteQuad(a + (lateral * (halfWidth - halfExtent)), b + (lateral * (halfWidth - halfExtent)), a + (lateral * (halfWidth + halfExtent)), b + (lateral * (halfWidth + halfExtent)), vertices.Slice(written));
            written += WriteQuad(a - (lateral * (halfWidth + halfExtent)), b - (lateral * (halfWidth + halfExtent)), a - (lateral * (halfWidth - halfExtent)), b - (lateral * (halfWidth - halfExtent)), vertices.Slice(written));
            written += WriteQuad(a - (direction * halfExtent) - (lateral * (halfWidth + halfExtent)), a + (direction * halfExtent) - (lateral * (halfWidth + halfExtent)), a - (direction * halfExtent) + (lateral * (halfWidth + halfExtent)), a + (direction * halfExtent) + (lateral * (halfWidth + halfExtent)), vertices.Slice(written));
            written += WriteQuad(b - (direction * halfExtent) - (lateral * (halfWidth + halfExtent)), b + (direction * halfExtent) - (lateral * (halfWidth + halfExtent)), b - (direction * halfExtent) + (lateral * (halfWidth + halfExtent)), b + (direction * halfExtent) + (lateral * (halfWidth + halfExtent)), vertices.Slice(written));
            return written;
        }

        /// <summary>沿路径生成宽 width 的条带（中心在 lateralOffset），Y 取路径点各自高度加 lift。</summary>
        public static int WriteRibbonStrip(ReadOnlySpan<Vector3> path, float lateralOffset, float width, float lift, Span<Vector3> vertices)
        {
            if (path.Length < 2 || width <= 0f)
            {
                return 0;
            }

            int required = (path.Length - 1) * 3 * 2;
            RequireCapacity(vertices, required, nameof(WriteRibbonStrip));
            float halfWidth = width * 0.5f;
            Span<Vector3> left = path.Length <= 32 ? stackalloc Vector3[path.Length] : new Vector3[path.Length];
            Span<Vector3> right = path.Length <= 32 ? stackalloc Vector3[path.Length] : new Vector3[path.Length];
            for (int i = 0; i < path.Length; i++)
            {
                Vector3 lateral = PathLateral(path, i);
                Vector3 lifted = new(path[i].X, path[i].Y + lift, path[i].Z);
                left[i] = lifted + (lateral * (lateralOffset + halfWidth));
                right[i] = lifted + (lateral * (lateralOffset - halfWidth));
            }

            int written = 0;
            for (int i = 0; i < path.Length - 1; i++)
            {
                vertices[written++] = left[i];
                vertices[written++] = left[i + 1];
                vertices[written++] = right[i];
                vertices[written++] = left[i + 1];
                vertices[written++] = right[i + 1];
                vertices[written++] = right[i];
            }

            return written;
        }

        private static int WriteArcBand(in Vector3 center, float y, float innerRadius, float outerRadius, float startAngle, float endAngle, int segments, Span<Vector3> vertices)
        {
            if (segments <= 0 || outerRadius <= 0f || outerRadius <= innerRadius)
            {
                return 0;
            }

            int required = segments * 3 * 2;
            RequireCapacity(vertices, required, nameof(WriteArcBand));
            float step = (endAngle - startAngle) / segments;
            int written = 0;
            for (int s = 0; s < segments; s++)
            {
                float a0 = startAngle + (s * step);
                float a1 = startAngle + ((s + 1) * step);
                Vector3 outer0 = RimPoint(center, y, outerRadius, a0);
                Vector3 outer1 = RimPoint(center, y, outerRadius, a1);
                Vector3 inner0 = innerRadius > 0f ? RimPoint(center, y, innerRadius, a0) : new(center.X, y, center.Z);
                Vector3 inner1 = innerRadius > 0f ? RimPoint(center, y, innerRadius, a1) : new(center.X, y, center.Z);
                vertices[written++] = outer1;
                vertices[written++] = outer0;
                vertices[written++] = inner0;
                vertices[written++] = outer1;
                vertices[written++] = inner0;
                vertices[written++] = inner1;
            }

            return written;
        }

        private static int WriteRadialEdgeBand(in Vector3 center, float y, float radius, float borderWidth, float angle, Span<Vector3> vertices)
        {
            Vector3 direction = new(MathF.Cos(angle), 0f, MathF.Sin(angle));
            Vector3 lateral = new(-MathF.Sin(angle), 0f, MathF.Cos(angle));
            Vector3 apex = new(center.X, y, center.Z);
            float halfExtent = borderWidth * 0.5f;
            return WriteQuad(
                apex - (lateral * halfExtent),
                apex + (direction * radius) - (lateral * halfExtent),
                apex + (lateral * halfExtent),
                apex + (direction * radius) + (lateral * halfExtent),
                vertices);
        }

        private static int WriteQuad(in Vector3 edgeA0, in Vector3 edgeA1, in Vector3 edgeB0, in Vector3 edgeB1, Span<Vector3> vertices)
        {
            RequireCapacity(vertices, LineFillVertices, nameof(WriteQuad));
            // 调用方只负责给出对边配对（A0-A1 / B0-B1），绕序在此统一：边方向 × 边间方向
            // 叉积 Y 分量非正时交换两条边，保证输出恒为 +Y 朝向（背面剔除开启时仍可见）。
            Vector3 along = edgeA1 - edgeA0;
            Vector3 across = edgeB0 - edgeA0;
            float facingY = (along.Z * across.X) - (along.X * across.Z);
            if (facingY > 0f)
            {
                vertices[0] = edgeA0;
                vertices[1] = edgeA1;
                vertices[2] = edgeB0;
                vertices[3] = edgeA1;
                vertices[4] = edgeB1;
                vertices[5] = edgeB0;
            }
            else
            {
                vertices[0] = edgeB0;
                vertices[1] = edgeB1;
                vertices[2] = edgeA0;
                vertices[3] = edgeB1;
                vertices[4] = edgeA1;
                vertices[5] = edgeA0;
            }

            return LineFillVertices;
        }

        private static Vector3 RimPoint(in Vector3 center, float y, float radius, float angle) =>
            new(center.X + (MathF.Cos(angle) * radius), y, center.Z + (MathF.Sin(angle) * radius));

        private static Vector3 WithY(in Vector3 value, float y) => new(value.X, y, value.Z);

        private static Vector3 PathLateral(ReadOnlySpan<Vector3> path, int index)
        {
            Vector3 current = path[index];
            Vector3 forward = index == path.Length - 1
                ? current - path[index - 1]
                : path[index + 1] - current;
            Vector2 lateral = new(-forward.Z, forward.X);
            float length = lateral.Length();
            if (length <= 0.0001f)
            {
                return Vector3.UnitZ;
            }

            lateral /= length;
            return new Vector3(lateral.X, 0f, lateral.Y);
        }

        private static void RequireCapacity(Span<Vector3> vertices, int required, string operation)
        {
            if (vertices.Length < required)
            {
                throw new InvalidOperationException($"{operation} requires {required} vertices, span has {vertices.Length}.");
            }
        }
    }
}
