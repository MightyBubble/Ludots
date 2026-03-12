namespace Ludots.Core.Presentation.Components
{
    public enum VisualRenderPath : byte
    {
        None = 0,
        StaticMesh = 1,
        InstancedStaticMesh = 2,
        HierarchicalInstancedStaticMesh = 3,
        SkinnedMesh = 4,
        GpuSkinnedInstance = 5,
    }
}
