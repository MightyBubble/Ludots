using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Scripting;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadRouteRefreshService
    {
        private readonly Dictionary<string, object> _globals;
        private readonly RoadRoutePlanningService _planning;

        public RoadRouteRefreshService(World world, Dictionary<string, object> globals, string agentTypeId)
        {
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _planning = new RoadRoutePlanningService(world, globals, agentTypeId);
        }

        public bool TryRefresh(Entity actor, int playerId, in Vector3 finalGoalWorldCm, out Order refreshedOrder, out string status)
        {
            refreshedOrder = default;
            status = string.Empty;
            if (!TryResolveMoveToOrderTypeId(out int moveToOrderTypeId))
            {
                status = "Road command rejected: moveTo order type is not configured.";
                return false;
            }

            var refreshRequest = new Order
            {
                OrderTypeId = moveToOrderTypeId,
                PlayerId = playerId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate
            };
            refreshRequest.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
            refreshRequest.Args.Spatial.Mode = OrderCollectionMode.Single;
            refreshRequest.Args.Spatial.WorldCm = finalGoalWorldCm;

            return _planning.TryPlan(in refreshRequest, out refreshedOrder, out status);
        }

        private bool TryResolveMoveToOrderTypeId(out int moveToOrderTypeId)
        {
            moveToOrderTypeId = 0;
            if (!_globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out object? configObj) ||
                configObj is not GameConfig config ||
                !config.Constants.OrderTypeIds.TryGetValue("moveTo", out moveToOrderTypeId) ||
                moveToOrderTypeId <= 0)
            {
                return false;
            }

            return true;
        }
    }
}
