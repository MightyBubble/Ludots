using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public static class OrderWorldSpatialResolver
    {
        public static int GetSpatialPointCount(World world, in Order order)
        {
            ref readonly OrderSpatial spatial = ref order.Args.Spatial;
            return spatial.Mode switch
            {
                OrderCollectionMode.Single when spatial.Kind == OrderSpatialKind.WorldCm => 1,
                OrderCollectionMode.List when spatial.Kind == OrderSpatialKind.WorldCm && spatial.PointCount > 0 =>
                    ValidateListPointCount(world, in order),
                _ => 0
            };
        }

        public static bool TryResolveSpatialPointAt(World world, in Order order, int pointIndex, out Vector3 targetWorldCm)
        {
            ref readonly OrderSpatial spatial = ref order.Args.Spatial;
            targetWorldCm = default;
            if (spatial.Kind != OrderSpatialKind.WorldCm || pointIndex < 0)
            {
                return false;
            }

            if (spatial.Mode == OrderCollectionMode.Single)
            {
                if (pointIndex != 0)
                {
                    return false;
                }

                targetWorldCm = spatial.WorldCm;
                return true;
            }

            if (spatial.Mode == OrderCollectionMode.List && pointIndex < spatial.PointCount)
            {
                if (spatial.PointCount <= OrderSpatial.MaxInlinePoints)
                {
                    targetWorldCm = pointIndex switch
                    {
                        0 => spatial.Point0WorldCm,
                        1 => spatial.Point1WorldCm,
                        _ => default,
                    };
                    return true;
                }

                ref readonly OrderSpatialPayloadBuffer payloads = ref RequirePayloadBuffer(world, in order);
                return payloads.TryGetPoint(in spatial.Payload, pointIndex, out targetWorldCm);
            }

            return false;
        }

        public static bool TryResolveSpatialTarget(World world, in Order order, out Vector3 targetWorldCm)
        {
            targetWorldCm = default;
            int pointCount = GetSpatialPointCount(world, in order);
            return pointCount > 0 && TryResolveSpatialPointAt(world, in order, pointCount - 1, out targetWorldCm);
        }

        public static bool TryResolveMoveDestination(World world, in Order order, out Vector3 targetWorldCm)
        {
            if (TryResolveExplicitMoveDestination(in order, out targetWorldCm))
            {
                return true;
            }

            return TryResolveSpatialTarget(world, in order, out targetWorldCm);
        }

        public static bool TryResolveExplicitMoveDestination(in Order order, out Vector3 targetWorldCm)
        {
            ref readonly OrderSpatial spatial = ref order.Args.Spatial;
            if (spatial.Kind == OrderSpatialKind.WorldCm && spatial.HasDestinationWorldCm != 0)
            {
                targetWorldCm = spatial.DestinationWorldCm;
                return true;
            }

            targetWorldCm = default;
            return false;
        }

        public static bool TryResolveMoveWaypoint(World world, in Order order, int pointIndex, out Vector3 targetWorldCm)
        {
            return TryResolveSpatialPointAt(world, in order, pointIndex, out targetWorldCm);
        }

        public static bool TryGetEntityWorldCm(World world, Entity entity, out Vector3 worldCm)
        {
            worldCm = default;
            if (!world.IsAlive(entity))
            {
                return false;
            }

            if (world.Has<WorldPositionCm>(entity))
            {
                WorldCmInt2 cm = world.Get<WorldPositionCm>(entity).ToWorldCmInt2();
                worldCm = new Vector3(cm.X, 0f, cm.Y);
                return true;
            }

            return false;
        }

        public static bool TryResolveProjectedQueuedOrigin(World world, Entity actor, int moveToOrderTypeId, out Vector3 projectedWorldCm)
        {
            projectedWorldCm = default;
            if (!TryGetEntityWorldCm(world, actor, out projectedWorldCm) ||
                !world.Has<OrderBuffer>(actor))
            {
                return false;
            }

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            if (buffer.HasActive &&
                buffer.ActiveOrder.Order.OrderTypeId == moveToOrderTypeId &&
                TryResolveMoveDestination(world, in buffer.ActiveOrder.Order, out var activeMoveWorldCm))
            {
                projectedWorldCm = activeMoveWorldCm;
            }

            for (int i = 0; i < buffer.QueuedCount; i++)
            {
                Order queued = buffer.GetQueued(i).Order;
                if (queued.OrderTypeId != moveToOrderTypeId ||
                    !TryResolveMoveDestination(world, in queued, out var queuedMoveWorldCm))
                {
                    continue;
                }

                projectedWorldCm = queuedMoveWorldCm;
            }

            return true;
        }

        private static int ValidateListPointCount(World world, in Order order)
        {
            ref readonly OrderSpatial spatial = ref order.Args.Spatial;
            if (spatial.PointCount <= OrderSpatial.MaxInlinePoints)
            {
                if (spatial.Payload.IsValid)
                {
                    throw new System.InvalidOperationException(
                        $"ORDER.SPATIAL.ERR.UnexpectedPayload: actor={order.Actor.Id}, pointCount={spatial.PointCount}.");
                }

                return spatial.PointCount;
            }

            ref readonly OrderSpatialPayloadBuffer payloads = ref RequirePayloadBuffer(world, in order);
            int payloadPointCount = payloads.GetPointCount(in spatial.Payload);
            if (payloadPointCount != spatial.PointCount)
            {
                throw new System.InvalidOperationException(
                    $"ORDER.SPATIAL.ERR.PointCountMismatch: actor={order.Actor.Id}, authored={spatial.PointCount}, payload={payloadPointCount}.");
            }

            return payloadPointCount;
        }

        private static ref readonly OrderSpatialPayloadBuffer RequirePayloadBuffer(World world, in Order order)
        {
            if (!order.Args.Spatial.Payload.IsValid ||
                !world.IsAlive(order.Actor) ||
                !world.Has<OrderSpatialPayloadBuffer>(order.Actor))
            {
                throw new System.InvalidOperationException(
                    $"ORDER.SPATIAL.ERR.MissingPayloadBuffer: actor={order.Actor.Id}, orderId={order.OrderId}, pointCount={order.Args.Spatial.PointCount}.");
            }

            return ref world.Get<OrderSpatialPayloadBuffer>(order.Actor);
        }
    }
}
