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
            MaintainExistingOrders(currentStep);
            ProcessIncomingOrders(currentStep);
        }

        private void MaintainExistingOrders(int currentStep)
        {
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

                    for (int queuedIndex = buffer.QueuedCount - 1; queuedIndex >= 0; queuedIndex--)
                    {
                        QueuedOrder queued = buffer.GetQueued(queuedIndex);
                        if (queued.ExpireStep < 0 || queued.ExpireStep > currentStep)
                        {
                            continue;
                        }

                        if (!HasAdmissionCapacity(in queued.Order, 1))
                        {
                            AdmissionBackpressureCount++;
                            continue;
                        }

                        PublishAdmission(in queued.Order, OrderSubmitResult.Expired);
                        buffer.RemoveAt(queuedIndex);
                    }

                    if (buffer.HasPending &&
                        buffer.PendingOrder.ExpireStep >= 0 &&
                        buffer.PendingOrder.ExpireStep <= currentStep)
                    {
                        Order pending = buffer.PendingOrder.Order;
                        if (HasAdmissionCapacity(in pending, 1))
                        {
                            PublishAdmission(in pending, OrderSubmitResult.Expired);
                            buffer.ClearPending();
                        }
                        else
                        {
                            AdmissionBackpressureCount++;
                        }
                    }

                    if (!buffer.HasActive && buffer.HasQueued)
                    {
                        Order next = buffer.GetQueued(0).Order;
                        if (!HasAdmissionCapacity(in next, 1))
                        {
                            AdmissionBackpressureCount++;
                            continue;
                        }

                        if (OrderSubmitter.TryPromoteNextQueuedToActive(
                                World,
                                entity,
                                ref buffer,
                                _orderTypeRegistry))
                        {
                            PublishUnreportedActivation(entity, ref buffer);
                        }
                    }

                    if (!buffer.HasActive && !buffer.HasQueued && buffer.HasPending)
                    {
                        TrySubmitPending(entity, ref buffer, currentStep);
                    }
                }
            }
        }

        private void ProcessIncomingOrders(int currentStep)
        {
            if (_incomingOrders == null) return;

            while (_incomingOrders.TryPeekBatch(_incomingBatchScratch, out int nextBatchSize))
            {
                bool isAtomicBatch = nextBatchSize != 1 || _incomingBatchScratch[0].AdmissionBatchId != 0;
                bool requiresAdmissionFeedback = RequiresAdmissionFeedback(in _incomingBatchScratch[0]);
                bool preflightSucceeded = !isAtomicBatch || PreflightIncomingBatch(nextBatchSize, currentStep);
                int requiredResults = _admissionResults == null || !requiresAdmissionFeedback ? 0 : nextBatchSize;
                if (_admissionResults != null && requiresAdmissionFeedback && preflightSucceeded)
                {
                    for (int i = 0; i < nextBatchSize; i++)
                    {
                        requiredResults += CountCorrelatedDisplacements(
                            in _incomingBatchScratch[i],
                            currentStep);
                    }
                }

                if (_admissionResults != null && _admissionResults.AvailableCapacity < requiredResults)
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
                    MarkActiveAdmissionPublished(
                        _incomingBatchScratch[0].Actor,
                        _incomingBatchScratch[0].OrderId,
                        result);
                    PublishAdmission(in _incomingBatchScratch[0], result);
                    continue;
                }

                if (!preflightSucceeded)
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
                        _stepRateHz,
                        _admissionResults);
                    if (result != OrderSubmitResult.Activated && result != OrderSubmitResult.Queued)
                    {
                        throw new InvalidOperationException(
                            $"Order admission batch {order.AdmissionBatchId} changed after successful preflight: row {i} returned {result}.");
                    }

                    MarkActiveAdmissionPublished(order.Actor, order.OrderId, result);
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
                _stepRateHz,
                _admissionResults);

            if (result == OrderSubmitResult.Blocked && config.PendingBufferWindowMs > 0)
            {
                int pendingExpireStep = currentStep + (config.PendingBufferWindowMs * _stepRateHz) / 1000;
                ref OrderBuffer buffer = ref World.Get<OrderBuffer>(order.Actor);
                if (buffer.HasPending)
                {
                    Order displaced = buffer.PendingOrder.Order;
                    PublishAdmission(in displaced, OrderSubmitResult.Cancelled);
                }

                buffer.SetPending(in order, config.Priority, pendingExpireStep, currentStep);
                return OrderSubmitResult.Pending;
            }

            return result;
        }

        private void PublishAdmission(in Order order, OrderSubmitResult result)
        {
            if (_admissionResults == null || !RequiresAdmissionFeedback(in order))
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

        private bool HasAdmissionCapacity(in Order order, int required)
        {
            if (!RequiresAdmissionFeedback(in order))
            {
                return true;
            }

            if (_admissionResults == null)
            {
                return false;
            }

            return _admissionResults.AvailableCapacity >= required;
        }

        private int CountCorrelatedDisplacements(in Order order, int currentStep)
        {
            if (!World.IsAlive(order.Actor) || !World.Has<OrderBuffer>(order.Actor))
            {
                return 0;
            }

            if (!_orderTypeRegistry.IsRegistered(order.OrderTypeId))
            {
                return 0;
            }

            Order candidate = order;
            candidate.SubmitStep = currentStep;
            if (ValidateIncomingOrder(in candidate, out OrderTypeConfig config) != OrderSubmitResult.Activated)
            {
                return 0;
            }

            OrderBuffer before = World.Get<OrderBuffer>(order.Actor);
            OrderSubmitResult previewResult = OrderSubmitter.Preview(
                World,
                order.Actor,
                in candidate,
                _orderTypeRegistry,
                _orderRuleRegistry,
                currentStep,
                _stepRateHz,
                out OrderBuffer after);
            if (previewResult == OrderSubmitResult.Blocked &&
                config.PendingBufferWindowMs > 0)
            {
                return before.HasPending ? 1 : 0;
            }

            return previewResult is OrderSubmitResult.Activated or OrderSubmitResult.Queued
                ? CountRemovedCorrelated(in before, in after)
                : 0;
        }

        private static int CountRemovedCorrelated(in OrderBuffer before, in OrderBuffer after)
            => OrderAdmissionTracking.CountRemovedWaiting(in before, in after);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool RequiresAdmissionFeedback(in Order order) =>
            OrderAdmissionTracking.RequiresNetworkFeedback(in order);

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

            if (correlatedWaiting > 0 &&
                (_admissionResults == null || _admissionResults.AvailableCapacity < correlatedWaiting))
            {
                AdmissionBackpressureCount++;
                return false;
            }

            OrderSubmitter.CancelAll(World, entity, _orderTypeRegistry, _admissionResults);
            return true;
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
            PublishAdmission(in order, OrderSubmitResult.Activated);
            buffer.ActiveOrder.AdmissionActivationPublished = 1;
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
                throw new InvalidOperationException(
                    $"Activated order {orderId} is not the actor's active order.");
            }

            buffer.ActiveOrder.AdmissionActivationPublished = 1;
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
                _stepRateHz,
                _admissionResults);
        }

        public void NotifyOrderComplete(Entity entity)
        {
            OrderSubmitter.NotifyOrderComplete(World, entity, _orderTypeRegistry);
        }

        private void TrySubmitPending(Entity entity, ref OrderBuffer buffer, int currentStep)
        {
            if (!buffer.HasPending || buffer.HasActive || buffer.HasQueued)
            {
                return;
            }

            Order pendingOrder = buffer.PendingOrder.Order;
            OrderSubmitResult preview = OrderSubmitter.Preview(
                World,
                entity,
                in pendingOrder,
                _orderTypeRegistry,
                _orderRuleRegistry,
                currentStep,
                _stepRateHz);
            if (preview == OrderSubmitResult.Blocked)
            {
                return;
            }

            if (!HasAdmissionCapacity(in pendingOrder, 1))
            {
                AdmissionBackpressureCount++;
                return;
            }

            buffer.ClearPending();
            OrderSubmitResult result = OrderSubmitter.Submit(
                World,
                entity,
                in pendingOrder,
                _orderTypeRegistry,
                _orderRuleRegistry,
                currentStep,
                _stepRateHz);
            if (result != preview)
            {
                throw new InvalidOperationException(
                    $"Pending order {pendingOrder.OrderId} changed after successful preflight: expected {preview}, returned {result}.");
            }

            MarkActiveAdmissionPublished(entity, pendingOrder.OrderId, result);
            PublishAdmission(in pendingOrder, result);
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
