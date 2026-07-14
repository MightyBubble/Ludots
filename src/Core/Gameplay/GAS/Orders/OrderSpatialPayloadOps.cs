using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public static class OrderSpatialPayloadOps
    {
        public static void SetPath(
            World world,
            Entity actor,
            ref Order order,
            ReadOnlySpan<int> pointXcm,
            ReadOnlySpan<int> pointYcm,
            int pointCount)
        {
            if (pointCount <= 0 ||
                pointCount > OrderSpatial.MaxPoints ||
                pointXcm.Length < pointCount ||
                pointYcm.Length < pointCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pointCount),
                    pointCount,
                    $"Order spatial path requires 1..{OrderSpatial.MaxPoints} points backed by complete X/Y spans.");
            }

            if (!world.IsAlive(actor))
            {
                throw new InvalidOperationException(
                    $"ORDER.SPATIAL.ERR.InvalidActor: actor={actor.Id}, pointCount={pointCount}.");
            }

            if (order.Args.Spatial.Payload.IsValid)
            {
                throw new InvalidOperationException(
                    $"ORDER.SPATIAL.ERR.PayloadAlreadyAssigned: actor={actor.Id}, orderId={order.OrderId}.");
            }

            ref OrderSpatial spatial = ref order.Args.Spatial;
            spatial.Kind = OrderSpatialKind.WorldCm;
            spatial.Mode = OrderCollectionMode.List;
            spatial.PointCount = 0;
            if (pointCount <= OrderSpatial.MaxInlinePoints)
            {
                for (int i = 0; i < pointCount; i++)
                {
                    spatial.AddInlinePointWorldCm(pointXcm[i], 0, pointYcm[i]);
                }

                return;
            }

            if (!world.Has<OrderSpatialPayloadBuffer>(actor))
            {
                throw new InvalidOperationException(
                    $"ORDER.SPATIAL.ERR.MissingPayloadBuffer: actor={actor.Id}, pointCount={pointCount}.");
            }

            ref OrderSpatialPayloadBuffer payloads = ref world.Get<OrderSpatialPayloadBuffer>(actor);
            if (!payloads.TryAllocate(pointXcm, pointYcm, pointCount, out OrderSpatialPayloadHandle handle))
            {
                throw new InvalidOperationException(
                    $"ORDER.SPATIAL.ERR.PayloadCapacity: actor={actor.Id}, pointCount={pointCount}, slotCapacity={OrderSpatialPayloadBuffer.SlotCapacity}.");
            }

            spatial.PointCount = pointCount;
            spatial.Payload = handle;
        }

        public static void Release(World world, in Order order)
        {
            if (!order.Args.Spatial.Payload.IsValid)
            {
                return;
            }

            if (!world.IsAlive(order.Actor))
            {
                return;
            }

            if (!world.Has<OrderSpatialPayloadBuffer>(order.Actor))
            {
                throw new InvalidOperationException(
                    $"ORDER.SPATIAL.ERR.MissingPayloadBuffer: actor={order.Actor.Id}, orderId={order.OrderId}.");
            }

            ref OrderSpatialPayloadBuffer payloads = ref world.Get<OrderSpatialPayloadBuffer>(order.Actor);
            payloads.Release(in order.Args.Spatial.Payload);
        }
    }
}
