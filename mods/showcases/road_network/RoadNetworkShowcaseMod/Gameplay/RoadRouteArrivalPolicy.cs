using System.Numerics;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Mathematics.FixedPoint;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadRouteArrivalPolicy
    {
        public bool HasReachedFinalTarget(in Order order, Fix64Vec2 position, float arrivalRadiusCm)
        {
            if (!RoadRouteFinalTargetResolver.TryResolve(in order, out Vector3 destinationWorldCm))
            {
                return true;
            }

            Fix64Vec2 destination = Fix64Vec2.FromFloat(destinationWorldCm.X, destinationWorldCm.Z);
            var delta = destination - position;
            float dx = delta.X.ToFloat();
            float dy = delta.Y.ToFloat();
            return (dx * dx) + (dy * dy) <= arrivalRadiusCm * arrivalRadiusCm;
        }
    }
}
