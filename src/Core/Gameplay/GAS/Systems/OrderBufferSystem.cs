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
        private readonly Order[] _incomingBatchScratch;

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
            _incomingBatchScratch = incomingOrders != null
                ? new Order[incomingOrders.Capacity]
                : Array.Empty<Order>();
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

            foreach (ref var chunk in World.Query(in _orderBufferQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var buffers = chunk.GetSpan<OrderBuffer>();
                foreach (var index in chunk)
                {
                    ref OrderBuffer buffer = ref buffers[index];
                    if (!buffer.HasActive && buffer.HasQueued)
                    {
                        Entity entity = Unsafe.Add(ref entityFirst, index);
                        OrderSubmitter.TryPromoteNextQueuedToActive(World, entity, ref buffer, _orderTypeRegistry);
                    }
                }
            }
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

            while (_incomingOrders.TryDequeueBatch(_incomingBatchScratch, out int batchCount))
            {
                if (batchCount == 1 && _incomingBatchScratch[0].AdmissionBatchId == 0)
                {
                    ProcessIncomingOrder(ref _incomingBatchScratch[0], currentStep);
                    continue;
                }

                if (!PreflightIncomingBatch(batchCount, currentStep))
                {
                    continue;
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

                    IncomingRevision++;
                }
            }
        }

        private bool PreflightIncomingBatch(int batchCount, int currentStep)
        {
            for (int i = 0; i < batchCount; i++)
            {
                ref Order order = ref _incomingBatchScratch[i];
                order.SubmitStep = currentStep;
                if (!ValidateIncomingOrder(in order))
                {
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
                if (result != OrderSubmitResult.Activated && result != OrderSubmitResult.Queued)
                {
                    return false;
                }
            }

            return true;
        }

        private void ProcessIncomingOrder(ref Order order, int currentStep)
        {
            IncomingRevision++;
            order.SubmitStep = currentStep;
            if (!ValidateIncomingOrder(in order))
            {
                return;
            }

            OrderTypeConfig config = _orderTypeRegistry.Get(order.OrderTypeId);
            OrderSubmitResult result = OrderSubmitter.Submit(
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
                ref OrderBuffer buffer = ref World.Get<OrderBuffer>(order.Actor);
                buffer.SetPending(in order, config.Priority, pendingExpireStep, currentStep);
            }
        }

        private bool ValidateIncomingOrder(in Order order)
        {
            if (!World.IsAlive(order.Actor) || !World.Has<OrderBuffer>(order.Actor))
            {
                return false;
            }

            OrderTypeConfig config = _orderTypeRegistry.Get(order.OrderTypeId);
            if (config.ValidationGraphId <= 0)
            {
                return true;
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
            return GasGraphExecutor.ExecuteValidation(
                World,
                order.Actor,
                order.Target,
                targetPos,
                validationProgram,
                _graphApi);
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

