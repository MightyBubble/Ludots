using System;
using System.Numerics;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Map.Hex;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
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
            .WithNone<PresentationStaticTransform>();
        private static readonly QueryDescription _visualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLocalBounds>()
            .WithNone<PresentationLodProfile, PresentationStaticTransform>();
        private static readonly QueryDescription _visualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform, PresentationLodProfile>()
            .WithNone<PresentationLocalBounds, PresentationStaticTransform>();
        private static readonly QueryDescription _visualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, VisualTransform>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile, PresentationStaticTransform>();
        private static readonly QueryDescription _noVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState>()
            .WithNone<VisualTransform, PresentationStaticTransform>();
        private static readonly QueryDescription _staticVisualBoundsLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform, PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _staticVisualBoundsQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform, PresentationLocalBounds>()
            .WithNone<PresentationLodProfile>();
        private static readonly QueryDescription _staticVisualLodQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform, PresentationLodProfile>()
            .WithNone<PresentationLocalBounds>();
        private static readonly QueryDescription _staticVisualDefaultQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, VisualTransform>()
            .WithNone<PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _staticNoVisualQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform>()
            .WithNone<VisualTransform, PresentationLocalBounds, PresentationLodProfile>();
        private static readonly QueryDescription _staticDirtyQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CullState, PresentationStaticTransform, PresentationStaticCullPending>();
        private static readonly PresentationLocalBounds _defaultBounds =
            PresentationLocalBounds.Create(Vector3.Zero, new Vector3(0.5f, 0.5f, 0.5f));

        private readonly CameraManager _cameraManager;
        private readonly IViewController _view;
        private readonly ILoadedChunks? _loadedChunks;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private readonly PerformerEntityRuntime? _performers;
        private HashSet<Entity> _changedOwnerSet = new HashSet<Entity>(32768);
        private List<Entity> _changedOwners = new List<Entity>(32768);
        private int _lastPerformerCullSyncStructureVersion = -1;
        private bool _ownerCullChangedThisFrame;
        private CameraStateSnapshot _lastStaticCullCameraState;
        private float _lastStaticCullAspectRatio = -1f;
        private bool _hasStaticCullCameraState;
        private int _lastStaticVisibleCount;

        public CameraCullingDebugState DebugState { get; } = new CameraCullingDebugState();

        public float HighLODDistCm = 4000f;
        public float MediumLODDistCm = 10000f;
        public float LowLODDistCm = 20000f;

        public CameraCullingSystem(
            World world,
            CameraManager cameraManager,
            ISpatialQueryService spatial,
            IViewController view,
            PresentationTimingDiagnostics? timingDiagnostics)
            : this(
                world,
                cameraManager,
                spatial,
                view,
                loadedChunks: null,
                performers: null,
                timingDiagnostics)
        {
        }

        public CameraCullingSystem(
            World world,
            CameraManager cameraManager,
            ISpatialQueryService spatial,
            IViewController view,
            ILoadedChunks? loadedChunks = null,
            PerformerEntityRuntime? performers = null,
            PresentationTimingDiagnostics? timingDiagnostics = null)
            : base(world)
        {
            _cameraManager = cameraManager;
            _ = spatial ?? throw new ArgumentNullException(nameof(spatial));
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _loadedChunks = loadedChunks;
            _performers = performers;
            _timingDiagnostics = timingDiagnostics;
        }

        public override void Update(in float dt)
        {
            long start = Stopwatch.GetTimestamp();
            CameraStateSnapshot cameraState = _cameraManager.GetInterpolatedState(ReadPresentationAlpha());
            var target = cameraState.TargetCm;
            float distanceCm = cameraState.DistanceCm;

            float fovY = cameraState.FovYDeg * (float)(Math.PI / 180.0f);
            float aspectRatio = _view.AspectRatio;
            float pitchRad = cameraState.Pitch * (float)(Math.PI / 180.0f);

            float logicHeight = 2.0f * distanceCm * (float)Math.Tan(fovY / 2.0f);
            float pitchScale = 1.0f / (float)Math.Max(Math.Sin(pitchRad), 0.1f);
            logicHeight *= pitchScale;
            float logicWidth = logicHeight * aspectRatio;

            float buffer = 1.5f;
            logicWidth *= buffer;
            logicHeight *= buffer;

            float minX = target.X - logicWidth / 2f;
            float maxX = target.X + logicWidth / 2f;
            float minY = target.Y - logicHeight / 2f;
            float maxY = target.Y + logicHeight / 2f;

            _changedOwnerSet.Clear();
            _changedOwners.Clear();

            int ix = (int)MathF.Floor(minX);
            int iy = (int)MathF.Floor(minY);
            int iw = (int)MathF.Ceiling(maxX - minX);
            int ih = (int)MathF.Ceiling(maxY - minY);
            if (iw < 0) iw = 0;
            if (ih < 0) ih = 0;

            WorldAabbCm queryBounds = new WorldAabbCm(ix, iy, iw, ih);
            _ownerCullChangedThisFrame = false;

            float tx = target.X;
            float ty = target.Y;
            float highSq = HighLODDistCm * HighLODDistCm;
            float medSq = MediumLODDistCm * MediumLODDistCm;
            float lowSq2 = LowLODDistCm * LowLODDistCm;
            bool cameraChanged = !_hasStaticCullCameraState ||
                                 HasCameraStateChanged(in cameraState, aspectRatio);
            int visibleCount = _lastStaticVisibleCount;
            long entityProcessStart = Stopwatch.GetTimestamp();

            if (cameraChanged)
            {
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

                _hasStaticCullCameraState = true;
                _lastStaticCullCameraState = cameraState;
                _lastStaticCullAspectRatio = aspectRatio;

                World.Remove<PresentationStaticCullPending>(in _staticDirtyQuery);

                visibleCount = _lastStaticVisibleCount;
            }
            else
            {
                _lastStaticVisibleCount = ProcessStaticEntities(
                    in _staticDirtyQuery,
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

                World.Remove<PresentationStaticCullPending>(in _staticDirtyQuery);

                visibleCount = _lastStaticVisibleCount;
            }

            ProcessVisualBoundsLod(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref visibleCount);
            ProcessVisualBounds(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref visibleCount);
            ProcessVisualLod(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref visibleCount);
            ProcessVisualDefault(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref visibleCount);
            ProcessNoVisual(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, ref visibleCount);

            DebugState.MinX = minX;
            DebugState.MaxX = maxX;
            DebugState.MinY = minY;
            DebugState.MaxY = maxY;
            DebugState.HighLodDist = HighLODDistCm;
            DebugState.MediumLodDist = MediumLODDistCm;
            DebugState.LowLodDist = LowLODDistCm;
            DebugState.CameraTargetCm = new System.Numerics.Vector2(target.X, target.Y);
            DebugState.VisibleEntityCount = visibleCount;
            double entityProcessMs = (Stopwatch.GetTimestamp() - entityProcessStart) * 1000.0 / Stopwatch.Frequency;
            long performerSyncStart = Stopwatch.GetTimestamp();
            SyncPerformerCullVisibilityIfDirty();
            double performerSyncMs = (Stopwatch.GetTimestamp() - performerSyncStart) * 1000.0 / Stopwatch.Frequency;
            _timingDiagnostics?.ObserveCameraCullingBreakdown(entityProcessMs, performerSyncMs);
            _timingDiagnostics?.ObserveCameraCulling((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency, visibleCount);
        }

        private void TrackCullToCulled(Entity owner, in CullState cull)
        {
            if (cull.LOD == LODLevel.Culled && !cull.IsVisible)
            {
                return;
            }

            TrackCullChange(owner);
        }

        private void TrackCullChange(Entity owner)
        {
            _ownerCullChangedThisFrame = true;
            if (owner != Entity.Null && _changedOwnerSet.Add(owner))
            {
                _changedOwners.Add(owner);
            }
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
            const float epsilon = 0.0001f;
            return MathF.Abs(_lastStaticCullAspectRatio - aspectRatio) > epsilon ||
                   Vector2.DistanceSquared(_lastStaticCullCameraState.TargetCm, state.TargetCm) > epsilon ||
                   MathF.Abs(_lastStaticCullCameraState.Yaw - state.Yaw) > epsilon ||
                   MathF.Abs(_lastStaticCullCameraState.Pitch - state.Pitch) > epsilon ||
                   MathF.Abs(_lastStaticCullCameraState.DistanceCm - state.DistanceCm) > epsilon ||
                   MathF.Abs(_lastStaticCullCameraState.FovYDeg - state.FovYDeg) > epsilon ||
                   _lastStaticCullCameraState.RigKind != state.RigKind ||
                   _lastStaticCullCameraState.ZoomLevel != state.ZoomLevel ||
                   _lastStaticCullCameraState.IsFollowing != state.IsFollowing;
        }

        private int ProcessStaticEntities(
            in QueryDescription query,
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
            WorldAabbCm bounds = queryBounds;
            int updatedVisibleCount = staticVisibleCount;
            World.Query(in query, (Entity entity, ref WorldPositionCm position, ref CullState cull) =>
            {
                bool wasVisible = cull.IsVisible;
                int localVisibleCount = 0;

                if (World.TryGet(entity, out VisualTransform visual))
                {
                    PresentationLocalBounds localBounds = World.TryGet(entity, out PresentationLocalBounds resolvedBounds)
                        ? resolvedBounds
                        : _defaultBounds;
                    bool hasLodProfile = World.TryGet(entity, out PresentationLodProfile lodProfile);
                    ProcessEntity(
                        entity,
                        ref cull,
                        in position,
                        in visual,
                        in localBounds,
                        in lodProfile,
                        hasLodProfile,
                        in bounds,
                        target,
                        distanceCm,
                        tx,
                        ty,
                        highSq,
                        medSq,
                        lowSq2,
                        ref localVisibleCount);
                }
                else
                {
                    PresentationLocalBounds localBounds = World.TryGet(entity, out PresentationLocalBounds resolvedBounds)
                        ? resolvedBounds
                        : _defaultBounds;
                    bool hasLodProfile = World.TryGet(entity, out PresentationLodProfile lodProfile);
                    ProcessEntityWithoutVisual(
                        entity,
                        ref cull,
                        in position,
                        in localBounds,
                        in lodProfile,
                        hasLodProfile,
                        in bounds,
                        target,
                        distanceCm,
                        tx,
                        ty,
                        highSq,
                        medSq,
                        lowSq2,
                        ref localVisibleCount);
                }

                if (rebuildVisibleCount)
                {
                    if (cull.IsVisible)
                    {
                        updatedVisibleCount++;
                    }
                }
                else if (!wasVisible && cull.IsVisible)
                {
                    updatedVisibleCount++;
                }
                else if (wasVisible && !cull.IsVisible)
                {
                    updatedVisibleCount--;
                }
            });

            return updatedVisibleCount;
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
            ProcessStaticVisualBoundsLod(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, rebuildVisibleCount, ref updatedVisibleCount);
            ProcessStaticVisualBounds(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, rebuildVisibleCount, ref updatedVisibleCount);
            ProcessStaticVisualLod(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, rebuildVisibleCount, ref updatedVisibleCount);
            ProcessStaticVisualDefault(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, rebuildVisibleCount, ref updatedVisibleCount);
            ProcessStaticNoVisual(in queryBounds, target, distanceCm, tx, ty, highSq, medSq, lowSq2, rebuildVisibleCount, ref updatedVisibleCount);
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
            ref int visibleCount)
        {
            foreach (ref var chunk in World.Query(in _staticVisualBoundsLodQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                var visuals = chunk.GetSpan<VisualTransform>();
                var bounds = chunk.GetSpan<PresentationLocalBounds>();
                var lods = chunk.GetSpan<PresentationLodProfile>();
                foreach (var index in chunk)
                {
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
                        ref localVisibleCount);
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
            ref int visibleCount)
        {
            foreach (ref var chunk in World.Query(in _staticVisualBoundsQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                var visuals = chunk.GetSpan<VisualTransform>();
                var bounds = chunk.GetSpan<PresentationLocalBounds>();
                foreach (var index in chunk)
                {
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
                        ref localVisibleCount);
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
            ref int visibleCount)
        {
            foreach (ref var chunk in World.Query(in _staticVisualLodQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                var visuals = chunk.GetSpan<VisualTransform>();
                var lods = chunk.GetSpan<PresentationLodProfile>();
                foreach (var index in chunk)
                {
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
                        ref localVisibleCount);
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
            ref int visibleCount)
        {
            foreach (ref var chunk in World.Query(in _staticVisualDefaultQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                var visuals = chunk.GetSpan<VisualTransform>();
                foreach (var index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    bool wasVisible = culls[index].IsVisible;
                    int localVisibleCount = 0;
                    ProcessEntity(
                        entity,
                        ref culls[index],
                        in positions[index],
                        in visuals[index],
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
                        ref localVisibleCount);
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
            ref int visibleCount)
        {
            foreach (ref var chunk in World.Query(in _staticNoVisualQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                foreach (var index in chunk)
                {
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
                        ref localVisibleCount);
                    AdjustVisibleCountAfterStaticProcess(rebuildVisibleCount, wasVisible, culls[index].IsVisible, ref visibleCount);
                }
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
            ref int visibleCount)
        {
            foreach (ref var chunk in World.Query(in _visualBoundsLodQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                var visuals = chunk.GetSpan<VisualTransform>();
                var bounds = chunk.GetSpan<PresentationLocalBounds>();
                var lods = chunk.GetSpan<PresentationLodProfile>();
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
            ref int visibleCount)
        {
            foreach (ref var chunk in World.Query(in _visualBoundsQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                var visuals = chunk.GetSpan<VisualTransform>();
                var bounds = chunk.GetSpan<PresentationLocalBounds>();
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
            ref int visibleCount)
        {
            foreach (ref var chunk in World.Query(in _visualLodQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                var visuals = chunk.GetSpan<VisualTransform>();
                var lods = chunk.GetSpan<PresentationLodProfile>();
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
            ref int visibleCount)
        {
            foreach (ref var chunk in World.Query(in _visualDefaultQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
                var visuals = chunk.GetSpan<VisualTransform>();
                foreach (var index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ProcessEntity(
                        entity,
                        ref culls[index],
                        in positions[index],
                        in visuals[index],
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
            ref int visibleCount)
        {
            foreach (ref var chunk in World.Query(in _noVisualQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                var culls = chunk.GetSpan<CullState>();
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
                        ref visibleCount);
                }
            }
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
            if (!HasPresentationPayload(entity))
            {
                ForceCull(entity, ref cull);
                return;
            }

            var wp = worldPosition.Value;
            float px = wp.X.ToFloat();
            float py = wp.Y.ToFloat();
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
            if (cull.IsVisible != (resolvedLod != LODLevel.Culled) || cull.LOD != resolvedLod)
            {
                TrackCullChange(entity);
            }

            cull.LOD = resolvedLod;
            cull.IsVisible = resolvedLod != LODLevel.Culled;
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
            if (!HasPresentationPayload(entity))
            {
                ForceCull(entity, ref cull);
                return;
            }

            var wp = worldPosition.Value;
            float px = wp.X.ToFloat();
            float py = wp.Y.ToFloat();
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
            if (cull.IsVisible != (resolvedLod != LODLevel.Culled) || cull.LOD != resolvedLod)
            {
                TrackCullChange(entity);
            }

            cull.LOD = resolvedLod;
            cull.IsVisible = resolvedLod != LODLevel.Culled;
            if (cull.IsVisible)
            {
                visibleCount++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ForceCull(Entity owner, ref CullState cull)
        {
            TrackCullToCulled(owner, in cull);
            cull.LOD = LODLevel.Culled;
            cull.IsVisible = false;
            cull.ScreenCoverage01 = 0f;
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
            in Vector3 scale,
            in PresentationLocalBounds localBounds,
            out float screenCoverage01,
            out bool intersectsViewport)
        {
            float halfWidthCm = MathF.Max(10f, localBounds.Extents.X * MathF.Abs(scale.X) * 100f);
            float halfDepthCm = MathF.Max(10f, localBounds.Extents.Z * MathF.Abs(scale.Z) * 100f);
            float minX = (center.X * 100f) + (localBounds.Center.X * scale.X * 100f) - halfWidthCm;
            float maxX = minX + (halfWidthCm * 2f);
            float minY = (center.Z * 100f) + (localBounds.Center.Z * scale.Z * 100f) - halfDepthCm;
            float maxY = minY + (halfDepthCm * 2f);

            intersectsViewport = maxX >= queryBounds.Left &&
                                 minX <= queryBounds.Right &&
                                 maxY >= queryBounds.Top &&
                                 minY <= queryBounds.Bottom;

            float approxRadiusCm = MathF.Max(halfWidthCm, halfDepthCm);
            float distanceToCameraCm = MathF.Max(1f, MathF.Sqrt(((px - target.X) * (px - target.X)) + ((py - target.Y) * (py - target.Y)) + (distanceCm * distanceCm * 0.04f)));
            screenCoverage01 = Math.Clamp((approxRadiusCm * 2f) / MathF.Max(distanceToCameraCm, 1f), 0f, 1f);
        }

        private bool HasPresentationPayload(Entity entity)
        {
            if (_performers == null)
            {
                return true;
            }

            return _performers.HasOwnerPayload(entity);
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

            return LODLevel.Culled;
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

            return LODLevel.Culled;
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
