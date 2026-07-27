using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Gameplay.ActionLoops;

public sealed class DirectAttackSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription AttackQuery = new QueryDescription()
        .WithAll<DirectAttackProfile, DirectAttackState, OrderBuffer, WorldPositionCm, PlayerOwner, Team>();

    private readonly OrderQueue _orders;
    private readonly OrderTypeRegistry _orderTypes;
    private readonly EffectRequestQueue _effects;
    private readonly IGameplayActionLoopGate _gate;

    public DirectAttackSystem(
        World world,
        OrderQueue orders,
        OrderTypeRegistry orderTypes,
        EffectRequestQueue effects,
        IGameplayActionLoopGate gate) : base(world)
    {
        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        _orderTypes = orderTypes ?? throw new ArgumentNullException(nameof(orderTypes));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public override void Update(in float dt)
    {
        if (!_gate.CanAdvanceGameplay)
        {
            return;
        }

        foreach (ref Chunk chunk in World.Query(in AttackQuery))
        {
            ReadOnlySpan<DirectAttackProfile> profiles = chunk.GetSpan<DirectAttackProfile>();
            Span<DirectAttackState> states = chunk.GetSpan<DirectAttackState>();
            Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
            ReadOnlySpan<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
            ReadOnlySpan<PlayerOwner> owners = chunk.GetSpan<PlayerOwner>();
            ReadOnlySpan<Team> teams = chunk.GetSpan<Team>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity actor = Unsafe.Add(ref first, index);
                ref DirectAttackState state = ref states[index];
                ref OrderBuffer buffer = ref buffers[index];
                ref readonly DirectAttackProfile profile = ref profiles[index];

                if (buffer.HasActive && buffer.ActiveOrder.Order.OrderTypeId == profile.AttackOrderTypeId)
                {
                    ValidateProfile(in profile);
                    Order attackOrder = buffer.ActiveOrder.Order;
                    Entity target = attackOrder.Target;
                    if (!IsValidTarget(target, teams[index].Id, profile.TargetRelation))
                    {
                        OrderSubmitter.NotifyOrderComplete(World, actor, _orderTypes);
                        state = default;
                        continue;
                    }

                    state.Target = target;
                    state.CooldownTicks = 0;
                    CaptureExplicitEngagementPoint(in attackOrder, ref state);
                    OrderSubmitter.NotifyOrderComplete(World, actor, _orderTypes);
                    RouteOrEngage(
                        actor,
                        owners[index].PlayerId,
                        in positions[index],
                        in profile,
                        ref state);
                    continue;
                }

                if (state.Phase == DirectAttackPhase.Idle)
                {
                    continue;
                }

                ValidateProfile(in profile);
                if (!IsValidTarget(state.Target, teams[index].Id, profile.TargetRelation))
                {
                    state = default;
                    continue;
                }

                if (buffer.HasActive)
                {
                    if (buffer.ActiveOrder.Order.OrderId == state.ExpectedMoveOrderId)
                    {
                        state.ExpectedMoveObserved = 1;
                        if (IsWithinRange(in positions[index], state.Target, profile.RangeCm))
                        {
                            if (!OrderSubmitter.NotifyOrderComplete(World, actor, _orderTypes))
                            {
                                throw new InvalidOperationException(
                                    "Direct attack could not complete its active pursuit order after entering range.");
                            }

                            BeginEngaging(ref state);
                        }
                        continue;
                    }

                    state = default;
                    continue;
                }

                if (state.Phase == DirectAttackPhase.Pursuing)
                {
                    if (state.ExpectedMoveOrderId > 0 && state.ExpectedMoveObserved == 0)
                    {
                        continue;
                    }

                    RouteOrEngage(
                        actor,
                        owners[index].PlayerId,
                        in positions[index],
                        in profile,
                        ref state);
                    continue;
                }

                if (state.Phase != DirectAttackPhase.Engaging)
                {
                    throw new InvalidOperationException($"Unsupported direct attack phase '{state.Phase}'.");
                }

                if (!IsWithinRange(in positions[index], state.Target, profile.RangeCm))
                {
                    RouteOrEngage(
                        actor,
                        owners[index].PlayerId,
                        in positions[index],
                        in profile,
                        ref state);
                    continue;
                }

                if (state.CooldownTicks > 0)
                {
                    state.CooldownTicks--;
                    continue;
                }

                int droppedBefore = _effects.DroppedCount;
                _effects.Publish(new EffectRequest
                {
                    Source = actor,
                    Target = state.Target,
                    TemplateId = profile.EffectTemplateId,
                });
                if (_effects.DroppedCount != droppedBefore)
                {
                    throw new InvalidOperationException("EffectRequestQueue cannot accept a direct attack effect.");
                }

                state.CooldownTicks = profile.CooldownTicks;
            }
        }
    }

    private void RouteOrEngage(
        Entity actor,
        int playerId,
        in WorldPositionCm actorPosition,
        in DirectAttackProfile profile,
        ref DirectAttackState state)
    {
        if (CanBeginEngaging(in actorPosition, state.Target, profile.RangeCm))
        {
            BeginEngaging(ref state);
            return;
        }

        WorldCmInt2 targetPosition = ResolvePursuitTarget(in state);
        var move = new Order
        {
            OrderTypeId = profile.MoveOrderTypeId,
            PlayerId = playerId,
            Actor = actor,
            Target = state.Target,
            Args = OrderArgs.CreateSingleWorldCm(new Vector3(targetPosition.X, 0f, targetPosition.Y)),
            SubmitMode = OrderSubmitMode.Immediate,
        };
        if (!_orders.TryEnqueueAssigned(ref move))
        {
            throw new InvalidOperationException("OrderQueue is full while routing a direct attack pursuit.");
        }

        state.Phase = DirectAttackPhase.Pursuing;
        state.ExpectedMoveOrderId = move.OrderId;
        state.ExpectedMoveObserved = 0;
    }

    private bool CanBeginEngaging(in WorldPositionCm actorPosition, Entity target, int rangeCm) =>
        IsWithinRange(in actorPosition, target, rangeCm);

    private WorldCmInt2 ResolvePursuitTarget(in DirectAttackState state)
    {
        if (state.HasExplicitEngagementPoint != 0)
        {
            return new WorldCmInt2(state.EngagementPointXCm, state.EngagementPointYCm);
        }

        return World.Get<WorldPositionCm>(state.Target).ToWorldCmInt2();
    }

    private static void BeginEngaging(ref DirectAttackState state)
    {
        state.Phase = DirectAttackPhase.Engaging;
        state.ExpectedMoveOrderId = 0;
        state.ExpectedMoveObserved = 0;
        state.EngagementPointXCm = 0;
        state.EngagementPointYCm = 0;
        state.HasExplicitEngagementPoint = 0;
    }

    private static void CaptureExplicitEngagementPoint(in Order attackOrder, ref DirectAttackState state)
    {
        state.ExpectedMoveObserved = 0;
        state.EngagementPointXCm = 0;
        state.EngagementPointYCm = 0;
        state.HasExplicitEngagementPoint = 0;

        if (attackOrder.Args.Spatial.Kind == OrderSpatialKind.None &&
            attackOrder.Args.Spatial.Mode == OrderCollectionMode.None)
        {
            return;
        }

        if (attackOrder.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
            attackOrder.Args.Spatial.Mode != OrderCollectionMode.Single)
        {
            throw new InvalidOperationException(
                "Direct attack explicit engagement point must be a single world-centimeter target.");
        }

        Vector3 worldCm = attackOrder.Args.Spatial.WorldCm;
        if (!TryRoundWorldCm(worldCm.X, out int x) ||
            !TryRoundWorldCm(worldCm.Z, out int y))
        {
            throw new InvalidOperationException(
                "Direct attack explicit engagement point exceeds the supported world-centimeter range.");
        }

        state.EngagementPointXCm = x;
        state.EngagementPointYCm = y;
        state.HasExplicitEngagementPoint = 1;
    }

    private static bool TryRoundWorldCm(float value, out int rounded)
    {
        rounded = 0;
        if (!float.IsFinite(value))
        {
            return false;
        }

        double roundedValue = Math.Round(value, MidpointRounding.AwayFromZero);
        if (roundedValue < int.MinValue || roundedValue > int.MaxValue)
        {
            return false;
        }

        rounded = (int)roundedValue;
        return true;
    }

    private bool IsValidTarget(Entity target, int sourceTeamId, RelationshipFilter filter)
    {
        return World.IsAlive(target) &&
            World.Has<Team>(target) &&
            World.Has<WorldPositionCm>(target) &&
            World.Has<AttributeBuffer>(target) &&
            RelationshipFilterUtil.Passes(filter, sourceTeamId, World.Get<Team>(target).Id);
    }

    private bool IsWithinRange(in WorldPositionCm actorPosition, Entity target, int rangeCm)
    {
        WorldCmInt2 source = actorPosition.ToWorldCmInt2();
        WorldCmInt2 destination = World.Get<WorldPositionCm>(target).ToWorldCmInt2();
        long dx = source.X - (long)destination.X;
        long dy = source.Y - (long)destination.Y;
        long range = rangeCm;
        return (dx * dx) + (dy * dy) <= range * range;
    }

    private void ValidateProfile(in DirectAttackProfile profile)
    {
        if (!_orderTypes.IsRegistered(profile.AttackOrderTypeId) ||
            !_orderTypes.IsRegistered(profile.MoveOrderTypeId))
        {
            throw new InvalidOperationException("Direct attack profile references an unregistered order type.");
        }
        if (profile.EffectTemplateId <= 0 ||
            (uint)profile.TargetRelation > (uint)RelationshipFilter.NotHostile ||
            profile.RangeCm <= 0 ||
            profile.CooldownTicks <= 0)
        {
            throw new InvalidOperationException("Direct attack profile contains invalid authored values.");
        }
    }
}
