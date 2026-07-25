using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public enum OrderSubmitResult
    {
        Activated = 0,
        Queued = 1,
        Pending = 2,
        Blocked = 3,
        QueueFull = 4,
        Ignored = 5,
        InvalidEntity = 6,
        BatchRejected = 7,
        ValidationRejected = 8,
        InvalidOrderType = 9,
        NetworkRateLimited = 10,
        NetworkTargetTickExpired = 11,
        NetworkTargetTickTooFarAhead = 12,
        NetworkActorLimitExceeded = 13,
        NetworkAdmissionBackpressured = 14,
        NetworkInvalidConnectionSeat = 15,
        NetworkSequenceGap = 16,
        NetworkSequenceOutsideHistory = 17,
        NetworkScheduled = 18,
        NetworkScheduleFull = 19,
        NetworkInvalidActorHandle = 20,
        NetworkStaleActorGeneration = 21,
        NetworkActorNotControlled = 22,
        NetworkInvalidTargetHandle = 23,
        NetworkStaleTargetGeneration = 24,
        NetworkTargetNotKnown = 25,
        NetworkCommandSchemaMismatch = 26,
        NetworkMatchNotStarted = 27,
        NetworkMatchCompleted = 28,
        InsufficientResources = 29,
        Expired = 30,
        Cancelled = 31,
        NetworkSequenceExhausted = 32
    }

    public static class OrderSubmitter
    {
        public static OrderSubmitResult Preview(
            World world,
            Entity entity,
            in Order order,
            OrderTypeRegistry registry,
            OrderRuleRegistry? orderRuleRegistry,
            int currentStep,
            int stepRateHz)
        {
            return Preview(
                world,
                entity,
                in order,
                registry,
                orderRuleRegistry,
                currentStep,
                stepRateHz,
                out _);
        }

        public static OrderSubmitResult Preview(
            World world,
            Entity entity,
            in Order order,
            OrderTypeRegistry registry,
            OrderRuleRegistry? orderRuleRegistry,
            int currentStep,
            int stepRateHz,
            out OrderBuffer preview)
        {
            if (stepRateHz <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stepRateHz), stepRateHz, "stepRateHz must be positive.");
            }

            OrderEntityReferenceContract.Validate(in order, "OrderSubmitter.Preview");

            if (!world.IsAlive(entity) || !world.Has<OrderBuffer>(entity))
            {
                preview = default;
                return OrderSubmitResult.InvalidEntity;
            }

            preview = world.Get<OrderBuffer>(entity);
            OrderTypeConfig config = registry.Get(order.OrderTypeId);
            OrderSubmitResult admission = orderRuleRegistry?.ValidateAdmission(
                world,
                entity,
                in order,
                in preview) ?? OrderSubmitResult.Activated;
            if (admission != OrderSubmitResult.Activated)
            {
                return admission;
            }

            if (IsQueuedMode(order.SubmitMode))
            {
                return HandleQueuedMode(ref preview, in order, in config, currentStep, stepRateHz);
            }

            int activeOrderTypeId = preview.HasActive ? preview.ActiveOrder.Order.OrderTypeId : 0;
            if (orderRuleRegistry != null && orderRuleRegistry.HasRule(order.OrderTypeId))
            {
                ref readonly var rules = ref orderRuleRegistry.Get(order.OrderTypeId);
                if (rules.Blocks(activeOrderTypeId))
                {
                    return OrderSubmitResult.Blocked;
                }
            }

            if (activeOrderTypeId == 0 || CanInterrupt(activeOrderTypeId, in order, in config, orderRuleRegistry))
            {
                if (config.ClearQueueOnActivate)
                {
                    preview.ClearQueued();
                }

                preview.SetActiveDirect(in order, config.Priority);
                return OrderSubmitResult.Activated;
            }

            return HandleSameTypePolicy(ref preview, in order, in config, currentStep, stepRateHz);
        }

        public static OrderSubmitResult Submit(
            World world,
            Entity entity,
            in Order order,
            OrderTypeRegistry registry,
            OrderRuleRegistry? orderRuleRegistry,
            int currentStep,
            int stepRateHz,
            OrderAdmissionResultBuffer? admissionResults = null)
        {
            if (stepRateHz <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stepRateHz), stepRateHz, "stepRateHz must be positive.");
            }

            OrderEntityReferenceContract.Validate(in order, "OrderSubmitter.Submit");

            if (!world.IsAlive(entity) || !world.Has<OrderBuffer>(entity))
            {
                return OrderSubmitResult.InvalidEntity;
            }

            var config = registry.Get(order.OrderTypeId);
            ref var buffer = ref world.Get<OrderBuffer>(entity);
            OrderSubmitResult admission = orderRuleRegistry?.ValidateAdmission(
                world,
                entity,
                in order,
                in buffer) ?? OrderSubmitResult.Activated;
            if (admission != OrderSubmitResult.Activated)
            {
                return admission;
            }

            OrderBuffer before = default;
            int expectedCancellations = 0;
            if (OrderAdmissionTracking.HasWaitingNetworkFeedback(in buffer))
            {
                before = buffer;
                Preview(
                    world,
                    entity,
                    in order,
                    registry,
                    orderRuleRegistry,
                    currentStep,
                    stepRateHz,
                    out OrderBuffer projected);
                expectedCancellations = OrderAdmissionTracking.CountRemovedWaiting(in before, in projected);
                if (expectedCancellations > 0 &&
                    (admissionResults == null || admissionResults.AvailableCapacity < expectedCancellations))
                {
                    throw new InvalidOperationException(
                        $"Submitting order {order.OrderId} would cancel {expectedCancellations} network-admitted waiting orders without matching result capacity.");
                }
            }

            OrderSubmitResult result = IsQueuedMode(order.SubmitMode)
                ? HandleQueuedMode(ref buffer, in order, in config, currentStep, stepRateHz)
                : HandleImmediateMode(world, entity, ref buffer, in order, in config, registry, orderRuleRegistry, currentStep, stepRateHz);
            if (expectedCancellations > 0)
            {
                int actualCancellations = OrderAdmissionTracking.CountRemovedWaiting(in before, in buffer);
                if (actualCancellations != expectedCancellations)
                {
                    throw new InvalidOperationException(
                        $"Order {order.OrderId} cancellation count changed after preflight: expected {expectedCancellations}, got {actualCancellations}.");
                }

                OrderAdmissionTracking.PublishRemovedWaiting(
                    admissionResults!,
                    in before,
                    in buffer,
                    OrderSubmitResult.Cancelled);
            }

            return result;
        }

        private static OrderSubmitResult HandleQueuedMode(
            ref OrderBuffer buffer,
            in Order order,
            in OrderTypeConfig config,
            int currentStep,
            int stepRateHz)
        {
            if (!config.AllowQueuedMode)
            {
                return OrderSubmitResult.Ignored;
            }

            if (buffer.QueuedCount >= config.QueuedModeMaxSize)
            {
                return OrderSubmitResult.QueueFull;
            }

            int expireStep = order.SubmitMode == OrderSubmitMode.PersistentQueued
                ? -1
                : CalculateExpireStep(config, currentStep, stepRateHz);
            return buffer.Enqueue(order, config.Priority, expireStep, currentStep)
                ? OrderSubmitResult.Queued
                : OrderSubmitResult.QueueFull;
        }

        private static OrderSubmitResult HandleImmediateMode(
            World world,
            Entity entity,
            ref OrderBuffer buffer,
            in Order order,
            in OrderTypeConfig config,
            OrderTypeRegistry registry,
            OrderRuleRegistry? orderRuleRegistry,
            int currentStep,
            int stepRateHz)
        {
            int activeOrderTypeId = buffer.HasActive ? buffer.ActiveOrder.Order.OrderTypeId : 0;
            if (orderRuleRegistry != null && orderRuleRegistry.HasRule(order.OrderTypeId))
            {
                ref readonly var rules = ref orderRuleRegistry.Get(order.OrderTypeId);
                if (rules.Blocks(activeOrderTypeId))
                {
                    return OrderSubmitResult.Blocked;
                }
            }

            bool canInterrupt = activeOrderTypeId == 0 || CanInterrupt(activeOrderTypeId, in order, in config, orderRuleRegistry);
            if (canInterrupt)
            {
                if (buffer.HasActive)
                {
                    DeactivateCurrentOrder(world, entity, ref buffer, registry);
                }

                if (config.ClearQueueOnActivate)
                {
                    buffer.ClearQueued();
                }

                ActivateOrder(world, entity, ref buffer, in order, in config);
                return OrderSubmitResult.Activated;
            }

            return HandleSameTypePolicy(ref buffer, in order, in config, currentStep, stepRateHz);
        }

        private static OrderSubmitResult HandleSameTypePolicy(
            ref OrderBuffer buffer,
            in Order order,
            in OrderTypeConfig config,
            int currentStep,
            int stepRateHz)
        {
            int expireStep = CalculateExpireStep(config, currentStep, stepRateHz);

            switch (config.SameTypePolicy)
            {
                case SameTypePolicy.Queue:
                {
                    int countOfType = buffer.CountOfType(order.OrderTypeId);
                    if (countOfType >= config.MaxQueueSize)
                    {
                        if (config.QueueFullPolicy == QueueFullPolicy.DropOldest)
                        {
                            buffer.RemoveOldestOfType(order.OrderTypeId);
                        }
                        else
                        {
                            return OrderSubmitResult.QueueFull;
                        }
                    }

                    return buffer.Enqueue(order, config.Priority, expireStep, currentStep)
                        ? OrderSubmitResult.Queued
                        : OrderSubmitResult.QueueFull;
                }
                case SameTypePolicy.Replace:
                    buffer.RemoveAllOfType(order.OrderTypeId);
                    return buffer.Enqueue(order, config.Priority, expireStep, currentStep)
                        ? OrderSubmitResult.Queued
                        : OrderSubmitResult.QueueFull;
                case SameTypePolicy.Ignore:
                default:
                    return OrderSubmitResult.Ignored;
            }
        }

        private static void ActivateOrder(
            World world,
            Entity entity,
            ref OrderBuffer buffer,
            in Order order,
            in OrderTypeConfig config)
        {
            WriteOrderToBlackboard(world, entity, in order, in config);
            if (!buffer.HasActive)
            {
                buffer.SetActiveDirect(in order, config.Priority);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DeactivateCurrentOrder(
            World world,
            Entity entity,
            ref OrderBuffer buffer,
            OrderTypeRegistry registry)
        {
            if (!buffer.HasActive)
            {
                return;
            }

            var activeConfig = registry.Get(buffer.ActiveOrder.Order.OrderTypeId);
            ClearOrderBlackboard(world, entity, in activeConfig);
            buffer.ClearActive();
        }

        private static void WriteOrderToBlackboard(World world, Entity entity, in Order order, in OrderTypeConfig config)
        {
            if (config.SpatialBlackboardKey >= 0 && world.Has<BlackboardSpatialBuffer>(entity))
            {
                ref var spatial = ref world.Get<BlackboardSpatialBuffer>(entity);
                int spatialKey = config.SpatialBlackboardKey;
                spatial.ClearPoints(spatialKey);

                if (order.Args.Spatial.Mode == OrderCollectionMode.Single)
                {
                    spatial.SetPoint(spatialKey, order.Args.Spatial.WorldCm);
                }
                else
                {
                    unsafe
                    {
                        fixed (int* px = order.Args.Spatial.PointX)
                        fixed (int* py = order.Args.Spatial.PointY)
                        fixed (int* pz = order.Args.Spatial.PointZ)
                        {
                            for (int i = 0; i < order.Args.Spatial.PointCount; i++)
                            {
                                spatial.AppendPoint(spatialKey, new Vector3(px[i], py[i], pz[i]));
                            }
                        }
                    }
                }
            }

            if (config.EntityBlackboardKey >= 0 && order.Target != Entity.Null && world.Has<BlackboardEntityBuffer>(entity))
            {
                ref var entities = ref world.Get<BlackboardEntityBuffer>(entity);
                entities.Set(config.EntityBlackboardKey, order.Target);
            }

            if (config.IntArg0BlackboardKey >= 0 && world.Has<BlackboardIntBuffer>(entity))
            {
                ref var ints = ref world.Get<BlackboardIntBuffer>(entity);
                ints.Set(config.IntArg0BlackboardKey, order.Args.I0);
            }
        }

        private static void ClearOrderBlackboard(World world, Entity entity, in OrderTypeConfig config)
        {
            if (config.SpatialBlackboardKey >= 0 && world.Has<BlackboardSpatialBuffer>(entity))
            {
                ref var spatial = ref world.Get<BlackboardSpatialBuffer>(entity);
                spatial.ClearPoints(config.SpatialBlackboardKey);
            }

            if (config.EntityBlackboardKey >= 0 && world.Has<BlackboardEntityBuffer>(entity))
            {
                ref var entities = ref world.Get<BlackboardEntityBuffer>(entity);
                entities.Remove(config.EntityBlackboardKey);
            }

            if (config.IntArg0BlackboardKey >= 0 && world.Has<BlackboardIntBuffer>(entity))
            {
                ref var ints = ref world.Get<BlackboardIntBuffer>(entity);
                ints.Remove(config.IntArg0BlackboardKey);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CanInterrupt(
            int activeOrderTypeId,
            in Order newOrder,
            in OrderTypeConfig newConfig,
            OrderRuleRegistry? orderRuleRegistry)
        {
            if (activeOrderTypeId <= 0)
            {
                return false;
            }

            if (activeOrderTypeId == newOrder.OrderTypeId)
            {
                return newConfig.CanInterruptSelf;
            }

            if (orderRuleRegistry == null || !orderRuleRegistry.HasRule(newOrder.OrderTypeId))
            {
                return false;
            }

            ref readonly var rules = ref orderRuleRegistry.Get(newOrder.OrderTypeId);
            return rules.Interrupts(activeOrderTypeId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CalculateExpireStep(in OrderTypeConfig config, int currentStep, int stepRateHz)
        {
            if (config.BufferWindowMs <= 0) return -1;
            int bufferTicks = (config.BufferWindowMs * stepRateHz) / 1000;
            if (bufferTicks < 1) bufferTicks = 1;
            return currentStep + bufferTicks;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsQueuedMode(OrderSubmitMode mode) =>
            mode is OrderSubmitMode.Queued or OrderSubmitMode.PersistentQueued;

        public static void NotifyOrderComplete(World world, Entity entity, OrderTypeRegistry registry)
        {
            if (!world.IsAlive(entity) || !world.Has<OrderBuffer>(entity))
            {
                return;
            }

            ref var buffer = ref world.Get<OrderBuffer>(entity);
            int completedOrderId = buffer.HasActive ? buffer.ActiveOrder.Order.OrderId : 0;
            int completedOrderTypeId = buffer.HasActive ? buffer.ActiveOrder.Order.OrderTypeId : 0;
            DeactivateCurrentOrder(world, entity, ref buffer, registry);
            WriteCompletedOrderSignal(world, entity, completedOrderId, completedOrderTypeId);

        }

        public static bool TryPromoteNextQueuedToActive(World world, Entity entity, OrderTypeRegistry registry)
        {
            if (!world.IsAlive(entity) || !world.Has<OrderBuffer>(entity))
            {
                return false;
            }

            ref var buffer = ref world.Get<OrderBuffer>(entity);
            return TryPromoteNextQueuedToActive(world, entity, ref buffer, registry);
        }

        public static bool TryPromoteNextQueuedToActive(
            World world,
            Entity entity,
            ref OrderBuffer buffer,
            OrderTypeRegistry registry)
        {
            if (buffer.HasActive || !buffer.HasQueued)
            {
                return false;
            }

            if (!buffer.PromoteNext())
            {
                return false;
            }

            var nextOrder = buffer.ActiveOrder.Order;
            var nextConfig = registry.Get(nextOrder.OrderTypeId);
            ActivateOrder(world, entity, ref buffer, in nextOrder, in nextConfig);
            return true;
        }

        public static void CancelCurrent(World world, Entity entity, OrderTypeRegistry registry)
        {
            if (!world.IsAlive(entity) || !world.Has<OrderBuffer>(entity))
            {
                return;
            }

            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(entity);
            int cancelledOrderId = buffer.HasActive ? buffer.ActiveOrder.Order.OrderId : 0;
            DeactivateCurrentOrder(world, entity, ref buffer, registry);
            if (cancelledOrderId > 0 && world.Has<OrderContinuationBuffer>(entity))
            {
                ref OrderContinuationBuffer continuations = ref world.Get<OrderContinuationBuffer>(entity);
                continuations.RemoveByTrigger(cancelledOrderId);
            }

            if (cancelledOrderId > 0 &&
                world.TryGet(entity, out CompletedOrderSignal completed) &&
                completed.OrderId == cancelledOrderId)
            {
                world.Set(entity, default(CompletedOrderSignal));
            }

        }

        public static void CancelAll(
            World world,
            Entity entity,
            OrderTypeRegistry registry,
            OrderAdmissionResultBuffer? admissionResults = null)
        {
            if (!world.IsAlive(entity) || !world.Has<OrderBuffer>(entity))
            {
                return;
            }

            ref var buffer = ref world.Get<OrderBuffer>(entity);
            int correlatedWaiting = 0;
            if (buffer.HasPending && OrderAdmissionTracking.RequiresNetworkFeedback(in buffer.PendingOrder.Order))
            {
                correlatedWaiting++;
            }

            for (int i = 0; i < buffer.QueuedCount; i++)
            {
                Order queued = buffer.GetQueued(i).Order;
                if (OrderAdmissionTracking.RequiresNetworkFeedback(in queued))
                {
                    correlatedWaiting++;
                }
            }

            if (correlatedWaiting > 0)
            {
                if (admissionResults == null || admissionResults.AvailableCapacity < correlatedWaiting)
                {
                    throw new InvalidOperationException(
                        $"Cancelling {correlatedWaiting} admitted waiting orders requires matching result capacity.");
                }

                if (buffer.HasPending && OrderAdmissionTracking.RequiresNetworkFeedback(in buffer.PendingOrder.Order))
                {
                    Order pending = buffer.PendingOrder.Order;
                    PublishCancellation(admissionResults, in pending);
                }

                for (int i = 0; i < buffer.QueuedCount; i++)
                {
                    Order queued = buffer.GetQueued(i).Order;
                    if (OrderAdmissionTracking.RequiresNetworkFeedback(in queued))
                    {
                        PublishCancellation(admissionResults, in queued);
                    }
                }
            }

            DeactivateCurrentOrder(world, entity, ref buffer, registry);
            buffer.Clear();
        }

        private static void PublishCancellation(
            OrderAdmissionResultBuffer admissionResults,
            in Order order)
        {
            var outcome = new OrderAdmissionOutcome(
                in order,
                OrderAdmissionStage.EntityIntake,
                OrderSubmitResult.Cancelled);
            if (!admissionResults.TryWrite(in outcome))
            {
                throw new InvalidOperationException(
                    $"Order cancellation result capacity {admissionResults.Capacity} is exhausted.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteCompletedOrderSignal(World world, Entity entity, int orderId, int orderTypeId)
        {
            if (orderId <= 0 ||
                !world.Has<OrderContinuationBuffer>(entity) ||
                !world.Get<OrderContinuationBuffer>(entity).HasEntries)
            {
                return;
            }

            if (!world.Has<CompletedOrderSignal>(entity))
            {
                world.Add(entity, new CompletedOrderSignal
                {
                    OrderId = orderId,
                    OrderTypeId = orderTypeId
                });
                return;
            }

            ref var signal = ref world.Get<CompletedOrderSignal>(entity);
            signal.OrderId = orderId;
            signal.OrderTypeId = orderTypeId;
        }
    }
}
