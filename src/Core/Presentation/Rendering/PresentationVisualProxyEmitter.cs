using System;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Rendering
{
    public sealed class PresentationVisualProxyEmitter
    {
        private readonly PresentationVisualProxyBuffer? _proxyBuffer;
        private readonly PrimitiveDrawBuffer _drawBuffer;
        private readonly PrimitiveDrawBuffer? _snapshotBuffer;
        private readonly SkinnedVisualBatchBuffer? _skinnedBatchBuffer;

        public PresentationVisualProxyEmitter(
            PrimitiveDrawBuffer drawBuffer,
            PrimitiveDrawBuffer? snapshotBuffer = null,
            PresentationVisualProxyBuffer? proxyBuffer = null,
            SkinnedVisualBatchBuffer? skinnedBatchBuffer = null)
        {
            _drawBuffer = drawBuffer ?? throw new ArgumentNullException(nameof(drawBuffer));
            _snapshotBuffer = snapshotBuffer;
            _proxyBuffer = proxyBuffer;
            _skinnedBatchBuffer = skinnedBatchBuffer;
        }

        public void Emit(in PresentationVisualProxy proxy)
        {
            if (_proxyBuffer != null && !_proxyBuffer.TryAdd(proxy))
            {
                throw new InvalidOperationException(
                    $"Presentation visual proxy buffer overflowed while emitting stableId={proxy.StableId}, renderPath={proxy.RenderPath}.");
            }

            var primitive = new PrimitiveDrawItem
            {
                AssetKind = proxy.AssetKind,
                MeshAssetId = proxy.MeshAssetId,
                Position = proxy.Position,
                Rotation = proxy.Rotation,
                Scale = proxy.Scale,
                Color = proxy.Color,
                StableId = proxy.StableId,
                MaterialId = proxy.MaterialId,
                TemplateId = proxy.TemplateId,
                AnimationProfileId = proxy.AnimationProfileId,
                RenderPath = proxy.RenderPath,
                Mobility = proxy.Mobility,
                Flags = proxy.Flags,
                Animator = proxy.Animator,
                AnimationOverlay = proxy.AnimationOverlay,
                Visibility = proxy.Visibility,
                SurfaceLayerKey = proxy.SurfaceLayerKey,
                SortId = proxy.SortId,
                MaterialCustomData = proxy.MaterialCustomData,
            };

            if (_snapshotBuffer != null && !_snapshotBuffer.TryAdd(primitive))
            {
                throw new InvalidOperationException(
                    $"Presentation visual snapshot buffer overflowed while emitting stableId={proxy.StableId}, renderPath={proxy.RenderPath}.");
            }

            if (proxy.RenderPath.IsSkinnedLane() &&
                _skinnedBatchBuffer != null &&
                !_skinnedBatchBuffer.TryAdd(new SkinnedVisualBatchItem
                {
                    AssetKind = proxy.AssetKind,
                    StableId = proxy.StableId,
                    MeshAssetId = proxy.MeshAssetId,
                    MaterialId = proxy.MaterialId,
                    TemplateId = proxy.TemplateId,
                    AnimationProfileId = proxy.AnimationProfileId,
                    RenderPath = proxy.RenderPath,
                    Position = proxy.Position,
                    Rotation = proxy.Rotation,
                    Scale = proxy.Scale,
                    Color = proxy.Color,
                    Animator = proxy.Animator,
                    AnimationOverlay = proxy.AnimationOverlay,
                    Visibility = proxy.Visibility,
                    MaterialCustomData = proxy.MaterialCustomData,
                }))
            {
                throw new InvalidOperationException(
                    $"Skinned visual batch buffer overflowed while emitting stableId={proxy.StableId}, controllerId={proxy.Animator.GetControllerId()}.");
            }

            if (proxy.Visibility == VisualVisibility.Visible)
            {
                if (!_drawBuffer.TryAdd(primitive))
                {
                    throw new InvalidOperationException(
                        $"Presentation primitive draw buffer overflowed while emitting visible stableId={proxy.StableId}, renderPath={proxy.RenderPath}.");
                }
            }
        }
    }
}
