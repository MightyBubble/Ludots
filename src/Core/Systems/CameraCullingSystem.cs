using System;
using System.Numerics;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arch.Buffer;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Map.Hex;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;

namespace Ludots.Core.Systems
{
    public class CameraCullingSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _presentationStateQuery = new QueryDescription()
            .WithAll<PresentationFrameState>();
        private static readonly QueryDescription _visualBoundsLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLocalBounds, PresentationLodProfile>()
            .WithNone<PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _payloadVisualBoundsLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLocalBounds, PresentationLodProfile, PresentationOwnerHasPerformerPayload>()
            .WithNone<PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _spatialExcludedVisualBoundsLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLocalBounds, PresentationLodProfile, SpatialPartitionExcluded>()
            .WithNone<PresentationStaticTransform>();
        private static readonly QueryDescription _payloadSpatialExcludedVisualBoundsLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLocalBounds, PresentationLodProfile, PresentationOwnerHasPerformerPayload, SpatialPartitionExcluded>()
            .WithNone<PresentationStaticTransform>();
        private static readonly QueryDescription _visualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLocalBounds>()
            .WithNone<PresentationLodProfile, PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _payloadVisualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLocalBounds, PresentationOwnerHasPerformerPayload>()
            .WithNone<PresentationLodProfile, PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _spatialExcludedVisualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLocalBounds, SpatialPartitionExcluded>()
            .WithNone<PresentationLodProfile, PresentationStaticTransform>();
        private static readonly QueryDescription _payloadSpatialExcludedVisualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLocalBounds, PresentationOwnerHasPerformerPayload, SpatialPartitionExcluded>()
            .WithNone<PresentationLodProfile, PresentationStaticTransform>();
        private static readonly QueryDescription _visualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLodProfile>()
            .WithNone<PresentationLocalBounds, PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _payloadVisualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLodProfile, PresentationOwnerHasPerformerPayload>()
            .WithNone<PresentationLocalBounds, PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _spatialExcludedVisualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLodProfile, SpatialPartitionExcluded>()
            .WithNone<PresentationLocalBounds, PresentationStaticTransform>();
        private static readonly QueryDescription _payloadSpatialExcludedVisualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLodProfile, PresentationOwnerHasPerformerPayload, SpatialPartitionExcluded>()
            .WithNone<PresentationLocalBounds, PresentationStaticTransform>();
        private static readonly QueryDescription _visualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile, PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _payloadVisualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationOwnerHasPerformerPayload>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile, PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _spatialExcludedVisualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, SpatialPartitionExcluded>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile, PresentationStaticTransform>();
        private static readonly QueryDescription _payloadSpatialExcludedVisualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationOwnerHasPerformerPayload, SpatialPartitionExcluded>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile, PresentationStaticTransform>();
        private static readonly QueryDescription _noVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState>()
            .WithNone<VisualTransform, PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _payloadNoVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationOwnerHasPerformerPayload>()
            .WithNone<VisualTransform, PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _spatialExcludedNoVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, SpatialPartitionExcluded>()
            .WithNone<VisualTransform, PresentationStaticTransform>();
        private static readonly QueryDescription _payloadSpatialExcludedNoVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationOwnerHasPerformerPayload, SpatialPartitionExcluded>()
            .WithNone<VisualTransform, PresentationStaticTransform>();
        private static readonly QueryDescription _staticVisualBoundsLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform, PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _payloadStaticVisualBoundsLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform, PresentationLocalBounds, PresentationLodProfile, PresentationOwnerHasPerformerPayload>();
        private static readonly QueryDescription _staticVisualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform, PresentationLocalBounds>()
            .WithNone<PresentationLodProfile>();
        private static readonly QueryDescription _payloadStaticVisualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform, PresentationLocalBounds, PresentationOwnerHasPerformerPayload>()
            .WithNone<PresentationLodProfile>();
        private static readonly QueryDescription _staticVisualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform, PresentationLodProfile>()
            .WithNone<PresentationLocalBounds>();
        private static readonly QueryDescription _payloadStaticVisualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform, PresentationLodProfile, PresentationOwnerHasPerformerPayload>()
            .WithNone<PresentationLocalBounds>();
        private static readonly QueryDescription _staticVisualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _payloadStaticVisualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform, PresentationOwnerHasPerformerPayload>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _staticNoVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform>()
            .WithNone<VisualTransform, PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _payloadStaticNoVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationOwnerHasPerformerPayload>()
            .WithNone<VisualTransform, PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _staticPendingVisualBoundsLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, VisualTransform, PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _payloadStaticPendingVisualBoundsLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, VisualTransform, PresentationLocalBounds, PresentationLodProfile, PresentationOwnerHasPerformerPayload>();
        private static readonly QueryDescription _staticPendingVisualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, VisualTransform, PresentationLocalBounds>()
            .WithNone<PresentationLodProfile>();
        private static readonly QueryDescription _payloadStaticPendingVisualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, VisualTransform, PresentationLocalBounds, PresentationOwnerHasPerformerPayload>()
            .WithNone<PresentationLodProfile>();
        private static readonly QueryDescription _staticPendingVisualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, VisualTransform, PresentationLodProfile>()
            .WithNone<PresentationLocalBounds>();
        private static readonly QueryDescription _payloadStaticPendingVisualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, VisualTransform, PresentationLodProfile, PresentationOwnerHasPerformerPayload>()
            .WithNone<PresentationLocalBounds>();
        private static readonly QueryDescription _staticPendingVisualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, VisualTransform>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _payloadStaticPendingVisualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, VisualTransform, PresentationOwnerHasPerformerPayload>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _staticPendingNoVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending>()
            .WithNone<VisualTransform, PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _payloadStaticPendingNoVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, PresentationOwnerHasPerformerPayload>()
            .WithNone<VisualTransform, PresentationLocalBounds, PresentationLodProfile>();
        private static readonly PresentationLocalBounds _defaultBounds =
            PresentationLocalBounds.Create(Vector3.Zero, new Vector3(0.5f, 0.5f, 0.5f));

        private readonly CameraManager _cameraManager;
        private readonly ISpatialQueryService _spatial;
        private readonly IViewController _view;
        private readonly ILoadedChunks? _loadedChunks;
        private readonly CameraCullingFocusOverride? _focusOverride;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private readonly PerformerEntityRuntime? _performers;
        private List<Entity> _changedOwners = new List<Entity>(32768);
        private Entity[] _spatialQueryBuffer = new Entity[65536];
        private readonly HashSet<Entity> _spatialCandidates = new(65536);
        private readonly CommandBuffer _commandBuffer = new();
        private int _lastPerformerCullSyncStructureVersion = -1;
        private bool _ownerCullChangedThisFrame;
        private bool _allCullChangesSyncedThisFrame;
        private CameraStateSnapshot _lastStaticCullCameraState;
        private float _lastStaticCullAspectRatio = -1f;
        private bool _hasStaticCullCameraState;
        private int _staticCullEpoch;
        private int _lastStaticVisibleCount;
        private int _visibilityRevision;

        public CameraCullingDebugState DebugState { get; } = new CameraCullingDebugState();

        public float HighLODDistCm { get; }
        public float MediumLODDistCm { get; }
        public float LowLODDistCm { get; }

