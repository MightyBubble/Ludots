namespace Ludots.Core.Presentation.Components
{
    /// <summary>
    /// Pure data definition of what visual asset this entity represents.
    /// </summary>
    public struct VisualModel
    {
        public int MeshId;     // ID for Mesh/Prefab resource
        public int MaterialId; // ID for Material variant
        public float BaseScale;
        
        // Future expansion: AnimationSetId, SkeletonId, etc.
    }
}
