using System.Numerics;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.MovePlanning;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadRouteFinalTargetMovePlanResolver : IMovePlanFinalTargetResolver
    {
        public bool TryResolveFinalTarget(Arch.Core.World world, in Order order, out Vector2 finalGoalWorldCm)
        {
            finalGoalWorldCm = default;
            if (!RoadRouteFinalTargetResolver.TryResolve(world, in order, out Vector3 targetWorldCm))
            {
                return false;
            }

            finalGoalWorldCm = new Vector2(targetWorldCm.X, targetWorldCm.Z);
            return true;
        }
    }

    internal static class RoadRouteFinalTargetResolver
    {
        private const int EncodedFlag = 1;

        public static bool TryResolve(Arch.Core.World world, in Order order, out Vector3 targetWorldCm)
        {
            targetWorldCm = default;
            if (order.Args.I2 == EncodedFlag)
            {
                targetWorldCm = new Vector3(order.Args.I0, 0f, order.Args.I1);
                return true;
            }

            // Directly-authored showcase follow orders may omit the preserved click target.
            // In that case, the last sampled waypoint remains the correct fallback arrival goal.
            return OrderWorldSpatialResolver.TryResolveMoveDestination(world, in order, out targetWorldCm);
        }

        public static bool TryEncodeFromSource(Arch.Core.World world, in Order sourceOrder, ref Order followOrder)
        {
            if (!OrderWorldSpatialResolver.TryResolveMoveDestination(world, in sourceOrder, out Vector3 targetWorldCm))
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
