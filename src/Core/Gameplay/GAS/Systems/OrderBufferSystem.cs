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
        private readonly OrderAdmissionResultBuffer _admissionResults;
        private readonly Order[] _incomingBatchScratch;
        private readonly OrderSubmitResult[] _incomingBatchResultsScratch;
        private readonly OrderAdmissionReservation[] _entityAdmissionReservationsScratch;

        private readonly GraphProgramRegistry? _graphProgramRegistry;
        private readonly IGraphRuntimeApi? _graphApi;

        public uint IncomingRevision { get; private set; }

        private static readonly QueryDescription _orderBufferQuery = new QueryDescription()
            .WithAll<OrderBuffer>();

        public OrderBufferSystem(
            World world,
            IClock clock,
            OrderTypeRegistry orderTypeRegistry,
            OrderRuleRegistry orderRuleRegistry,
            OrderAdmissionResultBuffer admissionResults,
            OrderQueue? incomingOrders = null,
            int stepRateHz = 30,
            GraphProgramRegistry? graphProgramRegistry = null,
            IGraphRuntimeApi? graphApi = null)
            : base(world)
        {
            _clock = clock;
            _orderTypeRegistry = orderTypeRegistry;
            _orderRuleRegistry = orderRuleRegistry;
            _incomingOrders = incomingOrders;
            int incomingCapacity = incomingOrders?.Capacity ?? 0;
            _incomingBatchScratch = incomingCapacity > 0
                ? new Order[incomingCapacity]
                : Array.Empty<Order>();
            _incomingBatchResultsScratch = incomingCapacity > 0
                ? new OrderSubmitResult[incomingCapacity]
                : Array.Empty<OrderSubmitResult>();
            _entityAdmissionReservationsScratch = incomingCapacity > 0
                ? new OrderAdmissionReservation[incomingCapacity]
                : Array.Empty<OrderAdmissionReservation>();
            if (stepRateHz <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stepRateHz), stepRateHz, "stepRateHz must be positive.");
            }

            _stepRateHz = stepRateHz;

            _graphProgramRegistry = graphProgramRegistry;
            _graphApi = graphApi;
            _admissionResults = admissionResults
                ?? throw new ArgumentNullException(nameof(admissionResults));
            if (_incomingOrders != null && !ReferenceEquals(_incomingOrders.AdmissionResults, _admissionResults))
            {
                throw new InvalidOperationException("ORDER.ADMISSION.ERR.AdmissionResultBufferMismatch");
            }
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

            _admissionResults.EndEntityIntake();
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

            while (_incomingOrders.TryPeekBatch(_incomingBatchScratch, out int batchCount))
            {
                if (batchCount == 1 && _incomingBatchScratch[0].AdmissionBatchId == 0)
                {
                    ProcessSingleIncomingOrder(ref _incomingBatchScratch[0], currentStep);
                    continue;
                }

                ProcessIncomingBatch(batchCount, currentStep);
            }
        }

        private void ProcessSingleIncomingOrder(ref Order order, int currentStep)
        {
            OrderAdmissionReservation reservation = _admissionResults.Reserve(
                OrderAdmissionStage.EntityIntake,
                order.OrderId,
                order.OrderTypeId);
            bool committed = false;
            try
            {
                DequeueReservedBatch(expectedBatchCount: 1);
                OrderSubmitResult result = ProcessIncomingOrder(ref order, currentStep);
                CommitAdmission(in reservation, in order, result);
                committed = true;
                if (OrderSubmitResultSemantics.IsAccepted(result))
                {
                    IncomingRevision++;
                }
            }
            finally
            {
                if (!committed && reservation.IsValid)
                {
                    _admissionResults.Cancel(in reservation);
                }
            }
        }

        private void ProcessIncomingBatch(int batchCount, int currentStep)
        {
            ReserveEntityAdmissions(batchCount);
            bool committed = false;
            try
            {
                DequeueReservedBatch(batchCount);
                bool accepted = PreflightIncomingBatch(batchCount, currentStep, out OrderSubmitResult failureResult);
                if (!accepted)
                {
                    for (int i = 0; i < batchCount; i++)
                    {
                        if (OrderSubmitResultSemantics.IsAccepted(_incomingBatchResultsScratch[i]))
                        {
                            _incomingBatchResultsScratch[i] = failureResult;
                        }

                        OrderSpatialPayloadOps.Release(World, in _incomingBatchScratch[i]);
                        CommitAdmission(
                            in _entityAdmissionReservationsScratch[i],
                            in _incomingBatchScratch[i],
                            _incomingBatchResultsScratch[i]);
                    }

                    committed = true;
                    ClearEntityAdmissionReservations(batchCount);
                    return;
                }

                for (int i = 0; i < batchCount; i++)
                {
                    ref Order order = ref _incomingBatchScratch[i];
                    OrderSubmitResult result = OrderSubmitter.Submit(
                        World,
                        order.Actor,
                        in order,
                        _orderTypeRegistry,
                        _orderRuleRegistry,
                        currentStep,
                        _stepRateHz);
                    if (result != OrderSubmitResult.Activated && result != OrderSubmitResult.Queued)
                    {
                        throw new InvalidOperationException(
                            $"Order admission batch {order.AdmissionBatchId} changed after successful preflight: row {i} returned {result}.");
                    }

                    CommitAdmission(in _entityAdmissionReservationsScratch[i], in order, result);
                    IncomingRevision++;
                }

                committed = true;
                ClearEntityAdmissionReservations(batchCount);
            }
            finally
            {
                if (!committed)
                {
                    CancelEntityAdmissions(batchCount);
                    for (int i = 0; i < batchCount; i++)
                    {
                        OrderSpatialPayloadOps.Release(World, in _incomingBatchScratch[i]);
                    }
                }
            }
        }

        private void ReserveEntityAdmissions(int batchCount)
        {
            for (int i = 0; i < batchCount; i++)
            {
                try
                {
                    _entityAdmissionReservationsScratch[i] = _admissionResults.Reserve(
                        OrderAdmissionStage.EntityIntake,
                        _incomingBatchScratch[i].OrderId,
                        _incomingBatchScratch[i].OrderTypeId);
                }
                catch
                {
                    CancelEntityAdmissions(i);
                    throw;
                }
            }
        }

        private void DequeueReservedBatch(int expectedBatchCount)
        {
            int dequeuedCount = 0;
            if (_incomingOrders == null ||
                !_incomingOrders.TryDequeueBatch(_incomingBatchScratch, out dequeuedCount) ||
                dequeuedCount != expectedBatchCount)
            {
                throw new InvalidOperationException(
                    $"ORDER.ADMISSION.ERR.IntakeQueueChangedDuringReservation: expected={expectedBatchCount}, actual={dequeuedCount}.");
            }
        }

        private void ClearEntityAdmissionReservations(int batchCount)
        {
            for (int i = 0; i < batchCount; i++)
            {
                _entityAdmissionReservationsScratch[i] = default;
            }
        }

        private void CancelEntityAdmissions(int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                if (_entityAdmissionReservationsScratch[i].IsValid)
                {
                    _admissionResults.Cancel(in _entityAdmissionReservationsScratch[i]);
                    _entityAdmissionReservationsScratch[i] = default;
                }
            }
        }

        private bool PreflightIncomingBatch(int batchCount, int currentStep, out OrderSubmitResult failureResult)
        {
            failureResult = OrderSubmitResult.Queued;
            for (int i = 0; i < batchCount; i++)
            {
                ref Order order = ref _incomingBatchScratch[i];
                order.SubmitStep = currentStep;
                OrderSubmitResult validationResult = ValidateIncomingOrder(in order, out _);
                if (!OrderSubmitResultSemantics.IsAccepted(validationResult))
                {
                    _incomingBatchResultsScratch[i] = validationResult;
                    failureResult = validationResult;
                    return false;
                }

                OrderSubmitResult result = OrderSubmitter.Preview(
                    World,
                    order.Actor,
                    in order,
                    _orderTypeRegistry,
                    _orderRuleRegistry,
                    currentStep,
                    _stepRateHz);
                _incomingBatchResultsScratch[i] = result;
                if (result != OrderSubmitResult.Activated && result != OrderSubmitResult.Queued)
                {
                    failureResult = result;
                    return false;
                }
            }

            return true;
        }

        private OrderSubmitResult ProcessIncomingOrder(ref Order order, int currentStep)
        {
            order.SubmitStep = currentStep;

            OrderSubmitResult validationResult = ValidateIncomingOrder(in order, out OrderTypeConfig config);
            if (!OrderSubmitResultSemantics.IsAccepted(validationResult))
            {
                OrderSpatialPayloadOps.Release(World, in order);
                return validationResult;
            }

            OrderSubmitResult result = OrderSubmitter.Submit(
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
            else if (!OrderSubmitResultSemantics.IsAccepted(result))
            {
                OrderSpatialPayloadOps.Release(World, in order);
            }

            return result;
        }

        private OrderSubmitResult ValidateIncomingOrder(in Order order, out OrderTypeConfig config)
        {
            config = default;
            if (!World.IsAlive(order.Actor) || !World.Has<OrderBuffer>(order.Actor))
            {
                return OrderSubmitResult.RejectedInvalidActor;
            }

            if (!_orderTypeRegistry.TryGet(order.OrderTypeId, out config))
            {
                return OrderSubmitResult.RejectedInvalidOrderType;
            }

            if (config.ValidationGraphId <= 0)
            {
                return OrderSubmitResult.Activated;
            }

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
            return passed ? OrderSubmitResult.Activated : OrderSubmitResult.RejectedValidation;
        }

        private void CommitAdmission(
            in OrderAdmissionReservation reservation,
            in Order order,
            OrderSubmitResult result)
        {
            var outcome = new OrderAdmissionOutcome(order.OrderId, order.OrderTypeId, OrderAdmissionStage.EntityIntake, result);
            _admissionResults.Commit(in reservation, in outcome);
        }

        public OrderSubmitResult SubmitOrder(Entity entity, in Order order)
        {
            if (order.OrderId <= 0)
            {
                throw new InvalidOperationException("ORDER.ADMISSION.ERR.DirectSubmitRequiresOrderId");
            }

            OrderAdmissionReservation reservation;
            try
            {
                reservation = _admissionResults.Reserve(
                    OrderAdmissionStage.EntityIntake,
                    order.OrderId,
                    order.OrderTypeId);
            }
            catch
            {
                OrderSpatialPayloadOps.Release(World, in order);
                throw;
            }

            bool committed = false;
            try
            {
                OrderSubmitResult result = SubmitOrderStateAndReleaseRejected(entity, in order);
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

        private OrderSubmitResult SubmitOrderStateAndReleaseRejected(Entity entity, in Order order)
        {
            OrderSubmitResult result = SubmitOrderState(entity, in order);
            if (!OrderSubmitResultSemantics.IsAccepted(result))
            {
                OrderSpatialPayloadOps.Release(World, in order);
            }
            return result;
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
            if (!OrderSubmitResultSemantics.IsAccepted(result))
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
