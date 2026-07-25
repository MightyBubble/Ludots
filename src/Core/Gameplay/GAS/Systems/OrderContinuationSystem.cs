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
                    !World.Has<OrderBuffer>(entity) ||
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

                int count;
                int triggerOrderId = outcome.OrderId;
                if (outcome.State == OrderTerminalState.Completed)
                {
                    int matchingCount = continuation.CountByTrigger(triggerOrderId);
                    _orderTypeRegistry.EnsureTerminalResultCapacity(matchingCount);
                    count = continuation.CopyByTrigger(triggerOrderId, extracted);
                    ReserveContinuationAdmissions(extracted.Slice(0, count), reservations);
                    int extractedCount = continuation.Extract(triggerOrderId, extracted);
                    if (extractedCount != count)
                    {
                        CancelContinuationAdmissions(reservations, count);
                        throw new InvalidOperationException(
                            $"ORDER.CONTINUATION.ERR.BufferChangedDuringAdmission: triggerOrderId={triggerOrderId}, expected={count}, actual={extractedCount}.");
                    }
                }
                else
                {
                    count = continuation.Extract(triggerOrderId, extracted);
                }

                if (outcome.State != OrderTerminalState.Completed)
                {
                    for (int i = 0; i < count; i++)
                    {
                        Order removed = extracted[i];
                        OrderSpatialPayloadOps.Release(World, in removed);
                    }
                    _processedCount++;
                    continue;
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

        private void ReserveContinuationAdmissions(
            ReadOnlySpan<Order> orders,
            Span<OrderAdmissionReservation> reservations)
        {
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
