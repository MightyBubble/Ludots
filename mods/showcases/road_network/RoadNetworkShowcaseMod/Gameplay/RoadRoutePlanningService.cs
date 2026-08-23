using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadRoutePlanningService
    {
        private const int MaxPathPoints = OrderSpatial.MaxPoints;

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly string? _statusKey;
        private readonly RoadRouteQueryService _query;
        private readonly RoadNetworkOrderTypeIds _orderTypeIds;

        public RoadRoutePlanningService(World world, Dictionary<string, object> globals, string agentTypeId, string? statusKey = RoadMoveOrderExpander.LastSubmitStatusKey)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _statusKey = statusKey;
            _query = new RoadRouteQueryService(world, globals, agentTypeId, MaxPathPoints);
            _orderTypeIds = RoadNetworkOrderTypeIds.Require(globals);
        }

        public World World => _world;

        public bool ShouldPlanRoadMove(in Order order, out int moveToOrderTypeId)
        {
            if (!_world.IsAlive(order.Actor))
            {
                moveToOrderTypeId = 0;
                return false;
            }

            moveToOrderTypeId = _orderTypeIds.MoveTo;
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

            if (!_query.TryQuery(in order, moveToOrderTypeId, out var queryResult, out status))
            {
                WriteStatus(status);
                return false;
            }

            var compute = new RoadRouteComputeService(_orderTypeIds.RoadMoveFollow);
            routeOrder = compute.CreateFollowOrder(_world, in order, queryResult.PathXcm, queryResult.PathYcm, queryResult.Count, queryResult.FinalGoalWorldCm);
            WriteStatus(status);
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
