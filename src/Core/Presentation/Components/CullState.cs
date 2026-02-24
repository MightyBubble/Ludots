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
    /// Stores the culling and LOD state of a visual entity.
    /// Updated by CameraCullingSystem.
    /// </summary>
    public struct CullState
    {
        public bool IsVisible;
        public LODLevel LOD;
        public float DistanceToCameraSq;
    }
}
