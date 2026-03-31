using System;
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
        public bool TrySelect(in RoadNavPlanView plan, Fix64Vec2 position, int currentWaypointIndex, float stopRadiusCm, out RoadRouteSelection selection)
        {
            selection = default;
            int pointCount = plan.Count;
            if (pointCount <= 0)
            {
                selection = new RoadRouteSelection(completed: true, waypointIndex: pointCount, target: default);
                return false;
            }

            int currentIndex = Math.Clamp(currentWaypointIndex, 0, pointCount - 1);
            while (currentIndex < pointCount)
            {
                if (!plan.TryGetWaypoint(currentIndex, out Fix64Vec2 target))
                {
                    currentIndex++;
                    continue;
                }

                if (!ShouldConsumeWaypoint(position, target, stopRadiusCm))
                {
                    selection = new RoadRouteSelection(completed: false, waypointIndex: currentIndex, target: target);
                    return true;
                }

                currentIndex++;
            }

            selection = new RoadRouteSelection(completed: true, waypointIndex: pointCount, target: default);
            return true;
        }

        private static bool ShouldConsumeWaypoint(Fix64Vec2 position, Fix64Vec2 target, float stopRadiusCm)
        {
            return DistanceSquaredCm(position, target) <= stopRadiusCm * stopRadiusCm;
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
