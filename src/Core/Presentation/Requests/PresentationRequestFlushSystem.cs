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
        private readonly MeshAssetRegistry _meshes;
        private readonly StableDrawCache _stableDrawCache;
        private readonly PresentationVisualProxyEmitter _visualProxyEmitter;
        private readonly GroundOverlayBuffer _groundOverlays;
        private readonly WorldHudBatchBuffer _worldHud;
        private readonly RoadSplineBuffer _roadSplines;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;

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
            _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
            _stableDrawCache = stableDrawCache ?? throw new ArgumentNullException(nameof(stableDrawCache));
            _timingDiagnostics = timingDiagnostics;
            _visualProxyEmitter = new PresentationVisualProxyEmitter(
                primitives ?? throw new ArgumentNullException(nameof(primitives)),
                snapshotBuffer ?? throw new ArgumentNullException(nameof(snapshotBuffer)),
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
                        if (!_groundOverlays.TryAdd(request.GroundOverlay))
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

                    default:
                        throw new InvalidOperationException($"Unknown PresentationRequestKind '{request.Kind}'.");
                }
            }

            _visualProxyEmitter.ClearProjectionTargets();
            _stableDrawCache.Project(_visualProxyEmitter, evictUntouched: true);

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
            _stableDrawCache.Upsert(proxy);
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
