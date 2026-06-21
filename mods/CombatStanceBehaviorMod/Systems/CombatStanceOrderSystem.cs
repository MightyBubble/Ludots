using System;
using System.Numerics;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using CombatStanceBehaviorMod.Components;

namespace CombatStanceBehaviorMod.Systems;

public sealed class CombatStanceOrderSystem : BaseSystem<World, float>
{
    private const int DefaultTargetCapacity = 256;
    private const int PendingEntityCapacity = 1024;
    private const int ArrivalRadiusCm = 50;
    private const int DefaultRetaliationTtlSteps = 180;
    private const string DamageTakenEventTag = "Event.DamageTaken";

    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<OrderBuffer, WorldPositionCm>();

    private readonly IClock _clock;
    private readonly OrderQueue _orders;
    private readonly OrderTypeRegistry _orderTypes;
    private readonly ISpatialQueryService _spatial;
    private readonly GameplayEventBus? _events;
    private readonly Entity[] _targets;
    private readonly CommandBuffer _commandBuffer = new();
    private readonly Entity[] _completedOrders = new Entity[PendingEntityCapacity];
    private readonly Entity[] _removeAttackMoveRuntime = new Entity[PendingEntityCapacity];
    private readonly Entity[] _removeGuardRuntime = new Entity[PendingEntityCapacity];
    private int _completedOrderCount;
    private int _removeAttackMoveRuntimeCount;
    private int _removeGuardRuntimeCount;

    private readonly int _attackMoveOrderTypeId;
    private readonly int _assaultMoveOrderTypeId;
    private readonly int _guardOrderTypeId;
    private readonly int _setCombatStanceOrderTypeId;
    private readonly int _scatterOrderTypeId;
    private readonly int _moveToOrderTypeId;
    private readonly int _attackTargetOrderTypeId;
    private readonly int _damageTakenEventTagId;

    public CombatStanceOrderSystem(
        World world,
        IClock clock,
        OrderQueue orders,
        OrderTypeRegistry orderTypes,
        ISpatialQueryService spatial,
        GameplayEventBus? events = null,
        int targetCapacity = DefaultTargetCapacity)
        : base(world)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        _orderTypes = orderTypes ?? throw new ArgumentNullException(nameof(orderTypes));
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        _events = events;
        _targets = new Entity[targetCapacity < 16 ? 16 : targetCapacity];

