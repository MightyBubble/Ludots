using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using RtsMultiplayerFrontlineMod.Runtime;

namespace RtsMultiplayerFrontlineMod.Systems;

internal sealed class FrontlinePreMatchOrderGateSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<FrontlineParticipant, OrderBuffer>();

    private readonly FrontlineRuntime _runtime;
    private readonly OrderTypeRegistry _orderTypes;

    public FrontlinePreMatchOrderGateSystem(World world, FrontlineRuntime runtime, OrderTypeRegistry orderTypes) : base(world)
    {
        _runtime = runtime;
        _orderTypes = orderTypes;
    }

    public override void Update(in float dt)
    {
        if (!_runtime.IsActive || _runtime.CanAdvanceGameplay)
        {
            return;
        }

        foreach (ref Chunk chunk in World.Query(in Query))
        {
            Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (!buffers[index].IsEmpty || buffers[index].HasPending)
                {
                    OrderSubmitter.CancelAll(World, Unsafe.Add(ref first, index), _orderTypes);
                }
            }
        }
    }
}

internal sealed class FrontlineTagBindingSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<FrontlineParticipant, Team, GameplayTagContainer, TagCountContainer, FrontlineTagBindingState>();
    private static readonly QueryDescription NodeQuery = new QueryDescription()
        .WithAll<FrontlineCrystalNode, GameplayTagContainer, TagCountContainer, FrontlineTagBindingState>();

    private readonly FrontlineRuntime _runtime;
    private readonly TagOps _tagOps;
    private readonly int _harvesterTagId;
    private readonly int _infantryTagId;
    private readonly int _crystalNodeTagId;

    public FrontlineTagBindingSystem(World world, FrontlineRuntime runtime, TagOps tagOps) : base(world)
    {
        _runtime = runtime;
        _tagOps = tagOps;
        _harvesterTagId = TagRegistry.Register(runtime.Config.HarvesterTag);
        _infantryTagId = TagRegistry.Register(runtime.Config.InfantryTag);
        _crystalNodeTagId = TagRegistry.Register(runtime.Config.CrystalNodeTag);
    }

    public override void Update(in float dt)
    {
        if (!_runtime.IsActive)
        {
            return;
        }

        foreach (ref Chunk chunk in World.Query(in Query))
        {
            Span<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            Span<Team> teams = chunk.GetSpan<Team>();
            Span<GameplayTagContainer> tags = chunk.GetSpan<GameplayTagContainer>();
            Span<TagCountContainer> counts = chunk.GetSpan<TagCountContainer>();
            Span<FrontlineTagBindingState> states = chunk.GetSpan<FrontlineTagBindingState>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (states[index].IsBound != 0)
                {
                    continue;
                }

                Entity entity = Unsafe.Add(ref first, index);
                participants[index].SideIndex = _runtime.Config.ResolveSideIndex(teams[index].Id);
                int tagId = World.Has<FrontlineHarvester>(entity)
                    ? _harvesterTagId
                    : World.Has<FrontlineInfantry>(entity)
                        ? _infantryTagId
                        : World.Has<FrontlineCrystalNode>(entity)
                            ? _crystalNodeTagId
                            : 0;
                if (tagId > 0)
                {
                    _tagOps.AddTag(ref tags[index], ref counts[index], tagId);
                }

                states[index].IsBound = 1;
            }
        }

        foreach (ref Chunk chunk in World.Query(in NodeQuery))
        {
            Span<GameplayTagContainer> tags = chunk.GetSpan<GameplayTagContainer>();
            Span<TagCountContainer> counts = chunk.GetSpan<TagCountContainer>();
            Span<FrontlineTagBindingState> states = chunk.GetSpan<FrontlineTagBindingState>();
            foreach (int index in chunk)
            {
                if (states[index].IsBound != 0)
                {
                    continue;
                }

                _tagOps.AddTag(ref tags[index], ref counts[index], _crystalNodeTagId);
                states[index].IsBound = 1;
            }
        }
    }
}

internal sealed class FrontlineTrainingAdmissionSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<FrontlineCore, FrontlineCoreState, OrderBuffer, AbilityStateBuffer, AttributeBuffer>();

    private readonly FrontlineRuntime _runtime;
    private readonly OrderTypeRegistry _orderTypes;
    private readonly int _castAbilityOrderTypeId;
    private readonly int _trainAbilityId;
    private readonly int _crystalAttributeId;

    public FrontlineTrainingAdmissionSystem(World world, FrontlineRuntime runtime, OrderTypeRegistry orderTypes) : base(world)
    {
        _runtime = runtime;
        _orderTypes = orderTypes;
        _castAbilityOrderTypeId = orderTypes.GetId(runtime.Config.CastAbilityOrderTypeKey);
        _trainAbilityId = AbilityIdRegistry.GetId(runtime.Config.TrainAbilityId);
        _crystalAttributeId = AttributeRegistry.Register(runtime.Config.CrystalAttribute);
        if (_trainAbilityId <= 0)
        {
            throw new InvalidOperationException($"RTS Frontline train ability '{runtime.Config.TrainAbilityId}' is not registered.");
        }
    }

    public override void Update(in float dt)
    {
        if (!_runtime.CanAdvanceGameplay)
        {
            return;
        }

        foreach (ref Chunk chunk in World.Query(in Query))
        {
            Span<FrontlineCoreState> coreStates = chunk.GetSpan<FrontlineCoreState>();
            Span<OrderBuffer> orders = chunk.GetSpan<OrderBuffer>();
            Span<AbilityStateBuffer> abilities = chunk.GetSpan<AbilityStateBuffer>();
            Span<AttributeBuffer> attributes = chunk.GetSpan<AttributeBuffer>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                ref OrderBuffer buffer = ref orders[index];
                if (!buffer.HasActive || buffer.ActiveOrder.Order.OrderTypeId != _castAbilityOrderTypeId)
                {
                    continue;
                }

                Order order = buffer.ActiveOrder.Order;
                int slot = order.Args.I0;
                if (abilities[index].Get(slot).AbilityId != _trainAbilityId ||
                    coreStates[index].LastHandledTrainOrderId == order.OrderId)
                {
                    continue;
                }

                Entity core = Unsafe.Add(ref first, index);
                coreStates[index].LastHandledTrainOrderId = order.OrderId;
                float crystals = attributes[index].GetCurrent(_crystalAttributeId);
                if (crystals < _runtime.Config.TrainCostCrystals)
                {
                    coreStates[index].LastTrainResult = FrontlineTrainResult.InsufficientCrystals;
                    OrderSubmitter.NotifyOrderComplete(World, core, _orderTypes);
                    continue;
                }

                attributes[index].SetCurrent(_crystalAttributeId, crystals - _runtime.Config.TrainCostCrystals);
                coreStates[index].LastTrainResult = FrontlineTrainResult.Accepted;
            }
        }
    }
}

