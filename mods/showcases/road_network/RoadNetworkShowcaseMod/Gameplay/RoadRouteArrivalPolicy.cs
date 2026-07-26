using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadRouteArrivalPolicy
    {
        public bool HasReachedFinalTarget(World world, in Order order, Vector2 position, float arrivalRadiusCm)
        {
            if (!RoadRouteFinalTargetResolver.TryResolve(world, in order, out Vector3 destinationWorldCm))
            {
                return true;
            }

            var destination = new Vector2(destinationWorldCm.X, destinationWorldCm.Z);
            var delta = destination - position;
            float dx = delta.X;
            float dy = delta.Y;
            return (dx * dx) + (dy * dy) <= arrivalRadiusCm * arrivalRadiusCm;
        }
    }
}
