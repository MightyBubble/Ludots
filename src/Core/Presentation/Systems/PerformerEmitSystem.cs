using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
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
        private readonly PerformerEntityRuntime _runtime;
        private readonly PerformerDefinitionRegistry _definitions;
        private readonly PresentationRequestBuffer _requests;
        private readonly Dictionary<string, object> _globals;
        private readonly PerformerAssetEmitRuntime _assetEmitter;
        private readonly StableDrawCache? _stableDrawCache;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;

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
            _assetEmitter = new PerformerAssetEmitRuntime(
                world, _runtime, _definitions, requests, globals, animatorStates, soundRequests);
        }
        public override void Update(in float dt)
        {
            long start = _timingDiagnostics != null ? Stopwatch.GetTimestamp() : 0L;
            _runtime.AdvanceElapsed(dt);
            var query = new QueryDescription().WithAll<PerformerState, PerformerCullState, PerformerWorldPosition>();
            World.Query(in query, (Entity entity, ref PerformerState state, ref PerformerCullState cull, ref PerformerWorldPosition pos) =>
            {
                if (!cull.OwnerCullVisible) return;
                if (!_definitions.TryGet(state.DefId, out PerformerDefinition definition)) return;
                if (state.AnchorKind == PresentationAnchorKind.Entity && !World.IsAlive(state.OwnerEntity))
                {
                    _runtime.Destroy(entity);
                    return;
                }
                if (definition.DefaultLifetime > 0f && state.Elapsed >= definition.DefaultLifetime) return;
                if (!EvaluateVisibility(definition, state.OwnerEntity)) return;

                if (_stableDrawCache != null && World.Has<PerformerEmitCache>(entity))
                {
                    ref PerformerEmitCache emitCache = ref World.Get<PerformerEmitCache>(entity);
                    bool versionClean = emitCache.CachedVersion == state.Version;
                    bool positionClean = emitCache.LastEmitPosition == pos.Value;
                    bool stableCacheEligible = DefinitionUsesStableVisualCache(definition);
                    if (stableCacheEligible && versionClean && positionClean) return;
                    if (stableCacheEligible && versionClean && !positionClean)
                    {
                        _stableDrawCache.UpdatePosition(state.StableId, pos.Value);
                        emitCache.LastEmitPosition = pos.Value;
                        return;
                    }
                }

                LODLevel lod = cull.LOD;
                EmitSurfaceSourceIfAny(entity, in state, definition, lod);
                EmitAssetBindings(entity, in state, definition, lod);

                if (World.Has<PerformerEmitCache>(entity))
                {
                    ref PerformerEmitCache emitCache = ref World.Get<PerformerEmitCache>(entity);
                    emitCache.CachedVersion = state.Version;
                    emitCache.LastEmitPosition = pos.Value;
                }
            });
            _runtime.ReleaseExpired(_definitions);
            if (_timingDiagnostics != null)
                _timingDiagnostics.ObservePerformerEmit((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
        }

        private void EmitSurfaceSourceIfAny(Entity entity, in PerformerState state, PerformerDefinition definition, LODLevel lod)
        {
            SurfaceAuthoringBlock? surface = definition.Surface;
            if (surface == null) return;
            Vector3 worldPos = World.Get<PerformerWorldPosition>(entity).Value;
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

        private void EmitAssetBindings(Entity entity, in PerformerState state, PerformerDefinition definition, LODLevel lod)
        {
            BehaviorSlot[] behaviors = definition.Behaviors ?? Array.Empty<BehaviorSlot>();
            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (slot.Kind != BehaviorKind.AssetBinding || !IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                    continue;
                _assetEmitter.Emit(entity, state.DefId, in state, definition, slot.SlotIndex, slot.AssetBinding, lod);
            }
        }

        private static bool IsBehaviorActive(uint mask, int slotIndex)
        {
            return slotIndex is >= 0 and < 32 && (mask & (1u << slotIndex)) != 0;
        }

        private static bool DefinitionUsesStableVisualCache(PerformerDefinition definition)
        {
            BehaviorSlot[] behaviors = definition.Behaviors ?? Array.Empty<BehaviorSlot>();
            bool hasCacheableVisual = false;
            for (int i = 0; i < behaviors.Length; i++)
            {
                if (behaviors[i].Kind != BehaviorKind.AssetBinding)
                {
                    continue;
                }

                AssetKind kind = behaviors[i].AssetBinding.AssetKind;
                if (kind is AssetKind.WorldHud or AssetKind.WorldText or AssetKind.Spline or AssetKind.GroundOverlay)
                {
                    return false;
                }

                if (kind is AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Decal or AssetKind.VFX)
                {
                    hasCacheableVisual = true;
                }
            }

            return hasCacheableVisual;
        }

        private bool EvaluateVisibility(in PerformerDefinition definition, Entity owner)
        {
            ref readonly ConditionRef condition = ref definition.VisibilityCondition;
            if (condition.Inline == InlineConditionKind.None && condition.GraphProgramId <= 0) return true;
            if (condition.GraphProgramId > 0) return true;
            return condition.Inline switch
            {
                InlineConditionKind.None => true,
                InlineConditionKind.SourceIsLocalPlayer => IsLocalPlayer(owner),
                InlineConditionKind.TargetIsLocalPlayer => IsLocalPlayer(owner),
                InlineConditionKind.SourceIsAlive => World.IsAlive(owner),
                InlineConditionKind.TargetIsAlive => World.IsAlive(owner),
                InlineConditionKind.OwnerCullVisible => IsOwnerCullVisible(owner),
                InlineConditionKind.SourceHasAttributes => OwnerSatisfiesAttributeRequirements(owner, definition),
                InlineConditionKind.SourceHasVisualTransform => World.IsAlive(owner) && World.Has<VisualTransform>(owner),
                _ => true,
            };
        }

        private bool IsLocalPlayer(Entity owner)
        {
            return _globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? candidate) &&
                   candidate is Entity localPlayer && localPlayer == owner;
        }

        private bool IsOwnerCullVisible(Entity owner)
        {
            if (!World.IsAlive(owner)) return false;
            return !World.Has<CullState>(owner) || World.Get<CullState>(owner).IsVisible;
        }

        private bool OwnerSatisfiesAttributeRequirements(Entity owner, in PerformerDefinition definition)
        {
            if (!World.IsAlive(owner) || !World.Has<AttributeBuffer>(owner)) return false;
            ref AttributeBuffer attributes = ref World.Get<AttributeBuffer>(owner);
            int[] required = definition.RequiredAttributeIds;
            if (required == null || required.Length == 0) return true;
            for (int i = 0; i < required.Length; i++)
            {
                if (!attributes.HasAttribute(required[i])) return false;
            }
            return true;
        }
    }
}
