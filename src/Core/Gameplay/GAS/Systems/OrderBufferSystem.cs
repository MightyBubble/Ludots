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
        private readonly OrderSubmitResult[] _incomingBatchResultsScratch;
        private readonly OrderAdmissionResultBuffer? _admissionResults;

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
            _incomingBatchScratch = incomingOrders != null
                ? new Order[incomingOrders.Capacity]
                : Array.Empty<Order>();
            _incomingBatchResultsScratch = incomingOrders != null
                ? new OrderSubmitResult[incomingOrders.Capacity]
                : Array.Empty<OrderSubmitResult>();
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

            while (_incomingOrders.TryPeekBatchSize(out int nextBatchSize))
            {
                if (_admissionResults != null &&
                    _admissionResults.AvailableCapacity < nextBatchSize)
                {
                    AdmissionBackpressureCount++;
                    break;
                }

                if (!_incomingOrders.TryDequeueBatch(_incomingBatchScratch, out int batchCount))
                {
                    throw new InvalidOperationException(
                        "OrderQueue changed after a successful batch-size peek.");
                }

                if (batchCount == 1 && _incomingBatchScratch[0].AdmissionBatchId == 0)
                {
                    OrderSubmitResult result = ProcessIncomingOrder(ref _incomingBatchScratch[0], currentStep);
                    PublishAdmission(in _incomingBatchScratch[0], result);
                    continue;
                }

                if (!PreflightIncomingBatch(batchCount, currentStep))
                {
                    for (int i = 0; i < batchCount; i++)
                    {
                        OrderSubmitResult result = _incomingBatchResultsScratch[i];
                        if (result == OrderSubmitResult.Activated || result == OrderSubmitResult.Queued)
                        {
                            result = OrderSubmitResult.BatchRejected;
                        }

                        PublishAdmission(in _incomingBatchScratch[i], result);
                    }

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

                    PublishAdmission(in order, result);
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
                OrderSubmitResult validationResult = ValidateIncomingOrder(in order, out _);
                if (validationResult != OrderSubmitResult.Activated)
                {
                    _incomingBatchResultsScratch[i] = validationResult;
                    continue;
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
                    continue;
                }
            }

            for (int i = 0; i < batchCount; i++)
            {
                OrderSubmitResult result = _incomingBatchResultsScratch[i];
                if (result != OrderSubmitResult.Activated && result != OrderSubmitResult.Queued)
                {
                    return false;
                }
            }

            return true;
        }

        private OrderSubmitResult ProcessIncomingOrder(ref Order order, int currentStep)
        {
            IncomingRevision++;
            order.SubmitStep = currentStep;
            OrderSubmitResult validationResult = ValidateIncomingOrder(in order, out OrderTypeConfig config);
            if (validationResult != OrderSubmitResult.Activated)
            {
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

            if (result == OrderSubmitResult.Blocked && config.PendingBufferWindowMs > 0)
            {
                int pendingExpireStep = currentStep + (config.PendingBufferWindowMs * _stepRateHz) / 1000;
                ref OrderBuffer buffer = ref World.Get<OrderBuffer>(order.Actor);
                buffer.SetPending(in order, config.Priority, pendingExpireStep, currentStep);
                return OrderSubmitResult.Pending;
            }

            return result;
        }

        private void PublishAdmission(in Order order, OrderSubmitResult result)
        {
            if (_admissionResults == null)
            {
                return;
            }

            var outcome = new OrderAdmissionOutcome(
                in order,
                OrderAdmissionStage.EntityIntake,
                result);
            if (!_admissionResults.TryWrite(in outcome))
            {
                throw new InvalidOperationException(
                    $"Order admission result capacity {_admissionResults.Capacity} is exhausted.");
            }
        }

        private OrderSubmitResult ValidateIncomingOrder(in Order order, out OrderTypeConfig config)
        {
            config = default;
            if (!World.IsAlive(order.Actor) || !World.Has<OrderBuffer>(order.Actor))
            {
                return OrderSubmitResult.InvalidEntity;
            }

            if (!_orderTypeRegistry.TryGet(order.OrderTypeId, out config))
            {
                return OrderSubmitResult.InvalidOrderType;
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
            return passed
                ? OrderSubmitResult.Activated
                : OrderSubmitResult.ValidationRejected;
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
