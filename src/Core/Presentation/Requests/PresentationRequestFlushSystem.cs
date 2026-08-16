using System;
using System.Diagnostics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Requests
{
    public sealed class PresentationRequestFlushSystem : BaseSystem<World, float>
    {
        private readonly PresentationRequestBuffer _requests;
        private readonly StableDrawCache _stableDrawCache;
        private readonly PresentationTargetGeneration? _targetGeneration;
        private readonly PresentationVisualProxyEmitter _visualProxyEmitter;
        private readonly PrimitiveDrawBuffer _snapshotBuffer;
        private readonly GroundOverlayBuffer _groundOverlays;
        private readonly WorldHudBatchBuffer _worldHud;
        private readonly SplineRibbonBuffer _splineRibbons;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private int _lastProjectedRevision = -1;
        private int _lastProjectedNonStaticRevision = -1;
        private int _lastProjectedTargetGeneration = -1;
        private bool _hadTransientVisualProjection;

        public PresentationRequestFlushSystem(
            World world,
            PresentationRequestBuffer requests,
            MeshAssetRegistry meshes,
            StableDrawCache stableDrawCache,
            PrimitiveDrawBuffer primitives,
            GroundOverlayBuffer groundOverlays,
            WorldHudBatchBuffer worldHud,
            SplineRibbonBuffer splineRibbons,
            PrimitiveDrawBuffer snapshotBuffer,
            PresentationVisualProxyBuffer proxyBuffer,
            SkinnedVisualBatchBuffer skinnedBatchBuffer,
            PresentationTimingDiagnostics? timingDiagnostics = null,
            PresentationTargetGeneration? targetGeneration = null)
            : base(world)
        {
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _ = meshes ?? throw new ArgumentNullException(nameof(meshes));
            _stableDrawCache = stableDrawCache ?? throw new ArgumentNullException(nameof(stableDrawCache));
            _targetGeneration = targetGeneration;
            _timingDiagnostics = timingDiagnostics;
            _snapshotBuffer = snapshotBuffer ?? throw new ArgumentNullException(nameof(snapshotBuffer));
            _visualProxyEmitter = new PresentationVisualProxyEmitter(
                primitives ?? throw new ArgumentNullException(nameof(primitives)),
                _snapshotBuffer,
                proxyBuffer ?? throw new ArgumentNullException(nameof(proxyBuffer)),
                skinnedBatchBuffer ?? throw new ArgumentNullException(nameof(skinnedBatchBuffer)));
            _groundOverlays = groundOverlays ?? throw new ArgumentNullException(nameof(groundOverlays));
            _worldHud = worldHud ?? throw new ArgumentNullException(nameof(worldHud));
            _splineRibbons = splineRibbons ?? throw new ArgumentNullException(nameof(splineRibbons));
        }

        public override void Update(in float dt)
        {
            long start = _timingDiagnostics != null ? Stopwatch.GetTimestamp() : 0L;
            _stableDrawCache.BeginFrame();
            bool hasTransientVisualProxy = HasTransientVisualProxy(_requests);
            bool projectionTargetsCleared = false;
            if (hasTransientVisualProxy || _hadTransientVisualProjection)
            {
                _visualProxyEmitter.ClearProjectionTargets();
                projectionTargetsCleared = true;
            }
            _hadTransientVisualProjection = hasTransientVisualProxy;

            ReadOnlySpan<PresentationRequestOp> ops = _requests.Ops;
            for (int i = 0; i < ops.Length; i++)
            {
                PresentationRequestOp op = ops[i];
                switch (op.Channel)
                {
                    case PresentationRequestChannel.VisualProxy:
                        EmitVisualProxy(in _requests.VisualProxyAt(op.Slot).VisualProxy);
                        break;

                    case PresentationRequestChannel.GroundOverlay:
                        if (!_groundOverlays.Upsert(_requests.GroundOverlayAt(op.Slot).Item))
                        {
                            throw new InvalidOperationException("GroundOverlayBuffer overflowed while flushing PresentationRequest.");
                        }

                        break;

                    case PresentationRequestChannel.WorldHud:
                    {
                        ref readonly WorldHudChannelItem hud = ref _requests.WorldHudAt(op.Slot);
                        if (!_worldHud.TryAdd(hud.Item))
                        {
                            throw new InvalidOperationException(
                                $"WorldHudBatchBuffer overflowed while flushing PresentationRequest stableId={hud.Item.StableId}.");
                        }

                        break;
                    }

                    case PresentationRequestChannel.SplineRibbon:
                        EmitSplineRibbon(in _requests.SplineRibbonAt(op.Slot).Item);
                        break;

                    case PresentationRequestChannel.SurfaceSource:
                        break;

                    case PresentationRequestChannel.ClearTransient:
                        break;

                    case PresentationRequestChannel.Removal:
                        FlushRemoval(in _requests.RemovalAt(op.Slot));
                        break;

                    default:
                        throw new InvalidOperationException($"Unknown PresentationRequestChannel '{op.Channel}'.");
                }
            }

            int contentRevision = _stableDrawCache.ContentRevision;
            int nonStaticRevision = _stableDrawCache.NonStaticContentRevision;
            int targetGeneration = _targetGeneration?.Generation ?? 0;
            bool needsFullProjection = projectionTargetsCleared ||
                _lastProjectedTargetGeneration != targetGeneration ||
                _lastProjectedNonStaticRevision != nonStaticRevision;
            if (needsFullProjection)
            {
                if (!projectionTargetsCleared)
                {
                    _visualProxyEmitter.ClearProjectionTargets();
                }

                _stableDrawCache.Project(_visualProxyEmitter, evictUntouched: false);
                PublishStaticProjectionState(contentRevision, nonStaticRevision, targetGeneration);
            }
            else if (_lastProjectedRevision != contentRevision)
            {
                _visualProxyEmitter.ApplyStaticInstanceDelta(
                    _stableDrawCache.StaticMeshDeltaItems,
                    _stableDrawCache.StaticMeshRemovedStableIds);
                PublishStaticProjectionState(contentRevision, nonStaticRevision, targetGeneration);
            }
            _requests.Clear();

            if (_timingDiagnostics != null)
            {
                _timingDiagnostics.ObservePresentationRequestFlush((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
            }
        }

        private void PublishStaticProjectionState(int contentRevision, int nonStaticRevision, int targetGeneration)
        {
            _lastProjectedRevision = contentRevision;
            _lastProjectedNonStaticRevision = nonStaticRevision;
            _lastProjectedTargetGeneration = targetGeneration;
            _snapshotBuffer.SetRevision(contentRevision);
            _snapshotBuffer.SetProjectionGeneration(targetGeneration);
            _snapshotBuffer.SetStaticMeshGeometryRevision(_stableDrawCache.StaticMeshGeometryRevision);
            _snapshotBuffer.SetStaticMeshDeltas(
                _stableDrawCache.StaticMeshDeltaBaseRevision,
                _stableDrawCache.StaticMeshDeltaItems,
                _stableDrawCache.StaticMeshRemovedStableIds);
            _stableDrawCache.ClearStaticMeshDeltas();
        }

        private void FlushRemoval(in PresentationRemovalRequest removal)
        {
            switch (removal.Kind)
            {
                case PresentationRequestKind.RemoveGroundOverlay:
                    _groundOverlays.Remove(removal.StableId);
                    break;
                case PresentationRequestKind.RemoveWorldHud:
                    _worldHud.Remove(removal.StableId);
                    break;
                case PresentationRequestKind.RemoveSplineRibbon:
                    _splineRibbons.Remove(removal.StableId);
                    break;
                case PresentationRequestKind.RemoveSurfaceSource:
                    break;
                default:
                    throw new InvalidOperationException($"Unknown PresentationRequestKind '{removal.Kind}' on removal channel.");
            }
        }

        private void EmitVisualProxy(in PresentationVisualProxy proxy)
        {
            if (IsTransientVisualProxy(in proxy))
            {
                _visualProxyEmitter.Emit(proxy);
                return;
            }

            _stableDrawCache.Upsert(proxy);
        }

        private static bool HasTransientVisualProxy(PresentationRequestBuffer requests)
        {
            if (requests.ClearTransientCount > 0)
            {
                return true;
            }

            ReadOnlySpan<VisualProxyChannelItem> visualProxies = requests.VisualProxies;
            for (int i = 0; i < visualProxies.Length; i++)
            {
                if (IsTransientVisualProxy(in visualProxies[i].VisualProxy))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTransientVisualProxy(in PresentationVisualProxy proxy)
        {
            return proxy.Mobility == VisualMobility.Movable ||
                   proxy.RenderPath.IsSkinnedLane();
        }

        private void EmitSplineRibbon(in SplineRibbonRequest spline)
        {
            if (!_splineRibbons.TryAdd(
                    spline.StableId,
                    spline.P0,
                    spline.P1,
                    spline.P2,
                    spline.P3,
                    spline.Width,
                    spline.FillColor,
                    spline.BorderColor,
                    spline.BorderWidth))
            {
                throw new InvalidOperationException(
                    $"SplineRibbonBuffer overflowed while flushing PresentationRequest stableId={spline.StableId}.");
            }
        }
    }
}
