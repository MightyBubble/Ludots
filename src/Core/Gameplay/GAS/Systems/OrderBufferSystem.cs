using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using GasGraphExecutor = Ludots.Core.NodeLibraries.GASGraph.GraphExecutor;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class OrderBufferSystem : BaseSystem<World, float>
    {
        private readonly IClock _clock;
        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly OrderRuleRegistry _orderRuleRegistry;
        private readonly OrderQueue? _incomingOrders;
        private readonly int _stepRateHz;
        private readonly OrderAdmissionResultBuffer? _admissionResults;

        private readonly GraphProgramRegistry? _graphProgramRegistry;
        private readonly IGraphRuntimeApi? _graphApi;

        private static readonly QueryDescription _orderBufferQuery = new QueryDescription()
            .WithAll<OrderBuffer>();

        public OrderBufferSystem(
            World world,
            IClock clock,
            OrderTypeRegistry orderTypeRegistry,
            OrderRuleRegistry orderRuleRegistry,
            OrderQueue? incomingOrders = null,
            int stepRateHz = 30,
            GraphProgramRegistry? graphProgramRegistry = null,
            IGraphRuntimeApi? graphApi = null,
            OrderAdmissionResultBuffer? admissionResults = null)
            : base(world)
        {
            _clock = clock;
            _orderTypeRegistry = orderTypeRegistry;
            _orderRuleRegistry = orderRuleRegistry;
            _incomingOrders = incomingOrders;
            if (stepRateHz <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stepRateHz), stepRateHz, "stepRateHz must be positive.");
            }

            _stepRateHz = stepRateHz;

            _graphProgramRegistry = graphProgramRegistry;
            _graphApi = graphApi;
            _admissionResults = admissionResults;
        }

        public override void Update(in float dt)
        {
            int currentStep = _clock.Now(ClockDomainId.Step);
            ProcessIncomingOrders(currentStep);

            foreach (ref var chunk in World.Query(in _orderBufferQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var buffers = chunk.GetSpan<OrderBuffer>();
                foreach (var index in chunk)
                {
                    ref OrderBuffer buffer = ref buffers[index];
                    ReleaseExpiredOrders(ref buffer, currentStep);
                    if (!buffer.HasActive && buffer.HasQueued)
                    {
                        Entity entity = Unsafe.Add(ref entityFirst, index);
                        OrderSubmitter.TryPromoteNextQueuedToActive(World, entity, ref buffer, _orderTypeRegistry);
                    }
                }
            }
        }

        private void ReleaseExpiredOrders(ref OrderBuffer buffer, int currentStep)
        {
            for (int i = buffer.QueuedCount - 1; i >= 0; i--)
            {
                QueuedOrder queued = buffer.GetQueued(i);
                if (queued.ExpireStep < 0 || queued.ExpireStep > currentStep)
                {
                    continue;
                }

                QueuedOrder removed = buffer.RemoveAtTransferred(i);
                OrderSpatialPayloadOps.Release(World, in removed.Order);
            }

            if (buffer.HasPending &&
                buffer.PendingOrder.ExpireStep >= 0 &&
                buffer.PendingOrder.ExpireStep <= currentStep)
            {
                OrderSubmitter.ReleasePendingOrder(World, ref buffer);
            }
        }

        private void ProcessIncomingOrders(int currentStep)
        {
            if (_incomingOrders == null) return;

            while (_incomingOrders.TryPeek(out var order))
            {
                OrderAdmissionReservation reservation = _admissionResults == null
                    ? default
                    : _admissionResults.Reserve(OrderAdmissionStage.EntityIntake, order.OrderId);
                bool committed = false;
                try
                {
                    if (!_incomingOrders.TryDequeue(out order))
                    {
                        throw new InvalidOperationException("ORDER.ADMISSION.ERR.IntakeQueueChangedDuringReservation");
                    }

                    OrderSubmitResult result = ProcessIncomingOrder(in order, currentStep);
                    CommitAdmission(in reservation, in order, result);
                    committed = true;
                }
                finally
                {
                    if (!committed && reservation.IsValid)
                    {
                        _admissionResults!.Cancel(in reservation);
                    }
                }
            }
        }

        private OrderSubmitResult ProcessIncomingOrder(in Order incomingOrder, int currentStep)
        {
            Order order = incomingOrder;
            order.SubmitStep = currentStep;

            if (!World.IsAlive(order.Actor) || !World.Has<OrderBuffer>(order.Actor))
            {
                OrderSpatialPayloadOps.Release(World, in order);
                return OrderSubmitResult.RejectedInvalidActor;
            }

            if (!_orderTypeRegistry.TryGet(order.OrderTypeId, out var config))
            {
                OrderSpatialPayloadOps.Release(World, in order);
                return OrderSubmitResult.RejectedInvalidOrderType;
            }

            if (config.ValidationGraphId > 0)
            {
                if (_graphProgramRegistry == null || _graphApi == null)
                {
                    throw new InvalidOperationException(
                        $"Order type {order.OrderTypeId} requires validation graph {config.ValidationGraphId}, but graph validation services are not configured.");
                }

                if (!_graphProgramRegistry.TryGetProgram(config.ValidationGraphId, out var validationProgram))
                {
                    throw new InvalidOperationException(
                        $"Order type {order.OrderTypeId} references missing validation graph {config.ValidationGraphId}.");
                }

                var targetPos = new IntVector2((int)order.Args.Spatial.WorldCm.X, (int)order.Args.Spatial.WorldCm.Z);
                bool passed = GasGraphExecutor.ExecuteValidation(
                    World,
                    order.Actor,
                    order.Target,
                    targetPos,
                    validationProgram,
                    _graphApi);
                if (!passed)
                {
                    OrderSpatialPayloadOps.Release(World, in order);
                    return OrderSubmitResult.RejectedValidation;
                }
            }

            var result = OrderSubmitter.Submit(
                World,
                order.Actor,
                in order,
                _orderTypeRegistry,
                _orderRuleRegistry,
                currentStep,
                _stepRateHz);

            if (result == OrderSubmitResult.RejectedByRule && config.PendingBufferWindowMs > 0)
            {
                int pendingExpireStep = currentStep + (config.PendingBufferWindowMs * _stepRateHz) / 1000;
                ref var buffer = ref World.Get<OrderBuffer>(order.Actor);
                OrderSubmitter.ReplacePending(World, ref buffer, in order, config.Priority, pendingExpireStep, currentStep);
                result = OrderSubmitResult.Pending;
            }
            else if (!IsAccepted(result))
            {
                OrderSpatialPayloadOps.Release(World, in order);
            }

            return result;
        }

        private static bool IsAccepted(OrderSubmitResult result) =>
            result == OrderSubmitResult.Activated ||
            result == OrderSubmitResult.Queued ||
            result == OrderSubmitResult.Pending;

        private void CommitAdmission(
            in OrderAdmissionReservation reservation,
            in Order order,
            OrderSubmitResult result)
        {
            if (_admissionResults == null) return;
            var outcome = new OrderAdmissionOutcome(order.OrderId, order.OrderTypeId, OrderAdmissionStage.EntityIntake, result);
            _admissionResults.Commit(in reservation, in outcome);
        }

        public OrderSubmitResult SubmitOrder(Entity entity, in Order order)
        {
            if (_admissionResults == null)
            {
                return SubmitOrderState(entity, in order);
            }

            if (order.OrderId <= 0)
            {
                throw new InvalidOperationException("ORDER.ADMISSION.ERR.DirectSubmitRequiresOrderId");
            }

            OrderAdmissionReservation reservation = _admissionResults.Reserve(
                OrderAdmissionStage.EntityIntake,
                order.OrderId);
            bool committed = false;
            try
            {
                OrderSubmitResult result = SubmitOrderState(entity, in order);
                CommitAdmission(in reservation, in order, result);
                committed = true;
                return result;
            }
            finally
            {
                if (!committed)
                {
                    _admissionResults.Cancel(in reservation);
                }
            }
        }

        private OrderSubmitResult SubmitOrderState(Entity entity, in Order order)
        {
            int currentStep = _clock.Now(ClockDomainId.Step);
            return OrderSubmitter.Submit(
                World,
                entity,
                in order,
                _orderTypeRegistry,
                _orderRuleRegistry,
                currentStep,
                _stepRateHz);
        }

        public void NotifyOrderComplete(Entity entity)
        {
            OrderSubmitter.NotifyOrderComplete(World, entity, _orderTypeRegistry);
            TrySubmitPending(entity);
        }

        private void TrySubmitPending(Entity entity)
        {
            if (!World.IsAlive(entity) || !World.Has<OrderBuffer>(entity))
            {
                return;
            }

            ref var buffer = ref World.Get<OrderBuffer>(entity);
            if (!buffer.HasPending || buffer.HasActive)
            {
                return;
            }

            var pendingOrder = buffer.PendingOrder.Order;
            buffer.ClearPendingTransferred();

            int currentStep = _clock.Now(ClockDomainId.Step);
            OrderSubmitResult result = OrderSubmitter.Submit(
                World,
                entity,
                in pendingOrder,
                _orderTypeRegistry,
                _orderRuleRegistry,
                currentStep,
                _stepRateHz);
            if (!IsAccepted(result))
            {
                OrderSpatialPayloadOps.Release(World, in pendingOrder);
            }
        }

        public bool TryGetActiveOrder(Entity entity, out Order order)
        {
            order = default;
            if (!World.IsAlive(entity) || !World.Has<OrderBuffer>(entity))
            {
                return false;
            }

            ref var buffer = ref World.Get<OrderBuffer>(entity);
            if (!buffer.HasActive)
            {
                return false;
            }

            order = buffer.ActiveOrder.Order;
            return true;
        }

        public OrderTypeRegistry OrderTypeRegistry => _orderTypeRegistry;
        public OrderRuleRegistry OrderRuleRegistry => _orderRuleRegistry;
    }
}
