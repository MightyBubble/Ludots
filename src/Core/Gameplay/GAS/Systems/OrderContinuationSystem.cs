using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    /// <summary>
    /// Re-submits planned follow-up orders after their trigger order completes.
    /// This keeps move-then-cast and future chained commands generic and queue-safe.
    /// </summary>
    public sealed class OrderContinuationSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<OrderBuffer, OrderContinuationBuffer, OrderTerminalSignal>();

        private readonly IClock _clock;
        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly OrderRuleRegistry _orderRuleRegistry;
        private readonly int _stepRateHz;

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

            foreach (ref var chunk in World.Query(in Query))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var buffers = chunk.GetSpan<OrderBuffer>();
                var continuations = chunk.GetSpan<OrderContinuationBuffer>();
                var signals = chunk.GetSpan<OrderTerminalSignal>();
                foreach (var index in chunk)
                {
                    ref OrderTerminalSignal signal = ref signals[index];
                    ref OrderContinuationBuffer continuation = ref continuations[index];
                    if (signal.OrderId <= 0 || !continuation.HasEntries)
                    {
                        signal = default;
                        continue;
                    }

                    int count;
                    if (signal.State == OrderTerminalState.Completed)
                    {
                        count = continuation.Extract(signal.OrderId, extracted);
                    }
                    else
                    {
                        continuation.RemoveByTrigger(signal.OrderId);
                        count = 0;
                    }
                    signal = default;
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    ref OrderBuffer buffer = ref buffers[index];

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

                        if (result != OrderSubmitResult.RejectedByRule)
                        {
                            continue;
                        }

                        var config = _orderTypeRegistry.Get(order.OrderTypeId);
                        if (config.PendingBufferWindowMs <= 0)
                        {
                            continue;
                        }

                        int expireStep = currentStep + (config.PendingBufferWindowMs * _stepRateHz) / 1000;
                        buffer.SetPending(in order, config.Priority, expireStep, currentStep);
                    }
                }
            }
        }
    }
}
