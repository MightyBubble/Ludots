using System.Numerics;
using Ludots.Core.Gameplay.GAS.Orders;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal static class RoadRouteFinalTargetResolver
    {
        private const int EncodedFlag = 1;

        public static bool TryResolve(in Order order, out Vector3 targetWorldCm)
        {
            targetWorldCm = default;
            if (order.Args.I2 == EncodedFlag)
            {
                targetWorldCm = new Vector3(order.Args.I0, 0f, order.Args.I1);
                return true;
            }

            // Directly-authored showcase follow orders may omit the preserved click target.
            // In that case, the last sampled waypoint remains the correct fallback arrival goal.
            return OrderWorldSpatialResolver.TryResolveMoveDestination(in order, out targetWorldCm);
        }

        public static bool TryEncodeFromSource(in Order sourceOrder, ref Order followOrder)
        {
            if (!OrderWorldSpatialResolver.TryResolveMoveDestination(in sourceOrder, out Vector3 targetWorldCm))
            {
                return false;
            }

            Encode(ref followOrder, targetWorldCm);
            return true;
        }

        public static void Encode(ref Order followOrder, in Vector3 targetWorldCm)
        {
            followOrder.Args.I0 = (int)targetWorldCm.X;
            followOrder.Args.I1 = (int)targetWorldCm.Z;
            followOrder.Args.I2 = EncodedFlag;
        }
    }
}
