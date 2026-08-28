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
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

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
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLocalBounds, PresentationLodProfile, PresentationOwnerHasPresenterPayload>()
            .WithNone<PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _spatialExcludedVisualBoundsLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLocalBounds, PresentationLodProfile, SpatialPartitionExcluded>()
            .WithNone<PresentationStaticTransform>();
        private static readonly QueryDescription _payloadSpatialExcludedVisualBoundsLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLocalBounds, PresentationLodProfile, PresentationOwnerHasPresenterPayload, SpatialPartitionExcluded>()
            .WithNone<PresentationStaticTransform>();
        private static readonly QueryDescription _visualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLocalBounds>()
            .WithNone<PresentationLodProfile, PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _payloadVisualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLocalBounds, PresentationOwnerHasPresenterPayload>()
            .WithNone<PresentationLodProfile, PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _spatialExcludedVisualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLocalBounds, SpatialPartitionExcluded>()
            .WithNone<PresentationLodProfile, PresentationStaticTransform>();
        private static readonly QueryDescription _payloadSpatialExcludedVisualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLocalBounds, PresentationOwnerHasPresenterPayload, SpatialPartitionExcluded>()
            .WithNone<PresentationLodProfile, PresentationStaticTransform>();
        private static readonly QueryDescription _visualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLodProfile>()
            .WithNone<PresentationLocalBounds, PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _payloadVisualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLodProfile, PresentationOwnerHasPresenterPayload>()
            .WithNone<PresentationLocalBounds, PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _spatialExcludedVisualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLodProfile, SpatialPartitionExcluded>()
            .WithNone<PresentationLocalBounds, PresentationStaticTransform>();
        private static readonly QueryDescription _payloadSpatialExcludedVisualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLodProfile, PresentationOwnerHasPresenterPayload, SpatialPartitionExcluded>()
            .WithNone<PresentationLocalBounds, PresentationStaticTransform>();
        private static readonly QueryDescription _visualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile, PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _payloadVisualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationOwnerHasPresenterPayload>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile, PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _spatialExcludedVisualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, SpatialPartitionExcluded>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile, PresentationStaticTransform>();
        private static readonly QueryDescription _payloadSpatialExcludedVisualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationOwnerHasPresenterPayload, SpatialPartitionExcluded>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile, PresentationStaticTransform>();
        private static readonly QueryDescription _noVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState>()
            .WithNone<VisualTransform, PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _payloadNoVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationOwnerHasPresenterPayload>()
            .WithNone<VisualTransform, PresentationStaticTransform, SpatialPartitionExcluded>();
        private static readonly QueryDescription _spatialExcludedNoVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, SpatialPartitionExcluded>()
            .WithNone<VisualTransform, PresentationStaticTransform>();
        private static readonly QueryDescription _payloadSpatialExcludedNoVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationOwnerHasPresenterPayload, SpatialPartitionExcluded>()
            .WithNone<VisualTransform, PresentationStaticTransform>();
        private static readonly QueryDescription _staticVisualBoundsLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform, PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _payloadStaticVisualBoundsLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform, PresentationLocalBounds, PresentationLodProfile, PresentationOwnerHasPresenterPayload>();
        private static readonly QueryDescription _staticVisualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform, PresentationLocalBounds>()
            .WithNone<PresentationLodProfile>();
        private static readonly QueryDescription _payloadStaticVisualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform, PresentationLocalBounds, PresentationOwnerHasPresenterPayload>()
            .WithNone<PresentationLodProfile>();
        private static readonly QueryDescription _staticVisualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform, PresentationLodProfile>()
            .WithNone<PresentationLocalBounds>();
        private static readonly QueryDescription _payloadStaticVisualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform, PresentationLodProfile, PresentationOwnerHasPresenterPayload>()
            .WithNone<PresentationLocalBounds>();
        private static readonly QueryDescription _staticVisualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _payloadStaticVisualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform, PresentationOwnerHasPresenterPayload>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _staticNoVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform>()
            .WithNone<VisualTransform, PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _payloadStaticNoVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationOwnerHasPresenterPayload>()
            .WithNone<VisualTransform, PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _staticPendingVisualBoundsLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, VisualTransform, PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _payloadStaticPendingVisualBoundsLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, VisualTransform, PresentationLocalBounds, PresentationLodProfile, PresentationOwnerHasPresenterPayload>();
        private static readonly QueryDescription _staticPendingVisualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, VisualTransform, PresentationLocalBounds>()
            .WithNone<PresentationLodProfile>();
        private static readonly QueryDescription _payloadStaticPendingVisualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, VisualTransform, PresentationLocalBounds, PresentationOwnerHasPresenterPayload>()
            .WithNone<PresentationLodProfile>();
        private static readonly QueryDescription _staticPendingVisualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, VisualTransform, PresentationLodProfile>()
            .WithNone<PresentationLocalBounds>();
        private static readonly QueryDescription _payloadStaticPendingVisualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, VisualTransform, PresentationLodProfile, PresentationOwnerHasPresenterPayload>()
            .WithNone<PresentationLocalBounds>();
        private static readonly QueryDescription _staticPendingVisualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, VisualTransform>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _payloadStaticPendingVisualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, VisualTransform, PresentationOwnerHasPresenterPayload>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _staticPendingNoVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending>()
            .WithNone<VisualTransform, PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _payloadStaticPendingNoVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending, PresentationOwnerHasPresenterPayload>()
            .WithNone<VisualTransform, PresentationLocalBounds, PresentationLodProfile>();
        private static readonly PresentationLocalBounds _defaultBounds =
            PresentationLocalBounds.Create(Vector3.Zero, new Vector3(0.5f, 0.5f, 0.5f));

        private CameraManager _cameraManager;
        private readonly ISpatialQueryService _spatial;
        private IViewController _view;
        private readonly ILoadedChunks? _loadedChunks;
        private readonly IWorldChunkKeyResolver? _loadedChunkKeyResolver;
        private readonly CameraCullingFocusOverride? _focusOverride;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private readonly PresenterEntityRuntime? _presenters;
        private bool _presentBindingArmed;
        private readonly List<Entity> _changedOwners = new List<Entity>(32768);
        private Entity[] _spatialQueryBuffer = new Entity[65536];
        private readonly HashSet<Entity> _spatialCandidates = new(65536);
        private readonly CommandBuffer _commandBuffer = new();
        private int _lastPresenterCullSyncStructureVersion = -1;
        private bool _ownerCullChangedThisFrame;
        private bool _allCullChangesSyncedThisFrame;
        private readonly List<PresentBindingCullPass> _presentBindingPasses = new(4);
        private CameraStateSnapshot[] _passLastStaticCameraState = Array.Empty<CameraStateSnapshot>();
        private float[] _passLastStaticAspectRatio = Array.Empty<float>();
        private bool[] _passHasStaticCameraState = Array.Empty<bool>();
        private int[] _passLastStaticVisibleCount = Array.Empty<int>();
        private bool _unionPass;
        private int _staticCullEpoch;
        private int _visibilityRevision;

        public CameraCullingDebugState DebugState { get; } = new CameraCullingDebugState();

        public float HighLODDistCm { get; }
        public float MediumLODDistCm { get; }
        public float LowLODDistCm { get; }

        private QueryDescription VisualBoundsLodQuery => _presenters == null ? _visualBoundsLodQuery : _payloadVisualBoundsLodQuery;
        private QueryDescription SpatialExcludedVisualBoundsLodQuery => _presenters == null ? _spatialExcludedVisualBoundsLodQuery : _payloadSpatialExcludedVisualBoundsLodQuery;
        private QueryDescription VisualBoundsQuery => _presenters == null ? _visualBoundsQuery : _payloadVisualBoundsQuery;
        private QueryDescription SpatialExcludedVisualBoundsQuery => _presenters == null ? _spatialExcludedVisualBoundsQuery : _payloadSpatialExcludedVisualBoundsQuery;
        private QueryDescription VisualLodQuery => _presenters == null ? _visualLodQuery : _payloadVisualLodQuery;
        private QueryDescription SpatialExcludedVisualLodQuery => _presenters == null ? _spatialExcludedVisualLodQuery : _payloadSpatialExcludedVisualLodQuery;
        private QueryDescription VisualDefaultQuery => _presenters == null ? _visualDefaultQuery : _payloadVisualDefaultQuery;
        private QueryDescription SpatialExcludedVisualDefaultQuery => _presenters == null ? _spatialExcludedVisualDefaultQuery : _payloadSpatialExcludedVisualDefaultQuery;
        private QueryDescription NoVisualQuery => _presenters == null ? _noVisualQuery : _payloadNoVisualQuery;
        private QueryDescription SpatialExcludedNoVisualQuery => _presenters == null ? _spatialExcludedNoVisualQuery : _payloadSpatialExcludedNoVisualQuery;
        private QueryDescription StaticVisualBoundsLodQuery => _presenters == null ? _staticVisualBoundsLodQuery : _payloadStaticVisualBoundsLodQuery;
        private QueryDescription StaticVisualBoundsQuery => _presenters == null ? _staticVisualBoundsQuery : _payloadStaticVisualBoundsQuery;
        private QueryDescription StaticVisualLodQuery => _presenters == null ? _staticVisualLodQuery : _payloadStaticVisualLodQuery;
        private QueryDescription StaticVisualDefaultQuery => _presenters == null ? _staticVisualDefaultQuery : _payloadStaticVisualDefaultQuery;
        private QueryDescription StaticNoVisualQuery => _presenters == null ? _staticNoVisualQuery : _payloadStaticNoVisualQuery;
        private QueryDescription StaticPendingVisualBoundsLodQuery => _presenters == null ? _staticPendingVisualBoundsLodQuery : _payloadStaticPendingVisualBoundsLodQuery;
        private QueryDescription StaticPendingVisualBoundsQuery => _presenters == null ? _staticPendingVisualBoundsQuery : _payloadStaticPendingVisualBoundsQuery;
        private QueryDescription StaticPendingVisualLodQuery => _presenters == null ? _staticPendingVisualLodQuery : _payloadStaticPendingVisualLodQuery;
        private QueryDescription StaticPendingVisualDefaultQuery => _presenters == null ? _staticPendingVisualDefaultQuery : _payloadStaticPendingVisualDefaultQuery;
        private QueryDescription StaticPendingNoVisualQuery => _presenters == null ? _staticPendingNoVisualQuery : _payloadStaticPendingNoVisualQuery;

        public CameraCullingSystem(
            World world,
            CameraManager cameraManager,
            ISpatialQueryService spatial,
            IViewController view,
            CameraCullingRuntimeConfig cullingConfig,
            ILoadedChunks? loadedChunks = null,
            CameraCullingFocusOverride? focusOverride = null,
            PresenterEntityRuntime? presenters = null,
            PresentationTimingDiagnostics? timingDiagnostics = null)
            : base(world)
        {
            _cameraManager = cameraManager ?? throw new ArgumentNullException(nameof(cameraManager));
            _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _loadedChunks = loadedChunks;
            _loadedChunkKeyResolver = loadedChunks as IWorldChunkKeyResolver;
            _focusOverride = focusOverride;
            _presenters = presenters;
            _timingDiagnostics = timingDiagnostics;
            // Unit/harness constructors supply an explicit present surface; hosts Disarm until PresentBinding sync.
            _presentBindingArmed = true;
            _presentBindingPasses.Add(new PresentBindingCullPass(null, cameraManager, view));
            ResetPassStaticCaches();
            cullingConfig = cullingConfig ?? throw new ArgumentNullException(nameof(cullingConfig));
            cullingConfig.Validate();
            HighLODDistCm = cullingConfig.HighLodDistanceCm;
            MediumLODDistCm = cullingConfig.MediumLodDistanceCm;
            LowLODDistCm = cullingConfig.LowLodDistanceCm;
        }

        /// <summary>
        /// Render culling is PresentBinding-owned. Without an armed binding, Update is a no-op
        /// (LogicView alone must not drive CullState).
        /// </summary>
        public void DisarmPresentBindingCulling()
        {
            _presentBindingArmed = false;
        }

        public void RebindPresentBinding(CameraManager cameraManager, IViewController presentSurface)
        {
            ArgumentNullException.ThrowIfNull(cameraManager);
            ArgumentNullException.ThrowIfNull(presentSurface);
            _presentBindingPasses.Clear();
            _presentBindingPasses.Add(new PresentBindingCullPass(null, cameraManager, presentSurface));
            _presentBindingArmed = true;
            ResetPassStaticCaches();
        }

        /// <summary>
        /// Rebinds render culling to one pass per present binding. Each pass culls against its own
        /// binding camera/surface; the shared CullState receives the union of the passes (visible in
        /// any binding ⇒ drawn). No merged cross-binding camera or global visible set is built.
        /// </summary>
        public void RebindPresentBindings(IReadOnlyList<PresentBindingCullPass> passes)
        {
            ArgumentNullException.ThrowIfNull(passes);
            if (passes.Count == 0)
            {
                throw new ArgumentException("At least one present binding cull pass is required.", nameof(passes));
            }

            _presentBindingPasses.Clear();
            for (int i = 0; i < passes.Count; i++)
            {
                _presentBindingPasses.Add(passes[i]);
            }

            _presentBindingArmed = true;
            ResetPassStaticCaches();
        }

        private void ResetPassStaticCaches()
        {
            int count = _presentBindingPasses.Count;
            if (_passHasStaticCameraState.Length != count)
            {
                _passLastStaticCameraState = new CameraStateSnapshot[count];
                _passLastStaticAspectRatio = new float[count];
                _passHasStaticCameraState = new bool[count];
                _passLastStaticVisibleCount = new int[count];
                return;
            }

            Array.Clear(_passHasStaticCameraState);
            Array.Clear(_passLastStaticVisibleCount);
        }

        public override void Update(in float dt)
        {
            if (!_presentBindingArmed)
            {
                return;
            }

            long start = Stopwatch.GetTimestamp();
            float presentationAlpha = ReadPresentationAlpha();
            _changedOwners.Clear();
            _ownerCullChangedThisFrame = false;
            _allCullChangesSyncedThisFrame = true;
            int unionVisibleCount = 0;
            double spatialQueryMs = 0d;
            double staticProcessMs = 0d;
            double dynamicProcessMs = 0d;
            float debugMinX = 0f;
            float debugMaxX = 0f;
            float debugMinY = 0f;
            float debugMaxY = 0f;
            bool hasDebugBounds = false;
            Vector2 lastPassTarget = Vector2.Zero;
            // A full static re-evaluation in one pass can cull entities that only a later binding
            // sees; later passes must therefore also run full so the union can restore them.
            bool anyPassFullStatic = false;
            long entityProcessStart = Stopwatch.GetTimestamp();

            for (int passIndex = 0; passIndex < _presentBindingPasses.Count; passIndex++)
            {
                _unionPass = passIndex > 0;
                PresentBindingCullPass pass = _presentBindingPasses[passIndex];
                _cameraManager = pass.Camera;
                _view = pass.Surface;
                RunCullPass(
                    passIndex,
                    presentationAlpha,
                    ref anyPassFullStatic,
                    ref unionVisibleCount,
                    ref spatialQueryMs,
                    ref staticProcessMs,
                    ref dynamicProcessMs,
                    ref debugMinX,
                    ref debugMaxX,
                    ref debugMinY,
                    ref debugMaxY,
                    ref hasDebugBounds,
                    out Vector2 passTarget);
                lastPassTarget = passTarget;
            }

            _unionPass = false;
            PlaybackStructuralChanges();

            DebugState.MinX = debugMinX;
            DebugState.MaxX = debugMaxX;
            DebugState.MinY = debugMinY;
            DebugState.MaxY = debugMaxY;
            DebugState.HighLodDist = HighLODDistCm;
            DebugState.MediumLodDist = MediumLODDistCm;
            DebugState.LowLodDist = LowLODDistCm;
            DebugState.CameraTargetCm = new System.Numerics.Vector2(lastPassTarget.X, lastPassTarget.Y);
            DebugState.VisibleEntityCount = unionVisibleCount;
            DebugState.VisibilityRevision = _visibilityRevision;
            double entityProcessMs = (Stopwatch.GetTimestamp() - entityProcessStart) * 1000.0 / Stopwatch.Frequency;
            long presenterSyncStart = Stopwatch.GetTimestamp();
            SyncPresenterCullVisibilityIfDirty();
            double presenterSyncMs = (Stopwatch.GetTimestamp() - presenterSyncStart) * 1000.0 / Stopwatch.Frequency;
            _timingDiagnostics?.ObserveCameraCullingBreakdown(entityProcessMs, presenterSyncMs);
            _timingDiagnostics?.ObserveCameraCullingSpatialQuery(spatialQueryMs);
            _timingDiagnostics?.ObserveCameraCullingStageBreakdown(staticProcessMs, 0d, dynamicProcessMs);
            _timingDiagnostics?.ObserveCameraCulling((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency, unionVisibleCount);
        }

        private void RunCullPass(
            int passIndex,
            float presentationAlpha,
            ref bool anyPassFullStatic,
            ref int unionVisibleCount,
            ref double spatialQueryMs,
            ref double staticProcessMs,
            ref double dynamicProcessMs,
            ref float debugMinX,
            ref float debugMaxX,
            ref float debugMinY,
            ref float debugMaxY,
            ref bool hasDebugBounds,
            out Vector2 passTarget)
        {
            CameraStateSnapshot cameraState = _cameraManager.GetInterpolatedState(presentationAlpha);
            if (_focusOverride != null)
            {
                cameraState = _focusOverride.Apply(in cameraState);
            }

            var target = cameraState.TargetCm;
            float distanceCm = cameraState.DistanceCm;
            passTarget = target;

            float aspectRatio = _view.AspectRatio;
            WorldAabbCm queryBounds = ComputeBroadPhaseCameraAabb(
                in cameraState,
                aspectRatio,
                out float minX,
                out float maxX,
                out float minY,
                out float maxY);

            if (!hasDebugBounds)
            {
                debugMinX = minX;
                debugMaxX = maxX;
                debugMinY = minY;
                debugMaxY = maxY;
                hasDebugBounds = true;
            }
            else
            {
                debugMinX = MathF.Min(debugMinX, minX);
                debugMaxX = MathF.Max(debugMaxX, maxX);
                debugMinY = MathF.Min(debugMinY, minY);
                debugMaxY = MathF.Max(debugMaxY, maxY);
            }

            bool hasDynamicCullWork = HasDynamicCullWork();
            if (hasDynamicCullWork)
            {
                long spatialQueryStart = Stopwatch.GetTimestamp();
                RefreshSpatialCandidates(in queryBounds);
                spatialQueryMs += ElapsedMs(spatialQueryStart);
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
            bool hasStaticCullCameraState = _passHasStaticCameraState[passIndex];
            bool cameraChanged = hasStaticCullCameraState &&
                                 HasCameraStateChanged(passIndex, in cameraState, aspectRatio);
            int activeStaticCullEpoch = _staticCullEpoch == int.MaxValue ? 1 : _staticCullEpoch + 1;
            long staticProcessStart = Stopwatch.GetTimestamp();
            int passStaticCount;
            bool runFullStatic = anyPassFullStatic || cameraChanged || !hasStaticCullCameraState;

            if (runFullStatic)
            {
                anyPassFullStatic = true;
                _staticCullEpoch = activeStaticCullEpoch;
                passStaticCount = ProcessStaticEntitiesFull(
                    queryBounds,
                    target,
                    distanceCm,
                    tx,
                    ty,
                    highSq,
                    medSq,
                    lowSq2,
                    rebuildVisibleCount: !_unionPass,
                    staticVisibleCount: 0);
            }
            else
            {
                passStaticCount = ProcessStaticEntitiesDirty(
                    queryBounds,
                    target,
                    distanceCm,
                    tx,
                    ty,
                    highSq,
                    medSq,
                    lowSq2,
                    rebuildVisibleCount: !_unionPass,
                    _unionPass ? 0 : _passLastStaticVisibleCount[passIndex]);
            }

            _passHasStaticCameraState[passIndex] = true;
            _passLastStaticCameraState[passIndex] = cameraState;
            _passLastStaticAspectRatio[passIndex] = aspectRatio;
            _passLastStaticVisibleCount[passIndex] = passStaticCount;
            staticProcessMs += ElapsedMs(staticProcessStart);
            unionVisibleCount += passStaticCount;

            if (hasDynamicCullWork)
            {
                long dynamicProcessStart = Stopwatch.GetTimestamp();
                ProcessVisualBoundsLod(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref unionVisibleCount, VisualBoundsLodQuery, useSpatialGate: true);
                ProcessVisualBounds(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref unionVisibleCount, VisualBoundsQuery, useSpatialGate: true);
                ProcessVisualLod(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref unionVisibleCount, VisualLodQuery, useSpatialGate: true);
                ProcessVisualDefault(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref unionVisibleCount, VisualDefaultQuery, useSpatialGate: true);
                ProcessNoVisual(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref unionVisibleCount, NoVisualQuery, useSpatialGate: true);
                ProcessVisualBoundsLod(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref unionVisibleCount, SpatialExcludedVisualBoundsLodQuery, useSpatialGate: false);
                ProcessVisualBounds(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref unionVisibleCount, SpatialExcludedVisualBoundsQuery, useSpatialGate: false);
                ProcessVisualLod(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref unionVisibleCount, SpatialExcludedVisualLodQuery, useSpatialGate: false);
                ProcessVisualDefault(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref unionVisibleCount, SpatialExcludedVisualDefaultQuery, useSpatialGate: false);
                ProcessNoVisual(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref unionVisibleCount, SpatialExcludedNoVisualQuery, useSpatialGate: false);
                dynamicProcessMs += ElapsedMs(dynamicProcessStart);
            }
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
            in PresentationOwnerHasPresenterPayload payload)
        {
            if (!TrySyncSingleRootPayloadCull(in payload, in cull))
            {
                TrackCullChange(owner);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TrySyncSingleRootPayloadCull(Entity owner, in CullState cull)
        {
            if (_presenters == null ||
                owner == Entity.Null ||
                !World.Has<PresentationOwnerHasPresenterPayload>(owner))
            {
                return false;
            }

            ref readonly PresentationOwnerHasPresenterPayload payload = ref World.Get<PresentationOwnerHasPresenterPayload>(owner);
            return TrySyncSingleRootPayloadCull(in payload, in cull);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TrySyncSingleRootPayloadCull(in PresentationOwnerHasPresenterPayload payload, in CullState cull)
        {
            if (_presenters == null)
            {
                return false;
            }

            if (payload.RootCount != 1 ||
                payload.SingleRootPresenter == Entity.Null)
            {
                return false;
            }

            if (_presenters.TrySyncSingleRootCullVisibilityAndMarkEventDrivenStaticEmitDirty(
                    payload.SingleRootPresenter,
                    cull.IsVisible,
                    cull.LOD))
            {
                AdvanceVisibilityRevision();
                return true;
            }

            return false;
        }

        private void SyncPresenterCullVisibilityIfDirty()
        {
            if (_presenters == null)
            {
                return;
            }

            int structureVersion = _presenters.StructureVersion;
            if (!_ownerCullChangedThisFrame &&
                _lastPresenterCullSyncStructureVersion == structureVersion)
            {
                return;
            }

            if (!_ownerCullChangedThisFrame &&
                _allCullChangesSyncedThisFrame &&
                _lastPresenterCullSyncStructureVersion == structureVersion)
            {
                _lastPresenterCullSyncStructureVersion = structureVersion;
                return;
            }

            if (_lastPresenterCullSyncStructureVersion != structureVersion)
            {
                if (_ownerCullChangedThisFrame && !_presenters.HasNonRootPresenters)
                {
                    _presenters.SyncRootCullVisibilityAndMarkEventDrivenStaticEmitDirty(CollectionsMarshal.AsSpan(_changedOwners));
                }
                else
                {
                    _presenters.SyncCullVisibility();
                    if (_ownerCullChangedThisFrame)
                    {
                        _presenters.MarkEventDrivenStaticEmitDirty(CollectionsMarshal.AsSpan(_changedOwners));
                    }
                }
            }
            else
            {
                _presenters.SyncCullVisibility(CollectionsMarshal.AsSpan(_changedOwners));
                _presenters.MarkEventDrivenStaticEmitDirty(CollectionsMarshal.AsSpan(_changedOwners));
            }

            _lastPresenterCullSyncStructureVersion = structureVersion;
        }

        private bool HasCameraStateChanged(int passIndex, in CameraStateSnapshot state, float aspectRatio)
        {
            const float scalarEpsilon = 0.01f;
            const float targetEpsilonSq = 1f;
            CameraStateSnapshot last = _passLastStaticCameraState[passIndex];
            return MathF.Abs(_passLastStaticAspectRatio[passIndex] - aspectRatio) > scalarEpsilon ||
                   Vector2.DistanceSquared(last.TargetCm, state.TargetCm) > targetEpsilonSq ||
                   MathF.Abs(last.TargetHeightCm - state.TargetHeightCm) > scalarEpsilon ||
                   MathF.Abs(AngleDeltaDeg(last.Yaw, state.Yaw)) > scalarEpsilon ||
                   MathF.Abs(last.Pitch - state.Pitch) > scalarEpsilon ||
                   MathF.Abs(last.DistanceCm - state.DistanceCm) > scalarEpsilon ||
                   MathF.Abs(last.FovYDeg - state.FovYDeg) > scalarEpsilon ||
                   last.RigKind != state.RigKind ||
                   last.ZoomLevel != state.ZoomLevel ||
                   last.IsFollowing != state.IsFollowing;
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
                bool hasPayloads = chunk.Has<PresentationOwnerHasPresenterPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPresenterPayload>()
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
                bool hasPayloads = chunk.Has<PresentationOwnerHasPresenterPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPresenterPayload>()
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
                bool hasPayloads = chunk.Has<PresentationOwnerHasPresenterPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPresenterPayload>()
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
                bool hasPayloads = chunk.Has<PresentationOwnerHasPresenterPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPresenterPayload>()
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
                bool hasPayloads = chunk.Has<PresentationOwnerHasPresenterPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPresenterPayload>()
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
            // Pass 0 always reprocesses every pending static before union passes run, and the
            // removal defers to end-of-frame playback; union passes must not queue a duplicate.
            if (_unionPass)
            {
                return;
            }

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
            // Union passes (later bindings) only contribute entities newly visible to the union;
            // pass 0 keeps the sole-binding counting contract unchanged.
            if (!rebuildVisibleCount)
            {
                if (!wasVisible && isVisible)
                {
                    visibleCount++;
                }
                else if (wasVisible && !isVisible)
                {
                    visibleCount--;
                }

                return;
            }

            if (isVisible)
            {
                visibleCount++;
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
                bool hasPayloads = chunk.Has<PresentationOwnerHasPresenterPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPresenterPayload>()
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
                bool hasPayloads = chunk.Has<PresentationOwnerHasPresenterPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPresenterPayload>()
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
                bool hasPayloads = chunk.Has<PresentationOwnerHasPresenterPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPresenterPayload>()
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
                bool hasPayloads = chunk.Has<PresentationOwnerHasPresenterPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPresenterPayload>()
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
                bool hasPayloads = chunk.Has<PresentationOwnerHasPresenterPayload>();
                var payloads = hasPayloads
                    ? chunk.GetSpan<PresentationOwnerHasPresenterPayload>()
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
            in PresentationOwnerHasPresenterPayload payload,
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
            LODLevel resolvedLod = hasLodProfile
                ? ResolveLod(distSq, coverage01, in lodProfile)
                : ResolveLod(distSq, coverage01, highSq, medSq, lowSq2);
            if (_unionPass && cull.IsVisible)
            {
                if (resolvedLod < cull.LOD)
                {
                    cull.LOD = resolvedLod;
                    cull.DistanceToCameraSq = distSq;
                    cull.ScreenCoverage01 = coverage01;
                    if (hasPayload)
                    {
                        TrackOrSyncCullChange(entity, in cull, in payload);
                    }
                    else
                    {
                        TrackOrSyncCullChange(entity, in cull);
                    }
                }

                return;
            }

            cull.DistanceToCameraSq = distSq;
            cull.ScreenCoverage01 = coverage01;

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
            in PresentationOwnerHasPresenterPayload payload,
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
            LODLevel resolvedLod = ResolveLod(distSq, coverage01: 0f, highSq, medSq, lowSq2);
            if (_unionPass && cull.IsVisible)
            {
                if (resolvedLod < cull.LOD)
                {
                    cull.LOD = resolvedLod;
                    cull.DistanceToCameraSq = distSq;
                    cull.ScreenCoverage01 = 0f;
                    if (hasPayload)
                    {
                        TrackOrSyncCullChange(entity, in cull, in payload);
                    }
                    else
                    {
                        TrackOrSyncCullChange(entity, in cull);
                    }
                }

                return;
            }

            cull.DistanceToCameraSq = distSq;
            cull.ScreenCoverage01 = 0f;

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
            in PresentationOwnerHasPresenterPayload payload,
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

            LODLevel resolvedLod = hasLodProfile
                ? ResolveLod(distSq, coverage01, in lodProfile)
                : ResolveLod(distSq, coverage01, highSq, medSq, lowSq2);
            if (_unionPass && cull.IsVisible)
            {
                if (resolvedLod < cull.LOD)
                {
                    cull.LOD = resolvedLod;
                    cull.DistanceToCameraSq = distSq;
                    cull.ScreenCoverage01 = coverage01;
                    if (hasPayload)
                    {
                        TrackOrSyncCullChange(entity, in cull, in payload);
                    }
                    else
                    {
                        TrackOrSyncCullChange(entity, in cull);
                    }
                }

                return;
            }

            cull.DistanceToCameraSq = distSq;
            cull.ScreenCoverage01 = coverage01;

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
        private void EnsureVisiblePayloadEmitWork(in PresentationOwnerHasPresenterPayload payload)
        {
            if (_presenters == null ||
                payload.RootCount != 1 ||
                payload.SingleRootPresenter == Entity.Null)
            {
                return;
            }

            if (_presenters.EnsureRequestBackedEmitWorkScheduledIfNeeded(payload.SingleRootPresenter))
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
            // Union passes never remove visibility: an entity outside this binding's view may
            // still be visible to an earlier binding, so its CullState must survive untouched.
            if (_unionPass)
            {
                return;
            }

            bool changed = cull.IsVisible;
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

            if (_loadedChunkKeyResolver == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(CameraCullingSystem)} requires {nameof(IWorldChunkKeyResolver)} when {nameof(ILoadedChunks)} gates visibility.");
            }

            return _loadedChunks.IsLoaded(_loadedChunkKeyResolver.GetChunkKeyForWorldCm(worldXCm, worldYCm));
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
            Quaternion normalizedRotation = VisualMath.NormalizeOrIdentity(rotation);
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