internal sealed class FrontlineHarvestSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription HarvesterQuery = new QueryDescription()
        .WithAll<FrontlineHarvester, FrontlineParticipant, FrontlineHarvestState, OrderBuffer, WorldPositionCm, PlayerOwner>();
    private static readonly QueryDescription CoreQuery = new QueryDescription()
        .WithAll<FrontlineCore, FrontlineParticipant, WorldPositionCm, AttributeBuffer>();

    private readonly FrontlineRuntime _runtime;
    private readonly OrderQueue _queue;
    private readonly OrderTypeRegistry _orderTypes;
    private readonly int _gatherOrderTypeId;
    private readonly int _moveOrderTypeId;
    private readonly int _crystalAttributeId;

    public FrontlineHarvestSystem(World world, FrontlineRuntime runtime, OrderQueue queue, OrderTypeRegistry orderTypes) : base(world)
    {
        _runtime = runtime;
        _queue = queue;
        _orderTypes = orderTypes;
        _gatherOrderTypeId = orderTypes.GetId(runtime.Config.GatherOrderTypeKey);
        _moveOrderTypeId = orderTypes.GetId(runtime.Config.MoveOrderTypeKey);
        _crystalAttributeId = AttributeRegistry.Register(runtime.Config.CrystalAttribute);
    }

    public override void Update(in float dt)
    {
        if (!_runtime.CanAdvanceGameplay)
        {
            return;
        }

        foreach (ref Chunk chunk in World.Query(in HarvesterQuery))
        {
            Span<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            Span<FrontlineHarvestState> states = chunk.GetSpan<FrontlineHarvestState>();
            Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
            Span<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
            Span<PlayerOwner> owners = chunk.GetSpan<PlayerOwner>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref first, index);
                ref FrontlineHarvestState state = ref states[index];
                ref OrderBuffer buffer = ref buffers[index];

                if (buffer.HasActive && buffer.ActiveOrder.Order.OrderTypeId == _gatherOrderTypeId)
                {
                    Entity target = buffer.ActiveOrder.Order.Target;
                    if (!World.IsAlive(target) || !World.Has<FrontlineCrystalNode>(target) || !World.Has<WorldPositionCm>(target))
                    {
                        OrderSubmitter.NotifyOrderComplete(World, entity, _orderTypes);
                        Reset(ref state);
                        continue;
                    }

                    WorldCmInt2 targetCm = World.Get<WorldPositionCm>(target).ToWorldCmInt2();
                    state.TargetXCm = targetCm.X;
                    state.TargetYCm = targetCm.Y;
                    state.Phase = FrontlineHarvestPhase.TravellingToNode;
                    OrderSubmitter.NotifyOrderComplete(World, entity, _orderTypes);
                    QueueMove(entity, owners[index].PlayerId, targetCm.X, targetCm.Y, ref state);
                    continue;
                }

                if (state.Phase == FrontlineHarvestPhase.Idle)
                {
                    continue;
                }

                if (buffer.HasActive)
                {
                    if (buffer.ActiveOrder.Order.OrderId == state.ExpectedMoveOrderId)
                    {
                        state.ExpectedMoveObserved = 1;
                        continue;
                    }

                    Reset(ref state);
                    continue;
                }

                if (state.ExpectedMoveOrderId > 0 && state.ExpectedMoveObserved == 0)
                {
                    continue;
                }

                if (state.Phase == FrontlineHarvestPhase.TravellingToNode)
                {
                    RequireArrival(in positions[index], state.TargetXCm, state.TargetYCm, "crystal node");
                    state.ExpectedMoveOrderId = 0;
                    state.ExpectedMoveObserved = 0;
                    state.RemainingTicks = _runtime.Config.HarvestLoadTicks;
                    state.Phase = FrontlineHarvestPhase.Loading;
                    continue;
                }

                if (state.Phase == FrontlineHarvestPhase.Loading)
                {
                    state.RemainingTicks--;
                    if (state.RemainingTicks > 0)
                    {
                        continue;
                    }

                    if (!TryFindCore(participants[index].SideIndex, out _, out WorldCmInt2 coreCm, out _))
                    {
                        throw new InvalidOperationException("RTS Frontline harvester could not resolve its command core.");
                    }

                    state.Phase = FrontlineHarvestPhase.ReturningToCore;
                    QueueMove(entity, owners[index].PlayerId, coreCm.X, coreCm.Y, ref state);
                    continue;
                }

                if (state.Phase == FrontlineHarvestPhase.ReturningToCore)
                {
                    if (!TryFindCore(participants[index].SideIndex, out Entity core, out WorldCmInt2 coreCm, out AttributeBuffer coreAttributes))
                    {
                        throw new InvalidOperationException("RTS Frontline harvester returned without a live command core.");
                    }

                    RequireArrival(in positions[index], coreCm.X, coreCm.Y, "command core");
                    coreAttributes.SetCurrent(
                        _crystalAttributeId,
                        coreAttributes.GetCurrent(_crystalAttributeId) + _runtime.Config.HarvestCargoCrystals);
                    World.Set(core, coreAttributes);
                    Reset(ref state);
                }
            }
        }
    }

    private void QueueMove(Entity actor, int playerId, int x, int y, ref FrontlineHarvestState state)
    {
        var order = new Order
        {
            OrderTypeId = _moveOrderTypeId,
            PlayerId = playerId,
            Actor = actor,
            Args = OrderArgs.CreateSingleWorldCm(new Vector3(x, 0f, y)),
            SubmitMode = OrderSubmitMode.Immediate,
        };
        if (!_queue.TryEnqueueAssigned(ref order))
        {
            throw new InvalidOperationException("RTS Frontline OrderQueue is full while routing a harvest move.");
        }

        state.ExpectedMoveOrderId = order.OrderId;
        state.ExpectedMoveObserved = 0;
    }

    private bool TryFindCore(int sideIndex, out Entity core, out WorldCmInt2 position, out AttributeBuffer attributes)
    {
        core = Entity.Null;
        position = default;
        attributes = default;
        foreach (ref Chunk chunk in World.Query(in CoreQuery))
        {
            ReadOnlySpan<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            ReadOnlySpan<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
            ReadOnlySpan<AttributeBuffer> buffers = chunk.GetSpan<AttributeBuffer>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (participants[index].SideIndex != sideIndex)
                {
                    continue;
                }

                core = Unsafe.Add(ref first, index);
                position = positions[index].ToWorldCmInt2();
                attributes = buffers[index];
                return true;
            }
        }

        return false;
    }

    private void RequireArrival(in WorldPositionCm position, int x, int y, string destination)
    {
        WorldCmInt2 current = position.ToWorldCmInt2();
        long dx = current.X - (long)x;
        long dy = current.Y - (long)y;
        long radius = _runtime.Config.ArrivalRadiusCm;
        if ((dx * dx) + (dy * dy) > radius * radius)
        {
            throw new InvalidOperationException($"RTS Frontline move completed outside configured arrival radius for {destination}.");
        }
    }

    private static void Reset(ref FrontlineHarvestState state) => state = default;
}

