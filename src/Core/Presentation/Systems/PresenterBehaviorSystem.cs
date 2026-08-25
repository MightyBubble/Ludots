using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Diagnostics;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Gameplay.Teams;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class PresenterBehaviorSystem : BaseSystem<World, float>
    {
        private readonly struct OwnerAttributeWorkTarget
        {
            public readonly int DefinitionId;
            public readonly PresenterDefinition Definition;
            public readonly PresenterDefinition.OwnerAttributeWorkItem Work;

            public OwnerAttributeWorkTarget(int definitionId, PresenterDefinition definition, in PresenterDefinition.OwnerAttributeWorkItem work)
            {
                DefinitionId = definitionId;
                Definition = definition;
                Work = work;
            }
        }

        private readonly struct OwnerTagWorkTarget
        {
            public readonly int DefinitionId;
            public readonly PresenterDefinition Definition;
            public readonly PresenterDefinition.OwnerTagWorkItem Work;

            public OwnerTagWorkTarget(int definitionId, PresenterDefinition definition, in PresenterDefinition.OwnerTagWorkItem work)
            {
                DefinitionId = definitionId;
                Definition = definition;
                Work = work;
            }
        }

        private readonly PresenterEntityRuntime _runtime;
        private readonly PresenterDefinitionRegistry _definitions;
        private readonly PresentationEventStream _events;
        private readonly PresentationOwnerChangeBuffer _ownerChanges;
        private readonly SoundRequestBuffer _soundRequests;
        private readonly Func<IVisualHeightmap?> _heightmapProvider;
        private readonly Func<IBoneTransformProvider?> _boneTransformProvider;
        private readonly PresenterBehaviorKindRegistry? _extensionBehaviors;
        private readonly PresenterBehaviorOps _extensionBehaviorOps;
        private readonly GraphProgramRegistry? _graphPrograms;
        private readonly IGraphRuntimeApi? _graphApi;
        private readonly float[] _graphFloatRegs = new float[GraphVmLimits.MaxFloatRegisters];
        private readonly int[] _graphIntRegs = new int[GraphVmLimits.MaxIntRegisters];
        private readonly byte[] _graphBoolRegs = new byte[GraphVmLimits.MaxBoolRegisters];
        private readonly Entity[] _graphEntityRegs = new Entity[GraphVmLimits.MaxEntityRegisters];
        private readonly Entity[] _graphTargets = new Entity[GraphVmLimits.MaxTargets];
        private readonly int[] _graphCallStack = new int[GraphVmLimits.MaxCallStackDepth];
        private readonly HashSet<long> _warnedGraphBindings = new();
        private readonly PresentPhaseResolver _phaseResolver = new();
        private readonly Dictionary<int, SoundTrackingState> _soundTracking = new();
        private Dictionary<int, OwnerAttributeWorkTarget[]> _ownerAttributeWorkIndex;
        private Dictionary<int, OwnerTagWorkTarget[]> _ownerTagWorkIndex;
        private int _definitionVersion = -1;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private readonly QueryDescription _bootstrapPendingQuery = new QueryDescription()
            .WithAll<PresenterState, PresenterBootstrapPending>();
        private readonly QueryDescription _tickDrivenQuery = new QueryDescription()
            .WithAll<PresenterState, PresenterWorldPosition, PresenterWorldPlanePosition>()
            .WithAny<PerfHasSpline, PerfHasAttachmentTick, PerfHasGrounding, PerfHasSound, PerfHasOwnerFacingBinding, PerfHasGraphParamBinding, PerfHasExtensionBehavior>()
            .WithNone<PresenterBootstrapPending>();
        private readonly QueryDescription _materialDirtyQuery = new QueryDescription()
            .WithAll<PresenterState, PerfMaterialDirty>()
            .WithNone<PresenterBootstrapPending>();
        private readonly CommandBuffer _commandBuffer = new();
        private readonly List<Entity> _bootstrapClearList = new(256);
        private readonly List<Entity> _materialDirtyClearList = new(256);
        private readonly HashSet<int> _bootstrapGroundingDeferredEntityIds = new();
        private bool _bootstrapPassActive;
        private Vector3[] _groundingPositions = Array.Empty<Vector3>();
        private GroundingMode[] _groundingModes = Array.Empty<GroundingMode>();
        private float[] _groundingOffsets = Array.Empty<float>();
        private int[] _groundingIndices = Array.Empty<int>();
        private Quaternion[] _groundingRotations = Array.Empty<Quaternion>();
        private float[] _groundingWorldXCm = Array.Empty<float>();
        private float[] _groundingWorldZCm = Array.Empty<float>();
        private float[] _groundingHeightsCm = Array.Empty<float>();
        private bool[] _groundingResolved = Array.Empty<bool>();
        private bool _warnedMissingGroundingHeightmap;
        private bool _warnedGroundingSampleFailure;
        private readonly HashSet<int> _warnedBoneAttachmentProviderMissing = new();
        private readonly HashSet<int> _warnedBoneAttachmentInvalidBone = new();
        private readonly HashSet<int> _warnedBoneAttachmentResolveFailed = new();

        private struct SoundTrackingState
        {
            public uint ActiveMask;
            public int StableId;
            public int DefinitionId;
        }

        public PresenterBehaviorSystem(
            World world,
            PresenterEntityRuntime runtime,
            PresenterDefinitionRegistry definitions,
            PresentationEventStream events,
            PresentationOwnerChangeBuffer ownerChanges,
            SoundRequestBuffer soundRequests,
            IVisualHeightmap? heightmap = null,
            IBoneTransformProvider? boneTransformProvider = null,
            PresentationTimingDiagnostics? timingDiagnostics = null,
            PresenterBehaviorKindRegistry? extensionBehaviors = null,
            GraphProgramRegistry? graphPrograms = null,
            IGraphRuntimeApi? graphApi = null)
            : this(world, runtime, definitions, events, ownerChanges, soundRequests,
                () => heightmap, () => boneTransformProvider, timingDiagnostics, extensionBehaviors, graphPrograms, graphApi)
        {
        }

        public PresenterBehaviorSystem(
            World world,
            PresenterEntityRuntime runtime,
            PresenterDefinitionRegistry definitions,
            PresentationEventStream events,
            PresentationOwnerChangeBuffer ownerChanges,
            SoundRequestBuffer soundRequests,
            Func<IVisualHeightmap?> heightmapProvider,
            Func<IBoneTransformProvider?>? boneTransformProvider = null,
            PresentationTimingDiagnostics? timingDiagnostics = null,
            PresenterBehaviorKindRegistry? extensionBehaviors = null,
            GraphProgramRegistry? graphPrograms = null,
            IGraphRuntimeApi? graphApi = null)
            : base(world)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _ownerChanges = ownerChanges ?? throw new ArgumentNullException(nameof(ownerChanges));
            _soundRequests = soundRequests ?? throw new ArgumentNullException(nameof(soundRequests));
            _heightmapProvider = heightmapProvider ?? throw new ArgumentNullException(nameof(heightmapProvider));
            _boneTransformProvider = boneTransformProvider ?? (static () => null);
            _extensionBehaviors = extensionBehaviors;
            _graphPrograms = graphPrograms;
            _graphApi = graphApi;
            _extensionBehaviorOps = new PresenterBehaviorOps(_runtime);
            RefreshDefinitionIndexes();
            _timingDiagnostics = timingDiagnostics;
            _runtime.BindDefinitions(_definitions);
        }

        public override void Update(in float dt)
        {
            EnsureDefinitionIndexesCurrent();
            long start = _timingDiagnostics != null ? Stopwatch.GetTimestamp() : 0L;
            int ownerChanges;
            int tickDrivenCount;
            _runtime.BeginDeferredStructuralChanges(_commandBuffer);
            try
            {
                ProcessCreatedPresenters(dt);
                ownerChanges = ProcessOwnerChanges();
                PlaybackStructuralChanges();
                ProcessDirtyMaterialPresenters();
                tickDrivenCount = ProcessTickDrivenPresenters(dt);
                ClearProcessedBootstrapMarkers();
                ClearProcessedMaterialDirtyMarkers();
            }
            finally
            {
                _runtime.EndDeferredStructuralChanges(_commandBuffer);
            }

            PlaybackStructuralChanges();
            int destroyEventScanCount = StopDestroyedSounds();
            _ownerChanges.Clear();

            if (_timingDiagnostics != null)
            {
                _timingDiagnostics.ObservePresenterBehaviorCounts(
                    _bootstrapClearList.Count,
                    ownerChanges,
                    _lastOwnerAttributeChangeCount,
                    _lastOwnerTagChangeCount,
                    tickDrivenCount,
                    CountActiveSoundTrackingPresenters(),
                    destroyEventScanCount);
                _timingDiagnostics.ObservePresenterBehavior((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
            }
        }

        private int _lastOwnerAttributeChangeCount;
        private int _lastOwnerTagChangeCount;

        private void EnsureDefinitionIndexesCurrent()
        {
            if (_definitionVersion == _definitions.Version)
            {
                return;
            }

            RefreshDefinitionIndexes();
            _runtime.BindDefinitions(_definitions);
        }

        private void RefreshDefinitionIndexes()
        {
            _ownerAttributeWorkIndex = BuildOwnerAttributeWorkIndex(_definitions);
            _ownerTagWorkIndex = BuildOwnerTagWorkIndex(_definitions);
            _definitionVersion = _definitions.Version;
        }

        private void ProcessCreatedPresenters(float tickDt)
        {
            _bootstrapClearList.Clear();
            _bootstrapGroundingDeferredEntityIds.Clear();
            _bootstrapPassActive = true;
            IVisualHeightmap? heightmap = _heightmapProvider();
            try
            {
                foreach (ref var chunk in World.Query(in _bootstrapPendingQuery))
                {
                    Span<PresenterState> states = chunk.GetSpan<PresenterState>();
                    bool processedChunk = false;
                    bool singleDefinitionChunk = TryResolveSingleDefinitionChunk(states, chunk.Count, out int chunkDefId);
                    if (singleDefinitionChunk && chunk.Has<PresenterInstanceBehaviors>())
                    {
                        singleDefinitionChunk = false;
                    }

                    if (singleDefinitionChunk &&
                        _definitions.TryGet(chunkDefId, out PresenterDefinition chunkDefinition))
                    {
                        processedChunk = TryProcessBootstrapChunkFast(
                            chunk,
                            states,
                            chunkDefinition,
                            tickDt,
                            heightmap);
                    }

                    ref Entity entityFirst = ref chunk.Entity(0);
                    foreach (int index in chunk)
                    {
                        Entity entity = Unsafe.Add(ref entityFirst, index);
                        if (!processedChunk)
                        {
                            ProcessPresenter(
                                entity,
                                firstFrame: true,
                                updateAttributeBindings: true,
                                updateTagBindings: true,
                                tickDt,
                                tickDrivenOnly: false);
                        }

                        if (!_bootstrapGroundingDeferredEntityIds.Contains(entity.Id))
                        {
                            _bootstrapClearList.Add(entity);
                        }
                    }
                }
            }
            finally
            {
                _bootstrapPassActive = false;
            }
        }

        private void ClearProcessedBootstrapMarkers()
        {
            for (int i = 0; i < _bootstrapClearList.Count; i++)
            {
                Entity entity = _bootstrapClearList[i];
                if (World.IsAlive(entity) && World.Has<PresenterBootstrapPending>(entity))
                {
                    _commandBuffer.Remove<PresenterBootstrapPending>(in entity);
                }
            }
        }

        private bool TryProcessBootstrapChunkFast(
            Chunk chunk,
            Span<PresenterState> states,
            PresenterDefinition definition,
            float tickDt,
            IVisualHeightmap? heightmap)
        {
            if (!CanProcessBootstrapChunkFast(definition))
            {
                return false;
            }

            Span<PresenterWorldPosition> positions = chunk.GetSpan<PresenterWorldPosition>();
            Span<PresenterWorldPlanePosition> planePositions = chunk.GetSpan<PresenterWorldPlanePosition>();
            Span<PresenterWorldRotation> rotations = chunk.GetSpan<PresenterWorldRotation>();
            Span<PresenterWorldFacing> facings = chunk.GetSpan<PresenterWorldFacing>();
            Span<PresenterWorldScale> scales = chunk.GetSpan<PresenterWorldScale>();
            Span<PresenterTransformSource> sources = chunk.GetSpan<PresenterTransformSource>();
            Span<PresenterParent> parents = chunk.GetSpan<PresenterParent>();

            ResolveDefaultTransformSourceBatch(states, sources, parents, chunk);
            ResolveTransformBatch(positions, planePositions, rotations, facings, scales, sources, parents, states, definition, chunk);

            if (TryResolveParentAttachmentOnly(definition, out AttachmentConfig attachmentConfig, out int attachmentSlot))
            {
                ApplyParentAttachmentBatch(
                    positions,
                    planePositions,
                    rotations,
                    facings,
                    scales,
                    sources,
                    parents,
                    states,
                    attachmentSlot,
                    in attachmentConfig,
                    chunk);
            }

            if (DefinitionHasBootstrapGroundingWork(definition))
            {
                if (TrySkipOwnerBackedSnapToGroundBatch(
                        states,
                        definition.BootstrapGroundingBehaviorIndices,
                        definition.Behaviors,
                        chunk))
                {
                    return true;
                }

                if (heightmap == null)
                {
                    WarnMissingGroundingHeightmap();
                    ResolveMissingBootstrapGroundingBatch(
                        positions,
                        states,
                        definition.BootstrapGroundingBehaviorIndices,
                        definition.Behaviors,
                        chunk);
                }
                else if (!ApplyBootstrapGroundingBatch(
                        positions,
                        rotations,
                        states,
                        definition.BootstrapGroundingBehaviorIndices,
                        definition.Behaviors,
                        heightmap,
                        chunk))
                {
                    // Unresolved entities already marked inside ApplyBootstrapGroundingBatch.
                }
            }

            return true;
        }

        private int ProcessOwnerChanges()
        {
            _lastOwnerAttributeChangeCount = 0;
            _lastOwnerTagChangeCount = 0;

            int processed = 0;
            ReadOnlySpan<PresentationOwnerChange> changes = _ownerChanges.GetSpan();
            for (int i = 0; i < changes.Length; i++)
            {
                ref readonly var change = ref changes[i];
                if (!World.IsAlive(change.Owner))
                {
                    continue;
                }

                switch (change.Kind)
                {
                    case PresentationOwnerChangeKind.Attribute:
                        ProcessOwnerAttributeChange(change.Owner, change.KeyId);
                        _lastOwnerAttributeChangeCount++;
                        processed++;
                        break;
                    case PresentationOwnerChangeKind.Tag:
                        ProcessOwnerTagChange(change.Owner, change.KeyId, change.TagActive);
                        _lastOwnerTagChangeCount++;
                        processed++;
                        break;
                }
            }

            return processed;
        }

        private void ProcessOwnerAttributeChange(Entity owner, int attributeId)
        {
            if (!World.IsAlive(owner) ||
                !World.Has<AttributeBuffer>(owner) ||
                !_runtime.TryGetActiveByOwner(owner, out PresenterEntityRuntime.OwnerPresenterBucket presenters))
            {
                return;
            }

            ref AttributeBuffer attributes = ref World.Get<AttributeBuffer>(owner);
            if (presenters.TryGetSingle(out Entity single))
            {
                ProcessOwnerAttributeWorkForPresenter(single, attributeId, ref attributes);
                return;
            }

            int count = presenters.Count;
            for (int i = 0; i < count; i++)
            {
                ProcessOwnerAttributeWorkForPresenter(presenters.GetAt(i), attributeId, ref attributes);
            }
        }

        private void ProcessOwnerTagChange(Entity owner, int tagId, bool tagActive)
        {
            if (!World.IsAlive(owner) ||
                !_runtime.TryGetActiveByOwner(owner, out PresenterEntityRuntime.OwnerPresenterBucket presenters))
            {
                return;
            }

            if (presenters.TryGetSingle(out Entity single))
            {
                ProcessOwnerTagWorkForPresenter(single, tagId, tagActive);
                return;
            }

            int count = presenters.Count;
            for (int i = 0; i < count; i++)
            {
                ProcessOwnerTagWorkForPresenter(presenters.GetAt(i), tagId, tagActive);
            }
        }

        private void ProcessOwnerAttributeWorkForPresenter(
            Entity presenter,
            int attributeId,
            ref AttributeBuffer attributes)
        {
            if (!World.IsAlive(presenter) || !World.Has<PresenterState>(presenter))
            {
                return;
            }

            ref readonly PresenterState state = ref World.Get<PresenterState>(presenter);
            if (!_definitions.TryGet(state.DefId, out PresenterDefinition definition))
            {
                return;
            }

            if (definition.TryGetOwnerAttributeWork(attributeId, out PresenterDefinition.OwnerAttributeWorkItem work))
            {
                ApplyOwnerAttributeWork(presenter, definition, ref attributes, in work);
            }

            if (definition.TryGetExtensionOwnerAttributeWork(attributeId, out PresenterDefinition.ExtensionOwnerAttributeWorkItem extensionWork))
            {
                ProcessExtensionBehaviors(
                    presenter,
                    in state,
                    definition,
                    definition.Behaviors,
                    extensionWork.BehaviorIndices,
                    PresenterBehaviorExecutionLane.OwnerAttributeDirty,
                    firstFrame: false,
                    tickDt: 0f);
            }
        }

        private void ProcessOwnerTagWorkForPresenter(Entity presenter, int tagId, bool tagActive)
        {
            if (!World.IsAlive(presenter) || !World.Has<PresenterState>(presenter))
            {
                return;
            }

            ref readonly PresenterState state = ref World.Get<PresenterState>(presenter);
            if (!_definitions.TryGet(state.DefId, out PresenterDefinition definition))
            {
                return;
            }

            if (definition.TryGetOwnerTagWork(tagId, out PresenterDefinition.OwnerTagWorkItem work))
            {
                ApplyOwnerTagWork(presenter, definition, in work, tagActive);
            }

            if (definition.TryGetExtensionOwnerTagWork(tagId, out PresenterDefinition.ExtensionOwnerTagWorkItem extensionWork))
            {
                ProcessExtensionBehaviors(
                    presenter,
                    in state,
                    definition,
                    definition.Behaviors,
                    extensionWork.BehaviorIndices,
                    PresenterBehaviorExecutionLane.OwnerTagDirty,
                    firstFrame: false,
                    tickDt: 0f);
            }
        }

        private int ProcessTickDrivenPresenters(float tickDt)
        {
            int processed = 0;
            foreach (ref var chunk in World.Query(in _tickDrivenQuery))
            {
                Span<PresenterState> states = chunk.GetSpan<PresenterState>();
                Span<PresenterWorldPosition> positions = chunk.GetSpan<PresenterWorldPosition>();
                Span<PresenterWorldPlanePosition> planePositions = chunk.GetSpan<PresenterWorldPlanePosition>();
                PresenterDefinition? chunkDefinition = null;
                bool singleDefinitionChunk = TryResolveSingleDefinitionChunk(states, chunk.Count, out int chunkDefId);
                if (singleDefinitionChunk && (chunk.Has<PresenterInstanceBehaviors>() || !_definitions.TryGet(chunkDefId, out chunkDefinition)))
                {
                    singleDefinitionChunk = false;
                }

                bool batchGrounding = singleDefinitionChunk && chunkDefinition != null && chunkDefinition.HasEveryFrameGroundingWork;
                Span<PresenterWorldRotation> rotations = batchGrounding
                    ? chunk.GetSpan<PresenterWorldRotation>()
                    : Span<PresenterWorldRotation>.Empty;
                ref Entity entityFirst = ref chunk.Entity(0);

                if (batchGrounding &&
                    chunkDefinition!.TickBehaviorsAreGroundingOnly &&
                    !chunkDefinition.HasOwnerFacingBindingWork &&
                    !chunkDefinition.HasGraphParamBindingWork)
                {
                    if (TrySkipOwnerBackedSnapToGroundBatch(
                            states,
                            chunkDefinition!.TickBehaviorIndices,
                            chunkDefinition.Behaviors,
                            chunk))
                    {
                        processed += chunk.Count;
                        continue;
                    }

                    IVisualHeightmap? heightmap = _heightmapProvider();
                    if (heightmap == null)
                    {
                        WarnMissingGroundingHeightmap();
                        SetGroundingMissingHeightmapHeightBatch(
                            positions,
                            states,
                            chunkDefinition!.TickBehaviorIndices,
                            chunkDefinition.Behaviors,
                            chunk);
                        processed += chunk.Count;
                        continue;
                    }

                    ApplyGroundingBatch(
                        positions,
                        rotations,
                        states,
                        chunkDefinition!.TickBehaviorIndices,
                        chunkDefinition.Behaviors,
                        heightmap,
                        chunk);
                    processed += chunk.Count;
                    continue;
                }

                if (singleDefinitionChunk &&
                    chunkDefinition != null &&
                    !chunkDefinition.HasOwnerFacingBindingWork &&
                    !chunkDefinition.HasGraphParamBindingWork &&
                    TryResolveParentAttachmentOnly(chunkDefinition, out AttachmentConfig attachmentConfig, out int attachmentSlot))
                {
                    Span<PresenterParent> parents = chunk.GetSpan<PresenterParent>();
                    rotations = chunk.GetSpan<PresenterWorldRotation>();
                    Span<PresenterWorldFacing> facings = chunk.GetSpan<PresenterWorldFacing>();
                    Span<PresenterWorldScale> scales = chunk.GetSpan<PresenterWorldScale>();
                    Span<PresenterTransformSource> sources = chunk.GetSpan<PresenterTransformSource>();
                    ApplyParentAttachmentBatch(
                        positions,
                        planePositions,
                        rotations,
                        facings,
                        scales,
                        sources,
                        parents,
                        states,
                        attachmentSlot,
                        in attachmentConfig,
                        chunk);
                    processed += chunk.Count;
                    continue;
                }

                foreach (int index in chunk)
                {
                    processed++;
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ProcessPresenter(
                        entity,
                        firstFrame: false,
                        updateAttributeBindings: false,
                        updateTagBindings: false,
                        tickDt,
                        tickDrivenOnly: true,
                        skipGroundingBehaviors: batchGrounding);
                }

                if (batchGrounding)
                {
                    PresenterDefinition groundingDefinition = chunkDefinition!;
                    if (TrySkipOwnerBackedSnapToGroundBatch(
                            states,
                            groundingDefinition.TickBehaviorIndices,
                            groundingDefinition.Behaviors,
                            chunk))
                    {
                        continue;
                    }

                    IVisualHeightmap? heightmap = _heightmapProvider();
                    if (heightmap == null)
                    {
                        WarnMissingGroundingHeightmap();
                        SetGroundingMissingHeightmapHeightBatch(
                            positions,
                            states,
                            groundingDefinition.TickBehaviorIndices,
                            groundingDefinition.Behaviors,
                            chunk);
                        continue;
                    }

                    ApplyGroundingBatch(
                        positions,
                        rotations,
                        states,
                        groundingDefinition.TickBehaviorIndices,
                        groundingDefinition.Behaviors,
                        heightmap,
                        chunk);
                }
            }

            return processed;
        }

        private void ProcessDirtyMaterialPresenters()
        {
            _materialDirtyClearList.Clear();
            foreach (ref Chunk chunk in World.Query(in _materialDirtyQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<PresenterState> states = chunk.GetSpan<PresenterState>();
                foreach (int index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ref readonly PresenterState state = ref states[index];
                    if (_definitions.TryGet(state.DefId, out PresenterDefinition definition))
                    {
                        ProcessMaterialBehaviors(entity, in state, definition);
                    }

                    if (World.Has<PresenterInstanceBehaviors>(entity))
                    {
                        ApplyInstanceMaterialBehaviors(entity, in state);
                    }

                    _materialDirtyClearList.Add(entity);
                }
            }
        }

        private void ClearProcessedMaterialDirtyMarkers()
        {
            for (int i = 0; i < _materialDirtyClearList.Count; i++)
            {
                Entity entity = _materialDirtyClearList[i];
                if (World.IsAlive(entity) && World.Has<PerfMaterialDirty>(entity))
                {
                    _commandBuffer.Remove<PerfMaterialDirty>(in entity);
                }
            }
        }

        private void PlaybackStructuralChanges()
        {
            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        private sealed class PresenterBehaviorOps : IPresenterBehaviorOps
        {
            private readonly PresenterEntityRuntime _runtime;
            private Entity _presenter;

            public PresenterBehaviorOps(PresenterEntityRuntime runtime)
            {
                _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            }

            public void Bind(Entity presenter)
            {
                _presenter = presenter;
            }

            public bool TryResolveFloat(int paramKey, out float value)
            {
                return _runtime.TryResolveFloat(_presenter, paramKey, out value);
            }

            public bool TryResolveInt(int paramKey, out int value)
            {
                return _runtime.TryResolveInt(_presenter, paramKey, out value);
            }

            public bool TryResolveVector(int paramKey, out Vector4 value)
            {
                return _runtime.TryResolveVector(_presenter, paramKey, out value);
            }

            public void SetParam(int paramKey, ParamLane lane, float floatValue = 0f, int intValue = 0, Vector4 vectorValue = default)
            {
                _runtime.SetParamAndPropagateToAffectedChildren(_presenter, paramKey, lane, floatValue, intValue, vectorValue);
            }

            public void ClearParam(int paramKey, ParamLane lane)
            {
                _runtime.ClearParamAndPropagateToAffectedChildren(_presenter, paramKey, lane);
            }
        }

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }

        private void ProcessPresenter(
            Entity entity,
            bool firstFrame,
            bool updateAttributeBindings,
            bool updateTagBindings,
            float tickDt,
            bool tickDrivenOnly,
            bool skipGroundingBehaviors = false)
        {
            if (!World.IsAlive(entity) || !World.Has<PresenterState>(entity))
            {
                return;
            }

            ref PresenterState state = ref World.Get<PresenterState>(entity);
            if (!_definitions.TryGet(state.DefId, out PresenterDefinition definition))
            {
                return;
            }

            Entity owner = state.OwnerEntity;
            BehaviorSlot[] behaviors = definition.Behaviors;
            bool hasInstanceBehaviors = World.Has<PresenterInstanceBehaviors>(entity);
            PresenterInstanceBehaviors instanceBehaviors = hasInstanceBehaviors
                ? World.Get<PresenterInstanceBehaviors>(entity)
                : default;
            ResolveDefaultTransformSource(entity, ref state);
            if (!tickDrivenOnly)
            {
                ApplyBindings(entity, owner, definition);
                ApplyCompiledDirtyBindings(
                    entity,
                    owner,
                    definition,
                    state.BehaviorActiveMask,
                    applyAttributes: firstFrame || updateAttributeBindings,
                    applyTags: firstFrame || updateTagBindings);
            }
            else if (definition.HasOwnerFacingBindingWork || definition.HasGraphParamBindingWork)
            {
                ApplyOwnerFacingBindings(entity, owner, definition);
                ApplyGraphParamBindings(entity, owner, definition);
            }

            bool hasSoundBehavior = definition.HasSoundBehavior || (hasInstanceBehaviors && instanceBehaviors.HasSound);
            if (hasSoundBehavior)
            {
                HandleReusedSoundSlot(entity, in state, behaviors);
            }

            ResolveTransform(entity, ref state, definition, behaviors);

            uint currentSoundMask = 0u;
            if (tickDrivenOnly)
            {
                int[] tickBehaviorIndices = definition.TickBehaviorIndices;
                for (int i = 0; i < tickBehaviorIndices.Length; i++)
                {
                    ref readonly BehaviorSlot slot = ref behaviors[tickBehaviorIndices[i]];
                    if (!IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                    {
                        continue;
                    }

                    switch (slot.Kind)
                    {
                        case BehaviorKind.Attachment:
                            ApplyAttachment(entity, in slot.Attachment);
                            break;
                        case BehaviorKind.Grounding:
                            if (!skipGroundingBehaviors)
                            {
                                ApplyGrounding(entity, in slot.Grounding);
                            }
                            break;
                        case BehaviorKind.Sound:
                            ApplySound(entity, in state, slot);
                            currentSoundMask |= 1u << slot.SlotIndex;
                            break;
                        case BehaviorKind.Spline:
                            ApplySpline(entity, ref state, slot.Spline, tickDt);
                            break;
                    }
                }
            }
            else
            {
                for (int i = 0; i < behaviors.Length; i++)
                {
                    ref readonly BehaviorSlot slot = ref behaviors[i];
                    if (!IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                        continue;
                    switch (slot.Kind)
                    {
                        case BehaviorKind.Material:
                            ApplyMaterialBinding(entity, slot.Material);
                            break;
                        case BehaviorKind.Attachment:
                            ApplyAttachment(entity, in slot.Attachment);
                            break;
                        case BehaviorKind.Grounding:
                            if (!skipGroundingBehaviors)
                            {
                                ApplyGrounding(entity, in slot.Grounding);
                            }
                            break;
                        case BehaviorKind.Sound:
                            ApplySound(entity, in state, slot);
                            currentSoundMask |= 1u << slot.SlotIndex;
                            break;
                        case BehaviorKind.Spline:
                            ApplySpline(entity, ref state, slot.Spline, tickDt);
                            break;
                    }
                }
            }

            if (hasInstanceBehaviors)
            {
                BehaviorSlot[] instanceSlots = instanceBehaviors.Slots;
                for (int i = 0; i < instanceSlots.Length; i++)
                {
                    ref readonly BehaviorSlot slot = ref instanceSlots[i];
                    if (!IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                        continue;
                    if (tickDrivenOnly)
                    {
                        switch (slot.Kind)
                        {
                            case BehaviorKind.Attachment:
                                ApplyAttachment(entity, in slot.Attachment);
                                break;
                            case BehaviorKind.Grounding:
                                if (!skipGroundingBehaviors)
                                {
                                    ApplyGrounding(entity, in slot.Grounding);
                                }
                                break;
                            case BehaviorKind.Sound:
                                ApplySound(entity, in state, slot);
                                currentSoundMask |= 1u << slot.SlotIndex;
                                break;
                            case BehaviorKind.Spline:
                                ApplySpline(entity, ref state, slot.Spline, tickDt);
                                break;
                        }
                    }
                    else
                    {
                        switch (slot.Kind)
                        {
                            case BehaviorKind.Material:
                                ApplyMaterialBinding(entity, slot.Material);
                                break;
                            case BehaviorKind.Attachment:
                                ApplyAttachment(entity, in slot.Attachment);
                                break;
                            case BehaviorKind.Grounding:
                                if (!skipGroundingBehaviors)
                                {
                                    ApplyGrounding(entity, in slot.Grounding);
                                }
                                break;
                            case BehaviorKind.Sound:
                                ApplySound(entity, in state, slot);
                                currentSoundMask |= 1u << slot.SlotIndex;
                                break;
                            case BehaviorKind.Spline:
                                ApplySpline(entity, ref state, slot.Spline, tickDt);
                                break;
                        }
                    }
                }
            }

            if (hasSoundBehavior)
            {
                StopInactiveSounds(entity, in state, behaviors, currentSoundMask);
                if (hasInstanceBehaviors)
                {
                    StopInactiveSounds(entity, in state, instanceBehaviors.Slots, currentSoundMask);
                }

                if (currentSoundMask != 0u)
                {
                    _soundTracking[entity.Id] = new SoundTrackingState
                    {
                        ActiveMask = currentSoundMask,
                        StableId = state.StableId,
                        DefinitionId = state.DefId,
                    };
                }
                else
                {
                    _soundTracking.Remove(entity.Id);
                }
            }

            if (tickDrivenOnly)
            {
                ProcessExtensionBehaviors(
                    entity,
                    in state,
                    definition,
                    behaviors,
                    definition.ExtensionTickBehaviorIndices,
                    PresenterBehaviorExecutionLane.ContinuousTick,
                    firstFrame: false,
                    tickDt);
                if (hasInstanceBehaviors)
                {
                    ProcessExtensionBehaviors(
                        entity,
                        in state,
                        definition,
                        instanceBehaviors.Slots,
                        instanceBehaviors.ExtensionTickIndices,
                        PresenterBehaviorExecutionLane.ContinuousTick,
                        firstFrame: false,
                        tickDt);
                }
            }
            else if (firstFrame)
            {
                ProcessExtensionBehaviors(
                    entity,
                    in state,
                    definition,
                    behaviors,
                    definition.ExtensionBootstrapBehaviorIndices,
                    PresenterBehaviorExecutionLane.Bootstrap,
                    firstFrame: true,
                    tickDt);
                if (hasInstanceBehaviors)
                {
                    ProcessExtensionBehaviors(
                        entity,
                        in state,
                        definition,
                        instanceBehaviors.Slots,
                        instanceBehaviors.ExtensionBootstrapIndices,
                        PresenterBehaviorExecutionLane.Bootstrap,
                        firstFrame: true,
                        tickDt);
                }
            }
        }

        private void ProcessExtensionBehaviors(
            Entity entity,
            in PresenterState state,
            PresenterDefinition definition,
            BehaviorSlot[] behaviors,
            int[] indices,
            PresenterBehaviorExecutionLane lane,
            bool firstFrame,
            float tickDt)
        {
            if (behaviors == null || indices == null || indices.Length == 0)
            {
                return;
            }

            if (_extensionBehaviors == null)
            {
                throw new InvalidOperationException(
                    $"Presenter definition '{definition.Key}' has extension behaviors, but presenter behavior extension registry is not configured.");
            }

            for (int i = 0; i < indices.Length; i++)
            {
                int behaviorIndex = indices[i];
                if ((uint)behaviorIndex >= (uint)behaviors.Length)
                {
                    continue;
                }

                BehaviorSlot slot = behaviors[behaviorIndex];
                if (slot.ExtensionLane != lane)
                {
                    throw new InvalidOperationException(
                        $"Presenter definition '{definition.Key}' behavior slot {slot.SlotIndex} is compiled for lane {slot.ExtensionLane}, but runtime lane is {lane}.");
                }

                if (!IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                {
                    continue;
                }

                int kindId = slot.KindId;
                if (kindId <= 0 || !_extensionBehaviors.TryGetDescriptor(kindId, out PresenterBehaviorExtensionDescriptor descriptor))
                {
                    throw new InvalidOperationException(
                        $"Presenter definition '{definition.Key}' behavior slot {slot.SlotIndex} references unregistered extension behavior id {kindId}.");
                }

                if (descriptor.Lane != lane)
                {
                    throw new InvalidOperationException(
                        $"Presenter definition '{definition.Key}' behavior slot {slot.SlotIndex} runtime lane {lane} does not match registered lane {descriptor.Lane}.");
                }

                _extensionBehaviorOps.Bind(entity);
                var view = new PresenterBehaviorView(
                    entity,
                    state.OwnerEntity,
                    state.DefId,
                    slot.SlotIndex,
                    kindId,
                    lane,
                    firstFrame,
                    tickDt);
                var context = new PresenterBehaviorExecutionContext(in view, _extensionBehaviorOps);
                descriptor.Handler(in context);
            }
        }

        private void ProcessMaterialBehaviors(Entity entity, in PresenterState state, PresenterDefinition definition)
        {
            int[] materialBehaviorIndices = definition.MaterialBehaviorIndices;
            if (materialBehaviorIndices.Length == 0)
            {
                return;
            }

            BehaviorSlot[] behaviors = definition.Behaviors;
            for (int i = 0; i < materialBehaviorIndices.Length; i++)
            {
                int behaviorIndex = materialBehaviorIndices[i];
                if ((uint)behaviorIndex >= (uint)behaviors.Length)
                {
                    continue;
                }

                ref readonly BehaviorSlot slot = ref behaviors[behaviorIndex];
                if (IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                {
                    ApplyMaterialBinding(entity, slot.Material);
                }
            }
        }

        private void ApplyInstanceMaterialBehaviors(Entity entity, in PresenterState state)
        {
            BehaviorSlot[] instanceSlots = World.Get<PresenterInstanceBehaviors>(entity).Slots;
            for (int i = 0; i < instanceSlots.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref instanceSlots[i];
                if (slot.Kind == BehaviorKind.Material && IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                {
                    ApplyMaterialBinding(entity, slot.Material);
                }
            }
        }

        private void ApplyBindings(Entity entity, Entity owner, PresenterDefinition definition)
        {
            PresenterParamBinding[] bindings = definition.Bindings;
            if (bindings == null || bindings.Length == 0) return;
            for (int i = 0; i < bindings.Length; i++)
            {
                ref readonly PresenterParamBinding binding = ref bindings[i];
                ValueRef value = binding.Value;
                switch (value.Source)
                {
                    case ValueSourceKind.EntityColor:
                        SetParam(entity, binding.ParamKey, ParamLane.Float, ResolveEntityColorChannel(entity, owner, value.SourceId), 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.EntityColorVector:
                        SetParam(entity, binding.ParamKey, ParamLane.Vector, 0f, 0, ResolveEntityColor(entity, owner));
                        break;
                    case ValueSourceKind.FacingRadians:
                        SetParam(entity, binding.ParamKey, ParamLane.Float, ResolveFacingRadians(owner), 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.FacingDegrees:
                        SetParam(entity, binding.ParamKey, ParamLane.Float, VisualMath.RadToDegValue(ResolveFacingRadians(owner)), 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.Constant:
                        SetParam(entity, binding.ParamKey, ParamLane.Float, value.ConstantValue, 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.Graph:
                        EvaluateGraphParamBinding(entity, owner, definition, i, in binding);
                        break;
                }
            }
        }

        /// <summary>
        /// Evaluates every source=graph <see cref="PresenterParamBinding"/> of a definition in binding order.
        /// Each evaluation seeds the graph input registers from the current Param Blackboard, so a later
        /// binding observes the value an earlier binding wrote in the same pass.
        /// </summary>
        private void ApplyGraphParamBindings(Entity entity, Entity owner, PresenterDefinition definition)
        {
            int[] graphBindingIndices = definition.GraphParamBindingIndices;
            for (int i = 0; i < graphBindingIndices.Length; i++)
            {
                int bindingIndex = graphBindingIndices[i];
                ref readonly PresenterParamBinding binding = ref definition.Bindings[bindingIndex];
                EvaluateGraphParamBinding(entity, owner, definition, bindingIndex, in binding);
            }
        }

        /// <summary>
        /// Input contract for a source=graph binding's program (<see cref="ValueSourceKind.Graph"/>):
        /// E[0]=owner, E[1]=presenter; F[k] for k&gt;=1 is seeded from the presenter Param Blackboard
        /// float lane key k (resolver order: override, default, parent chain); F[0] is the float result
        /// register written back to <see cref="PresenterParamBinding.ParamKey"/> on the Float lane.
        /// Unseeded registers hold a NaN sentinel so an incomplete input surfaces as a NaN result.
        /// </summary>
        private void EvaluateGraphParamBinding(
            Entity entity,
            Entity owner,
            PresenterDefinition definition,
            int bindingIndex,
            in PresenterParamBinding binding)
        {
            if (_graphPrograms == null || _graphApi == null)
            {
                throw new InvalidOperationException(
                    $"Presenter '{definition.Key}' bindings[{bindingIndex}] uses source=graph, but the presenter behavior system is not configured with a graph program registry and graph runtime API.");
            }

            int graphProgramId = binding.Value.SourceId;
            string bindingPath = $"presenter '{definition.Key}' bindings[{bindingIndex}] paramKey={binding.ParamKey} source=graph sourceId={graphProgramId}";
            if (!_graphPrograms.TryGetProgram(graphProgramId, out ReadOnlySpan<GraphInstruction> program) ||
                program.Length == 0)
            {
                WarnGraphBinding(entity, bindingIndex, $"{bindingPath}: graph program id {graphProgramId} is not registered; the blackboard value is left unchanged.");
                return;
            }

            float result;
            try
            {
                _graphPrograms.RequireKind(graphProgramId, GraphKind.Score);
                if (!ProgramWritesFloatResultRegister(program))
                {
                    WarnGraphBinding(entity, bindingIndex, $"{bindingPath}: graph program id {graphProgramId} never writes the float result register F[0]; the blackboard value is left unchanged.");
                    return;
                }

                SeedGraphInputRegisters(entity);
                GraphFrame frame = GraphFrame.Bind(
                    GraphKind.Score,
                    GraphEntityPreset.None,
                    World,
                    owner,
                    entity,
                    IntVector2.Zero,
                    _graphApi,
                    _graphPrograms,
                    _graphFloatRegs,
                    _graphIntRegs,
                    _graphBoolRegs,
                    _graphEntityRegs,
                    _graphTargets,
                    _graphCallStack);
                GraphExecutor.Execute(ref frame, program);
                result = _graphFloatRegs[0];
            }
            catch (InvalidOperationException exception)
            {
                WarnGraphBinding(entity, bindingIndex, $"{bindingPath}: graph evaluation failed ({exception.Message}); the blackboard value is left unchanged.");
                return;
            }

            if (float.IsNaN(result))
            {
                WarnGraphBinding(entity, bindingIndex, $"{bindingPath}: graph input incomplete (an input register F[k] has no Param Blackboard key k); the blackboard value is left unchanged.");
                return;
            }

            SetParam(entity, binding.ParamKey, ParamLane.Float, result, 0, Vector4.Zero);
        }

        private unsafe void SeedGraphInputRegisters(Entity entity)
        {
            for (int i = 0; i < _graphFloatRegs.Length; i++)
            {
                _graphFloatRegs[i] = float.NaN;
            }

            Array.Clear(_graphIntRegs, 0, _graphIntRegs.Length);
            Array.Clear(_graphBoolRegs, 0, _graphBoolRegs.Length);
            Array.Clear(_graphEntityRegs, 0, _graphEntityRegs.Length);
            Array.Clear(_graphCallStack, 0, _graphCallStack.Length);

            Entity current = entity;
            while (World.IsAlive(current))
            {
                if (World.Has<PresenterFloatParams>(current))
                {
                    ref var overrides = ref World.Get<PresenterFloatParams>(current);
                    fixed (int* keys = overrides.Keys)
                    fixed (float* values = overrides.Values)
                    {
                        SeedGraphInputRegisterEntries(overrides.Count, keys, values);
                    }
                }

                if (World.Has<PresenterFloatDefaults>(current))
                {
                    ref var defaults = ref World.Get<PresenterFloatDefaults>(current);
                    fixed (int* keys = defaults.Keys)
                    fixed (float* values = defaults.Values)
                    {
                        SeedGraphInputRegisterEntries(defaults.Count, keys, values);
                    }
                }

                if (!World.Has<PresenterParent>(current))
                {
                    break;
                }

                current = World.Get<PresenterParent>(current).Parent;
            }
        }

        private unsafe void SeedGraphInputRegisterEntries(int count, int* keys, float* values)
        {
            for (int i = 0; i < count; i++)
            {
                int key = keys[i];
                if (key >= 1 && key < _graphFloatRegs.Length && float.IsNaN(_graphFloatRegs[key]))
                {
                    _graphFloatRegs[key] = values[i];
                }
            }
        }

        private static bool ProgramWritesFloatResultRegister(ReadOnlySpan<GraphInstruction> program)
        {
            for (int i = 0; i < program.Length; i++)
            {
                ref readonly GraphInstruction instruction = ref program[i];
                if (instruction.Dst != 0)
                {
                    continue;
                }

                if (!GraphOpDescriptorTable.TryGet((GraphNodeOp)instruction.Op, out GraphOpDescriptor descriptor) ||
                    descriptor.LinearOutputType == GraphValueType.Float)
                {
                    return true;
                }
            }

            return false;
        }

        private void WarnGraphBinding(Entity entity, int bindingIndex, string reason)
        {
            if (!_warnedGraphBindings.Add(((long)entity.Id << 32) | (uint)bindingIndex))
            {
                return;
            }

            Log.Warn(in LogChannels.Presentation, reason);
        }

        private void ApplyOwnerFacingBindings(Entity entity, Entity owner, PresenterDefinition definition)
        {
            int[] bindingIndices = definition.OwnerFacingParamBindingIndices;
            if (bindingIndices.Length == 0)
            {
                return;
            }

            float facingRad = ResolveFacingRadians(owner);
            for (int i = 0; i < bindingIndices.Length; i++)
            {
                ref readonly PresenterParamBinding binding = ref definition.Bindings[bindingIndices[i]];
                switch (binding.Value.Source)
                {
                    case ValueSourceKind.FacingRadians:
                        SetParam(entity, binding.ParamKey, ParamLane.Float, facingRad, 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.FacingDegrees:
                        SetParam(entity, binding.ParamKey, ParamLane.Float, VisualMath.RadToDegValue(facingRad), 0, Vector4.Zero);
                        break;
                }
            }
        }

        private void ApplyCompiledDirtyBindings(
            Entity entity,
            Entity owner,
            PresenterDefinition definition,
            uint activeMask,
            bool applyAttributes,
            bool applyTags)
        {
            CompiledBinding[] compiled = definition.CompiledBindings;
            if (compiled.Length == 0)
            {
                return;
            }

            bool ownerAlive = World.IsAlive(owner);
            bool hasAttributes = applyAttributes && ownerAlive && World.Has<AttributeBuffer>(owner);
            bool hasTags = applyTags && ownerAlive && World.Has<GameplayTagContainer>(owner);
            if (!hasAttributes && !applyTags)
            {
                return;
            }

            for (int i = 0; i < compiled.Length; i++)
            {
                ref readonly CompiledBinding binding = ref compiled[i];
                if (!IsBehaviorActive(activeMask, binding.SlotIndex))
                {
                    continue;
                }

                if (applyAttributes && binding.IsAttributeBound && hasAttributes)
                {
                    ApplyCompiledAttributeBinding(entity, ref World.Get<AttributeBuffer>(owner), in binding);
                    continue;
                }

                if (applyTags && binding.IsTagBound)
                {
                    bool tagActive = hasTags && World.Get<GameplayTagContainer>(owner).HasTag(binding.SourceTagId);
                    ApplyCompiledTagBinding(entity, in binding, tagActive);
                }
            }
        }

        private void ApplyOwnerAttributeWork(
            Entity entity,
            PresenterDefinition definition,
            ref AttributeBuffer attributes,
            in PresenterDefinition.OwnerAttributeWorkItem work)
        {
            uint activeMask = World.Get<PresenterState>(entity).BehaviorActiveMask;
            CompiledBinding[] compiled = definition.CompiledBindings;
            int[] compiledBindingIndices = work.CompiledBindingIndices;
            for (int i = 0; i < compiledBindingIndices.Length; i++)
            {
                int compiledIndex = compiledBindingIndices[i];
                if ((uint)compiledIndex >= (uint)compiled.Length)
                {
                    throw new InvalidOperationException(
                        $"Presenter '{definition.Key}' compiled attribute work points at missing CompiledBinding[{compiledIndex}].");
                }

                ref readonly CompiledBinding binding = ref compiled[compiledIndex];
                if (!IsBehaviorActive(activeMask, binding.SlotIndex))
                {
                    continue;
                }

                ApplyCompiledAttributeBinding(entity, ref attributes, in binding);
            }
        }

        private void ApplyOwnerTagWork(
            Entity entity,
            PresenterDefinition definition,
            in PresenterDefinition.OwnerTagWorkItem work,
            bool tagActive)
        {
            uint activeMask = World.Get<PresenterState>(entity).BehaviorActiveMask;
            CompiledBinding[] compiled = definition.CompiledBindings;
            int[] compiledBindingIndices = work.CompiledBindingIndices;
            for (int i = 0; i < compiledBindingIndices.Length; i++)
            {
                int compiledIndex = compiledBindingIndices[i];
                if ((uint)compiledIndex >= (uint)compiled.Length)
                {
                    throw new InvalidOperationException(
                        $"Presenter '{definition.Key}' compiled tag work points at missing CompiledBinding[{compiledIndex}].");
                }

                ref readonly CompiledBinding binding = ref compiled[compiledIndex];
                if (!IsBehaviorActive(activeMask, binding.SlotIndex))
                {
                    continue;
                }

                ApplyCompiledTagBinding(entity, in binding, tagActive);
            }
        }

        private void ApplyCompiledAttributeBinding(Entity entity, ref AttributeBuffer attributes, in CompiledBinding binding)
        {
            float value = ResolveAttributeValue(ref attributes, binding.SourceAttributeId, binding.Mode);
            SetParam(entity, binding.TargetParamKey, ParamLane.Float, value, 0, Vector4.Zero);

            if (binding.TrySelectThreshold(value, out ThresholdMapping threshold))
            {
                int thresholdIntValue = (int)threshold.OutputValue;
                SetParam(entity, threshold.OutputParamKey, ParamLane.Float, threshold.OutputValue, thresholdIntValue, Vector4.Zero);
                SetParam(entity, threshold.OutputParamKey, ParamLane.Int, threshold.OutputValue, thresholdIntValue, Vector4.Zero);
                return;
            }

            ThresholdMapping[] thresholds = binding.Thresholds;
            bool hasFloatParams = World.Has<PresenterFloatParams>(entity);
            bool hasIntParams = World.Has<PresenterIntParams>(entity);
            for (int i = 0; i < thresholds.Length; i++)
            {
                ref readonly ThresholdMapping unused = ref thresholds[i];
                if (hasFloatParams)
                {
                    ClearParam(entity, unused.OutputParamKey, ParamLane.Float);
                }

                if (hasIntParams)
                {
                    ClearParam(entity, unused.OutputParamKey, ParamLane.Int);
                }
            }
        }

        private void ApplyCompiledTagBinding(Entity entity, in CompiledBinding binding, bool tagActive)
        {
            SetParam(entity, binding.TargetParamKey, ParamLane.Int, 0f, binding.ResolveTagInt(tagActive), Vector4.Zero);
        }

        private void ApplyMaterialBinding(Entity entity, in MaterialConfig config)
        {
            if (config.MaterialSwapParamKey < 0)
            {
                return;
            }

            if (!_runtime.TryResolveFloat(entity, config.MaterialSwapParamKey, out float paramValue))
            {
                throw new InvalidOperationException(
                    $"Material behavior materialSwapParamKey {config.MaterialSwapParamKey} did not resolve to a swap value.");
            }

            MaterialSwapEntry[] swapTable = config.SwapTable ?? Array.Empty<MaterialSwapEntry>();
            for (int i = 0; i < swapTable.Length; i++)
            {
                ref readonly MaterialSwapEntry entry = ref swapTable[i];
                if (MathF.Abs(entry.ParamValue - paramValue) <= 0.0001f)
                {
                    SetParam(entity, config.MaterialSwapParamKey, ParamLane.Int, 0f, entry.MaterialId, Vector4.Zero);
                    return;
                }
            }

            throw new InvalidOperationException(
                $"Material behavior materialSwapParamKey {config.MaterialSwapParamKey} resolved value {paramValue} with no matching swapTable entry.");
        }
        private void ApplySound(Entity entity, in PresenterState state, in BehaviorSlot slot)
        {
            if (slot.Sound.SoundAssetId <= 0) return;
            float volume = slot.Sound.Volume;
            if (slot.Sound.VolumeParamKey >= 0)
                volume = RequireFloatParam(entity, slot.Sound.VolumeParamKey, "Sound.volumeParamKey");
            Vector3 worldPos = World.Has<PresenterWorldPosition>(entity) ? World.Get<PresenterWorldPosition>(entity).Value : Vector3.Zero;
            _soundRequests.Add(new SoundRequest
            {
                Kind = SoundRequestKind.PlayOrUpdate,
                StableId = PresenterBehaviorRuntimeUtility.ComposeBehaviorStableId(state.StableId, slot.SlotIndex),
                SoundAssetId = slot.Sound.SoundAssetId,
                Loop = slot.Sound.Loop,
                Volume = Math.Clamp(volume, 0f, 1f),
                WorldPosition = worldPos,
                Owner = state.OwnerEntity,
            });
        }

        private static Dictionary<int, OwnerAttributeWorkTarget[]> BuildOwnerAttributeWorkIndex(PresenterDefinitionRegistry definitions)
        {
            var buckets = new Dictionary<int, List<OwnerAttributeWorkTarget>>();
            IReadOnlyList<int> registeredIds = definitions.RegisteredIds;
            for (int i = 0; i < registeredIds.Count; i++)
            {
                int definitionId = registeredIds[i];
                if (!definitions.TryGet(definitionId, out PresenterDefinition definition) ||
                    !definition.HasOwnerAttributeBindingWork)
                {
                    continue;
                }

                PresenterDefinition.OwnerAttributeWorkItem[] workItems = definition.OwnerAttributeWork;
                for (int workIndex = 0; workIndex < workItems.Length; workIndex++)
                {
                    ref readonly PresenterDefinition.OwnerAttributeWorkItem work = ref workItems[workIndex];
                    if (!buckets.TryGetValue(work.AttributeId, out List<OwnerAttributeWorkTarget>? bucket))
                    {
                        bucket = new List<OwnerAttributeWorkTarget>(1);
                        buckets.Add(work.AttributeId, bucket);
                    }

                    bucket.Add(new OwnerAttributeWorkTarget(definitionId, definition, in work));
                }
            }

            return FreezeBuckets(buckets);
        }

        private static Dictionary<int, OwnerTagWorkTarget[]> BuildOwnerTagWorkIndex(PresenterDefinitionRegistry definitions)
        {
            var buckets = new Dictionary<int, List<OwnerTagWorkTarget>>();
            IReadOnlyList<int> registeredIds = definitions.RegisteredIds;
            for (int i = 0; i < registeredIds.Count; i++)
            {
                int definitionId = registeredIds[i];
                if (!definitions.TryGet(definitionId, out PresenterDefinition definition) ||
                    !definition.HasOwnerTagBindingWork)
                {
                    continue;
                }

                PresenterDefinition.OwnerTagWorkItem[] workItems = definition.OwnerTagWork;
                for (int workIndex = 0; workIndex < workItems.Length; workIndex++)
                {
                    ref readonly PresenterDefinition.OwnerTagWorkItem work = ref workItems[workIndex];
                    if (!buckets.TryGetValue(work.TagId, out List<OwnerTagWorkTarget>? bucket))
                    {
                        bucket = new List<OwnerTagWorkTarget>(1);
                        buckets.Add(work.TagId, bucket);
                    }

                    bucket.Add(new OwnerTagWorkTarget(definitionId, definition, in work));
                }
            }

            var frozen = new Dictionary<int, OwnerTagWorkTarget[]>(buckets.Count);
            foreach ((int key, List<OwnerTagWorkTarget> value) in buckets)
            {
                frozen[key] = value.ToArray();
            }

            return frozen;
        }

        private static Dictionary<int, OwnerAttributeWorkTarget[]> FreezeBuckets(Dictionary<int, List<OwnerAttributeWorkTarget>> buckets)
        {
            var frozen = new Dictionary<int, OwnerAttributeWorkTarget[]>(buckets.Count);
            foreach ((int key, List<OwnerAttributeWorkTarget> value) in buckets)
            {
                frozen[key] = value.ToArray();
            }

            return frozen;
        }

        private void StopInactiveSounds(Entity entity, in PresenterState state, BehaviorSlot[] behaviors, uint currentSoundMask)
        {
            if (!_soundTracking.TryGetValue(entity.Id, out var prev)) return;
            uint stopMask = prev.ActiveMask & ~currentSoundMask;
            if (stopMask == 0u) return;
            Vector3 worldPos = World.Has<PresenterWorldPosition>(entity) ? World.Get<PresenterWorldPosition>(entity).Value : Vector3.Zero;
            EmitStopRequests(stopMask, behaviors, state.StableId, state.OwnerEntity, worldPos);
        }

        private int StopDestroyedSounds()
        {
            if (_soundTracking.Count == 0)
            {
                return 0;
            }

            ReadOnlySpan<PresentationEvent> events = _events.GetSpan();
            int scanned = 0;
            for (int i = 0; i < events.Length; i++)
            {
                scanned++;
                ref readonly PresentationEvent evt = ref events[i];
                if (evt.Kind != PresentationEventKind.PresenterDestroyed) continue;
                Entity presenter = evt.PresenterEntity;
                if (presenter == Entity.Null || !_soundTracking.TryGetValue(presenter.Id, out var prev)) continue;
                int stableId = (int)evt.Magnitude;
                if (prev.StableId != stableId) continue;
                if (prev.ActiveMask == 0u || !_definitions.TryGet(evt.KeyId, out PresenterDefinition definition))
                {
                    _soundTracking.Remove(presenter.Id);
                    continue;
                }
                EmitStopRequests(prev.ActiveMask, definition.Behaviors, stableId, evt.Source, Vector3.Zero);
                _soundTracking.Remove(presenter.Id);
            }

            return scanned;
        }

        private void HandleReusedSoundSlot(Entity entity, in PresenterState state, BehaviorSlot[] _)
        {
            if (!_soundTracking.TryGetValue(entity.Id, out var prev)) return;
            if (prev.StableId == 0 || prev.StableId == state.StableId) return;
            if (prev.ActiveMask != 0u && prev.DefinitionId > 0 &&
                _definitions.TryGet(prev.DefinitionId, out PresenterDefinition previousDefinition))
            {
                Vector3 worldPos = World.Has<PresenterWorldPosition>(entity) ? World.Get<PresenterWorldPosition>(entity).Value : Vector3.Zero;
                EmitStopRequests(prev.ActiveMask, previousDefinition.Behaviors, prev.StableId, state.OwnerEntity, worldPos);
            }
            _soundTracking.Remove(entity.Id);
        }

        private void EmitStopRequests(uint stopMask, BehaviorSlot[] behaviors, int stableId, Entity owner, Vector3 worldPosition)
        {
            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (slot.Kind != BehaviorKind.Sound || slot.Sound.SoundAssetId <= 0 ||
                    slot.SlotIndex < 0 || slot.SlotIndex >= 32 || (stopMask & (1u << slot.SlotIndex)) == 0)
                    continue;
                _soundRequests.Add(new SoundRequest
                {
                    Kind = SoundRequestKind.Stop,
                    StableId = PresenterBehaviorRuntimeUtility.ComposeBehaviorStableId(stableId, slot.SlotIndex),
                    SoundAssetId = slot.Sound.SoundAssetId,
                    Owner = owner,
                    WorldPosition = worldPosition,
                });
            }
        }

        private int CountActiveSoundTrackingPresenters()
        {
            return _soundTracking.Count;
        }

        private float RequireFloatParam(Entity entity, int paramKey, string context)
        {
            if (!_runtime.TryResolveFloat(entity, paramKey, out float value))
            {
                throw new InvalidOperationException($"{context} {paramKey} did not resolve to a float param value.");
            }

            return value;
        }

        private void ApplySpline(Entity entity, ref PresenterState state, in SplineConfig config, float dt)
        {
            if (config.Usage != SplineUsage.Patrol)
            {
                return;
            }

            if (config.ProgressParamKey < 0)
            {
                throw new InvalidOperationException("Spline usage 'Patrol' requires explicit progressParamKey.");
            }

            float progress = RequireFloatParam(entity, config.ProgressParamKey, "Spline.progressParamKey");
            float speed = config.SpeedParamKey >= 0
                ? RequireFloatParam(entity, config.SpeedParamKey, "Spline.speedParamKey")
                : 0f;
            progress += dt * speed;
            if (config.Loop) progress = progress - MathF.Floor(progress);
            else progress = Math.Clamp(progress, 0f, 1f);
            _runtime.SetParam(entity, config.ProgressParamKey, ParamLane.Float, progress, 0, Vector4.Zero);
            if (World.Has<PresenterTransformSource>(entity))
            {
                ref var ts = ref World.Get<PresenterTransformSource>(entity);
                ts.Value = TransformSource.SplineDriven;
            }
            if (World.Has<PresenterWorldPosition>(entity))
            {
                ref var pos = ref World.Get<PresenterWorldPosition>(entity);
                pos.Value = new Vector3(progress, pos.Value.Y, 0f);
                SyncPlanePosition(entity, in pos.Value);
            }
            if (World.Has<PresenterWorldRotation>(entity))
            {
                ref var rot = ref World.Get<PresenterWorldRotation>(entity);
                rot.Value = WorldPlane2D.FacingRadToVisualYRotation(0f);
            }
            if (World.Has<PresenterWorldFacing>(entity))
            {
                ref var facing = ref World.Get<PresenterWorldFacing>(entity);
                facing.AngleRad = 0f;
                facing.HasValue = 1;
            }
        }

        private void ApplyAttachment(Entity entity, in AttachmentConfig config)
        {
            Entity parentEntity = World.Get<PresenterParent>(entity).Parent;
            if (parentEntity == Entity.Null || !World.IsAlive(parentEntity)) return;
            switch (config.Target)
            {
                case AttachmentTarget.Parent:
                    Vector3 parentPos = World.Has<PresenterWorldPosition>(parentEntity) ? World.Get<PresenterWorldPosition>(parentEntity).Value : Vector3.Zero;
                    Quaternion parentRot = World.Has<PresenterWorldRotation>(parentEntity) ? World.Get<PresenterWorldRotation>(parentEntity).Value : Quaternion.Identity;
                    PresenterWorldFacing parentFacing = World.Has<PresenterWorldFacing>(parentEntity) ? World.Get<PresenterWorldFacing>(parentEntity) : default;
                    Vector3 parentScale = World.Has<PresenterWorldScale>(parentEntity) ? World.Get<PresenterWorldScale>(parentEntity).Value : Vector3.One;
                    ApplyParentAttachment(entity, parentPos, parentRot, parentFacing, parentScale, config);
                    return;
                case AttachmentTarget.Bone:
                    if (!World.Has<PresenterState>(parentEntity))
                    {
                        WarnBoneAttachment(entity, _warnedBoneAttachmentResolveFailed, "parent presenter state is missing");
                        return;
                    }

                    ApplyBoneAttachment(entity, World.Get<PresenterState>(parentEntity).StableId, config);
                    return;
            }
        }

        private void ApplyParentAttachment(
            Entity entity,
            Vector3 parentPos,
            Quaternion parentRot,
            in PresenterWorldFacing parentFacing,
            Vector3 parentScale,
            in AttachmentConfig config)
        {
            Quaternion normalizedParentRot = VisualMath.NormalizeOrIdentity(parentRot);
            Vector3 normalizedParentScale = VisualMath.NormalizeScale(parentScale);
            Vector3 scaledOffset = config.InheritScale ? normalizedParentScale * config.Offset : config.Offset;
            SetTransform(entity, TransformSource.AttachedToParent,
                parentPos + Vector3.Transform(scaledOffset, normalizedParentRot),
                VisualMath.NormalizeOrIdentity(normalizedParentRot * VisualMath.NormalizeOrIdentity(config.RotationOffset)),
                config.InheritScale ? normalizedParentScale : Vector3.One,
                parentFacing);
        }

        private void ApplyBoneAttachment(Entity entity, int parentStableId, in AttachmentConfig config)
        {
            IBoneTransformProvider? boneTransformProvider = _boneTransformProvider();
            if (boneTransformProvider == null)
            {
                WarnBoneAttachment(entity, _warnedBoneAttachmentProviderMissing, $"bone provider is not registered for parentStableId={parentStableId}, boneId={config.BoneId}");
                return;
            }

            if (config.BoneId <= 0)
            {
                WarnBoneAttachment(entity, _warnedBoneAttachmentInvalidBone, $"invalid boneId={config.BoneId} for parentStableId={parentStableId}");
                return;
            }

            if (!boneTransformProvider.TryGetBoneWorldTransform(parentStableId, config.BoneId,
                    out Vector3 bonePosition, out Quaternion boneRotation, out Vector3 boneScale))
            {
                WarnBoneAttachment(entity, _warnedBoneAttachmentResolveFailed, $"bone transform could not be resolved for parentStableId={parentStableId}, boneId={config.BoneId}");
                return;
            }

            Quaternion normalizedBoneRotation = VisualMath.NormalizeOrIdentity(boneRotation);
            SetTransform(entity, TransformSource.BoneAttached,
                bonePosition + Vector3.Transform(config.Offset, normalizedBoneRotation),
                VisualMath.NormalizeOrIdentity(normalizedBoneRotation * VisualMath.NormalizeOrIdentity(config.RotationOffset)),
                config.InheritScale ? VisualMath.NormalizeScale(boneScale) : Vector3.One,
                World.Has<PresenterWorldFacing>(entity) ? World.Get<PresenterWorldFacing>(entity) : default);
        }

        private static void WarnBoneAttachment(Entity entity, HashSet<int> once, string reason)
        {
            if (!once.Add(entity.Id))
            {
                return;
            }

            Log.Warn(
                in LogChannels.Presentation,
                $"Presenter bone attachment skipped for presenterEntityId={entity.Id}: {reason}. Parent-position substitution is not applied.");
        }

        private void ApplyGrounding(Entity entity, in GroundingConfig config)
        {
            if (config.Mode == GroundingMode.None || !World.Has<PresenterWorldPosition>(entity))
            {
                return;
            }

            if (World.Has<PresenterState>(entity) &&
                ShouldSkipOwnerBackedSnapToGround(in World.Get<PresenterState>(entity), config, entity))
            {
                return;
            }

            IVisualHeightmap? heightmap = _heightmapProvider();
            bool requireResolvedSample = config.UpdatePolicy == GroundingUpdatePolicy.Once;
            if (heightmap == null)
            {
                WarnMissingGroundingHeightmap();
                if (requireResolvedSample)
                {
                    MarkBootstrapGroundingDeferred(entity);
                    return;
                }

                SetGroundingMissingHeightmapHeight(entity, config.Offset);
                return;
            }

            ref PresenterWorldPosition position = ref World.Get<PresenterWorldPosition>(entity);
            if (config.Mode == GroundingMode.SnapToGround)
            {
                if (!TrySnapToGroundSingle(ref position.Value, config.Offset, heightmap, requireResolvedSample))
                {
                    MarkBootstrapGroundingDeferred(entity);
                }

                return;
            }

            Span<Vector3> positions = stackalloc Vector3[1] { position.Value };
            Span<GroundingMode> modes = stackalloc GroundingMode[1] { config.Mode };
            Span<float> offsets = stackalloc float[1] { config.Offset };
            if (config.Mode == GroundingMode.AlignToSurface && World.Has<PresenterWorldRotation>(entity))
            {
                ref PresenterWorldRotation rotation = ref World.Get<PresenterWorldRotation>(entity);
                Span<Quaternion> rotations = stackalloc Quaternion[1] { rotation.Value };
                PresenterGroundingUtility.ResolveBatch(positions, rotations, modes, offsets, heightmap);
                rotation.Value = rotations[0];
            }
            else
            {
                PresenterGroundingUtility.ResolveBatch(positions, modes, offsets, heightmap);
            }

            position.Value = positions[0];
        }

        private static bool TrySnapToGroundSingle(
            ref Vector3 position,
            float offsetMeters,
            IVisualHeightmap heightmap,
            bool requireResolvedSample)
        {
            const float metersToCm = 100f;
            const float cmToMeters = 0.01f;
            if (!heightmap.TrySampleHeightCm(position.X * metersToCm, position.Z * metersToCm, out float heightCm) ||
                !float.IsFinite(heightCm))
            {
                if (requireResolvedSample)
                {
                    return false;
                }

                position.Y = offsetMeters;
                return true;
            }

            position.Y = (heightCm * cmToMeters) + offsetMeters;
            return true;
        }

        private void WarnMissingGroundingHeightmap()
        {
            if (_warnedMissingGroundingHeightmap)
            {
                return;
            }

            Log.Warn(in LogChannels.Presentation, "Presenter grounding requested VisualHeightmap, but none is registered; one-shot grounding remains pending and every-frame grounding uses offset height.");
            _warnedMissingGroundingHeightmap = true;
        }

        private void MarkBootstrapGroundingDeferred(Entity entity)
        {
            if (!_bootstrapPassActive)
            {
                return;
            }

            _bootstrapGroundingDeferredEntityIds.Add(entity.Id);
        }

        private void ResolveMissingBootstrapGroundingBatch(
            Span<PresenterWorldPosition> positions,
            Span<PresenterState> states,
            int[] behaviorIndices,
            BehaviorSlot[] behaviors,
            Chunk chunk)
        {
            if (!_bootstrapPassActive)
            {
                return;
            }

            ref Entity entityFirst = ref chunk.Entity(0);
            for (int behaviorIndex = 0; behaviorIndex < behaviorIndices.Length; behaviorIndex++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[behaviorIndices[behaviorIndex]];
                if (slot.Kind != BehaviorKind.Grounding ||
                    slot.Grounding.Mode == GroundingMode.None)
                {
                    continue;
                }

                foreach (int index in chunk)
                {
                    if (!IsBehaviorActive(states[index].BehaviorActiveMask, slot.SlotIndex))
                    {
                        continue;
                    }

                    if (slot.Grounding.UpdatePolicy == GroundingUpdatePolicy.Once)
                    {
                        Entity entity = Unsafe.Add(ref entityFirst, index);
                        _bootstrapGroundingDeferredEntityIds.Add(entity.Id);
                        continue;
                    }

                    positions[index].Value.Y = slot.Grounding.Offset;
                }
            }
        }

        private void WarnGroundingSampleFailure()
        {
            if (_warnedGroundingSampleFailure)
            {
                return;
            }

            Log.Warn(in LogChannels.Presentation, "Presenter grounding batch could not sample visual heights; one-shot grounding remains pending and every-frame grounding uses offset height.");
            _warnedGroundingSampleFailure = true;
        }

        private void SetGroundingMissingHeightmapHeight(Entity entity, float offset)
        {
            if (!World.Has<PresenterWorldPosition>(entity))
            {
                return;
            }

            ref PresenterWorldPosition position = ref World.Get<PresenterWorldPosition>(entity);
            position.Value.Y = offset;
        }

        private void SetGroundingMissingHeightmapHeightBatch(
            Span<PresenterWorldPosition> positions,
            Span<PresenterState> states,
            int[] tickBehaviorIndices,
            BehaviorSlot[] behaviors,
            Chunk chunk)
        {
            for (int behaviorIndex = 0; behaviorIndex < tickBehaviorIndices.Length; behaviorIndex++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[tickBehaviorIndices[behaviorIndex]];
                if (slot.Kind != BehaviorKind.Grounding ||
                    slot.Grounding.Mode == GroundingMode.None)
                {
                    continue;
                }

                foreach (int index in chunk)
                {
                    if (!IsBehaviorActive(states[index].BehaviorActiveMask, slot.SlotIndex))
                    {
                        continue;
                    }

                    positions[index].Value.Y = slot.Grounding.Offset;
                }
            }
        }

        private bool CanSkipOwnerBackedSnapToGround(
            in PresenterState state,
            in GroundingConfig config,
            TransformSource transformSource)
        {
            if (config.Mode != GroundingMode.SnapToGround ||
                config.Offset != 0f ||
                state.AnchorKind != PresentationAnchorKind.Entity ||
                transformSource != TransformSource.EntityTransform ||
                !OwnerHasResolvedVisualHeightSample(state.OwnerEntity))
            {
                return false;
            }

            return true;
        }

        private bool ShouldSkipOwnerBackedSnapToGround(
            in PresenterState state,
            in GroundingConfig config,
            Entity presenter)
        {
            if (config.Mode != GroundingMode.SnapToGround ||
                config.Offset != 0f ||
                state.AnchorKind != PresentationAnchorKind.Entity ||
                !World.Has<PresenterTransformSource>(presenter) ||
                World.Get<PresenterTransformSource>(presenter).Value != TransformSource.EntityTransform ||
                !OwnerHasResolvedVisualHeightSample(state.OwnerEntity))
            {
                return false;
            }

            return true;
        }

        private bool TrySkipOwnerBackedSnapToGroundBatch(
            Span<PresenterState> states,
            int[] behaviorIndices,
            BehaviorSlot[] behaviors,
            Chunk chunk)
        {
            if (behaviorIndices.Length == 0 || !chunk.Has<PresenterTransformSource>())
            {
                return false;
            }

            Span<PresenterTransformSource> transformSources = chunk.GetSpan<PresenterTransformSource>();
            bool sawGrounding = false;
            for (int i = 0; i < behaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[behaviorIndices[i]];
                if (slot.Kind != BehaviorKind.Grounding || slot.Grounding.Mode == GroundingMode.None)
                {
                    continue;
                }

                sawGrounding = true;
                if (!CanSkipOwnerBackedSnapToGroundBehavior(in slot.Grounding))
                {
                    return false;
                }

                foreach (int index in chunk)
                {
                    if (!IsBehaviorActive(states[index].BehaviorActiveMask, slot.SlotIndex))
                    {
                        continue;
                    }

                    if (!CanSkipOwnerBackedSnapToGroundPresenter(in states[index], transformSources[index].Value) ||
                        !OwnerHasResolvedVisualHeightSample(states[index].OwnerEntity))
                    {
                        return false;
                    }
                }
            }

            return sawGrounding;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CanSkipOwnerBackedSnapToGroundBehavior(in GroundingConfig config)
        {
            return config.Mode == GroundingMode.SnapToGround && config.Offset == 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CanSkipOwnerBackedSnapToGroundPresenter(
            in PresenterState state,
            TransformSource transformSource)
        {
            return state.AnchorKind == PresentationAnchorKind.Entity &&
                   transformSource == TransformSource.EntityTransform &&
                   state.OwnerEntity != Entity.Null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool OwnerHasResolvedVisualHeightSample(Entity owner)
        {
            return owner != Entity.Null &&
                   World.IsAlive(owner) &&
                   World.Has<VisualHeightmapSampleState>(owner) &&
                   World.Get<VisualHeightmapSampleState>(owner).Sampled != 0;
        }

        private void ApplyGroundingBatch(
            Span<PresenterWorldPosition> positions,
            Span<PresenterWorldRotation> rotations,
            Span<PresenterState> states,
            int[] tickBehaviorIndices,
            BehaviorSlot[] behaviors,
            IVisualHeightmap heightmap,
            Chunk chunk)
        {
            if (tickBehaviorIndices.Length == 0)
            {
                return;
            }

            foreach (int behaviorIndex in tickBehaviorIndices)
            {
                ref readonly BehaviorSlot slot = ref behaviors[behaviorIndex];
                if (slot.Kind != BehaviorKind.Grounding || slot.Grounding.Mode == GroundingMode.None)
                {
                    continue;
                }

                int count = 0;
                EnsureGroundingCapacity(chunk.Count);
                Span<PresenterTransformSource> transformSources = chunk.Has<PresenterTransformSource>()
                    ? chunk.GetSpan<PresenterTransformSource>()
                    : Span<PresenterTransformSource>.Empty;
                foreach (int index in chunk)
                {
                    if (!IsBehaviorActive(states[index].BehaviorActiveMask, slot.SlotIndex))
                    {
                        continue;
                    }

                    TransformSource transformSource = transformSources.IsEmpty
                        ? TransformSource.WorldFixed
                        : transformSources[index].Value;
                    if (CanSkipOwnerBackedSnapToGround(in states[index], slot.Grounding, transformSource))
                    {
                        continue;
                    }

                    _groundingIndices[count] = index;
                    _groundingPositions[count] = positions[index].Value;
                    _groundingRotations[count] = rotations[index].Value;
                    _groundingModes[count] = slot.Grounding.Mode;
                    _groundingOffsets[count] = slot.Grounding.Offset;
                    count++;
                }

                if (count == 0)
                {
                    continue;
                }

                if (slot.Grounding.Mode == GroundingMode.SnapToGround)
                {
                    ApplySnapToGroundBatch(count, heightmap, requireResolvedSample: false, out _);
                }
                else
                {
                    PresenterGroundingUtility.ResolveBatch(
                        _groundingPositions.AsSpan(0, count),
                        _groundingRotations.AsSpan(0, count),
                        _groundingModes.AsSpan(0, count),
                        _groundingOffsets.AsSpan(0, count),
                        heightmap);
                }

                for (int i = 0; i < count; i++)
                {
                    int index = _groundingIndices[i];
                    positions[index].Value = _groundingPositions[i];
                    rotations[index].Value = _groundingRotations[i];
                }
            }
        }

        /// <returns>
        /// false when any Once SnapToGround sample is unresolved
        /// (batch sample failed or non-finite); affected presenters keep bootstrap pending.
        /// </returns>
        private bool ApplyBootstrapGroundingBatch(
            Span<PresenterWorldPosition> positions,
            Span<PresenterWorldRotation> rotations,
            Span<PresenterState> states,
            int[] behaviorIndices,
            BehaviorSlot[] behaviors,
            IVisualHeightmap heightmap,
            Chunk chunk)
        {
            bool resolved = true;
            ref Entity entityFirst = ref chunk.Entity(0);
            for (int behaviorIndex = 0; behaviorIndex < behaviorIndices.Length; behaviorIndex++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[behaviorIndices[behaviorIndex]];
                if (slot.Kind != BehaviorKind.Grounding || slot.Grounding.Mode == GroundingMode.None)
                {
                    continue;
                }

                bool requireResolvedSample =
                    slot.Grounding.UpdatePolicy == GroundingUpdatePolicy.Once &&
                    slot.Grounding.Mode == GroundingMode.SnapToGround;

                int count = 0;
                EnsureGroundingCapacity(chunk.Count);
                Span<PresenterTransformSource> transformSources = chunk.Has<PresenterTransformSource>()
                    ? chunk.GetSpan<PresenterTransformSource>()
                    : Span<PresenterTransformSource>.Empty;
                foreach (int index in chunk)
                {
                    if (!IsBehaviorActive(states[index].BehaviorActiveMask, slot.SlotIndex))
                    {
                        continue;
                    }

                    TransformSource transformSource = transformSources.IsEmpty
                        ? TransformSource.WorldFixed
                        : transformSources[index].Value;
                    if (CanSkipOwnerBackedSnapToGround(in states[index], slot.Grounding, transformSource))
                    {
                        continue;
                    }

                    _groundingIndices[count] = index;
                    _groundingPositions[count] = positions[index].Value;
                    _groundingRotations[count] = rotations[index].Value;
                    _groundingModes[count] = slot.Grounding.Mode;
                    _groundingOffsets[count] = slot.Grounding.Offset;
                    count++;
                }

                if (count == 0)
                {
                    continue;
                }

                if (slot.Grounding.Mode == GroundingMode.SnapToGround)
                {
                    if (!ApplySnapToGroundBatch(count, heightmap, requireResolvedSample, out bool anyUnresolved))
                    {
                        resolved = false;
                        if (requireResolvedSample && anyUnresolved)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                if (_groundingResolved[i])
                                {
                                    continue;
                                }

                                Entity entity = Unsafe.Add(ref entityFirst, _groundingIndices[i]);
                                MarkBootstrapGroundingDeferred(entity);
                            }
                        }
                    }
                    else if (requireResolvedSample)
                    {
                        // Partial non-finite: ApplySnapToGroundBatch returns false in that case.
                        // Success path: write resolved heights below.
                    }
                }
                else
                {
                    PresenterGroundingUtility.ResolveBatch(
                        _groundingPositions.AsSpan(0, count),
                        _groundingRotations.AsSpan(0, count),
                        _groundingModes.AsSpan(0, count),
                        _groundingOffsets.AsSpan(0, count),
                        heightmap);
                }

                for (int i = 0; i < count; i++)
                {
                    int index = _groundingIndices[i];
                    positions[index].Value = _groundingPositions[i];
                    rotations[index].Value = _groundingRotations[i];
                }
            }

            return resolved;
        }

        /// <returns>
        /// false when <paramref name="requireResolvedSample"/> is true and sampling fails
        /// or yields a non-finite height (positions left unchanged for unresolved entries).
        /// </returns>
        private bool ApplySnapToGroundBatch(
            int count,
            IVisualHeightmap heightmap,
            bool requireResolvedSample,
            out bool anyUnresolved)
        {
            const float metersToCm = 100f;
            const float cmToMeters = 0.01f;
            anyUnresolved = false;
            Span<float> worldXCm = _groundingWorldXCm.AsSpan(0, count);
            Span<float> worldZCm = _groundingWorldZCm.AsSpan(0, count);
            Span<float> heightsCm = _groundingHeightsCm.AsSpan(0, count);
            for (int i = 0; i < count; i++)
            {
                Vector3 position = _groundingPositions[i];
                worldXCm[i] = position.X * metersToCm;
                worldZCm[i] = position.Z * metersToCm;
            }

            if (!heightmap.SampleHeightsCm(worldXCm, worldZCm, heightsCm))
            {
                if (requireResolvedSample)
                {
                    for (int i = 0; i < count; i++)
                    {
                        _groundingResolved[i] = false;
                    }

                    anyUnresolved = true;
                    return false;
                }

                for (int i = 0; i < count; i++)
                {
                    _groundingPositions[i].Y = _groundingOffsets[i];
                }

                WarnGroundingSampleFailure();
                return true;
            }

            bool allResolved = true;
            for (int i = 0; i < count; i++)
            {
                _groundingResolved[i] = false;
                float heightCm = heightsCm[i];
                if (!float.IsFinite(heightCm))
                {
                    if (requireResolvedSample)
                    {
                        allResolved = false;
                        anyUnresolved = true;
                        continue;
                    }

                    _groundingPositions[i].Y = _groundingOffsets[i];
                    continue;
                }

                _groundingPositions[i].Y = (heightCm * cmToMeters) + _groundingOffsets[i];
                _groundingResolved[i] = true;
            }

            return allResolved;
        }

        private void ApplyParentAttachmentBatch(
            Span<PresenterWorldPosition> positions,
            Span<PresenterWorldPlanePosition> planePositions,
            Span<PresenterWorldRotation> rotations,
            Span<PresenterWorldFacing> facings,
            Span<PresenterWorldScale> scales,
            Span<PresenterTransformSource> sources,
            Span<PresenterParent> parents,
            Span<PresenterState> states,
            int slotIndex,
            in AttachmentConfig config,
            Chunk chunk)
        {
            ref Entity entityFirst = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (!IsBehaviorActive(states[index].BehaviorActiveMask, slotIndex))
                {
                    continue;
                }

                Entity parentEntity = parents[index].Parent;
                if (parentEntity == Entity.Null ||
                    !TryReadAttachmentParentTransform(
                        parentEntity,
                        out Vector3 parentPos,
                        out Quaternion parentRot,
                        out Vector3 parentScale))
                {
                    continue;
                }

                Quaternion normalizedParentRot = VisualMath.NormalizeOrIdentity(parentRot);
                Vector3 normalizedParentScale = VisualMath.NormalizeScale(parentScale);
                Vector3 scaledOffset = config.InheritScale ? normalizedParentScale * config.Offset : config.Offset;
                Vector3 resolvedPosition = parentPos + Vector3.Transform(scaledOffset, normalizedParentRot);
                Vector2 resolvedPlanePosition = WorldPlane2D.VisualMetersToLogicCm(in resolvedPosition);
                Quaternion resolvedRotation = VisualMath.NormalizeOrIdentity(normalizedParentRot * VisualMath.NormalizeOrIdentity(config.RotationOffset));
                PresenterWorldFacing resolvedFacing = World.Has<PresenterWorldFacing>(parentEntity)
                    ? World.Get<PresenterWorldFacing>(parentEntity)
                    : default;
                Vector3 resolvedScale = config.InheritScale ? normalizedParentScale : Vector3.One;
                bool changed =
                    sources[index].Value != TransformSource.AttachedToParent ||
                    positions[index].Value != resolvedPosition ||
                    planePositions[index].ValueCm != resolvedPlanePosition ||
                    rotations[index].Value != resolvedRotation ||
                    facings[index].AngleRad != resolvedFacing.AngleRad ||
                    facings[index].HasValue != resolvedFacing.HasValue ||
                    scales[index].Value != resolvedScale;
                if (!changed)
                {
                    continue;
                }

                sources[index].Value = TransformSource.AttachedToParent;
                positions[index].Value = resolvedPosition;
                planePositions[index].ValueCm = resolvedPlanePosition;
                rotations[index].Value = resolvedRotation;
                facings[index] = resolvedFacing;
                scales[index].Value = resolvedScale;
                _runtime.MarkTransformDrivenEmitDirty(Unsafe.Add(ref entityFirst, index));
            }
        }

        private bool TryReadAttachmentParentTransform(
            Entity parent,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            if (!World.IsAlive(parent) || !World.Has<PresenterWorldPosition>(parent))
            {
                position = default;
                rotation = Quaternion.Identity;
                scale = Vector3.One;
                return false;
            }

            position = World.Get<PresenterWorldPosition>(parent).Value;
            rotation = World.Has<PresenterWorldRotation>(parent)
                ? World.Get<PresenterWorldRotation>(parent).Value
                : Quaternion.Identity;
            scale = World.Has<PresenterWorldScale>(parent)
                ? World.Get<PresenterWorldScale>(parent).Value
                : Vector3.One;
            return true;
        }

        private static bool TryResolveSingleDefinitionChunk(Span<PresenterState> states, int count, out int definitionId)
        {
            definitionId = count > 0 ? states[0].DefId : -1;
            if (definitionId <= 0)
            {
                return false;
            }

            for (int i = 1; i < count; i++)
            {
                if (states[i].DefId != definitionId)
                {
                    definitionId = -1;
                    return false;
                }
            }

            return true;
        }

        private void ResolveDefaultTransformSourceBatch(
            Span<PresenterState> states,
            Span<PresenterTransformSource> sources,
            Span<PresenterParent> parents,
            Chunk chunk)
        {
            foreach (int index in chunk)
            {
                ref PresenterTransformSource source = ref sources[index];
                if (source.Value is TransformSource.BoneAttached or TransformSource.AttachedToParent)
                {
                    continue;
                }

                Entity parentEntity = parents[index].Parent;
                source.Value = parentEntity != Entity.Null && World.IsAlive(parentEntity)
                    ? TransformSource.InheritParent
                    : states[index].AnchorKind == PresentationAnchorKind.Entity
                        ? TransformSource.EntityTransform
                        : TransformSource.WorldFixed;
            }
        }

        private void ResolveTransformBatch(
            Span<PresenterWorldPosition> positions,
            Span<PresenterWorldPlanePosition> planePositions,
            Span<PresenterWorldRotation> rotations,
            Span<PresenterWorldFacing> facings,
            Span<PresenterWorldScale> scales,
            Span<PresenterTransformSource> sources,
            Span<PresenterParent> parents,
            Span<PresenterState> states,
            PresenterDefinition definition,
            Chunk chunk)
        {
            Vector3 anchorOffset = definition.PositionOffset;
            ref Entity entityFirst = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                PresenterTransformSnapshot presenterSnapshot = new PresenterTransformSnapshot
                {
                    WorldPosition = positions[index].Value,
                    WorldRotation = rotations[index].Value,
                    WorldScale = scales[index].Value,
                    WorldFacing = facings[index],
                    TransformSource = sources[index].Value,
                };

                Entity parentEntity = parents[index].Parent;
                bool hasParent = parentEntity != Entity.Null &&
                                 World.IsAlive(parentEntity) &&
                                 World.Has<PresenterState>(parentEntity);
                PresenterTransformSnapshot parentSnapshot = default;
                if (hasParent)
                {
                    parentSnapshot.WorldPosition = World.Has<PresenterWorldPosition>(parentEntity)
                        ? World.Get<PresenterWorldPosition>(parentEntity).Value
                        : Vector3.Zero;
                    parentSnapshot.WorldRotation = World.Has<PresenterWorldRotation>(parentEntity)
                        ? World.Get<PresenterWorldRotation>(parentEntity).Value
                        : Quaternion.Identity;
                    parentSnapshot.WorldScale = World.Has<PresenterWorldScale>(parentEntity)
                        ? World.Get<PresenterWorldScale>(parentEntity).Value
                        : Vector3.One;
                    parentSnapshot.WorldFacing = World.Has<PresenterWorldFacing>(parentEntity)
                        ? World.Get<PresenterWorldFacing>(parentEntity)
                        : default;
                }

                Entity ownerEntity = states[index].OwnerEntity;
                bool hasOwnerTransform = World.IsAlive(ownerEntity) && World.Has<VisualTransform>(ownerEntity);
                VisualTransform ownerTransform = hasOwnerTransform
                    ? World.Get<VisualTransform>(ownerEntity)
                    : default;

                PresenterResolvedTransform resolved = PresenterGroundingUtility.ResolveTransform(
                    presenterSnapshot,
                    parentSnapshot,
                    hasParent,
                    ownerTransform,
                    hasOwnerTransform,
                    anchorOffset,
                    ReadInstanceOverride(Unsafe.Add(ref entityFirst, index)));

                positions[index].Value = resolved.Position;
                planePositions[index].ValueCm = WorldPlane2D.VisualMetersToLogicCm(in resolved.Position);
                rotations[index].Value = resolved.Rotation;
                facings[index] = resolved.Facing;
                scales[index].Value = resolved.Scale;
            }
        }

        private static bool CanProcessBootstrapChunkFast(PresenterDefinition definition)
        {
            // Sound slots carry per-entity tracking state (_soundTracking) that only the full
            // ProcessPresenter pass maintains; default-inactive sound slots would otherwise skip
            // the StopInactiveSounds flush when a later activation is un-bootstrapped here.
            if (definition.HasSoundBehavior)
            {
                return false;
            }

            if (definition.Bindings.Length != 0 ||
                definition.MaterialBehaviorIndices.Length != 0 ||
                definition.ExtensionBootstrapBehaviorIndices.Length != 0 ||
                definition.HasOwnerAttributeBindingWork ||
                definition.HasOwnerTagBindingWork ||
                definition.HasSurfaceAuthoring)
            {
                return false;
            }

            BehaviorSlot[] behaviors = definition.Behaviors;
            uint defaultMask = BuildDefaultBehaviorMaskForFastEligibility(definition);
            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (!IsBehaviorActive(defaultMask, slot.SlotIndex))
                {
                    continue;
                }

                switch (slot.Kind)
                {
                    case BehaviorKind.AssetBinding:
                    case BehaviorKind.WorldText:
                        break;
                    case BehaviorKind.Animator:
                        break;
                    case BehaviorKind.Attachment:
                        if (slot.Attachment.Target != AttachmentTarget.Parent)
                        {
                            return false;
                        }

                        break;
                    case BehaviorKind.Grounding:
                        break;
                    default:
                        return false;
                }
            }

            return true;
        }

        private static uint BuildDefaultBehaviorMaskForFastEligibility(PresenterDefinition definition)
        {
            uint mask = 0u;
            BehaviorSlot[] behaviors = definition.Behaviors;
            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (slot.ActiveByDefault && slot.SlotIndex is >= 0 and < 32)
                {
                    mask |= 1u << slot.SlotIndex;
                }
            }

            return mask;
        }

        private static bool DefinitionHasBootstrapGroundingWork(PresenterDefinition definition)
        {
            return definition.BootstrapGroundingBehaviorIndices.Length != 0;
        }

        private static bool TryResolveParentAttachmentOnly(
            PresenterDefinition definition,
            out AttachmentConfig config,
            out int slotIndex)
        {
            config = default;
            slotIndex = -1;
            int[] tickBehaviorIndices = definition.TickBehaviorIndices;
            if (tickBehaviorIndices.Length != 1)
            {
                return false;
            }

            BehaviorSlot[] behaviors = definition.Behaviors;
            ref readonly BehaviorSlot slot = ref behaviors[tickBehaviorIndices[0]];
            if (slot.Kind != BehaviorKind.Attachment || slot.Attachment.Target != AttachmentTarget.Parent)
            {
                return false;
            }

            config = slot.Attachment;
            slotIndex = slot.SlotIndex;
            return true;
        }

        private void EnsureGroundingCapacity(int required)
        {
            if (required <= _groundingPositions.Length)
            {
                return;
            }

            int capacity = Math.Max(required, Math.Max(256, _groundingPositions.Length * 2));
            Array.Resize(ref _groundingPositions, capacity);
            Array.Resize(ref _groundingModes, capacity);
            Array.Resize(ref _groundingOffsets, capacity);
            Array.Resize(ref _groundingIndices, capacity);
            Array.Resize(ref _groundingRotations, capacity);
            Array.Resize(ref _groundingWorldXCm, capacity);
            Array.Resize(ref _groundingWorldZCm, capacity);
            Array.Resize(ref _groundingHeightsCm, capacity);
            Array.Resize(ref _groundingResolved, capacity);
        }

        private bool SetTransform(
            Entity entity,
            TransformSource source,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            in PresenterWorldFacing facing)
        {
            bool changed = false;
            if (World.Has<PresenterTransformSource>(entity))
            {
                ref PresenterTransformSource transformSource = ref World.Get<PresenterTransformSource>(entity);
                if (transformSource.Value != source)
                {
                    transformSource.Value = source;
                    changed = true;
                }
            }
            if (World.Has<PresenterWorldPosition>(entity))
            {
                ref PresenterWorldPosition worldPosition = ref World.Get<PresenterWorldPosition>(entity);
                if (worldPosition.Value != position)
                {
                    worldPosition.Value = position;
                    SyncPlanePosition(entity, in position);
                    changed = true;
                }
            }
            if (World.Has<PresenterWorldRotation>(entity))
            {
                ref PresenterWorldRotation worldRotation = ref World.Get<PresenterWorldRotation>(entity);
                if (worldRotation.Value != rotation)
                {
                    worldRotation.Value = rotation;
                    changed = true;
                }
            }
            if (World.Has<PresenterWorldFacing>(entity))
            {
                ref PresenterWorldFacing worldFacing = ref World.Get<PresenterWorldFacing>(entity);
                if (worldFacing.AngleRad != facing.AngleRad ||
                    worldFacing.HasValue != facing.HasValue)
                {
                    worldFacing = facing;
                    changed = true;
                }
            }
            if (World.Has<PresenterWorldScale>(entity))
            {
                ref PresenterWorldScale worldScale = ref World.Get<PresenterWorldScale>(entity);
                if (worldScale.Value != scale)
                {
                    worldScale.Value = scale;
                    changed = true;
                }
            }

            if (changed)
            {
                _runtime.MarkTransformDrivenEmitDirty(entity);
            }

            return changed;
        }

        private void SetParam(Entity entity, int paramKey, ParamLane lane, float floatValue, int intValue, in Vector4 vectorValue)
        {
            _runtime.SetParamAndPropagateToAffectedChildren(entity, paramKey, lane, floatValue, intValue, in vectorValue);
        }

        private void ClearParam(Entity entity, int paramKey, ParamLane lane)
        {
            _runtime.ClearParamAndPropagateToAffectedChildren(entity, paramKey, lane);
        }

        private void ResolveTransform(Entity entity, ref PresenterState state, PresenterDefinition definition, BehaviorSlot[] behaviors)
        {
            Vector3 anchorOffset = definition.PositionOffset;
            Entity parentEntity = World.Has<PresenterParent>(entity) ? World.Get<PresenterParent>(entity).Parent : Entity.Null;
            bool hasParent = parentEntity != Entity.Null && World.IsAlive(parentEntity) && World.Has<PresenterState>(parentEntity);
            PresenterTransformSnapshot parentSnapshot = default;
            if (hasParent)
            {
                parentSnapshot.WorldPosition = World.Has<PresenterWorldPosition>(parentEntity) ? World.Get<PresenterWorldPosition>(parentEntity).Value : Vector3.Zero;
                parentSnapshot.WorldRotation = World.Has<PresenterWorldRotation>(parentEntity) ? World.Get<PresenterWorldRotation>(parentEntity).Value : Quaternion.Identity;
                parentSnapshot.WorldScale = World.Has<PresenterWorldScale>(parentEntity) ? World.Get<PresenterWorldScale>(parentEntity).Value : Vector3.One;
                parentSnapshot.WorldFacing = World.Has<PresenterWorldFacing>(parentEntity) ? World.Get<PresenterWorldFacing>(parentEntity) : default;
            }

            bool hasOwnerTransform = World.IsAlive(state.OwnerEntity) && World.Has<VisualTransform>(state.OwnerEntity);
            VisualTransform ownerTransform = hasOwnerTransform ? World.Get<VisualTransform>(state.OwnerEntity) : default;

            PresenterTransformSnapshot presenterSnapshot = default;
            presenterSnapshot.WorldPosition = World.Has<PresenterWorldPosition>(entity) ? World.Get<PresenterWorldPosition>(entity).Value : Vector3.Zero;
            presenterSnapshot.WorldRotation = World.Has<PresenterWorldRotation>(entity) ? World.Get<PresenterWorldRotation>(entity).Value : Quaternion.Identity;
            presenterSnapshot.WorldScale = World.Has<PresenterWorldScale>(entity) ? World.Get<PresenterWorldScale>(entity).Value : Vector3.One;
            presenterSnapshot.WorldFacing = World.Has<PresenterWorldFacing>(entity) ? World.Get<PresenterWorldFacing>(entity) : default;
            presenterSnapshot.TransformSource = World.Has<PresenterTransformSource>(entity) ? World.Get<PresenterTransformSource>(entity).Value : TransformSource.EntityTransform;

            PresenterResolvedTransform resolved = PresenterGroundingUtility.ResolveTransform(
                presenterSnapshot, parentSnapshot, hasParent, ownerTransform, hasOwnerTransform, anchorOffset,
                ReadInstanceOverride(entity));

            if (World.Has<PresenterWorldPosition>(entity))
            {
                World.Get<PresenterWorldPosition>(entity).Value = resolved.Position;
                SyncPlanePosition(entity, in resolved.Position);
            }
            if (World.Has<PresenterWorldRotation>(entity))
                World.Get<PresenterWorldRotation>(entity).Value = resolved.Rotation;
            if (World.Has<PresenterWorldFacing>(entity))
                World.Get<PresenterWorldFacing>(entity) = resolved.Facing;
            if (World.Has<PresenterWorldScale>(entity))
                World.Get<PresenterWorldScale>(entity).Value = resolved.Scale;
        }

        private PresenterInstanceTransformOverride ReadInstanceOverride(Entity entity)
        {
            return World.Has<PresenterInstanceTransformOverride>(entity)
                ? World.Get<PresenterInstanceTransformOverride>(entity)
                : PresenterInstanceTransformOverride.Identity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SyncPlanePosition(Entity entity, in Vector3 position)
        {
            if (World.Has<PresenterWorldPlanePosition>(entity))
            {
                World.Get<PresenterWorldPlanePosition>(entity).ValueCm = WorldPlane2D.VisualMetersToLogicCm(in position);
            }
        }

        private void ResolveDefaultTransformSource(Entity entity, ref PresenterState state)
        {
            if (!World.Has<PresenterTransformSource>(entity)) return;
            ref var ts = ref World.Get<PresenterTransformSource>(entity);
            if (ts.Value is TransformSource.BoneAttached or TransformSource.AttachedToParent)
                return;
            Entity parentEntity = World.Has<PresenterParent>(entity) ? World.Get<PresenterParent>(entity).Parent : Entity.Null;
            if (parentEntity != Entity.Null && World.IsAlive(parentEntity))
            {
                ts.Value = TransformSource.InheritParent;
                return;
            }
            ts.Value = state.AnchorKind == PresentationAnchorKind.Entity
                ? TransformSource.EntityTransform
                : TransformSource.WorldFixed;
        }

        private static float ResolveAttributeValue(ref AttributeBuffer attributes, int attributeId, ValueSourceKind mode)
        {
            return mode switch
            {
                ValueSourceKind.Attribute => attributes.GetCurrent(attributeId),
                ValueSourceKind.AttributeRatio => ResolveAttributeRatio(ref attributes, attributeId),
                ValueSourceKind.AttributeBase => attributes.GetBase(attributeId),
                _ => attributes.GetCurrent(attributeId),
            };
        }

        private static float ResolveAttributeRatio(ref AttributeBuffer attributes, int attributeId)
        {
            float current = attributes.GetCurrent(attributeId);
            float max = attributes.GetBase(attributeId);
            return max <= 0f ? 0f : Math.Clamp(current / max, 0f, 1f);
        }

        private float ResolveEntityColorChannel(Entity presenter, Entity owner, int channelIndex)
        {
            Vector4 color = ResolveEntityColor(presenter, owner);
            return channelIndex switch { 0 => color.X, 1 => color.Y, 2 => color.Z, 3 => color.W, _ => 0f };
        }

        private Vector4 ResolveEntityColor(Entity presenter, Entity owner)
        {
            if (TryResolveViewerRelationshipColor(presenter, owner, out Vector4 relationshipColor))
            {
                return relationshipColor;
            }

            return World.IsAlive(owner) ? TeamColorResolver.Resolve(World, owner) : TeamColorResolver.DefaultColor;
        }

        private bool TryResolveViewerRelationshipColor(Entity presenter, Entity owner, out Vector4 color)
        {
            color = default;
            if (!World.IsAlive(presenter) ||
                !World.Has<PresenterRelationContext>(presenter) ||
                !World.IsAlive(owner))
            {
                return false;
            }

            Entity viewer = World.Get<PresenterRelationContext>(presenter).Viewer;
            if (!World.IsAlive(viewer))
            {
                return false;
            }

            PresentAudienceContext audience = _phaseResolver.CreateAudienceContext(World, viewer);
            if (!audience.HasViewerTeam && !audience.HasViewerOwner)
            {
                return false;
            }

            PresentPhaseInput input = _phaseResolver.CreateInput(World, owner, in audience, hasRelationshipLink: true);
            if (!input.HasTeamRelationship && !input.IsOwnedByAudience)
            {
                return false;
            }

            PresentPhaseResult result = _phaseResolver.Resolve(in input);
            color = result.IsOwnedByAudience
                ? TeamColorResolver.Team1Color
                : result.TeamRelationship switch
            {
                TeamRelationship.Friendly => TeamColorResolver.Team1Color,
                TeamRelationship.Hostile => TeamColorResolver.Team2Color,
                _ => TeamColorResolver.DefaultColor,
            };
            return true;
        }

        private float ResolveFacingRadians(Entity owner)
        {
            if (!World.IsAlive(owner) || !World.Has<FacingDirection>(owner)) return 0f;
            return World.Get<FacingDirection>(owner).AngleRad;
        }

        private static bool IsBehaviorActive(uint mask, int slotIndex)
        {
            return slotIndex is >= 0 and < 32 && (mask & (1u << slotIndex)) != 0;
        }

        private static bool IsAssetOutputBehavior(BehaviorKind kind)
        {
            return kind is BehaviorKind.AssetBinding or BehaviorKind.WorldText;
        }

    }
}
