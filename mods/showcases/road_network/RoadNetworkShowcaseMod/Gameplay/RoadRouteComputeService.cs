using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadRouteComputeService
    {
        private readonly int _roadMoveFollowOrderTypeId;

        public RoadRouteComputeService(int roadMoveFollowOrderTypeId)
        {
            _roadMoveFollowOrderTypeId = roadMoveFollowOrderTypeId;
        }

        public Order CreateFollowOrder(World world, in Order sourceOrder, ReadOnlySpan<int> pathXcm, ReadOnlySpan<int> pathYcm, int count, in Vector3 finalGoalWorldCm)
        {
            Order route = sourceOrder;
            route.OrderId = 0;
            route.OrderTypeId = _roadMoveFollowOrderTypeId;
            route.Target = Arch.Core.Entity.Null;
            route.Args = new OrderArgs();
            RoadRouteFinalTargetResolver.Encode(ref route, finalGoalWorldCm);
            OrderSpatialPayloadOps.SetPath(world, route.Actor, ref route, pathXcm, pathYcm, count);

            return route;
        }
    }
}
