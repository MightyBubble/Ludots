namespace Ludots.Core.Presentation.Components
{
    public enum LODLevel : byte
    {
        High = 0,
        Medium = 1,
        Low = 2,
        Culled = 255
    }

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
