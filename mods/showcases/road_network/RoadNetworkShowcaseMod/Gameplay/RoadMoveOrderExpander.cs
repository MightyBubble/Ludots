using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadMoveOrderExpander
    {
        public const string LastSubmitStatusKey = "RoadNetworkShowcase.LastSubmitStatus";

        private readonly Dictionary<string, object> _globals;
        private readonly OrderQueue _incomingOrders;
        private readonly string? _statusKey;
        private readonly RoadRoutePlanningService _planning;

        public RoadMoveOrderExpander(World world, Dictionary<string, object> globals, OrderQueue incomingOrders, string agentTypeId, string? statusKey = LastSubmitStatusKey)
        {
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _incomingOrders = incomingOrders ?? throw new ArgumentNullException(nameof(incomingOrders));
            _statusKey = statusKey;
            _planning = new RoadRoutePlanningService(world, globals, agentTypeId, statusKey);
        }

        public bool TrySubmit(in Order order)
        {
            if (!TryBuildFollowOrder(in order, out Order routeOrder))
            {
                if (!ShouldExpandRoadMove(in order, out _))
                {
                    Order passthrough = order;
                    bool enqueued = _incomingOrders.TryEnqueueAssigned(ref passthrough);
                    WriteStatus(enqueued ? "Passthrough order submitted." : "Passthrough order queue is full.");
                    return enqueued;
                }

                return false;
            }

            if (!_incomingOrders.TryEnqueueAssigned(ref routeOrder))
            {
                OrderSpatialPayloadOps.Release(_planning.World, in routeOrder);
                WriteStatus("Road command rejected: order queue is full.");
                return false;
            }

            return true;
        }

        public bool TryBuildFollowOrder(in Order order, out Order routeOrder)
        {
            return _planning.TryPlan(in order, out routeOrder, out _);
        }

        public bool ShouldExpandRoadMove(in Order order, out int moveToOrderTypeId)
        {
            return _planning.ShouldPlanRoadMove(in order, out moveToOrderTypeId);
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
