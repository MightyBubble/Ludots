using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Mathematics.FixedPoint;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadMoveRuntimeService
    {
        private readonly World _world;
        private readonly RoadNavPlanStore _plans;

        public RoadMoveRuntimeService(World world, RoadNavPlanStore plans)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _plans = plans ?? throw new ArgumentNullException(nameof(plans));
        }

        public bool TryBindActiveOrder(Entity entity, in Order order, bool preserveTimeoutCount, out RoadMoveOrderRuntime orderRuntime, out RoadNavPlanRuntime planRuntime)
        {
            EnsureOrderRuntime(entity);
            EnsurePlanRuntime(entity);
            ref RoadMoveOrderRuntime orderState = ref _world.Get<RoadMoveOrderRuntime>(entity);
            ref RoadNavPlanRuntime planState = ref _world.Get<RoadNavPlanRuntime>(entity);

            short timeoutCount = preserveTimeoutCount ? orderState.TimeoutCount : (short)0;
            short planGeneration;
            Vector3 finalGoalWorldCm;
            bool bound;
            if (TryResolveBindPosition(entity, out Fix64Vec2 bindPosition))
            {
                bound = _plans.TryBindFromOrder(entity, in order, bindPosition, out planGeneration, out finalGoalWorldCm);
            }
            else
            {
                bound = _plans.TryBindFromOrder(entity, in order, out planGeneration, out finalGoalWorldCm);
            }

            if (!bound)
            {
                orderState = new RoadMoveOrderRuntime
                {
                    ActiveOrderId = order.OrderId,
                    TimeoutCount = timeoutCount,
                    ExecutionGeneration = orderState.ExecutionGeneration,
                    LifecycleState = RoadMoveLifecycleState.Failed,
                    FailureReason = RoadMoveFailureReason.MissingPlan,
                };
                planState = default;
                orderRuntime = orderState;
                planRuntime = planState;
                return false;
            }

            int planPointCount = _plans.TryGetPlan(entity, order.OrderId, out RoadNavPlanView plan) ? plan.Count : OrderWorldSpatialResolver.GetSpatialPointCount(in order.Args.Spatial);

            short executionGeneration = orderState.ExecutionGeneration;
            executionGeneration++;
            if (executionGeneration <= 0)
            {
                executionGeneration = 1;
            }

            orderState = new RoadMoveOrderRuntime
            {
                ActiveOrderId = order.OrderId,
                TimeoutCount = timeoutCount,
                ExecutionGeneration = executionGeneration,
                LifecycleState = RoadMoveLifecycleState.Active,
                FailureReason = RoadMoveFailureReason.None,
            };
            planState = new RoadNavPlanRuntime
            {
                BoundOrderId = order.OrderId,
                PlanGeneration = planGeneration,
                PointCount = planPointCount,
                FinalGoalXcm = (int)MathF.Round(finalGoalWorldCm.X, MidpointRounding.AwayFromZero),
                FinalGoalYcm = (int)MathF.Round(finalGoalWorldCm.Z, MidpointRounding.AwayFromZero),
                CurrentWaypointIndex = 0,
            };
            orderRuntime = orderState;
            planRuntime = planState;
            return true;
        }

        private bool TryResolveBindPosition(Entity entity, out Fix64Vec2 bindPosition)
        {
            bindPosition = default;
            if (_world.Has<Ludots.Core.Physics2D.Components.Position2D>(entity))
            {
                bindPosition = _world.Get<Ludots.Core.Physics2D.Components.Position2D>(entity).Value;
                return true;
            }

            if (OrderWorldSpatialResolver.TryGetEntityWorldCm(_world, entity, out Vector3 worldCm))
            {
                bindPosition = Fix64Vec2.FromFloat(worldCm.X, worldCm.Z);
                return true;
            }

            return false;
        }

        public void Clear(Entity entity)
        {
            _plans.Clear(entity);
            ClearExecutionIntent(entity);
            if (_world.Has<RoadMoveOrderRuntime>(entity))
            {
                _world.Set(entity, default(RoadMoveOrderRuntime));
            }

            if (_world.Has<RoadNavPlanRuntime>(entity))
            {
                _world.Set(entity, default(RoadNavPlanRuntime));
            }
        }

        public ref RoadMoveOrderRuntime EnsureOrderRuntime(Entity entity)
        {
            if (!_world.Has<RoadMoveOrderRuntime>(entity))
            {
                _world.Add(entity, default(RoadMoveOrderRuntime));
            }

            return ref _world.Get<RoadMoveOrderRuntime>(entity);
        }

        public ref RoadNavPlanRuntime EnsurePlanRuntime(Entity entity)
        {
            if (!_world.Has<RoadNavPlanRuntime>(entity))
            {
                _world.Add(entity, default(RoadNavPlanRuntime));
            }

            return ref _world.Get<RoadNavPlanRuntime>(entity);
        }

        public ref RoadMoveExecutionIntent EnsureExecutionIntent(Entity entity)
        {
            if (!_world.Has<RoadMoveExecutionIntent>(entity))
            {
                _world.Add(entity, default(RoadMoveExecutionIntent));
            }

            return ref _world.Get<RoadMoveExecutionIntent>(entity);
        }

        public void ClearExecutionIntent(Entity entity)
        {
            if (_world.Has<RoadMoveExecutionIntent>(entity))
            {
                _world.Set(entity, default(RoadMoveExecutionIntent));
            }
        }
    }
}
