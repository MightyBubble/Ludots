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
                            return OrderSubmitResult.RejectedQueueFull;
                        }
                    }

                    return buffer.Enqueue(order, config.Priority, expireStep, currentStep)
                        ? OrderSubmitResult.Queued
                        : OrderSubmitResult.RejectedQueueFull;
                }
                case SameTypePolicy.Replace:
                    buffer.RemoveAllOfType(order.OrderTypeId);
                    return buffer.Enqueue(order, config.Priority, expireStep, currentStep)
                        ? OrderSubmitResult.Queued
                        : OrderSubmitResult.RejectedQueueFull;
                case SameTypePolicy.Ignore:
                default:
                    return OrderSubmitResult.RejectedByRule;
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

            if (config.EntityBlackboardKey >= 0 && order.Target != default && world.Has<BlackboardEntityBuffer>(entity))
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
            if (!buffer.HasActive)
            {
                return false;
            }

            int completedOrderId = buffer.ActiveOrder.Order.OrderId;
            int completedOrderTypeId = buffer.ActiveOrder.Order.OrderTypeId;
            DeactivateCurrentOrder(world, entity, ref buffer, registry);
            WriteTerminalSignal(world, entity, completedOrderId, completedOrderTypeId, state, failureReason);

            if (buffer.PromoteNext())
            {
                var nextOrder = buffer.ActiveOrder.Order;
                var nextConfig = registry.Get(nextOrder.OrderTypeId);
                ActivateOrder(world, entity, ref buffer, in nextOrder, in nextConfig);
            }

            return true;
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
            FinalizeCurrent(world, entity, registry, OrderTerminalState.Cancelled);
        }

        public static void CancelAll(World world, Entity entity, OrderTypeRegistry registry)
        {
            if (!world.IsAlive(entity) || !world.Has<OrderBuffer>(entity))
            {
                return;
            }

            ref var buffer = ref world.Get<OrderBuffer>(entity);
            DeactivateCurrentOrder(world, entity, ref buffer, registry);
            buffer.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteTerminalSignal(
            World world,
            Entity entity,
            int orderId,
            int orderTypeId,
            OrderTerminalState state,
            OrderFailureReason failureReason)
        {
            if (orderId <= 0)
            {
                return;
            }

            if (world.Has<OrderContinuationBuffer>(entity) && state != OrderTerminalState.Completed)
            {
                ref var continuation = ref world.Get<OrderContinuationBuffer>(entity);
                continuation.RemoveByTrigger(orderId);
            }

            if (!world.Has<OrderTerminalSignal>(entity))
            {
                return;
            }

            ref var signal = ref world.Get<OrderTerminalSignal>(entity);
            signal.OrderId = orderId;
            signal.OrderTypeId = orderTypeId;
            signal.State = state;
            signal.FailureReason = failureReason;
        }
    }
}
