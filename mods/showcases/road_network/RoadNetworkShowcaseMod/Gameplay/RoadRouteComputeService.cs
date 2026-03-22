using System;
using System.Numerics;
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

        public Order CreateFollowOrder(in Order sourceOrder, ReadOnlySpan<int> pathXcm, ReadOnlySpan<int> pathYcm, int count, in Vector3 finalGoalWorldCm)
        {
            Order route = sourceOrder;
            route.OrderId = 0;
            route.OrderTypeId = _roadMoveFollowOrderTypeId;
            route.Target = Arch.Core.Entity.Null;
            route.Args = new OrderArgs();
            route.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
            route.Args.Spatial.Mode = OrderCollectionMode.List;
            route.Args.Spatial.A0 = 0;
            RoadRouteFinalTargetResolver.Encode(ref route, finalGoalWorldCm);

            for (int i = 0; i < count; i++)
            {
                route.Args.Spatial.AddPointWorldCm(pathXcm[i], 0, pathYcm[i]);
            }

            return route;
        }
    }
}
