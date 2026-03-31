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
using Ludots.Core.Physics;
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
                        ClearNavGoal(entity);
                        continue;
                    }

                    if (!TryResolveTarget(in buffer.ActiveOrder.Order, out var target))
                    {
                        ClearNavGoal(entity);
                        OrderSubmitter.NotifyOrderComplete(World, entity, _orderTypeRegistry);
                        continue;
                    }

                    float speedCmPerSec = ResolveMoveSpeed(entity);
                    if (speedCmPerSec <= 0f)
                    {
                        ClearNavGoal(entity);
                        StopNavigationMotion(entity);
                        continue;
                    }

                    if (TryDriveNavigationGoal(entity, target, speedCmPerSec))
                    {
                        continue;
                    }

                    ref var position = ref positions[index];
                    var current = position.Value;
                    bool arrived = WorldMoveCmStepHelper.StepTowards(
                        ref current,
                        target,
                        stepCm: speedCmPerSec * dt,
                        stopRadiusCm: _stopRadiusCm);
                    position.Value = current;

                    if (arrived)
                    {
                        ClearNavGoal(entity);
                        OrderSubmitter.NotifyOrderComplete(World, entity, _orderTypeRegistry);
                    }
                }
            }
        }

        private float ResolveMoveSpeed(Entity entity)
        {
            float configured = 0f;
            if (_moveSpeedAttributeId != AttributeRegistry.InvalidId &&
                World.TryGet(entity, out AttributeBuffer attributes))
            {
                configured = attributes.GetCurrent(_moveSpeedAttributeId);
                if (configured > 0f)
                {
                    return GameplayControlStateResolver.ResolveMoveSpeed(World, entity, configured);
                }
            }

            return GameplayControlStateResolver.ResolveMoveSpeed(
                World,
                entity,
                configured > 0f ? configured : _defaultSpeedCmPerSec);
        }

        private bool TryDriveNavigationGoal(Entity entity, Fix64Vec2 target, float speedCmPerSec)
        {
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
            goal.Kind = NavGoalKind2D.Point;
            goal.TargetCm = target;
            goal.RadiusCm = Fix64.FromFloat(_stopRadiusCm);

            if (World.Has<NavKinematics2D>(entity))
            {
                ref var kinematics = ref World.Get<NavKinematics2D>(entity);
                kinematics.MaxSpeedCmPerSec = Fix64.FromFloat(speedCmPerSec);
            }

            ref var position = ref World.Get<Position2D>(entity);
            var delta = target - position.Value;
            if (delta.LengthSquared() > goal.RadiusCm * goal.RadiusCm)
            {
                return true;
            }

            goal.Kind = NavGoalKind2D.None;
            OrderSubmitter.NotifyOrderComplete(World, entity, _orderTypeRegistry);
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

        private void StopNavigationMotion(Entity entity)
        {
            if (World.Has<Velocity2D>(entity))
            {
                ref var velocity = ref World.Get<Velocity2D>(entity);
                velocity = Velocity2D.Zero;
            }

            if (World.Has<NavDesiredVelocity2D>(entity))
            {
                ref var desiredVelocity = ref World.Get<NavDesiredVelocity2D>(entity);
                desiredVelocity = new NavDesiredVelocity2D
                {
                    ValueCmPerSec = Fix64Vec2.Zero
                };
            }

            if (World.Has<ForceInput2D>(entity))
            {
                ref var force = ref World.Get<ForceInput2D>(entity);
                force = new ForceInput2D
                {
                    Force = Fix64Vec2.Zero
                };
            }
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
    }
}
