using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    /// <summary>
    /// Re-submits planned follow-up orders after their trigger order completes.
    /// This keeps move-then-cast and future chained commands generic and queue-safe.
    /// </summary>
    public sealed class OrderContinuationSystem : BaseSystem<World, float>
    {
        private readonly IClock _clock;
        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly OrderRuleRegistry _orderRuleRegistry;
        private readonly OrderAdmissionResultBuffer _admissionResults;
        private readonly OrderTerminalResultBuffer _terminalResults;
        private readonly int _stepRateHz;
        private uint _processedGeneration;
        private int _processedCount;

        public OrderContinuationSystem(
            World world,
            IClock clock,
            OrderTypeRegistry orderTypeRegistry,
            OrderRuleRegistry orderRuleRegistry,
            OrderAdmissionResultBuffer admissionResults,
            int stepRateHz = 30)
            : base(world)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _orderTypeRegistry = orderTypeRegistry ?? throw new ArgumentNullException(nameof(orderTypeRegistry));
            _orderRuleRegistry = orderRuleRegistry ?? throw new ArgumentNullException(nameof(orderRuleRegistry));
            _admissionResults = admissionResults ?? throw new ArgumentNullException(nameof(admissionResults));
            _terminalResults = orderTypeRegistry.TerminalResults;
            _processedGeneration = _terminalResults.Generation;
            if (stepRateHz <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stepRateHz), stepRateHz, "stepRateHz must be positive.");
            }

            _stepRateHz = stepRateHz;
            world.SubscribeEntityDestroyed(OnEntityDestroyed);
        }

        public override void Update(in float dt)
        {
            int currentStep = _clock.Now(ClockDomainId.Step);
            Span<Order> extracted = stackalloc Order[OrderContinuationBuffer.MAX_CONTINUATIONS];
            Span<OrderAdmissionReservation> reservations = stackalloc OrderAdmissionReservation[OrderContinuationBuffer.MAX_CONTINUATIONS];

            if (_processedGeneration != _terminalResults.Generation)
            {
                _processedGeneration = _terminalResults.Generation;
                _processedCount = 0;
            }

            while (_processedCount < _terminalResults.Count)
            {
                ref readonly OrderTerminalOutcome outcome = ref _terminalResults[_processedCount];

                Entity entity = outcome.Actor;
                if (!World.IsAlive(entity) ||
                    !World.Has<OrderContinuationBuffer>(entity))
                {
                    _processedCount++;
                    continue;
                }

                ref OrderContinuationBuffer continuation = ref World.Get<OrderContinuationBuffer>(entity);
                if (!continuation.HasEntries)
                {
                    _processedCount++;
                    continue;
                }

                int triggerOrderId = outcome.OrderId;
                int matchingCount = continuation.CountByTrigger(triggerOrderId);
                if (matchingCount == 0)
                {
                    _processedCount++;
                    continue;
                }

                if (outcome.State != OrderTerminalState.Completed)
                {
                    RejectContinuationsForTrigger(
                        ref continuation,
                        entity,
                        triggerOrderId,
                        matchingCount,
                        outcome.State == OrderTerminalState.Cancelled
                            ? OrderTerminalState.Cancelled
                            : OrderTerminalState.Failed,
                        outcome.State == OrderTerminalState.Failed && outcome.FailureReason != OrderFailureReason.None
                            ? outcome.FailureReason
                            : OrderFailureReason.PreconditionFailed,
                        OrderSubmitResult.RejectedValidation,
                        extracted,
                        reservations);
                    _processedCount++;
                    continue;
                }

                if (!World.Has<OrderBuffer>(entity))
                {
                    RejectContinuationsForTrigger(
                        ref continuation,
                        entity,
                        triggerOrderId,
                        matchingCount,
                        OrderTerminalState.Failed,
                        OrderFailureReason.SubmissionInvalidActor,
                        OrderSubmitResult.RejectedInvalidActor,
                        extracted,
                        reservations);
                    _processedCount++;
                    continue;
                }

                int count = continuation.CopyByTrigger(triggerOrderId, extracted);
                _orderTypeRegistry.EnsureTerminalResultCapacity(count);
                if (!TryReserveContinuationAdmissions(extracted.Slice(0, count), reservations))
                {
                    int rejectedCount = continuation.Extract(triggerOrderId, extracted);
                    if (rejectedCount != count)
                    {
                        throw new InvalidOperationException(
                            $"ORDER.CONTINUATION.ERR.BufferChangedDuringAdmissionCapacityRejection: triggerOrderId={triggerOrderId}, expected={count}, actual={rejectedCount}.");
                    }

                    PublishAdmissionCapacityRejectedContinuations(entity, extracted.Slice(0, count), count);
                    _processedCount++;
                    continue;
                }

                if (!PreflightContinuationSubmissions(
                    entity,
                    extracted.Slice(0, count),
                    currentStep,
                    out OrderSubmitResult preflightFailure,
                    out int terminalResultCount))
                {
                    int rejectedCount = continuation.Extract(triggerOrderId, extracted);
                    if (rejectedCount != count)
                    {
                        CancelContinuationAdmissions(reservations, count);
                        throw new InvalidOperationException(
                            $"ORDER.CONTINUATION.ERR.BufferChangedDuringPreflightRejection: triggerOrderId={triggerOrderId}, expected={count}, actual={rejectedCount}.");
                    }

                    PublishRejectedContinuations(
                        entity,
                        extracted.Slice(0, count),
                        reservations,
                        count,
                        OrderTerminalState.Failed,
                        OrderSubmitResultSemantics.ToFailureReason(preflightFailure),
                        preflightFailure);
                    _processedCount++;
                    continue;
                }

                if (terminalResultCount > count)
                {
                    _orderTypeRegistry.EnsureTerminalResultCapacity(terminalResultCount);
                }

                int extractedCount = continuation.Extract(triggerOrderId, extracted);
                if (extractedCount != count)
                {
                    CancelContinuationAdmissions(reservations, count);
                    throw new InvalidOperationException(
                        $"ORDER.CONTINUATION.ERR.BufferChangedDuringAdmission: triggerOrderId={triggerOrderId}, expected={count}, actual={extractedCount}.");
                }

                ref OrderBuffer buffer = ref World.Get<OrderBuffer>(entity);

                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        var order = extracted[i];
                        order.Actor = entity;

                        try
                        {
                            OrderSubmitResult result = OrderSubmitter.Submit(
                                World,
                                entity,
                                in order,
                                _orderTypeRegistry,
                                _orderRuleRegistry,
                                currentStep,
                                _stepRateHz);

                            if (result == OrderSubmitResult.RejectedByRule)
                            {
                                var config = _orderTypeRegistry.Get(order.OrderTypeId);
                                if (config.PendingBufferWindowMs > 0)
                                {
                                    int expireStep = currentStep + (config.PendingBufferWindowMs * _stepRateHz) / 1000;
                                    OrderSubmitter.ReplacePending(
                                        World,
                                        ref buffer,
                                        _orderTypeRegistry,
                                        in order,
                                        config.Priority,
                                        expireStep,
                                        currentStep);
                                    CommitAdmission(in reservations[i], in order, OrderSubmitResult.Pending);
                                    reservations[i] = default;
                                    continue;
                                }
                            }

                            CommitAdmission(in reservations[i], in order, result);
                            reservations[i] = default;

                            if (OrderSubmitResultSemantics.IsAccepted(result))
                            {
                                continue;
                            }

                            OrderSpatialPayloadOps.Release(World, in order);
                            var rejected = new OrderTerminalOutcome(
                                order.OrderId,
                                order.OrderTypeId,
                                OrderTerminalState.Failed,
                                OrderSubmitResultSemantics.ToFailureReason(result),
                                entity);
                            _orderTypeRegistry.PublishTerminalResult(in rejected);
                        }
                        catch
                        {
                            RestoreContinuationOwnership(
                                ref continuation,
                                triggerOrderId,
                                extracted.Slice(i, count - i),
                                reservations.Slice(i, count - i));
                            throw;
                        }
                    }
                }
                finally
                {
                    CancelContinuationAdmissions(reservations, count);
                }

                _processedCount++;
            }
        }

        private void OnEntityDestroyed(in Entity entity)
        {
            if (!World.IsAlive(entity) || !World.Has<OrderContinuationBuffer>(entity))
            {
                return;
            }

            ref OrderContinuationBuffer continuation = ref World.Get<OrderContinuationBuffer>(entity);
            if (!continuation.HasEntries)
            {
                return;
            }

            Span<Order> extracted = stackalloc Order[OrderContinuationBuffer.MAX_CONTINUATIONS];
            Span<OrderAdmissionReservation> reservations = stackalloc OrderAdmissionReservation[OrderContinuationBuffer.MAX_CONTINUATIONS];
            int count = continuation.CopyAll(extracted);
            _orderTypeRegistry.EnsureTerminalResultCapacity(count);
            if (!_admissionResults.EntityIntakeOpen)
            {
                int outsideIntakeCount = continuation.ExtractAll(extracted);
                for (int i = 0; i < outsideIntakeCount; i++)
                {
                    Order order = extracted[i];
                    OrderSpatialPayloadOps.Release(World, in order);
                    var terminal = new OrderTerminalOutcome(
                        order.OrderId,
                        order.OrderTypeId,
                        OrderTerminalState.Failed,
                        OrderFailureReason.SubmissionInvalidActor,
                        entity);
                    _orderTypeRegistry.PublishTerminalResult(in terminal);
                }

                return;
            }

            if (!TryReserveContinuationAdmissions(extracted.Slice(0, count), reservations))
            {
                int rejectedCount = continuation.ExtractAll(extracted);
                if (rejectedCount != count)
                {
                    throw new InvalidOperationException(
                        $"ORDER.CONTINUATION.ERR.DestroyedActorBufferChangedDuringAdmissionCapacityRejection: actor={entity.Id}, expected={count}, actual={rejectedCount}.");
                }

                PublishAdmissionCapacityRejectedContinuations(entity, extracted.Slice(0, count), count);
                return;
            }

            int extractedCount = continuation.ExtractAll(extracted);
            if (extractedCount != count)
            {
                CancelContinuationAdmissions(reservations, count);
                throw new InvalidOperationException(
                    $"ORDER.CONTINUATION.ERR.DestroyedActorBufferChangedDuringAdmission: actor={entity.Id}, expected={count}, actual={extractedCount}.");
            }

            PublishRejectedContinuations(
                entity,
                extracted.Slice(0, count),
                reservations,
                count,
                OrderTerminalState.Failed,
                OrderFailureReason.SubmissionInvalidActor,
                OrderSubmitResult.RejectedInvalidActor);
        }

        private bool TryReserveContinuationAdmissions(
            ReadOnlySpan<Order> orders,
            Span<OrderAdmissionReservation> reservations)
        {
            ClearContinuationAdmissions(reservations, orders.Length);
            if (!_admissionResults.CanReserve(OrderAdmissionStage.EntityIntake, orders.Length))
            {
                if (!_admissionResults.CanRecordCapacityFailures(OrderAdmissionStage.EntityIntake, orders.Length))
                {
                    throw new InvalidOperationException(
                        $"{OrderAdmissionResultBuffer.RejectionCapacityExceededError}: stage={OrderAdmissionStage.EntityIntake}, batchCount={orders.Length}, rejectionCapacity={_admissionResults.RejectionCapacity}.");
                }

                return false;
            }

            for (int i = 0; i < orders.Length; i++)
            {
                try
                {
                    reservations[i] = _admissionResults.Reserve(
                        OrderAdmissionStage.EntityIntake,
                        orders[i].OrderId,
                        orders[i].OrderTypeId);
                }
                catch
                {
                    CancelContinuationAdmissions(reservations, i);
                    throw;
                }
            }

            return true;
        }

        private void ClearContinuationAdmissions(Span<OrderAdmissionReservation> reservations, int count)
        {
            for (int i = 0; i < count; i++)
            {
                reservations[i] = default;
            }
        }

        private void CancelContinuationAdmissions(Span<OrderAdmissionReservation> reservations, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                if (reservations[i].IsValid)
                {
                    _admissionResults.Cancel(in reservations[i]);
                    reservations[i] = default;
                }
            }
        }

        private void RestoreContinuationOwnership(
            ref OrderContinuationBuffer continuation,
            int triggerOrderId,
            Span<Order> orders,
            Span<OrderAdmissionReservation> reservations)
        {
            for (int i = 0; i < orders.Length; i++)
            {
                if (reservations[i].IsValid)
                {
                    _admissionResults.Cancel(in reservations[i]);
                    reservations[i] = default;
                }

                if (continuation.TryAdd(triggerOrderId, in orders[i]))
                {
                    continue;
                }

                Order order = orders[i];
                _orderTypeRegistry.EnsureTerminalResultCapacity();
                OrderSpatialPayloadOps.Release(World, in order);
                var failed = new OrderTerminalOutcome(
                    order.OrderId,
                    order.OrderTypeId,
                    OrderTerminalState.Failed,
                    OrderFailureReason.SubmissionQueueFull,
                    order.Actor);
                _orderTypeRegistry.PublishTerminalResult(in failed);
            }
        }

        private void RejectContinuationsForTrigger(
            ref OrderContinuationBuffer continuation,
            Entity entity,
            int triggerOrderId,
            int expectedCount,
            OrderTerminalState terminalState,
            OrderFailureReason failureReason,
            OrderSubmitResult admissionResult,
            Span<Order> extracted,
            Span<OrderAdmissionReservation> reservations)
        {
            _orderTypeRegistry.EnsureTerminalResultCapacity(expectedCount);
            int count = continuation.CopyByTrigger(triggerOrderId, extracted);
            if (!TryReserveContinuationAdmissions(extracted.Slice(0, count), reservations))
            {
                int rejectedCount = continuation.Extract(triggerOrderId, extracted);
                if (rejectedCount != count)
                {
                    throw new InvalidOperationException(
                        $"ORDER.CONTINUATION.ERR.BufferChangedDuringRejectionAdmissionCapacity: triggerOrderId={triggerOrderId}, expected={count}, actual={rejectedCount}.");
                }

                PublishAdmissionCapacityRejectedContinuations(entity, extracted.Slice(0, count), count);
                return;
            }

            int extractedCount = continuation.Extract(triggerOrderId, extracted);
            if (extractedCount != count)
            {
                CancelContinuationAdmissions(reservations, count);
                throw new InvalidOperationException(
                    $"ORDER.CONTINUATION.ERR.BufferChangedDuringRejection: triggerOrderId={triggerOrderId}, expected={count}, actual={extractedCount}.");
            }

            PublishRejectedContinuations(
                entity,
                extracted.Slice(0, count),
                reservations,
                count,
                terminalState,
                failureReason,
                admissionResult);
        }

        private bool PreflightContinuationSubmissions(
            Entity entity,
            ReadOnlySpan<Order> orders,
            int currentStep,
            out OrderSubmitResult failureResult,
            out int terminalResultCount)
        {
            failureResult = OrderSubmitResult.Queued;
            terminalResultCount = 0;
            if (!World.IsAlive(entity) || !World.Has<OrderBuffer>(entity))
            {
                failureResult = OrderSubmitResult.RejectedInvalidActor;
                return false;
            }

            OrderBuffer projectedBuffer = World.Get<OrderBuffer>(entity);
            bool projectedHasPending = projectedBuffer.HasPending;
            for (int i = 0; i < orders.Length; i++)
            {
                Order order = orders[i];
                order.Actor = entity;
                order.SubmitStep = currentStep;
                if (!_orderTypeRegistry.TryGet(order.OrderTypeId, out OrderTypeConfig config))
                {
                    failureResult = OrderSubmitResult.RejectedInvalidOrderType;
                    return false;
                }

                if (order.SubmitMode == OrderSubmitMode.Queued)
                {
                    if (!config.AllowQueuedMode ||
                        projectedBuffer.QueuedCount >= config.QueuedModeMaxSize ||
                        projectedBuffer.QueuedCount >= OrderBuffer.MAX_QUEUED_ORDERS)
                    {
                        failureResult = config.AllowQueuedMode
                            ? OrderSubmitResult.RejectedQueueFull
                            : OrderSubmitResult.RejectedByRule;
                        return false;
                    }

                    projectedBuffer.Enqueue(in order, config.Priority, expireStep: -1, insertStep: currentStep);
                    continue;
                }

                OrderSubmitResult previewResult = OrderSubmitter.Preview(
                    World,
                    entity,
                    in order,
                    _orderTypeRegistry,
                    _orderRuleRegistry,
                    currentStep,
                    _stepRateHz);
                if (previewResult == OrderSubmitResult.RejectedByRule && config.PendingBufferWindowMs > 0)
                {
                    if (projectedHasPending)
                    {
                        terminalResultCount++;
                    }

                    projectedHasPending = true;
                    continue;
                }

                if (!OrderSubmitResultSemantics.IsAccepted(previewResult))
                {
                    failureResult = previewResult;
                    return false;
                }

                terminalResultCount += OrderSubmitter.CountTerminalResultsRequiredForSubmit(
                    World,
                    entity,
                    in order,
                    _orderTypeRegistry,
                    _orderRuleRegistry,
                    currentStep,
                    _stepRateHz);
            }

            return true;
        }

        private void PublishAdmissionCapacityRejectedContinuations(
            Entity entity,
            ReadOnlySpan<Order> orders,
            int count)
        {
            _admissionResults.RecordCapacityFailures(orders.Slice(0, count), OrderAdmissionStage.EntityIntake);
            for (int i = 0; i < count; i++)
            {
                Order order = orders[i];
                OrderSpatialPayloadOps.Release(World, in order);
                var terminal = new OrderTerminalOutcome(
                    order.OrderId,
                    order.OrderTypeId,
                    OrderTerminalState.Failed,
                    OrderFailureReason.SubmissionAdmissionCapacity,
                    entity);
                _orderTypeRegistry.PublishTerminalResult(in terminal);
            }
        }

        private void PublishRejectedContinuations(
            Entity entity,
            ReadOnlySpan<Order> orders,
            Span<OrderAdmissionReservation> reservations,
            int count,
            OrderTerminalState terminalState,
            OrderFailureReason failureReason,
            OrderSubmitResult admissionResult)
        {
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Order order = orders[i];
                    OrderSpatialPayloadOps.Release(World, in order);
                    CommitAdmission(in reservations[i], in order, admissionResult);
                    reservations[i] = default;
                    var terminal = new OrderTerminalOutcome(
                        order.OrderId,
                        order.OrderTypeId,
                        terminalState,
                        terminalState == OrderTerminalState.Failed ? failureReason : OrderFailureReason.None,
                        entity);
                    _orderTypeRegistry.PublishTerminalResult(in terminal);
                }
            }
            finally
            {
                CancelContinuationAdmissions(reservations, count);
            }
        }

        private void CommitAdmission(
            in OrderAdmissionReservation reservation,
            in Order order,
            OrderSubmitResult result)
        {
            var outcome = new OrderAdmissionOutcome(order.OrderId, order.OrderTypeId, OrderAdmissionStage.EntityIntake, result);
            _admissionResults.Commit(in reservation, in outcome);
        }
    }
}