internal sealed class FrontlineCombatSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<FrontlineInfantry, FrontlineParticipant, FrontlineAttackState, OrderBuffer, WorldPositionCm, PlayerOwner>();

    private readonly FrontlineRuntime _runtime;
    private readonly OrderQueue _queue;
    private readonly OrderTypeRegistry _orderTypes;
    private readonly EffectRequestQueue _effects;
    private readonly int _attackOrderTypeId;
    private readonly int _moveOrderTypeId;
    private readonly int _damageEffectId;

    public FrontlineCombatSystem(
        World world,
        FrontlineRuntime runtime,
        OrderQueue queue,
        OrderTypeRegistry orderTypes,
        EffectRequestQueue effects) : base(world)
    {
        _runtime = runtime;
        _queue = queue;
        _orderTypes = orderTypes;
        _effects = effects;
        _attackOrderTypeId = orderTypes.GetId(runtime.Config.AttackOrderTypeKey);
        _moveOrderTypeId = orderTypes.GetId(runtime.Config.MoveOrderTypeKey);
        _damageEffectId = EffectTemplateIdRegistry.GetId(runtime.Config.DamageEffectId);
        if (_damageEffectId <= 0)
        {
            throw new InvalidOperationException($"RTS Frontline damage effect '{runtime.Config.DamageEffectId}' is not registered.");
        }
    }

    public override void Update(in float dt)
    {
        if (!_runtime.CanAdvanceGameplay)
        {
            return;
        }

        foreach (ref Chunk chunk in World.Query(in Query))
        {
            Span<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            Span<FrontlineAttackState> states = chunk.GetSpan<FrontlineAttackState>();
            Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
            Span<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
            Span<PlayerOwner> owners = chunk.GetSpan<PlayerOwner>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity actor = Unsafe.Add(ref first, index);
                ref FrontlineAttackState state = ref states[index];
                ref OrderBuffer buffer = ref buffers[index];

                if (buffer.HasActive && buffer.ActiveOrder.Order.OrderTypeId == _attackOrderTypeId)
                {
                    Entity target = buffer.ActiveOrder.Order.Target;
                    if (!IsValidHostileTarget(target, participants[index].SideIndex))
                    {
                        OrderSubmitter.NotifyOrderComplete(World, actor, _orderTypes);
                        state = default;
                        continue;
                    }

                    state.Target = target;
                    state.CooldownTicks = 0;
                    OrderSubmitter.NotifyOrderComplete(World, actor, _orderTypes);
                    RouteOrEngage(actor, owners[index].PlayerId, in positions[index], ref state);
                    continue;
                }

                if (state.Phase == FrontlineAttackPhase.Idle)
                {
                    continue;
                }

                if (!IsValidHostileTarget(state.Target, participants[index].SideIndex))
                {
                    state = default;
                    continue;
                }

                if (buffer.HasActive)
                {
                    if (buffer.ActiveOrder.Order.OrderId == state.ExpectedMoveOrderId)
                    {
                        state.ExpectedMoveObserved = 1;
                        continue;
                    }

                    state = default;
                    continue;
                }

                if (state.Phase == FrontlineAttackPhase.Pursuing)
                {
                    if (state.ExpectedMoveOrderId > 0 && state.ExpectedMoveObserved == 0)
                    {
                        continue;
                    }

                    RouteOrEngage(actor, owners[index].PlayerId, in positions[index], ref state);
                    continue;
                }

                if (!IsWithinAttackRange(in positions[index], state.Target))
                {
                    RouteOrEngage(actor, owners[index].PlayerId, in positions[index], ref state);
                    continue;
                }

                if (state.CooldownTicks > 0)
                {
                    state.CooldownTicks--;
                    continue;
                }

                _effects.Publish(new EffectRequest
                {
                    Source = actor,
                    Target = state.Target,
                    TemplateId = _damageEffectId,
                });
                state.CooldownTicks = _runtime.Config.AttackCooldownTicks;
            }
        }
    }

    private void RouteOrEngage(Entity actor, int playerId, in WorldPositionCm actorPosition, ref FrontlineAttackState state)
    {
        if (IsWithinAttackRange(in actorPosition, state.Target))
        {
            state.Phase = FrontlineAttackPhase.Engaging;
            state.ExpectedMoveOrderId = 0;
            state.ExpectedMoveObserved = 0;
            return;
        }

        WorldCmInt2 targetCm = World.Get<WorldPositionCm>(state.Target).ToWorldCmInt2();
        var move = new Order
        {
            OrderTypeId = _moveOrderTypeId,
            PlayerId = playerId,
            Actor = actor,
            Target = state.Target,
            Args = OrderArgs.CreateSingleWorldCm(new Vector3(targetCm.X, 0f, targetCm.Y)),
            SubmitMode = OrderSubmitMode.Immediate,
        };
        if (!_queue.TryEnqueueAssigned(ref move))
        {
            throw new InvalidOperationException("RTS Frontline OrderQueue is full while routing an attack pursuit.");
        }

        state.Phase = FrontlineAttackPhase.Pursuing;
        state.ExpectedMoveOrderId = move.OrderId;
        state.ExpectedMoveObserved = 0;
    }

    private bool IsValidHostileTarget(Entity target, int actorSideIndex)
    {
        return World.IsAlive(target) &&
            World.Has<FrontlineParticipant>(target) &&
            World.Has<WorldPositionCm>(target) &&
            World.Has<AttributeBuffer>(target) &&
            World.Get<FrontlineParticipant>(target).SideIndex != actorSideIndex;
    }

    private bool IsWithinAttackRange(in WorldPositionCm actorPosition, Entity target)
    {
        WorldCmInt2 source = actorPosition.ToWorldCmInt2();
        WorldCmInt2 destination = World.Get<WorldPositionCm>(target).ToWorldCmInt2();
        long dx = source.X - (long)destination.X;
        long dy = source.Y - (long)destination.Y;
        long range = _runtime.Config.AttackRangeCm;
        return (dx * dx) + (dy * dy) <= range * range;
    }
}

internal sealed class FrontlineDeathAndMatchSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription DeathQuery = new QueryDescription()
        .WithAll<FrontlineDeathState, AttributeBuffer>();
    private static readonly QueryDescription CoreQuery = new QueryDescription()
        .WithAll<FrontlineCore, FrontlineParticipant, AttributeBuffer>();

    private readonly FrontlineRuntime _runtime;
    private readonly RuntimeEntityLifecycleQueue _lifecycle;
    private readonly int _healthAttributeId;
    private readonly int _consumeDefeatedUnitEffectId;

    public FrontlineDeathAndMatchSystem(World world, FrontlineRuntime runtime, RuntimeEntityLifecycleQueue lifecycle) : base(world)
    {
        _runtime = runtime;
        _lifecycle = lifecycle;
        _healthAttributeId = AttributeRegistry.Register(runtime.Config.HealthAttribute);
        _consumeDefeatedUnitEffectId = EffectTemplateIdRegistry.GetId(runtime.Config.ConsumeDefeatedUnitEffectId);
        if (_consumeDefeatedUnitEffectId <= EffectTemplateIdRegistry.InvalidId)
        {
            throw new InvalidOperationException($"RTS Frontline consume defeated unit effect '{runtime.Config.ConsumeDefeatedUnitEffectId}' is not registered.");
        }
    }

    public override void Update(in float dt)
    {
        if (!_runtime.IsActive || _runtime.Snapshot.Outcome != FrontlineMatchOutcome.InProgress)
        {
            return;
        }

        if (!_runtime.AdvanceFixedTick())
        {
            return;
        }

        int tick = _runtime.Snapshot.CommittedTick;
        Span<float> coreHealth = stackalloc float[2];
        Span<byte> coreFound = stackalloc byte[2];

        foreach (ref Chunk chunk in World.Query(in CoreQuery))
        {
            ReadOnlySpan<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            ReadOnlySpan<AttributeBuffer> attributes = chunk.GetSpan<AttributeBuffer>();
            foreach (int index in chunk)
            {
                int side = participants[index].SideIndex;
                if ((uint)side >= 2u || coreFound[side] != 0)
                {
                    throw new InvalidOperationException("RTS Frontline requires exactly one command core per configured side.");
                }

                coreFound[side] = 1;
                coreHealth[side] = attributes[index].GetCurrent(_healthAttributeId);
            }
        }

        if (coreFound[0] == 0 || coreFound[1] == 0)
        {
            throw new InvalidOperationException("RTS Frontline match cannot resolve both command cores.");
        }

        bool sideOneDestroyed = coreHealth[0] <= 0f;
        bool sideTwoDestroyed = coreHealth[1] <= 0f;
        if (sideOneDestroyed || sideTwoDestroyed)
        {
            if (sideOneDestroyed && sideTwoDestroyed)
            {
                _runtime.CommitOutcome(FrontlineMatchOutcome.Draw, -1);
            }
            else if (sideOneDestroyed)
            {
                _runtime.CommitOutcome(FrontlineMatchOutcome.SideTwoVictory, 1);
            }
            else
            {
                _runtime.CommitOutcome(FrontlineMatchOutcome.SideOneVictory, 0);
            }
        }
        else if (_runtime.IsDisconnectedPastGrace(0) || _runtime.IsDisconnectedPastGrace(1))
        {
            bool oneDisconnected = _runtime.IsDisconnectedPastGrace(0);
            bool twoDisconnected = _runtime.IsDisconnectedPastGrace(1);
            if (oneDisconnected && twoDisconnected)
            {
                _runtime.CommitOutcome(FrontlineMatchOutcome.Draw, -1);
            }
            else if (oneDisconnected)
            {
                _runtime.CommitOutcome(FrontlineMatchOutcome.SideTwoVictory, 1);
            }
            else
            {
                _runtime.CommitOutcome(FrontlineMatchOutcome.SideOneVictory, 0);
            }
        }
        else if (tick >= _runtime.Config.MatchDurationTicks)
        {
            if (coreHealth[0] == coreHealth[1])
            {
                _runtime.CommitOutcome(FrontlineMatchOutcome.Draw, -1);
            }
            else if (coreHealth[0] > coreHealth[1])
            {
                _runtime.CommitOutcome(FrontlineMatchOutcome.SideOneVictory, 0);
            }
            else
            {
                _runtime.CommitOutcome(FrontlineMatchOutcome.SideTwoVictory, 1);
            }
        }

        foreach (ref Chunk chunk in World.Query(in DeathQuery))
        {
            Span<FrontlineDeathState> deaths = chunk.GetSpan<FrontlineDeathState>();
            ReadOnlySpan<AttributeBuffer> attributes = chunk.GetSpan<AttributeBuffer>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (deaths[index].DestroyQueued != 0 || attributes[index].GetCurrent(_healthAttributeId) > 0f)
                {
                    continue;
                }

                if (!_lifecycle.TryEnqueue(new RuntimeEntityLifecycleRequest
                {
                    Source = Unsafe.Add(ref first, index),
                    Target = Unsafe.Add(ref first, index),
                    TargetContext = Entity.Null,
                    EffectTemplateId = _consumeDefeatedUnitEffectId,
                }))
                {
                    throw new InvalidOperationException("RTS Frontline lifecycle queue is full while removing a defeated unit.");
                }

                deaths[index].DestroyQueued = 1;
            }
        }
    }
}

