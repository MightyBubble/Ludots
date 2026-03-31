using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Scripting;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadRoutePlanningService
    {
        private const int MaxPathPoints = OrderSpatial.MaxPoints;

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly string? _statusKey;
        private readonly RoadRouteQueryService _query;

        public RoadRoutePlanningService(World world, Dictionary<string, object> globals, string agentTypeId, string? statusKey = RoadMoveOrderExpander.LastSubmitStatusKey)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _statusKey = statusKey;
            _query = new RoadRouteQueryService(world, globals, agentTypeId, MaxPathPoints);
        }

        public bool ShouldPlanRoadMove(in Order order, out int moveToOrderTypeId)
        {
            moveToOrderTypeId = 0;
            if (!_world.IsAlive(order.Actor))
            {
                return false;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out object? configObj) ||
                configObj is not GameConfig config)
            {
                WriteStatus("Road command rejected: game config service is unavailable.");
                return false;
            }

            if (!config.Constants.OrderTypeIds.TryGetValue("moveTo", out moveToOrderTypeId) ||
                moveToOrderTypeId <= 0)
            {
                WriteStatus("Road command rejected: moveTo order type is not configured.");
                return false;
            }

            return order.OrderTypeId == moveToOrderTypeId;
        }

        public bool TryPlan(in Order order, out Order routeOrder, out string status)
        {
            routeOrder = default;
            status = string.Empty;
            if (!ShouldPlanRoadMove(in order, out int moveToOrderTypeId))
            {
                return false;
            }

            if (!TryResolveRoadMoveFollowOrderTypeId(out int roadMoveFollowOrderTypeId))
            {
                status = "Road command rejected: roadMoveFollow order type is not configured.";
                return false;
            }

            if (!_query.TryQuery(in order, moveToOrderTypeId, out var queryResult, out status))
            {
                WriteStatus(status);
                return false;
            }

            var compute = new RoadRouteComputeService(roadMoveFollowOrderTypeId);
            routeOrder = compute.CreateFollowOrder(in order, queryResult.PathXcm, queryResult.PathYcm, queryResult.Count, queryResult.FinalGoalWorldCm);
            WriteStatus(status);
            return true;
        }

        private bool TryResolveRoadMoveFollowOrderTypeId(out int roadMoveFollowOrderTypeId)
        {
            roadMoveFollowOrderTypeId = 0;
            if (!_globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out object? configObj) ||
                configObj is not GameConfig config ||
                !config.Constants.OrderTypeIds.TryGetValue(RoadNetworkShowcaseIds.RoadMoveFollowOrderTypeKey, out roadMoveFollowOrderTypeId) ||
                roadMoveFollowOrderTypeId <= 0)
            {
                WriteStatus("Road command rejected: roadMoveFollow order type is not configured.");
                return false;
            }

            return true;
        }

        private void WriteStatus(string message)
        {
            if (string.IsNullOrWhiteSpace(_statusKey))
            {
                return;
            }

            _globals[_statusKey] = message;
        }
    }
}
