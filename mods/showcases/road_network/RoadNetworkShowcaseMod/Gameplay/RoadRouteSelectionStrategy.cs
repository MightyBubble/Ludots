using System;
using System.Numerics;
using Ludots.Core.MovePlanning;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal readonly struct RoadRouteSelection
    {
        public readonly bool Completed;
        public readonly int WaypointIndex;
        public readonly Vector2 Target;

        public RoadRouteSelection(bool completed, int waypointIndex, Vector2 target)
        {
            Completed = completed;
            WaypointIndex = waypointIndex;
            Target = target;
        }
    }

    internal sealed class RoadRouteSelectionStrategy
    {
        public bool TrySelect(in MovePlanView plan, Vector2 position, int currentWaypointIndex, float stopRadiusCm, out RoadRouteSelection selection)
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
                if (!plan.TryGetWaypoint(currentIndex, out Vector2 target))
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

        private static bool ShouldConsumeWaypoint(Vector2 position, Vector2 target, float stopRadiusCm)
        {
            return DistanceSquaredCm(position, target) <= stopRadiusCm * stopRadiusCm;
        }

        private static float DistanceSquaredCm(Vector2 current, Vector2 target)
        {
            var delta = target - current;
            float dx = delta.X;
            float dy = delta.Y;
            return (dx * dx) + (dy * dy);
        }
    }
}
