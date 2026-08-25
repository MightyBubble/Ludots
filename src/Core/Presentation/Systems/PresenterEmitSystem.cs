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
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Surfaces;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class PresenterEmitSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription EmitQuery = new QueryDescription()
            .WithAll<PresenterState, PresenterCullState, PresenterWorldPosition, PresenterWorldRotation, PresenterWorldFacing, PresenterWorldScale, PresenterEmitCache, PerfHasEmitWork>()
            .WithNone<PerfStaticStableVisual>();

        private static readonly QueryDescription DirtyStaticEmitQuery = new QueryDescription()
            .WithAll<PresenterState, PresenterCullState, PresenterWorldPosition, PresenterWorldRotation, PresenterWorldFacing, PresenterWorldScale, PresenterEmitCache, PerfStaticStableVisual>();

        private static readonly QueryDescription DirtyRetainedRequestEmitQuery = new QueryDescription()
            .WithAll<PresenterState, PresenterCullState, PresenterWorldPosition, PresenterWorldRotation, PresenterWorldFacing, PresenterWorldScale, PresenterEmitCache, PerfRetainedPresentationRequest>();

        private static readonly QueryDescription RetainedRequestLifecycleQuery = new QueryDescription()
            .WithAll<PresenterState, PresenterCullState, PresenterEmitCache, PerfRetainedPresentationRequest, PerfRetainedPresentationRequestLifecycleTick>();

        private readonly PresenterEntityRuntime _runtime;
        private readonly PresenterDefinitionRegistry _definitions;
        private readonly PresentationRequestBuffer _requests;
        private readonly Dictionary<string, object> _globals;
        private readonly PresenterAssetEmitRuntime _assetEmitter;
        private readonly StableDrawCache? _stableDrawCache;
        private readonly SkinnedVisualBatchBuffer? _skinnedVisualBatchBuffer;
        private readonly WorldHudBatchBuffer? _worldHudBuffer;
        private readonly PresenterVisualStableIdTable? _visualStableIds;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private readonly List<Entity> _pendingDestroy = new(256);
        private readonly Dictionary<Entity, PresentationRequest> _singleRequestReplayCache = new();
        private readonly WorldHudPresentBehavior _worldHudBehavior = new();

        public PresenterEmitSystem(
            World world,
            PresenterEntityRuntime runtime,
            PresenterDefinitionRegistry definitions,
            PresentationRequestBuffer requests,
            Dictionary<string, object> globals,
            PresenterAnimatorStateBuffer animatorStates = null,
            SoundRequestBuffer soundRequests = null,
            PresentationTimingDiagnostics? timingDiagnostics = null,
            StableDrawCache? stableDrawCache = null,
            SkinnedVisualBatchBuffer? skinnedVisualBatchBuffer = null,
            WorldHudBatchBuffer? worldHudBuffer = null,
            PresenterVisualStableIdTable? visualStableIds = null)
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
            _assetEmitter = new PresenterAssetEmitRuntime(
                world, _runtime, requests, globals, animatorStates, soundRequests, visualStableIds);
        }

        public override void Update(in float dt)
        {
            long start = _timingDiagnostics != null ? Stopwatch.GetTimestamp() : 0L;
            float deltaTime = dt;
            _pendingDestroy.Clear();
            _skinnedVisualBatchBuffer?.Clear();
            int cachedDefId = -1;
            PresenterDefinition? cachedDefinition = null;
            bool cachedFastDefinition = false;
            foreach (ref var chunk in World.Query(in EmitQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<PresenterState> states = chunk.GetSpan<PresenterState>();
                Span<PresenterCullState> culls = chunk.GetSpan<PresenterCullState>();
                Span<PresenterWorldPosition> positions = chunk.GetSpan<PresenterWorldPosition>();
                Span<PresenterWorldRotation> rotations = chunk.GetSpan<PresenterWorldRotation>();
                Span<PresenterWorldFacing> facings = chunk.GetSpan<PresenterWorldFacing>();
                Span<PresenterWorldScale> scales = chunk.GetSpan<PresenterWorldScale>();
                Span<PresenterEmitCache> emitCaches = chunk.GetSpan<PresenterEmitCache>();
                bool hasAnimatorSlots = chunk.Has<PresenterAnimatorSlot>();
                Span<PresenterAnimatorSlot> animatorSlots = hasAnimatorSlots
                    ? chunk.GetSpan<PresenterAnimatorSlot>()
                    : default;

                foreach (int index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ref PresenterState state = ref states[index];
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
                            hasAnimatorSlots ? animatorSlots[index].Value : -1))
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
                Entity presenter = _pendingDestroy[i];
                if (World.IsAlive(presenter))
                {
                    _runtime.Destroy(presenter, ReleaseDestroyedPresenterVisualStableIds);
                }
            }

            if (_timingDiagnostics != null)
            {
                _timingDiagnostics.ObservePresenterEmit((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
            }
        }

        private void ReleaseDestroyedPresenterVisualStableIds(Entity presenter, PresenterState state)
        {
            if (_definitions.TryGet(state.DefId, out PresenterDefinition definition) &&
                World.IsAlive(presenter) &&
                World.Has<PresenterEmitCache>(presenter))
            {
                ref PresenterEmitCache emitCache = ref World.Get<PresenterEmitCache>(presenter);
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
            }

            _visualStableIds?.ReleasePresenter(state.StableId);
        }

        private bool ResolveCachedDefinition(
            int definitionId,
            ref int cachedDefId,
            ref PresenterDefinition? cachedDefinition,
            ref bool cachedFastDefinition)
        {
            if (definitionId == cachedDefId)
            {
                return cachedDefinition != null;
            }

            cachedDefId = definitionId;
            cachedDefinition = _definitions.TryGet(definitionId, out PresenterDefinition definition)
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
                _timingDiagnostics?.ObservePresenterEmitRetainedBreakdown(processMs: 0d, dirtyCount: 0);
                _timingDiagnostics?.ObservePresenterEmitRetainedDirectPath(directHits: 0, fullPathCount: 0, directMisses: 0);
                return;
            }

            long processStart = _timingDiagnostics != null ? Stopwatch.GetTimestamp() : 0L;
            ReadOnlySpan<Entity> dirtyEntities = _runtime.RetainedPresentationDirtyEntities;
            if (!dirtyEntities.IsEmpty)
            {
                int dirtyCount = ProcessRetainedPresentationDirtyList(
                    dirtyEntities,
                    out int directHits,
                    out int fullPathCount,
                    out int directMisses);
                _runtime.ClearConsumedRetainedPresentationDirtyEntities();
                if (_timingDiagnostics != null)
                {
                    _timingDiagnostics.ObservePresenterEmitRetainedBreakdown(
                        (Stopwatch.GetTimestamp() - processStart) * 1000d / Stopwatch.Frequency,
                        dirtyCount);
                    _timingDiagnostics.ObservePresenterEmitRetainedDirectPath(directHits, fullPathCount, directMisses);
                }

                return;
            }

            int cachedDefId = -1;
            PresenterDefinition? cachedDefinition = null;
            int scannedDirtyCount = 0;
            foreach (ref var chunk in World.Query(in DirtyRetainedRequestEmitQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var states = chunk.GetSpan<PresenterState>();
                var culls = chunk.GetSpan<PresenterCullState>();
                var positions = chunk.GetSpan<PresenterWorldPosition>();
                var rotations = chunk.GetSpan<PresenterWorldRotation>();
                var facings = chunk.GetSpan<PresenterWorldFacing>();
                var scales = chunk.GetSpan<PresenterWorldScale>();
                var emitCaches = chunk.GetSpan<PresenterEmitCache>();
                foreach (var index in chunk)
                {
                    if (emitCaches[index].RetainedDirty == 0)
                    {
                        continue;
                    }

                    scannedDirtyCount++;
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ref PresenterState state = ref states[index];
                    if (state.DefId != cachedDefId)
                    {
                        cachedDefId = state.DefId;
                        cachedDefinition = _definitions.TryGet(state.DefId, out PresenterDefinition definition)
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
                        ref facings[index],
                        ref scales[index],
                        ref emitCaches[index],
                        deltaTime: 0f,
                        clearDirtyAfterProcessing: true);
                }
            }

            if (_timingDiagnostics != null)
            {
                _timingDiagnostics.ObservePresenterEmitRetainedBreakdown(
                    (Stopwatch.GetTimestamp() - processStart) * 1000d / Stopwatch.Frequency,
                    scannedDirtyCount);
                _timingDiagnostics.ObservePresenterEmitRetainedDirectPath(directHits: 0, fullPathCount: scannedDirtyCount, directMisses: 0);
            }
        }

        private int ProcessRetainedPresentationDirtyList(
            ReadOnlySpan<Entity> dirtyEntities,
            out int directHits,
            out int fullPathCount,
            out int directMisses)
        {
            int cachedDefId = -1;
            PresenterDefinition? cachedDefinition = null;
            int dirtyCount = 0;
            directHits = 0;
            fullPathCount = 0;
            directMisses = 0;
            for (int i = 0; i < dirtyEntities.Length; i++)
            {
                Entity entity = dirtyEntities[i];
                if (!World.IsAlive(entity) ||
                    !World.Has<PresenterState>(entity) ||
                    !World.Has<PresenterEmitCache>(entity))
                {
                    continue;
                }

                ref PresenterEmitCache emitCache = ref World.Get<PresenterEmitCache>(entity);
                if (emitCache.RetainedDirty == 0)
                {
                    continue;
                }

                ref PresenterState state = ref World.Get<PresenterState>(entity);
                if (state.DefId != cachedDefId)
                {
                    cachedDefId = state.DefId;
                    cachedDefinition = _definitions.TryGet(state.DefId, out PresenterDefinition definition)
                        ? definition
                        : null;
                }

                if (cachedDefinition == null)
                {
                    _runtime.ClearStaticDirty(entity);
                    continue;
                }

                if (!World.Has<PresenterCullState>(entity) ||
                    !World.Has<PresenterWorldPosition>(entity) ||
                    !World.Has<PresenterWorldRotation>(entity) ||
                    !World.Has<PresenterWorldFacing>(entity) ||
                    !World.Has<PresenterWorldScale>(entity))
                {
                    _runtime.ClearStaticDirty(entity);
                    continue;
                }

                ref PresenterCullState cull = ref World.Get<PresenterCullState>(entity);
                ref PresenterWorldPosition position = ref World.Get<PresenterWorldPosition>(entity);
                ref PresenterWorldRotation rotation = ref World.Get<PresenterWorldRotation>(entity);
                ref PresenterWorldFacing facing = ref World.Get<PresenterWorldFacing>(entity);
                ref PresenterWorldScale scale = ref World.Get<PresenterWorldScale>(entity);
                dirtyCount++;
                if (TryUpdateRetainedWorldHudDirect(
                        entity,
                        ref state,
                        cachedDefinition,
                        ref cull,
                        ref position,
                        ref scale,
                        ref emitCache))
                {
                    directHits++;
                    continue;
                }

                directMisses++;
                fullPathCount++;
                ProcessEmitEntity(entity, ref state, ref cull, ref position, ref rotation, ref facing, ref scale, ref emitCache, deltaTime: 0f, clearDirtyAfterProcessing: true);
            }

            return dirtyCount;
        }

        private bool TryUpdateRetainedWorldHudDirect(
            Entity entity,
            ref PresenterState state,
            PresenterDefinition definition,
            ref PresenterCullState cull,
            ref PresenterWorldPosition position,
            ref PresenterWorldScale scale,
            ref PresenterEmitCache emitCache)
        {
            if (_worldHudBuffer == null ||
                definition.HasSurfaceAuthoring ||
                definition.AssetBehaviorIndices.Length != 1)
            {
                return false;
            }

            int behaviorIndex = definition.AssetBehaviorIndices[0];
            if ((uint)behaviorIndex >= (uint)definition.Behaviors.Length)
            {
                return false;
            }

            ref readonly BehaviorSlot slot = ref definition.Behaviors[behaviorIndex];
            if ((slot.Kind != BehaviorKind.AssetBinding && slot.Kind != BehaviorKind.WorldText) ||
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

            int stableId = HudItemIdentity.ComposePresenterStableId(state.StableId, kind, state.DefId, slot.SlotIndex);

            bool hasProjection = _worldHudBehavior.TryResolveProjection(
                World,
                _globals,
                state.OwnerEntity,
                cull.LOD,
                kind,
                definition.RequiredAttributeIds,
                out PresentPhaseResult phaseResult);
            bool visible = cull.OwnerCullVisible &&
                           hasProjection &&
                           EvaluateVisibility(definition, state.OwnerEntity) &&
                           IsWithinMaxLod(cull.LOD, in asset) &&
                           ResolveAssetVisibility(entity, in asset) &&
                           IsWorldHudDebugEnabled(kind);
            if (!visible)
            {
                _worldHudBuffer.Remove(stableId);
                UpdateEmitCache(ref emitCache, state.Version, position.Value, cull.OwnerCullVisible, definitionVisible: true, cull.LOD, emitCache.StableVisualPresent, retainedRequestPresent: 0);
                _runtime.ClearStaticDirty(entity);
                return true;
            }

            WorldHudItem next = kind == WorldHudItemKind.Bar
                ? BuildWorldHudBarItemDirect(entity, in state, in definition, in slot, in asset, stableId, position.Value, in scale)
                : BuildWorldHudTextItemDirect(entity, in state, in definition, in slot, in asset, stableId, position.Value);

            if (!_worldHudBuffer.TryAdd(in next))
            {
                throw new InvalidOperationException(
                    $"WorldHudBatchBuffer overflowed while directly updating retained presenter HUD stableId={stableId}.");
            }

            UpdateEmitCache(ref emitCache, state.Version, position.Value, cull.OwnerCullVisible, definitionVisible: true, cull.LOD, emitCache.StableVisualPresent, retainedRequestPresent: 1);
            _runtime.ClearStaticDirty(entity);
            return true;
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
            in PresenterState state,
            in PresenterDefinition definition,
            in BehaviorSlot slot,
            in AssetBindingConfig asset,
            int stableId,
            in Vector3 worldPosition,
            in PresenterWorldScale presenterScale)
        {
            Vector3 resolvedScale = ResolveAssetScale(entity, in asset, presenterScale.Value);
            Vector4 foreground = ResolveAssetColor(entity, in asset, ResolveAuthoredColor(in slot));
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
            in PresenterState state,
            in PresenterDefinition definition,
            in BehaviorSlot slot,
            in AssetBindingConfig asset,
            int stableId,
            in Vector3 worldPosition)
        {
            Vector4 color = ResolveAssetColor(entity, in asset, ResolveAuthoredColor(in slot));
            int tokenId = ResolveAssetId(entity, in asset);
            if (tokenId <= 0)
            {
                throw new InvalidOperationException(
                    $"WorldText AssetBinding for presenter definition '{definition.Key}' resolved invalid asset id {tokenId}.");
            }

            float value0 = asset.ScaleParamKey >= 0
                ? ResolveWorldHudFloatParam(entity, asset.ScaleParamKey, "AssetBinding.scaleParamKey")
                : 0f;
            float value1 = asset.MaterialParamKey >= 0
                ? ResolveWorldHudFloatParam(entity, asset.MaterialParamKey, "AssetBinding.materialParamKey")
                : 0f;
            WorldHudValueMode valueMode = slot.WorldText.Mode;
            int fontSize = slot.WorldText.FontSize > 0 ? slot.WorldText.FontSize : 16;
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
                        $"Presenter AssetBinding assetIdParamKey {asset.AssetIdParamKey} did not resolve to a registered asset id.");
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
                    $"Presenter AssetBinding assetSwapParamKey {asset.AssetSwapParamKey} did not resolve to a swap value.");
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
                $"Presenter AssetBinding assetSwapParamKey {asset.AssetSwapParamKey} resolved value {resolved} with no matching assetSwapTable entry.");
        }

        private Vector3 ResolveAssetScale(Entity entity, in AssetBindingConfig asset, Vector3 presenterWorldScale)
        {
            return _assetEmitter.ResolveScale(entity, in asset, presenterWorldScale);
        }

        private static Quaternion ResolveAssetRotation(in AssetBindingConfig asset, Quaternion presenterWorldRotation)
        {
            return WorldPlane2D.ResolveVisualAssetRotation(in presenterWorldRotation, in asset.LocalRotation);
        }

        private static Vector3 ResolveAssetPosition(
            Vector3 position,
            Quaternion presenterWorldRotation,
            Vector3 presenterWorldScale,
            in AssetBindingConfig asset)
        {
            return WorldPlane2D.ResolveVisualAssetPosition(
                in position,
                in presenterWorldRotation,
                in presenterWorldScale,
                in asset.LocalOffset);
        }

        private Vector4 ResolveAssetColor(Entity entity, in AssetBindingConfig asset, Vector4 defaultColor)
        {
            return asset.ColorParamKey >= 0
                ? RequireVectorParam(entity, asset.ColorParamKey, "AssetBinding.colorParamKey")
                : defaultColor;
        }

        private static Vector4 ResolveAuthoredColor(in BehaviorSlot slot)
        {
            return slot.Style.HasColor ? slot.Style.Color : Vector4.One;
        }

        private static Vector4 ApplyAuthoredAlpha(Vector4 color, in PresenterState state, in PresenterDefinition definition, BehaviorAlphaPolicy policy)
        {
            if (policy == BehaviorAlphaPolicy.FadeOverLifetime && definition.DefaultLifetime > 0f)
            {
                color.W *= Math.Clamp(1f - state.Elapsed / definition.DefaultLifetime, 0f, 1f);
            }

            return color;
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
                    _timingDiagnostics.ObservePresenterEmitDirtyBreakdown(processMs: 0d, cleanupMs: 0d, dirtyCount: 0);
                }

                return;
            }

            if (_stableDrawCache == null)
            {
                foreach (ref var chunk in World.Query(in DirtyStaticEmitQuery))
                {
                    ref Entity entityFirst = ref chunk.Entity(0);
                    var states = chunk.GetSpan<PresenterState>();
                    var culls = chunk.GetSpan<PresenterCullState>();
                    var positions = chunk.GetSpan<PresenterWorldPosition>();
                    var rotations = chunk.GetSpan<PresenterWorldRotation>();
                    var facings = chunk.GetSpan<PresenterWorldFacing>();
                    var scales = chunk.GetSpan<PresenterWorldScale>();
                    var emitCaches = chunk.GetSpan<PresenterEmitCache>();
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
            PresenterDefinition? cachedDefinition = null;
            int dirtyCount = 0;
            foreach (ref var chunk in World.Query(in DirtyStaticEmitQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var states = chunk.GetSpan<PresenterState>();
                var culls = chunk.GetSpan<PresenterCullState>();
                var positions = chunk.GetSpan<PresenterWorldPosition>();
                var rotations = chunk.GetSpan<PresenterWorldRotation>();
                var facings = chunk.GetSpan<PresenterWorldFacing>();
                var scales = chunk.GetSpan<PresenterWorldScale>();
                var emitCaches = chunk.GetSpan<PresenterEmitCache>();
                foreach (var index in chunk)
                {
                    if (emitCaches[index].StaticDirty == 0)
                    {
                        continue;
                    }

                    dirtyCount++;
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ref PresenterState state = ref states[index];
                    if (state.DefId != cachedDefId)
                    {
                        cachedDefId = state.DefId;
                        cachedDefinition = _definitions.TryGet(state.DefId, out PresenterDefinition definition)
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
                _timingDiagnostics.ObservePresenterEmitDirtyBreakdown(processMs, cleanupMs: 0d, dirtyCount);
            }
        }

        private void ProcessRetainedPresentationRequestLifecycleEntities(float deltaTime)
        {
            foreach (ref var chunk in World.Query(in RetainedRequestLifecycleQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var states = chunk.GetSpan<PresenterState>();
                var emitCaches = chunk.GetSpan<PresenterEmitCache>();
                foreach (var index in chunk)
                {
                    ref PresenterEmitCache emitCache = ref emitCaches[index];
                    if (emitCache.RetainedRequestPresent == 0 || emitCache.RetainedDirty != 0)
                    {
                        continue;
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ref PresenterState state = ref states[index];
                    if (!_definitions.TryGet(state.DefId, out PresenterDefinition definition))
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
            ref PresenterState state,
            ref PresenterCullState cull,
            ref PresenterWorldPosition position,
            ref PresenterWorldRotation rotation,
            ref PresenterWorldFacing facing,
            ref PresenterWorldScale scale,
            ref PresenterEmitCache emitCache,
            float deltaTime,
            bool clearDirtyAfterProcessing)
        {
            state.Elapsed += deltaTime;
            if (!_definitions.TryGet(state.DefId, out PresenterDefinition definition))
            {
                RemoveReplayCache(entity);
                ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
                return;
            }

            if (state.AnchorKind == PresentationAnchorKind.Entity && !World.IsAlive(state.OwnerEntity))
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
                RemoveRetainedPresentationRequestIfPresent(in state, in definition, ref emitCache);
                RemoveSurfaceSourceIfPresent(in state, in definition, ref emitCache);
                RemoveReplayCache(entity);
                _pendingDestroy.Add(entity);
                ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
                return;
            }

            if (state.DefaultLifetime > 0f && state.Elapsed >= state.DefaultLifetime)
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
                RemoveRetainedPresentationRequestIfPresent(in state, in definition, ref emitCache);
                RemoveSurfaceSourceIfPresent(in state, in definition, ref emitCache);
                RemoveReplayCache(entity);
                _pendingDestroy.Add(entity);
                ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
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
                ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
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
                ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
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
                    ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
                    return;
                }

                if (versionClean && !positionClean && ownerCullClean && definitionVisibleClean && lodClean)
                {
                    UpdateStableVisualPositions(in state, in definition, position.Value, rotation.Value, scale.Value);
                    UpdateEmitCache(ref emitCache, state.Version, position.Value, ownerCullVisible, true, cull.LOD, stableVisualPresent: 1, emitCache.RetainedRequestPresent);
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
            ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
        }

        private bool TryProcessSingleVisualProxyFastEntity(
            Entity entity,
            in PresenterState state,
            PresenterDefinition definition,
            ref PresenterCullState cull,
            ref PresenterWorldPosition position,
            ref PresenterWorldRotation rotation,
            ref PresenterWorldFacing facing,
            ref PresenterWorldScale scale,
            ref PresenterEmitCache emitCache,
            bool ownerCullVisible,
            bool clearDirtyAfterProcessing)
        {
            if (!definition.SupportsVisualProxyFastEmit ||
                definition.HasSurfaceAuthoring ||
                definition.UsesStableVisualCache ||
                definition.UsesRetainedPresentationRequest ||
                definition.DefaultLifetime > 0f ||
                definition.HasOutputMotionOrFade ||
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
                ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
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
            ClearDirtyIfNeeded(entity, ref emitCache, clearDirtyAfterProcessing);
            return true;
        }

        private bool ProcessSingleVisualProxyFastChunkEntity(
            Entity entity,
            ref PresenterState state,
            PresenterDefinition definition,
            ref PresenterCullState cull,
            ref PresenterWorldPosition position,
            ref PresenterWorldRotation rotation,
            ref PresenterWorldFacing facing,
            ref PresenterWorldScale scale,
            ref PresenterEmitCache emitCache,
            float deltaTime,
            int animatorSlot)
        {
            state.Elapsed += deltaTime;

            if (state.AnchorKind == PresentationAnchorKind.Entity && !World.IsAlive(state.OwnerEntity))
            {
                RemoveReplayCache(entity);
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

        private static bool IsVisualProxyFastDefinition(PresenterDefinition definition)
        {
            return definition.SupportsVisualProxyFastEmit &&
                   !definition.HasSurfaceAuthoring &&
                   !definition.UsesStableVisualCache &&
                   !definition.UsesRetainedPresentationRequest &&
                   definition.DefaultLifetime <= 0f &&
                   !definition.HasOutputMotionOrFade &&
                   definition.VisibilityCondition.Inline == InlineConditionKind.None &&
                   definition.VisibilityCondition.GraphProgramId <= 0;
        }

        private static bool DefinitionHasTransientVisualBindings(PresenterDefinition definition)
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

        private void ClearDirtyIfNeeded(Entity entity, ref PresenterEmitCache emitCache, bool clearDirtyAfterProcessing)
        {
            if (clearDirtyAfterProcessing)
            {
                _runtime.ClearStaticDirty(entity);
            }
        }

        private void ProcessDirtyStaticStableEmit(
            Entity entity,
            ref PresenterState state,
            ref PresenterCullState cull,
            ref PresenterWorldPosition position,
            ref PresenterWorldRotation rotation,
            ref PresenterWorldFacing facing,
            ref PresenterWorldScale scale,
            ref PresenterEmitCache emitCache,
            PresenterDefinition definition)
        {
            if (state.AnchorKind == PresentationAnchorKind.Entity && !World.IsAlive(state.OwnerEntity))
            {
                RemoveStableCacheIfPresent(in state, in definition, ref emitCache);
                RemoveRetainedPresentationRequestIfPresent(in state, in definition, ref emitCache);
                RemoveSurfaceSourceIfPresent(in state, in definition, ref emitCache);
                RemoveReplayCache(entity);
                _pendingDestroy.Add(entity);
                _runtime.ClearStaticDirty(entity);
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
                _runtime.ClearStaticDirty(entity);
                return;
            }

            if (stableVisualPresent != 0)
            {
                if (versionClean && positionClean && ownerCullClean && definitionVisibleClean && lodClean)
                {
                    _runtime.ClearStaticDirty(entity);
                    return;
                }

                if (versionClean && !positionClean && ownerCullClean && definitionVisibleClean && lodClean)
                {
                    UpdateStableVisualPositions(in state, in definition, position.Value, rotation.Value, scale.Value);
                    UpdateEmitCache(ref emitCache, state.Version, position.Value, ownerCullVisible, true, cull.LOD, stableVisualPresent: 1, emitCache.RetainedRequestPresent);
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
            _runtime.ClearStaticDirty(entity);
        }

        private void EmitSurfaceSourceIfAny(in PresenterState state, Vector3 worldPos, PresenterDefinition definition, LODLevel lod)
        {
            int behaviorIndex = definition.SurfaceSourceBehaviorIndex;
            if (behaviorIndex < 0)
            {
                return;
            }

            BehaviorSlot[] behaviors = definition.Behaviors;
            if ((uint)behaviorIndex >= (uint)behaviors.Length)
            {
                return;
            }

            ref readonly BehaviorSlot slot = ref behaviors[behaviorIndex];
            if (slot.Kind != BehaviorKind.SurfaceSource ||
                slot.SurfaceSource == null ||
                !IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
            {
                return;
            }

            SurfaceAuthoringBlock surface = slot.SurfaceSource;
            _requests.Add(PresentationRequest.FromSurfaceSource(state.OwnerEntity, new SurfaceSourceRequest
            {
                StableId = state.StableId,
                PresenterDefinitionId = state.DefId,
                ScopeId = state.ScopeId,
                SurfaceKind = surface.Kind,
                Authoring = surface,
                AnchorPosition = worldPos,
                LodSeed = lod,
            }, lod));
        }

        private bool EmitAssetBindings(
            Entity entity,
            in PresenterState state,
            PresenterDefinition definition,
            LODLevel lod,
            Vector3 presenterWorldPosition,
            Quaternion presenterWorldRotation,
            in PresenterWorldFacing presenterWorldFacing,
            Vector3 presenterWorldScale,
            int animatorSlot = -1)
        {
            int[] assetBehaviorIndices = definition.AssetBehaviorIndices;
            if (assetBehaviorIndices.Length == 0)
            {
                return false;
            }

            bool emittedStableVisual = false;
            BehaviorSlot[] behaviors = definition.Behaviors;
            uint localOffsetConsumedMask = 0u;
            for (int i = 0; i < assetBehaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[assetBehaviorIndices[i]];
                if (!IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                {
                    continue;
                }

                ref readonly AssetBindingConfig asset = ref slot.AssetBinding;
                PresenterLocalOffsetConsumption.MarkSlotConsumed(slot.SlotIndex, in asset, state.DefId, ref localOffsetConsumedMask);
                if (TryEmitSkinnedVisualBatchFast(
                        entity,
                        in state,
                        definition,
                        in slot,
                        in asset,
                        lod,
                        presenterWorldPosition,
                        presenterWorldRotation,
                        in presenterWorldFacing,
                        presenterWorldScale,
                        animatorSlot))
                {
                    emittedStableVisual = true;
                    continue;
                }

                _assetEmitter.Emit(
                    entity,
                    in state,
                    in definition,
                    in slot,
                    in asset,
                    lod,
                    presenterWorldPosition,
                    presenterWorldRotation,
                    in presenterWorldFacing,
                    presenterWorldScale,
                    ref localOffsetConsumedMask);
                emittedStableVisual |= IsCacheableVisualKind(asset.AssetKind);
            }

            return emittedStableVisual;
        }

        private bool EmitVisualProxyFast(
            Entity entity,
            in PresenterState state,
            PresenterDefinition definition,
            LODLevel lod,
            Vector3 presenterWorldPosition,
            Quaternion presenterWorldRotation,
            in PresenterWorldFacing presenterWorldFacing,
            Vector3 presenterWorldScale,
            int animatorSlot = -1)
        {
            int[] assetBehaviorIndices = definition.AssetBehaviorIndices;
            if (assetBehaviorIndices.Length == 0)
            {
                return false;
            }

            bool emittedStableVisual = false;
            BehaviorSlot[] behaviors = definition.Behaviors;
            uint localOffsetConsumedMask = 0u;
            for (int i = 0; i < assetBehaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[assetBehaviorIndices[i]];
                if (!IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                {
                    continue;
                }

                ref readonly AssetBindingConfig asset = ref slot.AssetBinding;
                PresenterLocalOffsetConsumption.MarkSlotConsumed(slot.SlotIndex, in asset, state.DefId, ref localOffsetConsumedMask);
                Vector3 resolvedPosition = PresenterAssetEmitRuntime.ResolvePosition(
                    in state,
                    presenterWorldPosition,
                    slot.Motion.YDriftPerSecond);
                VisualVisibility visibility = lod == LODLevel.Culled || (asset.HasMaxLod && lod > asset.MaxLod)
                    ? VisualVisibility.Culled
                    : VisualVisibility.Visible;
                if (visibility == VisualVisibility.Visible &&
                    TryEmitSkinnedVisualBatchFast(
                        entity,
                        in state,
                        definition,
                        in slot,
                        in asset,
                        lod,
                        resolvedPosition,
                        presenterWorldRotation,
                        in presenterWorldFacing,
                        presenterWorldScale,
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
                        in slot,
                        in asset,
                        lod,
                        resolvedPosition,
                        presenterWorldRotation,
                        in presenterWorldFacing,
                        presenterWorldScale,
                        visibility,
                        animatorSlot)));
                emittedStableVisual |= IsCacheableVisualKind(asset.AssetKind);
            }

            return emittedStableVisual;
        }

        private bool TryEmitSkinnedVisualBatchFast(
            Entity entity,
            in PresenterState state,
            PresenterDefinition definition,
            in BehaviorSlot slot,
            in AssetBindingConfig asset,
            LODLevel lod,
            Vector3 resolvedPosition,
            Quaternion presenterWorldRotation,
            in PresenterWorldFacing presenterWorldFacing,
            Vector3 presenterWorldScale,
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
                Position = ResolveAssetPosition(resolvedPosition, presenterWorldRotation, presenterWorldScale, in asset),
                Rotation = ResolveAssetRotation(in asset, presenterWorldRotation),
                Scale = ResolveAssetScale(entity, in asset, presenterWorldScale),
                Color = ApplyAuthoredAlpha(ResolveAssetColor(entity, in asset, ResolveAuthoredColor(in slot)), in state, in definition, slot.Style.AlphaPolicy),
                StableId = PresenterBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slot.SlotIndex, asset.AssetKind, state.DefId),
                MaterialId = ResolveMaterialId(entity, in asset),
                TemplateId = state.DefId,
                AnimationProfileId = definition.AnimationProfileId,
                RenderPath = renderPath,
                AssetKind = asset.AssetKind,
                SurfaceLayerKey = asset.SurfaceLayerKey,
                SortId = asset.SortId,
                MaterialCustomData = PresenterMaterialCustomDataResolver.Resolve(_runtime, entity, in asset.MaterialCustomData),
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
            in PresenterState state,
            PresenterDefinition definition,
            in BehaviorSlot slot,
            in AssetBindingConfig asset,
            LODLevel lod,
            Vector3 resolvedPosition,
            Quaternion presenterWorldRotation,
            in PresenterWorldFacing presenterWorldFacing,
            Vector3 presenterWorldScale,
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
                ProxyKind = PresentationVisualProxyKind.Presenter,
                MeshAssetId = ResolveAssetId(entity, in asset),
                Position = ResolveAssetPosition(resolvedPosition, presenterWorldRotation, presenterWorldScale, in asset),
                Rotation = ResolveAssetRotation(in asset, presenterWorldRotation),
                Scale = ResolveAssetScale(entity, in asset, presenterWorldScale),
                Color = ApplyAuthoredAlpha(ResolveAssetColor(entity, in asset, ResolveAuthoredColor(in slot)), in state, in definition, slot.Style.AlphaPolicy),
                StableId = PresenterBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slot.SlotIndex, asset.AssetKind, state.DefId),
                MaterialId = ResolveMaterialId(entity, in asset),
                TemplateId = state.DefId,
                AnimationProfileId = definition.AnimationProfileId,
                RenderPath = renderPath,
                AssetKind = asset.AssetKind,
                SurfaceLayerKey = asset.SurfaceLayerKey,
                SortId = asset.SortId,
                MaterialCustomData = PresenterMaterialCustomDataResolver.Resolve(_runtime, entity, in asset.MaterialCustomData),
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

        private bool HasStaticStableVisuals(in PresenterState state, in PresenterDefinition definition)
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

        private void RemoveStableCacheIfPresent(in PresenterState state, in PresenterDefinition definition, ref PresenterEmitCache emitCache)
        {
            if (_stableDrawCache == null)
            {
                emitCache.StableVisualPresent = 0;
                return;
            }

            _assetEmitter.RemoveStaticStableVisuals(in state, in definition, _stableDrawCache);
            emitCache.StableVisualPresent = 0;
        }

        private void RemoveRetainedPresentationRequestIfPresent(in PresenterState state, in PresenterDefinition definition)
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
                AssetKind.WorldHud => HudItemIdentity.ComposePresenterStableId(state.StableId, WorldHudItemKind.Bar, definition.Id, slot.SlotIndex),
                AssetKind.WorldText => HudItemIdentity.ComposePresenterStableId(state.StableId, WorldHudItemKind.Text, definition.Id, slot.SlotIndex),
                AssetKind.Spline => PresenterBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slot.SlotIndex, slot.AssetBinding.AssetKind, state.DefId),
                AssetKind.GroundOverlay => PresenterBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slot.SlotIndex, slot.AssetBinding.AssetKind, state.DefId),
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
                    _requests.Add(PresentationRequest.RemoveSplineRibbon(state.OwnerEntity, stableId));
                    break;
                case AssetKind.GroundOverlay:
                    _requests.Add(PresentationRequest.RemoveGroundOverlay(state.OwnerEntity, stableId));
                    break;
            }
        }

        private void RemoveRetainedPresentationRequestIfPresent(
            in PresenterState state,
            in PresenterDefinition definition,
            ref PresenterEmitCache emitCache)
        {
            if (emitCache.RetainedRequestPresent == 0)
            {
                return;
            }

            RemoveRetainedPresentationRequestIfPresent(in state, in definition);
            emitCache.RetainedRequestPresent = 0;
        }

        private void RemoveSurfaceSourceIfPresent(
            in PresenterState state,
            in PresenterDefinition definition,
            ref PresenterEmitCache emitCache)
        {
            if (!definition.HasSurfaceAuthoring)
            {
                return;
            }

            _requests.Add(PresentationRequest.RemoveSurfaceSource(state.OwnerEntity, state.StableId));
            emitCache.RetainedRequestPresent = 0;
        }

        private void UpdateStableVisualPositions(in PresenterState state, in PresenterDefinition definition, Vector3 presenterWorldPosition, Quaternion presenterWorldRotation, Vector3 presenterWorldScale)
        {
            if (_stableDrawCache == null)
            {
                return;
            }

            BehaviorSlot[] behaviors = definition.Behaviors;
            int[] cacheableAssetBehaviorIndices = definition.CacheableAssetBehaviorIndices;
            uint localOffsetConsumedMask = 0u;
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
                    PresenterLocalOffsetConsumption.MarkSlotConsumed(slot.SlotIndex, in slot.AssetBinding, state.DefId, ref localOffsetConsumedMask);
                    Vector3 position = PresenterAssetEmitRuntime.ResolvePosition(in state, presenterWorldPosition, slot.Motion.YDriftPerSecond);
                    Vector3 assetPosition = ResolveAssetPosition(position, presenterWorldRotation, presenterWorldScale, in slot.AssetBinding);
                    _stableDrawCache.UpdatePosition(stableId, assetPosition);
                }
            }
        }

        private static void UpdateEmitCache(
            ref PresenterEmitCache emitCache,
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

        private bool EvaluateVisibility(in PresenterDefinition definition, Entity owner)
        {
            ref readonly ConditionRef condition = ref definition.VisibilityCondition;
            if (condition.Inline != InlineConditionKind.None)
            {
                return condition.Inline switch
                {
                    InlineConditionKind.SourceIsSolePossessedRep => IsSolePossessedRep(owner),
                    InlineConditionKind.TargetIsSolePossessedRep => IsSolePossessedRep(owner),
                    InlineConditionKind.SourceIsAlive => World.IsAlive(owner),
                    InlineConditionKind.TargetIsAlive => World.IsAlive(owner),
                    InlineConditionKind.OwnerCullVisible => IsOwnerCullVisible(owner),
                    InlineConditionKind.SourceHasAttributes => OwnerSatisfiesAttributeRequirements(owner, definition),
                    InlineConditionKind.SourceHasVisualTransform => World.IsAlive(owner) && World.Has<VisualTransform>(owner),
                    _ => throw new InvalidOperationException($"Unsupported presenter visibility inline condition '{condition.Inline}'."),
                };
            }

            if (condition.GraphProgramId > 0)
            {
                throw new InvalidOperationException(
                    $"Presenter visibility graph condition graphProgramId={condition.GraphProgramId} is not wired into PresenterEmitSystem; silent visible fallback is forbidden.");
            }

            return true;
        }

        private bool IsSolePossessedRep(Entity owner)
        {
            return ClientLocalSeatAccess.TryGetSolePossessedRep(_globals, out Entity localPlayer) &&
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

        private bool OwnerSatisfiesAttributeRequirements(Entity owner, in PresenterDefinition definition)
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
