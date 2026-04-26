using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Arch.System;
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
            .WithAny<PerfHasSpline, PerfHasAttachment, PerfHasSound>()
            .WithNone<PerformerBootstrapPending>();
        private readonly List<Entity> _bootstrapClearList = new(256);

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
            ProcessCreatedPerformers(dt);
            int ownerChanges = ProcessOwnerChanges();
            int tickDrivenCount = ProcessTickDrivenPerformers(dt);
            ClearProcessedBootstrapMarkers();
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
            World.Query(in _bootstrapPendingQuery, (Entity entity, ref PerformerState state, ref PerformerBootstrapPending pending) =>
            {
                ProcessPerformer(entity, firstFrame: true, updateAttributeBindings: true, updateTagBindings: true, tickDt, tickDrivenOnly: false);
                _bootstrapClearList.Add(entity);
            });
        }

        private void ClearProcessedBootstrapMarkers()
        {
            for (int i = 0; i < _bootstrapClearList.Count; i++)
            {
                Entity entity = _bootstrapClearList[i];
                if (World.IsAlive(entity) && World.Has<PerformerBootstrapPending>(entity))
                {
                    World.Remove<PerformerBootstrapPending>(entity);
                }
            }
        }

        private int ProcessOwnerChanges()
        {
            _lastOwnerAttributeChangeCount = 0;
            _lastOwnerTagChangeCount = 0;
            if (_ownerChanges == null)
            {
                return ProcessDirtyOwnersFromComponents();
            }

            ReadOnlySpan<PresentationOwnerChange> changes = _ownerChanges.GetSpan();
            int processed = 0;
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
            World.Query(in _dirtyOwnerAttributeQuery, (Entity owner, ref GameplayAttributeChangedBits bits) =>
            {
                for (int attributeId = 0; attributeId < AttributeBuffer.MAX_ATTRS; attributeId++)
                {
                    if (bits.IsSet(attributeId))
                    {
                        ProcessOwnerAttributeChange(owner, attributeId);
                        _lastOwnerAttributeChangeCount++;
                        processed++;
                    }
                }
            });

            World.Query(in _dirtyOwnerTagQuery, (Entity owner, ref GameplayTagEffectiveChangedBits bits) =>
            {
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
                                bool tagActive = World.Has<GameplayTagContainer>(owner) && World.Get<GameplayTagContainer>(owner).HasTag(tagId);
                                ProcessOwnerTagChange(owner, tagId, tagActive);
                                _lastOwnerTagChangeCount++;
                                processed++;
                            }
                        }
                    }
                }
            });

            return processed;
        }

        private void ProcessOwnerAttributeChange(Entity owner, int attributeId)
        {
            if (!World.IsAlive(owner) ||
                !World.Has<AttributeBuffer>(owner) ||
                !_ownerAttributeWorkIndex.TryGetValue(attributeId, out OwnerAttributeWorkTarget[] targets))
            {
                return;
            }

            ref AttributeBuffer attributes = ref World.Get<AttributeBuffer>(owner);
            for (int i = 0; i < targets.Length; i++)
            {
                ProcessOwnerAttributeChangeBucket(owner, ref attributes, in targets[i]);
            }
        }

        private void ProcessOwnerAttributeChangeBucket(
            Entity owner,
            ref AttributeBuffer attributes,
            in OwnerAttributeWorkTarget target)
        {
            IReadOnlyList<Entity> performers = _runtime.GetActiveByOwnerDefinition(target.DefinitionId, owner);
            for (int i = 0; i < performers.Count; i++)
            {
                Entity performer = performers[i];
                if (!World.IsAlive(performer) || !World.Has<PerformerState>(performer))
                {
                    continue;
                }

                ApplyOwnerAttributeWork(performer, target.Definition, ref attributes, in target.Work);
            }
        }

        private void ProcessOwnerTagChange(Entity owner, int tagId, bool? tagActiveOverride = null)
        {
            if (!World.IsAlive(owner) ||
                !_ownerTagWorkIndex.TryGetValue(tagId, out OwnerTagWorkTarget[] targets))
            {
                return;
            }

            bool tagActive = tagActiveOverride ??
                (World.Has<GameplayTagContainer>(owner) && World.Get<GameplayTagContainer>(owner).HasTag(tagId));
            for (int i = 0; i < targets.Length; i++)
            {
                ProcessOwnerTagChangeBucket(owner, tagActive, in targets[i]);
            }
        }

        private void ProcessOwnerTagChangeBucket(
            Entity owner,
            bool tagActive,
            in OwnerTagWorkTarget target)
        {
            IReadOnlyList<Entity> performers = _runtime.GetActiveByOwnerDefinition(target.DefinitionId, owner);
            for (int i = 0; i < performers.Count; i++)
            {
                Entity performer = performers[i];
                if (!World.IsAlive(performer) || !World.Has<PerformerState>(performer))
                {
                    continue;
                }

                ApplyOwnerTagWork(performer, target.Definition, in target.Work, tagActive);
            }
        }

        private int ProcessTickDrivenPerformers(float tickDt)
        {
            int processed = 0;
            World.Query(in _tickDrivenQuery, (Entity entity, ref PerformerState state, ref PerformerWorldPosition pos) =>
            {
                processed++;
                ProcessPerformer(entity, firstFrame: false, updateAttributeBindings: false, updateTagBindings: false, tickDt, tickDrivenOnly: true);
            });

            return processed;
        }

        private void ProcessPerformer(
            Entity entity,
            bool firstFrame,
            bool updateAttributeBindings,
            bool updateTagBindings,
            float tickDt,
            bool tickDrivenOnly)
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

            bool hasSoundBehavior = HasSoundBehavior(behaviors);
            if (hasSoundBehavior)
            {
                HandleReusedSoundSlot(entity, in state, behaviors);
            }

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

            ResolveTransform(entity, ref state, definition, behaviors);
        }

        private static bool HasSoundBehavior(BehaviorSlot[] behaviors)
        {
            for (int i = 0; i < behaviors.Length; i++)
            {
                if (behaviors[i].Kind == BehaviorKind.Sound)
                {
                    return true;
                }
            }

            return false;
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
                        _runtime.SetParam(entity, binding.ParamKey, ParamLane.Float, ResolveEntityColorChannel(owner, value.SourceId), 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.EntityColorVector:
                        _runtime.SetParam(entity, binding.ParamKey, ParamLane.Vector, 0f, 0, ResolveEntityColor(owner));
                        break;
                    case ValueSourceKind.FacingRadians:
                        _runtime.SetParam(entity, binding.ParamKey, ParamLane.Float, ResolveFacingRadians(owner), 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.FacingDegrees:
                        _runtime.SetParam(entity, binding.ParamKey, ParamLane.Float, ResolveFacingRadians(owner) * (180f / MathF.PI), 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.Attribute:
                    case ValueSourceKind.AttributeRatio:
                    case ValueSourceKind.AttributeBase:
                        if (!hasAttributes) continue;
                        ref AttributeBuffer attributes = ref World.Get<AttributeBuffer>(owner);
                        float resolved = ResolveAttributeValue(ref attributes, value.SourceId, value.Source);
                        _runtime.SetParam(entity, binding.ParamKey, ParamLane.Float, resolved, 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.Constant:
                        _runtime.SetParam(entity, binding.ParamKey, ParamLane.Float, value.ConstantValue, 0, Vector4.Zero);
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
                _runtime.SetParam(entity, binding.ParamKey, ParamLane.Float, resolved, 0, Vector4.Zero);
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
            _runtime.SetParam(entity, config.TargetParamKey, ParamLane.Float, value, 0, Vector4.Zero);

            ThresholdMapping[] thresholds = config.Thresholds ?? Array.Empty<ThresholdMapping>();
            for (int i = 0; i < thresholds.Length; i++)
            {
                ref readonly ThresholdMapping threshold = ref thresholds[i];
                if (value > threshold.Threshold)
                {
                    continue;
                }

                int thresholdIntValue = (int)threshold.OutputValue;
                _runtime.SetParam(entity, threshold.OutputParamKey, ParamLane.Float, threshold.OutputValue, thresholdIntValue, Vector4.Zero);
                _runtime.SetParam(entity, threshold.OutputParamKey, ParamLane.Int, threshold.OutputValue, thresholdIntValue, Vector4.Zero);
                return;
            }

            bool hasFloatParams = World.Has<PerformerFloatParams>(entity);
            bool hasIntParams = World.Has<PerformerIntParams>(entity);
            for (int i = 0; i < thresholds.Length; i++)
            {
                ref readonly ThresholdMapping threshold = ref thresholds[i];
                if (hasFloatParams)
                {
                    _runtime.ClearParam(entity, threshold.OutputParamKey, ParamLane.Float);
                }

                if (hasIntParams)
                {
                    _runtime.ClearParam(entity, threshold.OutputParamKey, ParamLane.Int);
                }
            }
        }

        private void ApplyTagBinding(Entity entity, in TagBindingConfig config, bool active)
        {
            if (config.InvertLogic)
            {
                active = !active;
            }

            _runtime.SetParam(entity, config.TargetParamKey, ParamLane.Int, 0f, active ? 1 : 0, Vector4.Zero);
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
                _runtime.SetParam(entity, config.MaterialSwapParamKey, ParamLane.Int, 0f, materialId, Vector4.Zero);
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
                    if (!World.Has<PerformerState>(parentEntity)) return;
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
            if (_boneTransformProvider == null || config.BoneId <= 0) return;
            if (!_boneTransformProvider.TryGetBoneWorldTransform(parentStableId, config.BoneId,
                    out Vector3 bonePosition, out Quaternion boneRotation, out Vector3 boneScale))
                return;
            Quaternion normalizedBoneRotation = NormalizeOrIdentity(boneRotation);
            SetTransform(entity, TransformSource.BoneAttached,
                bonePosition + Vector3.Transform(config.Offset, normalizedBoneRotation),
                NormalizeOrIdentity(normalizedBoneRotation * NormalizeOrIdentity(config.RotationOffset)),
                config.InheritScale ? NormalizeScale(boneScale) : Vector3.One);
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
                performerSnapshot, parentSnapshot, hasParent, ownerTransform, hasOwnerTransform, assetBinding, _heightmapProvider());

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
