using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Components
{
    /// <summary>
    /// Stores viewport/spatial visibility and visual quality tier for a visual entity.
    /// CameraCullingSystem owns IsVisible; LOD is a quality tier and must not be used as a visibility gate.
    /// </summary>
    public struct CullState
    {
        public bool IsVisible;
        public LODLevel LOD;
        public float DistanceToCameraSq;
        public float ScreenCoverage01;
    }
}
