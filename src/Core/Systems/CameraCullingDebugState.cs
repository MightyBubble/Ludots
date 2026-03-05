using System.Numerics;

namespace Ludots.Core.Systems
{
    /// <summary>
    /// Snapshot of the current culling state for debug visualization.
    /// Written by CameraCullingSystem each frame; read by presentation-layer debug systems.
    /// </summary>
    public sealed class CameraCullingDebugState
    {
        public float MinX { get; set; }
        public float MaxX { get; set; }
        public float MinY { get; set; }
        public float MaxY { get; set; }

        public float HighLodDist { get; set; }
        public float MediumLodDist { get; set; }
        public float LowLodDist { get; set; }

        public Vector2 CameraTargetCm { get; set; }

        public int VisibleEntityCount { get; set; }
        public int CulledEntityCount { get; set; }
    }
}
