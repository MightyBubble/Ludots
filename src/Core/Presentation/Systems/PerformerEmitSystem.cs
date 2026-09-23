using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Surfaces;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class PerformerEmitSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription EmitQuery = new QueryDescription()
            .WithAll<PerformerState, PerformerCullState, PerformerWorldPosition, PerformerWorldRotation, PerformerWorldFacing, PerformerWorldScale, PerformerEmitCache, PerfHasEmitWork>()
            .WithNone<PerfStaticStableVisual>();

        private static readonly QueryDescription DirtyStaticEmitQuery = new QueryDescription()
            .WithAll<PerformerState, PerformerCullState, PerformerWorldPosition, PerformerWorldRotation, PerformerWorldFacing, PerformerWorldScale, PerformerEmitCache, PerfStaticStableVisual>();

        private static readonly QueryDescription DirtyRetainedRequestEmitQuery = new QueryDescription()
            .WithAll<PerformerState, PerformerCullState, PerformerWorldPosition, PerformerWorldRotation, PerformerWorldFacing, PerformerWorldScale, PerformerEmitCache, PerfRetainedPresentationRequest>();

        private static readonly QueryDescription RetainedRequestLifecycleQuery = new QueryDescription()
            .WithAll<PerformerState, PerformerCullState, PerformerEmitCache, PerfRetainedPresentationRequest, PerfRetainedPresentationRequestLifecycleTick>();

        private readonly PerformerEntityRuntime _runtime;
        private readonly PerformerDefinitionRegistry _definitions;
        private readonly PresentationRequestBuffer _requests;
        private readonly Dictionary<string, object> _globals;
        private readonly PerformerAssetEmitRuntime _assetEmitter;
        private readonly StableDrawCache? _stableDrawCache;
        private readonly SkinnedVisualBatchBuffer? _skinnedVisualBatchBuffer;
        private readonly WorldHudBatchBuffer? _worldHudBuffer;
        private readonly PerformerVisualStableIdTable? _visualStableIds;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private readonly List<Entity> _pendingDestroy = new(256);
        private readonly Dictionary<Entity, PresentationRequest> _singleRequestReplayCache = new();
        private readonly WorldHudPerformBehavior _worldHudBehavior = new();

        public PerformerEmitSystem(
            World world,
            PerformerEntityRuntime runtime,
            PerformerDefinitionRegistry definitions,
            PresentationRequestBuffer requests,
            Dictionary<string, object> globals,
            PerformerAnimatorStateBuffer animatorStates = null,
            SoundRequestBuffer soundRequests = null,
            PresentationTimingDiagnostics? timingDiagnostics = null,
            StableDrawCache? stableDrawCache = null,
            SkinnedVisualBatchBuffer? skinnedVisualBatchBuffer = null,
            WorldHudBatchBuffer? worldHudBuffer = null,
            PerformerVisualStableIdTable? visualStableIds = null)
            : base(world)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _globals = globals ?? new Dictionary<string, object>();
            _timingDiagnostics = timingDiagnostics;
            _stableDrawCache = stableDrawCache;
            _skinnedVisualBatchBuffer = skinnedVisualBatchBuffer;
            _worldHudBuffer = worldHudBuffer;
            _visualStableIds = visualStableIds;
            _runtime.BindDefinitions(_definitions);
            _assetEmitter = new PerformerAssetEmitRuntime(
                world, _runtime, requests, globals, animatorStates, soundRequests, visualStableIds);
        }

        public override void Update(in float dt)
        {
            long start = _timingDiagnostics != null ? Stopwatch.GetTimestamp() : 0L;
            float deltaTime = dt;
            _pendingDestroy.Clear();
            _skinnedVisualBatchBuffer?.Clear();
            int cachedDefId = -1;
            PerformerDefinition? cachedDefinition = null;
            bool cachedFastDefinition = false;
            int singleVisualFastCount = 0;
            foreach (ref var chunk in World.Query(in EmitQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<PerformerState> states = chunk.GetSpan<PerformerState>();
                Span<PerformerCullState> culls = chunk.GetSpan<PerformerCullState>();
                Span<PerformerWorldPosition> positions = chunk.GetSpan<PerformerWorldPosition>();
                Span<PerformerWorldRotation> rotations = chunk.GetSpan<PerformerWorldRotation>();
                Span<PerformerWorldFacing> facings = chunk.GetSpan<PerformerWorldFacing>();
                Span<PerformerWorldScale> scales = chunk.GetSpan<PerformerWorldScale>();
                Span<PerformerEmitCache> emitCaches = chunk.GetSpan<PerformerEmitCache>();
                bool hasAnimatorSlots = chunk.Has<PerformerAnimatorSlot>();
                Span<PerformerAnimatorSlot> animatorSlots = hasAnimatorSlots
                    ? chunk.GetSpan<PerformerAnimatorSlot>()
                    : default;

                foreach (int index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ref PerformerState state = ref states[index];
                    ResolveCachedDefinition(
                        state.DefId,
                        ref cachedDefId,
                        ref cachedDefinition,
                        ref cachedFastDefinition);

                    if (cachedDefinition == null)
                    {
                        RemoveReplayCache(entity);
                        continue;
                    }

                    if (cachedFastDefinition &&
                        ProcessSingleVisualProxyFastChunkEntity(
                            entity,
                            ref state,
                            cachedDefinition,
                            ref culls[index],
                            ref positions[index],
                            ref rotations[index],
                            ref facings[index],
                            ref scales[index],
                            ref emitCaches[index],
                            deltaTime,
                            hasAnimatorSlots ? animatorSlots[index].Value : -1,
                            ref singleVisualFastCount))
                    {
                        continue;
                    }

                    ProcessEmitEntity(
                        entity,
                        ref state,
                        ref culls[index],
                        ref positions[index],
                        ref rotations[index],
                        ref facings[index],
                        ref scales[index],
                        ref emitCaches[index],
                        deltaTime,
                        clearDirtyAfterProcessing: false);
                }
            }

            ProcessDirtyStaticEmitEntities();
            ProcessDirtyRetainedPresentationRequestEntities();
            ProcessRetainedPresentationRequestLifecycleEntities(deltaTime);

            for (int i = 0; i < _pendingDestroy.Count; i++)
            {
                Entity performer = _pendingDestroy[i];
                if (World.IsAlive(performer))
                {
                    _runtime.Destroy(performer, ReleaseDestroyedPerformerVisualStableIds);
                }
            }

            if (_timingDiagnostics != null)
            {
                _timingDiagnostics.ObservePerformerEmitSingleVisualFastPath(singleVisualFastCount);
                _timingDiagnostics.ObservePerformerEmit((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
            }
        }

        private void ReleaseDestroyedPerformerVisualStableIds(Entity performer, PerformerState state)
        {
            if (_definitions.TryGet(state.DefId, out PerformerDefinition definition) &&
                World.IsAlive(performer) &&
                World.Has<PerformerEmitCache>(performer))
            {
                ref PerformerEmitCache emitCache = ref World.Get<PerformerEmitCache>(performer);
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
            }

            _visualStableIds?.ReleasePerformer(state.StableId);
        }

        private bool ResolveCachedDefinition(
            int definitionId,
            ref int cachedDefId,
            ref PerformerDefinition? cachedDefinition,
            ref bool cachedFastDefinition)
        {
            if (definitionId == cachedDefId)
            {
                return cachedDefinition != null;
            }

            cachedDefId = definitionId;
            cachedDefinition = _definitions.TryGet(definitionId, out PerformerDefinition definition)
                ? definition
                : null;
            cachedFastDefinition = cachedDefinition != null && IsVisualProxyFastDefinition(cachedDefinition);

            return cachedDefinition != null;
        }

        private void ProcessDirtyRetainedPresentationRequestEntities()
        {
            if (!_runtime.HasDirtyRetainedPresentationRequests)
            {
                _runtime.ClearConsumedRetainedPresentationDirtyEntities();
                _timingDiagnostics?.ObservePerformerEmitRetainedBreakdown(processMs: 0d, dirtyCount: 0);
                _timingDiagnostics?.ObservePerformerEmitRetainedDirectPath(directHits: 0, fullPathCount: 0, directMisses: 0);
                return;
            }

            long processStart = _timingDiagnostics != null ? Stopwatch.GetTimestamp() : 0L;
            ReadOnlySpan<Entity> dirtyEntities = _runtime.RetainedPresentationDirtyEntities;
            if (!dirtyEntities.IsEmpty)
            {
                int dirtyCount = ProcessRetainedPresentationDirtyList(
                    dirtyEntities,
                    out int listDirectHits,
                    out int listFullPathCount,
                    out int listDirectMisses);
                _runtime.ClearConsumedRetainedPresentationDirtyEntities();
                if (_timingDiagnostics != null)
                {
                    _timingDiagnostics.ObservePerformerEmitRetainedBreakdown(
                        (Stopwatch.GetTimestamp() - processStart) * 1000d / Stopwatch.Frequency,
                        dirtyCount);
                    _timingDiagnostics.ObservePerformerEmitRetainedDirectPath(
                        listDirectHits,
                        listFullPathCount,
                        listDirectMisses);
                }

                return;
            }

            int cachedDefId = -1;
            PerformerDefinition? cachedDefinition = null;
            int scannedDirtyCount = 0;
            int directHits = 0;
            int fullPathCount = 0;
            int directMisses = 0;
            foreach (ref var chunk in World.Query(in DirtyRetainedRequestEmitQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var states = chunk.GetSpan<PerformerState>();
                var culls = chunk.GetSpan<PerformerCullState>();
                var positions = chunk.GetSpan<PerformerWorldPosition>();
                var rotations = chunk.GetSpan<PerformerWorldRotation>();
                var facings = chunk.GetSpan<PerformerWorldFacing>();
                var scales = chunk.GetSpan<PerformerWorldScale>();
                var emitCaches = chunk.GetSpan<PerformerEmitCache>();
                foreach (var index in chunk)
                {
                    if (emitCaches[index].RetainedDirty == 0)
                    {
                        continue;
                    }

                    scannedDirtyCount++;
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ref PerformerState state = ref states[index];
                    if (state.DefId != cachedDefId)
                    {
                        cachedDefId = state.DefId;
                        cachedDefinition = _definitions.TryGet(state.DefId, out PerformerDefinition definition)
                            ? definition
                            : null;
                    }

                    if (cachedDefinition == null)
                    {
                        _runtime.ClearStaticDirty(ref emitCaches[index]);
                        continue;
                    }

                    if (TryUpdateRetainedWorldHudDirect(
                            entity,
                            ref state,
                            cachedDefinition,
                            ref culls[index],
                            ref positions[index],
                            ref rotations[index],
                            ref scales[index],
                            ref emitCaches[index]))
                    {
                        directHits++;
                        continue;
                    }

                    directMisses++;
                    fullPathCount++;
                    ProcessEmitEntity(
                        entity,
                        ref state,
                        ref culls[index],
                        ref positions[index],
                        ref rotations[index],
                        ref facings[index],
                        ref scales[index],
                        ref emitCaches[index],
                        deltaTime: 0f,
                        clearDirtyAfterProcessing: true);
                }
            }

            if (_timingDiagnostics != null)
            {
                _timingDiagnostics.ObservePerformerEmitRetainedBreakdown(
                    (Stopwatch.GetTimestamp() - processStart) * 1000d / Stopwatch.Frequency,
                    scannedDirtyCount);
                _timingDiagnostics.ObservePerformerEmitRetainedDirectPath(directHits, fullPathCount, directMisses);
            }
        }

        private int ProcessRetainedPresentationDirtyList(
            ReadOnlySpan<Entity> dirtyEntities,
            out int directHits,
            out int fullPathCount,
            out int directMisses)
        {
            int cachedDefId = -1;
            PerformerDefinition? cachedDefinition = null;
            int dirtyCount = 0;
            directHits = 0;
            fullPathCount = 0;
            directMisses = 0;
            for (int i = 0; i < dirtyEntities.Length; i++)
            {
                Entity entity = dirtyEntities[i];
                if (!World.IsAlive(entity) ||
                    !World.Has<PerformerState>(entity) ||
                    !World.Has<PerformerEmitCache>(entity))
                {
                    continue;
                }

                ref PerformerEmitCache emitCache = ref World.Get<PerformerEmitCache>(entity);
                if (emitCache.RetainedDirty == 0)
                {
                    continue;
                }

                ref PerformerState state = ref World.Get<PerformerState>(entity);
                if (state.DefId != cachedDefId)
                {
                    cachedDefId = state.DefId;
                    cachedDefinition = _definitions.TryGet(state.DefId, out PerformerDefinition definition)
                        ? definition
                        : null;
                }

                if (cachedDefinition == null)
                {
                    _runtime.ClearStaticDirty(ref emitCache);
                    continue;
                }

                if (!World.Has<PerformerCullState>(entity) ||
                    !World.Has<PerformerWorldPosition>(entity) ||
                    !World.Has<PerformerWorldRotation>(entity) ||
                    !World.Has<PerformerWorldFacing>(entity) ||
                    !World.Has<PerformerWorldScale>(entity))
                {
                    _runtime.ClearStaticDirty(ref emitCache);
                    continue;
                }

                ref PerformerCullState cull = ref World.Get<PerformerCullState>(entity);
                ref PerformerWorldPosition position = ref World.Get<PerformerWorldPosition>(entity);
                ref PerformerWorldRotation rotation = ref World.Get<PerformerWorldRotation>(entity);
                ref PerformerWorldFacing facing = ref World.Get<PerformerWorldFacing>(entity);
                ref PerformerWorldScale scale = ref World.Get<PerformerWorldScale>(entity);
                dirtyCount++;
                if (TryUpdateRetainedWorldHudDirect(
                        entity,
                        ref state,
                        cachedDefinition,
                        ref cull,
                        ref position,
                        ref rotation,
                        ref scale,
                        ref emitCache))
                {
                    directHits++;
                    continue;
                }

                directMisses++;
                fullPathCount++;
                ProcessEmitEntity(
                    entity,
                    ref state,
                    ref cull,
                    ref position,
                    ref rotation,
                    ref facing,
                    ref scale,
                    ref emitCache,
                    deltaTime: 0f,
                    clearDirtyAfterProcessing: true);
            }

            return dirtyCount;
        }

        private bool TryUpdateRetainedWorldHudDirect(
            Entity entity,
            ref PerformerState state,
            PerformerDefinition definition,
            ref PerformerCullState cull,
            ref PerformerWorldPosition position,
            ref PerformerWorldRotation rotation,
            ref PerformerWorldScale scale,
            ref PerformerEmitCache emitCache)
        {
            if (_worldHudBuffer == null || definition.HasSurfaceAuthoring)
            {
                return false;
            }

            if (definition.SupportsSingleVisualProxyFastEmit && definition.HasRetainedWorldHudLanes)
            {
                return false;
            }

            if (definition.HasRetainedWorldHudLanes)
            {
                return TryUpdateRetainedWorldHudLanesDirect(
                    entity,
                    ref state,
                    definition,
                    ref cull,
                    ref position,
                    ref rotation,
                    ref scale,
                    ref emitCache);
            }

            if (definition.AssetBehaviorIndices.Length != 1)
            {
                return false;
            }

            int behaviorIndex = definition.AssetBehaviorIndices[0];
            if ((uint)behaviorIndex >= (uint)definition.Behaviors.Length)
            {
                return false;
            }

            ref readonly BehaviorSlot slot = ref definition.Behaviors[behaviorIndex];
            if (slot.Kind != BehaviorKind.AssetBinding ||
                !IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
            {
                return false;
            }

            ref readonly AssetBindingConfig asset = ref slot.AssetBinding;
            WorldHudItemKind kind = asset.AssetKind switch
            {
                AssetKind.WorldHud => WorldHudItemKind.Bar,
                AssetKind.WorldText => WorldHudItemKind.Text,
                _ => default,
            };

            if (kind == default)
            {
                return false;
            }

            if (kind == WorldHudItemKind.Bar)
            {
                return TryUpdateRetainedWorldHudLaneDirect(
                    entity,
                    ref state,
                    definition,
                    ref cull,
                    ref position,
                    ref rotation,
                    ref scale,
                    ref emitCache,
                    behaviorIndex,
                    in asset,
                    kind,
                    ref emitCache.RetainedBarBufferIndexPlusOne);
            }

            return TryUpdateRetainedWorldHudLaneDirect(
                entity,
                ref state,
                definition,
                ref cull,
                ref position,
                ref rotation,
                ref scale,
                ref emitCache,
                behaviorIndex,
                in asset,
                kind,
                ref emitCache.RetainedTextBufferIndexPlusOne);
        }

        private bool TryUpdateRetainedWorldHudLanesDirect(
            Entity entity,
            ref PerformerState state,
            PerformerDefinition definition,
            ref PerformerCullState cull,
            ref PerformerWorldPosition position,
            ref PerformerWorldRotation rotation,
            ref PerformerWorldScale scale,
            ref PerformerEmitCache emitCache)
        {
            bool updated = false;
            if (definition.RetainedHudBarBehaviorIndex >= 0)
            {
                ref readonly BehaviorSlot barSlot = ref definition.Behaviors[definition.RetainedHudBarBehaviorIndex];
                if (barSlot.Kind == BehaviorKind.AssetBinding &&
                    IsBehaviorActive(state.BehaviorActiveMask, barSlot.SlotIndex))
                {
                    updated |= TryUpdateRetainedWorldHudLaneDirect(
                        entity,
                        ref state,
                        definition,
                        ref cull,
                        ref position,
                        ref rotation,
                        ref scale,
                        ref emitCache,
                        definition.RetainedHudBarBehaviorIndex,
                        in barSlot.AssetBinding,
                        WorldHudItemKind.Bar,
                        ref emitCache.RetainedBarBufferIndexPlusOne);
                }
                else
                {
                    RemoveRetainedWorldHudLane(
                        in state,
                        definition.Id,
                        WorldHudItemKind.Bar,
                        ref emitCache.RetainedBarBufferIndexPlusOne);
                }
            }

            if (definition.RetainedHudTextBehaviorIndex >= 0)
            {
                ref readonly BehaviorSlot textSlot = ref definition.Behaviors[definition.RetainedHudTextBehaviorIndex];
                if (textSlot.Kind == BehaviorKind.AssetBinding &&
                    IsBehaviorActive(state.BehaviorActiveMask, textSlot.SlotIndex))
                {
                    updated |= TryUpdateRetainedWorldHudLaneDirect(
                        entity,
                        ref state,
                        definition,
                        ref cull,
                        ref position,
                        ref rotation,
                        ref scale,
                        ref emitCache,
                        definition.RetainedHudTextBehaviorIndex,
                        in textSlot.AssetBinding,
                        WorldHudItemKind.Text,
                        ref emitCache.RetainedTextBufferIndexPlusOne);
                }
                else
                {
                    RemoveRetainedWorldHudLane(
                        in state,
                        definition.Id,
                        WorldHudItemKind.Text,
                        ref emitCache.RetainedTextBufferIndexPlusOne);
                }
            }

            if (!updated &&
                emitCache.RetainedBarBufferIndexPlusOne == 0 &&
                emitCache.RetainedTextBufferIndexPlusOne == 0)
            {
                return false;
            }

            byte retainedPresent = (byte)((emitCache.RetainedBarBufferIndexPlusOne > 0 || emitCache.RetainedTextBufferIndexPlusOne > 0) ? 1 : 0);
            UpdateEmitCache(
                ref emitCache,
                state.Version,
                position.Value,
                cull.OwnerCullVisible,
                definitionVisible: true,
                cull.LOD,
                emitCache.StableVisualPresent,
                retainedPresent);
            _runtime.ClearStaticDirty(ref emitCache);
            return true;
        }

        private bool TryUpdateRetainedWorldHudLaneDirect(
            Entity entity,
            ref PerformerState state,
            PerformerDefinition definition,
            ref PerformerCullState cull,
            ref PerformerWorldPosition position,
            ref PerformerWorldRotation rotation,
            ref PerformerWorldScale scale,
            ref PerformerEmitCache emitCache,
            int behaviorIndex,
            in AssetBindingConfig asset,
            WorldHudItemKind kind,
            ref int retainedBufferIndexPlusOne)
        {
            int stableId = HudItemIdentity.ComposeStableId(state.StableId, kind, state.DefId);
            bool visible = cull.OwnerCullVisible &&
                           IsWithinMaxLod(cull.LOD, in asset) &&
                           EvaluateVisibility(definition, state.OwnerEntity) &&
                           ResolveAssetVisibility(entity, in asset) &&
                           _worldHudBehavior.TryResolveProjection(
                               World,
                               _globals,
                               state.OwnerEntity,
                               cull.LOD,
                               kind,
                               definition.RequiredAttributeIds,
                               out PerformPhaseResult phaseResult) &&
                           IsWorldHudDebugEnabled(kind);
            if (!visible)
            {
                RemoveRetainedWorldHudLane(in state, definition.Id, kind, ref retainedBufferIndexPlusOne);
                return true;
            }

            Vector3 resolvedPosition = position.Value + definition.PositionOffset;
            Vector3 assetPosition = ResolveAssetPosition(resolvedPosition, rotation.Value, scale.Value, in asset);
            WorldHudItem next = kind == WorldHudItemKind.Bar
                ? BuildWorldHudBarItemDirect(entity, in state, in definition, in asset, stableId, assetPosition, in scale)
                : BuildWorldHudTextItemDirect(entity, in state, in definition, in asset, stableId, assetPosition);

            if (!_worldHudBuffer.TryAdd(in next, ref retainedBufferIndexPlusOne))
            {
                throw new InvalidOperationException(
                    $"WorldHudBatchBuffer overflowed while directly updating retained performer HUD stableId={stableId}.");
            }

            return true;
        }

        private void TryEmitRetainedWorldHudLanes(
            Entity entity,
            ref PerformerState state,
            PerformerDefinition definition,
            ref PerformerCullState cull,
            ref PerformerWorldPosition position,
            ref PerformerWorldRotation rotation,
            ref PerformerWorldScale scale,
            ref PerformerEmitCache emitCache)
        {
            if (_worldHudBuffer == null || !definition.HasRetainedWorldHudLanes)
            {
                return;
            }

            if (!cull.OwnerCullVisible)
            {
                RemoveRetainedWorldHudLanesIfPresent(in state, in definition, ref emitCache);
                return;
            }

            if (definition.RetainedHudBarBehaviorIndex >= 0)
            {
                ref readonly BehaviorSlot barSlot = ref definition.Behaviors[definition.RetainedHudBarBehaviorIndex];
                if (barSlot.Kind == BehaviorKind.AssetBinding &&
                    IsBehaviorActive(state.BehaviorActiveMask, barSlot.SlotIndex))
                {
                    TryUpdateRetainedWorldHudLaneDirect(
                        entity,
                        ref state,
                        definition,
                        ref cull,
                        ref position,
                        ref rotation,
                        ref scale,
                        ref emitCache,
                        definition.RetainedHudBarBehaviorIndex,
                        in barSlot.AssetBinding,
                        WorldHudItemKind.Bar,
                        ref emitCache.RetainedBarBufferIndexPlusOne);
                }
                else
                {
                    RemoveRetainedWorldHudLane(
                        in state,
                        definition.Id,
                        WorldHudItemKind.Bar,
                        ref emitCache.RetainedBarBufferIndexPlusOne);
                }
            }

            if (definition.RetainedHudTextBehaviorIndex >= 0)
            {
                ref readonly BehaviorSlot textSlot = ref definition.Behaviors[definition.RetainedHudTextBehaviorIndex];
                if (textSlot.Kind == BehaviorKind.AssetBinding &&
                    IsBehaviorActive(state.BehaviorActiveMask, textSlot.SlotIndex))
                {
                    TryUpdateRetainedWorldHudLaneDirect(
                        entity,
                        ref state,
                        definition,
                        ref cull,
                        ref position,
                        ref rotation,
                        ref scale,
                        ref emitCache,
                        definition.RetainedHudTextBehaviorIndex,
                        in textSlot.AssetBinding,
                        WorldHudItemKind.Text,
                        ref emitCache.RetainedTextBufferIndexPlusOne);
                }
                else
                {
                    RemoveRetainedWorldHudLane(
                        in state,
                        definition.Id,
                        WorldHudItemKind.Text,
                        ref emitCache.RetainedTextBufferIndexPlusOne);
                }
            }
        }

        private void RemoveRetainedWorldHudLane(
            in PerformerState state,
            int definitionId,
            WorldHudItemKind kind,
            ref int retainedBufferIndexPlusOne)
        {
            if (_worldHudBuffer == null || retainedBufferIndexPlusOne == 0)
            {
                retainedBufferIndexPlusOne = 0;
                return;
            }

            int stableId = HudItemIdentity.ComposeStableId(state.StableId, kind, definitionId);
            _worldHudBuffer.Remove(stableId, ref retainedBufferIndexPlusOne);
        }

        private void RemoveRetainedWorldHudLanesIfPresent(
            in PerformerState state,
            in PerformerDefinition definition,
            ref PerformerEmitCache emitCache)
        {
            if (_worldHudBuffer == null || !definition.HasRetainedWorldHudLanes)
            {
                return;
            }

            if (definition.RetainedHudBarBehaviorIndex >= 0)
            {
                RemoveRetainedWorldHudLane(
                    in state,
                    definition.Id,
                    WorldHudItemKind.Bar,
                    ref emitCache.RetainedBarBufferIndexPlusOne);
            }

            if (definition.RetainedHudTextBehaviorIndex >= 0)
            {
                RemoveRetainedWorldHudLane(
                    in state,
                    definition.Id,
                    WorldHudItemKind.Text,
                    ref emitCache.RetainedTextBufferIndexPlusOne);
            }
        }

        private bool IsWorldHudDebugEnabled(WorldHudItemKind kind)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.RenderDebugState.Name, out object? obj) ||
                obj is not RenderDebugState state)
            {
                return true;
            }

            return kind switch
            {
                WorldHudItemKind.Bar => state.DrawWorldHudBars,
                WorldHudItemKind.Text => state.DrawWorldHudText,
                _ => throw new InvalidOperationException($"Unsupported world HUD item kind '{kind}'."),
            };
        }

        private WorldHudItem BuildWorldHudBarItemDirect(
            Entity entity,
            in PerformerState state,
            in PerformerDefinition definition,
            in AssetBindingConfig asset,
            int stableId,
            in Vector3 worldPosition,
            in PerformerWorldScale performerScale)
        {
            Vector3 resolvedScale = ResolveAssetScale(entity, in asset, performerScale.Value);
            Vector4 foreground = ResolveAssetColor(entity, in asset, definition.DefaultColor);
            Vector4 background = new(0.2f, 0.2f, 0.2f, foreground.W);
            float value = asset.MaterialParamKey >= 0
                ? ResolveWorldHudFloatParam(entity, asset.MaterialParamKey, "AssetBinding.materialParamKey")
                : 1f;
            float width = resolvedScale.X > 0f ? resolvedScale.X : 40f;
            float height = resolvedScale.Y > 0f ? resolvedScale.Y : 6f;

            return new WorldHudItem
            {
                Owner = state.OwnerEntity,
                StableId = stableId,
                DirtySerial = HudItemIdentity.ComposeBarDirtySerial(width, height, value, background, foreground),
                Kind = WorldHudItemKind.Bar,
                WorldPosition = worldPosition,
                Value0 = value,
                Width = width,
                Height = height,
                Color0 = background,
                Color1 = foreground,
            };
        }

        private WorldHudItem BuildWorldHudTextItemDirect(
            Entity entity,
            in PerformerState state,
            in PerformerDefinition definition,
            in AssetBindingConfig asset,
            int stableId,
            in Vector3 worldPosition)
        {
            Vector4 color = ResolveAssetColor(entity, in asset, definition.DefaultColor);
            int tokenId = ResolveAssetId(entity, in asset);
            if (tokenId <= 0)
            {
                throw new InvalidOperationException(
                    $"WorldText AssetBinding for performer definition '{definition.Key}' resolved invalid asset id {tokenId}.");
            }

            float value0 = asset.ScaleParamKey >= 0
                ? ResolveWorldHudFloatParam(entity, asset.ScaleParamKey, "AssetBinding.scaleParamKey")
                : 0f;
            float value1 = asset.MaterialParamKey >= 0
                ? ResolveWorldHudFloatParam(entity, asset.MaterialParamKey, "AssetBinding.materialParamKey")
                : 0f;
            WorldHudValueMode valueMode = definition.WorldTextMode;
            int fontSize = definition.DefaultFontSize > 0 ? definition.DefaultFontSize : 16;
            int stringTableId = valueMode == WorldHudValueMode.None ? tokenId : 0;
            PresentationTextPacket packet = PresentationTextPacket.FromWorldHudValueMode(tokenId, valueMode, value0, value1);

            return new WorldHudItem
            {
                Owner = state.OwnerEntity,
                StableId = stableId,
                DirtySerial = HudItemIdentity.ComposeTextDirtySerial(fontSize, stringTableId, (int)valueMode, value0, value1, color, packet),
                Kind = WorldHudItemKind.Text,
                WorldPosition = worldPosition,
                Value0 = value0,
                Value1 = value1,
                Id0 = stringTableId,
                Id1 = (int)valueMode,
                FontSize = fontSize,
                Color0 = color,
                Text = packet,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ResolveAssetVisibility(Entity entity, in AssetBindingConfig asset)
        {
            return asset.VisibilityParamKey < 0 ||
                RequireIntParam(entity, asset.VisibilityParamKey, "AssetBinding.visibilityParamKey") != 0;
        }

        private int ResolveAssetId(Entity entity, in AssetBindingConfig asset)
        {
            if (asset.AssetIdParamKey >= 0)
            {
                if (!_runtime.TryResolveInt(entity, asset.AssetIdParamKey, out int assetId) || assetId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Performer AssetBinding assetIdParamKey {asset.AssetIdParamKey} did not resolve to a registered asset id.");
                }

                return assetId;
            }

            if (asset.AssetSwapParamKey < 0)
            {
                return asset.AssetId;
            }

            if (!_runtime.TryResolveInt(entity, asset.AssetSwapParamKey, out int resolved))
            {
                throw new InvalidOperationException(
                    $"Performer AssetBinding assetSwapParamKey {asset.AssetSwapParamKey} did not resolve to a swap value.");
            }

            AssetSwapEntry[] table = asset.AssetSwapTable ?? Array.Empty<AssetSwapEntry>();
            for (int i = 0; i < table.Length; i++)
            {
                ref readonly AssetSwapEntry entry = ref table[i];
                if (MathF.Abs(entry.ParamValue - resolved) <= 0.0001f)
                {
                    return entry.AssetId;
                }
            }

            throw new InvalidOperationException(
                $"Performer AssetBinding assetSwapParamKey {asset.AssetSwapParamKey} resolved value {resolved} with no matching assetSwapTable entry.");
        }

        private Vector3 ResolveAssetScale(Entity entity, in AssetBindingConfig asset, Vector3 performerWorldScale)
        {
            Vector3 resolved = performerWorldScale == Vector3.Zero ? Vector3.One : performerWorldScale;
            resolved *= asset.LocalScale == Vector3.Zero ? Vector3.One : asset.LocalScale;
            if (asset.ScaleParamKey >= 0)
            {
                resolved *= RequireFloatParam(entity, asset.ScaleParamKey, "AssetBinding.scaleParamKey");
            }

            return resolved;
        }

        private static Quaternion ResolveAssetRotation(in AssetBindingConfig asset, Quaternion performerWorldRotation)
        {
            return WorldPlane2D.ResolveVisualAssetRotation(in performerWorldRotation, in asset.LocalRotation);
        }

        private static Vector3 ResolveAssetPosition(
            Vector3 position,
            Quaternion performerWorldRotation,
            Vector3 performerWorldScale,
            in AssetBindingConfig asset)
        {
            return WorldPlane2D.ResolveVisualAssetPosition(
                in position,
                in performerWorldRotation,
                in performerWorldScale,
                in asset.LocalOffset);
        }

        private Vector4 ResolveAssetColor(Entity entity, in AssetBindingConfig asset, Vector4 defaultColor)
        {
            return asset.ColorParamKey >= 0
                ? RequireVectorParam(entity, asset.ColorParamKey, "AssetBinding.colorParamKey")
                : defaultColor;
        }

        private int ResolveMaterialId(Entity entity, in AssetBindingConfig asset)
        {
            if (asset.MaterialParamKey < 0)
            {
                return asset.MaterialId;
            }

            int materialId = RequireIntParam(entity, asset.MaterialParamKey, "AssetBinding.materialParamKey");
            if (materialId <= 0)
            {
                throw new InvalidOperationException(
                    $"AssetBinding.materialParamKey {asset.MaterialParamKey} resolved invalid material id {materialId}.");
            }

            return materialId;
        }

        private float ResolveWorldHudFloatParam(Entity entity, int paramKey, string context)
        {
            return RequireFloatParam(entity, paramKey, context);
        }

        private int RequireIntParam(Entity entity, int paramKey, string context)
        {
            if (!_runtime.TryResolveInt(entity, paramKey, out int value))
            {
                throw new InvalidOperationException($"{context} {paramKey} did not resolve to an int param value.");
            }

            return value;
        }

        private float RequireFloatParam(Entity entity, int paramKey, string context)
        {
            if (!_runtime.TryResolveFloat(entity, paramKey, out float value))
            {
                throw new InvalidOperationException($"{context} {paramKey} did not resolve to a float param value.");
            }

            return value;
        }

        private Vector4 RequireVectorParam(Entity entity, int paramKey, string context)
        {
            if (!_runtime.TryResolveVector(entity, paramKey, out Vector4 value))
            {
                throw new InvalidOperationException($"{context} {paramKey} did not resolve to a vector param value.");
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsWithinMaxLod(LODLevel lod, in AssetBindingConfig asset)
        {
            return lod != LODLevel.Culled && (!asset.HasMaxLod || lod <= asset.MaxLod);
        }

        private void ProcessDirtyStaticEmitEntities()
        {
            if (!_runtime.HasDirtyStaticVisuals)
            {
                if (_timingDiagnostics != null)
                {
                    _timingDiagnostics.ObservePerformerEmitDirtyBreakdown(processMs: 0d, cleanupMs: 0d, dirtyCount: 0);
                }

                return;
            }

            if (_stableDrawCache == null)
            {
                foreach (ref var chunk in World.Query(in DirtyStaticEmitQuery))
                {
                    ref Entity entityFirst = ref chunk.Entity(0);
                    var states = chunk.GetSpan<PerformerState>();
                    var culls = chunk.GetSpan<PerformerCullState>();
                    var positions = chunk.GetSpan<PerformerWorldPosition>();
                    var rotations = chunk.GetSpan<PerformerWorldRotation>();
                    var facings = chunk.GetSpan<PerformerWorldFacing>();
                    var scales = chunk.GetSpan<PerformerWorldScale>();
                    var emitCaches = chunk.GetSpan<PerformerEmitCache>();
                    foreach (var index in chunk)
                    {
                        if (emitCaches[index].StaticDirty == 0)
                        {
                            continue;
                        }

                        Entity entity = Unsafe.Add(ref entityFirst, index);
                        ProcessEmitEntity(
                            entity,
                            ref states[index],
                            ref culls[index],
                            ref positions[index],
                            ref rotations[index],
                            ref facings[index],
                            ref scales[index],
                            ref emitCaches[index],
                            deltaTime: 0f,
                            clearDirtyAfterProcessing: true);
                    }
                }
                return;
            }

            long processStart = _timingDiagnostics != null ? Stopwatch.GetTimestamp() : 0L;
            int cachedDefId = -1;
            PerformerDefinition? cachedDefinition = null;
            int dirtyCount = 0;
            foreach (ref var chunk in World.Query(in DirtyStaticEmitQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var states = chunk.GetSpan<PerformerState>();
                var culls = chunk.GetSpan<PerformerCullState>();
                var positions = chunk.GetSpan<PerformerWorldPosition>();
                var rotations = chunk.GetSpan<PerformerWorldRotation>();
                var facings = chunk.GetSpan<PerformerWorldFacing>();
                var scales = chunk.GetSpan<PerformerWorldScale>();
                var emitCaches = chunk.GetSpan<PerformerEmitCache>();
                foreach (var index in chunk)
                {
                    if (emitCaches[index].StaticDirty == 0)
                    {
                        continue;
                    }

                    dirtyCount++;
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ref PerformerState state = ref states[index];
                    if (state.DefId != cachedDefId)
                    {
                        cachedDefId = state.DefId;
                        cachedDefinition = _definitions.TryGet(state.DefId, out PerformerDefinition definition)
                            ? definition
                            : null;
                    }

                    if (cachedDefinition == null)
                    {
                        continue;
                    }

                    ProcessDirtyStaticStableEmit(
                        entity,
                        ref state,
                        ref culls[index],
                        ref positions[index],
                        ref rotations[index],
                        ref facings[index],
                        ref scales[index],
                        ref emitCaches[index],
                        cachedDefinition);
                }
            }

            double processMs = _timingDiagnostics != null
                ? (Stopwatch.GetTimestamp() - processStart) * 1000d / Stopwatch.Frequency
                : 0d;
            if (_timingDiagnostics != null)
            {
                _timingDiagnostics.ObservePerformerEmitDirtyBreakdown(processMs, cleanupMs: 0d, dirtyCount);
            }
        }

        private void ProcessRetainedPresentationRequestLifecycleEntities(float deltaTime)
        {
            foreach (ref var chunk in World.Query(in RetainedRequestLifecycleQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var states = chunk.GetSpan<PerformerState>();
                var emitCaches = chunk.GetSpan<PerformerEmitCache>();
                foreach (var index in chunk)
                {
                    ref PerformerEmitCache emitCache = ref emitCaches[index];
                    if (emitCache.RetainedRequestPresent == 0 || emitCache.RetainedDirty != 0)
                    {
                        continue;
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ref PerformerState state = ref states[index];
                    if (!_definitions.TryGet(state.DefId, out PerformerDefinition definition))
                    {
                        RemoveReplayCache(entity);
                        continue;
                    }

                    if (!definition.UsesRetainedPresentationRequest)
                    {
                        continue;
                    }

                    bool ownerDead = state.AnchorKind == PresentationAnchorKind.Entity && !World.IsAlive(state.OwnerEntity);
                    bool lifetimeExpired = state.DefaultLifetime > 0f && state.Elapsed + deltaTime >= state.DefaultLifetime;
                    bool hiddenByDefinition = !EvaluateVisibility(definition, state.OwnerEntity);
                    if (!ownerDead && !lifetimeExpired && !hiddenByDefinition)
                    {
                        continue;
                    }

                    RemoveRetainedPresentationRequestIfPresent(in state, in definition, ref emitCache);
                    RemoveSurfaceSourceIfPresent(in state, in definition, ref emitCache);
                    RemoveReplayCache(entity);
                    if (ownerDead || lifetimeExpired)
                    {
                        _pendingDestroy.Add(entity);
                    }
                }
            }
        }

        private void ProcessEmitEntity(
            Entity entity,
            ref PerformerState state,
            ref PerformerCullState cull,
            ref PerformerWorldPosition position,
            ref PerformerWorldRotation rotation,
            ref PerformerWorldFacing facing,
            ref PerformerWorldScale scale,
            ref PerformerEmitCache emitCache,
            float deltaTime,
            bool clearDirtyAfterProcessing)
        {
            state.Elapsed += deltaTime;
            if (!_definitions.TryGet(state.DefId, out PerformerDefinition definition))
            {
                RemoveReplayCache(entity);
                ClearDirtyIfNeeded(ref emitCache, clearDirtyAfterProcessing);
                return;
            }

            if (state.AnchorKind == PresentationAnchorKind.Entity && !World.IsAlive(state.OwnerEntity))
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
                RemoveRetainedPresentationRequestIfPresent(in state, in definition, ref emitCache);
                RemoveSurfaceSourceIfPresent(in state, in definition, ref emitCache);
                RemoveReplayCache(entity);
                _pendingDestroy.Add(entity);
                ClearDirtyIfNeeded(ref emitCache, clearDirtyAfterProcessing);
                return;
            }

            if (state.DefaultLifetime > 0f && state.Elapsed >= state.DefaultLifetime)
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
                RemoveRetainedPresentationRequestIfPresent(in state, in definition, ref emitCache);
                RemoveSurfaceSourceIfPresent(in state, in definition, ref emitCache);
                RemoveReplayCache(entity);
                _pendingDestroy.Add(entity);
                ClearDirtyIfNeeded(ref emitCache, clearDirtyAfterProcessing);
                return;
            }

            bool ownerCullVisible = cull.OwnerCullVisible;
            if (TryProcessSingleVisualProxyFastEntity(
                    entity,
                    in state,
                    definition,
                    ref cull,
                    ref position,
                    ref rotation,
                    ref facing,
                    ref scale,
                    ref emitCache,
                    ownerCullVisible,
                    clearDirtyAfterProcessing))
            {
                return;
            }

            bool definitionVisible = EvaluateVisibility(definition, state.OwnerEntity);
            bool stableCacheEligible = _stableDrawCache != null && definition.UsesStableVisualCache;
            if (!stableCacheEligible && emitCache.StableVisualPresent != 0)
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
            }

            if (!definitionVisible)
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
                RemoveRetainedPresentationRequestIfPresent(in state, in definition, ref emitCache);
                RemoveSurfaceSourceIfPresent(in state, in definition, ref emitCache);
                RemoveReplayCache(entity);
                UpdateEmitCache(
                    ref emitCache,
                    state.Version,
                    position.Value,
                    ownerCullVisible,
                    definitionVisible,
                    cull.LOD,
                    emitCache.StableVisualPresent,
                    emitCache.RetainedRequestPresent);
                ClearDirtyIfNeeded(ref emitCache, clearDirtyAfterProcessing);
                return;
            }

            bool retainedCullState = stableCacheEligible || definition.UsesRetainedPresentationRequest;
            if (!ownerCullVisible && !retainedCullState)
            {
                RemoveReplayCache(entity);
                UpdateEmitCache(
                    ref emitCache,
                    state.Version,
                    position.Value,
                    ownerCullVisible,
                    true,
                    cull.LOD,
                    stableVisualPresent: 0,
                    retainedRequestPresent: 0);
                ClearDirtyIfNeeded(ref emitCache, clearDirtyAfterProcessing);
                return;
            }

            bool versionClean = emitCache.CachedVersion == state.Version;
            bool positionClean = emitCache.LastEmitPosition == position.Value;
            bool ownerCullClean = emitCache.LastOwnerCullVisible == (ownerCullVisible ? (byte)1 : (byte)0);
            bool definitionVisibleClean = emitCache.LastDefinitionVisible == 1;
            bool lodClean = emitCache.LastLod == cull.LOD;
            bool replayEligible = definition.SupportsSingleRequestReplay;
            if (stableCacheEligible && emitCache.StableVisualPresent != 0)
            {
                if (versionClean && positionClean && ownerCullClean && definitionVisibleClean && lodClean)
                {
                    ClearDirtyIfNeeded(ref emitCache, clearDirtyAfterProcessing);
                    return;
                }

                if (versionClean && !positionClean && ownerCullClean && definitionVisibleClean && lodClean)
                {
                    UpdateStableVisualPositions(in state, in definition, position.Value);
                    UpdateEmitCache(ref emitCache, state.Version, position.Value, ownerCullVisible, true, cull.LOD, stableVisualPresent: 1, emitCache.RetainedRequestPresent);
                    ClearDirtyIfNeeded(ref emitCache, clearDirtyAfterProcessing);
                    return;
                }
            }

            if (replayEligible &&
                !definition.UsesRetainedPresentationRequest &&
                versionClean &&
                positionClean &&
                ownerCullClean &&
                definitionVisibleClean &&
                lodClean &&
                TryReplayCachedRequest(entity))
            {
                ClearDirtyIfNeeded(ref emitCache, clearDirtyAfterProcessing);
                return;
            }

            int emitRequestStartCount = _requests.Count;
            if (ownerCullVisible && definition.HasSurfaceAuthoring)
            {
                EmitSurfaceSourceIfAny(in state, position.Value, definition, cull.LOD);
            }

            bool emittedStableVisual = false;
            int requestStartCount = replayEligible ? _requests.Count : -1;
            if (definition.HasAssetBindingBehavior)
            {
                emittedStableVisual =
                    stableCacheEligible &&
                    clearDirtyAfterProcessing &&
                    definition.UsesEventDrivenStaticEmit
                        ? _assetEmitter.EmitStaticStableVisualDirect(
                            entity,
                            in state,
                            in definition,
                            cull.LOD,
                            position.Value,
                            rotation.Value,
                            in facing,
                            scale.Value,
                            _stableDrawCache!,
                            addOnly: emitCache.StableVisualPresent == 0)
                        : definition.SupportsVisualProxyFastEmit
                            ? EmitVisualProxyFast(
                                entity,
                                in state,
                                definition,
                                cull.LOD,
                                position.Value,
                                rotation.Value,
                                in facing,
                                scale.Value)
                        : EmitAssetBindings(
                            entity,
                            in state,
                            definition,
                            cull.LOD,
                            position.Value,
                            rotation.Value,
                            in facing,
                            scale.Value);
            }

            byte retainedRequestPresent = definition.UsesRetainedPresentationRequest
                ? (_requests.Count > emitRequestStartCount ? (byte)1 : (byte)0)
                : (byte)0;
            if (definition.HasSurfaceAuthoring && emitCache.RetainedRequestPresent != 0)
            {
                retainedRequestPresent = 1;
            }

            if (ownerCullVisible && !emittedStableVisual && _requests.Count == emitRequestStartCount && emitCache.CachedVersion != 0)
            {
                if (definition.UsesRetainedPresentationRequest && emitCache.RetainedRequestPresent != 0)
                {
                    RemoveRetainedPresentationRequestIfPresent(in state, in definition, ref emitCache);
                    RemoveSurfaceSourceIfPresent(in state, in definition, ref emitCache);
                    retainedRequestPresent = 0;
                }
                else if (DefinitionHasTransientVisualBindings(definition))
                {
                    _requests.Add(PresentationRequest.ClearTransientVisualProjection(state.OwnerEntity));
                }
            }

            UpdateReplayCache(entity, replayEligible, requestStartCount);

            if (stableCacheEligible && !emittedStableVisual && emitCache.StableVisualPresent != 0)
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
            }

            UpdateEmitCache(
                ref emitCache,
                state.Version,
                position.Value,
                ownerCullVisible,
                true,
                cull.LOD,
                stableCacheEligible && emittedStableVisual ? (byte)1 : (byte)0,
                retainedRequestPresent);
            ClearDirtyIfNeeded(ref emitCache, clearDirtyAfterProcessing);
        }

        private bool TryProcessSingleVisualProxyFastEntity(
            Entity entity,
            in PerformerState state,
            PerformerDefinition definition,
            ref PerformerCullState cull,
            ref PerformerWorldPosition position,
            ref PerformerWorldRotation rotation,
            ref PerformerWorldFacing facing,
            ref PerformerWorldScale scale,
            ref PerformerEmitCache emitCache,
            bool ownerCullVisible,
            bool clearDirtyAfterProcessing)
        {
            if (!definition.SupportsVisualProxyFastEmit ||
                definition.HasSurfaceAuthoring ||
                definition.UsesStableVisualCache ||
                definition.UsesRetainedPresentationRequest ||
                definition.DefaultLifetime > 0f ||
                definition.PositionYDriftPerSecond != 0f ||
                definition.AlphaFadeOverLifetime ||
                definition.VisibilityCondition.Inline != InlineConditionKind.None ||
                definition.VisibilityCondition.GraphProgramId > 0)
            {
                return false;
            }

            if (!ownerCullVisible)
            {
                RemoveReplayCache(entity);
                UpdateEmitCache(
                    ref emitCache,
                    state.Version,
                    position.Value,
                    ownerCullVisible: false,
                    definitionVisible: true,
                    cull.LOD,
                    stableVisualPresent: 0,
                    retainedRequestPresent: 0);
                ClearDirtyIfNeeded(ref emitCache, clearDirtyAfterProcessing);
                return true;
            }

            EmitVisualProxyFast(
                entity,
                in state,
                definition,
                cull.LOD,
                position.Value,
                rotation.Value,
                in facing,
                scale.Value);
            UpdateEmitCache(
                ref emitCache,
                state.Version,
                position.Value,
                ownerCullVisible: true,
                definitionVisible: true,
                cull.LOD,
                stableVisualPresent: 0,
                retainedRequestPresent: 0);
            ClearDirtyIfNeeded(ref emitCache, clearDirtyAfterProcessing);
            return true;
        }

        private bool ProcessSingleVisualProxyFastChunkEntity(
            Entity entity,
            ref PerformerState state,
            PerformerDefinition definition,
            ref PerformerCullState cull,
            ref PerformerWorldPosition position,
            ref PerformerWorldRotation rotation,
            ref PerformerWorldFacing facing,
            ref PerformerWorldScale scale,
            ref PerformerEmitCache emitCache,
            float deltaTime,
            int animatorSlot,
            ref int singleVisualFastCount)
        {
            state.Elapsed += deltaTime;

            bool useSingleVisualFastPath = definition.SupportsSingleVisualProxyFastEmit;
            if (useSingleVisualFastPath)
            {
                singleVisualFastCount++;
            }

            if (state.AnchorKind == PresentationAnchorKind.Entity && !World.IsAlive(state.OwnerEntity))
            {
                RemoveReplayCache(entity);
                RemoveRetainedWorldHudLanesIfPresent(in state, in definition, ref emitCache);
                _pendingDestroy.Add(entity);
                UpdateEmitCache(
                    ref emitCache,
                    state.Version,
                    position.Value,
                    ownerCullVisible: false,
                    definitionVisible: true,
                    cull.LOD,
                    stableVisualPresent: 0,
                    retainedRequestPresent: 0);
                return true;
            }

            bool ownerCullVisible = cull.OwnerCullVisible;
            if (!ownerCullVisible)
            {
                RemoveReplayCache(entity);
                RemoveRetainedWorldHudLanesIfPresent(in state, in definition, ref emitCache);
                UpdateEmitCache(
                    ref emitCache,
                    state.Version,
                    position.Value,
                    ownerCullVisible: false,
                    definitionVisible: true,
                    cull.LOD,
                    stableVisualPresent: 0,
                    retainedRequestPresent: 0);
                return true;
            }

            if (useSingleVisualFastPath)
            {
                EmitSingleVisualProxyFast(
                    entity,
                    in state,
                    definition,
                    cull.LOD,
                    position.Value,
                    rotation.Value,
                    in facing,
                    scale.Value,
                    animatorSlot);
                TryEmitRetainedWorldHudLanes(
                    entity,
                    ref state,
                    definition,
                    ref cull,
                    ref position,
                    ref rotation,
                    ref scale,
                    ref emitCache);
            }
            else
            {
                EmitVisualProxyFast(
                    entity,
                    in state,
                    definition,
                    cull.LOD,
                    position.Value,
                    rotation.Value,
                    in facing,
                    scale.Value,
                    animatorSlot);
            }

            UpdateEmitCache(
                ref emitCache,
                state.Version,
                position.Value,
                ownerCullVisible: true,
                definitionVisible: true,
                cull.LOD,
                stableVisualPresent: 0,
                retainedRequestPresent: 0);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EmitSingleVisualProxyFast(
            Entity entity,
            in PerformerState state,
            PerformerDefinition definition,
            LODLevel lod,
            Vector3 performerWorldPosition,
            Quaternion performerWorldRotation,
            in PerformerWorldFacing performerWorldFacing,
            Vector3 performerWorldScale,
            int animatorSlot)
        {
            ref readonly BehaviorSlot slot = ref definition.Behaviors[definition.SingleVisualProxyFastBehaviorIndex];
            if (!IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
            {
                return;
            }

            ref readonly AssetBindingConfig asset = ref slot.AssetBinding;
            VisualVisibility visibility = lod == LODLevel.Culled || (asset.HasMaxLod && lod > asset.MaxLod)
                ? VisualVisibility.Culled
                : VisualVisibility.Visible;
            Vector3 resolvedPosition = performerWorldPosition + definition.PositionOffset;
            if (visibility == VisualVisibility.Visible &&
                TryEmitSingleSkinnedVisualBatchFast(
                    entity,
                    in state,
                    definition,
                    slot.SlotIndex,
                    in asset,
                    lod,
                    resolvedPosition,
                    performerWorldRotation,
                    in performerWorldFacing,
                    performerWorldScale,
                    animatorSlot))
            {
                return;
            }

            _requests.Add(PresentationRequest.FromVisualProxy(
                state.OwnerEntity,
                BuildSingleVisualProxyFast(
                    entity,
                    in state,
                    definition,
                    slot.SlotIndex,
                    in asset,
                    lod,
                    resolvedPosition,
                    performerWorldRotation,
                    in performerWorldFacing,
                    performerWorldScale,
                    visibility,
                    animatorSlot)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryEmitSingleSkinnedVisualBatchFast(
            Entity entity,
            in PerformerState state,
            PerformerDefinition definition,
            int slotIndex,
            in AssetBindingConfig asset,
            LODLevel lod,
            Vector3 resolvedPosition,
            Quaternion performerWorldRotation,
            in PerformerWorldFacing performerWorldFacing,
            Vector3 performerWorldScale,
            int animatorSlot)
        {
            if (_skinnedVisualBatchBuffer == null || asset.AssetKind != AssetKind.SkinnedMesh)
            {
                return false;
            }

            VisualRenderPath renderPath = asset.RenderPath;
            if (renderPath == VisualRenderPath.None)
            {
                throw new InvalidOperationException("SkinnedMesh AssetBinding requires an explicit skinned renderPath.");
            }

            if (!renderPath.IsSkinnedLane())
            {
                return false;
            }

            if (!_skinnedVisualBatchBuffer.TryAddDirect(new SkinnedVisualBatchItem
            {
                Payload = new VisualRenderPayload
                {
                    MeshAssetId = asset.AssetId,
                    Position = ResolveAssetPosition(resolvedPosition, performerWorldRotation, performerWorldScale, in asset),
                    Rotation = ResolveAssetRotation(in asset, performerWorldRotation),
                    Scale = ResolveStaticAssetScale(performerWorldScale, in asset),
                    Color = definition.DefaultColor,
                    StableId = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slotIndex, asset.AssetKind, state.DefId),
                    MaterialId = asset.MaterialId,
                    TemplateId = state.DefId,
                    AnimationProfileId = definition.AnimationProfileId,
                    RenderPath = renderPath,
                    AssetKind = asset.AssetKind,
                    SurfaceLayerKey = asset.SurfaceLayerKey,
                    SortId = asset.SortId,
                    Animator = ResolveAnimatorFast(entity, animatorSlot),
                    AnimationOverlay = ResolveAnimationOverlayFast(entity, renderPath, animatorSlot),
                    Visibility = VisualVisibility.Visible,
                },
                LOD = lod,
            }))
            {
                throw new InvalidOperationException(
                    $"Skinned visual batch buffer overflowed while single-fast-emitting stableId={state.StableId}, definitionId={state.DefId}.");
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PresentationVisualProxy BuildSingleVisualProxyFast(
            Entity entity,
            in PerformerState state,
            PerformerDefinition definition,
            int slotIndex,
            in AssetBindingConfig asset,
            LODLevel lod,
            Vector3 resolvedPosition,
            Quaternion performerWorldRotation,
            in PerformerWorldFacing performerWorldFacing,
            Vector3 performerWorldScale,
            VisualVisibility visibility,
            int animatorSlot)
        {
            VisualRenderPath renderPath = asset.RenderPath;
            if (renderPath == VisualRenderPath.None)
            {
                throw new InvalidOperationException(
                    $"Visual AssetBinding assetKind '{asset.AssetKind}' requires an explicit renderPath.");
            }

            return new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Performer,
                MeshAssetId = asset.AssetId,
                Position = ResolveAssetPosition(resolvedPosition, performerWorldRotation, performerWorldScale, in asset),
                Rotation = ResolveAssetRotation(in asset, performerWorldRotation),
                Scale = ResolveStaticAssetScale(performerWorldScale, in asset),
                Color = definition.DefaultColor,
                StableId = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slotIndex, asset.AssetKind, state.DefId),
                MaterialId = asset.MaterialId,
                TemplateId = state.DefId,
                AnimationProfileId = definition.AnimationProfileId,
                RenderPath = renderPath,
                AssetKind = asset.AssetKind,
                SurfaceLayerKey = asset.SurfaceLayerKey,
                SortId = asset.SortId,
                Mobility = asset.Mobility,
                Flags = VisualRuntimeFlags.Visible,
                Animator = renderPath.SupportsAnimatorPackedState() ? ResolveAnimatorFast(entity, animatorSlot) : default,
                AnimationOverlay = ResolveAnimationOverlayFast(entity, renderPath, animatorSlot),
                Visibility = visibility,
                LOD = lod,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 ResolveStaticAssetScale(Vector3 performerWorldScale, in AssetBindingConfig asset)
        {
            Vector3 resolved = performerWorldScale == Vector3.Zero ? Vector3.One : performerWorldScale;
            return resolved * (asset.LocalScale == Vector3.Zero ? Vector3.One : asset.LocalScale);
        }

        private static bool IsVisualProxyFastDefinition(PerformerDefinition definition)
        {
            return definition.SupportsVisualProxyFastEmit &&
                   !definition.HasSurfaceAuthoring &&
                   !definition.UsesStableVisualCache &&
                   !definition.UsesRetainedPresentationRequest &&
                   definition.DefaultLifetime <= 0f &&
                   definition.PositionYDriftPerSecond == 0f &&
                   !definition.AlphaFadeOverLifetime &&
                   definition.VisibilityCondition.Inline == InlineConditionKind.None &&
                   definition.VisibilityCondition.GraphProgramId <= 0;
        }

        private static bool DefinitionHasTransientVisualBindings(PerformerDefinition definition)
        {
            int[] assetBehaviorIndices = definition.AssetBehaviorIndices;
            BehaviorSlot[] behaviors = definition.Behaviors;
            for (int i = 0; i < assetBehaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[assetBehaviorIndices[i]];
                if (slot.AssetBinding.Mobility == VisualMobility.Movable ||
                    slot.AssetBinding.AssetKind == AssetKind.SkinnedMesh ||
                    slot.AssetBinding.AssetKind == AssetKind.Mesh ||
                    slot.AssetBinding.AssetKind == AssetKind.Decal ||
                    slot.AssetBinding.AssetKind == AssetKind.VFX ||
                    slot.AssetBinding.AssetKind == AssetKind.Surface)
                {
                    return true;
                }
            }

            return false;
        }

        private void ClearDirtyIfNeeded(ref PerformerEmitCache emitCache, bool clearDirtyAfterProcessing)
        {
            if (clearDirtyAfterProcessing)
            {
                _runtime.ClearStaticDirty(ref emitCache);
            }
        }

        private void ProcessDirtyStaticStableEmit(
            Entity entity,
            ref PerformerState state,
            ref PerformerCullState cull,
            ref PerformerWorldPosition position,
            ref PerformerWorldRotation rotation,
            ref PerformerWorldFacing facing,
            ref PerformerWorldScale scale,
            ref PerformerEmitCache emitCache,
            PerformerDefinition definition)
        {
            if (state.AnchorKind == PresentationAnchorKind.Entity && !World.IsAlive(state.OwnerEntity))
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
                RemoveRetainedPresentationRequestIfPresent(in state, in definition, ref emitCache);
                RemoveSurfaceSourceIfPresent(in state, in definition, ref emitCache);
                RemoveReplayCache(entity);
                _pendingDestroy.Add(entity);
                _runtime.ClearStaticDirty(ref emitCache);
                return;
            }

            bool ownerCullVisible = cull.OwnerCullVisible;
            bool definitionVisible = EvaluateVisibility(definition, state.OwnerEntity);
            byte stableVisualPresent = emitCache.StableVisualPresent != 0
                ? (HasStaticStableVisuals(in state, in definition) ? (byte)1 : (byte)0)
                : (byte)0;
            emitCache.StableVisualPresent = stableVisualPresent;
            bool versionClean = emitCache.CachedVersion == state.Version;
            bool positionClean = emitCache.LastEmitPosition == position.Value;
            bool ownerCullClean = emitCache.LastOwnerCullVisible == (ownerCullVisible ? (byte)1 : (byte)0);
            bool definitionVisibleClean = emitCache.LastDefinitionVisible == 1;
            bool lodClean = emitCache.LastLod == cull.LOD;

            if (!definitionVisible)
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
                RemoveRetainedPresentationRequestIfPresent(in state, in definition, ref emitCache);
                RemoveSurfaceSourceIfPresent(in state, in definition, ref emitCache);
                RemoveReplayCache(entity);
                UpdateEmitCache(ref emitCache, state.Version, position.Value, ownerCullVisible, false, cull.LOD, stableVisualPresent: 0, retainedRequestPresent: 0);
                _runtime.ClearStaticDirty(ref emitCache);
                return;
            }

            if (stableVisualPresent != 0)
            {
                if (versionClean && positionClean && ownerCullClean && definitionVisibleClean && lodClean)
                {
                    _runtime.ClearStaticDirty(ref emitCache);
                    return;
                }

                if (versionClean && !positionClean && ownerCullClean && definitionVisibleClean && lodClean)
                {
                    UpdateStableVisualPositions(in state, in definition, position.Value);
                    UpdateEmitCache(ref emitCache, state.Version, position.Value, ownerCullVisible, true, cull.LOD, stableVisualPresent: 1, emitCache.RetainedRequestPresent);
                    _runtime.ClearStaticDirty(ref emitCache);
                    return;
                }
            }

            bool emittedStableVisual = _assetEmitter.EmitStaticStableVisualDirect(
                entity,
                in state,
                in definition,
                cull.LOD,
                position.Value,
                rotation.Value,
                in facing,
                scale.Value,
                _stableDrawCache!,
                addOnly: stableVisualPresent == 0);

            if (!emittedStableVisual && emitCache.StableVisualPresent != 0)
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
            }

            UpdateEmitCache(
                ref emitCache,
                state.Version,
                position.Value,
                ownerCullVisible,
                true,
                cull.LOD,
                emittedStableVisual ? (byte)1 : (byte)0,
                emitCache.RetainedRequestPresent);
            _runtime.ClearStaticDirty(ref emitCache);
        }

        private void EmitSurfaceSourceIfAny(in PerformerState state, Vector3 worldPos, PerformerDefinition definition, LODLevel lod)
        {
            SurfaceAuthoringBlock? surface = definition.Surface;
            if (surface == null)
            {
                return;
            }

            _requests.Add(PresentationRequest.FromSurfaceSource(state.OwnerEntity, new SurfaceSourceRequest
            {
                StableId = state.StableId,
                PerformerDefinitionId = state.DefId,
                ScopeId = state.ScopeId,
                SurfaceKind = surface.Kind,
                Authoring = surface,
                AnchorPosition = worldPos + definition.PositionOffset,
                LodSeed = lod,
            }, lod));
        }

        private bool EmitAssetBindings(
            Entity entity,
            in PerformerState state,
            PerformerDefinition definition,
            LODLevel lod,
            Vector3 performerWorldPosition,
            Quaternion performerWorldRotation,
            in PerformerWorldFacing performerWorldFacing,
            Vector3 performerWorldScale,
            int animatorSlot = -1)
        {
            int[] assetBehaviorIndices = definition.AssetBehaviorIndices;
            if (assetBehaviorIndices.Length == 0)
            {
                return false;
            }

            bool emittedStableVisual = false;
            BehaviorSlot[] behaviors = definition.Behaviors;
            for (int i = 0; i < assetBehaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[assetBehaviorIndices[i]];
                if (!IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                {
                    continue;
                }

                ref readonly AssetBindingConfig asset = ref slot.AssetBinding;
                if (TryEmitSkinnedVisualBatchFast(
                        entity,
                        in state,
                        definition,
                        slot.SlotIndex,
                        in asset,
                        lod,
                        performerWorldPosition + definition.PositionOffset,
                        performerWorldRotation,
                        in performerWorldFacing,
                        performerWorldScale,
                        animatorSlot))
                {
                    emittedStableVisual = true;
                    continue;
                }

                _assetEmitter.Emit(
                    entity,
                    in state,
                    in definition,
                    slot.SlotIndex,
                    in asset,
                    lod,
                    performerWorldPosition,
                    performerWorldRotation,
                    in performerWorldFacing,
                    performerWorldScale);
                emittedStableVisual |= IsCacheableVisualKind(asset.AssetKind);
            }

            return emittedStableVisual;
        }

        private bool EmitVisualProxyFast(
            Entity entity,
            in PerformerState state,
            PerformerDefinition definition,
            LODLevel lod,
            Vector3 performerWorldPosition,
            Quaternion performerWorldRotation,
            in PerformerWorldFacing performerWorldFacing,
            Vector3 performerWorldScale,
            int animatorSlot = -1)
        {
            int[] assetBehaviorIndices = definition.AssetBehaviorIndices;
            if (assetBehaviorIndices.Length == 0)
            {
                return false;
            }

            bool emittedStableVisual = false;
            BehaviorSlot[] behaviors = definition.Behaviors;
            Vector3 resolvedPosition = performerWorldPosition + definition.PositionOffset;
            for (int i = 0; i < assetBehaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[assetBehaviorIndices[i]];
                if (!IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                {
                    continue;
                }

                ref readonly AssetBindingConfig asset = ref slot.AssetBinding;
                VisualVisibility visibility = lod == LODLevel.Culled || (asset.HasMaxLod && lod > asset.MaxLod)
                    ? VisualVisibility.Culled
                    : VisualVisibility.Visible;
                if (visibility == VisualVisibility.Visible &&
                    TryEmitSkinnedVisualBatchFast(
                        entity,
                        in state,
                        definition,
                        slot.SlotIndex,
                        in asset,
                        lod,
                        resolvedPosition,
                        performerWorldRotation,
                        in performerWorldFacing,
                        performerWorldScale,
                        animatorSlot))
                {
                    continue;
                }

                _requests.Add(PresentationRequest.FromVisualProxy(
                    state.OwnerEntity,
                    BuildVisualProxyFast(
                        entity,
                        in state,
                        definition,
                        slot.SlotIndex,
                        in asset,
                        lod,
                        resolvedPosition,
                        performerWorldRotation,
                        in performerWorldFacing,
                        performerWorldScale,
                        visibility,
                        animatorSlot)));
                emittedStableVisual |= IsCacheableVisualKind(asset.AssetKind);
            }

            return emittedStableVisual;
        }

        private bool TryEmitSkinnedVisualBatchFast(
            Entity entity,
            in PerformerState state,
            PerformerDefinition definition,
            int slotIndex,
            in AssetBindingConfig asset,
            LODLevel lod,
            Vector3 resolvedPosition,
            Quaternion performerWorldRotation,
            in PerformerWorldFacing performerWorldFacing,
            Vector3 performerWorldScale,
            int animatorSlot = -1)
        {
            if (_skinnedVisualBatchBuffer == null ||
                asset.AssetKind != AssetKind.SkinnedMesh ||
                lod == LODLevel.Culled ||
                (asset.HasMaxLod && lod > asset.MaxLod) ||
                !ResolveAssetVisibility(entity, in asset))
            {
                return false;
            }

            VisualRenderPath renderPath = asset.RenderPath;
            if (renderPath == VisualRenderPath.None)
            {
                throw new InvalidOperationException("SkinnedMesh AssetBinding requires an explicit skinned renderPath.");
            }

            if (!renderPath.IsSkinnedLane())
            {
                return false;
            }

            if (!_skinnedVisualBatchBuffer.TryAddDirect(new SkinnedVisualBatchItem
            {
                MeshAssetId = ResolveAssetId(entity, in asset),
                Position = ResolveAssetPosition(resolvedPosition, performerWorldRotation, performerWorldScale, in asset),
                Rotation = ResolveAssetRotation(in asset, performerWorldRotation),
                Scale = ResolveAssetScale(entity, in asset, performerWorldScale),
                Color = ResolveAssetColor(entity, in asset, definition.DefaultColor),
                StableId = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slotIndex, asset.AssetKind, state.DefId),
                MaterialId = ResolveMaterialId(entity, in asset),
                TemplateId = state.DefId,
                AnimationProfileId = definition.AnimationProfileId,
                RenderPath = renderPath,
                AssetKind = asset.AssetKind,
                SurfaceLayerKey = asset.SurfaceLayerKey,
                SortId = asset.SortId,
                MaterialCustomData = PerformerMaterialCustomDataResolver.Resolve(_runtime, entity, in asset.MaterialCustomData),
                Animator = ResolveAnimatorFast(entity, animatorSlot),
                AnimationOverlay = ResolveAnimationOverlayFast(entity, renderPath, animatorSlot),
                Visibility = VisualVisibility.Visible,
                LOD = lod,
            }))
            {
                throw new InvalidOperationException(
                    $"Skinned visual batch buffer overflowed while fast-emitting stableId={state.StableId}, definitionId={state.DefId}.");
            }

            return true;
        }

        private PresentationVisualProxy BuildVisualProxyFast(
            Entity entity,
            in PerformerState state,
            PerformerDefinition definition,
            int slotIndex,
            in AssetBindingConfig asset,
            LODLevel lod,
            Vector3 resolvedPosition,
            Quaternion performerWorldRotation,
            in PerformerWorldFacing performerWorldFacing,
            Vector3 performerWorldScale,
            VisualVisibility visibility,
            int animatorSlot = -1)
        {
            VisualRenderPath renderPath = asset.RenderPath;
            if (renderPath == VisualRenderPath.None)
            {
                throw new InvalidOperationException(
                    $"Visual AssetBinding assetKind '{asset.AssetKind}' requires an explicit renderPath.");
            }

            return new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Performer,
                MeshAssetId = ResolveAssetId(entity, in asset),
                Position = ResolveAssetPosition(resolvedPosition, performerWorldRotation, performerWorldScale, in asset),
                Rotation = ResolveAssetRotation(in asset, performerWorldRotation),
                Scale = ResolveAssetScale(entity, in asset, performerWorldScale),
                Color = ResolveAssetColor(entity, in asset, definition.DefaultColor),
                StableId = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slotIndex, asset.AssetKind, state.DefId),
                MaterialId = ResolveMaterialId(entity, in asset),
                TemplateId = state.DefId,
                AnimationProfileId = definition.AnimationProfileId,
                RenderPath = renderPath,
                AssetKind = asset.AssetKind,
                SurfaceLayerKey = asset.SurfaceLayerKey,
                SortId = asset.SortId,
                MaterialCustomData = PerformerMaterialCustomDataResolver.Resolve(_runtime, entity, in asset.MaterialCustomData),
                Mobility = asset.Mobility,
                Flags = VisualRuntimeFlags.Visible,
                Animator = renderPath.SupportsAnimatorPackedState() ? ResolveAnimatorFast(entity, animatorSlot) : default,
                AnimationOverlay = ResolveAnimationOverlayFast(entity, renderPath, animatorSlot),
                Visibility = visibility,
                LOD = lod,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AnimatorPackedState ResolveAnimatorFast(Entity entity, int animatorSlot = -1)
        {
            if (animatorSlot >= 0)
            {
                return _assetEmitter.GetAnimatorPackedStateBySlot(animatorSlot);
            }

            return _assetEmitter.TryGetAnimatorPackedState(entity, out AnimatorPackedState state)
                ? state
                : default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AnimationOverlayRequest ResolveAnimationOverlayFast(Entity entity, VisualRenderPath renderPath, int animatorSlot = -1)
        {
            if (!renderPath.SupportsAnimatorPackedState())
            {
                return default;
            }

            if (animatorSlot >= 0)
            {
                return _assetEmitter.GetAnimationOverlayBySlot(animatorSlot);
            }

            return _assetEmitter.TryGetAnimationOverlay(entity, out AnimationOverlayRequest overlay)
                ? overlay
                : default;
        }

        private static bool IsBehaviorActive(uint mask, int slotIndex)
        {
            return slotIndex is >= 0 and < 32 && (mask & (1u << slotIndex)) != 0;
        }

        private static bool IsCacheableVisualKind(AssetKind kind)
        {
            return kind is AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Decal or AssetKind.VFX or AssetKind.Surface;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryReplayCachedRequest(Entity entity)
        {
            if (!_singleRequestReplayCache.TryGetValue(entity, out PresentationRequest request))
            {
                return false;
            }

            _requests.Add(request);
            return true;
        }

        private void UpdateReplayCache(Entity entity, bool replayEligible, int requestStartCount)
        {
            if (!replayEligible)
            {
                RemoveReplayCache(entity);
                return;
            }

            int emittedCount = _requests.Count - requestStartCount;
            if (emittedCount != 1)
            {
                RemoveReplayCache(entity);
                return;
            }

            _singleRequestReplayCache[entity] = _requests.Get(requestStartCount);
        }

        private void RemoveReplayCache(Entity entity)
        {
            _singleRequestReplayCache.Remove(entity);
        }

        private bool HasStaticStableVisuals(in PerformerState state, in PerformerDefinition definition)
        {
            if (_stableDrawCache == null)
            {
                return false;
            }

            BehaviorSlot[] behaviors = definition.Behaviors;
            int[] cacheableAssetBehaviorIndices = definition.CacheableAssetBehaviorIndices;
            for (int i = 0; i < cacheableAssetBehaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[cacheableAssetBehaviorIndices[i]];
                if (_assetEmitter.TryGetStaticStableVisualId(
                        in state,
                        slot.SlotIndex,
                        slot.AssetBinding.AssetKind,
                        state.DefId,
                        out int stableId) &&
                    _stableDrawCache.Contains(stableId))
                {
                    return true;
                }
            }

            return false;
        }

        private void RemoveStableCacheIfPresent(in PerformerState state, in PerformerDefinition definition, ref PerformerEmitCache emitCache)
        {
            if (_stableDrawCache == null)
            {
                emitCache.StableVisualPresent = 0;
                return;
            }

            _assetEmitter.RemoveStaticStableVisuals(in state, in definition, _stableDrawCache);
            emitCache.StableVisualPresent = 0;
        }

        private void RemoveRetainedPresentationRequestIfPresent(in PerformerState state, in PerformerDefinition definition)
        {
            if (!definition.UsesRetainedPresentationRequest ||
                definition.AssetBehaviorIndices.Length != 1 ||
                definition.Behaviors == null)
            {
                return;
            }

            ref readonly BehaviorSlot slot = ref definition.Behaviors[definition.AssetBehaviorIndices[0]];
            int stableId = slot.AssetBinding.AssetKind switch
            {
                AssetKind.WorldHud => HudItemIdentity.ComposeStableId(state.StableId, WorldHudItemKind.Bar, definition.Id),
                AssetKind.WorldText => HudItemIdentity.ComposeStableId(state.StableId, WorldHudItemKind.Text, definition.Id),
                AssetKind.Spline => PerformerBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slot.SlotIndex, slot.AssetBinding.AssetKind, state.DefId),
                AssetKind.GroundOverlay => PerformerBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slot.SlotIndex, slot.AssetBinding.AssetKind, state.DefId),
                _ => 0,
            };
            if (stableId <= 0)
            {
                return;
            }

            switch (slot.AssetBinding.AssetKind)
            {
                case AssetKind.WorldHud:
                case AssetKind.WorldText:
                    _requests.Add(PresentationRequest.RemoveWorldHud(state.OwnerEntity, stableId));
                    break;
                case AssetKind.Spline:
                    _requests.Add(PresentationRequest.RemoveRoadSpline(state.OwnerEntity, stableId));
                    break;
                case AssetKind.GroundOverlay:
                    _requests.Add(PresentationRequest.RemoveGroundOverlay(state.OwnerEntity, stableId));
                    break;
            }
        }

        private void RemoveRetainedPresentationRequestIfPresent(
            in PerformerState state,
            in PerformerDefinition definition,
            ref PerformerEmitCache emitCache)
        {
            if (definition.HasRetainedWorldHudLanes && _worldHudBuffer != null)
            {
                RemoveRetainedWorldHudLanesIfPresent(in state, in definition, ref emitCache);
            }

            if (emitCache.RetainedRequestPresent == 0)
            {
                return;
            }

            RemoveRetainedPresentationRequestIfPresent(in state, in definition);
            emitCache.RetainedRequestPresent = 0;
            emitCache.RetainedBarBufferIndexPlusOne = 0;
            emitCache.RetainedTextBufferIndexPlusOne = 0;
        }

        private void RemoveSurfaceSourceIfPresent(
            in PerformerState state,
            in PerformerDefinition definition,
            ref PerformerEmitCache emitCache)
        {
            if (!definition.HasSurfaceAuthoring)
            {
                return;
            }

            _requests.Add(PresentationRequest.RemoveSurfaceSource(state.OwnerEntity, state.StableId));
            emitCache.RetainedRequestPresent = 0;
        }

        private void UpdateStableVisualPositions(in PerformerState state, in PerformerDefinition definition, Vector3 performerWorldPosition)
        {
            if (_stableDrawCache == null)
            {
                return;
            }

            Vector3 position = PerformerAssetEmitRuntime.ResolvePosition(in state, in definition, performerWorldPosition);
            BehaviorSlot[] behaviors = definition.Behaviors;
            int[] cacheableAssetBehaviorIndices = definition.CacheableAssetBehaviorIndices;
            for (int i = 0; i < cacheableAssetBehaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[cacheableAssetBehaviorIndices[i]];
                if (_assetEmitter.TryGetStaticStableVisualId(
                    in state,
                    slot.SlotIndex,
                    slot.AssetBinding.AssetKind,
                    state.DefId,
                    out int stableId))
                {
                    _stableDrawCache.UpdatePosition(stableId, position);
                }
            }
        }

        private static void UpdateEmitCache(
            ref PerformerEmitCache emitCache,
            int version,
            Vector3 emitPosition,
            bool ownerCullVisible,
            bool definitionVisible,
            LODLevel lod,
            byte stableVisualPresent,
            byte retainedRequestPresent)
        {
            emitCache.CachedVersion = version;
            emitCache.LastEmitPosition = emitPosition;
            emitCache.LastOwnerCullVisible = ownerCullVisible ? (byte)1 : (byte)0;
            emitCache.LastDefinitionVisible = definitionVisible ? (byte)1 : (byte)0;
            emitCache.LastLod = lod;
            emitCache.StableVisualPresent = stableVisualPresent;
            emitCache.RetainedRequestPresent = retainedRequestPresent;
        }

        private bool EvaluateVisibility(in PerformerDefinition definition, Entity owner)
        {
            ref readonly ConditionRef condition = ref definition.VisibilityCondition;
            if (condition.Inline != InlineConditionKind.None)
            {
                return condition.Inline switch
                {
                    InlineConditionKind.SourceIsLocalPlayer => IsLocalPlayer(owner),
                    InlineConditionKind.TargetIsLocalPlayer => IsLocalPlayer(owner),
                    InlineConditionKind.SourceIsAlive => World.IsAlive(owner),
                    InlineConditionKind.TargetIsAlive => World.IsAlive(owner),
                    InlineConditionKind.OwnerCullVisible => IsOwnerCullVisible(owner),
                    InlineConditionKind.SourceHasAttributes => OwnerSatisfiesAttributeRequirements(owner, definition),
                    InlineConditionKind.SourceHasVisualTransform => World.IsAlive(owner) && World.Has<VisualTransform>(owner),
                    _ => throw new InvalidOperationException($"Unsupported performer visibility inline condition '{condition.Inline}'."),
                };
            }

            if (condition.GraphProgramId > 0)
            {
                throw new InvalidOperationException(
                    $"Performer visibility graph condition graphProgramId={condition.GraphProgramId} is not wired into PerformerEmitSystem; silent visible fallback is forbidden.");
            }

            return true;
        }

        private bool IsLocalPlayer(Entity owner)
        {
            return _globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? candidate) &&
                   candidate is Entity localPlayer &&
                   localPlayer == owner;
        }

        private bool IsOwnerCullVisible(Entity owner)
        {
            if (!World.IsAlive(owner))
            {
                return false;
            }

            return !World.Has<CullState>(owner) || World.Get<CullState>(owner).IsVisible;
        }

        private bool OwnerSatisfiesAttributeRequirements(Entity owner, in PerformerDefinition definition)
        {
            if (!World.IsAlive(owner) || !World.Has<AttributeBuffer>(owner))
            {
                return false;
            }

            ref AttributeBuffer attributes = ref World.Get<AttributeBuffer>(owner);
            int[] required = definition.RequiredAttributeIds;
            if (required == null || required.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < required.Length; i++)
            {
                if (!attributes.HasAttribute(required[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