        private QueryDescription VisualBoundsLodQuery => _performers == null ? _visualBoundsLodQuery : _payloadVisualBoundsLodQuery;
        private QueryDescription SpatialExcludedVisualBoundsLodQuery => _performers == null ? _spatialExcludedVisualBoundsLodQuery : _payloadSpatialExcludedVisualBoundsLodQuery;
        private QueryDescription VisualBoundsQuery => _performers == null ? _visualBoundsQuery : _payloadVisualBoundsQuery;
        private QueryDescription SpatialExcludedVisualBoundsQuery => _performers == null ? _spatialExcludedVisualBoundsQuery : _payloadSpatialExcludedVisualBoundsQuery;
        private QueryDescription VisualLodQuery => _performers == null ? _visualLodQuery : _payloadVisualLodQuery;
        private QueryDescription SpatialExcludedVisualLodQuery => _performers == null ? _spatialExcludedVisualLodQuery : _payloadSpatialExcludedVisualLodQuery;
        private QueryDescription VisualDefaultQuery => _performers == null ? _visualDefaultQuery : _payloadVisualDefaultQuery;
        private QueryDescription SpatialExcludedVisualDefaultQuery => _performers == null ? _spatialExcludedVisualDefaultQuery : _payloadSpatialExcludedVisualDefaultQuery;
        private QueryDescription NoVisualQuery => _performers == null ? _noVisualQuery : _payloadNoVisualQuery;
        private QueryDescription SpatialExcludedNoVisualQuery => _performers == null ? _spatialExcludedNoVisualQuery : _payloadSpatialExcludedNoVisualQuery;
        private QueryDescription StaticVisualBoundsLodQuery => _performers == null ? _staticVisualBoundsLodQuery : _payloadStaticVisualBoundsLodQuery;
        private QueryDescription StaticVisualBoundsQuery => _performers == null ? _staticVisualBoundsQuery : _payloadStaticVisualBoundsQuery;
        private QueryDescription StaticVisualLodQuery => _performers == null ? _staticVisualLodQuery : _payloadStaticVisualLodQuery;
        private QueryDescription StaticVisualDefaultQuery => _performers == null ? _staticVisualDefaultQuery : _payloadStaticVisualDefaultQuery;
        private QueryDescription StaticNoVisualQuery => _performers == null ? _staticNoVisualQuery : _payloadStaticNoVisualQuery;
        private QueryDescription StaticPendingVisualBoundsLodQuery => _performers == null ? _staticPendingVisualBoundsLodQuery : _payloadStaticPendingVisualBoundsLodQuery;
        private QueryDescription StaticPendingVisualBoundsQuery => _performers == null ? _staticPendingVisualBoundsQuery : _payloadStaticPendingVisualBoundsQuery;
        private QueryDescription StaticPendingVisualLodQuery => _performers == null ? _staticPendingVisualLodQuery : _payloadStaticPendingVisualLodQuery;
        private QueryDescription StaticPendingVisualDefaultQuery => _performers == null ? _staticPendingVisualDefaultQuery : _payloadStaticPendingVisualDefaultQuery;
        private QueryDescription StaticPendingNoVisualQuery => _performers == null ? _staticPendingNoVisualQuery : _payloadStaticPendingNoVisualQuery;

        public CameraCullingSystem(
            World world,
            CameraManager cameraManager,
            ISpatialQueryService spatial,
            IViewController view,
            CameraCullingRuntimeConfig cullingConfig,
            ILoadedChunks? loadedChunks = null,
            CameraCullingFocusOverride? focusOverride = null,
            PerformerEntityRuntime? performers = null,
            PresentationTimingDiagnostics? timingDiagnostics = null)
            : base(world)
        {
            _cameraManager = cameraManager;
            _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _loadedChunks = loadedChunks;
            _focusOverride = focusOverride;
            _performers = performers;
            _timingDiagnostics = timingDiagnostics;
            cullingConfig = cullingConfig ?? throw new ArgumentNullException(nameof(cullingConfig));
            cullingConfig.Validate();
            HighLODDistCm = cullingConfig.HighLodDistanceCm;
            MediumLODDistCm = cullingConfig.MediumLodDistanceCm;
            LowLODDistCm = cullingConfig.LowLodDistanceCm;
        }

