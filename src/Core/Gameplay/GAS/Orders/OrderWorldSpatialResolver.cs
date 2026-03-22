using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public static class OrderWorldSpatialResolver
    {
        public static int GetSpatialPointCount(in OrderSpatial spatial)
        {
            return spatial.Mode switch
            {
                OrderCollectionMode.Single when spatial.Kind == OrderSpatialKind.WorldCm => 1,
                OrderCollectionMode.List when spatial.Kind == OrderSpatialKind.WorldCm && spatial.PointCount > 0 => spatial.PointCount,
                _ => 0
            };
        }

        public static bool TryResolveSpatialPointAt(in OrderSpatial spatial, int pointIndex, out Vector3 targetWorldCm)
        {
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
                unsafe
                {
                    fixed (int* px = spatial.PointX)
                    fixed (int* py = spatial.PointY)
                    fixed (int* pz = spatial.PointZ)
                    {
                        targetWorldCm = new Vector3(px[pointIndex], py[pointIndex], pz[pointIndex]);
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool TryResolveSpatialTarget(in OrderSpatial spatial, out Vector3 targetWorldCm)
        {
            targetWorldCm = default;
            int pointCount = GetSpatialPointCount(in spatial);
            return pointCount > 0 && TryResolveSpatialPointAt(in spatial, pointCount - 1, out targetWorldCm);
        }

        public static bool TryResolveMoveDestination(in Order order, out Vector3 targetWorldCm)
        {
            return TryResolveSpatialTarget(in order.Args.Spatial, out targetWorldCm);
        }

        public static bool TryResolveMoveWaypoint(in Order order, int pointIndex, out Vector3 targetWorldCm)
        {
            return TryResolveSpatialPointAt(in order.Args.Spatial, pointIndex, out targetWorldCm);
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

            if (world.Has<VisualTransform>(entity))
            {
                Vector3 visual = world.Get<VisualTransform>(entity).Position;
                worldCm = new Vector3(visual.X * 100f, 0f, visual.Z * 100f);
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
                TryResolveMoveDestination(in buffer.ActiveOrder.Order, out var activeMoveWorldCm))
            {
                projectedWorldCm = activeMoveWorldCm;
            }

            for (int i = 0; i < buffer.QueuedCount; i++)
            {
                Order queued = buffer.GetQueued(i).Order;
                if (queued.OrderTypeId != moveToOrderTypeId ||
                    !TryResolveMoveDestination(in queued, out var queuedMoveWorldCm))
                {
                    continue;
                }

                projectedWorldCm = queuedMoveWorldCm;
            }

            return true;
        }
    }
}
