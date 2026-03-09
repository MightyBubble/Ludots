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
            IGraphRuntimeApi? graphApi = null)
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
        }

        public override void Update(in float dt)
        {
            int currentStep = _clock.Now(ClockDomainId.Step);
            ProcessIncomingOrders(currentStep);

            var job = new OrderBufferUpdateJob
            {
                CurrentStep = currentStep
            };
            World.InlineQuery<OrderBufferUpdateJob, OrderBuffer>(in _orderBufferQuery, ref job);

            World.Query(in _orderBufferQuery, (Entity entity, ref OrderBuffer buffer) =>
            {
                if (!buffer.HasActive && buffer.HasQueued)
                {
                    OrderSubmitter.TryPromoteNextQueuedToActive(World, entity, ref buffer, _orderTypeRegistry);
                }
            });
        }

        private struct OrderBufferUpdateJob : IForEach<OrderBuffer>
        {
            public int CurrentStep;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref OrderBuffer buffer)
            {
                buffer.RemoveExpired(CurrentStep);
                buffer.ExpirePending(CurrentStep);
            }
        }

        private void ProcessIncomingOrders(int currentStep)
        {
            if (_incomingOrders == null) return;

            while (_incomingOrders.TryDequeue(out var order))
            {
                order.SubmitStep = currentStep;

                if (!World.IsAlive(order.Actor) || !World.Has<OrderBuffer>(order.Actor))
                {
                    continue;
                }

                var config = _orderTypeRegistry.Get(order.OrderTypeId);
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
                    if (!passed) continue;
                }

                var result = OrderSubmitter.Submit(
                    World,
                    order.Actor,
                    in order,
                    _orderTypeRegistry,
                    _orderRuleRegistry,
                    currentStep,
                    _stepRateHz);

                if (result == OrderSubmitResult.Blocked && config.PendingBufferWindowMs > 0)
                {
                    int pendingExpireStep = currentStep + (config.PendingBufferWindowMs * _stepRateHz) / 1000;
                    ref var buffer = ref World.Get<OrderBuffer>(order.Actor);
                    buffer.SetPending(in order, config.Priority, pendingExpireStep, currentStep);
                }
            }
        }

        public OrderSubmitResult SubmitOrder(Entity entity, in Order order)
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
            buffer.ClearPending();

            int currentStep = _clock.Now(ClockDomainId.Step);
            OrderSubmitter.Submit(
                World,
                entity,
                in pendingOrder,
                _orderTypeRegistry,
                _orderRuleRegistry,
                currentStep,
                _stepRateHz);
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



