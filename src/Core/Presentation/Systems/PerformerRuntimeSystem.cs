using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation;
using Ludots.Core.Gameplay.Spawning;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class PerformerRuntimeSystem : BaseSystem<World, float>
    {
        private readonly PerformerCommandBuffer _commands;
        private readonly PresentationEventStream _events;
        private readonly TransientMarkerBuffer _markers;
        private readonly PresentationRequestBuffer _requests;
        private readonly PerformerEntityRuntime _runtime;
        private readonly PresentationStableIdAllocator _stableIds;
        private readonly PerformerDefinitionRegistry _definitions;
        private readonly PerformerAnimatorStateBuffer? _animatorStates;
        private readonly StableDrawCache? _stableDrawCache;
        private int _lastCullSyncStructureVersion = -1;

        public PerformerRuntimeSystem(
            World world,
            PerformerCommandBuffer commands,
            PresentationEventStream events,
            TransientMarkerBuffer markers,
            PresentationRequestBuffer requests,
            PerformerEntityRuntime runtime,
            PresentationStableIdAllocator stableIds,
            PerformerDefinitionRegistry definitions,
            PerformerAnimatorStateBuffer? animatorStates = null,
            StableDrawCache? stableDrawCache = null)
            : base(world)
        {
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _markers = markers ?? throw new ArgumentNullException(nameof(markers));
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _stableIds = stableIds ?? throw new ArgumentNullException(nameof(stableIds));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _animatorStates = animatorStates;
            _stableDrawCache = stableDrawCache;
            _runtime.BindDefinitions(_definitions);
        }
        public override void Update(in float dt)
        {
            bool hasCommands = _commands.Count != 0;
            bool hasEvents = _events.Count != 0;
            bool hasMarkers = _markers.Count != 0;
            ReleaseDestroyedOwnerAnchors();
            int releasedDeadOwners = _runtime.ReleaseDeadOwners(EmitDestroyedEvent);
            bool needsCullSync = _lastCullSyncStructureVersion != _runtime.StructureVersion;
            if (!hasCommands && !hasEvents && !hasMarkers && !needsCullSync && releasedDeadOwners == 0)
            {
                return;
            }

            var cmdSpan = _commands.GetSpan();
            for (int i = 0; i < cmdSpan.Length; i++)
            {
                ref readonly var cmd = ref cmdSpan[i];
                switch (cmd.CommandKind)
                {
                    case PerformerCommandKind.CreatePerformer:
                        HandleCreatePerformer(in cmd);
                        break;

                    case PerformerCommandKind.DestroyPerformer:
                        _runtime.Destroy(cmd.PerformerEntity, EmitDestroyedEvent);
                        break;

                    case PerformerCommandKind.DestroyPerformerScope:
                        if (cmd.ScopeTag <= 0)
                        {
                            throw new InvalidOperationException(
                                $"DestroyPerformerScope requires a positive scopeTag, got {cmd.ScopeTag}.");
                        }
                        _runtime.DestroyScope(cmd.ScopeTag, EmitDestroyedEvent);
                        break;

                    case PerformerCommandKind.DestroyScopedPerformer:
                        HandleDestroyScopedPerformer(in cmd);
                        break;

                    case PerformerCommandKind.SetParam:
                        if (World.IsAlive(cmd.PerformerEntity) && World.Has<PerformerState>(cmd.PerformerEntity))
                        {
                            _runtime.SetParamAndPropagateToAffectedChildren(cmd.PerformerEntity, cmd.ParamKey, cmd.ParamLane, cmd.ParamValue, cmd.IntValue, cmd.VectorValue);
                        }
                        break;

                    case PerformerCommandKind.ActivateBehavior:
                        if (World.IsAlive(cmd.PerformerEntity) && World.Has<PerformerState>(cmd.PerformerEntity) && cmd.TargetBehaviorSlot is >= 0 and < 32)
                        {
                            ref PerformerState state = ref World.Get<PerformerState>(cmd.PerformerEntity);
                            if (_definitions.TryGet(state.DefId, out PerformerDefinition definition))
                            {
                                if (_runtime.SetBehaviorActive(cmd.PerformerEntity, definition, cmd.TargetBehaviorSlot, active: true))
                                {
                                    MarkHierarchyForBootstrap(cmd.PerformerEntity);
                                }
                            }
                        }
                        break;

                    case PerformerCommandKind.DeactivateBehavior:
                        if (World.IsAlive(cmd.PerformerEntity) && World.Has<PerformerState>(cmd.PerformerEntity) && cmd.TargetBehaviorSlot is >= 0 and < 32)
                        {
                            ref PerformerState state = ref World.Get<PerformerState>(cmd.PerformerEntity);
                            if (_definitions.TryGet(state.DefId, out PerformerDefinition definition))
                            {
                                if (_runtime.SetBehaviorActive(cmd.PerformerEntity, definition, cmd.TargetBehaviorSlot, active: false))
                                {
                                    MarkHierarchyForBootstrap(cmd.PerformerEntity);
                                }
                            }
                        }
                        break;
                    case PerformerCommandKind.InitializeTransform:
                        HandleInitializeTransform(in cmd);
                        break;
                }
            }
            _commands.Clear();

            int structureVersion = _runtime.StructureVersion;
            if (_lastCullSyncStructureVersion != structureVersion)
            {
                _runtime.SyncCullVisibility();
                _lastCullSyncStructureVersion = structureVersion;
            }

            _markers.TickAndRequest(_requests, dt, World);
        }

        private void ReleaseDestroyedOwnerAnchors()
        {
            ReadOnlySpan<PresentationEvent> events = _events.GetSpan();
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly PresentationEvent evt = ref events[i];
                if (evt.Kind != PresentationEventKind.EntityDestroyed)
                {
                    continue;
                }

                DestroyPerformersOwnedBy(evt.Source);
            }
        }

        private void DestroyPerformersOwnedBy(Entity owner)
        {
            if (!_runtime.TryGetActiveByOwner(owner, out PerformerEntityRuntime.OwnerPerformerBucket performers))
            {
                return;
            }

            if (performers.TryGetSingle(out Entity single))
            {
                if (World.IsAlive(single) && World.Has<PerformerState>(single))
                {
                    _runtime.Destroy(single, EmitDestroyedEvent);
                }

                return;
            }

            for (int i = 0; i < performers.Count; i++)
            {
                Entity performer = performers.GetAt(i);
                if (World.IsAlive(performer) && World.Has<PerformerState>(performer))
                {
                    _runtime.Destroy(performer, EmitDestroyedEvent);
                }
            }
        }
        private void HandleInitializeTransform(in PerformerCommand cmd)
        {
            Entity performer = cmd.PerformerEntity;
            if (!World.IsAlive(performer) || !World.Has<PerformerState>(performer))
            {
                return;
            }

            ref PerformerState state = ref World.Get<PerformerState>(performer);
            if (!_definitions.TryGet(state.DefId, out PerformerDefinition definition))
            {
                return;
            }

            _runtime.InitializeTransform(performer, definition);
        }

        private void HandleDestroyScopedPerformer(in PerformerCommand cmd)
        {
            if (cmd.PerformerDefinitionId <= 0)
            {
                throw new InvalidOperationException("DestroyScopedPerformer requires a positive performer definition id.");
            }

            if (cmd.ScopeTag <= 0)
            {
                throw new InvalidOperationException(
                    $"DestroyScopedPerformer requires a positive scopeTag, got {cmd.ScopeTag}.");
            }

            if (!World.IsAlive(cmd.Source))
            {
                return;
            }

            if (_runtime.TryGetActiveScopedInstance(
                    cmd.PerformerDefinitionId,
                    cmd.Source,
                    cmd.ScopeTag,
                    PresentationAnchorKind.Entity,
                    default,
                    out Entity performer))
            {
                _runtime.Destroy(performer, EmitDestroyedEvent);
            }
        }

        private void HandleCreatePerformer(in PerformerCommand cmd)
        {
            if (!_definitions.TryGet(cmd.PerformerDefinitionId, out var definition))
            {
                throw new InvalidOperationException($"Performer definition id={cmd.PerformerDefinitionId} is not registered.");
            }

            if (ShouldSkipHandledRootBootstrapCreate(in cmd))
            {
                return;
            }

            Entity parentEntity = NormalizeOptionalEntity(cmd.ParentEntity);
            if (parentEntity != Entity.Null && (!World.IsAlive(parentEntity) || !World.Has<PerformerState>(parentEntity)))
            {
                throw new InvalidOperationException(
                    $"CreatePerformer defId={cmd.PerformerDefinitionId} references inactive parent entity.");
            }

            if (ShouldSkipDuplicatePersistentScopedCreate(in cmd, definition, parentEntity))
            {
                return;
            }

            Entity entity = _runtime.CreateHierarchy(
                _definitions,
                cmd.PerformerDefinitionId,
                cmd.Source,
                cmd.ScopeTag,
                cmd.AnchorKind,
                cmd.Position,
                _stableIds.Allocate(),
                parentEntity,
                definition,
                _stableIds.Allocate);

            ref PerformerState state = ref World.Get<PerformerState>(entity);
            state.BehaviorActiveMask = BuildDefaultBehaviorMask(definition);
            MarkHierarchyForBootstrapIfNeeded(entity);
            EmitCreatedEvent(entity, World.Get<PerformerState>(entity));
        }

        private static Entity NormalizeOptionalEntity(Entity entity)
        {
            return entity == default || entity.Id < 0 ? Entity.Null : entity;
        }

        private bool ShouldSkipDuplicatePersistentScopedCreate(
            in PerformerCommand cmd,
            PerformerDefinition definition,
            Entity parentEntity)
        {
            if (definition.DefaultLifetime > 0f || cmd.ScopeTag <= 0)
                return false;
            if (!_runtime.TryGetActiveScopedInstance(
                    cmd.PerformerDefinitionId,
                    cmd.Source,
                    cmd.ScopeTag,
                    cmd.AnchorKind,
                    cmd.Position,
                    out Entity existing))
            {
                return false;
            }

            if (ScopedInstanceParentMatches(existing, parentEntity))
            {
                return true;
            }

            _runtime.Destroy(existing, EmitDestroyedEvent);
            return false;
        }

        private bool ScopedInstanceParentMatches(Entity performer, Entity parentEntity)
        {
            if (!World.IsAlive(performer))
            {
                return false;
            }

            Entity existingParent = World.Has<PerformerParent>(performer)
                ? World.Get<PerformerParent>(performer).Parent
                : Entity.Null;
            return existingParent == parentEntity;
        }

        private bool ShouldSkipHandledRootBootstrapCreate(in PerformerCommand cmd)
        {
            if (!World.IsAlive(cmd.Source) ||
                !World.Has<PerformerRootBootstrapHandled>(cmd.Source) ||
                cmd.AnchorKind != PresentationAnchorKind.Entity ||
                cmd.ParentEntity != Entity.Null ||
                !World.Has<EntityTemplateKeyRef>(cmd.Source))
            {
                return false;
            }

            int templateKeyId = World.Get<EntityTemplateKeyRef>(cmd.Source).TemplateKeyId;
            if (templateKeyId <= 0 ||
                !_definitions.BootstrapRegistry.TryGetEntitySpawnCreates(
                    templateKeyId,
                    out CompiledPerformerBootstrapRegistry.BootstrapCreateRule[] rules))
            {
                return false;
            }

            for (int i = 0; i < rules.Length; i++)
            {
                if (rules[i].PerformerDefinitionId != cmd.PerformerDefinitionId)
                {
                    continue;
                }

                int stableId = World.Has<PresentationStableId>(cmd.Source)
                    ? World.Get<PresentationStableId>(cmd.Source).Value
                    : 0;
                return rules[i].ResolveScopeTag(stableId) == cmd.ScopeTag;
            }

            return false;
        }

        private static uint BuildDefaultBehaviorMask(PerformerDefinition definition)
        {
            if (definition.Behaviors == null || definition.Behaviors.Length == 0)
                return 0u;
            uint mask = 0u;
            for (int i = 0; i < definition.Behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref definition.Behaviors[i];
                if (!slot.ActiveByDefault || slot.SlotIndex < 0 || slot.SlotIndex >= 32)
                    continue;
                mask |= 1u << slot.SlotIndex;
            }
            return mask;
        }

        private void EmitCreatedEvent(Entity performer, in PerformerState state)
        {
            if (!_events.TryAdd(new PresentationEvent
                {
                    Kind = PresentationEventKind.PerformerCreated,
                    KeyId = state.DefId,
                    Source = state.OwnerEntity,
                    Target = state.OwnerEntity,
                    PerformerEntity = performer,
                    PayloadB = state.ScopeId,
                    Magnitude = state.StableId,
                }))
            {
                throw new InvalidOperationException("PresentationEventStream is full while publishing PerformerCreated.");
            }
        }

        private void EmitDestroyedEvent(Entity performer, PerformerState state)
        {
            RemoveStableVisualCacheIfPresent(performer, in state);
            EmitRetainedPresentationRemovalIfPresent(performer, in state);
            _animatorStates?.Clear(performer);

            if (!_events.TryAdd(new PresentationEvent
                {
                    Kind = PresentationEventKind.PerformerDestroyed,
                    KeyId = state.DefId,
                    Source = state.OwnerEntity,
                    Target = state.OwnerEntity,
                    PerformerEntity = performer,
                    PayloadB = state.ScopeId,
                    Magnitude = state.StableId,
                }))
            {
                throw new InvalidOperationException("PresentationEventStream is full while publishing PerformerDestroyed.");
            }
        }

        private void RemoveStableVisualCacheIfPresent(Entity performer, in PerformerState state)
        {
            if (_stableDrawCache == null ||
                !World.IsAlive(performer) ||
                !World.Has<PerformerEmitCache>(performer) ||
                !_definitions.TryGet(state.DefId, out PerformerDefinition definition))
            {
                return;
            }

            int[] behaviorIndices = definition.CacheableAssetBehaviorIndices;
            BehaviorSlot[] behaviors = definition.Behaviors;
            for (int i = 0; i < behaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[behaviorIndices[i]];
                _stableDrawCache.Remove(PerformerBehaviorRuntimeUtility.ComposeVisualStableId(
                    state.StableId,
                    slot.SlotIndex,
                    slot.AssetBinding.AssetKind,
                    state.DefId));
            }
        }

        private void EmitRetainedPresentationRemovalIfPresent(Entity performer, in PerformerState state)
        {
            if (!_definitions.TryGet(state.DefId, out PerformerDefinition definition) ||
                !World.IsAlive(performer) ||
                !World.Has<PerformerEmitCache>(performer) ||
                World.Get<PerformerEmitCache>(performer).RetainedRequestPresent == 0)
            {
                return;
            }

            if (definition.HasSurfaceAuthoring)
            {
                _requests.Add(PresentationRequest.RemoveSurfaceSource(state.OwnerEntity, state.StableId));
            }

            if (!definition.UsesRetainedPresentationRequest ||
                definition.AssetBehaviorIndices.Length != 1 ||
                definition.Behaviors == null)
            {
                return;
            }

            ref readonly BehaviorSlot slot = ref definition.Behaviors[definition.AssetBehaviorIndices[0]];
            int stableId = slot.AssetBinding.AssetKind switch
            {
                AssetKind.WorldHud => HudItemIdentity.ComposeStableId(state.StableId, WorldHudItemKind.Bar, state.DefId),
                AssetKind.WorldText => HudItemIdentity.ComposeStableId(state.StableId, WorldHudItemKind.Text, state.DefId),
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

        private void MarkHierarchyForBootstrap(Entity root)
        {
            if (!World.IsAlive(root) || !World.Has<PerformerState>(root))
            {
                return;
            }

            MarkSingle(root);
            ref PerformerChildren children = ref World.Get<PerformerChildren>(root);
            for (int i = 0; i < children.Count; i++)
            {
                Entity child = children.Get(i);
                if (World.IsAlive(child))
                {
                    MarkHierarchyForBootstrap(child);
                }
            }
        }

        private void MarkSingle(Entity performer)
        {
            if (World.Has<PerformerBootstrapPending>(performer))
            {
                return;
            }

            World.Add(performer, new PerformerBootstrapPending());
        }

        private void MarkHierarchyForBootstrapIfNeeded(Entity root)
        {
            if (!World.IsAlive(root) || !World.Has<PerformerState>(root))
            {
                return;
            }

            ref readonly PerformerState state = ref World.Get<PerformerState>(root);
            if (_definitions.TryGet(state.DefId, out PerformerDefinition definition) &&
                definition.RequiresBootstrapProcessing)
            {
                MarkSingle(root);
            }

            ref PerformerChildren children = ref World.Get<PerformerChildren>(root);
            for (int i = 0; i < children.Count; i++)
            {
                Entity child = children.Get(i);
                if (World.IsAlive(child))
                {
                    MarkHierarchyForBootstrapIfNeeded(child);
                }
            }
        }
    }
}
