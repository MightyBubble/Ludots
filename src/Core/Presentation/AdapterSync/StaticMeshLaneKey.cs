using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
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

        public static bool Supports(in PrimitiveDrawItem item)
        {
            return item.AssetKind != AssetKind.Surface && Supports(item.RenderPath);
        }

        public static void ValidateSurfaceContract(in PrimitiveDrawItem item)
        {
            if (item.RenderPath == VisualRenderPath.Surface && item.AssetKind != AssetKind.Surface)
            {
                throw new ArgumentException(
                    $"RenderPath '{item.RenderPath}' requires AssetKind '{AssetKind.Surface}', but got '{item.AssetKind}'.",
                    nameof(item));
            }

            if (item.AssetKind == AssetKind.Surface && item.RenderPath != VisualRenderPath.Surface)
            {
                throw new ArgumentException(
                    $"AssetKind '{AssetKind.Surface}' requires RenderPath '{VisualRenderPath.Surface}', but got '{item.RenderPath}'.",
                    nameof(item));
            }
        }

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
