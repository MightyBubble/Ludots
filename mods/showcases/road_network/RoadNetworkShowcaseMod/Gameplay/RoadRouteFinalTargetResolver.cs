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
        public static bool TryResolve(Arch.Core.World world, in Order order, out Vector3 targetWorldCm)
        {
            return OrderWorldSpatialResolver.TryResolveExplicitMoveDestination(in order, out targetWorldCm);
        }
    }
}
