using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation;

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

        public PerformerRuntimeSystem(
            World world,
            PerformerCommandBuffer commands,
            PresentationEventStream events,
            TransientMarkerBuffer markers,
            PresentationRequestBuffer requests,
            PerformerEntityRuntime runtime,
            PresentationStableIdAllocator stableIds,
            PerformerDefinitionRegistry definitions,
            PerformerAnimatorStateBuffer? animatorStates = null)
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
        }
        public override void Update(in float dt)
        {
            _runtime.ReleaseDeadEntityAnchors(EmitDestroyedEvent);

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

                    case PerformerCommandKind.SetParam:
                        if (World.IsAlive(cmd.PerformerEntity) && World.Has<PerformerState>(cmd.PerformerEntity))
                        {
                            _runtime.SetParam(cmd.PerformerEntity, cmd.ParamKey, cmd.ParamLane, cmd.ParamValue, cmd.IntValue, cmd.VectorValue);
                        }
                        break;

                    case PerformerCommandKind.ActivateBehavior:
                        if (World.IsAlive(cmd.PerformerEntity) && World.Has<PerformerState>(cmd.PerformerEntity) && cmd.TargetBehaviorSlot is >= 0 and < 32)
                        {
                            ref PerformerState state = ref World.Get<PerformerState>(cmd.PerformerEntity);
                            state.BehaviorActiveMask |= 1u << cmd.TargetBehaviorSlot;
                        }
                        break;

                    case PerformerCommandKind.DeactivateBehavior:
                        if (World.IsAlive(cmd.PerformerEntity) && World.Has<PerformerState>(cmd.PerformerEntity) && cmd.TargetBehaviorSlot is >= 0 and < 32)
                        {
                            ref PerformerState state = ref World.Get<PerformerState>(cmd.PerformerEntity);
                            state.BehaviorActiveMask &= ~(1u << cmd.TargetBehaviorSlot);
                        }
                        break;
                    case PerformerCommandKind.InitializeTransform:
                        HandleInitializeTransform(in cmd);
                        break;
                }
            }
            _commands.Clear();

            _runtime.SyncCullVisibility();
            _markers.TickAndRequest(_requests, dt, World);
        }
        private void HandleInitializeTransform(in PerformerCommand cmd)
        {
            Entity performer = cmd.PerformerEntity;
            if (!World.IsAlive(performer) || !World.Has<PerformerState>(performer)) return;
            ref PerformerState state = ref World.Get<PerformerState>(performer);
            if (!_definitions.TryGet(state.DefId, out PerformerDefinition definition)) return;

            Entity owner = state.OwnerEntity;
            bool hasOwnerTransform = World.IsAlive(owner) && World.Has<VisualTransform>(owner);
            VisualTransform ownerTransform = hasOwnerTransform ? World.Get<VisualTransform>(owner) : VisualTransform.Default;

            Vector3 position = hasOwnerTransform ? ownerTransform.Position : World.Get<PerformerWorldPosition>(performer).Value;
            Quaternion rotation = hasOwnerTransform ? ownerTransform.Rotation : Quaternion.Identity;
            Vector3 scale = hasOwnerTransform ? ownerTransform.Scale : Vector3.One;

            position += definition.PositionOffset;

            World.Get<PerformerWorldPosition>(performer).Value = position;
            if (World.Has<PerformerWorldRotation>(performer))
                World.Get<PerformerWorldRotation>(performer).Value = rotation;
            if (World.Has<PerformerWorldScale>(performer))
                World.Get<PerformerWorldScale>(performer).Value = scale;
        }

        private void HandleCreatePerformer(in PerformerCommand cmd)
        {
            if (!_definitions.TryGet(cmd.PerformerDefinitionId, out var definition))
            {
                throw new InvalidOperationException($"Performer definition id={cmd.PerformerDefinitionId} is not registered.");
            }

            if (ShouldSkipDuplicatePersistentScopedCreate(in cmd, definition))
            {
                return;
            }

            Entity parentEntity = cmd.ParentEntity;
            if (parentEntity != Entity.Null && (!World.IsAlive(parentEntity) || !World.Has<PerformerState>(parentEntity)))
            {
                throw new InvalidOperationException(
                    $"CreatePerformer defId={cmd.PerformerDefinitionId} references inactive parent entity.");
            }

            Entity entity = _runtime.Create(
                cmd.PerformerDefinitionId,
                cmd.Source,
                cmd.ScopeTag,
                cmd.AnchorKind,
                cmd.Position,
                _stableIds.Allocate(),
                parentEntity,
                definition);

            ref PerformerState state = ref World.Get<PerformerState>(entity);
            state.BehaviorActiveMask = BuildDefaultBehaviorMask(definition);
            _runtime.SetParamDefault(definition, entity);

            EmitCreatedEvent(entity, World.Get<PerformerState>(entity));
        }

        private bool ShouldSkipDuplicatePersistentScopedCreate(in PerformerCommand cmd, PerformerDefinition definition)
        {
            if (definition.DefaultLifetime > 0f || cmd.ScopeTag <= 0)
                return false;
            return _runtime.HasActiveScopedInstance(
                cmd.PerformerDefinitionId, cmd.Source, cmd.ScopeTag, cmd.AnchorKind, cmd.Position);
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
    }
}

