using System;
using System.Numerics;
using Ludots.Core.Gameplay.GAS.Orders;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal static class RoadRouteRuntimeBinding
    {
        public static bool Matches(in RoadRouteRuntimeState state, in Order order)
        {
            int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(in order.Args.Spatial);
            if (pointCount != state.ActivePointCount)
            {
                return false;
            }

            if (state.ActiveOrderId > 0 &&
                order.OrderId > 0 &&
                state.ActiveOrderId != order.OrderId)
            {
                return false;
            }

            ResolveGoalSignature(in order, out int goalXcm, out int goalYcm);
            return state.ActiveGoalXcm == goalXcm && state.ActiveGoalYcm == goalYcm;
        }

        public static void Rebind(ref RoadRouteRuntimeState state, in Order order, bool preserveTimeoutCount = false)
        {
            short timeoutCount = preserveTimeoutCount ? state.TimeoutCount : (short)0;
            state = default;
            state.TimeoutCount = timeoutCount;
            state.ActiveOrderId = order.OrderId;
            state.ActivePointCount = OrderWorldSpatialResolver.GetSpatialPointCount(in order.Args.Spatial);
            ResolveGoalSignature(in order, out state.ActiveGoalXcm, out state.ActiveGoalYcm);
        }

        public static int ResolveStartWaypointIndex(in RoadRouteRuntimeState state, in Order order)
        {
            int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(in order.Args.Spatial);
            if (pointCount <= 0)
            {
                return 0;
            }

            return Math.Clamp(state.CurrentWaypointIndex, 0, pointCount - 1);
        }

        private static void ResolveGoalSignature(in Order order, out int goalXcm, out int goalYcm)
        {
            goalXcm = 0;
            goalYcm = 0;
            if (!RoadRouteFinalTargetResolver.TryResolve(in order, out Vector3 goalWorldCm))
            {
                return;
            }

            goalXcm = (int)MathF.Round(goalWorldCm.X, MidpointRounding.AwayFromZero);
            goalYcm = (int)MathF.Round(goalWorldCm.Z, MidpointRounding.AwayFromZero);
        }
    }
}
