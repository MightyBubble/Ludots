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

        public OrderSubmitResult TrySubmit(in Order order)
        {
            if (!TryBuildFollowOrder(in order, out Order routeOrder))
            {
                if (!ShouldExpandRoadMove(in order, out _))
                {
                    Order passthrough = order;
                    OrderSubmitResult passthroughResult = _incomingOrders.SubmitAssigned(ref passthrough);
                    WriteStatus(FormatSubmitStatus("Passthrough order", passthroughResult));
                    return passthroughResult;
                }

                return OrderSubmitResult.RejectedValidation;
            }

            OrderSubmitResult result = _incomingOrders.SubmitAssigned(ref routeOrder);
            if (!OrderSubmitResultSemantics.IsAccepted(result))
            {
                OrderSpatialPayloadOps.Release(_planning.World, in routeOrder);
                WriteStatus(FormatSubmitStatus("Road command rejected", result));
                return result;
            }

            return result;
        }

        public OrderSubmitResult TrySubmitSharedBatch(Span<Order> orders)
        {
            if (orders.IsEmpty)
            {
                return OrderSubmitResult.Queued;
            }

            for (int i = 0; i < orders.Length; i++)
            {
                if (TryBuildFollowOrder(in orders[i], out Order routeOrder))
                {
                    orders[i] = routeOrder;
                    continue;
                }

                if (ShouldExpandRoadMove(in orders[i], out _))
                {
                    ReleaseBuiltRoutePayloads(orders.Slice(0, i));
                    return OrderSubmitResult.RejectedValidation;
                }
            }

            OrderSubmitResult result = _incomingOrders.TryEnqueueSharedBatch(orders);
            if (!OrderSubmitResultSemantics.IsAccepted(result))
            {
                WriteStatus(FormatSubmitStatus("Road command rejected", result));
                ReleaseBuiltRoutePayloads(orders);
            }

            return result;
        }

        public bool TryBuildFollowOrder(in Order order, out Order routeOrder)
        {
            return _planning.TryPlan(in order, out routeOrder, out _);
        }

        public bool ShouldExpandRoadMove(in Order order, out int moveToOrderTypeId)
        {
            return _planning.ShouldPlanRoadMove(in order, out moveToOrderTypeId);
        }

        private void ReleaseBuiltRoutePayloads(ReadOnlySpan<Order> orders)
        {
            for (int i = 0; i < orders.Length; i++)
            {
                OrderSpatialPayloadOps.Release(_planning.World, in orders[i]);
            }
        }

        private static string FormatSubmitStatus(string prefix, OrderSubmitResult result)
        {
            if (OrderSubmitResultSemantics.IsAccepted(result))
            {
                return $"{prefix} submitted.";
            }

            return result switch
            {
                OrderSubmitResult.RejectedQueueFull => $"{prefix}: order queue is full.",
                OrderSubmitResult.RejectedAdmissionCapacity => $"{prefix}: admission capacity exhausted.",
                OrderSubmitResult.RejectedValidation => $"{prefix}: validation failed.",
                OrderSubmitResult.RejectedInvalidActor => $"{prefix}: invalid actor.",
                OrderSubmitResult.RejectedInvalidOrderType => $"{prefix}: invalid order type.",
                OrderSubmitResult.RejectedByRule => $"{prefix}: rejected by rule.",
                OrderSubmitResult.RejectedBlackboardCapacity => $"{prefix}: blackboard capacity exhausted.",
                OrderSubmitResult.RejectedMissingBlackboard => $"{prefix}: missing blackboard.",
                _ => $"{prefix}: {result}."
            };
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