internal sealed class FrontlinePresentationSystem : ISystem<float>
{
    private static readonly Vector4 PanelFill = new(0.035f, 0.055f, 0.07f, 0.88f);
    private static readonly Vector4 PanelBorder = new(0.23f, 0.58f, 0.62f, 0.95f);
    private static readonly Vector4 Title = new(0.93f, 0.96f, 0.94f, 1f);
    private static readonly Vector4 Text = new(0.75f, 0.85f, 0.82f, 1f);
    private static readonly Vector4 Accent = new(0.96f, 0.77f, 0.32f, 1f);

    private readonly FrontlineRuntime _runtime;
    private readonly ScreenOverlayBuffer? _overlay;
    private FrontlineMatchPhase _cachedPhase = (FrontlineMatchPhase)byte.MaxValue;
    private int _cachedCountdownSeconds = -1;
    private byte _cachedLobbyState = byte.MaxValue;
    private string _roomStatusText = string.Empty;
    private string _sideStatusText = string.Empty;
    private FrontlineMatchOutcome _cachedOutcome = (FrontlineMatchOutcome)byte.MaxValue;
    private string _outcomeText = string.Empty;

    public FrontlinePresentationSystem(GameEngine engine, FrontlineRuntime runtime)
    {
        _runtime = runtime;
        _overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_runtime.IsActive || _overlay == null)
        {
            return;
        }

