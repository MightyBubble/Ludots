using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Mathematics;

namespace Ludots.Core.MovePlanning;

public sealed class MovePlanRuntimeService
{
    private readonly World _world;
    private readonly IMovePlanStore _plans;

    public MovePlanRuntimeService(World world, IMovePlanStore plans)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _plans = plans ?? throw new ArgumentNullException(nameof(plans));
    }

    public bool TryBindActiveOrder(
        Entity entity,
        in Order order,
        bool preserveTimeoutCount,
        out MovePlanOrderRuntime orderRuntime,
        out MovePlanRuntime planRuntime)
    {
        EnsureOrderRuntime(entity);
        EnsurePlanRuntime(entity);
        ref MovePlanOrderRuntime orderState = ref _world.Get<MovePlanOrderRuntime>(entity);
        ref MovePlanRuntime planState = ref _world.Get<MovePlanRuntime>(entity);

        short timeoutCount = preserveTimeoutCount ? orderState.TimeoutCount : (short)0;
        short planGeneration;
        Vector2 finalGoalWorldCm;
        bool bound = TryResolveBindPosition(entity, out Vector2 bindPositionWorldCm)
            ? _plans.TryBindFromOrder(entity, in order, bindPositionWorldCm, out planGeneration, out finalGoalWorldCm)
            : _plans.TryBindFromOrder(entity, in order, out planGeneration, out finalGoalWorldCm);

        if (!bound)
        {
            orderState = new MovePlanOrderRuntime
            {
                ActiveOrderId = order.OrderId,
                TimeoutCount = timeoutCount,
                ExecutionGeneration = orderState.ExecutionGeneration,
                LifecycleState = MovePlanLifecycleState.Failed,
                FailureReason = MovePlanFailureReason.MissingPlan,
            };
            planState = default;
            orderRuntime = orderState;
            planRuntime = planState;
            return false;
        }

        int planPointCount = _plans.TryGetPlan(entity, order.OrderId, out MovePlanView plan)
            ? plan.Count
            : OrderWorldSpatialResolver.GetSpatialPointCount(_world, in order);

        short executionGeneration = orderState.ExecutionGeneration;
        executionGeneration++;
        if (executionGeneration <= 0)
        {
            executionGeneration = 1;
        }

        orderState = new MovePlanOrderRuntime
        {
            ActiveOrderId = order.OrderId,
            TimeoutCount = timeoutCount,
            ExecutionGeneration = executionGeneration,
            LifecycleState = MovePlanLifecycleState.Active,
            FailureReason = MovePlanFailureReason.None,
        };
        planState = new MovePlanRuntime
        {
            BoundOrderId = order.OrderId,
            PlanGeneration = planGeneration,
            PointCount = planPointCount,
            FinalGoalXcm = (int)MathF.Round(finalGoalWorldCm.X, MidpointRounding.AwayFromZero),
            FinalGoalYcm = (int)MathF.Round(finalGoalWorldCm.Y, MidpointRounding.AwayFromZero),
            CurrentWaypointIndex = 0,
        };
        orderRuntime = orderState;
        planRuntime = planState;
        return true;
    }

    public void Clear(Entity entity)
    {
        _plans.Clear(entity);
        ClearExecutionIntent(entity);
        if (_world.Has<MovePlanOrderRuntime>(entity))
        {
            _world.Set(entity, default(MovePlanOrderRuntime));
        }

        if (_world.Has<MovePlanRuntime>(entity))
        {
            _world.Set(entity, default(MovePlanRuntime));
        }
    }

    public ref MovePlanOrderRuntime EnsureOrderRuntime(Entity entity)
    {
        if (!_world.Has<MovePlanOrderRuntime>(entity))
        {
            _world.Add(entity, default(MovePlanOrderRuntime));
        }

        return ref _world.Get<MovePlanOrderRuntime>(entity);
    }

    public ref MovePlanRuntime EnsurePlanRuntime(Entity entity)
    {
        if (!_world.Has<MovePlanRuntime>(entity))
        {
            _world.Add(entity, default(MovePlanRuntime));
        }

        return ref _world.Get<MovePlanRuntime>(entity);
    }

    public ref MovePlanExecutionIntent EnsureExecutionIntent(Entity entity)
    {
        if (!_world.Has<MovePlanExecutionIntent>(entity))
        {
            _world.Add(entity, default(MovePlanExecutionIntent));
        }

        return ref _world.Get<MovePlanExecutionIntent>(entity);
    }

    public void ClearExecutionIntent(Entity entity)
    {
        if (_world.Has<MovePlanExecutionIntent>(entity))
        {
            _world.Set(entity, default(MovePlanExecutionIntent));
        }
    }

    private bool TryResolveBindPosition(Entity entity, out Vector2 bindPositionWorldCm)
    {
        if (_world.TryGet(entity, out WorldPositionCm worldPosition))
        {
            WorldCmInt2 worldCm = worldPosition.ToWorldCmInt2();
            bindPositionWorldCm = new Vector2(worldCm.X, worldCm.Y);
            return true;
        }

        if (OrderWorldSpatialResolver.TryGetEntityWorldCm(_world, entity, out Vector3 resolvedWorldCm))
        {
            bindPositionWorldCm = new Vector2(resolvedWorldCm.X, resolvedWorldCm.Z);
            return true;
        }

        bindPositionWorldCm = default;
        return false;
    }
}
