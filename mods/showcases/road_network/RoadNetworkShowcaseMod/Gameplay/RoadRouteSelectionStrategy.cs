using System;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Mathematics.FixedPoint;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal readonly struct RoadRouteSelection
    {
        public readonly bool Completed;
        public readonly int WaypointIndex;
        public readonly Fix64Vec2 Target;

        public RoadRouteSelection(bool completed, int waypointIndex, Fix64Vec2 target)
        {
            Completed = completed;
            WaypointIndex = waypointIndex;
            Target = target;
        }
    }

    internal sealed class RoadRouteSelectionStrategy
    {
        public bool TrySelect(in Order order, Fix64Vec2 position, float stopRadiusCm, out RoadRouteSelection selection)
        {
            selection = default;
            int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(in order.Args.Spatial);
            if (pointCount <= 0)
            {
                selection = new RoadRouteSelection(completed: true, waypointIndex: pointCount, target: default);
                return false;
            }

            int currentIndex = Math.Clamp(order.Args.Spatial.A0, 0, pointCount - 1);
            while (currentIndex < pointCount)
            {
                if (!TryResolveWaypoint(in order, currentIndex, out Fix64Vec2 target))
                {
                    currentIndex++;
                    continue;
                }

                if (!ShouldConsumeWaypoint(in order, position, currentIndex, target, stopRadiusCm))
                {
                    selection = new RoadRouteSelection(completed: false, waypointIndex: currentIndex, target: target);
                    return true;
                }

                currentIndex++;
            }

            selection = new RoadRouteSelection(completed: true, waypointIndex: pointCount, target: default);
            return true;
        }

        private static bool ShouldConsumeWaypoint(in Order order, Fix64Vec2 position, int waypointIndex, Fix64Vec2 target, float stopRadiusCm)
        {
            return DistanceSquaredCm(position, target) <= stopRadiusCm * stopRadiusCm;
        }

        private static bool TryResolveWaypoint(in Order order, int waypointIndex, out Fix64Vec2 target)
        {
            target = default;
            int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(in order.Args.Spatial);
            if ((uint)waypointIndex >= (uint)pointCount ||
                !OrderWorldSpatialResolver.TryResolveMoveWaypoint(in order, waypointIndex, out var worldCm))
            {
                return false;
            }

            target = Fix64Vec2.FromFloat(worldCm.X, worldCm.Z);
            return true;
        }

        private static float DistanceSquaredCm(Fix64Vec2 current, Fix64Vec2 target)
        {
            var delta = target - current;
            float dx = delta.X.ToFloat();
            float dy = delta.Y.ToFloat();
            return (dx * dx) + (dy * dy);
        }
    }
}
