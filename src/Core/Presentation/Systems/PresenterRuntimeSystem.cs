using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Diagnostics;
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
        private readonly PresenterCommandKindRegistry? _extensionCommands;
        private readonly PresenterCommandOps _extensionCommandOps;
        private readonly PresenterTimerTable? _timers;
        private readonly PresenterAssetEmitRuntime _assetEmitter;
        private readonly SoundRequestBuffer? _soundRequests;
        private Entity[] _ownerDestroyScratch = Array.Empty<Entity>();
        private int _lastCullSyncStructureVersion = -1;

        public PresenterSinkDiagnostics SinkDiagnostics { get; } = new();

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
            PresenterCommandKindRegistry? extensionCommands = null,
            PresenterTimerTable? timers = null,
            Dictionary<string, object>? globals = null,
            SoundRequestBuffer? soundRequests = null)
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
            _timers = timers;
            _soundRequests = soundRequests;
            _extensionCommandOps = new PresenterCommandOps(World, _runtime, _definitions, MarkHierarchyForBootstrap);
            _assetEmitter = new PresenterAssetEmitRuntime(
                World,
                _runtime,
                _requests,
                globals ?? new Dictionary<string, object>(),
                _animatorStates!,
                _soundRequests!,
                _visualStableIds);
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
                    case PresenterCommandKind.SinkParamToAsset:
                        HandleSinkParamToAsset(in cmd);
                        break;

                    case PresenterCommandKind.InitializeTransform:
                        HandleInitializeTransform(in cmd);
                        break;

                    case PresenterCommandKind.TimerSet:
                        if (World.IsAlive(cmd.PresenterEntity) && World.Has<PresenterState>(cmd.PresenterEntity))
                        {
                            ref readonly PresenterState timerOwner = ref World.Get<PresenterState>(cmd.PresenterEntity);
                            RequireTimerTable(PresenterCommandKind.TimerSet).Set(
                                timerOwner.StableId,
                                cmd.PresenterEntity,
                                timerOwner.OwnerEntity,
                                cmd.TimerNameId,
                                cmd.TimerDurationSeconds,
                                cmd.TimerDurationRangeSeconds);
                        }
                        break;

                    case PresenterCommandKind.TimerKill:
                        if (World.IsAlive(cmd.PresenterEntity) && World.Has<PresenterState>(cmd.PresenterEntity))
                        {
                            ref readonly PresenterState timerOwner = ref World.Get<PresenterState>(cmd.PresenterEntity);
                            PresenterTimerTable table = RequireTimerTable(PresenterCommandKind.TimerKill);
                            // Killing the compiled duration timer cancels the instance's only
                            // scheduled destroy, so the transient presenter is torn down now
                            // through the destroy funnel instead of leaking.
                            int durationNameId = PresenterTimerNameRegistry.GetId(PresenterTimerNameRegistry.DurationTimerName);
                            if (durationNameId > 0 && table.Contains(timerOwner.StableId, durationNameId))
                            {
                                _runtime.Destroy(cmd.PresenterEntity, EmitDestroyedEvent);
                                break;
                            }

                            if (cmd.TimerNameId == PresenterTimerNameRegistry.AllTimersId)
                            {
                                table.KillAll(timerOwner.StableId);
                            }
                            else
                            {
                                table.Kill(timerOwner.StableId, cmd.TimerNameId);
                            }
                        }
                        break;

                    case PresenterCommandKind.Extension:
                        HandleExtensionCommand(in cmd);
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported presenter command kind '{cmd.CommandKind}' (id={ResolveCommandKindId(in cmd)}).");
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

        private PresenterTimerTable RequireTimerTable(PresenterCommandKind kind)
        {
            return _timers ?? throw new InvalidOperationException(
                $"{kind} requires a PresenterTimerTable; this PresenterRuntimeSystem was constructed without one.");
        }

        private void HandleSinkParamToAsset(in PresenterCommand cmd)
        {
            int commandId = ResolveCommandKindId(in cmd);

            Entity target = cmd.PresenterEntity;
            if ((target == Entity.Null || !World.IsAlive(target) || !World.Has<PresenterState>(target)) &&
                cmd.PresenterDefinitionId > 0 &&
                cmd.ScopeTag > 0)
            {
                _runtime.TryGetActiveScopedInstance(
                    cmd.PresenterDefinitionId,
                    cmd.Source,
                    cmd.ScopeTag,
                    cmd.AnchorKind,
                    cmd.Position,
                    out target);
            }

            if (target == Entity.Null || !World.IsAlive(target) || !World.Has<PresenterState>(target))
            {
                RejectSinkCommand(in cmd, commandId, Entity.Null, 0, PresenterSinkRejection.TargetPresenterMissing,
                    "target presenter handle did not resolve to an alive presenter instance");
                return;
            }

            ref PresenterState state = ref World.Get<PresenterState>(target);
            if (!_definitions.TryGet(state.DefId, out PresenterDefinition definition))
            {
                RejectSinkCommand(in cmd, commandId, target, state.DefId, PresenterSinkRejection.TargetDefinitionMissing,
                    $"target presenter definition id={state.DefId} is not registered");
                return;
            }

            int slotIndex = cmd.TargetBehaviorSlot;
            if (slotIndex is < 0 or >= 32)
            {
                RejectSinkCommand(in cmd, commandId, target, state.DefId, PresenterSinkRejection.AssetSlotMissing,
                    $"asset slot index {slotIndex} is outside the valid 0..31 range");
                return;
            }

            if ((definition.AssetBindingSlotMask & (1u << slotIndex)) == 0)
            {
                RejectSinkCommand(in cmd, commandId, target, state.DefId, PresenterSinkRejection.AssetSlotNotAssetBinding,
                    $"behavior slot {slotIndex} of definition id={state.DefId} is not an asset slot");
                return;
            }

            if (!TryGetBehaviorSlot(definition, slotIndex, out BehaviorSlot slot))
            {
                RejectSinkCommand(in cmd, commandId, target, state.DefId, PresenterSinkRejection.AssetSlotMissing,
                    $"behavior slot {slotIndex} of definition id={state.DefId} is not declared");
                return;
            }

            if ((state.BehaviorActiveMask & (1u << slotIndex)) == 0)
            {
                RejectSinkCommand(in cmd, commandId, target, state.DefId, PresenterSinkRejection.AssetSlotInactive,
                    $"behavior slot {slotIndex} of definition id={state.DefId} is deactivated");
                return;
            }

            if (!TryReadSinkLaneValue(target, cmd.ParamKey, cmd.ParamLane))
            {
                bool presentOnOtherLane =
                    (cmd.ParamLane != ParamLane.Float && _runtime.TryResolveFloat(target, cmd.ParamKey, out _)) ||
                    (cmd.ParamLane != ParamLane.Int && _runtime.TryResolveInt(target, cmd.ParamKey, out _)) ||
                    (cmd.ParamLane != ParamLane.Vector && _runtime.TryResolveVector(target, cmd.ParamKey, out _));
                RejectSinkCommand(in cmd, commandId, target, state.DefId,
                    presentOnOtherLane ? PresenterSinkRejection.LaneTypeMismatch : PresenterSinkRejection.LaneMissing,
                    presentOnOtherLane
                        ? $"param key {cmd.ParamKey} is present on a different lane than requested {cmd.ParamLane}"
                        : $"param key {cmd.ParamKey} has no current value on lane {cmd.ParamLane}");
                return;
            }

            if (!World.Has<PresenterCullState>(target) ||
                !World.Has<PresenterWorldPosition>(target) ||
                !World.Has<PresenterWorldRotation>(target) ||
                !World.Has<PresenterWorldFacing>(target) ||
                !World.Has<PresenterWorldScale>(target))
            {
                RejectSinkCommand(in cmd, commandId, target, state.DefId, PresenterSinkRejection.TargetEmitComponentsMissing,
                    $"target presenter is missing emit components required for a synchronous asset write");
                return;
            }

            int requestsBefore = _requests.Count;
            int soundRequestsBefore = _soundRequests?.Count ?? 0;
            ref readonly PresenterCullState cull = ref World.Get<PresenterCullState>(target);
            ref readonly PresenterWorldPosition position = ref World.Get<PresenterWorldPosition>(target);
            ref readonly PresenterWorldRotation rotation = ref World.Get<PresenterWorldRotation>(target);
            ref readonly PresenterWorldFacing facing = ref World.Get<PresenterWorldFacing>(target);
            ref readonly PresenterWorldScale scale = ref World.Get<PresenterWorldScale>(target);
            uint sinkLocalOffsetConsumedMask = 0u;
            _assetEmitter.Emit(
                target,
                in state,
                definition,
                in slot,
                in slot.AssetBinding,
                cull.LOD,
                cull.OwnerCullVisible,
                position.Value,
                rotation.Value,
                in facing,
                scale.Value,
                ref sinkLocalOffsetConsumedMask);

            if (_requests.Count == requestsBefore && (_soundRequests?.Count ?? 0) == soundRequestsBefore)
            {
                RejectSinkCommand(in cmd, commandId, target, state.DefId, PresenterSinkRejection.AssetWriteSuppressed,
                    $"asset kind {slot.AssetBinding.AssetKind} at slot {slotIndex} produced no synchronous write at LOD {cull.LOD}");
                return;
            }

            AcceptSinkCommand(in cmd, commandId, target, state.DefId, slotIndex);
        }

        private static bool TryGetBehaviorSlot(PresenterDefinition definition, int slotIndex, out BehaviorSlot slot)
        {
            BehaviorSlot[] behaviors = definition.Behaviors;
            for (int i = 0; i < behaviors.Length; i++)
            {
                if (behaviors[i].SlotIndex == slotIndex)
                {
                    slot = behaviors[i];
                    return true;
                }
            }

            slot = default;
            return false;
        }

        private bool TryReadSinkLaneValue(Entity target, int paramKey, ParamLane lane)
        {
            return lane switch
            {
                ParamLane.Float => _runtime.TryResolveFloat(target, paramKey, out _),
                ParamLane.Int => _runtime.TryResolveInt(target, paramKey, out _),
                ParamLane.Vector => _runtime.TryResolveVector(target, paramKey, out _),
                _ => false,
            };
        }

        private void AcceptSinkCommand(in PresenterCommand cmd, int commandId, Entity target, int definitionId, int slotIndex)
        {
            string message =
                $"SinkParamToAsset accepted: commandId={commandId} target={target} definitionId={definitionId} " +
                $"paramKey={cmd.ParamKey} lane={cmd.ParamLane} slot={slotIndex}";
            SinkDiagnostics.Record(new PresenterSinkOutcome(
                accepted: true,
                PresenterSinkRejection.None,
                commandId,
                target,
                definitionId,
                cmd.ParamKey,
                cmd.ParamLane,
                slotIndex,
                message));
            Log.Info(LogChannels.Presentation, message);
        }

        private void RejectSinkCommand(
            in PresenterCommand cmd,
            int commandId,
            Entity target,
            int definitionId,
            PresenterSinkRejection rejection,
            string reason)
        {
            string message =
                $"SinkParamToAsset rejected: reason={rejection} commandId={commandId} target={target} definitionId={definitionId} " +
                $"paramKey={cmd.ParamKey} lane={cmd.ParamLane} slot={cmd.TargetBehaviorSlot}: {reason}";
            SinkDiagnostics.Record(new PresenterSinkOutcome(
                accepted: false,
                rejection,
                commandId,
                target,
                definitionId,
                cmd.ParamKey,
                cmd.ParamLane,
                cmd.TargetBehaviorSlot,
                message));
            Log.Warn(LogChannels.Presentation, message);
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
                throw new InvalidOperationException("Presenter extension command requires a positive CommandKindId.");
            }

            if (_extensionCommands == null || !_extensionCommands.TryGetDescriptor(commandKindId, out PresenterCommandExtensionDescriptor descriptor))
            {
                throw new InvalidOperationException($"No extension presenter command handler registered for id {commandKindId}.");
            }

            if (descriptor.RouteStrategy != cmd.RouteStrategy)
            {
                throw new InvalidOperationException(
                    $"Extension presenter command id {commandKindId} was routed as {cmd.RouteStrategy}, but registered route is {descriptor.RouteStrategy}.");
            }

            if (RouteRequiresRoutedPresenter(cmd.RouteStrategy) &&
                (!World.IsAlive(cmd.PresenterEntity) || !World.Has<PresenterState>(cmd.PresenterEntity)))
            {
                throw new InvalidOperationException(
                    $"Extension presenter command id {commandKindId} route {cmd.RouteStrategy} requires a routed presenter entity.");
            }

            _extensionCommandOps.Bind(cmd.PresenterEntity);
            var context = new PresenterCommandExecutionContext(in cmd, _extensionCommandOps);
            descriptor.Handler(in context);
        }

        private static int ResolveCommandKindId(in PresenterCommand cmd)
        {
            return cmd.CommandKindId != 0 ? cmd.CommandKindId : (byte)cmd.CommandKind;
        }

        private static bool RouteRequiresRoutedPresenter(PresenterCommandRouteStrategy route)
        {
            return route is PresenterCommandRouteStrategy.ExistingInstances
                or PresenterCommandRouteStrategy.ScopedInstance;
        }

        private sealed class PresenterCommandOps : IPresenterCommandOps
        {
            private readonly World _world;
            private readonly PresenterEntityRuntime _runtime;
            private readonly PresenterDefinitionRegistry _definitions;
            private readonly Action<Entity> _markHierarchyForBootstrap;
            private Entity _presenter;

            public PresenterCommandOps(
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

            public bool HasRoutedPresenter =>
                _world.IsAlive(_presenter) && _world.Has<PresenterState>(_presenter);

            public void Bind(Entity presenter)
            {
                _presenter = presenter;
            }

            public void SetParam(int paramKey, ParamLane lane, float floatValue = 0f, int intValue = 0, Vector4 vectorValue = default)
            {
                RequireRoutedPresenter();
                _runtime.SetParamAndPropagateToAffectedChildren(_presenter, paramKey, lane, floatValue, intValue, vectorValue);
            }

            public void ClearParam(int paramKey, ParamLane lane)
            {
                RequireRoutedPresenter();
                _runtime.ClearParamAndPropagateToAffectedChildren(_presenter, paramKey, lane);
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
                RequireRoutedPresenter();
                ref PresenterState state = ref _world.Get<PresenterState>(_presenter);
                if (!_definitions.TryGet(state.DefId, out PresenterDefinition definition))
                {
                    throw new InvalidOperationException($"Presenter definition id={state.DefId} is not registered.");
                }

                if (_runtime.SetBehaviorActive(_presenter, definition, slotIndex, active))
                {
                    _markHierarchyForBootstrap(_presenter);
                }
            }

            private void RequireRoutedPresenter()
            {
                if (!HasRoutedPresenter)
                {
                    throw new InvalidOperationException("Presenter extension command operation requires a routed presenter entity.");
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
            _timers?.KillAll(state.StableId);

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
                _requests.RemoveSurfaceSource(state.OwnerEntity, state.StableId);
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
                AssetKind.WorldHud => HudItemIdentity.ComposePresenterStableId(state.StableId, WorldHudItemKind.Bar, state.DefId, slot.SlotIndex),
                AssetKind.WorldText => HudItemIdentity.ComposePresenterStableId(state.StableId, WorldHudItemKind.Text, state.DefId, slot.SlotIndex),
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
                    _requests.RemoveWorldHud(state.OwnerEntity, stableId);
                    break;
                case AssetKind.Spline:
                    _requests.RemoveSplineRibbon(state.OwnerEntity, stableId);
                    break;
                case AssetKind.GroundOverlay:
                    _requests.RemoveGroundOverlay(state.OwnerEntity, stableId);
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
