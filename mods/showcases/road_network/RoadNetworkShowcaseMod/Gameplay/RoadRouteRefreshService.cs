using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadRouteRefreshService
    {
        private readonly RoadRoutePlanningService _planning;
        private readonly RoadNetworkOrderTypeIds _orderTypeIds;

        public RoadRouteRefreshService(World world, Dictionary<string, object> globals, string agentTypeId)
        {
            _ = globals ?? throw new ArgumentNullException(nameof(globals));
            _planning = new RoadRoutePlanningService(world, globals, agentTypeId);
            _orderTypeIds = RoadNetworkOrderTypeIds.Require(globals);
        }

        public bool TryRefresh(Entity actor, int playerId, in Vector3 finalGoalWorldCm, out Order refreshedOrder, out string status)
        {
            refreshedOrder = default;
            status = string.Empty;
            var refreshRequest = new Order
            {
                OrderTypeId = _orderTypeIds.MoveTo,
                PlayerId = playerId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate
            };
            refreshRequest.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
            refreshRequest.Args.Spatial.Mode = OrderCollectionMode.Single;
            refreshRequest.Args.Spatial.WorldCm = finalGoalWorldCm;

            return _planning.TryPlan(in refreshRequest, out refreshedOrder, out status);
        }

    }
}
