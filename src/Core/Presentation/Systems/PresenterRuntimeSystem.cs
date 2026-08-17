using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class PresenterRuntimeSystem : BaseSystem<World, float>
    {
        private readonly PresenterCommandBuffer _commands;
        private readonly PresentationEventStream _events;
        private readonly TransientMarkerBuffer _markers;
        private readonly PresentationRequestBuffer _requests;
        private readonly PresenterEntityRuntime _runtime;
        private readonly PresentationStableIdAllocator _stableIds;
        private readonly PresenterDefinitionRegistry _definitions;
        private readonly PresenterAnimatorStateBuffer? _animatorStates;
        private readonly StableDrawCache? _stableDrawCache;
        private readonly PresenterVisualStableIdTable? _visualStableIds;
        private readonly PerformerCommandKindRegistry? _extensionCommands;
        private readonly PerformerCommandOps _extensionCommandOps;
        private Entity[] _ownerDestroyScratch = Array.Empty<Entity>();
        private int _lastCullSyncStructureVersion = -1;

        public PresenterRuntimeSystem(
            World world,
            PresenterCommandBuffer commands,
            PresentationEventStream events,
            TransientMarkerBuffer markers,
            PresentationRequestBuffer requests,
            PresenterEntityRuntime runtime,
            PresentationStableIdAllocator stableIds,
            PresenterDefinitionRegistry definitions,
            PresenterAnimatorStateBuffer? animatorStates = null,
            StableDrawCache? stableDrawCache = null,
            PresenterVisualStableIdTable? visualStableIds = null,
            PerformerCommandKindRegistry? extensionCommands = null)
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
            _visualStableIds = visualStableIds;
            _extensionCommands = extensionCommands;
            _extensionCommandOps = new PerformerCommandOps(World, _runtime, _definitions, MarkHierarchyForBootstrap);
            _runtime.BindDefinitions(_definitions);
        }
        public override void Update(in float dt)
        {
            if (dt < 0f || !float.IsFinite(dt))
            {
                throw new InvalidOperationException($"PresenterRuntimeSystem dt must be finite and >= 0, got {dt}.");
            }

            bool hasCommands = _commands.Count != 0;
            bool hasEvents = _events.Count != 0;
            bool hasMarkers = _markers.Count != 0;
            ReleaseDestroyedOwnerAnchors();
            int releasedDeadOwners = hasCommands ? _runtime.ReleaseDeadOwners(EmitDestroyedEvent) : 0;
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
                    case PresenterCommandKind.CreatePresenter:
                        HandleCreatePresenter(in cmd);
                        break;

                    case PresenterCommandKind.DestroyPresenter:
                        _runtime.Destroy(cmd.PresenterEntity, EmitDestroyedEvent);
                        break;

                    case PresenterCommandKind.DestroyPresenterScope:
                        if (cmd.ScopeTag <= 0)
                        {
                            throw new InvalidOperationException(
                                $"DestroyPresenterScope requires a positive scopeTag, got {cmd.ScopeTag}.");
                        }
                        _runtime.DestroyScope(cmd.ScopeTag, EmitDestroyedEvent);
                        break;

                    case PresenterCommandKind.DestroyScopedPresenter:
                        HandleDestroyScopedPresenter(in cmd);
                        break;

                    case PresenterCommandKind.SetParam:
                        Entity paramTarget = cmd.PresenterEntity;
                        if ((paramTarget == Entity.Null || !World.IsAlive(paramTarget)) &&
                            cmd.PresenterDefinitionId > 0 &&
                            cmd.ScopeTag > 0)
                        {
                            _runtime.TryGetActiveScopedInstance(
                                cmd.PresenterDefinitionId,
                                cmd.Source,
                                cmd.ScopeTag,
                                cmd.AnchorKind,
                                cmd.Position,
                                out paramTarget);
                        }

                        if (World.IsAlive(paramTarget) && World.Has<PresenterState>(paramTarget))
                        {
                            if (cmd.UseEventPosition)
                            {
                                _runtime.UpdateWorldPosition(paramTarget, cmd.Position);
                                ApplyRelationContext(paramTarget, in cmd);
                            }

                            _runtime.SetParamAndPropagateToAffectedChildren(paramTarget, cmd.ParamKey, cmd.ParamLane, cmd.ParamValue, cmd.IntValue, cmd.VectorValue);
                        }
                        break;

                    case PresenterCommandKind.ActivateBehavior:
                        if (World.IsAlive(cmd.PresenterEntity) && World.Has<PresenterState>(cmd.PresenterEntity) && cmd.TargetBehaviorSlot is >= 0 and < 32)
                        {
                            ref PresenterState state = ref World.Get<PresenterState>(cmd.PresenterEntity);
                            if (_definitions.TryGet(state.DefId, out PresenterDefinition definition))
                            {
                                if (_runtime.SetBehaviorActive(cmd.PresenterEntity, definition, cmd.TargetBehaviorSlot, active: true))
                                {
                                    MarkHierarchyForBootstrap(cmd.PresenterEntity);
                                }
                            }
                        }
                        break;

                    case PresenterCommandKind.DeactivateBehavior:
                        if (World.IsAlive(cmd.PresenterEntity) && World.Has<PresenterState>(cmd.PresenterEntity) && cmd.TargetBehaviorSlot is >= 0 and < 32)
                        {
                            ref PresenterState state = ref World.Get<PresenterState>(cmd.PresenterEntity);
                            if (_definitions.TryGet(state.DefId, out PresenterDefinition definition))
                            {
                                if (_runtime.SetBehaviorActive(cmd.PresenterEntity, definition, cmd.TargetBehaviorSlot, active: false))
                                {
                                    MarkHierarchyForBootstrap(cmd.PresenterEntity);
                                }
                            }
                        }
                        break;
                    case PresenterCommandKind.InitializeTransform:
                        HandleInitializeTransform(in cmd);
                        break;

                    case PresenterCommandKind.Extension:
                        HandleExtensionCommand(in cmd);
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported performer command kind '{cmd.CommandKind}' (id={ResolveCommandKindId(in cmd)}).");
                }
            }
            _commands.Clear();

            int structureVersion = _runtime.StructureVersion;
            if (_lastCullSyncStructureVersion != structureVersion)
            {
                _runtime.SyncCullVisibility();
                _lastCullSyncStructureVersion = structureVersion;
            }

            if (hasMarkers && dt > 0f)
            {
                _markers.TickAndRequest(_requests, dt, World);
            }
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

                DestroyPresentersOwnedBy(evt.Source);
            }
        }

        private void HandleExtensionCommand(in PresenterCommand cmd)
        {
            int commandKindId = ResolveCommandKindId(in cmd);
            if (commandKindId <= 0)
            {
                throw new InvalidOperationException("Performer extension command requires a positive CommandKindId.");
            }

            if (_extensionCommands == null || !_extensionCommands.TryGetDescriptor(commandKindId, out PerformerCommandExtensionDescriptor descriptor))
            {
                throw new InvalidOperationException($"No extension performer command handler registered for id {commandKindId}.");
            }

            if (descriptor.RouteStrategy != cmd.RouteStrategy)
            {
                throw new InvalidOperationException(
                    $"Extension performer command id {commandKindId} was routed as {cmd.RouteStrategy}, but registered route is {descriptor.RouteStrategy}.");
            }

            if (RouteRequiresRoutedPerformer(cmd.RouteStrategy) &&
                (!World.IsAlive(cmd.PresenterEntity) || !World.Has<PresenterState>(cmd.PresenterEntity)))
            {
                throw new InvalidOperationException(
                    $"Extension performer command id {commandKindId} route {cmd.RouteStrategy} requires a routed performer entity.");
            }

            _extensionCommandOps.Bind(cmd.PresenterEntity);
            var context = new PerformerCommandExecutionContext(in cmd, _extensionCommandOps);
            descriptor.Handler(in context);
        }

        private static int ResolveCommandKindId(in PresenterCommand cmd)
        {
            return cmd.CommandKindId != 0 ? cmd.CommandKindId : (byte)cmd.CommandKind;
        }

        private static bool RouteRequiresRoutedPerformer(PerformerCommandRouteStrategy route)
        {
            return route is PerformerCommandRouteStrategy.ExistingInstances
                or PerformerCommandRouteStrategy.ScopedInstance;
        }

        private sealed class PerformerCommandOps : IPerformerCommandOps
        {
            private readonly World _world;
            private readonly PresenterEntityRuntime _runtime;
            private readonly PresenterDefinitionRegistry _definitions;
            private readonly Action<Entity> _markHierarchyForBootstrap;
            private Entity _performer;

            public PerformerCommandOps(
                World world,
                PresenterEntityRuntime runtime,
                PresenterDefinitionRegistry definitions,
                Action<Entity> markHierarchyForBootstrap)
            {
                _world = world;
                _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
                _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
                _markHierarchyForBootstrap = markHierarchyForBootstrap ?? throw new ArgumentNullException(nameof(markHierarchyForBootstrap));
            }

            public bool HasRoutedPerformer =>
                _world.IsAlive(_performer) && _world.Has<PresenterState>(_performer);

            public void Bind(Entity performer)
            {
                _performer = performer;
            }

            public void SetParam(int paramKey, ParamLane lane, float floatValue = 0f, int intValue = 0, Vector4 vectorValue = default)
            {
                RequireRoutedPerformer();
                _runtime.SetParamAndPropagateToAffectedChildren(_performer, paramKey, lane, floatValue, intValue, vectorValue);
            }

            public void ClearParam(int paramKey, ParamLane lane)
            {
                RequireRoutedPerformer();
                _runtime.ClearParamAndPropagateToAffectedChildren(_performer, paramKey, lane);
            }

            public void ActivateBehavior(int slotIndex)
            {
                SetBehaviorActive(slotIndex, active: true);
            }

            public void DeactivateBehavior(int slotIndex)
            {
                SetBehaviorActive(slotIndex, active: false);
            }

            private void SetBehaviorActive(int slotIndex, bool active)
            {
                RequireRoutedPerformer();
                ref PresenterState state = ref _world.Get<PresenterState>(_performer);
                if (!_definitions.TryGet(state.DefId, out PresenterDefinition definition))
                {
                    throw new InvalidOperationException($"Performer definition id={state.DefId} is not registered.");
                }

                if (_runtime.SetBehaviorActive(_performer, definition, slotIndex, active))
                {
                    _markHierarchyForBootstrap(_performer);
                }
            }

            private void RequireRoutedPerformer()
            {
                if (!HasRoutedPerformer)
                {
                    throw new InvalidOperationException("Performer extension command operation requires a routed performer entity.");
                }
            }
        }

        private void DestroyPresentersOwnedBy(Entity owner)
        {
            if (!_runtime.TryGetActiveByOwner(owner, out PresenterEntityRuntime.OwnerPresenterBucket presenters))
            {
                return;
            }

            if (presenters.TryGetSingle(out Entity single))
            {
                if (World.IsAlive(single) && World.Has<PresenterState>(single))
                {
                    _runtime.Destroy(single, EmitDestroyedEvent);
                }

                return;
            }

            int count = presenters.Count;
            EnsureOwnerDestroyScratchCapacity(count);
            int scratchCount = 0;
            for (int i = 0; i < count; i++)
            {
                Entity presenter = presenters.GetAt(i);
                if (World.IsAlive(presenter) && World.Has<PresenterState>(presenter))
                {
                    _ownerDestroyScratch[scratchCount++] = presenter;
                }
            }

            for (int i = 0; i < scratchCount; i++)
            {
                Entity presenter = _ownerDestroyScratch[i];
                _ownerDestroyScratch[i] = Entity.Null;
                if (World.IsAlive(presenter) && World.Has<PresenterState>(presenter))
                {
                    _runtime.Destroy(presenter, EmitDestroyedEvent);
                }
            }
        }

        private void EnsureOwnerDestroyScratchCapacity(int required)
        {
            if (_ownerDestroyScratch.Length >= required)
            {
                return;
            }

            int capacity = Math.Max(required, _ownerDestroyScratch.Length == 0 ? 8 : _ownerDestroyScratch.Length * 2);
            Array.Resize(ref _ownerDestroyScratch, capacity);
        }

        private void HandleInitializeTransform(in PresenterCommand cmd)
        {
            Entity presenter = cmd.PresenterEntity;
            if (!World.IsAlive(presenter) || !World.Has<PresenterState>(presenter))
            {
                return;
            }

            ref PresenterState state = ref World.Get<PresenterState>(presenter);
            if (!_definitions.TryGet(state.DefId, out PresenterDefinition definition))
            {
                return;
            }

            _runtime.InitializeTransform(presenter, definition);
        }

        private void HandleDestroyScopedPresenter(in PresenterCommand cmd)
        {
            if (cmd.PresenterDefinitionId <= 0)
            {
                throw new InvalidOperationException("DestroyScopedPresenter requires a positive presenter definition id.");
            }

            if (cmd.ScopeTag <= 0)
            {
                throw new InvalidOperationException(
                    $"DestroyScopedPresenter requires a positive scopeTag, got {cmd.ScopeTag}.");
            }

            if (cmd.Source != Entity.Null && !World.IsAlive(cmd.Source))
            {
                return;
            }

            bool found = cmd.UseEventPosition
                ? _runtime.TryGetActiveScopedInstance(
                    cmd.PresenterDefinitionId,
                    cmd.Source,
                    cmd.ScopeTag,
                    cmd.AnchorKind,
                    cmd.Position,
                    out Entity presenter)
                : _runtime.TryGetUniqueActiveScopedInstanceByScope(
                    cmd.PresenterDefinitionId,
                    cmd.Source,
                    cmd.ScopeTag,
                    out presenter);

            if (found)
            {
                _runtime.Destroy(presenter, EmitDestroyedEvent);
            }
        }

        private void HandleCreatePresenter(in PresenterCommand cmd)
        {
            if (!_definitions.TryGet(cmd.PresenterDefinitionId, out var definition))
            {
                throw new InvalidOperationException($"Presenter definition id={cmd.PresenterDefinitionId} is not registered.");
            }

            if (ShouldSkipHandledRootBootstrapCreate(in cmd))
            {
                return;
            }

            Entity parentEntity = NormalizeOptionalEntity(cmd.ParentEntity);
            if (parentEntity != Entity.Null && (!World.IsAlive(parentEntity) || !World.Has<PresenterState>(parentEntity)))
            {
                throw new InvalidOperationException(
                    $"CreatePresenter defId={cmd.PresenterDefinitionId} references inactive parent entity.");
            }

            if (ShouldSkipDuplicatePersistentScopedCreate(in cmd, definition, parentEntity))
            {
                if (cmd.ScopeTag > 0 &&
                    _runtime.TryGetActiveScopedInstance(
                        cmd.PresenterDefinitionId,
                        cmd.Source,
                        cmd.ScopeTag,
                        cmd.AnchorKind,
                        cmd.Position,
                        out Entity existing))
                {
                    _runtime.UpdateWorldPosition(existing, cmd.Position);
                    ApplyRelationContext(existing, in cmd);
                    ApplyCreateParamPayload(existing, in cmd);
                    MarkHierarchyForBootstrapIfNeeded(existing);
                }

                return;
            }

            Entity entity = _runtime.CreateHierarchy(
                _definitions,
                cmd.PresenterDefinitionId,
                cmd.Source,
                cmd.ScopeTag,
                cmd.AnchorKind,
                cmd.Position,
                _stableIds.Allocate(),
                parentEntity,
                definition,
                _stableIds.Allocate);

            ApplyRelationContext(entity, in cmd);
            ApplyCreateParamPayload(entity, in cmd);
            ref PresenterState state = ref World.Get<PresenterState>(entity);
            state.BehaviorActiveMask = BuildDefaultBehaviorMask(definition);
            MarkHierarchyForBootstrapIfNeeded(entity);
            EmitCreatedEvent(entity, World.Get<PresenterState>(entity));
        }

        private void ApplyCreateParamPayload(Entity presenter, in PresenterCommand cmd)
        {
            if (!cmd.HasParamPayload)
            {
                return;
            }

            _runtime.SetParamAndPropagateToAffectedChildren(
                presenter,
                cmd.ParamKey,
                cmd.ParamLane,
                cmd.ParamValue,
                cmd.IntValue,
                cmd.VectorValue);
        }

        private void ApplyRelationContext(Entity root, in PresenterCommand cmd)
        {
            Entity viewer = cmd.Viewer;
            Entity target = cmd.Target;
            if (viewer == Entity.Null && target == Entity.Null)
            {
                return;
            }

            var context = new PresenterRelationContext
            {
                Viewer = viewer,
                Target = target,
            };
            ApplyRelationContextRecursive(root, in context);
        }

        private void ApplyRelationContextRecursive(Entity presenter, in PresenterRelationContext context)
        {
            if (!World.IsAlive(presenter) || !World.Has<PresenterState>(presenter))
            {
                return;
            }

            if (World.Has<PresenterRelationContext>(presenter))
            {
                World.Set(presenter, context);
            }
            else
            {
                World.Add(presenter, context);
            }

            if (!World.Has<PresenterChildren>(presenter))
            {
                return;
            }

            ref PresenterChildren children = ref World.Get<PresenterChildren>(presenter);
            for (int i = 0; i < children.Count; i++)
            {
                ApplyRelationContextRecursive(children.Get(i), in context);
            }
        }

        private static Entity NormalizeOptionalEntity(Entity entity)
        {
            return entity == default || entity.Id < 0 ? Entity.Null : entity;
        }

        private bool ShouldSkipDuplicatePersistentScopedCreate(
            in PresenterCommand cmd,
            PresenterDefinition definition,
            Entity parentEntity)
        {
            if (definition.DefaultLifetime > 0f || cmd.ScopeTag <= 0)
                return false;
            if (!_runtime.TryGetActiveScopedInstance(
                    cmd.PresenterDefinitionId,
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

        private bool ScopedInstanceParentMatches(Entity presenter, Entity parentEntity)
        {
            if (!World.IsAlive(presenter))
            {
                return false;
            }

            Entity existingParent = World.Has<PresenterParent>(presenter)
                ? World.Get<PresenterParent>(presenter).Parent
                : Entity.Null;
            return existingParent == parentEntity;
        }

        private bool ShouldSkipHandledRootBootstrapCreate(in PresenterCommand cmd)
        {
            if (!World.IsAlive(cmd.Source) ||
                !World.Has<PresenterRootBootstrapHandled>(cmd.Source) ||
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
                    out CompiledPresenterBootstrapRegistry.BootstrapCreateRule[] rules))
            {
                return false;
            }

            for (int i = 0; i < rules.Length; i++)
            {
                if (rules[i].PresenterDefinitionId != cmd.PresenterDefinitionId)
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

        private static uint BuildDefaultBehaviorMask(PresenterDefinition definition)
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

        private void EmitCreatedEvent(Entity presenter, in PresenterState state)
        {
            if (!_events.TryAdd(new PresentationEvent
                {
                    Kind = PresentationEventKind.PresenterCreated,
                    KeyId = state.DefId,
                    Source = state.OwnerEntity,
                    Target = state.OwnerEntity,
                    PresenterEntity = presenter,
                    PayloadB = state.ScopeId,
                    Magnitude = state.StableId,
                }))
            {
                throw new InvalidOperationException("PresentationEventStream is full while publishing PresenterCreated.");
            }
        }

        private void EmitDestroyedEvent(Entity presenter, PresenterState state)
        {
            RemoveStableVisualCacheIfPresent(presenter, in state);
            EmitRetainedPresentationRemovalIfPresent(presenter, in state);
            _animatorStates?.Clear(presenter);

            if (!_events.TryAdd(new PresentationEvent
                {
                    Kind = PresentationEventKind.PresenterDestroyed,
                    KeyId = state.DefId,
                    Source = state.OwnerEntity,
                    Target = state.OwnerEntity,
                    PresenterEntity = presenter,
                    PayloadB = state.ScopeId,
                    Magnitude = state.StableId,
                }))
            {
                throw new InvalidOperationException("PresentationEventStream is full while publishing PresenterDestroyed.");
            }
        }

        private void RemoveStableVisualCacheIfPresent(Entity presenter, in PresenterState state)
        {
            if (_stableDrawCache == null ||
                !World.IsAlive(presenter) ||
                !World.Has<PresenterEmitCache>(presenter) ||
                !_definitions.TryGet(state.DefId, out PresenterDefinition definition))
            {
                return;
            }

            int[] behaviorIndices = definition.CacheableAssetBehaviorIndices;
            BehaviorSlot[] behaviors = definition.Behaviors;
            for (int i = 0; i < behaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[behaviorIndices[i]];
                if (_visualStableIds != null &&
                    _visualStableIds.Remove(
                        PresenterBehaviorRuntimeUtility.ComposeVisualStableKey(
                            state.StableId,
                            slot.SlotIndex,
                            slot.AssetBinding.AssetKind,
                            state.DefId),
                        out int stableId))
                {
                    _stableDrawCache.Remove(stableId);
                }
            }

            _visualStableIds?.ReleasePresenter(state.StableId);
        }

        private void EmitRetainedPresentationRemovalIfPresent(Entity presenter, in PresenterState state)
        {
            if (!_definitions.TryGet(state.DefId, out PresenterDefinition definition) ||
                !World.IsAlive(presenter) ||
                !World.Has<PresenterEmitCache>(presenter) ||
                World.Get<PresenterEmitCache>(presenter).RetainedRequestPresent == 0)
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

        private void MarkHierarchyForBootstrap(Entity root)
        {
            if (!World.IsAlive(root) || !World.Has<PresenterState>(root))
            {
                return;
            }

            MarkSingle(root);
            ref PresenterChildren children = ref World.Get<PresenterChildren>(root);
            for (int i = 0; i < children.Count; i++)
            {
                Entity child = children.Get(i);
                if (World.IsAlive(child))
                {
                    MarkHierarchyForBootstrap(child);
                }
            }
        }

        private void MarkSingle(Entity presenter)
        {
            if (World.Has<PresenterBootstrapPending>(presenter))
            {
                return;
            }

            World.Add(presenter, new PresenterBootstrapPending());
        }

        private void MarkHierarchyForBootstrapIfNeeded(Entity root)
        {
            if (!World.IsAlive(root) || !World.Has<PresenterState>(root))
            {
                return;
            }

            ref readonly PresenterState state = ref World.Get<PresenterState>(root);
            if (_definitions.TryGet(state.DefId, out PresenterDefinition definition) &&
                definition.RequiresBootstrapProcessing)
            {
                MarkSingle(root);
            }

            ref PresenterChildren children = ref World.Get<PresenterChildren>(root);
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
