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

        public void ClearProjectionTargets()
        {
            _drawBuffer.Clear();
            _snapshotBuffer?.Clear();
            _proxyBuffer?.Clear();
            _skinnedBatchBuffer?.ClearProjection();
        }

        public void ApplyStaticInstanceDelta(ReadOnlySpan<PrimitiveDrawItem> changedItems, ReadOnlySpan<int> removedStableIds)
        {
            _snapshotBuffer?.ApplyStaticMeshDelta(changedItems, removedStableIds, visibleOnly: false);
            _drawBuffer.ApplyStaticMeshDelta(changedItems, removedStableIds, visibleOnly: true);
        }

        public void Emit(in PresentationVisualProxy proxy)
        {
            var primitive = new PrimitiveDrawItem
            {
                Payload = proxy.Payload,
                Mobility = proxy.Mobility,
                Flags = proxy.Flags,
                LOD = proxy.LOD,
            };

            if (proxy.Visibility == VisualVisibility.Visible &&
                _drawBuffer.Count >= _drawBuffer.Capacity)
            {
                throw _drawBuffer.CreateOverflowException(proxy.StableId, proxy.RenderPath);
            }

            if (_proxyBuffer != null && _proxyBuffer.Count >= _proxyBuffer.Capacity)
            {
                throw new InvalidOperationException(
                    $"Presentation visual proxy buffer overflowed while emitting stableId={proxy.StableId}, renderPath={proxy.RenderPath}.");
            }

            if (_snapshotBuffer != null && _snapshotBuffer.Count >= _snapshotBuffer.Capacity)
            {
                throw new InvalidOperationException(
                    $"Presentation visual snapshot buffer overflowed while emitting stableId={proxy.StableId}, renderPath={proxy.RenderPath}.");
            }

            if (proxy.RenderPath.IsSkinnedLane() &&
                _skinnedBatchBuffer != null &&
                _skinnedBatchBuffer.Count >= _skinnedBatchBuffer.Capacity)
            {
                throw new InvalidOperationException(
                    $"Skinned visual batch buffer overflowed while emitting stableId={proxy.StableId}, controllerId={proxy.Animator.GetControllerId()}.");
            }

            if (_proxyBuffer != null && !_proxyBuffer.TryAdd(proxy))
            {
                throw new InvalidOperationException(
                    $"Presentation visual proxy buffer overflowed while emitting stableId={proxy.StableId}, renderPath={proxy.RenderPath}.");
            }

            if (_snapshotBuffer != null && !_snapshotBuffer.TryAdd(primitive))
            {
                throw new InvalidOperationException(
                    $"Presentation visual snapshot buffer overflowed while emitting stableId={proxy.StableId}, renderPath={proxy.RenderPath}.");
            }

            if (proxy.RenderPath.IsSkinnedLane() &&
                _skinnedBatchBuffer != null &&
                !_skinnedBatchBuffer.TryAdd(new SkinnedVisualBatchItem
                {
                    Payload = proxy.Payload,
                    LOD = proxy.LOD,
                }))
            {
                throw new InvalidOperationException(
                    $"Skinned visual batch buffer overflowed while emitting stableId={proxy.StableId}, controllerId={proxy.Animator.GetControllerId()}.");
            }

            if (proxy.Visibility == VisualVisibility.Visible)
            {
                _drawBuffer.Add(primitive);
            }
        }
    }
}
