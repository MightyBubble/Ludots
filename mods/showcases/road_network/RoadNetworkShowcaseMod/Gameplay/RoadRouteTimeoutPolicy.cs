using System;
using System.Numerics;
using Ludots.Core.MovePlanning;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadRouteTimeoutPolicy
    {
        public bool Update(
            ref MovePlanRuntime state,
            Vector2 position,
            Vector2 target,
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
                              DistanceCm(state.LastProgressPositionCm, target) - DistanceCm(position, target) >= minProgressCm;
            if (progressed)
            {
                Reset(ref state, position, waypointIndex);
                return false;
            }

            state.StallSeconds += Math.Max(0f, dt);
            return state.StallSeconds >= stallTimeoutSeconds;
        }

        public void Reset(ref MovePlanRuntime state, Vector2 position, int waypointIndex)
        {
            state.LastProgressPositionCm = position;
            state.LastResolvedWaypointIndex = waypointIndex;
            state.StallSeconds = 0f;
            state.Initialized = 1;
        }

        public void Clear(ref MovePlanRuntime state)
        {
            state = default;
        }

        private static float DistanceCm(Vector2 current, Vector2 target)
        {
            var delta = target - current;
            float dx = delta.X;
            float dy = delta.Y;
            return MathF.Sqrt((dx * dx) + (dy * dy));
        }
    }
}
