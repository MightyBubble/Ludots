using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using ThreeKingdomsScenarioMod.Gameplay;

namespace ThreeKingdomsScenarioMod.Systems;

internal sealed class ThreeKingdomsScenarioInteractionSystem : ISystem<float>
{
    private const byte UnassignedSlot = byte.MaxValue;
    private const float HostAttachDistanceCm = 180f;

    private static readonly QueryDescription ExecQuery = new QueryDescription().WithAll<AbilityExecInstance, AbilityStateBuffer>();
    private static readonly QueryDescription ChildSlotQuery = new QueryDescription().WithAll<ChildOf, ThreeKingdomsAttachedSlotRef>();
    private static readonly QueryDescription SelectableQuery = new QueryDescription().WithAll<SelectionSelectableTag>();
    private static readonly QueryDescription EnterAttachmentQuery = new QueryDescription().WithAll<WorldPositionCm, ThreeKingdomsEnterAttachmentState>();
    private static readonly QueryDescription WallClimbQuery = new QueryDescription().WithAll<WorldPositionCm, ThreeKingdomsWallClimbState>();
    private static readonly QueryDescription GateCaptureQuery = new QueryDescription().WithAll<WorldPositionCm, ThreeKingdomsGateCaptureState>();
    private static readonly QueryDescription TrenchFillQuery = new QueryDescription().WithAll<WorldPositionCm, ThreeKingdomsTrenchFillState>();
    private static readonly QueryDescription LadderAttachQuery = new QueryDescription().WithAll<WorldPositionCm, ThreeKingdomsLadderAttachState>();
    private static readonly QueryDescription LadderModeQuery = new QueryDescription().WithAll<ThreeKingdomsLadderFeature, ThreeKingdomsLadderModeState, ChildrenBuffer, ChildOf>();
    private static readonly QueryDescription TunnelTransitQuery = new QueryDescription().WithAll<WorldPositionCm, ThreeKingdomsTunnelTransitState>();
    private static readonly QueryDescription TrenchHostQuery = new QueryDescription().WithAll<WorldPositionCm, ThreeKingdomsTrenchFeature>();

    private readonly GameEngine _engine;
    private readonly World _world;
    private readonly OrderQueue _orders;

    private int _moveToOrderTypeId;
    private int _stopOrderTypeId;

    private int _enterAttachmentAbilityId;
    private int _wallClimbAbilityId;
    private int _gateCaptureAbilityId;
    private int _fillTrenchAbilityId;
    private int _ladderAttachAbilityId;
    private int _ladderDeployOneAbilityId;
    private int _ladderToggleHoldAbilityId;
    private int _tunnelSetExitAbilityId;
    private int _tunnelSummonAbilityId;
    private int _tunnelEvacuateAbilityId;

