using System.Numerics;
using Ludots.Core.Gameplay.GAS.Orders;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadRouteArrivalPolicy
    {
        public bool HasReachedFinalTarget(in Order order, Vector2 position, float arrivalRadiusCm)
        {
            if (!RoadRouteFinalTargetResolver.TryResolve(in order, out Vector3 destinationWorldCm))
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
