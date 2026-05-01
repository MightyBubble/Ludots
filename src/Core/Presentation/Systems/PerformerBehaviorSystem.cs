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
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Presentation.Utils;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class PerformerBehaviorSystem : BaseSystem<World, float>
    {
        private readonly struct OwnerAttributeWorkTarget
        {
            public readonly int DefinitionId;
            public readonly PerformerDefinition Definition;
            public readonly PerformerDefinition.OwnerAttributeWorkItem Work;

            public OwnerAttributeWorkTarget(int definitionId, PerformerDefinition definition, in PerformerDefinition.OwnerAttributeWorkItem work)
            {
                DefinitionId = definitionId;
                Definition = definition;
                Work = work;
            }
        }

        private readonly struct OwnerTagWorkTarget
        {
            public readonly int DefinitionId;
            public readonly PerformerDefinition Definition;
            public readonly PerformerDefinition.OwnerTagWorkItem Work;

            public OwnerTagWorkTarget(int definitionId, PerformerDefinition definition, in PerformerDefinition.OwnerTagWorkItem work)
            {
                DefinitionId = definitionId;
                Definition = definition;
                Work = work;
            }
        }

        private readonly PerformerEntityRuntime _runtime;
        private readonly PerformerDefinitionRegistry _definitions;
        private readonly PresentationEventStream _events;
        private readonly PresentationOwnerChangeBuffer? _ownerChanges;
        private readonly SoundRequestBuffer _soundRequests;
        private readonly Func<IVisualHeightmap?> _heightmapProvider;
        private readonly IBoneTransformProvider? _boneTransformProvider;
        private readonly Dictionary<int, SoundTrackingState> _soundTracking = new();
        private Dictionary<int, OwnerAttributeWorkTarget[]> _ownerAttributeWorkIndex;
        private Dictionary<int, OwnerTagWorkTarget[]> _ownerTagWorkIndex;
        private int _definitionVersion = -1;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private readonly QueryDescription _bootstrapPendingQuery = new QueryDescription()
            .WithAll<PerformerState, PerformerBootstrapPending>();
        private readonly QueryDescription _dirtyOwnerAttributeQuery = new QueryDescription()
            .WithAll<GameplayAttributeChangedBits>();
        private readonly QueryDescription _dirtyOwnerTagQuery = new QueryDescription()
            .WithAll<GameplayTagEffectiveChangedBits>();
        private readonly QueryDescription _tickDrivenQuery = new QueryDescription()
            .WithAll<PerformerState, PerformerWorldPosition>()
            .WithAny<PerfHasSpline, PerfHasAttachmentTick, PerfHasGrounding, PerfHasSound>()
            .WithNone<PerformerBootstrapPending>();
        private readonly QueryDescription _materialDirtyQuery = new QueryDescription()
            .WithAll<PerformerState, PerfMaterialDirty>()
            .WithNone<PerformerBootstrapPending>();
        private readonly CommandBuffer _commandBuffer = new();
        private readonly List<Entity> _bootstrapClearList = new(256);
        private readonly List<Entity> _materialDirtyClearList = new(256);
        private Vector3[] _groundingPositions = Array.Empty<Vector3>();
        private GroundingMode[] _groundingModes = Array.Empty<GroundingMode>();
        private float[] _groundingOffsets = Array.Empty<float>();
        private int[] _groundingIndices = Array.Empty<int>();
        private Quaternion[] _groundingRotations = Array.Empty<Quaternion>();
        private float[] _groundingWorldXCm = Array.Empty<float>();
        private float[] _groundingWorldZCm = Array.Empty<float>();
        private float[] _groundingHeightsCm = Array.Empty<float>();
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

        public PerformerBehaviorSystem(
            World world,
            PerformerEntityRuntime runtime,
            PerformerDefinitionRegistry definitions,
            PresentationEventStream events,
            SoundRequestBuffer soundRequests,
            IVisualHeightmap? heightmap = null,
            IBoneTransformProvider? boneTransformProvider = null,
            PresentationTimingDiagnostics? timingDiagnostics = null)
            : this(world, runtime, definitions, events, null, soundRequests,
                () => heightmap, boneTransformProvider, timingDiagnostics)
        {
        }

        public PerformerBehaviorSystem(
            World world,
            PerformerEntityRuntime runtime,
            PerformerDefinitionRegistry definitions,
            PresentationEventStream events,
            PresentationOwnerChangeBuffer? ownerChanges,
            SoundRequestBuffer soundRequests,
            IVisualHeightmap? heightmap = null,
            IBoneTransformProvider? boneTransformProvider = null,
            PresentationTimingDiagnostics? timingDiagnostics = null)
            : this(world, runtime, definitions, events, ownerChanges, soundRequests,
                () => heightmap, boneTransformProvider, timingDiagnostics)
        {
        }

        public PerformerBehaviorSystem(
            World world,
            PerformerEntityRuntime runtime,
            PerformerDefinitionRegistry definitions,
            PresentationEventStream events,
            PresentationOwnerChangeBuffer? ownerChanges,
            SoundRequestBuffer soundRequests,
            Func<IVisualHeightmap?> heightmapProvider,
            IBoneTransformProvider? boneTransformProvider = null,
            PresentationTimingDiagnostics? timingDiagnostics = null)
            : base(world)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _ownerChanges = ownerChanges;
            _soundRequests = soundRequests ?? throw new ArgumentNullException(nameof(soundRequests));
            _heightmapProvider = heightmapProvider ?? throw new ArgumentNullException(nameof(heightmapProvider));
            _boneTransformProvider = boneTransformProvider;
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
                ProcessCreatedPerformers(dt);
                ownerChanges = ProcessOwnerChanges();
                PlaybackStructuralChanges();
                ProcessDirtyMaterialPerformers();
                tickDrivenCount = ProcessTickDrivenPerformers(dt);
                ClearProcessedBootstrapMarkers();
                ClearProcessedMaterialDirtyMarkers();
            }
            finally
            {
                _runtime.EndDeferredStructuralChanges(_commandBuffer);
            }

            PlaybackStructuralChanges();
            int destroyEventScanCount = StopDestroyedSounds();
            _ownerChanges?.Clear();

            if (_timingDiagnostics != null)
            {
                _timingDiagnostics.ObservePerformerBehaviorCounts(
                    _bootstrapClearList.Count,
                    ownerChanges,
                    _lastOwnerAttributeChangeCount,
                    _lastOwnerTagChangeCount,
                    tickDrivenCount,
                    CountActiveSoundTrackingPerformers(),
                    destroyEventScanCount);
                _timingDiagnostics.ObservePerformerBehavior((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
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

        private void ProcessCreatedPerformers(float tickDt)
        {
            _bootstrapClearList.Clear();
            foreach (ref var chunk in World.Query(in _bootstrapPendingQuery))
            {
                Span<PerformerState> states = chunk.GetSpan<PerformerState>();
                bool processedChunk = false;
                bool singleDefinitionChunk = TryResolveSingleDefinitionChunk(states, chunk.Count, out int chunkDefId);
                if (singleDefinitionChunk &&
                    _definitions.TryGet(chunkDefId, out PerformerDefinition chunkDefinition))
                {
                    processedChunk = TryProcessBootstrapChunkFast(chunk, states, chunkDefinition, tickDt);
                }

                ref Entity entityFirst = ref chunk.Entity(0);
                foreach (int index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    if (!processedChunk)
                    {
                        ProcessPerformer(
                            entity,
                            firstFrame: true,
                            updateAttributeBindings: true,
                            updateTagBindings: true,
                            tickDt,
                            tickDrivenOnly: false);
                    }

                    _bootstrapClearList.Add(entity);
                }
            }
        }

        private void ClearProcessedBootstrapMarkers()
        {
            for (int i = 0; i < _bootstrapClearList.Count; i++)
            {
                Entity entity = _bootstrapClearList[i];
                if (World.IsAlive(entity) && World.Has<PerformerBootstrapPending>(entity))
                {
                    _commandBuffer.Remove<PerformerBootstrapPending>(in entity);
                }
            }
        }

        private bool TryProcessBootstrapChunkFast(
            Chunk chunk,
            Span<PerformerState> states,
            PerformerDefinition definition,
            float tickDt)
        {
            if (!CanProcessBootstrapChunkFast(definition))
            {
                return false;
            }

            Span<PerformerWorldPosition> positions = chunk.GetSpan<PerformerWorldPosition>();
            Span<PerformerWorldRotation> rotations = chunk.GetSpan<PerformerWorldRotation>();
            Span<PerformerWorldScale> scales = chunk.GetSpan<PerformerWorldScale>();
            Span<PerformerTransformSource> sources = chunk.GetSpan<PerformerTransformSource>();
            Span<PerformerParent> parents = chunk.GetSpan<PerformerParent>();

            ResolveDefaultTransformSourceBatch(states, sources, parents, chunk);
            ResolveTransformBatch(positions, rotations, scales, sources, parents, states, definition, chunk);

            if (TryResolveParentAttachmentOnly(definition, out AttachmentConfig attachmentConfig, out int attachmentSlot))
            {
                ApplyParentAttachmentBatch(
                    positions,
                    rotations,
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

                IVisualHeightmap? heightmap = _heightmapProvider();
                if (heightmap == null)
                {
                    WarnMissingGroundingHeightmap();
                    SetBootstrapGroundingMissingHeightmapHeightBatch(
                        positions,
                        states,
                        definition.BootstrapGroundingBehaviorIndices,
                        definition.Behaviors,
                        chunk);
                }
                else
                {
                    ApplyBootstrapGroundingBatch(
                        positions,
                        rotations,
                        states,
                        definition.BootstrapGroundingBehaviorIndices,
                        definition.Behaviors,
                        heightmap,
                        chunk);
                }
            }

            return true;
        }

        private int ProcessOwnerChanges()
        {
            _lastOwnerAttributeChangeCount = 0;
            _lastOwnerTagChangeCount = 0;
            if (_ownerChanges == null)
            {
                return ProcessDirtyOwnersFromComponents();
            }

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

        private int ProcessDirtyOwnersFromComponents()
        {
            int processed = 0;
            foreach (ref Chunk chunk in World.Query(in _dirtyOwnerAttributeQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<GameplayAttributeChangedBits> changedBits = chunk.GetSpan<GameplayAttributeChangedBits>();
                foreach (int index in chunk)
                {
                    Entity owner = Unsafe.Add(ref entityFirst, index);
                    ref GameplayAttributeChangedBits bits = ref changedBits[index];
                    for (int attributeId = 0; attributeId < AttributeBuffer.MAX_ATTRS; attributeId++)
                    {
                        if (bits.IsSet(attributeId))
                        {
                            ProcessOwnerAttributeChange(owner, attributeId);
                            _lastOwnerAttributeChangeCount++;
                            processed++;
                        }
                    }
                }
            }

            foreach (ref Chunk chunk in World.Query(in _dirtyOwnerTagQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<GameplayTagEffectiveChangedBits> changedBits = chunk.GetSpan<GameplayTagEffectiveChangedBits>();
                Span<GameplayTagContainer> tagContainers = chunk.Has<GameplayTagContainer>()
                    ? chunk.GetSpan<GameplayTagContainer>()
                    : Span<GameplayTagContainer>.Empty;
                foreach (int index in chunk)
                {
                    Entity owner = Unsafe.Add(ref entityFirst, index);
                    ref GameplayTagEffectiveChangedBits bits = ref changedBits[index];
                    unsafe
                    {
                        fixed (ulong* words = bits.Bits)
                        {
                            for (int wordIndex = 0; wordIndex < 4; wordIndex++)
                            {
                                ulong word = words[wordIndex];
                                while (word != 0)
                                {
                                    int bit = BitOperations.TrailingZeroCount(word);
                                    word &= word - 1;
                                    int tagId = (wordIndex << 6) + bit;
                                    bool tagActive = !tagContainers.IsEmpty && tagContainers[index].HasTag(tagId);
                                    ProcessOwnerTagChange(owner, tagId, tagActive);
                                    _lastOwnerTagChangeCount++;
                                    processed++;
                                }
                            }
                        }
                    }
                }
            }

            return processed;
        }

        private void ProcessOwnerAttributeChange(Entity owner, int attributeId)
        {
            if (!World.IsAlive(owner) ||
                !World.Has<AttributeBuffer>(owner) ||
                !_runtime.TryGetActiveByOwner(owner, out PerformerEntityRuntime.OwnerPerformerBucket performers))
            {
                return;
            }

            ref AttributeBuffer attributes = ref World.Get<AttributeBuffer>(owner);
            if (performers.TryGetSingle(out Entity single))
            {
                ProcessOwnerAttributeWorkForPerformer(single, attributeId, ref attributes);
                return;
            }

            int count = performers.Count;
            for (int i = 0; i < count; i++)
            {
                ProcessOwnerAttributeWorkForPerformer(performers.GetAt(i), attributeId, ref attributes);
            }
        }

        private void ProcessOwnerTagChange(Entity owner, int tagId, bool? tagActiveOverride = null)
        {
            if (!World.IsAlive(owner) ||
                !_runtime.TryGetActiveByOwner(owner, out PerformerEntityRuntime.OwnerPerformerBucket performers))
            {
                return;
            }

            bool tagActive = tagActiveOverride ??
                (World.Has<GameplayTagContainer>(owner) && World.Get<GameplayTagContainer>(owner).HasTag(tagId));
            if (performers.TryGetSingle(out Entity single))
            {
                ProcessOwnerTagWorkForPerformer(single, tagId, tagActive);
                return;
            }

            int count = performers.Count;
            for (int i = 0; i < count; i++)
            {
                ProcessOwnerTagWorkForPerformer(performers.GetAt(i), tagId, tagActive);
            }
        }

        private void ProcessOwnerAttributeWorkForPerformer(
            Entity performer,
            int attributeId,
            ref AttributeBuffer attributes)
        {
            if (!World.IsAlive(performer) || !World.Has<PerformerState>(performer))
            {
                return;
            }

            ref readonly PerformerState state = ref World.Get<PerformerState>(performer);
            if (!_definitions.TryGet(state.DefId, out PerformerDefinition definition) ||
                !definition.TryGetOwnerAttributeWork(attributeId, out PerformerDefinition.OwnerAttributeWorkItem work))
            {
                return;
            }

            ApplyOwnerAttributeWork(performer, definition, ref attributes, in work);
        }

        private void ProcessOwnerTagWorkForPerformer(Entity performer, int tagId, bool tagActive)
        {
            if (!World.IsAlive(performer) || !World.Has<PerformerState>(performer))
            {
                return;
            }

            ref readonly PerformerState state = ref World.Get<PerformerState>(performer);
            if (!_definitions.TryGet(state.DefId, out PerformerDefinition definition) ||
                !definition.TryGetOwnerTagWork(tagId, out PerformerDefinition.OwnerTagWorkItem work))
            {
                return;
            }

            ApplyOwnerTagWork(performer, definition, in work, tagActive);
        }

        private int ProcessTickDrivenPerformers(float tickDt)
        {
            int processed = 0;
            foreach (ref var chunk in World.Query(in _tickDrivenQuery))
            {
                Span<PerformerState> states = chunk.GetSpan<PerformerState>();
                Span<PerformerWorldPosition> positions = chunk.GetSpan<PerformerWorldPosition>();
                PerformerDefinition? chunkDefinition = null;
                bool singleDefinitionChunk = TryResolveSingleDefinitionChunk(states, chunk.Count, out int chunkDefId);
                if (singleDefinitionChunk && !_definitions.TryGet(chunkDefId, out chunkDefinition))
                {
                    singleDefinitionChunk = false;
                }

                bool batchGrounding = singleDefinitionChunk && chunkDefinition != null && chunkDefinition.HasEveryFrameGroundingWork;
                Span<PerformerWorldRotation> rotations = batchGrounding
                    ? chunk.GetSpan<PerformerWorldRotation>()
                    : Span<PerformerWorldRotation>.Empty;
                ref Entity entityFirst = ref chunk.Entity(0);

                if (batchGrounding && chunkDefinition!.TickBehaviorsAreGroundingOnly)
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
                    TryResolveParentAttachmentOnly(chunkDefinition, out AttachmentConfig attachmentConfig, out int attachmentSlot))
                {
                    Span<PerformerParent> parents = chunk.GetSpan<PerformerParent>();
                    rotations = chunk.GetSpan<PerformerWorldRotation>();
                    Span<PerformerWorldScale> scales = chunk.GetSpan<PerformerWorldScale>();
                    Span<PerformerTransformSource> sources = chunk.GetSpan<PerformerTransformSource>();
                    ApplyParentAttachmentBatch(
                        positions,
                        rotations,
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
                    ProcessPerformer(
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
                    PerformerDefinition groundingDefinition = chunkDefinition!;
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

        private void ProcessDirtyMaterialPerformers()
        {
            _materialDirtyClearList.Clear();
            foreach (ref Chunk chunk in World.Query(in _materialDirtyQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<PerformerState> states = chunk.GetSpan<PerformerState>();
                foreach (int index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ref readonly PerformerState state = ref states[index];
                    if (_definitions.TryGet(state.DefId, out PerformerDefinition definition))
                    {
                        ProcessMaterialBehaviors(entity, in state, definition);
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

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }

        private void ProcessPerformer(
            Entity entity,
            bool firstFrame,
            bool updateAttributeBindings,
            bool updateTagBindings,
            float tickDt,
            bool tickDrivenOnly,
            bool skipGroundingBehaviors = false)
        {
            if (!World.IsAlive(entity) || !World.Has<PerformerState>(entity))
            {
                return;
            }

            ref PerformerState state = ref World.Get<PerformerState>(entity);
            if (!_definitions.TryGet(state.DefId, out PerformerDefinition definition))
            {
                return;
            }

            Entity owner = state.OwnerEntity;
            BehaviorSlot[] behaviors = definition.Behaviors;
            ResolveDefaultTransformSource(entity, ref state);
            if (!tickDrivenOnly)
            {
                ApplyBindings(entity, owner, definition);
            }

            bool hasSoundBehavior = definition.HasSoundBehavior;
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
                        case BehaviorKind.AttributeBinding:
                            if (firstFrame || updateAttributeBindings)
                                ApplyAttributeBinding(entity, owner, slot.AttributeBinding);
                            break;
                        case BehaviorKind.TagBinding:
                            if (firstFrame || updateTagBindings)
                                ApplyTagBinding(entity, owner, slot.TagBinding);
                            break;
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

            if (hasSoundBehavior)
            {
                StopInactiveSounds(entity, in state, behaviors, currentSoundMask);
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

        }

        private void ProcessMaterialBehaviors(Entity entity, in PerformerState state, PerformerDefinition definition)
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

        private void ApplyBindings(Entity entity, Entity owner, PerformerDefinition definition)
        {
            PerformerParamBinding[] bindings = definition.Bindings;
            if (bindings == null || bindings.Length == 0) return;
            bool hasAttributes = World.IsAlive(owner) && World.Has<AttributeBuffer>(owner);
            for (int i = 0; i < bindings.Length; i++)
            {
                ref readonly PerformerParamBinding binding = ref bindings[i];
                ValueRef value = binding.Value;
                switch (value.Source)
                {
                    case ValueSourceKind.EntityColor:
                        SetParam(entity, binding.ParamKey, ParamLane.Float, ResolveEntityColorChannel(owner, value.SourceId), 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.EntityColorVector:
                        SetParam(entity, binding.ParamKey, ParamLane.Vector, 0f, 0, ResolveEntityColor(owner));
                        break;
                    case ValueSourceKind.FacingRadians:
                        SetParam(entity, binding.ParamKey, ParamLane.Float, ResolveFacingRadians(owner), 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.FacingDegrees:
                        SetParam(entity, binding.ParamKey, ParamLane.Float, ResolveFacingRadians(owner) * (180f / MathF.PI), 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.Attribute:
                    case ValueSourceKind.AttributeRatio:
                    case ValueSourceKind.AttributeBase:
                        if (!hasAttributes) continue;
                        ref AttributeBuffer attributes = ref World.Get<AttributeBuffer>(owner);
                        float resolved = ResolveAttributeValue(ref attributes, value.SourceId, value.Source);
                        SetParam(entity, binding.ParamKey, ParamLane.Float, resolved, 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.Constant:
                        SetParam(entity, binding.ParamKey, ParamLane.Float, value.ConstantValue, 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.Graph:
                        break;
                }
            }
        }

        private void ApplyOwnerAttributeWork(
            Entity entity,
            PerformerDefinition definition,
            ref AttributeBuffer attributes,
            in PerformerDefinition.OwnerAttributeWorkItem work)
        {
            int[] paramBindingIndices = work.ParamBindingIndices;
            for (int i = 0; i < paramBindingIndices.Length; i++)
            {
                ref readonly PerformerParamBinding binding = ref definition.Bindings[paramBindingIndices[i]];
                float resolved = ResolveAttributeValue(ref attributes, binding.Value.SourceId, binding.Value.Source);
                SetParam(entity, binding.ParamKey, ParamLane.Float, resolved, 0, Vector4.Zero);
            }

            int[] behaviorIndices = work.BehaviorIndices;
            for (int i = 0; i < behaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref definition.Behaviors[behaviorIndices[i]];
                ApplyAttributeBinding(entity, ref attributes, in slot.AttributeBinding);
            }
        }

        private void ApplyOwnerTagWork(
            Entity entity,
            PerformerDefinition definition,
            in PerformerDefinition.OwnerTagWorkItem work,
            bool tagActive)
        {
            int[] behaviorIndices = work.BehaviorIndices;
            for (int i = 0; i < behaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref definition.Behaviors[behaviorIndices[i]];
                ApplyTagBinding(entity, in slot.TagBinding, tagActive);
            }
        }

        private void ApplyAttributeBinding(Entity entity, Entity owner, in AttributeBindingConfig config)
        {
            if (!World.IsAlive(owner) || !World.Has<AttributeBuffer>(owner)) return;
            ref AttributeBuffer attributes = ref World.Get<AttributeBuffer>(owner);
            ApplyAttributeBinding(entity, ref attributes, config);
        }

        private void ApplyTagBinding(Entity entity, Entity owner, in TagBindingConfig config)
        {
            bool active = World.IsAlive(owner) && World.Has<GameplayTagContainer>(owner) && World.Get<GameplayTagContainer>(owner).HasTag(config.TagId);
            ApplyTagBinding(entity, in config, active);
        }

        private void ApplyAttributeBinding(Entity entity, ref AttributeBuffer attributes, in AttributeBindingConfig config)
        {
            float value = ResolveAttributeValue(ref attributes, config.AttributeId, config.Mode);
            SetParam(entity, config.TargetParamKey, ParamLane.Float, value, 0, Vector4.Zero);

            ThresholdMapping[] thresholds = config.Thresholds ?? Array.Empty<ThresholdMapping>();
            for (int i = 0; i < thresholds.Length; i++)
            {
                ref readonly ThresholdMapping threshold = ref thresholds[i];
                if (value > threshold.Threshold)
                {
                    continue;
                }

                int thresholdIntValue = (int)threshold.OutputValue;
                SetParam(entity, threshold.OutputParamKey, ParamLane.Float, threshold.OutputValue, thresholdIntValue, Vector4.Zero);
                SetParam(entity, threshold.OutputParamKey, ParamLane.Int, threshold.OutputValue, thresholdIntValue, Vector4.Zero);
                return;
            }

            bool hasFloatParams = World.Has<PerformerFloatParams>(entity);
            bool hasIntParams = World.Has<PerformerIntParams>(entity);
            for (int i = 0; i < thresholds.Length; i++)
            {
                ref readonly ThresholdMapping threshold = ref thresholds[i];
                if (hasFloatParams)
                {
                    ClearParam(entity, threshold.OutputParamKey, ParamLane.Float);
                }

                if (hasIntParams)
                {
                    ClearParam(entity, threshold.OutputParamKey, ParamLane.Int);
                }
            }
        }

        private void ApplyTagBinding(Entity entity, in TagBindingConfig config, bool active)
        {
            if (config.InvertLogic)
            {
                active = !active;
            }

            SetParam(entity, config.TargetParamKey, ParamLane.Int, 0f, active ? 1 : 0, Vector4.Zero);
        }

        private void ApplyMaterialBinding(Entity entity, in MaterialConfig config)
        {
            int materialId = config.BaseMaterialId;
            if (config.MaterialSwapParamKey >= 0)
            {
                float paramValue = _runtime.ResolveFloat(entity, config.MaterialSwapParamKey, float.NaN);
                MaterialSwapEntry[] swapTable = config.SwapTable ?? Array.Empty<MaterialSwapEntry>();
                if (!float.IsNaN(paramValue))
                {
                    for (int i = 0; i < swapTable.Length; i++)
                    {
                        ref readonly MaterialSwapEntry entry = ref swapTable[i];
                        if (MathF.Abs(entry.ParamValue - paramValue) <= 0.0001f)
                        {
                            materialId = entry.MaterialId;
                            break;
                        }
                    }
                }
            }
            if (materialId > 0 && config.MaterialSwapParamKey >= 0)
                SetParam(entity, config.MaterialSwapParamKey, ParamLane.Int, 0f, materialId, Vector4.Zero);
        }
        private void ApplySound(Entity entity, in PerformerState state, in BehaviorSlot slot)
        {
            if (slot.Sound.SoundAssetId <= 0) return;
            float volume = slot.Sound.Volume;
            if (slot.Sound.VolumeParamKey >= 0)
                volume = _runtime.ResolveFloat(entity, slot.Sound.VolumeParamKey, volume);
            Vector3 worldPos = World.Has<PerformerWorldPosition>(entity) ? World.Get<PerformerWorldPosition>(entity).Value : Vector3.Zero;
            _soundRequests.Add(new SoundRequest
            {
                Kind = SoundRequestKind.PlayOrUpdate,
                StableId = PerformerBehaviorRuntimeUtility.ComposeBehaviorStableId(state.StableId, slot.SlotIndex),
                SoundAssetId = slot.Sound.SoundAssetId,
                Loop = slot.Sound.Loop,
                Volume = Math.Clamp(volume, 0f, 1f),
                WorldPosition = worldPos,
                Owner = state.OwnerEntity,
            });
        }

        private static Dictionary<int, OwnerAttributeWorkTarget[]> BuildOwnerAttributeWorkIndex(PerformerDefinitionRegistry definitions)
        {
            var buckets = new Dictionary<int, List<OwnerAttributeWorkTarget>>();
            IReadOnlyList<int> registeredIds = definitions.RegisteredIds;
            for (int i = 0; i < registeredIds.Count; i++)
            {
                int definitionId = registeredIds[i];
                if (!definitions.TryGet(definitionId, out PerformerDefinition definition) ||
                    !definition.HasOwnerAttributeBindingWork)
                {
                    continue;
                }

                PerformerDefinition.OwnerAttributeWorkItem[] workItems = definition.OwnerAttributeWork;
                for (int workIndex = 0; workIndex < workItems.Length; workIndex++)
                {
                    ref readonly PerformerDefinition.OwnerAttributeWorkItem work = ref workItems[workIndex];
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

        private static Dictionary<int, OwnerTagWorkTarget[]> BuildOwnerTagWorkIndex(PerformerDefinitionRegistry definitions)
        {
            var buckets = new Dictionary<int, List<OwnerTagWorkTarget>>();
            IReadOnlyList<int> registeredIds = definitions.RegisteredIds;
            for (int i = 0; i < registeredIds.Count; i++)
            {
                int definitionId = registeredIds[i];
                if (!definitions.TryGet(definitionId, out PerformerDefinition definition) ||
                    !definition.HasOwnerTagBindingWork)
                {
                    continue;
                }

                PerformerDefinition.OwnerTagWorkItem[] workItems = definition.OwnerTagWork;
                for (int workIndex = 0; workIndex < workItems.Length; workIndex++)
                {
                    ref readonly PerformerDefinition.OwnerTagWorkItem work = ref workItems[workIndex];
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

        private void StopInactiveSounds(Entity entity, in PerformerState state, BehaviorSlot[] behaviors, uint currentSoundMask)
        {
            if (!_soundTracking.TryGetValue(entity.Id, out var prev)) return;
            uint stopMask = prev.ActiveMask & ~currentSoundMask;
            if (stopMask == 0u) return;
            Vector3 worldPos = World.Has<PerformerWorldPosition>(entity) ? World.Get<PerformerWorldPosition>(entity).Value : Vector3.Zero;
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
                if (evt.Kind != PresentationEventKind.PerformerDestroyed) continue;
                Entity performer = evt.PerformerEntity;
                if (performer == Entity.Null || !_soundTracking.TryGetValue(performer.Id, out var prev)) continue;
                int stableId = (int)evt.Magnitude;
                if (prev.StableId != stableId) continue;
                if (prev.ActiveMask == 0u || !_definitions.TryGet(evt.KeyId, out PerformerDefinition definition))
                {
                    _soundTracking.Remove(performer.Id);
                    continue;
                }
                EmitStopRequests(prev.ActiveMask, definition.Behaviors, stableId, evt.Source, Vector3.Zero);
                _soundTracking.Remove(performer.Id);
            }

            return scanned;
        }

        private void HandleReusedSoundSlot(Entity entity, in PerformerState state, BehaviorSlot[] _)
        {
            if (!_soundTracking.TryGetValue(entity.Id, out var prev)) return;
            if (prev.StableId == 0 || prev.StableId == state.StableId) return;
            if (prev.ActiveMask != 0u && prev.DefinitionId > 0 &&
                _definitions.TryGet(prev.DefinitionId, out PerformerDefinition previousDefinition))
            {
                Vector3 worldPos = World.Has<PerformerWorldPosition>(entity) ? World.Get<PerformerWorldPosition>(entity).Value : Vector3.Zero;
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
                    StableId = PerformerBehaviorRuntimeUtility.ComposeBehaviorStableId(stableId, slot.SlotIndex),
                    SoundAssetId = slot.Sound.SoundAssetId,
                    Owner = owner,
                    WorldPosition = worldPosition,
                });
            }
        }

        private int CountActiveSoundTrackingPerformers()
        {
            return _soundTracking.Count;
        }
        private void ApplySpline(Entity entity, ref PerformerState state, in SplineConfig config, float dt)
        {
            if (config.Usage != SplineUsage.Patrol || config.ProgressParamKey < 0) return;
            float progress = _runtime.ResolveFloat(entity, config.ProgressParamKey, 0f);
            float speed = config.SpeedParamKey >= 0 ? _runtime.ResolveFloat(entity, config.SpeedParamKey, 0f) : 0f;
            progress += dt * speed;
            if (config.Loop) progress = progress - MathF.Floor(progress);
            else progress = Math.Clamp(progress, 0f, 1f);
            _runtime.SetParam(entity, config.ProgressParamKey, ParamLane.Float, progress, 0, Vector4.Zero);
            if (World.Has<PerformerTransformSource>(entity))
            {
                ref var ts = ref World.Get<PerformerTransformSource>(entity);
                ts.Value = TransformSource.SplineDriven;
            }
            if (World.Has<PerformerWorldPosition>(entity))
            {
                ref var pos = ref World.Get<PerformerWorldPosition>(entity);
                pos.Value = new Vector3(progress, pos.Value.Y, 0f);
            }
            if (World.Has<PerformerWorldRotation>(entity))
            {
                ref var rot = ref World.Get<PerformerWorldRotation>(entity);
                rot.Value = Quaternion.Identity;
            }
        }

        private void ApplyAttachment(Entity entity, in AttachmentConfig config)
        {
            Entity parentEntity = World.Get<PerformerParent>(entity).Parent;
            if (parentEntity == Entity.Null || !World.IsAlive(parentEntity)) return;
            switch (config.Target)
            {
                case AttachmentTarget.Parent:
                    Vector3 parentPos = World.Has<PerformerWorldPosition>(parentEntity) ? World.Get<PerformerWorldPosition>(parentEntity).Value : Vector3.Zero;
                    Quaternion parentRot = World.Has<PerformerWorldRotation>(parentEntity) ? World.Get<PerformerWorldRotation>(parentEntity).Value : Quaternion.Identity;
                    Vector3 parentScale = World.Has<PerformerWorldScale>(parentEntity) ? World.Get<PerformerWorldScale>(parentEntity).Value : Vector3.One;
                    ApplyParentAttachment(entity, parentPos, parentRot, parentScale, config);
                    return;
                case AttachmentTarget.Bone:
                    if (!World.Has<PerformerState>(parentEntity))
                    {
                        WarnBoneAttachment(entity, _warnedBoneAttachmentResolveFailed, "parent performer state is missing");
                        return;
                    }

                    ApplyBoneAttachment(entity, World.Get<PerformerState>(parentEntity).StableId, config);
                    return;
            }
        }

        private void ApplyParentAttachment(Entity entity, Vector3 parentPos, Quaternion parentRot, Vector3 parentScale, in AttachmentConfig config)
        {
            Quaternion normalizedParentRot = NormalizeOrIdentity(parentRot);
            Vector3 normalizedParentScale = NormalizeScale(parentScale);
            Vector3 scaledOffset = config.InheritScale ? normalizedParentScale * config.Offset : config.Offset;
            SetTransform(entity, TransformSource.AttachedToParent,
                parentPos + Vector3.Transform(scaledOffset, normalizedParentRot),
                NormalizeOrIdentity(normalizedParentRot * NormalizeOrIdentity(config.RotationOffset)),
                config.InheritScale ? normalizedParentScale : Vector3.One);
        }

        private void ApplyBoneAttachment(Entity entity, int parentStableId, in AttachmentConfig config)
        {
            if (_boneTransformProvider == null)
            {
                WarnBoneAttachment(entity, _warnedBoneAttachmentProviderMissing, $"bone provider is not registered for parentStableId={parentStableId}, boneId={config.BoneId}");
                return;
            }

            if (config.BoneId <= 0)
            {
                WarnBoneAttachment(entity, _warnedBoneAttachmentInvalidBone, $"invalid boneId={config.BoneId} for parentStableId={parentStableId}");
                return;
            }

            if (!_boneTransformProvider.TryGetBoneWorldTransform(parentStableId, config.BoneId,
                    out Vector3 bonePosition, out Quaternion boneRotation, out Vector3 boneScale))
            {
                WarnBoneAttachment(entity, _warnedBoneAttachmentResolveFailed, $"bone transform could not be resolved for parentStableId={parentStableId}, boneId={config.BoneId}");
                return;
            }

            Quaternion normalizedBoneRotation = NormalizeOrIdentity(boneRotation);
            SetTransform(entity, TransformSource.BoneAttached,
                bonePosition + Vector3.Transform(config.Offset, normalizedBoneRotation),
                NormalizeOrIdentity(normalizedBoneRotation * NormalizeOrIdentity(config.RotationOffset)),
                config.InheritScale ? NormalizeScale(boneScale) : Vector3.One);
        }

        private static void WarnBoneAttachment(Entity entity, HashSet<int> once, string reason)
        {
            if (!once.Add(entity.Id))
            {
                return;
            }

            Log.Warn(
                in LogChannels.Presentation,
                $"Performer bone attachment skipped for performerEntityId={entity.Id}: {reason}. Parent-position substitution is not applied.");
        }

        private void ApplyGrounding(Entity entity, in GroundingConfig config)
        {
            if (config.Mode == GroundingMode.None || !World.Has<PerformerWorldPosition>(entity))
            {
                return;
            }

            if (World.Has<PerformerState>(entity) &&
                ShouldSkipOwnerBackedSnapToGround(in World.Get<PerformerState>(entity), config, entity))
            {
                return;
            }

            IVisualHeightmap? heightmap = _heightmapProvider();
            if (heightmap == null)
            {
                WarnMissingGroundingHeightmap();
                SetGroundingMissingHeightmapHeight(entity, config.Offset);
                return;
            }

            ref PerformerWorldPosition position = ref World.Get<PerformerWorldPosition>(entity);
            Span<Vector3> positions = stackalloc Vector3[1] { position.Value };
            Span<GroundingMode> modes = stackalloc GroundingMode[1] { config.Mode };
            Span<float> offsets = stackalloc float[1] { config.Offset };
            if (config.Mode == GroundingMode.AlignToSurface && World.Has<PerformerWorldRotation>(entity))
            {
                ref PerformerWorldRotation rotation = ref World.Get<PerformerWorldRotation>(entity);
                Span<Quaternion> rotations = stackalloc Quaternion[1] { rotation.Value };
                PerformerGroundingUtility.ResolveBatch(positions, rotations, modes, offsets, heightmap);
                rotation.Value = rotations[0];
            }
            else
            {
                PerformerGroundingUtility.ResolveBatch(positions, modes, offsets, heightmap);
            }

            position.Value = positions[0];
        }

        private void WarnMissingGroundingHeightmap()
        {
            if (_warnedMissingGroundingHeightmap)
            {
                return;
            }

            Log.Warn(in LogChannels.Presentation, "Performer grounding requested VisualHeightmap, but none is registered; missing-heightmap grounding writes height 0.");
            _warnedMissingGroundingHeightmap = true;
        }

        private void WarnGroundingSampleFailure()
        {
            if (_warnedGroundingSampleFailure)
            {
                return;
            }

            Log.Warn(in LogChannels.Presentation, "Performer grounding batch could not sample visual heights; grounding writes height 0.");
            _warnedGroundingSampleFailure = true;
        }

        private void SetGroundingMissingHeightmapHeight(Entity entity, float offset)
        {
            if (!World.Has<PerformerWorldPosition>(entity))
            {
                return;
            }

            ref PerformerWorldPosition position = ref World.Get<PerformerWorldPosition>(entity);
            position.Value.Y = offset;
        }

        private void SetGroundingMissingHeightmapHeightBatch(
            Span<PerformerWorldPosition> positions,
            Span<PerformerState> states,
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

        private void SetBootstrapGroundingMissingHeightmapHeightBatch(
            Span<PerformerWorldPosition> positions,
            Span<PerformerState> states,
            int[] behaviorIndices,
            BehaviorSlot[] behaviors,
            Chunk chunk)
        {
            for (int i = 0; i < behaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[behaviorIndices[i]];
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

        private static bool CanSkipOwnerBackedSnapToGround(
            in PerformerState state,
            in GroundingConfig config,
            TransformSource transformSource)
        {
            if (config.Mode != GroundingMode.SnapToGround ||
                config.Offset != 0f ||
                state.AnchorKind != PresentationAnchorKind.Entity ||
                transformSource != TransformSource.EntityTransform)
            {
                return false;
            }

            return true;
        }

        private bool ShouldSkipOwnerBackedSnapToGround(
            in PerformerState state,
            in GroundingConfig config,
            Entity performer)
        {
            if (config.Mode != GroundingMode.SnapToGround ||
                config.Offset != 0f ||
                state.AnchorKind != PresentationAnchorKind.Entity ||
                !World.Has<PerformerTransformSource>(performer) ||
                World.Get<PerformerTransformSource>(performer).Value != TransformSource.EntityTransform)
            {
                return false;
            }

            return true;
        }

        private bool TrySkipOwnerBackedSnapToGroundBatch(
            Span<PerformerState> states,
            int[] behaviorIndices,
            BehaviorSlot[] behaviors,
            Chunk chunk)
        {
            if (behaviorIndices.Length == 0 || !chunk.Has<PerformerTransformSource>())
            {
                return false;
            }

            Span<PerformerTransformSource> transformSources = chunk.GetSpan<PerformerTransformSource>();
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

                    if (!CanSkipOwnerBackedSnapToGroundPerformer(in states[index], transformSources[index].Value))
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
        private static bool CanSkipOwnerBackedSnapToGroundPerformer(
            in PerformerState state,
            TransformSource transformSource)
        {
            return state.AnchorKind == PresentationAnchorKind.Entity &&
                   transformSource == TransformSource.EntityTransform &&
                   state.OwnerEntity != Entity.Null;
        }

        private void ApplyGroundingBatch(
            Span<PerformerWorldPosition> positions,
            Span<PerformerWorldRotation> rotations,
            Span<PerformerState> states,
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
                Span<PerformerTransformSource> transformSources = chunk.Has<PerformerTransformSource>()
                    ? chunk.GetSpan<PerformerTransformSource>()
                    : Span<PerformerTransformSource>.Empty;
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
                    ApplySnapToGroundBatch(count, heightmap);
                }
                else
                {
                    PerformerGroundingUtility.ResolveBatch(
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

        private void ApplyBootstrapGroundingBatch(
            Span<PerformerWorldPosition> positions,
            Span<PerformerWorldRotation> rotations,
            Span<PerformerState> states,
            int[] behaviorIndices,
            BehaviorSlot[] behaviors,
            IVisualHeightmap heightmap,
            Chunk chunk)
        {
            for (int behaviorIndex = 0; behaviorIndex < behaviorIndices.Length; behaviorIndex++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[behaviorIndices[behaviorIndex]];
                if (slot.Kind != BehaviorKind.Grounding || slot.Grounding.Mode == GroundingMode.None)
                {
                    continue;
                }

                int count = 0;
                EnsureGroundingCapacity(chunk.Count);
                Span<PerformerTransformSource> transformSources = chunk.Has<PerformerTransformSource>()
                    ? chunk.GetSpan<PerformerTransformSource>()
                    : Span<PerformerTransformSource>.Empty;
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
                    ApplySnapToGroundBatch(count, heightmap);
                }
                else
                {
                    PerformerGroundingUtility.ResolveBatch(
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

        private void ApplySnapToGroundBatch(int count, IVisualHeightmap heightmap)
        {
            const float metersToCm = 100f;
            const float cmToMeters = 0.01f;
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
                for (int i = 0; i < count; i++)
                {
                    _groundingPositions[i].Y = _groundingOffsets[i];
                }

                WarnGroundingSampleFailure();
                return;
            }

            for (int i = 0; i < count; i++)
            {
                float heightCm = heightsCm[i];
                if (!float.IsFinite(heightCm))
                {
                    _groundingPositions[i].Y = _groundingOffsets[i];
                    continue;
                }

                _groundingPositions[i].Y = (heightCm * cmToMeters) + _groundingOffsets[i];
            }
        }

        private void ApplyParentAttachmentBatch(
            Span<PerformerWorldPosition> positions,
            Span<PerformerWorldRotation> rotations,
            Span<PerformerWorldScale> scales,
            Span<PerformerTransformSource> sources,
            Span<PerformerParent> parents,
            Span<PerformerState> states,
            int slotIndex,
            in AttachmentConfig config,
            Chunk chunk)
        {
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

                Quaternion normalizedParentRot = NormalizeOrIdentity(parentRot);
                Vector3 normalizedParentScale = NormalizeScale(parentScale);
                Vector3 scaledOffset = config.InheritScale ? normalizedParentScale * config.Offset : config.Offset;
                sources[index].Value = TransformSource.AttachedToParent;
                positions[index].Value = parentPos + Vector3.Transform(scaledOffset, normalizedParentRot);
                rotations[index].Value = NormalizeOrIdentity(normalizedParentRot * NormalizeOrIdentity(config.RotationOffset));
                scales[index].Value = config.InheritScale ? normalizedParentScale : Vector3.One;
            }
        }

        private bool TryReadAttachmentParentTransform(
            Entity parent,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            if (!World.IsAlive(parent) || !World.Has<PerformerWorldPosition>(parent))
            {
                position = default;
                rotation = Quaternion.Identity;
                scale = Vector3.One;
                return false;
            }

            position = World.Get<PerformerWorldPosition>(parent).Value;
            rotation = World.Has<PerformerWorldRotation>(parent)
                ? World.Get<PerformerWorldRotation>(parent).Value
                : Quaternion.Identity;
            scale = World.Has<PerformerWorldScale>(parent)
                ? World.Get<PerformerWorldScale>(parent).Value
                : Vector3.One;
            return true;
        }

        private static bool TryResolveSingleDefinitionChunk(Span<PerformerState> states, int count, out int definitionId)
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
            Span<PerformerState> states,
            Span<PerformerTransformSource> sources,
            Span<PerformerParent> parents,
            Chunk chunk)
        {
            foreach (int index in chunk)
            {
                ref PerformerTransformSource source = ref sources[index];
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
            Span<PerformerWorldPosition> positions,
            Span<PerformerWorldRotation> rotations,
            Span<PerformerWorldScale> scales,
            Span<PerformerTransformSource> sources,
            Span<PerformerParent> parents,
            Span<PerformerState> states,
            PerformerDefinition definition,
            Chunk chunk)
        {
            AssetBindingConfig assetBinding = ResolvePrimaryAssetBinding(definition, states[0].BehaviorActiveMask);
            foreach (int index in chunk)
            {
                PerformerTransformSnapshot performerSnapshot = new PerformerTransformSnapshot
                {
                    WorldPosition = positions[index].Value,
                    WorldRotation = rotations[index].Value,
                    WorldScale = scales[index].Value,
                    TransformSource = sources[index].Value,
                };

                Entity parentEntity = parents[index].Parent;
                bool hasParent = parentEntity != Entity.Null &&
                                 World.IsAlive(parentEntity) &&
                                 World.Has<PerformerState>(parentEntity);
                PerformerTransformSnapshot parentSnapshot = default;
                if (hasParent)
                {
                    parentSnapshot.WorldPosition = World.Has<PerformerWorldPosition>(parentEntity)
                        ? World.Get<PerformerWorldPosition>(parentEntity).Value
                        : Vector3.Zero;
                    parentSnapshot.WorldRotation = World.Has<PerformerWorldRotation>(parentEntity)
                        ? World.Get<PerformerWorldRotation>(parentEntity).Value
                        : Quaternion.Identity;
                    parentSnapshot.WorldScale = World.Has<PerformerWorldScale>(parentEntity)
                        ? World.Get<PerformerWorldScale>(parentEntity).Value
                        : Vector3.One;
                }

                Entity ownerEntity = states[index].OwnerEntity;
                bool hasOwnerTransform = World.IsAlive(ownerEntity) && World.Has<VisualTransform>(ownerEntity);
                VisualTransform ownerTransform = hasOwnerTransform
                    ? World.Get<VisualTransform>(ownerEntity)
                    : default;

                PerformerResolvedTransform resolved = PerformerGroundingUtility.ResolveTransform(
                    performerSnapshot,
                    parentSnapshot,
                    hasParent,
                    ownerTransform,
                    hasOwnerTransform,
                    assetBinding);

                positions[index].Value = resolved.Position;
                rotations[index].Value = resolved.Rotation;
                scales[index].Value = resolved.Scale;
            }
        }

        private static AssetBindingConfig ResolvePrimaryAssetBinding(
            PerformerDefinition definition,
            uint activeBehaviorMask)
        {
            BehaviorSlot[] behaviors = definition.Behaviors;
            int primaryAssetBehaviorIndex = definition.PrimaryAssetBehaviorIndex;
            if (primaryAssetBehaviorIndex >= 0 && primaryAssetBehaviorIndex < behaviors.Length)
            {
                ref readonly BehaviorSlot primarySlot = ref behaviors[primaryAssetBehaviorIndex];
                if (primarySlot.Kind == BehaviorKind.AssetBinding &&
                    IsBehaviorActive(activeBehaviorMask, primarySlot.SlotIndex))
                {
                    return primarySlot.AssetBinding;
                }
            }

            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (slot.Kind == BehaviorKind.AssetBinding &&
                    IsBehaviorActive(activeBehaviorMask, slot.SlotIndex))
                {
                    return slot.AssetBinding;
                }
            }

            return new AssetBindingConfig { LocalScale = Vector3.One };
        }

        private static bool CanProcessBootstrapChunkFast(PerformerDefinition definition)
        {
            if (definition.Bindings.Length != 0 ||
                definition.MaterialBehaviorIndices.Length != 0 ||
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

        private static uint BuildDefaultBehaviorMaskForFastEligibility(PerformerDefinition definition)
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

        private static bool DefinitionHasBootstrapGroundingWork(PerformerDefinition definition)
        {
            return definition.BootstrapGroundingBehaviorIndices.Length != 0;
        }

        private static bool TryResolveParentAttachmentOnly(
            PerformerDefinition definition,
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
        }

        private void SetTransform(Entity entity, TransformSource source, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (World.Has<PerformerTransformSource>(entity))
                World.Get<PerformerTransformSource>(entity).Value = source;
            if (World.Has<PerformerWorldPosition>(entity))
                World.Get<PerformerWorldPosition>(entity).Value = position;
            if (World.Has<PerformerWorldRotation>(entity))
                World.Get<PerformerWorldRotation>(entity).Value = rotation;
            if (World.Has<PerformerWorldScale>(entity))
                World.Get<PerformerWorldScale>(entity).Value = scale;
        }

        private void SetParam(Entity entity, int paramKey, ParamLane lane, float floatValue, int intValue, in Vector4 vectorValue)
        {
            _runtime.SetParamAndPropagateToAffectedChildren(entity, paramKey, lane, floatValue, intValue, in vectorValue);
        }

        private void ClearParam(Entity entity, int paramKey, ParamLane lane)
        {
            _runtime.ClearParamAndPropagateToAffectedChildren(entity, paramKey, lane);
        }

        private void ResolveTransform(Entity entity, ref PerformerState state, PerformerDefinition definition, BehaviorSlot[] behaviors)
        {
            AssetBindingConfig assetBinding = new AssetBindingConfig { LocalScale = Vector3.One };
            int primaryAssetBehaviorIndex = definition.PrimaryAssetBehaviorIndex;
            if (primaryAssetBehaviorIndex >= 0 && primaryAssetBehaviorIndex < behaviors.Length)
            {
                ref readonly BehaviorSlot primarySlot = ref behaviors[primaryAssetBehaviorIndex];
                if (primarySlot.Kind == BehaviorKind.AssetBinding &&
                    IsBehaviorActive(state.BehaviorActiveMask, primarySlot.SlotIndex))
                {
                    assetBinding = primarySlot.AssetBinding;
                }
                else
                {
                    for (int i = 0; i < behaviors.Length; i++)
                    {
                        ref readonly BehaviorSlot slot = ref behaviors[i];
                        if (slot.Kind == BehaviorKind.AssetBinding && IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                        {
                            assetBinding = slot.AssetBinding;
                            break;
                        }
                    }
                }
            }
            else
            {
                for (int i = 0; i < behaviors.Length; i++)
                {
                    ref readonly BehaviorSlot slot = ref behaviors[i];
                    if (slot.Kind == BehaviorKind.AssetBinding && IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                    {
                        assetBinding = slot.AssetBinding;
                        break;
                    }
                }
            }

            Entity parentEntity = World.Has<PerformerParent>(entity) ? World.Get<PerformerParent>(entity).Parent : Entity.Null;
            bool hasParent = parentEntity != Entity.Null && World.IsAlive(parentEntity) && World.Has<PerformerState>(parentEntity);
            PerformerTransformSnapshot parentSnapshot = default;
            if (hasParent)
            {
                parentSnapshot.WorldPosition = World.Has<PerformerWorldPosition>(parentEntity) ? World.Get<PerformerWorldPosition>(parentEntity).Value : Vector3.Zero;
                parentSnapshot.WorldRotation = World.Has<PerformerWorldRotation>(parentEntity) ? World.Get<PerformerWorldRotation>(parentEntity).Value : Quaternion.Identity;
                parentSnapshot.WorldScale = World.Has<PerformerWorldScale>(parentEntity) ? World.Get<PerformerWorldScale>(parentEntity).Value : Vector3.One;
            }

            bool hasOwnerTransform = World.IsAlive(state.OwnerEntity) && World.Has<VisualTransform>(state.OwnerEntity);
            VisualTransform ownerTransform = hasOwnerTransform ? World.Get<VisualTransform>(state.OwnerEntity) : default;

            PerformerTransformSnapshot performerSnapshot = default;
            performerSnapshot.WorldPosition = World.Has<PerformerWorldPosition>(entity) ? World.Get<PerformerWorldPosition>(entity).Value : Vector3.Zero;
            performerSnapshot.WorldRotation = World.Has<PerformerWorldRotation>(entity) ? World.Get<PerformerWorldRotation>(entity).Value : Quaternion.Identity;
            performerSnapshot.WorldScale = World.Has<PerformerWorldScale>(entity) ? World.Get<PerformerWorldScale>(entity).Value : Vector3.One;
            performerSnapshot.TransformSource = World.Has<PerformerTransformSource>(entity) ? World.Get<PerformerTransformSource>(entity).Value : TransformSource.EntityTransform;

            PerformerResolvedTransform resolved = PerformerGroundingUtility.ResolveTransform(
                performerSnapshot, parentSnapshot, hasParent, ownerTransform, hasOwnerTransform, assetBinding);

            if (World.Has<PerformerWorldPosition>(entity))
                World.Get<PerformerWorldPosition>(entity).Value = resolved.Position;
            if (World.Has<PerformerWorldRotation>(entity))
                World.Get<PerformerWorldRotation>(entity).Value = resolved.Rotation;
            if (World.Has<PerformerWorldScale>(entity))
                World.Get<PerformerWorldScale>(entity).Value = resolved.Scale;
        }

        private void ResolveDefaultTransformSource(Entity entity, ref PerformerState state)
        {
            if (!World.Has<PerformerTransformSource>(entity)) return;
            ref var ts = ref World.Get<PerformerTransformSource>(entity);
            if (ts.Value is TransformSource.BoneAttached or TransformSource.AttachedToParent)
                return;
            Entity parentEntity = World.Has<PerformerParent>(entity) ? World.Get<PerformerParent>(entity).Parent : Entity.Null;
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

        private float ResolveEntityColorChannel(Entity owner, int channelIndex)
        {
            Vector4 color = ResolveEntityColor(owner);
            return channelIndex switch { 0 => color.X, 1 => color.Y, 2 => color.Z, 3 => color.W, _ => 0f };
        }

        private Vector4 ResolveEntityColor(Entity owner)
        {
            return World.IsAlive(owner) ? TeamColorResolver.Resolve(World, owner) : TeamColorResolver.DefaultColor;
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

        private static Quaternion NormalizeOrIdentity(Quaternion value)
        {
            return value.LengthSquared() > 0.000001f ? Quaternion.Normalize(value) : Quaternion.Identity;
        }

        private static Vector3 NormalizeScale(Vector3 value)
        {
            return value == Vector3.Zero ? Vector3.One : value;
        }
    }
}