    public ThreeKingdomsScenarioInteractionSystem(GameEngine engine, OrderQueue orders)
    {
        _engine = engine;
        _world = engine.World;
        _orders = orders;
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        if (!ThreeKingdomsScenarioIds.IsScenarioMap(_engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        EnsureResolvedIds();
        ProcessAbilityExecIntents();
        TickEnterAttachmentStates();
        TickWallClimbStates(dt);
        TickGateCaptureStates(dt);
        TickTrenchFillStates(dt);
        TickLadderAttachStates(dt);
        TickLadderDeployment(dt);
        TickTunnelTransitStates(dt);
        SyncAttachedChildrenToHostSlots();
        SyncSelectionAvailability();
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }

    private void EnsureResolvedIds()
    {
        if (_moveToOrderTypeId <= 0 &&
            _engine.GlobalContext.TryGetValue(CoreServiceKeys.GameConfig.Name, out object? configObj) &&
            configObj is GameConfig config)
        {
            config.Constants.OrderTypeIds.TryGetValue("moveTo", out _moveToOrderTypeId);
            config.Constants.OrderTypeIds.TryGetValue("stop", out _stopOrderTypeId);
        }

        _enterAttachmentAbilityId = ResolveAbilityId(_enterAttachmentAbilityId, ThreeKingdomsScenarioIds.InfantryEnterAttachmentAbilityId);
        _wallClimbAbilityId = ResolveAbilityId(_wallClimbAbilityId, ThreeKingdomsScenarioIds.InfantryWallClimbAbilityId);
        _gateCaptureAbilityId = ResolveAbilityId(_gateCaptureAbilityId, ThreeKingdomsScenarioIds.InfantryGateCaptureAbilityId);
        _fillTrenchAbilityId = ResolveAbilityId(_fillTrenchAbilityId, ThreeKingdomsScenarioIds.InfantryFillTrenchAbilityId);
        _ladderAttachAbilityId = ResolveAbilityId(_ladderAttachAbilityId, ThreeKingdomsScenarioIds.LadderAttachAbilityId);
        _ladderDeployOneAbilityId = ResolveAbilityId(_ladderDeployOneAbilityId, ThreeKingdomsScenarioIds.LadderDeployOneAbilityId);
        _ladderToggleHoldAbilityId = ResolveAbilityId(_ladderToggleHoldAbilityId, ThreeKingdomsScenarioIds.LadderToggleHoldAbilityId);
        _tunnelSetExitAbilityId = ResolveAbilityId(_tunnelSetExitAbilityId, ThreeKingdomsScenarioIds.TunnelSetExitAbilityId);
        _tunnelSummonAbilityId = ResolveAbilityId(_tunnelSummonAbilityId, ThreeKingdomsScenarioIds.TunnelSummonAbilityId);
        _tunnelEvacuateAbilityId = ResolveAbilityId(_tunnelEvacuateAbilityId, ThreeKingdomsScenarioIds.TunnelEvacuateAbilityId);
    }

    private static int ResolveAbilityId(int current, string abilityName)
    {
        return current > 0 ? current : AbilityIdRegistry.GetId(abilityName);
    }

    private void ProcessAbilityExecIntents()
    {
        _world.Query(in ExecQuery, (Entity actor, ref AbilityExecInstance exec, ref AbilityStateBuffer _) =>
        {
            if (exec.OrderId <= 0 || !ShouldProcessExec(actor, exec.OrderId))
            {
                return;
            }

            switch (exec.AbilityId)
            {
                case var abilityId when abilityId == _enterAttachmentAbilityId:
                    BeginEnterAttachment(actor, exec.Target);
                    break;
                case var abilityId when abilityId == _wallClimbAbilityId:
                    BeginWallClimb(actor, exec.Target);
                    break;
                case var abilityId when abilityId == _gateCaptureAbilityId:
                    BeginGateCapture(actor, exec.Target);
                    break;
                case var abilityId when abilityId == _fillTrenchAbilityId:
                    BeginTrenchFill(actor, exec.Target);
                    break;
                case var abilityId when abilityId == _ladderAttachAbilityId:
                    BeginLadderAttach(actor, exec.Target);
                    break;
                case var abilityId when abilityId == _ladderDeployOneAbilityId:
                    TryDeployLadderPassenger(actor, setCooldown: true);
                    break;
                case var abilityId when abilityId == _ladderToggleHoldAbilityId:
                    ToggleLadderHold(actor);
                    break;
                case var abilityId when abilityId == _tunnelSetExitAbilityId:
                    SetTunnelExit(actor, exec.Target);
                    break;
                case var abilityId when abilityId == _tunnelSummonAbilityId:
                    StartTunnelTransfer(actor, reverse: false);
                    break;
                case var abilityId when abilityId == _tunnelEvacuateAbilityId:
                    StartTunnelTransfer(actor, reverse: true);
                    break;
            }
        });
    }

    private bool ShouldProcessExec(Entity actor, int orderId)
    {
        if (_world.Has<ThreeKingdomsProcessedExecState>(actor))
        {
            ref var state = ref _world.Get<ThreeKingdomsProcessedExecState>(actor);
            if (state.LastOrderId == orderId)
            {
                return false;
            }

            state.LastOrderId = orderId;
            return true;
        }

        _world.Add(actor, new ThreeKingdomsProcessedExecState { LastOrderId = orderId });
        return true;
    }

    private void BeginEnterAttachment(Entity actor, Entity host)
    {
        if (!IsAttachableHost(host))
        {
            return;
        }

        ClearActionStates(actor);
        SubmitStop(actor);
        SubmitMoveTo(actor, ResolveHostApproachPosition(host, actor));
        Upsert(actor, new ThreeKingdomsEnterAttachmentState
        {
            Host = host,
            Phase = ThreeKingdomsSiegeActionPhase.Approaching
        });
    }

    private void BeginWallClimb(Entity actor, Entity wall)
    {
        if (!IsWallHost(wall))
        {
            return;
        }

        ClearActionStates(actor);
        SubmitStop(actor);
        bool fromInnerSide = IsOnInnerSide(actor, wall);
        SubmitMoveTo(actor, ResolveWallApproachPosition(wall, fromInnerSide));
        Upsert(actor, new ThreeKingdomsWallClimbState
        {
            Wall = wall,
            Phase = ThreeKingdomsSiegeActionPhase.Approaching,
            ProgressSeconds = 0f,
            FromInnerSide = (byte)(fromInnerSide ? 1 : 0),
            SlotIndex = UnassignedSlot
        });
    }

    private void BeginGateCapture(Entity actor, Entity gate)
    {
        if (!IsGateHost(gate))
        {
            return;
        }

        ClearActionStates(actor);
        SubmitStop(actor);
        SubmitMoveTo(actor, ResolveGateApproachPosition(gate, actor));
        Upsert(actor, new ThreeKingdomsGateCaptureState
        {
            Gate = gate,
            Phase = ThreeKingdomsSiegeActionPhase.Approaching,
            ProgressSeconds = 0f
        });
    }

    private void BeginTrenchFill(Entity actor, Entity trench)
    {
        if (!IsTrenchHost(trench))
        {
            return;
        }

        ClearActionStates(actor);
        SubmitStop(actor);
        SubmitMoveTo(actor, GetWorldPosition(trench));
        Upsert(actor, new ThreeKingdomsTrenchFillState
        {
            Trench = trench,
            Phase = ThreeKingdomsSiegeActionPhase.Approaching,
            ProgressSeconds = 0f,
            SlotIndex = UnassignedSlot
        });
    }

    private void BeginLadderAttach(Entity ladder, Entity wall)
    {
        if (!IsWallHost(wall) || !_world.Has<ThreeKingdomsLadderFeature>(ladder))
        {
            return;
        }

        ClearActionStates(ladder);
        SubmitStop(ladder);
        SubmitMoveTo(ladder, ResolveWallApproachPosition(wall, fromInnerSide: false));
        Upsert(ladder, new ThreeKingdomsLadderAttachState
        {
            Wall = wall,
            Phase = ThreeKingdomsSiegeActionPhase.Approaching,
            ProgressSeconds = 0f,
            SlotIndex = UnassignedSlot
        });

        if (!_world.Has<ThreeKingdomsLadderModeState>(ladder))
        {
            _world.Add(ladder, new ThreeKingdomsLadderModeState());
        }
    }

    private void TickEnterAttachmentStates()
    {
        _world.Query(in EnterAttachmentQuery, (Entity actor, ref WorldPositionCm _, ref ThreeKingdomsEnterAttachmentState state) =>
        {
            if (!IsAttachableHost(state.Host))
            {
                _world.Remove<ThreeKingdomsEnterAttachmentState>(actor);
                return;
            }

            if (state.Phase == ThreeKingdomsSiegeActionPhase.Approaching &&
                IsWithinDistance(actor, ResolveHostApproachPosition(state.Host, actor), HostAttachDistanceCm) &&
                TryAttachActorToHost(actor, state.Host))
            {
                _world.Remove<ThreeKingdomsEnterAttachmentState>(actor);
            }
        });
    }

    private void TickWallClimbStates(float dt)
    {
        _world.Query(in WallClimbQuery, (Entity actor, ref WorldPositionCm _, ref ThreeKingdomsWallClimbState state) =>
        {
            if (!IsWallHost(state.Wall))
            {
                _world.Remove<ThreeKingdomsWallClimbState>(actor);
                return;
            }

            bool fromInnerSide = state.FromInnerSide != 0;
            switch (state.Phase)
            {
                case ThreeKingdomsSiegeActionPhase.Approaching:
                    if (IsWithinDistance(actor, ResolveWallApproachPosition(state.Wall, fromInnerSide), HostAttachDistanceCm))
                    {
                        state.Phase = ThreeKingdomsSiegeActionPhase.Channeling;
                        state.ProgressSeconds = 0f;
                        SubmitStop(actor);
                    }

                    break;
                case ThreeKingdomsSiegeActionPhase.Channeling:
                    if (!_world.TryGet(state.Wall, out ThreeKingdomsWallFeature wall))
                    {
                        _world.Remove<ThreeKingdomsWallClimbState>(actor);
                        return;
                    }

                    state.ProgressSeconds += dt;
                    float climbDuration = HasAttachedLadder(state.Wall)
                        ? MathF.Max(0.05f, wall.FastClimbDurationSeconds)
                        : MathF.Max(0.05f, wall.ClimbDurationSeconds);
                    if (state.ProgressSeconds < climbDuration)
                    {
                        return;
                    }

                    if (TryAttachActorToHost(actor, state.Wall, ref state.SlotIndex))
                    {
                        _world.Remove<ThreeKingdomsWallClimbState>(actor);
                    }

                    break;
            }
        });
    }

    private void TickGateCaptureStates(float dt)
    {
        _world.Query(in GateCaptureQuery, (Entity actor, ref WorldPositionCm _, ref ThreeKingdomsGateCaptureState state) =>
        {
            if (!IsGateHost(state.Gate))
            {
                _world.Remove<ThreeKingdomsGateCaptureState>(actor);
                return;
            }

            switch (state.Phase)
            {
                case ThreeKingdomsSiegeActionPhase.Approaching:
                    if (IsWithinDistance(actor, ResolveGateApproachPosition(state.Gate, actor), HostAttachDistanceCm))
                    {
                        state.Phase = ThreeKingdomsSiegeActionPhase.Channeling;
                        state.ProgressSeconds = 0f;
                        SubmitStop(actor);
                    }

                    break;
                case ThreeKingdomsSiegeActionPhase.Channeling:
                    if (!_world.TryGet(state.Gate, out ThreeKingdomsGateFeature gate))
                    {
                        _world.Remove<ThreeKingdomsGateCaptureState>(actor);
                        return;
                    }

                    state.ProgressSeconds += dt;
                    if (state.ProgressSeconds < MathF.Max(0.05f, gate.CaptureDurationSeconds))
                    {
                        return;
                    }

                    if (_world.TryGet(actor, out Team actorTeam))
                    {
                        Upsert(state.Gate, new Team { Id = actorTeam.Id });
                    }

                    TryAttachActorToHost(actor, state.Gate);
                    _world.Remove<ThreeKingdomsGateCaptureState>(actor);
                    break;
            }
        });
    }

    private void TickTrenchFillStates(float dt)
    {
        _world.Query(in TrenchFillQuery, (Entity actor, ref WorldPositionCm _, ref ThreeKingdomsTrenchFillState state) =>
        {
            if (!IsTrenchHost(state.Trench))
            {
                _world.Remove<ThreeKingdomsTrenchFillState>(actor);
                return;
            }

            switch (state.Phase)
            {
                case ThreeKingdomsSiegeActionPhase.Approaching:
                    if (IsWithinDistance(actor, GetWorldPosition(state.Trench), HostAttachDistanceCm))
                    {
                        state.Phase = ThreeKingdomsSiegeActionPhase.Channeling;
                        state.ProgressSeconds = 0f;
                        SubmitStop(actor);
                    }

                    break;
                case ThreeKingdomsSiegeActionPhase.Channeling:
                    if (!_world.TryGet(state.Trench, out ThreeKingdomsTrenchFeature trench))
                    {
                        _world.Remove<ThreeKingdomsTrenchFillState>(actor);
                        return;
                    }

                    state.ProgressSeconds += dt;
                    Upsert(state.Trench, new ThreeKingdomsTrenchRuntimeState
                    {
                        FillProgressSeconds = MathF.Min(trench.FillDurationSeconds, state.ProgressSeconds)
                    });

                    if (state.ProgressSeconds < MathF.Max(0.05f, trench.FillDurationSeconds))
                    {
                        return;
                    }

                    TryAttachActorToHost(actor, state.Trench, ref state.SlotIndex);
                    _world.Remove<ThreeKingdomsTrenchFillState>(actor);
                    break;
            }
        });
    }

    private void TickLadderAttachStates(float dt)
    {
        _world.Query(in LadderAttachQuery, (Entity ladder, ref WorldPositionCm _, ref ThreeKingdomsLadderAttachState state) =>
        {
            if (!_world.Has<ThreeKingdomsLadderFeature>(ladder) || !IsWallHost(state.Wall))
            {
                _world.Remove<ThreeKingdomsLadderAttachState>(ladder);
                return;
            }

            switch (state.Phase)
            {
                case ThreeKingdomsSiegeActionPhase.Approaching:
                    if (IsWithinDistance(ladder, ResolveWallApproachPosition(state.Wall, fromInnerSide: false), HostAttachDistanceCm))
                    {
                        state.Phase = ThreeKingdomsSiegeActionPhase.Channeling;
                        state.ProgressSeconds = 0f;
                        SubmitStop(ladder);
                    }

                    break;
                case ThreeKingdomsSiegeActionPhase.Channeling:
                    ThreeKingdomsLadderFeature feature = _world.Get<ThreeKingdomsLadderFeature>(ladder);
                    state.ProgressSeconds += dt;
                    if (state.ProgressSeconds < MathF.Max(0.05f, feature.AttachDurationSeconds))
                    {
                        return;
                    }

                    if (TryAttachActorToHost(ladder, state.Wall, ref state.SlotIndex))
                    {
                        _world.Remove<ThreeKingdomsLadderAttachState>(ladder);
                    }

                break;
            }
        });
    }

    private void TickLadderDeployment(float dt)
    {
        _world.Query(in LadderModeQuery, (Entity ladder, ref ThreeKingdomsLadderFeature feature, ref ThreeKingdomsLadderModeState mode, ref ChildrenBuffer _, ref ChildOf parentRef) =>
        {
            if (!_world.IsAlive(parentRef.Parent) || !_world.Has<ThreeKingdomsWallFeature>(parentRef.Parent))
            {
                return;
            }

            mode.DeployCooldownSeconds = MathF.Max(0f, mode.DeployCooldownSeconds - dt);
            if (mode.HoldDeployment != 0 || mode.DeployCooldownSeconds > 0f)
            {
                return;
            }

            if (TryDeployLadderPassenger(ladder, setCooldown: false))
            {
                mode.DeployCooldownSeconds = MathF.Max(0.05f, feature.DeployIntervalSeconds);
            }
        });
    }

    private void TickTunnelTransitStates(float dt)
    {
        _world.Query(in TunnelTransitQuery, (Entity actor, ref WorldPositionCm current, ref ThreeKingdomsTunnelTransitState state) =>
        {
            if (!_world.IsAlive(state.Destination) || !_world.Has<WorldPositionCm>(state.Destination))
            {
                _world.Remove<ThreeKingdomsTunnelTransitState>(actor);
                return;
            }

            Fix64Vec2 destination = GetWorldPosition(state.Destination);
            Fix64Vec2 position = current.Value;
            float dx = destination.X.ToFloat() - position.X.ToFloat();
            float dy = destination.Y.ToFloat() - position.Y.ToFloat();
            float distance = MathF.Sqrt((dx * dx) + (dy * dy));
            float speed = MathF.Max(120f, state.TravelSpeedCmPerSecond);
            float step = speed * dt;

            if (distance <= MathF.Max(step, HostAttachDistanceCm))
            {
                SetWorldPosition(actor, destination + ComputeHostDetachOffset(state.Destination, actor), destination);
                _world.Remove<ThreeKingdomsTunnelTransitState>(actor);
                return;
            }

            float scale = step / MathF.Max(distance, 0.001f);
            Fix64Vec2 delta = Fix64Vec2.FromFloat(dx * scale, dy * scale);
            SetWorldPosition(actor, position + delta, position);
        });
    }

    private void SyncAttachedChildrenToHostSlots()
    {
        _world.Query(in ChildSlotQuery, (Entity child, ref ChildOf childOf, ref ThreeKingdomsAttachedSlotRef slotRef) =>
        {
            if (!_world.IsAlive(childOf.Parent) || !_world.Has<ThreeKingdomsAttachmentHost>(childOf.Parent))
            {
                if (_world.Has<ThreeKingdomsAttachedSlotRef>(child))
                {
                    _world.Remove<ThreeKingdomsAttachedSlotRef>(child);
                }

                return;
            }

            Fix64Vec2 targetPosition = ComputeHostSlotWorldPosition(childOf.Parent, slotRef.SlotIndex);
            SetWorldPosition(child, targetPosition, targetPosition);
        });
    }

    private void SyncSelectionAvailability()
    {
        _world.Query(in SelectableQuery, (Entity entity, ref SelectionSelectableTag _) =>
        {
            bool disabled =
                _world.Has<ChildOf>(entity) ||
                _world.Has<ThreeKingdomsEnterAttachmentState>(entity) ||
                _world.Has<ThreeKingdomsWallClimbState>(entity) ||
                _world.Has<ThreeKingdomsGateCaptureState>(entity) ||
                _world.Has<ThreeKingdomsTrenchFillState>(entity) ||
                _world.Has<ThreeKingdomsLadderAttachState>(entity) ||
                _world.Has<ThreeKingdomsTunnelTransitState>(entity);

            Upsert(entity, disabled ? SelectionSelectableState.Disabled : SelectionSelectableState.EnabledByDefault);
        });
    }

    private bool TryDeployLadderPassenger(Entity ladder, bool setCooldown)
    {
        if (!_world.IsAlive(ladder) ||
            !_world.Has<ChildrenBuffer>(ladder) ||
            !_world.Has<ChildOf>(ladder))
        {
            return false;
        }

        Entity wall = _world.Get<ChildOf>(ladder).Parent;
        if (!IsWallHost(wall))
        {
            return false;
        }

        ref var children = ref _world.Get<ChildrenBuffer>(ladder);
        for (int i = 0; i < children.Count; i++)
        {
            Entity passenger = children.Get(i);
            if (!_world.IsAlive(passenger))
            {
                continue;
            }

            ClearActionStates(passenger);
            if (!TryAttachActorToHost(passenger, wall))
            {
                continue;
            }

            if (setCooldown && _world.TryGet(ladder, out ThreeKingdomsLadderFeature feature))
            {
                byte hold = _world.TryGet(ladder, out ThreeKingdomsLadderModeState mode) ? mode.HoldDeployment : (byte)0;
                Upsert(ladder, new ThreeKingdomsLadderModeState
                {
                    HoldDeployment = hold,
                    DeployCooldownSeconds = MathF.Max(0.05f, feature.DeployIntervalSeconds)
                });
            }

            return true;
        }

        return false;
    }

    private void ToggleLadderHold(Entity ladder)
    {
        if (!_world.Has<ThreeKingdomsLadderModeState>(ladder))
        {
            _world.Add(ladder, new ThreeKingdomsLadderModeState { HoldDeployment = 1 });
            return;
        }

        ref var mode = ref _world.Get<ThreeKingdomsLadderModeState>(ladder);
        mode.HoldDeployment = (byte)(mode.HoldDeployment == 0 ? 1 : 0);
    }

    private void SetTunnelExit(Entity tunnel, Entity exit)
    {
        if (!_world.Has<ThreeKingdomsTunnelFeature>(tunnel) || !_world.Has<ThreeKingdomsTunnelFeature>(exit) || tunnel == exit)
        {
            return;
        }

        Upsert(tunnel, new ThreeKingdomsTunnelExitState { Exit = exit });
    }

    private void StartTunnelTransfer(Entity tunnel, bool reverse)
    {
        if (!_world.Has<ThreeKingdomsTunnelFeature>(tunnel) ||
            !_world.TryGet(tunnel, out ThreeKingdomsTunnelExitState exitState) ||
            !_world.IsAlive(exitState.Exit) ||
            !_world.Has<ThreeKingdomsTunnelFeature>(exitState.Exit))
        {
            return;
        }

        Entity source = reverse ? exitState.Exit : tunnel;
        Entity destination = reverse ? tunnel : exitState.Exit;
        if (!_world.Has<ChildrenBuffer>(source))
        {
            return;
        }

        float travelSpeed = _world.Get<ThreeKingdomsTunnelFeature>(source).TravelSpeedCmPerSecond;
        ref var children = ref _world.Get<ChildrenBuffer>(source);
        int remaining = children.Count;
        while (remaining-- > 0 && children.Count > 0)
        {
            Entity child = children.Get(0);
            if (!_world.IsAlive(child))
            {
                RelationOps.RemoveParent(_world, child);
                continue;
            }

            if (TryTrapTunnelTransit(child, source, destination))
            {
                continue;
            }

            RelationOps.RemoveParent(_world, child);
            if (_world.Has<ThreeKingdomsAttachedSlotRef>(child))
            {
                _world.Remove<ThreeKingdomsAttachedSlotRef>(child);
            }

            ClearActionStates(child);
            Upsert(child, new ThreeKingdomsTunnelTransitState
            {
                Destination = destination,
                TravelSpeedCmPerSecond = travelSpeed
            });
        }
    }

    private bool TryTrapTunnelTransit(Entity actor, Entity source, Entity destination)
    {
        Fix64Vec2 sourcePosition = GetWorldPosition(source);
        Fix64Vec2 destinationPosition = GetWorldPosition(destination);
        Entity blockingTrench = Entity.Null;
        float bestDistance = float.MaxValue;
        _world.Query(in TrenchHostQuery, (Entity trench, ref WorldPositionCm trenchPosition, ref ThreeKingdomsTrenchFeature feature) =>
        {
            float fillProgress = _world.TryGet(trench, out ThreeKingdomsTrenchRuntimeState runtime)
                ? runtime.FillProgressSeconds
                : 0f;
            if (fillProgress >= feature.FillDurationSeconds)
            {
                return;
            }

            if (_world.Has<ChildrenBuffer>(trench) && _world.Get<ChildrenBuffer>(trench).Count >= feature.TrapCapacity)
            {
                return;
            }

            float distance = DistancePointToSegment(trenchPosition.Value, sourcePosition, destinationPosition);
            if (distance < MathF.Max(feature.HalfWidthCm, feature.HalfHeightCm) + 80f && distance < bestDistance)
            {
                bestDistance = distance;
                blockingTrench = trench;
            }
        });

        if (blockingTrench == Entity.Null)
        {
            return false;
        }

        ClearActionStates(actor);
        return TryAttachActorToHost(actor, blockingTrench);
    }

    private bool TryAttachActorToHost(Entity actor, Entity host)
    {
        byte slot = UnassignedSlot;
        return TryAttachActorToHost(actor, host, ref slot);
    }

    private bool TryAttachActorToHost(Entity actor, Entity host, ref byte slot)
    {
        if (!IsAttachableHost(host) || !_world.IsAlive(actor))
        {
            return false;
        }

        if (_world.Has<ChildOf>(actor))
        {
            Entity existingParent = _world.Get<ChildOf>(actor).Parent;
            if (existingParent == host)
            {
                if (slot == UnassignedSlot &&
                    _world.TryGet(actor, out ThreeKingdomsAttachedSlotRef existingSlotRef))
                {
                    slot = existingSlotRef.SlotIndex;
                }

                if (slot == UnassignedSlot && TryClaimHostSlot(host, out byte claimedSlot))
                {
                    slot = claimedSlot;
                }

                if (slot == UnassignedSlot)
                {
                    return false;
                }

                Upsert(actor, new ThreeKingdomsAttachedSlotRef { SlotIndex = slot });
                SubmitStop(actor);
                return true;
            }

            RelationOps.RemoveParent(_world, actor);
        }

        if (slot == UnassignedSlot && !TryClaimHostSlot(host, out slot))
        {
            return false;
        }

        RelationOps.SetParent(_world, actor, host);
        Upsert(actor, new ThreeKingdomsAttachedSlotRef { SlotIndex = slot });
        SubmitStop(actor);
        return true;
    }

    private bool TryClaimHostSlot(Entity host, out byte slotIndex)
    {
        slotIndex = 0;
        if (!_world.TryGet(host, out ThreeKingdomsAttachmentHost hostState))
        {
            return false;
        }

        int slotCount = hostState.SlotCount > 0 ? hostState.SlotCount : 1;
        int capacity = hostState.Capacity <= 0 ? slotCount : hostState.Capacity;
        int childCount = _world.Has<ChildrenBuffer>(host) ? _world.Get<ChildrenBuffer>(host).Count : 0;
        if (childCount >= capacity)
        {
            return false;
        }

        for (int i = 0; i < slotCount; i++)
        {
            if (!IsHostSlotOccupied(host, (byte)i))
            {
                slotIndex = (byte)i;
                return true;
            }
        }

        return false;
    }

    private bool IsHostSlotOccupied(Entity host, byte slotIndex)
    {
        if (!_world.Has<ChildrenBuffer>(host))
        {
            return false;
        }

        ref var children = ref _world.Get<ChildrenBuffer>(host);
        for (int i = 0; i < children.Count; i++)
        {
            Entity child = children.Get(i);
            if (!_world.IsAlive(child) || !_world.TryGet(child, out ThreeKingdomsAttachedSlotRef slotRef))
            {
                continue;
            }

            if (slotRef.SlotIndex == slotIndex)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsAttachableHost(Entity entity)
    {
        return _world.IsAlive(entity) && _world.Has<ThreeKingdomsAttachmentHost>(entity);
    }

    private bool IsWallHost(Entity entity)
    {
        return IsAttachableHost(entity) && _world.Has<ThreeKingdomsWallFeature>(entity);
    }

    private bool IsGateHost(Entity entity)
    {
        return IsAttachableHost(entity) && _world.Has<ThreeKingdomsGateFeature>(entity);
    }

    private bool IsTrenchHost(Entity entity)
    {
        return IsAttachableHost(entity) && _world.Has<ThreeKingdomsTrenchFeature>(entity);
    }

    private Fix64Vec2 ResolveHostApproachPosition(Entity host, Entity actor)
    {
        if (_world.Has<ThreeKingdomsWallFeature>(host))
        {
            return ResolveWallApproachPosition(host, IsOnInnerSide(actor, host));
        }

        if (_world.Has<ThreeKingdomsGateFeature>(host))
        {
            return ResolveGateApproachPosition(host, actor);
        }

        return GetWorldPosition(host);
    }

    private Fix64Vec2 ResolveWallApproachPosition(Entity wall, bool fromInnerSide)
    {
        Fix64Vec2 center = GetWorldPosition(wall);
        var forward = GetForwardVector(wall);
        ThreeKingdomsWallFeature feature = _world.Get<ThreeKingdomsWallFeature>(wall);
        float signedOffset = fromInnerSide ? feature.InnerOffsetCm : -feature.OuterOffsetCm;
        return center + Fix64Vec2.FromFloat(forward.X * signedOffset, forward.Y * signedOffset);
    }

    private Fix64Vec2 ResolveGateApproachPosition(Entity gate, Entity actor)
    {
        Fix64Vec2 center = GetWorldPosition(gate);
        var forward = GetForwardVector(gate);
        ThreeKingdomsGateFeature feature = _world.Get<ThreeKingdomsGateFeature>(gate);
        bool innerSide = IsOnInnerSide(actor, gate);
        float signedOffset = innerSide ? feature.InnerOffsetCm : -feature.OuterOffsetCm;
        return center + Fix64Vec2.FromFloat(forward.X * signedOffset, forward.Y * signedOffset);
    }

    private bool IsOnInnerSide(Entity actor, Entity host)
    {
        Fix64Vec2 actorPosition = GetWorldPosition(actor);
        Fix64Vec2 hostPosition = GetWorldPosition(host);
        var forward = GetForwardVector(host);
        float dot = ((actorPosition.X.ToFloat() - hostPosition.X.ToFloat()) * forward.X) +
                    ((actorPosition.Y.ToFloat() - hostPosition.Y.ToFloat()) * forward.Y);
        return dot >= 0f;
    }

    private bool HasAttachedLadder(Entity wall)
    {
        if (!_world.Has<ChildrenBuffer>(wall))
        {
            return false;
        }

        ref var children = ref _world.Get<ChildrenBuffer>(wall);
        for (int i = 0; i < children.Count; i++)
        {
            Entity child = children.Get(i);
            if (_world.IsAlive(child) && _world.Has<ThreeKingdomsLadderFeature>(child))
            {
                return true;
            }
        }

        return false;
    }

    private Fix64Vec2 ComputeHostSlotWorldPosition(Entity host, byte slotIndex)
    {
        Fix64Vec2 center = GetWorldPosition(host);
        ThreeKingdomsAttachmentHost layout = _world.Get<ThreeKingdomsAttachmentHost>(host);
        var forward = GetForwardVector(host);
        float rightX = -forward.Y;
        float rightY = forward.X;

        int slotCount = layout.SlotCount > 0 ? layout.SlotCount : 1;
        float centeredIndex = slotIndex - ((slotCount - 1) * 0.5f);
        float forwardOffset = 0f;
        float sideOffset = 0f;

        switch (layout.Layout)
        {
            case ThreeKingdomsAttachmentLayoutKind.FrontLine:
                forwardOffset = layout.ForwardOffsetCm;
                sideOffset = centeredIndex * layout.SideSpacingCm;
                break;
            case ThreeKingdomsAttachmentLayoutKind.BackLine:
                forwardOffset = -layout.ForwardOffsetCm;
                sideOffset = centeredIndex * layout.SideSpacingCm;
                break;
            case ThreeKingdomsAttachmentLayoutKind.Ring:
            {
                float angle = ((MathF.PI * 2f) / slotCount) * slotIndex;
                forwardOffset = MathF.Cos(angle) * layout.RadiusCm;
                sideOffset = MathF.Sin(angle) * layout.RadiusCm;
                break;
            }
        }

        return center + Fix64Vec2.FromFloat(
            (forward.X * forwardOffset) + (rightX * sideOffset),
            (forward.Y * forwardOffset) + (rightY * sideOffset));
    }

    private Fix64Vec2 ComputeHostDetachOffset(Entity host, Entity actor)
    {
        var forward = GetForwardVector(host);
        float rightX = -forward.Y;
        float rightY = forward.X;
        float sideBias = (_world.TryGet(actor, out Team team) ? (team.Id & 1) : actor.Id & 1) == 0 ? -150f : 150f;
        return Fix64Vec2.FromFloat((forward.X * 220f) + (rightX * sideBias), (forward.Y * 220f) + (rightY * sideBias));
    }

    private (float X, float Y) GetForwardVector(Entity entity)
    {
        float angle = _world.TryGet(entity, out FacingDirection facing) ? facing.AngleRad : 0f;
        return (MathF.Cos(angle), MathF.Sin(angle));
    }

    private bool IsWithinDistance(Entity actor, Fix64Vec2 target, float radiusCm)
    {
        if (!_world.TryGet(actor, out WorldPositionCm current))
        {
            return false;
        }

        float dx = current.Value.X.ToFloat() - target.X.ToFloat();
        float dy = current.Value.Y.ToFloat() - target.Y.ToFloat();
        return ((dx * dx) + (dy * dy)) <= (radiusCm * radiusCm);
    }

    private static float DistancePointToSegment(Fix64Vec2 point, Fix64Vec2 a, Fix64Vec2 b)
    {
        float px = point.X.ToFloat();
        float py = point.Y.ToFloat();
        float ax = a.X.ToFloat();
        float ay = a.Y.ToFloat();
        float bx = b.X.ToFloat();
        float by = b.Y.ToFloat();

        float abx = bx - ax;
        float aby = by - ay;
        float abLengthSq = (abx * abx) + (aby * aby);
        if (abLengthSq <= 0.001f)
        {
            float dx = px - ax;
            float dy = py - ay;
            return MathF.Sqrt((dx * dx) + (dy * dy));
        }

        float t = (((px - ax) * abx) + ((py - ay) * aby)) / abLengthSq;
        t = Math.Clamp(t, 0f, 1f);
        float closestX = ax + (abx * t);
        float closestY = ay + (aby * t);
        float cx = px - closestX;
        float cy = py - closestY;
        return MathF.Sqrt((cx * cx) + (cy * cy));
    }

    private Fix64Vec2 GetWorldPosition(Entity entity)
    {
        return _world.TryGet(entity, out WorldPositionCm position) ? position.Value : Fix64Vec2.Zero;
    }

    private void SubmitMoveTo(Entity actor, Fix64Vec2 destination)
    {
        if (_moveToOrderTypeId <= 0 || !_world.Has<OrderBuffer>(actor))
        {
            return;
        }

        var order = new Order
        {
            OrderTypeId = _moveToOrderTypeId,
            Actor = actor,
            PlayerId = ResolvePlayerId(actor),
            SubmitMode = OrderSubmitMode.Immediate
        };
        order.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
        order.Args.Spatial.Mode = OrderCollectionMode.Single;
        order.Args.Spatial.WorldCm = new System.Numerics.Vector3(destination.X.ToFloat(), 0f, destination.Y.ToFloat());
        _orders.TryEnqueue(order);
    }

    private void SubmitStop(Entity actor)
    {
        if (_stopOrderTypeId <= 0 || !_world.Has<OrderBuffer>(actor))
        {
            return;
        }

        _orders.TryEnqueue(new Order
        {
            OrderTypeId = _stopOrderTypeId,
            Actor = actor,
            PlayerId = ResolvePlayerId(actor),
            SubmitMode = OrderSubmitMode.Immediate
        });
    }

    private int ResolvePlayerId(Entity actor)
    {
        return _world.TryGet(actor, out PlayerOwner owner) && owner.PlayerId > 0
            ? owner.PlayerId
            : 1;
    }

    private void SetWorldPosition(Entity entity, in Fix64Vec2 position, in Fix64Vec2 previous)
    {
        Upsert(entity, new WorldPositionCm { Value = position });
        Upsert(entity, new PreviousWorldPositionCm { Value = previous });
    }

    private void ClearActionStates(Entity entity)
    {
        RemoveIfPresent<ThreeKingdomsEnterAttachmentState>(entity);
        RemoveIfPresent<ThreeKingdomsWallClimbState>(entity);
        RemoveIfPresent<ThreeKingdomsGateCaptureState>(entity);
        RemoveIfPresent<ThreeKingdomsTrenchFillState>(entity);
        RemoveIfPresent<ThreeKingdomsLadderAttachState>(entity);
        RemoveIfPresent<ThreeKingdomsTunnelTransitState>(entity);
    }

    private void RemoveIfPresent<T>(Entity entity)
    {
        if (_world.Has<T>(entity))
        {
            _world.Remove<T>(entity);
        }
    }

    private void Upsert<T>(Entity entity, in T component)
    {
        if (_world.Has<T>(entity))
        {
            _world.Set(entity, component);
        }
        else
        {
            _world.Add(entity, component);
        }
    }
}
