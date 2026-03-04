using System.Numerics;

namespace Ludots.Core.Presentation.Camera
{
    /// <summary>
    /// Debug-only state that decouples render camera from logical camera.
    /// Logical camera (gameplay/culling) stays unchanged.
    /// </summary>
    public sealed class RenderCameraDebugState
    {
        /// <summary>
        /// When true, render camera receives debug offsets.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Additional pull-back distance along render camera backward direction (meters).
        /// </summary>
        public float PullBackMeters { get; set; }

        /// <summary>
        /// Extra world-space translation applied to render camera position (meters).
        /// </summary>
        public Vector3 PositionOffsetMeters { get; set; } = Vector3.Zero;

        /// <summary>
        /// Extra world-space translation applied to render camera target (meters).
        /// </summary>
        public Vector3 TargetOffsetMeters { get; set; } = Vector3.Zero;

        /// <summary>
        /// Controls whether logical culling/LOD debug visualization is shown.
        /// </summary>
        public bool DrawLogicalCullingDebug { get; set; }

        /// <summary>
        /// Global render-only scale multiplier for acceptance checks.
        /// </summary>
        public float AcceptanceScaleMultiplier { get; set; } = 1f;

        /// <summary>
        /// Draw position probes for emitted draw items.
        /// Helps verify "entity exists but model not visible" cases.
        /// </summary>
        public bool DrawAcceptanceProbes { get; set; }

        public void Reset()
        {
            Enabled = false;
            PullBackMeters = 0f;
            PositionOffsetMeters = Vector3.Zero;
            TargetOffsetMeters = Vector3.Zero;
            DrawLogicalCullingDebug = false;
            AcceptanceScaleMultiplier = 1f;
            DrawAcceptanceProbes = false;
        }
    }
}
