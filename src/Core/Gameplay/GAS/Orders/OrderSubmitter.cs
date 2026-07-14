using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public static class OrderSubmitter
    {
        public static OrderSubmitResult Submit(
            World world,
            Entity entity,
            in Order order,
            OrderTypeRegistry registry,
            OrderRuleRegistry? orderRuleRegistry,
            int currentStep,
            int stepRateHz)
        {
            if (stepRateHz <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stepRateHz), stepRateHz, "stepRateHz must be positive.");
            }

            if (!world.IsAlive(entity) || !world.Has<OrderBuffer>(entity))
            {
                return OrderSubmitResult.RejectedInvalidActor;
            }

            var config = registry.Get(order.OrderTypeId);
            ref var buffer = ref world.Get<OrderBuffer>(entity);

            return order.SubmitMode == OrderSubmitMode.Queued
                ? HandleQueuedMode(ref buffer, in order, in config, currentStep, stepRateHz)
                : HandleImmediateMode(world, entity, ref buffer, in order, in config, registry, orderRuleRegistry, currentStep, stepRateHz);
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
                return OrderSubmitResult.RejectedByRule;
            }

            if (buffer.QueuedCount >= config.QueuedModeMaxSize)
            {
                return OrderSubmitResult.RejectedQueueFull;
            }

            int expireStep = CalculateExpireStep(config, currentStep, stepRateHz);
            return buffer.Enqueue(order, config.Priority, expireStep, currentStep)
                ? OrderSubmitResult.Queued
                : OrderSubmitResult.RejectedQueueFull;
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
                    return OrderSubmitResult.RejectedByRule;
                }
            }

            bool canInterrupt = activeOrderTypeId == 0 || CanInterrupt(activeOrderTypeId, in order, in config, orderRuleRegistry);
            if (canInterrupt)
            {
                OrderTypeConfig? activeConfig = buffer.HasActive
                    ? registry.Get(buffer.ActiveOrder.Order.OrderTypeId)
                    : null;
                OrderSubmitResult preparationResult = TryPrepareActivationBlackboard(
                    world,
                    entity,
                    in order,
                    in config,
                    activeConfig,
                    out PreparedActivationBlackboard preparedBlackboard);
                if (preparationResult != OrderSubmitResult.Activated)
                {
                    return preparationResult;
                }

                if (buffer.HasActive)
                {
                    registry.EnsureTerminalResultCapacity();
                }

                if (buffer.HasActive)
                {
                    FinalizeActive(
                        world,
                        entity,
                        ref buffer,
                        registry,
                        OrderTerminalState.Cancelled,
                        OrderFailureReason.Interrupted,
                        promoteNext: false);
                }

                if (config.ClearQueueOnActivate)
                {
                    ReleaseQueuedOrders(world, ref buffer);
                }

                buffer.SetActiveDirect(in order, config.Priority);
                CommitPreparedBlackboard(world, entity, in preparedBlackboard);
                return OrderSubmitResult.Activated;
            }

            return HandleSameTypePolicy(world, ref buffer, in order, in config, currentStep, stepRateHz);
        }

        private static OrderSubmitResult HandleSameTypePolicy(
            World world,
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
                            ReleaseOldestQueuedOrderOfType(world, ref buffer, order.OrderTypeId);
                        }
                        else
                        {
                            return OrderSubmitResult.RejectedQueueFull;
                        }
                    }

                    return buffer.Enqueue(order, config.Priority, expireStep, currentStep)
                        ? OrderSubmitResult.Queued
                        : OrderSubmitResult.RejectedQueueFull;
                }
                case SameTypePolicy.Replace:
                    ReleaseAllQueuedOrdersOfType(world, ref buffer, order.OrderTypeId);
                    return buffer.Enqueue(order, config.Priority, expireStep, currentStep)
                        ? OrderSubmitResult.Queued
                        : OrderSubmitResult.RejectedQueueFull;
                case SameTypePolicy.Ignore:
                default:
                    return OrderSubmitResult.RejectedByRule;
            }
        }

        private struct PreparedActivationBlackboard
        {
            public bool HasSpatial;
            public bool HasEntity;
            public bool HasInt;
            public BlackboardSpatialBuffer Spatial;
            public BlackboardEntityBuffer Entity;
            public BlackboardIntBuffer Int;
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
            buffer.ClearActiveTransferred();
        }

        private static OrderSubmitResult TryPrepareActivationBlackboard(
            World world,
            Entity entity,
            in Order order,
            in OrderTypeConfig config,
            OrderTypeConfig? activeConfig,
            out PreparedActivationBlackboard prepared)
        {
            prepared = default;

            if (config.SpatialBlackboardKey >= 0)
            {
                if (!world.Has<BlackboardSpatialBuffer>(entity))
                {
                    return OrderSubmitResult.RejectedMissingBlackboard;
                }

                prepared.HasSpatial = true;
                prepared.Spatial = world.Get<BlackboardSpatialBuffer>(entity);
                int spatialKey = config.SpatialBlackboardKey;
                if (activeConfig != null && activeConfig.SpatialBlackboardKey >= 0)
                {
                    int activeSpatialKey = activeConfig.SpatialBlackboardKey;
                    if (activeSpatialKey == spatialKey)
                    {
                        prepared.Spatial.ClearPoints(activeSpatialKey);
                    }
                    else
                    {
                        prepared.Spatial.RemoveEntry(activeSpatialKey);
                    }
                }

                if (!prepared.Spatial.HasKey(spatialKey) &&
                    prepared.Spatial.EntryCount >= BlackboardSpatialBuffer.MAX_ENTRIES)
                {
                    return OrderSubmitResult.RejectedBlackboardCapacity;
                }

                prepared.Spatial.ClearPoints(spatialKey);

                if (order.Args.Spatial.Mode == OrderCollectionMode.Single)
                {
                    prepared.Spatial.SetPoint(spatialKey, order.Args.Spatial.WorldCm);
                }
                else
                {
                    int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(world, in order);
                    if (pointCount > BlackboardSpatialBuffer.MAX_POINTS_PER_ENTRY)
                    {
                        return OrderSubmitResult.RejectedBlackboardCapacity;
                    }

                    for (int i = 0; i < pointCount; i++)
                    {
                        if (!OrderWorldSpatialResolver.TryResolveSpatialPointAt(world, in order, i, out Vector3 point))
                        {
                            throw new InvalidOperationException(
                                $"ORDER.SPATIAL.ERR.PointMissing: actor={order.Actor.Id}, orderId={order.OrderId}, pointIndex={i}.");
                        }

                        if (!prepared.Spatial.AppendPoint(spatialKey, point))
                        {
                            return OrderSubmitResult.RejectedBlackboardCapacity;
                        }
                    }
                }
            }

            if (config.EntityBlackboardKey >= 0)
            {
                if (!world.Has<BlackboardEntityBuffer>(entity))
                {
                    return OrderSubmitResult.RejectedMissingBlackboard;
                }

                prepared.HasEntity = true;
                prepared.Entity = world.Get<BlackboardEntityBuffer>(entity);
                if (activeConfig != null && activeConfig.EntityBlackboardKey >= 0)
                {
                    prepared.Entity.Remove(activeConfig.EntityBlackboardKey);
                }

                if (order.Target != default)
                {
                    if (!prepared.Entity.HasKey(config.EntityBlackboardKey) &&
                        prepared.Entity.Count >= BlackboardEntityBuffer.MAX_ENTRIES)
                    {
                        return OrderSubmitResult.RejectedBlackboardCapacity;
                    }
                    prepared.Entity.Set(config.EntityBlackboardKey, order.Target);
                }
            }

            if (config.IntArg0BlackboardKey >= 0)
            {
                if (!world.Has<BlackboardIntBuffer>(entity))
                {
                    return OrderSubmitResult.RejectedMissingBlackboard;
                }

                prepared.HasInt = true;
                prepared.Int = world.Get<BlackboardIntBuffer>(entity);
                if (activeConfig != null && activeConfig.IntArg0BlackboardKey >= 0)
                {
                    prepared.Int.Remove(activeConfig.IntArg0BlackboardKey);
                }

                if (!prepared.Int.TryGet(config.IntArg0BlackboardKey, out _) &&
                    prepared.Int.Count >= GasConstants.MAX_BLACKBOARD_ENTRIES)
                {
                    return OrderSubmitResult.RejectedBlackboardCapacity;
                }
                prepared.Int.Set(config.IntArg0BlackboardKey, order.Args.I0);
            }

            return OrderSubmitResult.Activated;
        }

        private static void CommitPreparedBlackboard(
            World world,
            Entity entity,
            in PreparedActivationBlackboard prepared)
        {
            if (prepared.HasSpatial)
            {
                world.Get<BlackboardSpatialBuffer>(entity) = prepared.Spatial;
            }
            if (prepared.HasEntity)
            {
                world.Get<BlackboardEntityBuffer>(entity) = prepared.Entity;
            }
            if (prepared.HasInt)
            {
                world.Get<BlackboardIntBuffer>(entity) = prepared.Int;
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

        public static bool FinalizeCurrent(
            World world,
            Entity entity,
            OrderTypeRegistry registry,
            OrderTerminalState state,
            OrderFailureReason failureReason = OrderFailureReason.None)
        {
            if (!world.IsAlive(entity) || !world.Has<OrderBuffer>(entity))
            {
                return false;
            }

            ref var buffer = ref world.Get<OrderBuffer>(entity);
            return FinalizeActive(
                world,
                entity,
                ref buffer,
                registry,
                state,
                failureReason,
                promoteNext: true);
        }

        public static bool NotifyOrderComplete(World world, Entity entity, OrderTypeRegistry registry)
            => FinalizeCurrent(world, entity, registry, OrderTerminalState.Completed);

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

            var nextOrder = buffer.GetQueued(0).Order;
            var nextConfig = registry.Get(nextOrder.OrderTypeId);
            OrderSubmitResult preparationResult = TryPrepareActivationBlackboard(
                world,
                entity,
                in nextOrder,
                in nextConfig,
                activeConfig: null,
                out PreparedActivationBlackboard preparedBlackboard);
            if (preparationResult != OrderSubmitResult.Activated)
            {
                throw new InvalidOperationException(
                    $"ORDER.ACTIVATION.ERR.QueuedRequirementsRejected: orderId={nextOrder.OrderId}, result={preparationResult}.");
            }

            if (!buffer.PromoteNext())
            {
                return false;
            }
            CommitPreparedBlackboard(world, entity, in preparedBlackboard);
            return true;
        }

        public static void CancelCurrent(World world, Entity entity, OrderTypeRegistry registry)
        {
            FinalizeCurrent(world, entity, registry, OrderTerminalState.Cancelled);
        }

        public static void CancelAll(World world, Entity entity, OrderTypeRegistry registry)
        {
            if (!world.IsAlive(entity) || !world.Has<OrderBuffer>(entity))
            {
                return;
            }

            ref var buffer = ref world.Get<OrderBuffer>(entity);
            FinalizeActive(
                world,
                entity,
                ref buffer,
                registry,
                OrderTerminalState.Cancelled,
                OrderFailureReason.None,
                promoteNext: false);
            ReleaseQueuedOrders(world, ref buffer);
            ReleasePendingOrder(world, ref buffer);
            buffer.Clear();
        }

        private static bool FinalizeActive(
            World world,
            Entity entity,
            ref OrderBuffer buffer,
            OrderTypeRegistry registry,
            OrderTerminalState state,
            OrderFailureReason failureReason,
            bool promoteNext)
        {
            if (!buffer.HasActive)
            {
                return false;
            }

            Order terminalOrder = buffer.ActiveOrder.Order;
            ValidateTerminalOutcome(in terminalOrder, state, failureReason);
            registry.EnsureTerminalResultCapacity();

            bool hasPreparedPromotion = false;
            PreparedActivationBlackboard preparedPromotion = default;
            if (promoteNext && buffer.HasQueued)
            {
                Order nextOrder = buffer.GetQueued(0).Order;
                OrderTypeConfig nextConfig = registry.Get(nextOrder.OrderTypeId);
                OrderTypeConfig activeConfig = registry.Get(terminalOrder.OrderTypeId);
                OrderSubmitResult preparationResult = TryPrepareActivationBlackboard(
                    world,
                    entity,
                    in nextOrder,
                    in nextConfig,
                    activeConfig,
                    out preparedPromotion);
                if (preparationResult != OrderSubmitResult.Activated)
                {
                    throw new InvalidOperationException(
                        $"ORDER.ACTIVATION.ERR.QueuedRequirementsRejected: orderId={nextOrder.OrderId}, result={preparationResult}.");
                }
                hasPreparedPromotion = true;
            }

            DeactivateCurrentOrder(world, entity, ref buffer, registry);
            OrderSpatialPayloadOps.Release(world, in terminalOrder);

            if (world.Has<OrderContinuationBuffer>(entity) && state != OrderTerminalState.Completed)
            {
                ref var continuation = ref world.Get<OrderContinuationBuffer>(entity);
                continuation.RemoveByTrigger(terminalOrder.OrderId);
            }

            var outcome = new OrderTerminalOutcome(
                terminalOrder.OrderId,
                terminalOrder.OrderTypeId,
                state,
                failureReason,
                entity);
            registry.PublishTerminalResult(in outcome);

            if (hasPreparedPromotion && buffer.PromoteNext())
            {
                CommitPreparedBlackboard(world, entity, in preparedPromotion);
            }

            return true;
        }

        public static void ReplacePending(
            World world,
            ref OrderBuffer buffer,
            in Order order,
            int priority,
            int expireStep,
            int insertStep)
        {
            ReleasePendingOrder(world, ref buffer);
            buffer.SetPending(in order, priority, expireStep, insertStep);
        }

        public static void ReleasePendingOrder(World world, ref OrderBuffer buffer)
        {
            if (!buffer.HasPending)
            {
                return;
            }

            OrderSpatialPayloadOps.Release(world, in buffer.PendingOrder.Order);
            buffer.ClearPendingTransferred();
        }

        public static void ReleaseQueuedOrders(World world, ref OrderBuffer buffer)
        {
            while (buffer.QueuedCount > 0)
            {
                QueuedOrder removed = buffer.RemoveAtTransferred(buffer.QueuedCount - 1);
                OrderSpatialPayloadOps.Release(world, in removed.Order);
            }
        }

        private static void ReleaseOldestQueuedOrderOfType(World world, ref OrderBuffer buffer, int orderTypeId)
        {
            for (int i = buffer.QueuedCount - 1; i >= 0; i--)
            {
                if (buffer.GetQueued(i).Order.OrderTypeId != orderTypeId)
                {
                    continue;
                }

                QueuedOrder removed = buffer.RemoveAtTransferred(i);
                OrderSpatialPayloadOps.Release(world, in removed.Order);
                return;
            }
        }

        private static void ReleaseAllQueuedOrdersOfType(World world, ref OrderBuffer buffer, int orderTypeId)
        {
            for (int i = buffer.QueuedCount - 1; i >= 0; i--)
            {
                if (buffer.GetQueued(i).Order.OrderTypeId != orderTypeId)
                {
                    continue;
                }

                QueuedOrder removed = buffer.RemoveAtTransferred(i);
                OrderSpatialPayloadOps.Release(world, in removed.Order);
            }
        }

        private static void ValidateTerminalOutcome(
            in Order order,
            OrderTerminalState state,
            OrderFailureReason failureReason)
        {
            if (order.OrderId <= 0)
            {
                throw new InvalidOperationException(
                    $"ORDER.TERMINAL.ERR.InvalidOrderId: orderTypeId={order.OrderTypeId}, orderId={order.OrderId}.");
            }

            if (order.OrderTypeId <= 0)
            {
                throw new InvalidOperationException(
                    $"ORDER.TERMINAL.ERR.InvalidOrderTypeId: orderId={order.OrderId}, orderTypeId={order.OrderTypeId}.");
            }

            if (state == OrderTerminalState.Completed && failureReason != OrderFailureReason.None)
            {
                throw new InvalidOperationException(
                    $"ORDER.TERMINAL.ERR.CompletedWithFailure: orderId={order.OrderId}, failureReason={failureReason}.");
            }

            if (state == OrderTerminalState.Failed && failureReason == OrderFailureReason.None)
            {
                throw new InvalidOperationException(
                    $"ORDER.TERMINAL.ERR.FailedWithoutReason: orderId={order.OrderId}.");
            }

            if (state == OrderTerminalState.Cancelled &&
                failureReason != OrderFailureReason.None &&
                failureReason != OrderFailureReason.Interrupted)
            {
                throw new InvalidOperationException(
                    $"ORDER.TERMINAL.ERR.InvalidCancellationReason: orderId={order.OrderId}, failureReason={failureReason}.");
            }
        }
    }
}
