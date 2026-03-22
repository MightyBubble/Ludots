using System;
using Ludots.Core.Mathematics.FixedPoint;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadRouteTimeoutPolicy
    {
        public bool Update(
            ref RoadNavPlanRuntime state,
            Fix64Vec2 position,
            int waypointIndex,
            float dt,
            float minProgressCm,
            float stallTimeoutSeconds)
        {
            if (state.Initialized == 0)
            {
                Reset(ref state, position, waypointIndex);
                return false;
            }

            minProgressCm = Math.Max(0f, minProgressCm);
            stallTimeoutSeconds = Math.Max(0.05f, stallTimeoutSeconds);
            bool progressed = waypointIndex != state.LastResolvedWaypointIndex ||
                              DistanceSquaredCm(position, state.LastProgressPosition) >= minProgressCm * minProgressCm;
            if (progressed)
            {
                Reset(ref state, position, waypointIndex);
                return false;
            }

            state.StallSeconds += Math.Max(0f, dt);
            return state.StallSeconds >= stallTimeoutSeconds;
        }

        public void Reset(ref RoadNavPlanRuntime state, Fix64Vec2 position, int waypointIndex)
        {
            state.LastProgressPosition = position;
            state.LastResolvedWaypointIndex = waypointIndex;
            state.StallSeconds = 0f;
            state.Initialized = 1;
        }

        public void Clear(ref RoadNavPlanRuntime state)
        {
            state = default;
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
