using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class MoveToWorldCmOrderSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<WorldPositionCm, OrderBuffer>()
            .WithNone<MovementSuppressed2D>();

        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly int _moveToOrderTypeId;
        private readonly Fix64 _stopRadiusCm;
        private readonly int _moveSpeedAttributeId;
        private readonly List<Entity> _completedOrders = new(64);

        public MoveToWorldCmOrderSystem(
            World world,
            OrderTypeRegistry orderTypeRegistry,
            int moveToOrderTypeId,
            float stopRadiusCm = 40f) : base(world)
        {
            _orderTypeRegistry = orderTypeRegistry ?? throw new ArgumentNullException(nameof(orderTypeRegistry));
            _moveToOrderTypeId = moveToOrderTypeId;
            _stopRadiusCm = Fix64.FromFloat(Math.Max(0f, stopRadiusCm));
            _moveSpeedAttributeId = AttributeRegistry.Register("MoveSpeed");
        }

        public override void Update(in float dt)
        {
            if (_moveToOrderTypeId <= 0 || dt <= 0f)
            {
                return;
            }

            _completedOrders.Clear();
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
                        continue;
                    }

                    int currentWaypointIndex = SyncMoveRuntime(ref buffer);

                    if (!TryResolveTarget(in buffer.ActiveOrder.Order, currentWaypointIndex, out var target))
                    {
                        ResetMoveRuntime(ref buffer);
                        _completedOrders.Add(entity);
                        continue;
                    }

                    // Movement is gated on an explicit, authored MoveSpeed attribute, which is the single
                    // source of truth for both "is this entity movable" and "how fast". OrderBuffer alone is
                    // a general command/ability queue and does not imply movability; there is no implicit
                    // default speed. Structures and other orderable-but-immovable entities (no MoveSpeed)
                    // ignore moveTo instead of teleporting.
                    if (!TryResolveMoveSpeed(entity, out Fix64 speedCmPerSec))
                    {
                        continue;
                    }

                    if (TryDrivePhysicsBody(entity, in buffer.ActiveOrder.Order, currentWaypointIndex, speedCmPerSec, out bool physicsCompleted, out int physicsNextWaypointIndex))
                    {
                        WriteMoveRuntimeIndex(ref buffer, physicsNextWaypointIndex);
                        if (physicsCompleted)
                        {
                            ResetMoveRuntime(ref buffer);
                            _completedOrders.Add(entity);
                        }

                        continue;
                    }

                    ref var position = ref positions[index];
                    var current = position.Value;
                    Fix64 stepCm = speedCmPerSec * Fix64.FromFloat(dt);
                    bool completed = AdvanceLinearRoute(
                        in buffer.ActiveOrder.Order,
                        currentWaypointIndex,
                        ref current,
                        stepCm,
                        out int nextWaypointIndex);
                    WriteMoveRuntimeIndex(ref buffer, nextWaypointIndex);
                    if (completed)
                    {
                        ResetMoveRuntime(ref buffer);
                        _completedOrders.Add(entity);
                    }

                    position.Value = current;
                }
            }

            for (int i = 0; i < _completedOrders.Count; i++)
            {
                OrderSubmitter.NotifyOrderComplete(World, _completedOrders[i], _orderTypeRegistry);
            }
        }

        // Resolves the authored move speed. Returns false when the entity declares no positive MoveSpeed
        // attribute: such an entity is not movable via this system and its moveTo orders are ignored.
        // There is intentionally no default-speed fallback — speed must be explicit, data-driven (SSOT).
        private bool TryResolveMoveSpeed(Entity entity, out Fix64 speedCmPerSec)
        {
            speedCmPerSec = Fix64.Zero;
            if (_moveSpeedAttributeId != AttributeRegistry.InvalidId &&
                World.TryGet(entity, out AttributeBuffer attributes))
            {
                float configured = attributes.GetCurrent(_moveSpeedAttributeId);
                if (configured > 0f)
                {
                    speedCmPerSec = Fix64.FromFloat(configured);
                    return true;
                }
            }

            return false;
        }

        private bool TryDrivePhysicsBody(Entity entity, in Order order, int currentWaypointIndex, Fix64 speedCmPerSec, out bool completed, out int nextWaypointIndex)
        {
            completed = false;
            nextWaypointIndex = currentWaypointIndex;
            if (!World.TryGet(entity, out Position2D physicsPosition) ||
                !World.TryGet(entity, out Velocity2D velocity) ||
                !World.TryGet(entity, out Mass2D mass) ||
                mass.IsStatic)
            {
                return false;
            }

            while (TryResolveCurrentTarget(in order, nextWaypointIndex, out var target))
            {
                var delta = target - physicsPosition.Value;
                Fix64 distanceCm = delta.Length();
                if (distanceCm > _stopRadiusCm)
                {
                    velocity.Linear = ComposePhysicsMoveVelocity(velocity.Linear, delta, speedCmPerSec);
                    World.Set(entity, velocity);
                    return true;
                }

                if (!TryAdvanceRoute(in order, ref nextWaypointIndex))
                {
                    velocity.Linear = Fix64Vec2.Zero;
                    World.Set(entity, velocity);
                    completed = true;
                    return true;
                }
            }

            velocity.Linear = Fix64Vec2.Zero;
            World.Set(entity, velocity);
            completed = true;
            return true;
        }

        private static Fix64Vec2 ComposePhysicsMoveVelocity(Fix64Vec2 currentVelocity, Fix64Vec2 toTarget, Fix64 speedCmPerSec)
        {
            if (speedCmPerSec <= Fix64.Zero)
            {
                return Fix64Vec2.Zero;
            }

            var desiredDirection = toTarget.Normalized();
            var currentForwardSpeed = Fix64Vec2.Dot(currentVelocity, desiredDirection);
            var lateralVelocity = currentVelocity - desiredDirection * currentForwardSpeed;
            var drivenVelocity = currentForwardSpeed < Fix64.Zero
                ? desiredDirection * currentForwardSpeed
                : desiredDirection * speedCmPerSec;

            var combined = drivenVelocity + lateralVelocity;
            return ClampLength(combined, speedCmPerSec);
        }

        private static Fix64Vec2 ClampLength(Fix64Vec2 value, Fix64 maxLength)
        {
            if (maxLength <= Fix64.Zero)
            {
                return Fix64Vec2.Zero;
            }

            var lengthSq = value.LengthSquared();
            var maxLengthSq = maxLength * maxLength;
            if (lengthSq <= maxLengthSq)
            {
                return value;
            }

            var length = Fix64Math.Sqrt(lengthSq);
            if (length <= Fix64.Zero)
            {
                return Fix64Vec2.Zero;
            }

            return value * (maxLength / length);
        }

        private bool TryResolveTarget(in Order order, int currentWaypointIndex, out Fix64Vec2 target)
        {
            target = default;
            if (!TryResolveCurrentTarget(in order, currentWaypointIndex, out target))
            {
                return false;
            }
            return true;
        }

        private bool AdvanceLinearRoute(
            in Order order,
            int currentWaypointIndex,
            ref Fix64Vec2 current,
            Fix64 remainingStepCm,
            out int nextWaypointIndex)
        {
            nextWaypointIndex = currentWaypointIndex;
            int guard = OrderSpatial.MaxPoints + 1;
            while (remainingStepCm > Fix64.Zero && guard-- > 0)
            {
                if (!TryResolveCurrentTarget(in order, nextWaypointIndex, out Fix64Vec2 target))
                {
                    return true;
                }

                Fix64 distanceToTargetCm = Fix64Vec2.Distance(current, target);
                bool arrived = WorldMoveCmStepHelper.StepTowards(
                    ref current,
                    target,
                    stepCm: remainingStepCm,
                    stopRadiusCm: _stopRadiusCm);
                if (!arrived)
                {
                    return false;
                }

                if (!TryAdvanceRoute(in order, ref nextWaypointIndex))
                {
                    return true;
                }

                if (distanceToTargetCm <= _stopRadiusCm)
                {
                    continue;
                }

                remainingStepCm = Fix64.Max(Fix64.Zero, remainingStepCm - distanceToTargetCm);
            }

            return false;
        }

        private bool TryResolveCurrentTarget(in Order order, int currentWaypointIndex, out Fix64Vec2 target)
        {
            target = default;
            int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(World, in order);
            if (pointCount <= 0)
            {
                return false;
            }

            int pointIndex = order.Args.Spatial.Mode == OrderCollectionMode.List
                ? Math.Clamp(currentWaypointIndex, 0, pointCount - 1)
                : 0;
            if (!OrderWorldSpatialResolver.TryResolveMoveWaypoint(World, in order, pointIndex, out var worldCm))
            {
                return false;
            }

            target = Fix64Vec2.FromFloat(worldCm.X, worldCm.Z);
            return true;
        }

        private bool TryAdvanceRoute(in Order order, ref int currentWaypointIndex)
        {
            if (order.Args.Spatial.Mode != OrderCollectionMode.List)
            {
                return false;
            }

            int nextIndex = currentWaypointIndex + 1;
            if (nextIndex >= OrderWorldSpatialResolver.GetSpatialPointCount(World, in order))
            {
                return false;
            }

            currentWaypointIndex = nextIndex;
            return true;
        }

        private int SyncMoveRuntime(ref OrderBuffer buffer)
        {
            if (buffer.ActiveOrder.Order.Args.Spatial.Mode != OrderCollectionMode.List)
            {
                buffer.ActiveRuntimeInt0 = 0;
                return 0;
            }

            int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(World, in buffer.ActiveOrder.Order);
            buffer.ActiveRuntimeInt0 = pointCount > 0
                ? Math.Clamp(buffer.ActiveRuntimeInt0, 0, pointCount - 1)
                : 0;
            return buffer.ActiveRuntimeInt0;
        }

        private void WriteMoveRuntimeIndex(ref OrderBuffer buffer, int currentWaypointIndex)
        {
            if (buffer.ActiveOrder.Order.Args.Spatial.Mode != OrderCollectionMode.List)
            {
                buffer.ActiveRuntimeInt0 = 0;
                return;
            }

            int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(World, in buffer.ActiveOrder.Order);
            buffer.ActiveRuntimeInt0 = pointCount > 0
                ? Math.Clamp(currentWaypointIndex, 0, pointCount - 1)
                : 0;
        }

        private static void ResetMoveRuntime(ref OrderBuffer buffer)
        {
            buffer.ActiveRuntimeInt0 = 0;
        }

        public override void Dispose() => base.Dispose();
    }
}