        FrontlineHudConfig hud = _runtime.Config.Hud;
        FrontlineMatchSnapshot snapshot = _runtime.Snapshot;
        RefreshLobbyText(in snapshot, hud);
        _overlay.AddRect(14, 14, 760, 210, PanelFill, PanelBorder, stableId: 71400, dirtySerial: 1);
        _overlay.AddText(28, 26, hud.Title, 22, Title, stableId: 71401, dirtySerial: 1);
        _overlay.AddText(28, 56, _roomStatusText, 16, Accent, stableId: 71402, dirtySerial: 1);
        if (_sideStatusText.Length > 0)
        {
            _overlay.AddText(28, 82, _sideStatusText, 14, Text, stableId: 71403, dirtySerial: 1);
        }
        _overlay.AddText(28, 108, hud.Objective, 15, Accent, stableId: 71404, dirtySerial: 1);
        _overlay.AddText(28, 132, hud.GatherHint, 14, Text, stableId: 71405, dirtySerial: 1);
        _overlay.AddText(28, 156, hud.TrainHint, 14, Text, stableId: 71406, dirtySerial: 1);
        _overlay.AddText(28, 180, hud.AttackHint, 14, Text, stableId: 71407, dirtySerial: 1);

        FrontlineMatchOutcome outcome = snapshot.Outcome;
        if (outcome != FrontlineMatchOutcome.InProgress)
        {
            if (_cachedOutcome != outcome)
            {
                _cachedOutcome = outcome;
                _outcomeText = outcome switch
                {
                    FrontlineMatchOutcome.SideOneVictory => hud.SideOneVictoryText,
                    FrontlineMatchOutcome.SideTwoVictory => hud.SideTwoVictoryText,
                    _ => hud.DrawText,
                };
            }

            _overlay.AddRect(14, 236, 430, 54, PanelFill, Accent, stableId: 71410, dirtySerial: (int)outcome);
            _overlay.AddText(28, 250, _outcomeText, 22, Accent, stableId: 71411, dirtySerial: (int)outcome);
        }
    }

    private void RefreshLobbyText(in FrontlineMatchSnapshot snapshot, FrontlineHudConfig hud)
    {
        int countdownSeconds = snapshot.Phase == FrontlineMatchPhase.Countdown
            ? Math.Max(1, (snapshot.CountdownRemainingTicks + _runtime.Config.SimulationTickRateHz - 1) / _runtime.Config.SimulationTickRateHz)
            : 0;
        byte lobbyState = (byte)(
            (snapshot.SideOneReady ? 1 : 0) |
            (snapshot.SideTwoReady ? 2 : 0) |
            (snapshot.SideOneConnected ? 4 : 0) |
            (snapshot.SideTwoConnected ? 8 : 0));
        if (_cachedPhase == snapshot.Phase && _cachedCountdownSeconds == countdownSeconds && _cachedLobbyState == lobbyState)
        {
            return;
        }

        _cachedPhase = snapshot.Phase;
        _cachedCountdownSeconds = countdownSeconds;
        _cachedLobbyState = lobbyState;
        _roomStatusText = snapshot.Phase switch
        {
            FrontlineMatchPhase.WaitingForPlayers => hud.WaitingText,
            FrontlineMatchPhase.Countdown => $"{hud.CountdownText} {countdownSeconds}",
            FrontlineMatchPhase.InProgress => hud.BattleStartedText,
            _ => string.Empty,
        };
        _sideStatusText = snapshot.Phase is FrontlineMatchPhase.WaitingForPlayers or FrontlineMatchPhase.Countdown
            ? $"{_runtime.Config.Sides[0].DisplayName}: {ResolveLobbyState(snapshot.SideOneConnected, snapshot.SideOneReady, hud)}    " +
              $"{_runtime.Config.Sides[1].DisplayName}: {ResolveLobbyState(snapshot.SideTwoConnected, snapshot.SideTwoReady, hud)}"
            : string.Empty;
    }

    private static string ResolveLobbyState(bool connected, bool ready, FrontlineHudConfig hud) =>
        !connected ? hud.DisconnectedText : ready ? hud.ReadyText : hud.NotReadyText;
}
