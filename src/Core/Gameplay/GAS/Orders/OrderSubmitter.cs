using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    using Ludots.Core.Gameplay.GAS;

    /// <summary>
    /// Result of an order submission attempt.
    /// </summary>
    public enum OrderSubmitResult
    {
        /// <summary>
        /// Order was accepted and activated immediately (converted to Tag + Blackboard).
        /// </summary>
        Activated = 0,
        
        /// <summary>
        /// Order was accepted and queued for later execution.
        /// </summary>
        Queued = 1,
        
        /// <summary>
        /// Order was rejected because it was blocked by tags.
        /// </summary>
        Blocked = 2,
        
        /// <summary>
        /// Order was rejected because the queue is full.
        /// </summary>
        QueueFull = 3,
        
        /// <summary>
        /// Order was rejected because it was ignored (sameTypePolicy=Ignore).
        /// </summary>
        Ignored = 4,
        
        /// <summary>
        /// Entity does not have required components.
        /// </summary>
        InvalidEntity = 5
    }
    
    /// <summary>
    /// Unified entry point for submitting orders.
    /// Orders are "intent declarations" - when activated, they are converted to Tag + Blackboard data.
    /// 
    /// Architecture:
    /// - Tag (GameplayTagContainer): Represents execution state (Order.Active.MoveTo, etc.)
    /// - Blackboard: Stores order parameters (waypoints, targets, etc.)
    /// - OrderBuffer: Stores queued orders waiting to be executed
    /// </summary>
    public static class OrderSubmitter
    {
        /// <summary>
        /// Default step rate used when caller does not specify one.
        /// This exists only as a migration aid; callers should provide the real rate.
        /// </summary>
        private const int DefaultStepRateHz = 30;
        
        /// <summary>
        /// Submit an order to an entity.
        /// AI and player use the same interface - no special handling.
        /// If AI needs to execute directly, call the ability system instead.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="entity">The target entity.</param>
        /// <param name="order">The order to submit.</param>
        /// <param name="registry">The order type registry.</param>
        /// <param name="tagRuleRegistry">The tag rule registry (for conflict checking).</param>
        /// <param name="currentStep">Current simulation step (for expiration).</param>
        /// <param name="stepRateHz">Step domain tick rate in Hz (e.g. 30 for 30 steps/sec). Defaults to 30.</param>
        /// <returns>The result of the submission.</returns>
        public static OrderSubmitResult Submit(
            World world,
            Entity entity,
            in Order order,
            OrderTypeRegistry registry,
            TagRuleRegistry? tagRuleRegistry,
            int currentStep,
            int stepRateHz = DefaultStepRateHz)
        {
            // Check entity has required components
            if (!world.IsAlive(entity)) return OrderSubmitResult.InvalidEntity;
            if (!world.Has<OrderBuffer>(entity)) return OrderSubmitResult.InvalidEntity;
            if (!world.Has<GameplayTagContainer>(entity)) return OrderSubmitResult.InvalidEntity;
            
            // Get order type config
            var config = registry.Get(order.OrderTagId);
            
            ref var buffer = ref world.Get<OrderBuffer>(entity);
            ref var tagsRef = ref world.Get<GameplayTagContainer>(entity);
            
            // Branch based on submit mode
            if (order.SubmitMode == OrderSubmitMode.Queued)
            {
                return HandleQueuedMode(ref buffer, in order, in config, currentStep, stepRateHz);
            }
            else
            {
                return HandleImmediateMode(world, entity, ref buffer, ref tagsRef, in order, in config, registry, tagRuleRegistry, currentStep, stepRateHz);
            }
        }
        
        /// <summary>
        /// Handle Queued mode: Skip conflict check, append to queue.
        /// </summary>
        private static OrderSubmitResult HandleQueuedMode(
            ref OrderBuffer buffer,
            in Order order,
            in OrderTypeConfig config,
            int currentStep,
            int stepRateHz)
        {
            // Check if Queued mode is allowed for this order type
            if (!config.AllowQueuedMode)
            {
                return OrderSubmitResult.Ignored;
            }
            
            // Check Queued mode queue size limit
            if (buffer.QueuedCount >= config.QueuedModeMaxSize)
            {
                return OrderSubmitResult.QueueFull;
            }
            
            // Calculate expiration
            int expireStep = CalculateExpireStep(config, currentStep, stepRateHz);
            
            // Append to queue end (no priority sorting for Shift+ mode - FIFO)
            if (buffer.Enqueue(order, config.Priority, expireStep, currentStep))
            {
                return OrderSubmitResult.Queued;
            }
            
            return OrderSubmitResult.QueueFull;
        }
        
        /// <summary>
        /// Handle Immediate mode: Check TagRuleSet, may interrupt or queue based on policy.
        /// </summary>
        private static OrderSubmitResult HandleImmediateMode(
            World world,
            Entity entity,
            ref OrderBuffer buffer,
            ref GameplayTagContainer tags,
            in Order order,
            in OrderTypeConfig config,
            OrderTypeRegistry registry,
            TagRuleRegistry? tagRuleRegistry,
            int currentStep,
            int stepRateHz)
        {
            // 1. Check BlockedTags - if blocked, reject entirely
            if (tagRuleRegistry != null && config.OrderStateTagId > 0)
            {
                if (tagRuleRegistry.HasRule(config.OrderStateTagId))
                {
                    ref readonly var ruleCompiled = ref tagRuleRegistry.Get(config.OrderStateTagId);
                    if (IsBlocked(in tags, in ruleCompiled))
                    {
                        return OrderSubmitResult.Blocked;
                    }
                }
            }
            
            // 2. Check if we can interrupt current order
            bool hasActive = tags.HasTag(OrderStateTags.State_HasActive);
            bool canInterrupt = !hasActive || CanInterrupt(ref tags, in config, tagRuleRegistry);
            
            if (canInterrupt)
            {
                // Interrupt current and activate new order
                if (hasActive)
                {
                    DeactivateCurrentOrder(world, entity, ref tags, ref buffer, registry);
                }
                
                // Clear queue if configured (e.g., right-click clears Shift+ queue)
                if (config.ClearQueueOnActivate)
                {
                    buffer.ClearQueued();
                    if (tags.HasTag(OrderStateTags.State_HasQueued))
                    {
                        tags.RemoveTag(OrderStateTags.State_HasQueued);
                    }
                }
                
                // Activate the new order
                ActivateOrder(world, entity, ref tags, in order, in config);
                return OrderSubmitResult.Activated;
            }
            
            // 3. Cannot interrupt - handle based on sameTypePolicy
            return HandleSameTypePolicy(ref buffer, ref tags, in order, in config, currentStep, stepRateHz);
        }
        
        /// <summary>
        /// Handle queuing based on sameTypePolicy (for Immediate mode when can't interrupt).
        /// </summary>
        private static OrderSubmitResult HandleSameTypePolicy(
            ref OrderBuffer buffer,
            ref GameplayTagContainer tags,
            in Order order,
            in OrderTypeConfig config,
            int currentStep,
            int stepRateHz)
        {
            int expireStep = CalculateExpireStep(config, currentStep, stepRateHz);
            
            switch (config.SameTypePolicy)
            {
                case SameTypePolicy.Queue:
                    // Check queue size for this type
                    int countOfType = buffer.CountOfType(order.OrderTagId);
                    if (countOfType >= config.MaxQueueSize)
                    {
                        if (config.QueueFullPolicy == QueueFullPolicy.DropOldest)
                        {
                            buffer.RemoveOldestOfType(order.OrderTagId);
                        }
                        else
                        {
                            return OrderSubmitResult.QueueFull;
                        }
                    }
                    
                    if (buffer.Enqueue(order, config.Priority, expireStep, currentStep))
                    {
                        tags.AddTag(OrderStateTags.State_HasQueued);
                        return OrderSubmitResult.Queued;
                    }
                    return OrderSubmitResult.QueueFull;
                    
                case SameTypePolicy.Replace:
                    // Remove all queued of this type and add new
                    buffer.RemoveAllOfType(order.OrderTagId);
                    if (buffer.Enqueue(order, config.Priority, expireStep, currentStep))
                    {
                        tags.AddTag(OrderStateTags.State_HasQueued);
                        return OrderSubmitResult.Queued;
                    }
                    return OrderSubmitResult.QueueFull;
                    
                case SameTypePolicy.Ignore:
                default:
                    return OrderSubmitResult.Ignored;
            }
        }
        
        /// <summary>
        /// Activate an order: Convert to Tag + Blackboard data.
        /// Also syncs the OrderBuffer active state so buffer.HasActive stays consistent.
        /// </summary>
        private static void ActivateOrder(
            World world,
            Entity entity,
            ref GameplayTagContainer tags,
            in Order order,
            in OrderTypeConfig config)
        {
            if (world.Has<OrderBuffer>(entity))
            {
                ref var buffer = ref world.Get<OrderBuffer>(entity);
                ActivateOrder(world, entity, ref tags, ref buffer, in order, in config);
            }
        }

        private static void ActivateOrder(
            World world,
            Entity entity,
            ref GameplayTagContainer tags,
            ref OrderBuffer buffer,
            in Order order,
            in OrderTypeConfig config)
        {
            // 1. Add the order-specific active tag
            if (config.OrderStateTagId > 0)
            {
                tags.AddTag(config.OrderStateTagId);
            }

            // 2. Add HasActive state tag
            tags.AddTag(OrderStateTags.State_HasActive);

            // 3. Write order parameters to Blackboard
            WriteOrderToBlackboard(world, entity, in order, in config);

            // 4. Sync OrderBuffer active state so buffer.HasActive is consistent with tags.
            //    Skip if buffer is already active (e.g., set by PromoteNext).
            if (!buffer.HasActive)
            {
                buffer.SetActiveDirect(in order, config.Priority);
            }
        }
        
        /// <summary>
        /// Deactivate the current order: Remove Tag, clear Blackboard data, and reset buffer active state.
        /// Uses the active order's config from the registry for data-driven cleanup.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DeactivateCurrentOrder(
            World world,
            Entity entity,
            ref GameplayTagContainer tags,
            ref OrderBuffer buffer,
            OrderTypeRegistry registry)
        {
            // Remove all Order.Active.* tags using bitmask (100-109)
            // This is more efficient than looping when multiple tags may be set
            tags.RemoveTagRange(OrderStateTags.Active_CastAbility, OrderStateTags.Active_Reserved9);
            
            // Clear order-related Blackboard entries based on active order's config
            if (buffer.HasActive)
            {
                var activeConfig = registry.Get(buffer.ActiveOrder.Order.OrderTagId);
                ClearOrderBlackboard(world, entity, in activeConfig);
                buffer.ClearActive();
            }
        }
        
        /// <summary>
        /// Write order parameters to Blackboard components.
        /// All key mappings are data-driven via OrderTypeConfig — no hardcoded switch.
        /// </summary>
        private static void WriteOrderToBlackboard(
            World world,
            Entity entity,
            in Order order,
            in OrderTypeConfig config)
        {
            // Write spatial data if present (key from config)
            if (config.SpatialBlackboardKey >= 0 &&
                order.Args.Spatial.Kind != OrderSpatialKind.None && 
                world.Has<BlackboardSpatialBuffer>(entity))
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
                                var point = new Vector3(px[i], py[i], pz[i]);
                                spatial.AppendPoint(spatialKey, point);
                            }
                        }
                    }
                }
            }
            
            // Write target entity if present (key from config)
            if (config.EntityBlackboardKey >= 0 &&
                order.Target != default && 
                world.Has<BlackboardEntityBuffer>(entity))
            {
                ref var entities = ref world.Get<BlackboardEntityBuffer>(entity);
                entities.Set(config.EntityBlackboardKey, order.Target);
            }
            
            // Write integer arg0 if configured
            if (config.IntArg0BlackboardKey >= 0 &&
                world.Has<BlackboardIntBuffer>(entity))
            {
                ref var ints = ref world.Get<BlackboardIntBuffer>(entity);
                ints.Set(config.IntArg0BlackboardKey, order.Args.I0);
            }
        }
        
        /// <summary>
        /// Clear order-related Blackboard entries using config-driven keys.
        /// Only clears the keys associated with the specific order type.
        /// </summary>
        private static void ClearOrderBlackboard(World world, Entity entity, in OrderTypeConfig config)
        {
            // Clear spatial data using config-driven key
            if (config.SpatialBlackboardKey >= 0 && world.Has<BlackboardSpatialBuffer>(entity))
            {
                ref var spatial = ref world.Get<BlackboardSpatialBuffer>(entity);
                spatial.ClearPoints(config.SpatialBlackboardKey);
            }
            
            // Clear entity reference using config-driven key
            if (config.EntityBlackboardKey >= 0 && world.Has<BlackboardEntityBuffer>(entity))
            {
                ref var entities = ref world.Get<BlackboardEntityBuffer>(entity);
                entities.Remove(config.EntityBlackboardKey);
            }
            
            // Clear int arg0 using config-driven key
            if (config.IntArg0BlackboardKey >= 0 && world.Has<BlackboardIntBuffer>(entity))
            {
                ref var ints = ref world.Get<BlackboardIntBuffer>(entity);
                ints.Remove(config.IntArg0BlackboardKey);
            }
        }
        
        /// <summary>
        /// Check if the order is blocked by current tags.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsBlocked(in GameplayTagContainer tags, in TagRuleCompiled ruleCompiled)
        {
            return tags.Intersects(in ruleCompiled.BlockedMask);
        }
        
        /// <summary>
        /// Check if the new order can interrupt the current one.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CanInterrupt(
            ref GameplayTagContainer tags,
            in OrderTypeConfig newConfig,
            TagRuleRegistry? tagRuleRegistry)
        {
            if (tagRuleRegistry == null || newConfig.OrderStateTagId <= 0) return false;
            
            if (!tagRuleRegistry.HasRule(newConfig.OrderStateTagId)) return false;
            
            ref readonly var ruleCompiled = ref tagRuleRegistry.Get(newConfig.OrderStateTagId);
            
            // Check if any of the RemovedTags are currently active
            return tags.Intersects(in ruleCompiled.RemovedMask);
        }
        
        /// <summary>
        /// Calculate the expiration step for an order.
        /// </summary>
        /// <param name="config">Order type configuration.</param>
        /// <param name="currentStep">Current simulation step.</param>
        /// <param name="stepRateHz">Step domain tick rate in Hz (e.g. 30 for 30 steps/sec).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CalculateExpireStep(in OrderTypeConfig config, int currentStep, int stepRateHz)
        {
            if (config.BufferWindowMs <= 0) return -1;
            
            int bufferTicks = (config.BufferWindowMs * stepRateHz) / 1000;
            if (bufferTicks < 1) bufferTicks = 1;
            return currentStep + bufferTicks;
        }
        
        /// <summary>
        /// Notify that the current order has completed.
        /// Promotes the next queued order to active.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="entity">The entity.</param>
        /// <param name="registry">The order type registry.</param>
        public static void NotifyOrderComplete(World world, Entity entity, OrderTypeRegistry registry)
        {
            if (!world.IsAlive(entity)) return;
            if (!world.Has<OrderBuffer>(entity)) return;
            if (!world.Has<GameplayTagContainer>(entity)) return;
            
            ref var buffer = ref world.Get<OrderBuffer>(entity);
            ref var tags = ref world.Get<GameplayTagContainer>(entity);
            
            // Deactivate current order
            DeactivateCurrentOrder(world, entity, ref tags, ref buffer, registry);
            
            // Promote next order from queue
            if (buffer.PromoteNext())
            {
                var nextOrder = buffer.ActiveOrder.Order;
                var nextConfig = registry.Get(nextOrder.OrderTagId);
                ActivateOrder(world, entity, ref tags, ref buffer, in nextOrder, in nextConfig);
            }
            else
            {
                // No more orders - remove HasActive tag
                tags.RemoveTag(OrderStateTags.State_HasActive);
            }
            
            // Update HasQueued tag
            if (!buffer.HasQueued && tags.HasTag(OrderStateTags.State_HasQueued))
            {
                tags.RemoveTag(OrderStateTags.State_HasQueued);
            }
        }

        public static bool TryPromoteNextQueuedToActive(World world, Entity entity, OrderTypeRegistry registry)
        {
            if (!world.IsAlive(entity)) return false;
            if (!world.Has<OrderBuffer>(entity)) return false;
            if (!world.Has<GameplayTagContainer>(entity)) return false;

            ref var buffer = ref world.Get<OrderBuffer>(entity);
            ref var tags = ref world.Get<GameplayTagContainer>(entity);
            return TryPromoteNextQueuedToActive(world, entity, ref buffer, ref tags, registry);
        }

        public static bool TryPromoteNextQueuedToActive(
            World world,
            Entity entity,
            ref OrderBuffer buffer,
            ref GameplayTagContainer tags,
            OrderTypeRegistry registry)
        {
            if (buffer.HasActive || tags.HasTag(OrderStateTags.State_HasActive)) return false;

            if (!buffer.HasQueued)
            {
                if (tags.HasTag(OrderStateTags.State_HasQueued))
                {
                    tags.RemoveTag(OrderStateTags.State_HasQueued);
                }
                return false;
            }

            if (!buffer.PromoteNext())
            {
                if (tags.HasTag(OrderStateTags.State_HasQueued))
                {
                    tags.RemoveTag(OrderStateTags.State_HasQueued);
                }
                return false;
            }

            var nextOrder = buffer.ActiveOrder.Order;
            var nextConfig = registry.Get(nextOrder.OrderTagId);
            ActivateOrder(world, entity, ref tags, ref buffer, in nextOrder, in nextConfig);

            if (!buffer.HasQueued && tags.HasTag(OrderStateTags.State_HasQueued))
            {
                tags.RemoveTag(OrderStateTags.State_HasQueued);
            }

            return true;
        }
        
        /// <summary>
        /// Cancel the current order (Stop command).
        /// Does not clear the queue - next order will execute.
        /// </summary>
        public static void CancelCurrent(World world, Entity entity, OrderTypeRegistry registry)
        {
            NotifyOrderComplete(world, entity, registry);
        }
        
        /// <summary>
        /// Cancel all orders (Stop + clear queue).
        /// </summary>
        public static void CancelAll(World world, Entity entity, OrderTypeRegistry registry)
        {
            if (!world.IsAlive(entity)) return;
            if (!world.Has<OrderBuffer>(entity)) return;
            if (!world.Has<GameplayTagContainer>(entity)) return;
            
            ref var buffer = ref world.Get<OrderBuffer>(entity);
            ref var tags = ref world.Get<GameplayTagContainer>(entity);
            
            // Deactivate current order
            DeactivateCurrentOrder(world, entity, ref tags, ref buffer, registry);

            // Clear queue
            buffer.Clear();
            
            // Remove state tags
            tags.RemoveTag(OrderStateTags.State_HasActive);
            tags.RemoveTag(OrderStateTags.State_HasQueued);
        }
        
        
    }
}