        public override void Update(in float dt)
        {
            long start = Stopwatch.GetTimestamp();
            CameraStateSnapshot cameraState = _cameraManager.GetInterpolatedState(ReadPresentationAlpha());
            if (_focusOverride != null)
            {
                cameraState = _focusOverride.Apply(in cameraState);
            }

            var target = cameraState.TargetCm;
            float distanceCm = cameraState.DistanceCm;

            float aspectRatio = _view.AspectRatio;
            WorldAabbCm queryBounds = ComputeBroadPhaseCameraAabb(
                in cameraState,
                aspectRatio,
                out float minX,
                out float maxX,
                out float minY,
                out float maxY);

            _changedOwners.Clear();
            _ownerCullChangedThisFrame = false;
            _allCullChangesSyncedThisFrame = true;
            bool hasDynamicCullWork = HasDynamicCullWork();
            double spatialQueryMs = 0d;
            if (hasDynamicCullWork)
            {
                long spatialQueryStart = Stopwatch.GetTimestamp();
                RefreshSpatialCandidates(in queryBounds);
                spatialQueryMs = ElapsedMs(spatialQueryStart);
            }
            else if (_spatialCandidates.Count != 0)
            {
                _spatialCandidates.Clear();
            }

            float tx = target.X;
            float ty = target.Y;
            float highSq = HighLODDistCm * HighLODDistCm;
            float medSq = MediumLODDistCm * MediumLODDistCm;
            float lowSq2 = LowLODDistCm * LowLODDistCm;
            bool hasStaticCullCameraState = _hasStaticCullCameraState;
            bool cameraChanged = hasStaticCullCameraState &&
                                 HasCameraStateChanged(in cameraState, aspectRatio);
            int visibleCount = _lastStaticVisibleCount;
            long entityProcessStart = Stopwatch.GetTimestamp();
            int activeStaticCullEpoch = _staticCullEpoch == int.MaxValue ? 1 : _staticCullEpoch + 1;
            double staticProcessMs = 0d;
            double staticPendingRemoveMs = 0d;
            double dynamicProcessMs = 0d;
            long staticProcessStart = Stopwatch.GetTimestamp();

            if (cameraChanged)
            {
                _staticCullEpoch = activeStaticCullEpoch;
                _lastStaticVisibleCount = 0;
                _lastStaticVisibleCount = ProcessStaticEntitiesFull(
                    queryBounds,
                    target,
                    distanceCm,
                    tx,
                    ty,
                    highSq,
                    medSq,
                    lowSq2,
                    rebuildVisibleCount: true,
                    _lastStaticVisibleCount);

                staticProcessMs += ElapsedMs(staticProcessStart);
                visibleCount = _lastStaticVisibleCount;
            }
            else
            {
                if (!hasStaticCullCameraState)
                {
                    _staticCullEpoch = activeStaticCullEpoch;
                    _lastStaticVisibleCount = ProcessStaticEntitiesFull(
                        queryBounds,
                        target,
                        distanceCm,
                        tx,
                        ty,
                        highSq,
                        medSq,
                        lowSq2,
                        rebuildVisibleCount: true,
                        staticVisibleCount: 0);
                }
                else
                {
                    _lastStaticVisibleCount = ProcessStaticEntitiesDirty(
                        queryBounds,
                        target,
                        distanceCm,
                        tx,
                        ty,
                        highSq,
                        medSq,
                        lowSq2,
                        rebuildVisibleCount: false,
                        _lastStaticVisibleCount);
                }

                staticProcessMs += ElapsedMs(staticProcessStart);

                visibleCount = _lastStaticVisibleCount;
            }

            _hasStaticCullCameraState = true;
            _lastStaticCullCameraState = cameraState;
            _lastStaticCullAspectRatio = aspectRatio;
            PlaybackStructuralChanges();

            if (hasDynamicCullWork)
            {
                long dynamicProcessStart = Stopwatch.GetTimestamp();
                ProcessVisualBoundsLod(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref visibleCount, VisualBoundsLodQuery, useSpatialGate: true);
                ProcessVisualBounds(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref visibleCount, VisualBoundsQuery, useSpatialGate: true);
                ProcessVisualLod(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref visibleCount, VisualLodQuery, useSpatialGate: true);
                ProcessVisualDefault(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref visibleCount, VisualDefaultQuery, useSpatialGate: true);
                ProcessNoVisual(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref visibleCount, NoVisualQuery, useSpatialGate: true);
                ProcessVisualBoundsLod(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref visibleCount, SpatialExcludedVisualBoundsLodQuery, useSpatialGate: false);
                ProcessVisualBounds(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref visibleCount, SpatialExcludedVisualBoundsQuery, useSpatialGate: false);
                ProcessVisualLod(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref visibleCount, SpatialExcludedVisualLodQuery, useSpatialGate: false);
                ProcessVisualDefault(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref visibleCount, SpatialExcludedVisualDefaultQuery, useSpatialGate: false);
                ProcessNoVisual(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref visibleCount, SpatialExcludedNoVisualQuery, useSpatialGate: false);
                dynamicProcessMs = ElapsedMs(dynamicProcessStart);
            }

            DebugState.MinX = minX;
            DebugState.MaxX = maxX;
            DebugState.MinY = minY;
            DebugState.MaxY = maxY;
            DebugState.HighLodDist = HighLODDistCm;
            DebugState.MediumLodDist = MediumLODDistCm;
            DebugState.LowLodDist = LowLODDistCm;
            DebugState.CameraTargetCm = new System.Numerics.Vector2(target.X, target.Y);
            DebugState.VisibleEntityCount = visibleCount;
            DebugState.VisibilityRevision = _visibilityRevision;
            double entityProcessMs = (Stopwatch.GetTimestamp() - entityProcessStart) * 1000.0 / Stopwatch.Frequency;
            long performerSyncStart = Stopwatch.GetTimestamp();
            SyncPerformerCullVisibilityIfDirty();
            double performerSyncMs = (Stopwatch.GetTimestamp() - performerSyncStart) * 1000.0 / Stopwatch.Frequency;
            _timingDiagnostics?.ObserveCameraCullingBreakdown(entityProcessMs, performerSyncMs);
            _timingDiagnostics?.ObserveCameraCullingSpatialQuery(spatialQueryMs);
            _timingDiagnostics?.ObserveCameraCullingStageBreakdown(staticProcessMs, staticPendingRemoveMs, dynamicProcessMs);
            _timingDiagnostics?.ObserveCameraCulling((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency, visibleCount);
        }

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double ElapsedMs(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
        }

        private void TrackCullChange(Entity owner)
        {
            _ownerCullChangedThisFrame = true;
            _allCullChangesSyncedThisFrame = false;
            AdvanceVisibilityRevision();
            if (owner != Entity.Null)
            {
                _changedOwners.Add(owner);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AdvanceVisibilityRevision()
        {
            _visibilityRevision = _visibilityRevision == int.MaxValue ? 1 : _visibilityRevision + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TrackOrSyncCullChange(Entity owner, in CullState cull)
        {
            if (!TrySyncSingleRootPayloadCull(owner, in cull))
            {
                TrackCullChange(owner);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TrackOrSyncCullChange(
            Entity owner,
            in CullState cull,
            in PresentationOwnerHasPerformerPayload payload)
        {
            if (!TrySyncSingleRootPayloadCull(in payload, in cull))
            {
                TrackCullChange(owner);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TrySyncSingleRootPayloadCull(Entity owner, in CullState cull)
        {
            if (_performers == null ||
                owner == Entity.Null ||
                !World.Has<PresentationOwnerHasPerformerPayload>(owner))
            {
                return false;
            }

            ref readonly PresentationOwnerHasPerformerPayload payload = ref World.Get<PresentationOwnerHasPerformerPayload>(owner);
            return TrySyncSingleRootPayloadCull(in payload, in cull);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TrySyncSingleRootPayloadCull(in PresentationOwnerHasPerformerPayload payload, in CullState cull)
        {
            if (_performers == null)
            {
                return false;
            }

            if (payload.RootCount != 1 ||
                payload.SingleRootPerformer == Entity.Null)
            {
                return false;
            }

            if (_performers.TrySyncSingleRootCullVisibilityAndMarkEventDrivenStaticEmitDirty(
                    payload.SingleRootPerformer,
                    cull.IsVisible,
                    cull.LOD))
            {
                AdvanceVisibilityRevision();
                return true;
            }

            return false;
        }

        private void SyncPerformerCullVisibilityIfDirty()
        {
            if (_performers == null)
            {
                return;
            }

            int structureVersion = _performers.StructureVersion;
            if (!_ownerCullChangedThisFrame &&
                _lastPerformerCullSyncStructureVersion == structureVersion)
            {
                return;
            }

            if (!_ownerCullChangedThisFrame &&
                _allCullChangesSyncedThisFrame &&
                _lastPerformerCullSyncStructureVersion == structureVersion)
            {
                _lastPerformerCullSyncStructureVersion = structureVersion;
                return;
            }

            if (_lastPerformerCullSyncStructureVersion != structureVersion)
            {
                if (_ownerCullChangedThisFrame && !_performers.HasNonRootPerformers)
                {
                    _performers.SyncRootCullVisibilityAndMarkEventDrivenStaticEmitDirty(CollectionsMarshal.AsSpan(_changedOwners));
                }
                else
                {
                    _performers.SyncCullVisibility();
                    if (_ownerCullChangedThisFrame)
                    {
                        _performers.MarkEventDrivenStaticEmitDirty(CollectionsMarshal.AsSpan(_changedOwners));
                    }
                }
            }
            else
            {
                _performers.SyncCullVisibility(CollectionsMarshal.AsSpan(_changedOwners));
                _performers.MarkEventDrivenStaticEmitDirty(CollectionsMarshal.AsSpan(_changedOwners));
            }

            _lastPerformerCullSyncStructureVersion = structureVersion;
        }

        private bool HasCameraStateChanged(in CameraStateSnapshot state, float aspectRatio)
        {
            const float scalarEpsilon = 0.01f;
            const float targetEpsilonSq = 1f;
            return MathF.Abs(_lastStaticCullAspectRatio - aspectRatio) > scalarEpsilon ||
                   Vector2.DistanceSquared(_lastStaticCullCameraState.TargetCm, state.TargetCm) > targetEpsilonSq ||
                   MathF.Abs(_lastStaticCullCameraState.TargetHeightCm - state.TargetHeightCm) > scalarEpsilon ||
                   MathF.Abs(AngleDeltaDeg(_lastStaticCullCameraState.Yaw, state.Yaw)) > scalarEpsilon ||
                   MathF.Abs(_lastStaticCullCameraState.Pitch - state.Pitch) > scalarEpsilon ||
                   MathF.Abs(_lastStaticCullCameraState.DistanceCm - state.DistanceCm) > scalarEpsilon ||
                   MathF.Abs(_lastStaticCullCameraState.FovYDeg - state.FovYDeg) > scalarEpsilon ||
                   _lastStaticCullCameraState.RigKind != state.RigKind ||
                   _lastStaticCullCameraState.ZoomLevel != state.ZoomLevel ||
                   _lastStaticCullCameraState.IsFollowing != state.IsFollowing;
        }

        private void RefreshSpatialCandidates(in WorldAabbCm queryBounds)
        {
            _spatialCandidates.Clear();

            while (true)
            {
                SpatialQueryResult result = _spatial.QueryAabb(in queryBounds, _spatialQueryBuffer);
                for (int i = 0; i < result.Count; i++)
                {
                    _spatialCandidates.Add(_spatialQueryBuffer[i]);
                }

                if (!result.Overflowed)
                {
                    return;
                }

                int nextCapacity = _spatialQueryBuffer.Length <= 0
                    ? 1024
                    : _spatialQueryBuffer.Length * 2;
                _spatialQueryBuffer = new Entity[nextCapacity];
                _spatialCandidates.Clear();
            }
        }

        private int ProcessStaticEntitiesDirty(
            WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            bool rebuildVisibleCount,
            int staticVisibleCount)
        {
            int updatedVisibleCount = staticVisibleCount;
            int cullEpoch = _staticCullEpoch;
            ProcessStaticVisualBoundsLod(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, rebuildVisibleCount, ref updatedVisibleCount, StaticPendingVisualBoundsLodQuery, cullEpoch, onlyUnprocessed: false);
            ProcessStaticVisualBounds(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, rebuildVisibleCount, ref updatedVisibleCount, StaticPendingVisualBoundsQuery, cullEpoch, onlyUnprocessed: false);
            ProcessStaticVisualLod(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, rebuildVisibleCount, ref updatedVisibleCount, StaticPendingVisualLodQuery, cullEpoch, onlyUnprocessed: false);
            ProcessStaticVisualDefault(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, rebuildVisibleCount, ref updatedVisibleCount, StaticPendingVisualDefaultQuery, cullEpoch, onlyUnprocessed: false);
            ProcessStaticNoVisual(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, rebuildVisibleCount, ref updatedVisibleCount, StaticPendingNoVisualQuery, cullEpoch, onlyUnprocessed: false);
            return updatedVisibleCount;
        }

        private bool HasDynamicCullWork()
        {
            QueryDescription query = VisualBoundsLodQuery;
            if (HasAny(in query)) return true;
            query = VisualBoundsQuery;
            if (HasAny(in query)) return true;
            query = VisualLodQuery;
            if (HasAny(in query)) return true;
            query = VisualDefaultQuery;
            if (HasAny(in query)) return true;
            query = NoVisualQuery;
            if (HasAny(in query)) return true;
            query = SpatialExcludedVisualBoundsLodQuery;
            if (HasAny(in query)) return true;
            query = SpatialExcludedVisualBoundsQuery;
            if (HasAny(in query)) return true;
            query = SpatialExcludedVisualLodQuery;
            if (HasAny(in query)) return true;
            query = SpatialExcludedVisualDefaultQuery;
            if (HasAny(in query)) return true;
            query = SpatialExcludedNoVisualQuery;
            return HasAny(in query);
        }

        private bool HasAny(in QueryDescription query)
        {
            foreach (ref var _ in World.Query(in query))
            {
                return true;
            }

            return false;
        }

        private int ProcessStaticEntitiesFull(
            WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            bool rebuildVisibleCount,
            int staticVisibleCount)
        {
            int updatedVisibleCount = staticVisibleCount;
            int cullEpoch = _staticCullEpoch;
            ProcessStaticVisualBoundsLod(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, rebuildVisibleCount, ref updatedVisibleCount, StaticVisualBoundsLodQuery, cullEpoch, onlyUnprocessed: false);
            ProcessStaticVisualBounds(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, rebuildVisibleCount, ref updatedVisibleCount, StaticVisualBoundsQuery, cullEpoch, onlyUnprocessed: false);
            ProcessStaticVisualLod(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, rebuildVisibleCount, ref updatedVisibleCount, StaticVisualLodQuery, cullEpoch, onlyUnprocessed: false);
            ProcessStaticVisualDefault(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, rebuildVisibleCount, ref updatedVisibleCount, StaticVisualDefaultQuery, cullEpoch, onlyUnprocessed: false);
            ProcessStaticNoVisual(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, rebuildVisibleCount, ref updatedVisibleCount, StaticNoVisualQuery, cullEpoch, onlyUnprocessed: false);
            return updatedVisibleCount;
        }

        private void ProcessStaticVisualBoundsLod(
            in WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            bool rebuildVisibleCount,
            ref int visibleCount,
            in QueryDescription query,
            int cullEpoch,
            bool onlyUnprocessed)
        {
            foreach (ref var chunk in World.Query(in query))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                var statics = chunk.GetSpan<PresentationStaticTransform>();
                var visuals = chunk.GetSpan<VisualTransform>();
                var bounds = chunk.GetSpan<PresentationLocalBounds>();
                var lods = chunk.GetSpan<PresentationLodProfile>();
                bool hasPayloads = chunk.Has<PresentationOwnerHasPerformerPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPerformerPayload>()
                    : default;
                foreach (var index in chunk)
                {
                    if (onlyUnprocessed && statics[index].CullEpoch == cullEpoch)
                    {
                        continue;
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    bool wasVisible = culls[index].IsVisible;
                    int localVisibleCount = 0;
                    ProcessEntity(
                        entity,
                        ref culls[index],
                        in positions[index],
                        in visuals[index],
                        in bounds[index],
                        in lods[index],
                        hasLodProfile: true,
                        in queryBounds,
                        target,
                        distanceCm,
                        tx,
                        ty,
                        highSq,
                        medSq,
                        lowSq2,
                        useSpatialGate: false,
                        hasPayloads,
                        hasPayloads ? payloads[index] : default,
                        ref localVisibleCount);
                    statics[index].CullEpoch = cullEpoch;
                    QueueStaticCullPendingClear(entity);
                    AdjustVisibleCountAfterStaticProcess(rebuildVisibleCount, wasVisible, culls[index].IsVisible, ref visibleCount);
                }
            }
        }

        private void ProcessStaticVisualBounds(
            in WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            bool rebuildVisibleCount,
            ref int visibleCount,
            in QueryDescription query,
            int cullEpoch,
            bool onlyUnprocessed)
        {
            foreach (ref var chunk in World.Query(in query))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                var statics = chunk.GetSpan<PresentationStaticTransform>();
                var visuals = chunk.GetSpan<VisualTransform>();
                var bounds = chunk.GetSpan<PresentationLocalBounds>();
                bool hasPayloads = chunk.Has<PresentationOwnerHasPerformerPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPerformerPayload>()
                    : default;
                foreach (var index in chunk)
                {
                    if (onlyUnprocessed && statics[index].CullEpoch == cullEpoch)
                    {
                        continue;
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    bool wasVisible = culls[index].IsVisible;
                    int localVisibleCount = 0;
                    ProcessEntity(
                        entity,
                        ref culls[index],
                        in positions[index],
                        in visuals[index],
                        in bounds[index],
                        default,
                        hasLodProfile: false,
                        in queryBounds,
                        target,
                        distanceCm,
                        tx,
                        ty,
                        highSq,
                        medSq,
                        lowSq2,
                        useSpatialGate: false,
                        hasPayloads,
                        hasPayloads ? payloads[index] : default,
                        ref localVisibleCount);
                    statics[index].CullEpoch = cullEpoch;
                    QueueStaticCullPendingClear(entity);
                    AdjustVisibleCountAfterStaticProcess(rebuildVisibleCount, wasVisible, culls[index].IsVisible, ref visibleCount);
                }
            }
        }

        private void ProcessStaticVisualLod(
            in WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            bool rebuildVisibleCount,
            ref int visibleCount,
            in QueryDescription query,
            int cullEpoch,
            bool onlyUnprocessed)
        {
            foreach (ref var chunk in World.Query(in query))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                var statics = chunk.GetSpan<PresentationStaticTransform>();
                var visuals = chunk.GetSpan<VisualTransform>();
                var lods = chunk.GetSpan<PresentationLodProfile>();
                bool hasPayloads = chunk.Has<PresentationOwnerHasPerformerPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPerformerPayload>()
                    : default;
                foreach (var index in chunk)
                {
                    if (onlyUnprocessed && statics[index].CullEpoch == cullEpoch)
                    {
                        continue;
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    bool wasVisible = culls[index].IsVisible;
                    int localVisibleCount = 0;
                    ProcessEntity(
                        entity,
                        ref culls[index],
                        in positions[index],
                        in visuals[index],
                        in _defaultBounds,
                        in lods[index],
                        hasLodProfile: true,
                        in queryBounds,
                        target,
                        distanceCm,
                        tx,
                        ty,
                        highSq,
                        medSq,
                        lowSq2,
                        useSpatialGate: false,
                        hasPayloads,
                        hasPayloads ? payloads[index] : default,
                        ref localVisibleCount);
                    statics[index].CullEpoch = cullEpoch;
                    QueueStaticCullPendingClear(entity);
                    AdjustVisibleCountAfterStaticProcess(rebuildVisibleCount, wasVisible, culls[index].IsVisible, ref visibleCount);
                }
            }
        }

        private void ProcessStaticVisualDefault(
            in WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            bool rebuildVisibleCount,
            ref int visibleCount,
            in QueryDescription query,
            int cullEpoch,
            bool onlyUnprocessed)
        {
            foreach (ref var chunk in World.Query(in query))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                var statics = chunk.GetSpan<PresentationStaticTransform>();
                var visuals = chunk.GetSpan<VisualTransform>();
                bool hasPayloads = chunk.Has<PresentationOwnerHasPerformerPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPerformerPayload>()
                    : default;
                foreach (var index in chunk)
                {
                    if (onlyUnprocessed && statics[index].CullEpoch == cullEpoch)
                    {
                        continue;
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    bool wasVisible = culls[index].IsVisible;
                    int localVisibleCount = 0;
                    ProcessEntityWithDefaultVisual(
                        entity,
                        ref culls[index],
                        in positions[index],
                        in visuals[index],
                        in queryBounds,
                        target,
                        distanceCm,
                        tx,
                        ty,
                        highSq,
                        medSq,
                        lowSq2,
                        useSpatialGate: false,
                        hasPayloads,
                        hasPayloads ? payloads[index] : default,
                        ref localVisibleCount);
                    statics[index].CullEpoch = cullEpoch;
                    QueueStaticCullPendingClear(entity);
                    AdjustVisibleCountAfterStaticProcess(rebuildVisibleCount, wasVisible, culls[index].IsVisible, ref visibleCount);
                }
            }
        }

        private void ProcessStaticNoVisual(
            in WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            bool rebuildVisibleCount,
            ref int visibleCount,
            in QueryDescription query,
            int cullEpoch,
            bool onlyUnprocessed)
        {
            foreach (ref var chunk in World.Query(in query))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                var statics = chunk.GetSpan<PresentationStaticTransform>();
                bool hasPayloads = chunk.Has<PresentationOwnerHasPerformerPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPerformerPayload>()
                    : default;
                foreach (var index in chunk)
                {
                    if (onlyUnprocessed && statics[index].CullEpoch == cullEpoch)
                    {
                        continue;
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    bool wasVisible = culls[index].IsVisible;
                    int localVisibleCount = 0;
                    ProcessEntityWithoutVisual(
                        entity,
                        ref culls[index],
                        in positions[index],
                        in _defaultBounds,
                        default,
                        hasLodProfile: false,
                        in queryBounds,
                        target,
                        distanceCm,
                        tx,
                        ty,
                        highSq,
                        medSq,
                        lowSq2,
                        useSpatialGate: false,
                        hasPayloads,
                        hasPayloads ? payloads[index] : default,
                        ref localVisibleCount);
                    statics[index].CullEpoch = cullEpoch;
                    QueueStaticCullPendingClear(entity);
                    AdjustVisibleCountAfterStaticProcess(rebuildVisibleCount, wasVisible, culls[index].IsVisible, ref visibleCount);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void QueueStaticCullPendingClear(Entity entity)
        {
            if (World.Has<PresentationStaticCullPending>(entity))
            {
                _commandBuffer.Remove<PresentationStaticCullPending>(in entity);
            }
        }

        private void PlaybackStructuralChanges()
        {
            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AdjustVisibleCountAfterStaticProcess(
            bool rebuildVisibleCount,
            bool wasVisible,
            bool isVisible,
            ref int visibleCount)
        {
            if (rebuildVisibleCount)
            {
                if (isVisible)
                {
                    visibleCount++;
                }

                return;
            }

            if (!wasVisible && isVisible)
            {
                visibleCount++;
            }
            else if (wasVisible && !isVisible)
            {
                visibleCount--;
            }
        }

        private void ProcessVisualBoundsLod(
            in WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            ref int visibleCount,
            in QueryDescription query,
            bool useSpatialGate)
        {
            foreach (ref var chunk in World.Query(in query))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                var visuals = chunk.GetSpan<VisualTransform>();
                var bounds = chunk.GetSpan<PresentationLocalBounds>();
                var lods = chunk.GetSpan<PresentationLodProfile>();
                bool hasPayloads = chunk.Has<PresentationOwnerHasPerformerPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPerformerPayload>()
                    : default;
                foreach (var index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ProcessEntity(
                        entity,
                        ref culls[index],
                        in positions[index],
                        in visuals[index],
                        in bounds[index],
                        in lods[index],
                        hasLodProfile: true,
                        in queryBounds,
                        target,
                        distanceCm,
                        tx,
                        ty,
                        highSq,
                        medSq,
                        lowSq2,
                        useSpatialGate,
                        hasPayloads,
                        hasPayloads ? payloads[index] : default,
                        ref visibleCount);
                }
            }
        }

        private void ProcessVisualBounds(
            in WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            ref int visibleCount,
            in QueryDescription query,
            bool useSpatialGate)
        {
            foreach (ref var chunk in World.Query(in query))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                var visuals = chunk.GetSpan<VisualTransform>();
                var bounds = chunk.GetSpan<PresentationLocalBounds>();
                bool hasPayloads = chunk.Has<PresentationOwnerHasPerformerPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPerformerPayload>()
                    : default;
                foreach (var index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ProcessEntity(
                        entity,
                        ref culls[index],
                        in positions[index],
                        in visuals[index],
                        in bounds[index],
                        default,
                        hasLodProfile: false,
                        in queryBounds,
                        target,
                        distanceCm,
                        tx,
                        ty,
                        highSq,
                        medSq,
                        lowSq2,
                        useSpatialGate,
                        hasPayloads,
                        hasPayloads ? payloads[index] : default,
                        ref visibleCount);
                }
            }
        }

        private void ProcessVisualLod(
            in WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            ref int visibleCount,
            in QueryDescription query,
            bool useSpatialGate)
        {
            foreach (ref var chunk in World.Query(in query))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                var visuals = chunk.GetSpan<VisualTransform>();
                var lods = chunk.GetSpan<PresentationLodProfile>();
                bool hasPayloads = chunk.Has<PresentationOwnerHasPerformerPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPerformerPayload>()
                    : default;
                foreach (var index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ProcessEntity(
                        entity,
                        ref culls[index],
                        in positions[index],
                        in visuals[index],
                        in _defaultBounds,
                        in lods[index],
                        hasLodProfile: true,
                        in queryBounds,
                        target,
                        distanceCm,
                        tx,
                        ty,
                        highSq,
                        medSq,
                        lowSq2,
                        useSpatialGate,
                        hasPayloads,
                        hasPayloads ? payloads[index] : default,
                        ref visibleCount);
                }
            }
        }

        private void ProcessVisualDefault(
            in WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            ref int visibleCount,
            in QueryDescription query,
            bool useSpatialGate)
        {
            foreach (ref var chunk in World.Query(in query))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                var visuals = chunk.GetSpan<VisualTransform>();
                bool hasPayloads = chunk.Has<PresentationOwnerHasPerformerPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPerformerPayload>()
                    : default;
                foreach (var index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ProcessEntityWithDefaultVisual(
                        entity,
                        ref culls[index],
                        in positions[index],
                        in visuals[index],
                        in queryBounds,
                        target,
                        distanceCm,
                        tx,
                        ty,
                        highSq,
                        medSq,
                        lowSq2,
                        useSpatialGate,
                        hasPayloads,
                        hasPayloads ? payloads[index] : default,
                        ref visibleCount);
                }
            }
        }

        private void ProcessNoVisual(
            in WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            ref int visibleCount,
            in QueryDescription query,
            bool useSpatialGate)
        {
            foreach (ref var chunk in World.Query(in query))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                bool hasPayloads = chunk.Has<PresentationOwnerHasPerformerPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPerformerPayload>()
                    : default;
                foreach (var index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    PresentationLocalBounds bounds = World.TryGet(entity, out PresentationLocalBounds resolvedBounds)
                        ? resolvedBounds
                        : _defaultBounds;
                    bool hasLodProfile = World.TryGet(entity, out PresentationLodProfile lodProfile);
                    ProcessEntityWithoutVisual(
                        entity,
                        ref culls[index],
                        in positions[index],
                        in bounds,
                        in lodProfile,
                        hasLodProfile,
                        in queryBounds,
                        target,
                        distanceCm,
                        tx,
                        ty,
                        highSq,
                        medSq,
                        lowSq2,
                        useSpatialGate,
                        hasPayloads,
                        hasPayloads ? payloads[index] : default,
                        ref visibleCount);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float AngleDeltaDeg(float from, float to)
        {
            return ((to - from + 540f) % 360f) - 180f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static WorldAabbCm ComputeBroadPhaseCameraAabb(
            in CameraStateSnapshot cameraState,
            float aspectRatio,
            out float minX,
            out float maxX,
            out float minY,
            out float maxY)
        {
            (float logicWidth, float logicHeight) = CameraViewportUtil.ComputeViewportExtent(
                cameraState.DistanceCm,
                cameraState.FovYDeg,
                cameraState.Pitch,
                aspectRatio);

            float halfWidth = logicWidth * 0.5f;
            float halfHeight = logicHeight * 0.5f;
            Vector2 cameraForward = WorldPlane2D.CameraForwardFromYawDegrees(cameraState.Yaw);
            Vector2 cameraRight = WorldPlane2D.CameraRightFromYawDegrees(cameraState.Yaw);

            float halfX = (MathF.Abs(cameraRight.X) * halfWidth) + (MathF.Abs(cameraForward.X) * halfHeight);
            float halfY = (MathF.Abs(cameraRight.Y) * halfWidth) + (MathF.Abs(cameraForward.Y) * halfHeight);

            minX = cameraState.TargetCm.X - halfX;
            maxX = cameraState.TargetCm.X + halfX;
            minY = cameraState.TargetCm.Y - halfY;
            maxY = cameraState.TargetCm.Y + halfY;

            int ix = (int)MathF.Floor(minX);
            int iy = (int)MathF.Floor(minY);
            int iw = Math.Max(0, (int)MathF.Ceiling(maxX - minX));
            int ih = Math.Max(0, (int)MathF.Ceiling(maxY - minY));
            return new WorldAabbCm(ix, iy, iw, ih);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessEntity(
            Entity entity,
            ref CullState cull,
            in WorldPositionCm worldPosition,
            in VisualTransform visualTransform,
            in PresentationLocalBounds localBounds,
            in PresentationLodProfile lodProfile,
            bool hasLodProfile,
            in WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            ref int visibleCount)
        {
            ProcessEntity(
                entity,
                ref cull,
                in worldPosition,
                in visualTransform,
                in localBounds,
                in lodProfile,
                hasLodProfile,
                in queryBounds,
                target,
                distanceCm,
                tx,
                ty,
                highSq,
                medSq,
                lowSq2,
                useSpatialGate: true,
                hasPayload: false,
                default,
                ref visibleCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessEntity(
            Entity entity,
            ref CullState cull,
            in WorldPositionCm worldPosition,
            in VisualTransform visualTransform,
            in PresentationLocalBounds localBounds,
            in PresentationLodProfile lodProfile,
            bool hasLodProfile,
            in WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            bool useSpatialGate,
            bool hasPayload,
            in PresentationOwnerHasPerformerPayload payload,
            ref int visibleCount)
        {
            var wp = worldPosition.Value;
            float px = wp.X.ToFloat();
            float py = wp.Y.ToFloat();
            if (useSpatialGate && !PassesSpatialCandidateGate(entity))
            {
                ForceCull(entity, ref cull);
                return;
            }

            if (!PassesLoadedChunkGate(px, py))
            {
                ForceCull(entity, ref cull);
                return;
            }

            ComputeScreenCoverageAndViewportIntersection(
                px,
                py,
                target,
                distanceCm,
                in queryBounds,
                in visualTransform,
                in localBounds,
                out float coverage01,
                out bool inViewport);
            if (!inViewport)
            {
                ForceCull(entity, ref cull);
                return;
            }

            float dx = px - tx;
            float dy = py - ty;
            float distSq = dx * dx + dy * dy;
            cull.DistanceToCameraSq = distSq;
            cull.ScreenCoverage01 = coverage01;

            LODLevel resolvedLod = hasLodProfile
                ? ResolveLod(distSq, coverage01, in lodProfile)
                : ResolveLod(distSq, coverage01, highSq, medSq, lowSq2);
            bool changed = !cull.IsVisible || cull.LOD != resolvedLod;

            cull.LOD = resolvedLod;
            cull.IsVisible = true;
            if (changed)
            {
                if (hasPayload)
                {
                    TrackOrSyncCullChange(entity, in cull, in payload);
                }
                else
                {
                    TrackOrSyncCullChange(entity, in cull);
                }
            }
            else if (hasPayload)
            {
                EnsureVisiblePayloadEmitWork(in payload);
            }

            if (cull.IsVisible)
            {
                visibleCount++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessEntityWithDefaultVisual(
            Entity entity,
            ref CullState cull,
            in WorldPositionCm worldPosition,
            in VisualTransform visualTransform,
            in WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            ref int visibleCount)
        {
            ProcessEntityWithDefaultVisual(
                entity,
                ref cull,
                in worldPosition,
                in visualTransform,
                in queryBounds,
                target,
                distanceCm,
                tx,
                ty,
                highSq,
                medSq,
                lowSq2,
                useSpatialGate: true,
                hasPayload: false,
                default,
                ref visibleCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessEntityWithDefaultVisual(
            Entity entity,
            ref CullState cull,
            in WorldPositionCm worldPosition,
            in VisualTransform visualTransform,
            in WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            bool useSpatialGate,
            bool hasPayload,
            in PresentationOwnerHasPerformerPayload payload,
            ref int visibleCount)
        {
            var wp = worldPosition.Value;
            float px = wp.X.ToFloat();
            float py = wp.Y.ToFloat();
            if (useSpatialGate && !PassesSpatialCandidateGate(entity))
            {
                ForceCull(entity, ref cull);
                return;
            }

            if (!PassesLoadedChunkGate(px, py))
            {
                ForceCull(entity, ref cull);
                return;
            }

            if (!IntersectsViewportDefaultBounds(in visualTransform, in queryBounds))
            {
                ForceCull(entity, ref cull);
                return;
            }

            float dx = px - tx;
            float dy = py - ty;
            float distSq = dx * dx + dy * dy;
            cull.DistanceToCameraSq = distSq;
            cull.ScreenCoverage01 = 0f;

            LODLevel resolvedLod = ResolveLod(distSq, coverage01: 0f, highSq, medSq, lowSq2);
            bool changed = !cull.IsVisible || cull.LOD != resolvedLod;

            cull.LOD = resolvedLod;
            cull.IsVisible = true;
            if (changed)
            {
                if (hasPayload)
                {
                    TrackOrSyncCullChange(entity, in cull, in payload);
                }
                else
                {
                    TrackOrSyncCullChange(entity, in cull);
                }
            }
            else if (hasPayload)
            {
                EnsureVisiblePayloadEmitWork(in payload);
            }

            if (cull.IsVisible)
            {
                visibleCount++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessEntityWithoutVisual(
            Entity entity,
            ref CullState cull,
            in WorldPositionCm worldPosition,
            in PresentationLocalBounds localBounds,
            in PresentationLodProfile lodProfile,
            bool hasLodProfile,
            in WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            ref int visibleCount)
        {
            ProcessEntityWithoutVisual(
                entity,
                ref cull,
                in worldPosition,
                in localBounds,
                in lodProfile,
                hasLodProfile,
                in queryBounds,
                target,
                distanceCm,
                tx,
                ty,
                highSq,
                medSq,
                lowSq2,
                useSpatialGate: true,
                hasPayload: false,
                default,
                ref visibleCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessEntityWithoutVisual(
            Entity entity,
            ref CullState cull,
            in WorldPositionCm worldPosition,
            in PresentationLocalBounds localBounds,
            in PresentationLodProfile lodProfile,
            bool hasLodProfile,
            in WorldAabbCm queryBounds,
            Vector2 target,
            float distanceCm,
            float tx,
            float ty,
            float highSq,
            float medSq,
            float lowSq2,
            bool useSpatialGate,
            bool hasPayload,
            in PresentationOwnerHasPerformerPayload payload,
            ref int visibleCount)
        {
            var wp = worldPosition.Value;
            float px = wp.X.ToFloat();
            float py = wp.Y.ToFloat();
            if (useSpatialGate && !PassesSpatialCandidateGate(entity))
            {
                ForceCull(entity, ref cull);
                return;
            }

            if (!PassesLoadedChunkGate(px, py))
            {
                ForceCull(entity, ref cull);
                return;
            }

            ComputeScreenCoverageAndViewportIntersection(
                px,
                py,
                target,
                distanceCm,
                in queryBounds,
                new Vector3(px * 0.01f, 0f, py * 0.01f),
                Quaternion.Identity,
                Vector3.One,
                in localBounds,
                out float coverage01,
                out bool inViewport);
            if (!inViewport)
            {
                ForceCull(entity, ref cull);
                return;
            }

            float dx = px - tx;
            float dy = py - ty;
            float distSq = dx * dx + dy * dy;
            cull.DistanceToCameraSq = distSq;
            cull.ScreenCoverage01 = coverage01;

            LODLevel resolvedLod = hasLodProfile
                ? ResolveLod(distSq, coverage01, in lodProfile)
                : ResolveLod(distSq, coverage01, highSq, medSq, lowSq2);
            bool changed = !cull.IsVisible || cull.LOD != resolvedLod;

            cull.LOD = resolvedLod;
            cull.IsVisible = true;
            if (changed)
            {
                if (hasPayload)
                {
                    TrackOrSyncCullChange(entity, in cull, in payload);
                }
                else
                {
                    TrackOrSyncCullChange(entity, in cull);
                }
            }
            else if (hasPayload)
            {
                EnsureVisiblePayloadEmitWork(in payload);
            }

            if (cull.IsVisible)
            {
                visibleCount++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureVisiblePayloadEmitWork(in PresentationOwnerHasPerformerPayload payload)
        {
            if (_performers == null ||
                payload.RootCount != 1 ||
                payload.SingleRootPerformer == Entity.Null)
            {
                return;
            }

            if (_performers.EnsureRequestBackedEmitWorkScheduledIfNeeded(payload.SingleRootPerformer))
            {
                AdvanceVisibilityRevision();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IntersectsViewportDefaultBounds(
            in VisualTransform visualTransform,
            in WorldAabbCm queryBounds)
        {
            ComputeVisualWorldAabbCm(
                in visualTransform.Position,
                in visualTransform.Rotation,
                in visualTransform.Scale,
                in _defaultBounds,
                out float minX,
                out float maxX,
                out float minY,
                out float maxY,
                out _,
                out _);
            return maxX >= queryBounds.Left &&
                   minX <= queryBounds.Right &&
                   maxY >= queryBounds.Top &&
                   minY <= queryBounds.Bottom;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ForceCull(Entity owner, ref CullState cull)
        {
            bool changed = cull.IsVisible || cull.LOD == LODLevel.Culled;
            if (cull.LOD == LODLevel.Culled)
            {
                cull.LOD = LODLevel.Low;
            }

            cull.IsVisible = false;
            cull.ScreenCoverage01 = 0f;
            if (changed)
            {
                TrackOrSyncCullChange(owner, in cull);
            }
        }

        private bool PassesLoadedChunkGate(float worldXCm, float worldYCm)
        {
            if (_loadedChunks == null || _loadedChunks.ActiveChunkKeys.Count == 0)
            {
                return true;
            }

            int cellX = (int)MathF.Floor(worldXCm / HexCoordinates.EdgeLengthCm);
            int cellY = (int)MathF.Floor(worldYCm / HexCoordinates.EdgeLengthCm);
            int chunkX = cellX >> 6;
            int chunkY = cellY >> 6;
            long key = HexCoordinates.GetChunkKey(chunkX, chunkY);
            return _loadedChunks.IsLoaded(key);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool PassesSpatialCandidateGate(Entity entity)
        {
            return _spatialCandidates.Contains(entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ComputeScreenCoverageAndViewportIntersection(
            float px,
            float py,
            Vector2 target,
            float distanceCm,
            in WorldAabbCm queryBounds,
            in VisualTransform visualTransform,
            in PresentationLocalBounds localBounds,
            out float screenCoverage01,
            out bool intersectsViewport)
        {
            ComputeScreenCoverageAndViewportIntersection(
                px,
                py,
                target,
                distanceCm,
                in queryBounds,
                visualTransform.Position,
                visualTransform.Rotation,
                visualTransform.Scale,
                in localBounds,
                out screenCoverage01,
                out intersectsViewport);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ComputeScreenCoverageAndViewportIntersection(
            float px,
            float py,
            Vector2 target,
            float distanceCm,
            in WorldAabbCm queryBounds,
            in Vector3 center,
            in Quaternion rotation,
            in Vector3 scale,
            in PresentationLocalBounds localBounds,
            out float screenCoverage01,
            out bool intersectsViewport)
        {
            ComputeVisualWorldAabbCm(
                in center,
                in rotation,
                in scale,
                in localBounds,
                out float minX,
                out float maxX,
                out float minY,
                out float maxY,
                out float halfWidthCm,
                out float halfDepthCm);

            intersectsViewport = maxX >= queryBounds.Left &&
                                 minX <= queryBounds.Right &&
                                 maxY >= queryBounds.Top &&
                                 minY <= queryBounds.Bottom;

            float approxRadiusCm = MathF.Max(halfWidthCm, halfDepthCm);
            float distanceToCameraCm = MathF.Max(1f, MathF.Sqrt(((px - target.X) * (px - target.X)) + ((py - target.Y) * (py - target.Y)) + (distanceCm * distanceCm * 0.04f)));
            screenCoverage01 = Math.Clamp((approxRadiusCm * 2f) / MathF.Max(distanceToCameraCm, 1f), 0f, 1f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ComputeVisualWorldAabbCm(
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in PresentationLocalBounds localBounds,
            out float minX,
            out float maxX,
            out float minY,
            out float maxY,
            out float halfWidthCm,
            out float halfDepthCm)
        {
            Quaternion normalizedRotation = WorldPlane2D.NormalizeOrIdentity(rotation);
            Vector3 scaledCenter = new Vector3(
                localBounds.Center.X * scale.X,
                localBounds.Center.Y * scale.Y,
                localBounds.Center.Z * scale.Z);
            Vector3 worldCenter = position + Vector3.Transform(scaledCenter, normalizedRotation);

            Vector3 extents = new Vector3(
                MathF.Abs(localBounds.Extents.X * scale.X),
                MathF.Abs(localBounds.Extents.Y * scale.Y),
                MathF.Abs(localBounds.Extents.Z * scale.Z));

            Vector3 axisX = Vector3.Transform(Vector3.UnitX, normalizedRotation);
            Vector3 axisY = Vector3.Transform(Vector3.UnitY, normalizedRotation);
            Vector3 axisZ = Vector3.Transform(Vector3.UnitZ, normalizedRotation);
            halfWidthCm = MathF.Max(
                10f,
                ((MathF.Abs(axisX.X) * extents.X) +
                 (MathF.Abs(axisY.X) * extents.Y) +
                 (MathF.Abs(axisZ.X) * extents.Z)) * WorldUnits.CmPerMeter);
            halfDepthCm = MathF.Max(
                10f,
                ((MathF.Abs(axisX.Z) * extents.X) +
                 (MathF.Abs(axisY.Z) * extents.Y) +
                 (MathF.Abs(axisZ.Z) * extents.Z)) * WorldUnits.CmPerMeter);

            float centerXCm = worldCenter.X * WorldUnits.CmPerMeter;
            float centerYCm = worldCenter.Z * WorldUnits.CmPerMeter;
            minX = centerXCm - halfWidthCm;
            maxX = centerXCm + halfWidthCm;
            minY = centerYCm - halfDepthCm;
            maxY = centerYCm + halfDepthCm;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static LODLevel ResolveLod(float distSq, float coverage01, in PresentationLodProfile profile)
        {
            if (distSq <= (profile.High.MaxDistanceCm * profile.High.MaxDistanceCm) && coverage01 >= profile.High.MinScreenCoverage01)
            {
                return LODLevel.High;
            }

            if (distSq <= (profile.Medium.MaxDistanceCm * profile.Medium.MaxDistanceCm) && coverage01 >= profile.Medium.MinScreenCoverage01)
            {
                return LODLevel.Medium;
            }

            if (distSq <= (profile.Low.MaxDistanceCm * profile.Low.MaxDistanceCm) && coverage01 >= profile.Low.MinScreenCoverage01)
            {
                return LODLevel.Low;
            }

            return LODLevel.Low;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static LODLevel ResolveLod(float distSq, float coverage01, float highSq, float medSq, float lowSq2)
        {
            if (distSq < highSq)
            {
                return LODLevel.High;
            }

            if (distSq < medSq)
            {
                return LODLevel.Medium;
            }

            if (distSq < lowSq2)
            {
                return LODLevel.Low;
            }

            return LODLevel.Low;
        }

        private float ReadPresentationAlpha()
        {
            var job = new ReadAlphaJob();
            World.InlineQuery<ReadAlphaJob, PresentationFrameState>(in _presentationStateQuery, ref job);
            return job.Alpha;
        }

        private struct ReadAlphaJob : IForEach<PresentationFrameState>
        {
            public float Alpha;

            public ReadAlphaJob()
            {
                Alpha = 1f;
            }

            public void Update(ref PresentationFrameState state)
            {
                Alpha = state.Enabled ? state.InterpolationAlpha : 1f;
            }
        }
    }
}
