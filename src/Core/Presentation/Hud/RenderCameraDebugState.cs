using System.Numerics;

namespace Ludots.Core.Presentation.Hud
{
    /// <summary>
    /// Debug overrides for the render camera.
    /// When Enabled, the adapter applies PullBack/Offset to the camera output,
    /// allowing a detached debug view without affecting gameplay logic.
    /// </summary>
    public sealed class RenderCameraDebugState
    {
        public bool Enabled { get; set; }
        public float PullBackMeters { get; set; }
        public Vector3 PositionOffsetMeters { get; set; }
        public bool DrawLogicalCullingDebug { get; set; }
        public bool DrawAcceptanceProbes { get; set; }
        public float AcceptanceScaleMultiplier { get; set; } = 1f;
    }
}
