using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
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
            .WithAll<PerformerState, PerformerCullState, PerformerWorldPosition, PerformerWorldRotation, PerformerWorldScale, PerformerEmitCache, PerfHasEmitWork>()
            .WithNone<PerfStaticStableVisual>();

        private static readonly QueryDescription DirtyStaticEmitQuery = new QueryDescription()
            .WithAll<PerformerState, PerformerCullState, PerformerWorldPosition, PerformerWorldRotation, PerformerWorldScale, PerformerEmitCache, PerfStaticStableVisual>();

        private static readonly QueryDescription DirtyRetainedRequestEmitQuery = new QueryDescription()
            .WithAll<PerformerState, PerformerCullState, PerformerWorldPosition, PerformerWorldRotation, PerformerWorldScale, PerformerEmitCache, PerfRetainedPresentationRequest>();

        private readonly PerformerEntityRuntime _runtime;
        private readonly PerformerDefinitionRegistry _definitions;
        private readonly PresentationRequestBuffer _requests;
        private readonly Dictionary<string, object> _globals;
        private readonly PerformerAssetEmitRuntime _assetEmitter;
        private readonly StableDrawCache? _stableDrawCache;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private readonly List<Entity> _pendingDestroy = new(256);
        private readonly Dictionary<Entity, PresentationRequest> _singleRequestReplayCache = new();

        public PerformerEmitSystem(
            World world,
            PerformerEntityRuntime runtime,
            PerformerDefinitionRegistry definitions,
            PresentationRequestBuffer requests,
            Dictionary<string, object> globals,
            PerformerAnimatorStateBuffer animatorStates = null,
            SoundRequestBuffer soundRequests = null,
            PresentationTimingDiagnostics? timingDiagnostics = null,
            StableDrawCache? stableDrawCache = null)
            : base(world)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _globals = globals ?? new Dictionary<string, object>();
            _timingDiagnostics = timingDiagnostics;
            _stableDrawCache = stableDrawCache;
            _runtime.BindDefinitions(_definitions);
            _assetEmitter = new PerformerAssetEmitRuntime(
                world, _runtime, requests, globals, animatorStates, soundRequests);
        }

        public override void Update(in float dt)
        {
            long start = _timingDiagnostics != null ? Stopwatch.GetTimestamp() : 0L;
            float deltaTime = dt;
            _pendingDestroy.Clear();
            World.Query(in EmitQuery, (Entity entity,
                ref PerformerState state,
                ref PerformerCullState cull,
                ref PerformerWorldPosition position,
                ref PerformerWorldRotation rotation,
                ref PerformerWorldScale scale,
                ref PerformerEmitCache emitCache) =>
            {
                ProcessEmitEntity(entity, ref state, ref cull, ref position, ref rotation, ref scale, ref emitCache, deltaTime, clearDirtyAfterProcessing: false);
            });

            ProcessDirtyStaticEmitEntities();
            ProcessDirtyRetainedPresentationRequestEntities();

            for (int i = 0; i < _pendingDestroy.Count; i++)
            {
                Entity performer = _pendingDestroy[i];
                if (World.IsAlive(performer))
                {
                    _runtime.Destroy(performer);
                }
            }

            if (_timingDiagnostics != null)
            {
                _timingDiagnostics.ObservePerformerEmit((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
            }
        }

        private void ProcessDirtyRetainedPresentationRequestEntities()
        {
            if (!_runtime.HasDirtyRetainedPresentationRequests)
            {
                return;
            }

            int cachedDefId = -1;
            PerformerDefinition? cachedDefinition = null;
            foreach (ref var chunk in World.Query(in DirtyRetainedRequestEmitQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var states = chunk.GetSpan<PerformerState>();
                var culls = chunk.GetSpan<PerformerCullState>();
                var positions = chunk.GetSpan<PerformerWorldPosition>();
                var rotations = chunk.GetSpan<PerformerWorldRotation>();
                var scales = chunk.GetSpan<PerformerWorldScale>();
                var emitCaches = chunk.GetSpan<PerformerEmitCache>();
                foreach (var index in chunk)
                {
                    if (emitCaches[index].RetainedDirty == 0)
                    {
                        continue;
                    }

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
                        _runtime.ClearStaticDirty(entity);
                        continue;
                    }

                    ProcessEmitEntity(
                        entity,
                        ref state,
                        ref culls[index],
                        ref positions[index],
                        ref rotations[index],
                        ref scales[index],
                        ref emitCaches[index],
                        deltaTime: 0f,
                        clearDirtyAfterProcessing: true);
                }
            }
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
                World.Query(in DirtyStaticEmitQuery, (Entity entity,
                    ref PerformerState state,
                    ref PerformerCullState cull,
                    ref PerformerWorldPosition position,
                    ref PerformerWorldRotation rotation,
                    ref PerformerWorldScale scale,
                    ref PerformerEmitCache emitCache,
                    ref PerfStaticStableVisual staticVisual) =>
                {
                    if (emitCache.StaticDirty == 0)
                    {
                        return;
                    }

                    ProcessEmitEntity(entity, ref state, ref cull, ref position, ref rotation, ref scale, ref emitCache, deltaTime: 0f, clearDirtyAfterProcessing: true);
                });
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

        private void ProcessEmitEntity(
            Entity entity,
            ref PerformerState state,
            ref PerformerCullState cull,
            ref PerformerWorldPosition position,
            ref PerformerWorldRotation rotation,
            ref PerformerWorldScale scale,
            ref PerformerEmitCache emitCache,
            float deltaTime,
            bool clearDirtyAfterProcessing)
        {
            state.Elapsed += deltaTime;
            if (!_definitions.TryGet(state.DefId, out PerformerDefinition definition))
            {
                RemoveReplayCache(entity);
                ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
                return;
            }

            if (state.AnchorKind == PresentationAnchorKind.Entity && !World.IsAlive(state.OwnerEntity))
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
                RemoveRetainedPresentationRequestIfPresent(in state, in definition, ref emitCache);
                RemoveReplayCache(entity);
                _pendingDestroy.Add(entity);
                ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
                return;
            }

            if (state.DefaultLifetime > 0f && state.Elapsed >= state.DefaultLifetime)
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
                RemoveRetainedPresentationRequestIfPresent(in state, in definition, ref emitCache);
                RemoveReplayCache(entity);
                _pendingDestroy.Add(entity);
                ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
                return;
            }

            bool ownerCullVisible = cull.OwnerCullVisible;
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
                ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
                return;
            }

            if (!ownerCullVisible && (stableCacheEligible || definition.UsesRetainedPresentationRequest))
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
                RemoveRetainedPresentationRequestIfPresent(in state, in definition, ref emitCache);
                RemoveReplayCache(entity);
                UpdateEmitCache(ref emitCache, state.Version, position.Value, false, true, cull.LOD, stableVisualPresent: 0, retainedRequestPresent: 0);
                ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
                return;
            }

            bool versionClean = emitCache.CachedVersion == state.Version;
            bool positionClean = emitCache.LastEmitPosition == position.Value;
            bool ownerCullClean = emitCache.LastOwnerCullVisible == 1;
            bool definitionVisibleClean = emitCache.LastDefinitionVisible == 1;
            bool lodClean = emitCache.LastLod == cull.LOD;
            bool replayEligible = definition.SupportsSingleRequestReplay;
            if (stableCacheEligible && emitCache.StableVisualPresent != 0)
            {
                if (versionClean && positionClean && ownerCullClean && definitionVisibleClean && lodClean)
                {
                    ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
                    return;
                }

                if (versionClean && !positionClean && ownerCullClean && definitionVisibleClean && lodClean)
                {
                    UpdateStableVisualPositions(in state, in definition, position.Value);
                    UpdateEmitCache(ref emitCache, state.Version, position.Value, true, true, cull.LOD, stableVisualPresent: 1, emitCache.RetainedRequestPresent);
                    ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
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
                ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
                return;
            }

            if (ownerCullVisible && definition.HasSurfaceAuthoring)
            {
                EmitSurfaceSourceIfAny(in state, position.Value, definition, cull.LOD);
            }

            bool emittedStableVisual = false;
            int emitRequestStartCount = _requests.Count;
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
                            scale.Value,
                            _stableDrawCache!,
                            addOnly: emitCache.StableVisualPresent == 0)
                        : EmitAssetBindings(
                            entity,
                            in state,
                            definition,
                            cull.LOD,
                            position.Value,
                            rotation.Value,
                            scale.Value);
            }

            byte retainedRequestPresent = definition.UsesRetainedPresentationRequest
                ? (_requests.Count > emitRequestStartCount ? (byte)1 : (byte)0)
                : (byte)0;

            UpdateReplayCache(entity, replayEligible, requestStartCount);

            if (stableCacheEligible && !emittedStableVisual && emitCache.StableVisualPresent != 0)
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
            }

            UpdateEmitCache(
                ref emitCache,
                state.Version,
                position.Value,
                true,
                true,
                cull.LOD,
                stableCacheEligible && emittedStableVisual ? (byte)1 : (byte)0,
                retainedRequestPresent);
            ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
        }

        private void ClearDirtyIfNeeded(Entity entity, ref PerformerEmitCache emitCache, bool clearDirtyAfterProcessing)
        {
            if (clearDirtyAfterProcessing)
            {
                _runtime.ClearStaticDirty(entity);
            }
        }

        private void ProcessDirtyStaticStableEmit(
            Entity entity,
            ref PerformerState state,
            ref PerformerCullState cull,
            ref PerformerWorldPosition position,
            ref PerformerWorldRotation rotation,
            ref PerformerWorldScale scale,
            ref PerformerEmitCache emitCache,
            PerformerDefinition definition)
        {
            if (state.AnchorKind == PresentationAnchorKind.Entity && !World.IsAlive(state.OwnerEntity))
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
                RemoveRetainedPresentationRequestIfPresent(in state, in definition, ref emitCache);
                RemoveReplayCache(entity);
                _pendingDestroy.Add(entity);
                _runtime.ClearStaticDirty(entity);
                return;
            }

            bool ownerCullVisible = cull.OwnerCullVisible;
            bool versionClean = emitCache.CachedVersion == state.Version;
            bool positionClean = emitCache.LastEmitPosition == position.Value;
            bool ownerCullClean = emitCache.LastOwnerCullVisible == 1;
            bool lodClean = emitCache.LastLod == cull.LOD;

            if (!ownerCullVisible)
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
                RemoveRetainedPresentationRequestIfPresent(in state, in definition, ref emitCache);
                RemoveReplayCache(entity);
                UpdateEmitCache(ref emitCache, state.Version, position.Value, false, true, cull.LOD, stableVisualPresent: 0, retainedRequestPresent: 0);
                _runtime.ClearStaticDirty(entity);
                return;
            }

            if (emitCache.StableVisualPresent != 0)
            {
                if (versionClean && positionClean && ownerCullClean && lodClean)
                {
                    _runtime.ClearStaticDirty(entity);
                    return;
                }

                if (versionClean && !positionClean && ownerCullClean && lodClean)
                {
                    UpdateStableVisualPositions(in state, in definition, position.Value);
                    UpdateEmitCache(ref emitCache, state.Version, position.Value, true, true, cull.LOD, stableVisualPresent: 1, emitCache.RetainedRequestPresent);
                    _runtime.ClearStaticDirty(entity);
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
                scale.Value,
                _stableDrawCache!,
                addOnly: emitCache.StableVisualPresent == 0);

            if (!emittedStableVisual && emitCache.StableVisualPresent != 0)
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
            }

            UpdateEmitCache(
                ref emitCache,
                state.Version,
                position.Value,
                true,
                true,
                cull.LOD,
                emittedStableVisual ? (byte)1 : (byte)0,
                emitCache.RetainedRequestPresent);
            _runtime.ClearStaticDirty(entity);
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
            Vector3 performerWorldScale)
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

                _assetEmitter.Emit(
                    entity,
                    in state,
                    in definition,
                    slot.SlotIndex,
                    in slot.AssetBinding,
                    lod,
                    performerWorldPosition,
                    performerWorldRotation,
                    performerWorldScale);
                emittedStableVisual |= IsCacheableVisualKind(slot.AssetBinding.AssetKind);
            }

            return emittedStableVisual;
        }

        private static bool IsBehaviorActive(uint mask, int slotIndex)
        {
            return slotIndex is >= 0 and < 32 && (mask & (1u << slotIndex)) != 0;
        }

        private static bool IsCacheableVisualKind(AssetKind kind)
        {
            return kind is AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Decal or AssetKind.VFX;
        }

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

        private void RemoveStableCacheIfPresent(in PerformerState state, in PerformerDefinition definition, ref PerformerEmitCache emitCache)
        {
            if (_stableDrawCache == null || emitCache.StableVisualPresent == 0)
            {
                emitCache.StableVisualPresent = 0;
                return;
            }

            BehaviorSlot[] behaviors = definition.Behaviors;
            int[] cacheableAssetBehaviorIndices = definition.CacheableAssetBehaviorIndices;
            for (int i = 0; i < cacheableAssetBehaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[cacheableAssetBehaviorIndices[i]];
                _stableDrawCache.Remove(PerformerBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slot.SlotIndex, slot.AssetBinding.AssetKind, state.DefId));
            }

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
            if (emitCache.RetainedRequestPresent == 0)
            {
                return;
            }

            RemoveRetainedPresentationRequestIfPresent(in state, in definition);
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
                _stableDrawCache.UpdatePosition(PerformerBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slot.SlotIndex, slot.AssetBinding.AssetKind, state.DefId), position);
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
