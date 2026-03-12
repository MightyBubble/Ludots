using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Assets
{
    public struct VisualTemplateDefinition
    {
        public int TemplateId;
        public int MeshAssetId;
        public int MaterialId;
        public int AnimatorControllerId;
        public float BaseScale;
        public VisualRenderPath RenderPath;
        public VisualMobility Mobility;
        public bool VisibleByDefault;

        public readonly VisualRuntimeState ToRuntimeState(bool? visibleOverride = null)
        {
            bool visible = visibleOverride ?? VisibleByDefault;
            return VisualRuntimeState.Create(
                MeshAssetId,
                MaterialId,
                BaseScale,
                RenderPath,
                Mobility,
                visible,
                AnimatorControllerId);
        }
    }
}
