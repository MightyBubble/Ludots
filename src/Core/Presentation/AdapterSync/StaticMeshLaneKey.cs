using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.AdapterSync
{
    /// <summary>
    /// Adapter-local batch/lane key for persistent static mesh ownership.
    /// Stable identity maps into one of these lanes plus a slot/generation.
    /// </summary>
    public readonly record struct StaticMeshLaneKey(
        VisualRenderPath RenderPath,
        int MeshAssetId,
        int MaterialId,
        VisualMobility Mobility)
    {
        public static bool Supports(VisualRenderPath renderPath)
        {
            return renderPath == VisualRenderPath.StaticMesh
                || renderPath == VisualRenderPath.InstancedStaticMesh
                || renderPath == VisualRenderPath.HierarchicalInstancedStaticMesh;
        }

        public static bool Supports(in PrimitiveDrawItem item) => Supports(item.RenderPath);

        public static StaticMeshLaneKey FromItem(in PrimitiveDrawItem item)
        {
            if (!Supports(item))
            {
                throw new ArgumentException(
                    $"RenderPath '{item.RenderPath}' is not part of the persistent static lane contract.",
                    nameof(item));
            }

            return new StaticMeshLaneKey(item.RenderPath, item.MeshAssetId, item.MaterialId, item.Mobility);
        }
    }
}
