namespace Ludots.Core.Presentation.Components
{
    public struct VisualRuntimeState
    {
        public int MeshAssetId;
        public int MaterialId;
        public int AnimatorControllerId;
        public float BaseScale;
        public VisualRenderPath RenderPath;
        public VisualMobility Mobility;
        public VisualRuntimeFlags Flags;

        public readonly bool IsVisibleRequested => (Flags & VisualRuntimeFlags.Visible) != 0;
        public readonly bool HasAnimator => (Flags & VisualRuntimeFlags.HasAnimator) != 0;
        public readonly bool HasRenderableAsset => MeshAssetId > 0 && RenderPath != VisualRenderPath.None;
        public readonly bool ShouldEmit => HasRenderableAsset && IsVisibleRequested;

        public readonly VisualVisibility ResolveVisibility(bool cullVisible)
        {
            if (!IsVisibleRequested)
                return VisualVisibility.Hidden;

            return cullVisible
                ? VisualVisibility.Visible
                : VisualVisibility.Culled;
        }

        public static VisualRuntimeState Create(
            int meshAssetId,
            int materialId,
            float baseScale,
            VisualRenderPath renderPath,
            VisualMobility mobility = VisualMobility.Movable,
            bool visible = true,
            int animatorControllerId = 0)
        {
            PresentationRenderContract.ValidateTemplate(nameof(VisualRuntimeState), renderPath, animatorControllerId);

            var flags = visible ? VisualRuntimeFlags.Visible : VisualRuntimeFlags.None;
            if (animatorControllerId > 0)
                flags |= VisualRuntimeFlags.HasAnimator;

            return new VisualRuntimeState
            {
                MeshAssetId = meshAssetId,
                MaterialId = materialId,
                AnimatorControllerId = animatorControllerId,
                BaseScale = baseScale <= 0f ? 1f : baseScale,
                RenderPath = renderPath,
                Mobility = mobility,
                Flags = flags,
            };
        }
    }
}
