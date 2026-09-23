using System;
using System.Numerics;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    public struct TrailMeshVertex
    {
        public Vector3 Position;
        public Vector4 Color;
    }

    /// <summary>
    /// 刀光轨迹的三角带重建（纯函数，无 GL 依赖，供渲染器与测试共用）。
    /// 输入样本约定 index 0 为最新（头部）；相邻样本对织成一个 quad 两个三角，
    /// 顶点色在 headColor/tailColor 之间按样本 age01 线性插值，实现沿轨迹渐隐。
    /// 顶点绕序不承诺朝向——绘制端与 RaylibWorldOverlayRenderer 同款禁用背面剔除。
    /// </summary>
    public static class TrailMeshGeometry
    {
        public const int MaxStripVertices = (TrailMeshBuffer.MaxSamplesPerTrail - 1) * 6;

        public static int WriteTrailStrip(
            ReadOnlySpan<TrailMeshSample> samples,
            in Vector4 headColor,
            in Vector4 tailColor,
            Span<TrailMeshVertex> vertices)
        {
            if (samples.Length < 2)
            {
                return 0;
            }

            int segments = samples.Length - 1;
            int required = segments * 6;
            if (vertices.Length < required)
            {
                throw new InvalidOperationException($"WriteTrailStrip requires {required} vertices, span has {vertices.Length}.");
            }

            int written = 0;
            for (int i = 0; i < segments; i++)
            {
                ref readonly TrailMeshSample s0 = ref samples[i];
                ref readonly TrailMeshSample s1 = ref samples[i + 1];
                Vector4 c0 = Vector4.Lerp(headColor, tailColor, s0.Age01);
                Vector4 c1 = Vector4.Lerp(headColor, tailColor, s1.Age01);
                vertices[written++] = new TrailMeshVertex { Position = s0.Base, Color = c0 };
                vertices[written++] = new TrailMeshVertex { Position = s1.Base, Color = c1 };
                vertices[written++] = new TrailMeshVertex { Position = s0.Tip, Color = c0 };
                vertices[written++] = new TrailMeshVertex { Position = s1.Base, Color = c1 };
                vertices[written++] = new TrailMeshVertex { Position = s1.Tip, Color = c1 };
                vertices[written++] = new TrailMeshVertex { Position = s0.Tip, Color = c0 };
            }

            return written;
        }
    }
}
