using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class MoveToWorldCmOrderSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<WorldPositionCm, OrderBuffer>();

        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly int _moveToOrderTypeId;
        private readonly float _defaultSpeedCmPerSec;
        private readonly float _stopRadiusCm;
        private readonly int _moveSpeedAttributeId;

        public MoveToWorldCmOrderSystem(
            World world,
            OrderTypeRegistry orderTypeRegistry,
            int moveToOrderTypeId,
            float defaultSpeedCmPerSec = 600f,
            float stopRadiusCm = 40f) : base(world)
        {
            _orderTypeRegistry = orderTypeRegistry ?? throw new ArgumentNullException(nameof(orderTypeRegistry));
            _moveToOrderTypeId = moveToOrderTypeId;
            _defaultSpeedCmPerSec = Math.Max(0f, defaultSpeedCmPerSec);
            _stopRadiusCm = Math.Max(0f, stopRadiusCm);
            _moveSpeedAttributeId = AttributeRegistry.Register("MoveSpeed");
        }

        public override void Update(in float dt)
        {
            if (_moveToOrderTypeId <= 0 || dt <= 0f)
            {
                return;
            }

            foreach (ref var chunk in World.Query(in Query))
            {
                var positions = chunk.GetSpan<WorldPositionCm>();
                var buffers = chunk.GetSpan<OrderBuffer>();
                ref var entityFirst = ref chunk.Entity(0);

                foreach (var index in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, index);
                    if (!World.IsAlive(entity))
                    {
                        continue;
                    }

                    ref var buffer = ref buffers[index];
                    if (!buffer.HasActive || buffer.ActiveOrder.Order.OrderTypeId != _moveToOrderTypeId)
                    {
                        SetSmartStopSuppression(entity, suppressed: false);
                        ClearNavGoal(entity);
                        continue;
                    }

                    SetSmartStopSuppression(entity, buffer.ActiveOrder.Order.Args.Spatial.Mode == OrderCollectionMode.List);

                    if (!TryResolveTarget(in buffer.ActiveOrder.Order, out var target))
                    {
                        SetSmartStopSuppression(entity, suppressed: false);
                        ClearNavGoal(entity);
                        OrderSubmitter.NotifyOrderComplete(World, entity, _orderTypeRegistry);
                        continue;
                    }

                    float speedCmPerSec = ResolveMoveSpeed(entity);
                    if (speedCmPerSec <= 0f)
                    {
                        SetSmartStopSuppression(entity, suppressed: false);
                        ClearNavGoal(entity);
                        continue;
                    }

                    if (TryDriveNavigationGoal(entity, ref buffer.ActiveOrder.Order, speedCmPerSec, out bool navCompleted))
                    {
                        if (navCompleted)
                        {
                            SetSmartStopSuppression(entity, suppressed: false);
                            ClearNavGoal(entity);
                            OrderSubmitter.NotifyOrderComplete(World, entity, _orderTypeRegistry);
                        }

                        continue;
                    }

                    ref var position = ref positions[index];
                    var current = position.Value;
                    if (AdvanceLinearRoute(ref buffer.ActiveOrder.Order, ref current, speedCmPerSec * dt))
                    {
                        SetSmartStopSuppression(entity, suppressed: false);
                        ClearNavGoal(entity);
                        OrderSubmitter.NotifyOrderComplete(World, entity, _orderTypeRegistry);
                    }

                    position.Value = current;
                }
            }
        }

        private float ResolveMoveSpeed(Entity entity)
        {
            if (_moveSpeedAttributeId != AttributeRegistry.InvalidId &&
                World.TryGet(entity, out AttributeBuffer attributes))
            {
                float configured = attributes.GetCurrent(_moveSpeedAttributeId);
                if (configured > 0f)
                {
                    return configured;
                }
            }

            return _defaultSpeedCmPerSec;
        }

        private bool TryDriveNavigationGoal(Entity entity, ref Order order, float speedCmPerSec, out bool completed)
        {
            completed = false;
            if (!World.Has<NavAgent2D>(entity) ||
                !World.Has<Position2D>(entity))
            {
                return false;
            }

            if (!World.Has<NavGoal2D>(entity))
            {
                World.Add(entity, new NavGoal2D());
            }

            ref var goal = ref World.Get<NavGoal2D>(entity);
            if (World.Has<NavKinematics2D>(entity))
            {
                ref var kinematics = ref World.Get<NavKinematics2D>(entity);
                kinematics.MaxSpeedCmPerSec = Fix64.FromFloat(speedCmPerSec);
            }

            ref var position = ref World.Get<Position2D>(entity);
            goal.RadiusCm = Fix64.FromFloat(_stopRadiusCm);

            while (TryResolveCurrentTarget(in order, out var target))
            {
                goal.Kind = NavGoalKind2D.Point;
                goal.TargetCm = target;

                var delta = target - position.Value;
                if (delta.LengthSquared() > goal.RadiusCm * goal.RadiusCm)
                {
                    return true;
                }

                if (!TryAdvanceRoute(ref order))
                {
                    goal.Kind = NavGoalKind2D.None;
                    completed = true;
                    return true;
                }
            }

            goal.Kind = NavGoalKind2D.None;
            completed = true;
            return true;
        }

        private void ClearNavGoal(Entity entity)
        {
            if (!World.Has<NavGoal2D>(entity))
            {
                return;
            }

            ref var goal = ref World.Get<NavGoal2D>(entity);
            goal.Kind = NavGoalKind2D.None;
        }

        private void SetSmartStopSuppression(Entity entity, bool suppressed)
        {
            if (!World.Has<NavAgent2D>(entity))
            {
                return;
            }

            ref var navAgent = ref World.Get<NavAgent2D>(entity);
            byte suppressedByte = suppressed ? (byte)1 : (byte)0;
            if (navAgent.SmartStopSuppressed == suppressedByte)
            {
                return;
            }

            navAgent.SmartStopSuppressed = suppressedByte;
        }

        private static bool TryResolveTarget(in Order order, out Fix64Vec2 target)
        {
            target = default;
            if (!OrderWorldSpatialResolver.TryResolveMoveDestination(in order, out var worldCm))
            {
                return false;
            }

            target = Fix64Vec2.FromFloat(worldCm.X, worldCm.Z);
            return true;
        }

        private bool AdvanceLinearRoute(ref Order order, ref Fix64Vec2 current, float remainingStepCm)
        {
            int guard = OrderSpatial.MaxPoints + 1;
            while (remainingStepCm > 0f && guard-- > 0)
            {
                if (!TryResolveCurrentTarget(in order, out Fix64Vec2 target))
                {
                    return true;
                }

                float distanceToTargetCm = DistanceCm(current, target);
                bool arrived = WorldMoveCmStepHelper.StepTowards(
                    ref current,
                    target,
                    stepCm: remainingStepCm,
                    stopRadiusCm: _stopRadiusCm);
                if (!arrived)
                {
                    return false;
                }

                if (!TryAdvanceRoute(ref order))
                {
                    return true;
                }

                if (distanceToTargetCm <= _stopRadiusCm)
                {
                    continue;
                }

                remainingStepCm = Math.Max(0f, remainingStepCm - distanceToTargetCm);
            }

            return false;
        }

        private static bool TryResolveCurrentTarget(in Order order, out Fix64Vec2 target)
        {
            target = default;
            int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(in order.Args.Spatial);
            if (pointCount <= 0)
            {
                return false;
            }

            int pointIndex = order.Args.Spatial.Mode == OrderCollectionMode.List
                ? Math.Clamp(order.Args.Spatial.A0, 0, pointCount - 1)
                : 0;
            if (!OrderWorldSpatialResolver.TryResolveMoveWaypoint(in order, pointIndex, out var worldCm))
            {
                return false;
            }

            target = Fix64Vec2.FromFloat(worldCm.X, worldCm.Z);
            return true;
        }

        private static bool TryAdvanceRoute(ref Order order)
        {
            if (order.Args.Spatial.Mode != OrderCollectionMode.List)
            {
                return false;
            }

            int nextIndex = order.Args.Spatial.A0 + 1;
            if (nextIndex >= order.Args.Spatial.PointCount)
            {
                return false;
            }

            order.Args.Spatial.A0 = nextIndex;
            return true;
        }

        private static float DistanceCm(Fix64Vec2 current, Fix64Vec2 target)
        {
            var delta = target - current;
            float dx = delta.X.ToFloat();
            float dy = delta.Y.ToFloat();
            return MathF.Sqrt((dx * dx) + (dy * dy));
        }
    }
}
