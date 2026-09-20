using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Components
{
    /// <summary>
    /// Stores viewport/spatial visibility and visual quality tier for a visual entity.
    /// CameraCullingSystem owns IsVisible; LOD is a quality tier and must not be used as a visibility gate.
    /// The hysteresis anchor records the logic-plane position (WorldPositionCm) at the entity's last full
    /// cull evaluation, bound to the CameraCullingSystem instance that produced it via the owner token,
    /// so co-owners of the same CullState never treat a foreign anchor as their own.
    /// </summary>
    public struct CullState
    {
        public bool IsVisible;
        public LODLevel LOD;
        public float DistanceToCameraSq;
        public float ScreenCoverage01;
        public int HysteresisOwnerToken;
        public float HysteresisAnchorXCm;
        public float HysteresisAnchorYCm;
    }
}
