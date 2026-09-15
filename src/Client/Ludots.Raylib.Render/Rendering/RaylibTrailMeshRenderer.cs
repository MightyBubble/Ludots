using System;
using System.Numerics;
using Ludots.Platform.Abstractions;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 刀光轨迹实心渲染：每条 trail 的样本条带经 TrailMeshGeometry 重建为三角列表，
    /// 逐顶点色（rlColor4ub + rlVertex3f）实现沿轨迹的平滑渐隐。
    /// 与 RaylibWorldOverlayRenderer 同款配对：绘制期间禁用背面剔除，画完恢复。
    /// </summary>
    public static class RaylibTrailMeshRenderer
    {
        private const int RlTriangles = 0x0004;

        public static void DrawTrailMeshes(TrailMeshBuffer trails)
        {
            if (trails == null)
            {
                throw new ArgumentNullException(nameof(trails));
            }

            Span<TrailMeshVertex> vertices = stackalloc TrailMeshVertex[TrailMeshGeometry.MaxStripVertices];
            Rl.rlDisableBackfaceCulling();
            Rl.BeginBlendMode(BlendMode.BLEND_ALPHA);
            try
            {
                for (int i = 0; i < trails.Count; i++)
                {
                    ReadOnlySpan<TrailMeshSample> samples = trails.GetSamples(i);
                    Vector4 head = trails.GetHeadColor(i);
                    Vector4 tail = trails.GetTailColor(i);
                    int count = TrailMeshGeometry.WriteTrailStrip(samples, in head, in tail, vertices);
                    if (count == 0)
                    {
                        continue;
                    }

                    DrawVertices(vertices[..count]);
                }
            }
            finally
            {
                Rl.EndBlendMode();
                Rl.rlEnableBackfaceCulling();
            }
        }

        private static void DrawVertices(ReadOnlySpan<TrailMeshVertex> vertices)
        {
            Rl.rlBegin(RlTriangles);
            for (int i = 0; i < vertices.Length; i++)
            {
                ref readonly TrailMeshVertex vertex = ref vertices[i];
                Rl.rlColor4ub(
                    RaylibColorUtil.Clamp01ToByte(vertex.Color.X),
                    RaylibColorUtil.Clamp01ToByte(vertex.Color.Y),
                    RaylibColorUtil.Clamp01ToByte(vertex.Color.Z),
                    RaylibColorUtil.Clamp01ToByte(vertex.Color.W));
                Rl.rlVertex3f(vertex.Position.X, vertex.Position.Y, vertex.Position.Z);
            }

            Rl.rlEnd();
        }
    }
}
