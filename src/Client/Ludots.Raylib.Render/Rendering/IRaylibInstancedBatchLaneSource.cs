using System.Numerics;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// Resident typed instanced-batch lanes injected by the adapter host, following the
    /// IRaylibReceiverMeshProjector precedent: the renderer stays blind to Core typed buffers
    /// and only draws what the source declares resident. Residency, declared capacity and
    /// chunk progress are Core-owned decisions mirrored by the source; the renderer must not
    /// re-derive or extend them.
    /// </summary>
    public interface IRaylibInstancedBatchLaneSource
    {
        int ResidentLaneCount { get; }

        RaylibInstancedBatchLane GetResidentLane(int index);
    }

    /// <summary>
    /// Read-only view of one resident instanced batch lane. Matrices are world-space
    /// (visual meters, Y up) and only the first <see cref="Count"/> entries are valid;
    /// <see cref="Revision"/> changes exactly when new chunk data arrives so renderers can
    /// cache their native matrix copies between frames.
    /// </summary>
    public readonly struct RaylibInstancedBatchLane
    {
        public RaylibInstancedBatchLane(
            int laneId,
            int meshAssetId,
            int materialAssetId,
            VisualRenderPath renderPath,
            Matrix4x4[] matrices,
            int count,
            int revision,
            bool visible)
        {
            LaneId = laneId;
            MeshAssetId = meshAssetId;
            MaterialAssetId = materialAssetId;
            RenderPath = renderPath;
            Matrices = matrices;
            Count = count;
            Revision = revision;
            Visible = visible;
        }

        public int LaneId { get; }
        public int MeshAssetId { get; }
        public int MaterialAssetId { get; }
        public VisualRenderPath RenderPath { get; }
        public Matrix4x4[] Matrices { get; }
        public int Count { get; }
        public int Revision { get; }
        public bool Visible { get; }
    }
}
