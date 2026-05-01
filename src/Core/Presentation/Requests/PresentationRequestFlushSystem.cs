using System;
using System.Diagnostics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Requests
{
    public sealed class PresentationRequestFlushSystem : BaseSystem<World, float>
    {
        private readonly PresentationRequestBuffer _requests;
        private readonly PrefabRegistry _prefabs;
        private readonly StableDrawCache _stableDrawCache;
        private readonly PresentationVisualProxyEmitter _visualProxyEmitter;
        private readonly PrimitiveDrawBuffer _snapshotBuffer;
        private readonly GroundOverlayBuffer _groundOverlays;
        private readonly WorldHudBatchBuffer _worldHud;
        private readonly RoadSplineBuffer _roadSplines;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private int _lastProjectedRevision = -1;

        public PresentationRequestFlushSystem(
            World world,
            PresentationRequestBuffer requests,
            PrefabRegistry prefabs,
            MeshAssetRegistry meshes,
            StableDrawCache stableDrawCache,
            PrimitiveDrawBuffer primitives,
            GroundOverlayBuffer groundOverlays,
            WorldHudBatchBuffer worldHud,
            RoadSplineBuffer roadSplines,
            PrimitiveDrawBuffer snapshotBuffer,
            PresentationVisualProxyBuffer proxyBuffer,
            SkinnedVisualBatchBuffer skinnedBatchBuffer,
            PresentationTimingDiagnostics? timingDiagnostics = null)
            : base(world)
        {
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _prefabs = prefabs ?? throw new ArgumentNullException(nameof(prefabs));
            _ = meshes ?? throw new ArgumentNullException(nameof(meshes));
            _stableDrawCache = stableDrawCache ?? throw new ArgumentNullException(nameof(stableDrawCache));
            _timingDiagnostics = timingDiagnostics;
            _snapshotBuffer = snapshotBuffer ?? throw new ArgumentNullException(nameof(snapshotBuffer));
            _visualProxyEmitter = new PresentationVisualProxyEmitter(
                primitives ?? throw new ArgumentNullException(nameof(primitives)),
                _snapshotBuffer,
                proxyBuffer ?? throw new ArgumentNullException(nameof(proxyBuffer)),
                skinnedBatchBuffer ?? throw new ArgumentNullException(nameof(skinnedBatchBuffer)));
            _groundOverlays = groundOverlays ?? throw new ArgumentNullException(nameof(groundOverlays));
            _worldHud = worldHud ?? throw new ArgumentNullException(nameof(worldHud));
            _roadSplines = roadSplines ?? throw new ArgumentNullException(nameof(roadSplines));
        }

        public override void Update(in float dt)
        {
            long start = _timingDiagnostics != null ? Stopwatch.GetTimestamp() : 0L;
            _stableDrawCache.BeginFrame();
            ReadOnlySpan<PresentationRequest> span = _requests.GetSpan();
            bool hasTransientVisualProxy = HasTransientVisualProxy(span);
            bool projectionTargetsCleared = false;
            if (hasTransientVisualProxy)
            {
                _visualProxyEmitter.ClearProjectionTargets();
                projectionTargetsCleared = true;
            }

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationRequest request = ref span[i];
                switch (request.Kind)
                {
                    case PresentationRequestKind.VisualProxy:
                        EmitVisualProxy(request.VisualProxy);
                        break;

                    case PresentationRequestKind.Prefab:
                        EmitPrefab(in request);
                        break;

                    case PresentationRequestKind.GroundOverlay:
                        if (!_groundOverlays.Upsert(request.GroundOverlay))
                        {
                            throw new InvalidOperationException("GroundOverlayBuffer overflowed while flushing PresentationRequest.");
                        }

                        break;

                    case PresentationRequestKind.WorldHud:
                        if (!_worldHud.TryAdd(request.WorldHud))
                        {
                            throw new InvalidOperationException(
                                $"WorldHudBatchBuffer overflowed while flushing PresentationRequest stableId={request.WorldHud.StableId}.");
                        }

                        break;

                    case PresentationRequestKind.RoadSpline:
                        EmitRoadSpline(in request.RoadSpline);
                        break;

                    case PresentationRequestKind.SurfaceSource:
                        // SurfaceSource requests are consumed by the dedicated performer-surface runtime
                        // before the legacy request flush reaches adapter-facing buffers.
                        break;

                    case PresentationRequestKind.RemoveSurfaceSource:
                        // SurfaceSource removals are consumed by SurfaceSourceFlushSystem.
                        break;

                    case PresentationRequestKind.ClearTransientVisualProjection:
                        // Projection targets are cleared once before request replay when this marker is present.
                        break;

                    case PresentationRequestKind.RemoveGroundOverlay:
                        _groundOverlays.Remove(request.StableId);
                        break;

                    case PresentationRequestKind.RemoveWorldHud:
                        _worldHud.Remove(request.StableId);
                        break;

                    case PresentationRequestKind.RemoveRoadSpline:
                        _roadSplines.Remove(request.StableId);
                        break;

                    default:
                        throw new InvalidOperationException($"Unknown PresentationRequestKind '{request.Kind}'.");
                }
            }

            int contentRevision = _stableDrawCache.ContentRevision;
            if (projectionTargetsCleared || _lastProjectedRevision != contentRevision)
            {
                if (!projectionTargetsCleared)
                {
                    _visualProxyEmitter.ClearProjectionTargets();
                }

                _stableDrawCache.Project(_visualProxyEmitter, evictUntouched: false);
                _lastProjectedRevision = contentRevision;
                _snapshotBuffer.SetRevision(contentRevision);
                _snapshotBuffer.SetStaticMeshGeometryRevision(_stableDrawCache.StaticMeshGeometryRevision);
                _snapshotBuffer.SetStaticMeshDeltas(
                    _stableDrawCache.StaticMeshDeltaBaseRevision,
                    _stableDrawCache.StaticMeshDeltaItems,
                    _stableDrawCache.StaticMeshRemovedStableIds);
                _stableDrawCache.ClearStaticMeshDeltas();
            }
            _requests.Clear();

            if (_timingDiagnostics != null)
            {
                _timingDiagnostics.ObservePresentationRequestFlush((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
            }
        }

        private void EmitPrefab(in PresentationRequest request)
        {
            if (!_prefabs.TryGet(request.PrefabId, out PrefabDefinition prefab))
            {
                throw new InvalidOperationException($"Presentation prefab request references unknown prefabId={request.PrefabId}.");
            }

            EmitVisualProxy(new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Performer,
                MeshAssetId = prefab.MeshAssetId,
                Position = request.Position,
                Rotation = request.Rotation,
                Scale = request.Scale * prefab.BaseScale,
                Color = request.Color,
                StableId = request.StableId,
                RenderPath = VisualRenderPath.StaticMesh,
                Mobility = VisualMobility.Movable,
                Flags = VisualRuntimeFlags.Visible,
                Visibility = request.LOD == LODLevel.Culled ? VisualVisibility.Culled : VisualVisibility.Visible,
                LOD = request.LOD,
            });
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

        private static bool HasTransientVisualProxy(ReadOnlySpan<PresentationRequest> requests)
        {
            for (int i = 0; i < requests.Length; i++)
            {
                ref readonly PresentationRequest request = ref requests[i];
                if (request.Kind == PresentationRequestKind.ClearTransientVisualProjection ||
                    (request.Kind == PresentationRequestKind.VisualProxy &&
                     IsTransientVisualProxy(in request.VisualProxy)))
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

        private void EmitRoadSpline(in RoadSplineRequest spline)
        {
            if (!_roadSplines.TryAdd(
                    spline.StableId,
                    spline.P0,
                    spline.P1,
                    spline.P2,
                    spline.P3,
                    spline.Width,
                    spline.FillColor,
                    spline.BorderColor,
                    spline.BorderWidth,
                    spline.Style))
            {
                throw new InvalidOperationException(
                    $"RoadSplineBuffer overflowed while flushing PresentationRequest stableId={spline.StableId}.");
            }
        }
    }
}
