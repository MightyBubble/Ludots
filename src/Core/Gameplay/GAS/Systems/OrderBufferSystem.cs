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
        private readonly bool _closeEntityIntakeOnUpdate;

        private readonly GraphProgramRegistry? _graphProgramRegistry;
        private readonly IGraphRuntimeApi? _graphApi;

        public uint IncomingRevision { get; private set; }
        public long AdmissionBackpressureCount { get; private set; }

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
            IGraphRuntimeApi? graphApi = null,
            bool closeEntityIntakeOnUpdate = true)
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
            _closeEntityIntakeOnUpdate = closeEntityIntakeOnUpdate;
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
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    if (buffer.HasActive &&
                        buffer.ActiveOrder.AdmissionActivationPublished == 0 &&
                        buffer.ActiveOrder.Order.AdmissionBatchId > 0 &&
                        !HasAdmissionCapacity(in buffer.ActiveOrder.Order, 1))
                    {
                        AdmissionBackpressureCount++;
                        continue;
                    }

                    PublishUnreportedActivation(entity, ref buffer);
                    ReleaseExpiredOrders(ref buffer, currentStep);
                    if (!buffer.HasActive && buffer.HasQueued)
                    {
                        Order next = buffer.GetQueued(0).Order;
                        if (!HasAdmissionCapacity(in next, 1))
                        {
                            AdmissionBackpressureCount++;
                            continue;
                        }

                        bool promoted = OrderSubmitter.TryPromoteNextQueuedToActive(
                            World,
                            entity,
                            ref buffer,
                            _orderTypeRegistry,
                            out OrderSubmitResult failureResult,
                            out int failedOrderId,
                            out int failedOrderTypeId);
                        if (!promoted &&
                            failedOrderId > 0 &&
                            failureResult != OrderSubmitResult.Activated)
                        {
                            CommitPromotionFailureAdmission(failedOrderId, failedOrderTypeId, failureResult);
                        }
                        else if (promoted)
                        {
                            PublishUnreportedActivation(entity, ref buffer);
                        }
                    }
                }
            }

            if (_closeEntityIntakeOnUpdate)
            {
                _admissionResults.EndEntityIntake();
            }
        }

        private void ReleaseExpiredOrders(ref OrderBuffer buffer, int currentStep)
        {
            int terminalResultCount = 0;
            for (int i = buffer.QueuedCount - 1; i >= 0; i--)
            {
                QueuedOrder queued = buffer.GetQueued(i);
                if (queued.ExpireStep >= 0 && queued.ExpireStep <= currentStep)
                {
                    terminalResultCount++;
                }
            }

            if (buffer.HasPending &&
                buffer.PendingOrder.ExpireStep >= 0 &&
                buffer.PendingOrder.ExpireStep <= currentStep)
            {
                terminalResultCount++;
            }

            if (terminalResultCount > 0)
            {
                _orderTypeRegistry.EnsureTerminalResultCapacity(terminalResultCount);
            }

            for (int i = buffer.QueuedCount - 1; i >= 0; i--)
            {
                QueuedOrder queued = buffer.GetQueued(i);
                if (queued.ExpireStep < 0 || queued.ExpireStep > currentStep)
                {
                    continue;
                }

                OrderSubmitter.ReleaseQueuedOrderAt(
                    World,
                    ref buffer,
                    _orderTypeRegistry,
                    i,
                    OrderTerminalState.Cancelled,
                    OrderFailureReason.None);
            }

            if (buffer.HasPending &&
                buffer.PendingOrder.ExpireStep >= 0 &&
                buffer.PendingOrder.ExpireStep <= currentStep)
            {
                OrderSubmitter.ReleasePendingOrder(World, ref buffer, _orderTypeRegistry);
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
                // Process before dequeue so an unexpected submit failure keeps GlobalIntake ownership
                // on the still-queued order instead of silently cancelling the reservation.
                OrderSubmitResult result = ProcessIncomingOrder(ref order, currentStep);
                if (!OrderSubmitResultSemantics.IsAccepted(result))
                {
                    // GlobalIntake already Queued this order; EntityIntake rejection must emit a Failed
                    // terminal so OrderContinuationSystem can release follow-ups keyed by this order id.
                    EnsureAndPublishFailedTerminal(in order, result);
                }

                DequeueReservedBatch(expectedBatchCount: 1);
                CommitAdmission(in reservation, in order, result);
                committed = true;
                if (OrderSubmitResultSemantics.IsAccepted(result))
                {
                    IncomingRevision++;
                    MarkActiveAdmissionPublished(order.Actor, order.OrderId, result);
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
            InitializeIncomingBatchScratch(batchCount);
            if (!_admissionResults.CanReserve(OrderAdmissionStage.EntityIntake, batchCount))
            {
                if (!_admissionResults.CanRecordCapacityFailures(OrderAdmissionStage.EntityIntake, batchCount))
                {
                    throw new InvalidOperationException(
                        $"{OrderAdmissionResultBuffer.RejectionCapacityExceededError}: stage={OrderAdmissionStage.EntityIntake}, batchCount={batchCount}, rejectionCapacity={_admissionResults.RejectionCapacity}.");
                }

                DequeueReservedBatch(batchCount);
                _admissionResults.RecordCapacityFailures(
                    _incomingBatchScratch.AsSpan(0, batchCount),
                    OrderAdmissionStage.EntityIntake);
                _orderTypeRegistry.EnsureTerminalResultCapacity(batchCount);
                for (int i = 0; i < batchCount; i++)
                {
                    OrderSpatialPayloadOps.Release(World, in _incomingBatchScratch[i]);
                    PublishFailedTerminal(
                        in _incomingBatchScratch[i],
                        OrderSubmitResult.RejectedAdmissionCapacity);
                }

                return;
            }

            ReserveEntityAdmissions(batchCount);
            bool committed = false;
            bool dequeued = false;
            try
            {
                bool accepted = PreflightIncomingBatch(batchCount, currentStep, out OrderSubmitResult failureResult);
                if (!accepted)
                {
                    DequeueReservedBatch(batchCount);
                    dequeued = true;
                    _orderTypeRegistry.EnsureTerminalResultCapacity(batchCount);
                    for (int i = 0; i < batchCount; i++)
                    {
                        if (OrderSubmitResultSemantics.IsAccepted(_incomingBatchResultsScratch[i]))
                        {
                            _incomingBatchResultsScratch[i] = failureResult;
                        }

                        OrderSpatialPayloadOps.Release(World, in _incomingBatchScratch[i]);
                        PublishFailedTerminal(in _incomingBatchScratch[i], _incomingBatchResultsScratch[i]);
                        CommitAdmission(
                            in _entityAdmissionReservationsScratch[i],
                            in _incomingBatchScratch[i],
                            _incomingBatchResultsScratch[i]);
                    }

                    committed = true;
                    ClearEntityAdmissionReservations(batchCount);
                    return;
                }

                DequeueReservedBatch(batchCount);
                dequeued = true;
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
                        _stepRateHz,
                        _admissionResults);
                    if (result != OrderSubmitResult.Activated && result != OrderSubmitResult.Queued)
                    {
                        throw new InvalidOperationException(
                            $"Order admission batch {order.AdmissionBatchId} changed after successful preflight: row {i} returned {result}.");
                    }

                    MarkActiveAdmissionPublished(order.Actor, order.OrderId, result);
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
                    if (dequeued)
                    {
                        for (int i = 0; i < batchCount; i++)
                        {
                            OrderSpatialPayloadOps.Release(World, in _incomingBatchScratch[i]);
                        }
                    }
                }
            }
        }

        private void InitializeIncomingBatchScratch(int batchCount)
        {
            for (int i = 0; i < batchCount; i++)
            {
                _incomingBatchResultsScratch[i] = OrderSubmitResult.Queued;
                _entityAdmissionReservationsScratch[i] = default;
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
            int terminalResultCount = 0;
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

                terminalResultCount += OrderSubmitter.CountTerminalResultsRequiredForSubmit(
                    World,
                    order.Actor,
                    in order,
                    _orderTypeRegistry,
                    _orderRuleRegistry,
                    currentStep,
                    _stepRateHz);
            }

            if (terminalResultCount > 0)
            {
                _orderTypeRegistry.EnsureTerminalResultCapacity(terminalResultCount);
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
                _stepRateHz,
                _admissionResults);

            if (result == OrderSubmitResult.RejectedByRule && config.PendingBufferWindowMs > 0)
            {
                int pendingExpireStep = currentStep + (config.PendingBufferWindowMs * _stepRateHz) / 1000;
                ref var buffer = ref World.Get<OrderBuffer>(order.Actor);
                OrderSubmitter.ReplacePending(
                    World,
                    ref buffer,
                    _orderTypeRegistry,
                    in order,
                    config.Priority,
                    pendingExpireStep,
                    currentStep);
                result = OrderSubmitResult.Pending;
            }
            else if (!OrderSubmitResultSemantics.IsAccepted(result))
            {
                OrderSpatialPayloadOps.Release(World, in order);
            }

            return result;
        }

        private void EnsureAndPublishFailedTerminal(in Order order, OrderSubmitResult result)
        {
            _orderTypeRegistry.EnsureTerminalResultCapacity();
            PublishFailedTerminal(in order, result);
        }

        private void PublishFailedTerminal(in Order order, OrderSubmitResult result)
        {
            if (OrderSubmitResultSemantics.IsAccepted(result))
            {
                throw new InvalidOperationException(
                    $"ORDER.ENTITY_INTAKE.ERR.FailedTerminalRequiresRejection: orderId={order.OrderId}, result={result}.");
            }

            var failed = new OrderTerminalOutcome(
                order.OrderId,
                order.OrderTypeId,
                OrderTerminalState.Failed,
                OrderSubmitResultSemantics.ToFailureReason(result),
                order.Actor);
            _orderTypeRegistry.PublishTerminalResult(in failed);
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
            var outcome = new OrderAdmissionOutcome(in order, OrderAdmissionStage.EntityIntake, result);
            _admissionResults.Commit(in reservation, in outcome);
        }

        private void CommitPromotionFailureAdmission(int orderId, int orderTypeId, OrderSubmitResult result)
        {
            OrderAdmissionReservation reservation = _admissionResults.Reserve(
                OrderAdmissionStage.EntityIntake,
                orderId,
                orderTypeId);
            var outcome = new OrderAdmissionOutcome(orderId, orderTypeId, OrderAdmissionStage.EntityIntake, result);
            _admissionResults.Commit(in reservation, in outcome);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool RequiresAdmissionFeedback(in Order order) =>
            OrderAdmissionTracking.RequiresNetworkFeedback(in order);

        private bool HasAdmissionCapacity(in Order order, int required)
        {
            if (!RequiresAdmissionFeedback(in order))
            {
                return true;
            }

            return _admissionResults.AvailableCapacity >= required;
        }

        private void PublishUnreportedActivation(Entity entity, ref OrderBuffer buffer)
        {
            if (!buffer.HasActive ||
                buffer.ActiveOrder.AdmissionActivationPublished != 0 ||
                !RequiresAdmissionFeedback(in buffer.ActiveOrder.Order))
            {
                return;
            }

            Order order = buffer.ActiveOrder.Order;
            if (!HasAdmissionCapacity(in order, 1))
            {
                AdmissionBackpressureCount++;
                return;
            }

            OrderAdmissionReservation reservation = _admissionResults.Reserve(
                OrderAdmissionStage.EntityIntake,
                order.OrderId,
                order.OrderTypeId);
            try
            {
                var outcome = new OrderAdmissionOutcome(
                    in order,
                    OrderAdmissionStage.EntityIntake,
                    OrderSubmitResult.Activated);
                _admissionResults.Commit(in reservation, in outcome);
                buffer.ActiveOrder.AdmissionActivationPublished = 1;
            }
            catch
            {
                if (reservation.IsValid)
                {
                    _admissionResults.Cancel(in reservation);
                }

                throw;
            }
        }

        private void MarkActiveAdmissionPublished(
            Entity entity,
            int orderId,
            OrderSubmitResult result)
        {
            if (result != OrderSubmitResult.Activated ||
                !World.IsAlive(entity) ||
                !World.Has<OrderBuffer>(entity))
            {
                return;
            }

            ref OrderBuffer buffer = ref World.Get<OrderBuffer>(entity);
            if (!buffer.HasActive || buffer.ActiveOrder.Order.OrderId != orderId)
            {
                return;
            }

            if (RequiresAdmissionFeedback(in buffer.ActiveOrder.Order))
            {
                buffer.ActiveOrder.AdmissionActivationPublished = 0;
                return;
            }

            buffer.ActiveOrder.AdmissionActivationPublished = 1;
        }

        public bool TryCancelAll(Entity entity)
        {
            if (!World.IsAlive(entity) || !World.Has<OrderBuffer>(entity))
            {
                return false;
            }

            ref OrderBuffer buffer = ref World.Get<OrderBuffer>(entity);
            int correlatedWaiting = 0;
            if (buffer.HasPending && RequiresAdmissionFeedback(in buffer.PendingOrder.Order))
            {
                correlatedWaiting++;
            }

            for (int i = 0; i < buffer.QueuedCount; i++)
            {
                Order queued = buffer.GetQueued(i).Order;
                if (RequiresAdmissionFeedback(in queued))
                {
                    correlatedWaiting++;
                }
            }

            if (correlatedWaiting > 0 && _admissionResults.AvailableCapacity < correlatedWaiting)
            {
                AdmissionBackpressureCount++;
                return false;
            }

            OrderSubmitter.CancelAll(World, entity, _orderTypeRegistry, _admissionResults);
            return true;
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
                _stepRateHz,
                _admissionResults);
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
            int currentStep = _clock.Now(ClockDomainId.Step);
            OrderSubmitResult previewResult = OrderSubmitter.Preview(
                World,
                entity,
                in pendingOrder,
                _orderTypeRegistry,
                _orderRuleRegistry,
                currentStep,
                _stepRateHz);

            OrderAdmissionReservation reservation = _admissionResults.Reserve(
                OrderAdmissionStage.EntityIntake,
                pendingOrder.OrderId,
                pendingOrder.OrderTypeId);
            bool committed = false;
            try
            {
                if (!OrderSubmitResultSemantics.IsAccepted(previewResult))
                {
                    _orderTypeRegistry.EnsureTerminalResultCapacity();
                }

                OrderSubmitResult result = OrderSubmitter.Submit(
                    World,
                    entity,
                    in pendingOrder,
                    _orderTypeRegistry,
                    _orderRuleRegistry,
                    currentStep,
                    _stepRateHz,
                    _admissionResults);
                if (result != previewResult)
                {
                    throw new InvalidOperationException(
                        $"ORDER.PENDING.ERR.PreviewChanged: orderId={pendingOrder.OrderId}, preview={previewResult}, result={result}.");
                }

                buffer.ClearPendingTransferred();
                if (!OrderSubmitResultSemantics.IsAccepted(result))
                {
                    OrderSpatialPayloadOps.Release(World, in pendingOrder);
                    var failed = new OrderTerminalOutcome(
                        pendingOrder.OrderId,
                        pendingOrder.OrderTypeId,
                        OrderTerminalState.Failed,
                        OrderSubmitResultSemantics.ToFailureReason(result),
                        entity);
                    _orderTypeRegistry.PublishTerminalResult(in failed);
                }

                CommitAdmission(in reservation, in pendingOrder, result);
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    _admissionResults.Cancel(in reservation);
                }
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