        _attackMoveOrderTypeId = RequireOrderTypeId(orderTypes, StanceOrderKeys.AttackMove);
        _assaultMoveOrderTypeId = RequireOrderTypeId(orderTypes, StanceOrderKeys.AssaultMove);
        _guardOrderTypeId = RequireOrderTypeId(orderTypes, StanceOrderKeys.Guard);
        _setCombatStanceOrderTypeId = RequireOrderTypeId(orderTypes, StanceOrderKeys.SetCombatStance);
        _scatterOrderTypeId = RequireOrderTypeId(orderTypes, StanceOrderKeys.Scatter);
        _moveToOrderTypeId = RequireOrderTypeId(orderTypes, StanceOrderKeys.MoveTo);
        _attackTargetOrderTypeId = RequireOrderTypeId(orderTypes, StanceOrderKeys.AttackTarget);
        _damageTakenEventTagId = RequireTagId(DamageTakenEventTag);
    }

    public override void Update(in float dt)
    {
        _completedOrderCount = 0;
        _removeAttackMoveRuntimeCount = 0;
        _removeGuardRuntimeCount = 0;

        int step = _clock.Now(ClockDomainId.Step);
        CaptureRetaliationEvents(step);
        foreach (ref var chunk in World.Query(in Query))
        {
            ref var entityFirst = ref chunk.Entity(0);
            var buffers = chunk.GetSpan<OrderBuffer>();
            var positions = chunk.GetSpan<WorldPositionCm>();

            foreach (var index in chunk)
            {
                Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                if (!World.IsAlive(entity))
                {
                    continue;
                }

                ref OrderBuffer buffer = ref buffers[index];
                WorldCmInt2 actorPosition = positions[index].Value.ToWorldCmInt2();
                if (buffer.HasActive)
                {
                    TickActiveOrder(entity, ref buffer, actorPosition, step);
                }
                else if (!buffer.HasQueued && !buffer.HasPending)
                {
                    TickPersistentBehavior(entity, actorPosition, step);
                }
            }
        }

        if (_commandBuffer.Size > 0)
        {
            _commandBuffer.Playback(World);
        }

        for (int i = 0; i < _completedOrderCount; i++)
        {
            OrderSubmitter.NotifyOrderComplete(World, _completedOrders[i], _orderTypes);
        }

        for (int i = 0; i < _removeAttackMoveRuntimeCount; i++)
        {
            Entity entity = _removeAttackMoveRuntime[i];
            if (World.IsAlive(entity) && World.Has<AttackMoveRuntime>(entity))
            {
                _commandBuffer.Remove<AttackMoveRuntime>(in entity);
            }
        }

        for (int i = 0; i < _removeGuardRuntimeCount; i++)
        {
            Entity entity = _removeGuardRuntime[i];
            if (World.IsAlive(entity) && World.Has<GuardRuntime>(entity))
            {
                _commandBuffer.Remove<GuardRuntime>(in entity);
            }
        }

        if (_commandBuffer.Size > 0)
        {
            _commandBuffer.Playback(World);
        }
    }

    private void CaptureRetaliationEvents(int step)
    {
        if (_events == null)
        {
            return;
        }

        var events = _events.Events;
        for (int i = 0; i < events.Count; i++)
        {
            GameplayEvent evt = events[i];
            if (evt.TagId != _damageTakenEventTagId ||
                evt.Source == default ||
                evt.Target == default ||
                !World.IsAlive(evt.Source) ||
                !World.IsAlive(evt.Target) ||
                !IsHostile(evt.Target, evt.Source))
            {
                continue;
            }

            Upsert(evt.Target, new RetaliationMemory
            {
                LastAttacker = evt.Source,
                LastAttackerStep = step
            });
        }
    }

    private void TickActiveOrder(Entity entity, ref OrderBuffer buffer, in WorldCmInt2 actorPosition, int step)
    {
        int orderTypeId = buffer.ActiveOrder.Order.OrderTypeId;
        if (orderTypeId == _setCombatStanceOrderTypeId)
        {
            HandleSetCombatStance(entity, in buffer.ActiveOrder.Order);
            CompleteActive(entity);
            return;
        }

        if (orderTypeId == _scatterOrderTypeId)
        {
            HandleScatter(entity, in actorPosition, in buffer.ActiveOrder.Order, step);
            CompleteActive(entity);
            return;
        }

        if (orderTypeId == _attackMoveOrderTypeId || orderTypeId == _assaultMoveOrderTypeId)
        {
            HandleAttackMoveOrder(entity, in actorPosition, in buffer.ActiveOrder.Order, orderTypeId == _assaultMoveOrderTypeId, step);
            CompleteActive(entity);
            return;
        }

        if (orderTypeId == _guardOrderTypeId)
        {
            HandleGuardOrder(entity, in actorPosition, in buffer.ActiveOrder.Order, step);
            CompleteActive(entity);
            return;
        }

        if (World.Has<AttackMoveRuntime>(entity) && orderTypeId == _moveToOrderTypeId)
        {
            TickAttackMoveRuntime(entity, in actorPosition, step);
        }

        if (World.Has<GuardRuntime>(entity) && orderTypeId == _moveToOrderTypeId)
        {
            TickGuardRuntime(entity, in actorPosition, step);
        }
    }

    private void TickPersistentBehavior(Entity entity, in WorldCmInt2 actorPosition, int step)
    {
        if (World.Has<AttackMoveRuntime>(entity))
        {
            TickAttackMoveRuntime(entity, in actorPosition, step);
            return;
        }

        if (World.Has<GuardRuntime>(entity))
        {
            TickGuardRuntime(entity, in actorPosition, step);
            return;
        }

        TickIdleStance(entity, in actorPosition, step);
    }

    private void HandleSetCombatStance(Entity entity, in Order order)
    {
        int stance = order.Args.I0;
        if (!CombatStances.IsDefined(stance))
        {
            throw new InvalidOperationException($"Combat stance setCombatStance received unknown stance {stance}.");
        }

        int leashRadiusCm = order.Args.I1;
        int retaliationTtlSteps = order.Args.I2 > 0 ? order.Args.I2 : DefaultRetaliationTtlSteps;
        if (stance != CombatStances.HoldFire && leashRadiusCm <= 0)
        {
            throw new InvalidOperationException("Combat stance setCombatStance requires Args.I1 leash radius for non-HoldFire stances.");
        }

        Upsert(entity, new CombatStanceState
        {
            Stance = stance,
            LeashRadiusCm = leashRadiusCm,
            RetaliationTtlSteps = retaliationTtlSteps
        });
    }

    private void HandleScatter(Entity entity, in WorldCmInt2 actorPosition, in Order order, int step)
    {
        int radiusCm = order.Args.I0;
        if (radiusCm <= 0)
        {
            throw new InvalidOperationException("Combat stance scatter requires Args.I0 positive scatter radius.");
        }

        DeterministicScatterOffset(entity, step, radiusCm, out int dx, out int dy);
        EnqueueMove(entity, actorPosition.X + dx, actorPosition.Y + dy, step);
    }

    private void HandleAttackMoveOrder(Entity entity, in WorldCmInt2 actorPosition, in Order order, bool assault, int step)
    {
        WorldCmInt2 destination = RequireSingleWorldPoint(in order, "attackMove");
        int leashRadiusCm = order.Args.I0;
        if (leashRadiusCm <= 0)
        {
            throw new InvalidOperationException("Combat stance attackMove/assaultMove requires Args.I0 positive leash radius.");
        }

        var runtime = new AttackMoveRuntime
        {
            DestinationX = destination.X,
            DestinationY = destination.Y,
            LeashRadiusCm = leashRadiusCm,
            Assault = assault ? (byte)1 : (byte)0
        };
        Upsert(entity, runtime);

        int stance = assault ? CombatStances.AttackAnything : ResolveConfiguredStance(entity);
        if (TryAcquireStanceTarget(entity, actorPosition, actorPosition, leashRadiusCm, step, stance, out Entity target))
        {
            EnqueueAttack(entity, target, step);
            return;
        }

        if (!IsWithinRadius(actorPosition, destination, ArrivalRadiusCm))
        {
            EnqueueMove(entity, destination.X, destination.Y, step);
        }
        else
        {
            QueuePendingEntity(_removeAttackMoveRuntime, ref _removeAttackMoveRuntimeCount, entity, "Combat stance attack-move removal queue");
        }
    }

    private void HandleGuardOrder(Entity entity, in WorldCmInt2 actorPosition, in Order order, int step)
    {
        if (order.Target == default || !World.IsAlive(order.Target))
        {
            throw new InvalidOperationException("Combat stance guard requires a live target entity.");
        }

        int radiusCm = order.Args.I0;
        int leashRadiusCm = order.Args.I1;
        if (radiusCm <= 0 || leashRadiusCm <= 0)
        {
            throw new InvalidOperationException("Combat stance guard requires Args.I0 radius and Args.I1 leash radius.");
        }

        var runtime = new GuardRuntime
        {
            Guarded = order.Target,
            RadiusCm = radiusCm,
            LeashRadiusCm = leashRadiusCm
        };

        if (!World.Has<WorldPositionCm>(runtime.Guarded))
        {
            Upsert(entity, runtime);
            return;
        }

        WorldCmInt2 guardedPosition = World.Get<WorldPositionCm>(runtime.Guarded).Value.ToWorldCmInt2();
        int stance = ResolveConfiguredStance(entity);
        if (TryAcquireStanceTarget(entity, actorPosition, guardedPosition, runtime.LeashRadiusCm, step, stance, out Entity target))
        {
            runtime.EngagedTarget = target;
            Upsert(entity, runtime);
            EnqueueAttack(entity, target, step);
            return;
        }

        Upsert(entity, runtime);
        if (!IsWithinRadius(actorPosition, guardedPosition, runtime.RadiusCm))
        {
            EnqueueMove(entity, guardedPosition.X, guardedPosition.Y, step);
        }
    }

    private void TickAttackMoveRuntime(Entity entity, in WorldCmInt2 actorPosition, int step)
    {
        var runtime = World.Get<AttackMoveRuntime>(entity);
        var destination = new WorldCmInt2(runtime.DestinationX, runtime.DestinationY);
        int stance = runtime.Assault != 0 ? CombatStances.AttackAnything : ResolveConfiguredStance(entity);
        if (TryAcquireStanceTarget(entity, actorPosition, actorPosition, runtime.LeashRadiusCm, step, stance, out Entity target))
        {
            runtime.EngagedTarget = target;
            World.Set(entity, runtime);
            EnqueueAttack(entity, target, step);
            return;
        }

        if (IsWithinRadius(actorPosition, destination, ArrivalRadiusCm))
        {
            QueuePendingEntity(_removeAttackMoveRuntime, ref _removeAttackMoveRuntimeCount, entity, "Combat stance attack-move removal queue");
            return;
        }

        EnqueueMove(entity, destination.X, destination.Y, step);
    }

    private void TickGuardRuntime(Entity entity, in WorldCmInt2 actorPosition, int step)
    {
        var runtime = World.Get<GuardRuntime>(entity);
        if (!World.IsAlive(runtime.Guarded) || !World.Has<WorldPositionCm>(runtime.Guarded))
        {
            QueuePendingEntity(_removeGuardRuntime, ref _removeGuardRuntimeCount, entity, "Combat stance guard removal queue");
            return;
        }

        WorldCmInt2 guardedPosition = World.Get<WorldPositionCm>(runtime.Guarded).Value.ToWorldCmInt2();
        int stance = ResolveConfiguredStance(entity);
        if (TryAcquireStanceTarget(entity, actorPosition, guardedPosition, runtime.LeashRadiusCm, step, stance, out Entity target))
        {
            runtime.EngagedTarget = target;
            World.Set(entity, runtime);
            EnqueueAttack(entity, target, step);
            return;
        }

        if (!IsWithinRadius(actorPosition, guardedPosition, runtime.RadiusCm))
        {
            EnqueueMove(entity, guardedPosition.X, guardedPosition.Y, step);
        }
    }

    private void TickIdleStance(Entity entity, in WorldCmInt2 actorPosition, int step)
    {
        if (!World.Has<CombatStanceState>(entity))
        {
            return;
        }

        var state = World.Get<CombatStanceState>(entity);
        if (state.Stance == CombatStances.HoldFire)
        {
            return;
        }

        if (state.LeashRadiusCm <= 0)
        {
            throw new InvalidOperationException("Combat stance requires positive LeashRadiusCm.");
        }

        if (TryAcquireStanceTarget(entity, actorPosition, actorPosition, state.LeashRadiusCm, step, state.Stance, out Entity target))
        {
            EnqueueAttack(entity, target, step);
        }
    }

    private bool TryAcquireStanceTarget(
        Entity actor,
        in WorldCmInt2 actorPosition,
        in WorldCmInt2 center,
        int radiusCm,
        int step,
        int stance,
        out Entity target)
    {
        target = default;
        if (stance == CombatStances.HoldFire)
        {
            return false;
        }

        if (stance == CombatStances.ReturnFire)
        {
            return TryAcquireRetaliationTarget(actor, in actorPosition, radiusCm, step, out target);
        }

        if (stance != CombatStances.Defend && stance != CombatStances.AttackAnything)
        {
            throw new InvalidOperationException($"Combat stance {stance} is not defined.");
        }

        return TryAcquireHostileTarget(actor, in center, radiusCm, out target);
    }

    private bool TryAcquireRetaliationTarget(Entity actor, in WorldCmInt2 actorPosition, int radiusCm, int step, out Entity target)
    {
        target = default;
        if (!World.Has<RetaliationMemory>(actor))
        {
            return false;
        }

        var memory = World.Get<RetaliationMemory>(actor);
        int ttl = World.Has<CombatStanceState>(actor)
            ? World.Get<CombatStanceState>(actor).RetaliationTtlSteps
            : DefaultRetaliationTtlSteps;
        if (memory.LastAttacker == default ||
            !World.IsAlive(memory.LastAttacker) ||
            step - memory.LastAttackerStep > ttl ||
            !IsHostile(actor, memory.LastAttacker) ||
            !World.Has<WorldPositionCm>(memory.LastAttacker))
        {
            return false;
        }

        WorldCmInt2 attackerPosition = World.Get<WorldPositionCm>(memory.LastAttacker).Value.ToWorldCmInt2();
        if (!IsWithinRadius(actorPosition, attackerPosition, radiusCm))
        {
            return false;
        }

        target = memory.LastAttacker;
        return true;
    }

    private bool TryAcquireHostileTarget(Entity actor, in WorldCmInt2 center, int radiusCm, out Entity target)
    {
        target = default;
        int count = _spatial.QueryRadius(center, radiusCm, _targets).Count;
        int bestPriority = int.MinValue;
        long bestDistanceSq = long.MaxValue;
        for (int i = 0; i < count && i < _targets.Length; i++)
        {
            Entity candidate = _targets[i];
            if (candidate == default ||
                candidate.Equals(actor) ||
                !World.IsAlive(candidate) ||
                !World.Has<WorldPositionCm>(candidate) ||
                !IsHostile(actor, candidate))
            {
                continue;
            }

            int priority = World.Has<UtilityAiTargetPriority>(candidate)
                ? World.Get<UtilityAiTargetPriority>(candidate).Bucket
                : 0;
            WorldCmInt2 candidatePosition = World.Get<WorldPositionCm>(candidate).Value.ToWorldCmInt2();
            long distanceSq = DistanceSquared(center, candidatePosition);
            if (priority > bestPriority || (priority == bestPriority && distanceSq < bestDistanceSq))
            {
                target = candidate;
                bestPriority = priority;
                bestDistanceSq = distanceSq;
            }
        }

        return target != default;
    }

    private bool IsHostile(Entity actor, Entity target)
    {
        if (!World.Has<Team>(actor) || !World.Has<Team>(target))
        {
            return false;
        }

        int actorTeam = World.Get<Team>(actor).Id;
        int targetTeam = World.Get<Team>(target).Id;
        return TeamManager.GetRelationship(actorTeam, targetTeam) == TeamRelationship.Hostile;
    }

    private int ResolveConfiguredStance(Entity entity)
    {
        if (!World.Has<CombatStanceState>(entity))
        {
            throw new InvalidOperationException("Combat stance behavior requires an explicit CombatStanceState.");
        }

        int stance = World.Get<CombatStanceState>(entity).Stance;
        if (!CombatStances.IsDefined(stance))
        {
            throw new InvalidOperationException($"Combat stance entity has unknown stance {stance}.");
        }

        return stance;
    }

    private void EnqueueAttack(Entity actor, Entity target, int step)
    {
        var order = new Order
        {
            Actor = actor,
            Target = target,
            OrderTypeId = _attackTargetOrderTypeId,
            SubmitMode = OrderSubmitMode.Immediate,
            SubmitStep = step
        };

        if (World.Has<WorldPositionCm>(target))
        {
            WorldCmInt2 pos = World.Get<WorldPositionCm>(target).Value.ToWorldCmInt2();
            order.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
            order.Args.Spatial.Mode = OrderCollectionMode.Single;
            order.Args.Spatial.WorldCm = new Vector3(pos.X, 0f, pos.Y);
        }

        if (!_orders.TryEnqueue(in order))
        {
            throw new InvalidOperationException("Combat stance behavior order queue is full while submitting attackTarget.");
        }
    }

    private void EnqueueMove(Entity actor, int x, int y, int step)
    {
        var order = new Order
        {
            Actor = actor,
            OrderTypeId = _moveToOrderTypeId,
            SubmitMode = OrderSubmitMode.Immediate,
            SubmitStep = step
        };
        order.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
        order.Args.Spatial.Mode = OrderCollectionMode.Single;
        order.Args.Spatial.WorldCm = new Vector3(x, 0f, y);

        if (!_orders.TryEnqueue(in order))
        {
            throw new InvalidOperationException("Combat stance behavior order queue is full while submitting moveTo.");
        }
    }

    private void CompleteActive(Entity entity)
    {
        QueuePendingEntity(_completedOrders, ref _completedOrderCount, entity, "Combat stance completed-order queue");
    }

    private void Upsert<T>(Entity entity, in T component)
        where T : struct
    {
        if (World.Has<T>(entity))
        {
            World.Set(entity, component);
        }
        else
        {
            _commandBuffer.Add(entity, component);
        }
    }

    private static WorldCmInt2 RequireSingleWorldPoint(in Order order, string orderName)
    {
        if (order.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
            order.Args.Spatial.Mode != OrderCollectionMode.Single)
        {
            throw new InvalidOperationException($"Combat stance {orderName} requires a single WorldCm spatial target.");
        }

        return new WorldCmInt2((int)order.Args.Spatial.WorldCm.X, (int)order.Args.Spatial.WorldCm.Z);
    }

    private static void DeterministicScatterOffset(Entity entity, int step, int radiusCm, out int dx, out int dy)
    {
        int selector = (entity.Id * 1103515245 + step * 12345) & 7;
        switch (selector)
        {
            case 0: dx = radiusCm; dy = 0; break;
            case 1: dx = radiusCm; dy = radiusCm; break;
            case 2: dx = 0; dy = radiusCm; break;
            case 3: dx = -radiusCm; dy = radiusCm; break;
            case 4: dx = -radiusCm; dy = 0; break;
            case 5: dx = -radiusCm; dy = -radiusCm; break;
            case 6: dx = 0; dy = -radiusCm; break;
            default: dx = radiusCm; dy = -radiusCm; break;
        }
    }

    private static bool IsWithinRadius(in WorldCmInt2 a, in WorldCmInt2 b, int radiusCm)
    {
        return DistanceSquared(a, b) <= (long)radiusCm * radiusCm;
    }

    private static long DistanceSquared(in WorldCmInt2 a, in WorldCmInt2 b)
    {
        long dx = b.X - a.X;
        long dy = b.Y - a.Y;
        return dx * dx + dy * dy;
    }

    private static int RequireOrderTypeId(OrderTypeRegistry orderTypes, string key)
    {
        if (!orderTypes.TryGetId(key, out int id) || id <= 0 || !orderTypes.IsRegistered(id))
        {
            throw new InvalidOperationException($"CombatStanceBehaviorMod requires GAS/order_types.json to define '{key}'.");
        }

        return id;
    }

    private static int RequireTagId(string key)
    {
        int id = TagRegistry.GetId(key);
        if (id <= 0)
        {
            throw new InvalidOperationException($"CombatStanceBehaviorMod requires GAS/tag_rules.json or effect config to define '{key}'.");
        }

        return id;
    }

    private static void QueuePendingEntity(Entity[] buffer, ref int count, Entity entity, string queueName)
    {
        if ((uint)count >= (uint)buffer.Length)
        {
            throw new InvalidOperationException($"{queueName} exceeded fixed capacity {buffer.Length}.");
        }

        buffer[count++] = entity;
    }

    public override void Dispose()
    {
        _commandBuffer.Dispose();
        base.Dispose();
    }
}
