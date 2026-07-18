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
        private readonly OrderTerminalResultBuffer _terminalResults;
        private readonly int _stepRateHz;
        private uint _processedGeneration;
        private int _processedCount;

        public OrderContinuationSystem(
            World world,
            IClock clock,
            OrderTypeRegistry orderTypeRegistry,
            OrderRuleRegistry orderRuleRegistry,
            int stepRateHz = 30)
            : base(world)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _orderTypeRegistry = orderTypeRegistry ?? throw new ArgumentNullException(nameof(orderTypeRegistry));
            _orderRuleRegistry = orderRuleRegistry ?? throw new ArgumentNullException(nameof(orderRuleRegistry));
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
                if (outcome.State == OrderTerminalState.Completed)
                {
                    int matchingCount = continuation.CountByTrigger(outcome.OrderId);
                    _orderTypeRegistry.EnsureTerminalResultCapacity(matchingCount);
                    count = continuation.Extract(outcome.OrderId, extracted);
                }
                else
                {
                    count = continuation.Extract(outcome.OrderId, extracted);
                }

                _processedCount++;

                if (outcome.State != OrderTerminalState.Completed)
                {
                    for (int i = 0; i < count; i++)
                    {
                        Order removed = extracted[i];
                        OrderSpatialPayloadOps.Release(World, in removed);
                    }
                    continue;
                }

                ref OrderBuffer buffer = ref World.Get<OrderBuffer>(entity);

                for (int i = 0; i < count; i++)
                {
                    var order = extracted[i];
                    order.Actor = entity;

                    var result = OrderSubmitter.Submit(
                        World,
                        entity,
                        in order,
                        _orderTypeRegistry,
                        _orderRuleRegistry,
                        currentStep,
                        _stepRateHz);

                    if (OrderSubmitResultSemantics.IsAccepted(result))
                    {
                        continue;
                    }

                    if (result == OrderSubmitResult.RejectedByRule)
                    {
                        var config = _orderTypeRegistry.Get(order.OrderTypeId);
                        if (config.PendingBufferWindowMs > 0)
                        {
                            int expireStep = currentStep + (config.PendingBufferWindowMs * _stepRateHz) / 1000;
                            OrderSubmitter.ReplacePending(World, ref buffer, in order, config.Priority, expireStep, currentStep);
                            continue;
                        }
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
            }
        }
    }
}
