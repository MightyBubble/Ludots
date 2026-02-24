using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.Core.Extensions;
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
    /// <summary>
    /// System that manages per-Entity OrderBuffer components.
    /// Handles order expiration, queue promotion, tag synchronization,
    /// and routes incoming orders from the global OrderQueue to per-entity buffers.
    /// Replaces OrderDispatchSystem as the single entry point for all orders.
    /// Chain orders (ResponseChain) are filtered and routed to a separate queue.
    /// </summary>
    public sealed class OrderBufferSystem : BaseSystem<World, float>
    {
        private readonly IClock _clock;
        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly TagRuleRegistry _tagRuleRegistry;
        private readonly OrderQueue? _incomingOrders;
        private readonly int _stepRateHz;
        
        // Chain order routing (ResponseChain orders bypass per-entity buffer)
        private readonly OrderQueue? _chainOrders;
        private readonly int _respondChainOrderTagId;
        
        // Optional: graph-based order validation
        private readonly GraphProgramRegistry? _graphProgramRegistry;
        private readonly IGraphRuntimeApi? _graphApi;
        
        // Query description for entities with OrderBuffer
        private static readonly QueryDescription _orderBufferQuery = new QueryDescription()
            .WithAll<OrderBuffer, GameplayTagContainer>();
        
        public OrderBufferSystem(
            World world,
            IClock clock,
            OrderTypeRegistry orderTypeRegistry,
            TagRuleRegistry tagRuleRegistry,
            OrderQueue? incomingOrders = null,
            int stepRateHz = 30,
            OrderQueue? chainOrders = null,
            int respondChainOrderTagId = -1,
            GraphProgramRegistry? graphProgramRegistry = null,
            IGraphRuntimeApi? graphApi = null)
            : base(world)
        {
            _clock = clock;
            _orderTypeRegistry = orderTypeRegistry;
            _tagRuleRegistry = tagRuleRegistry;
            _incomingOrders = incomingOrders;
            _stepRateHz = stepRateHz > 0 ? stepRateHz : 30;
            _chainOrders = chainOrders;
            _respondChainOrderTagId = respondChainOrderTagId;
            _graphProgramRegistry = graphProgramRegistry;
            _graphApi = graphApi;
        }
        
        public override void Update(in float dt)
        {
            int currentStep = _clock.Now(ClockDomainId.Step);
            
            // Process incoming orders from the global queue (for backwards compatibility)
            ProcessIncomingOrders(currentStep);
            
            // Process all entities with OrderBuffer using struct job (zero allocation)
            var job = new OrderBufferUpdateJob
            {
                CurrentStep = currentStep
            };
            World.InlineQuery<OrderBufferUpdateJob, OrderBuffer, GameplayTagContainer>(in _orderBufferQuery, ref job);

            World.Query(in _orderBufferQuery, (Entity entity, ref OrderBuffer buffer, ref GameplayTagContainer tags) =>
            {
                if (!buffer.HasActive && buffer.HasQueued)
                {
                    OrderSubmitter.TryPromoteNextQueuedToActive(World, entity, ref buffer, ref tags, _orderTypeRegistry);
                }
            });
        }
        
        /// <summary>
        /// Zero-allocation job struct for OrderBuffer processing.
        /// </summary>
        private struct OrderBufferUpdateJob : IForEach<OrderBuffer, GameplayTagContainer>
        {
            public int CurrentStep;
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref OrderBuffer buffer, ref GameplayTagContainer tags)
            {
                // Remove expired orders
                buffer.RemoveExpired(CurrentStep);
                
                // Expire pending order if timed out
                buffer.ExpirePending(CurrentStep);
                
                // Sync HasQueued tag (branchless-friendly)
                bool hasQueued = buffer.HasQueued;
                bool tagHasQueued = tags.HasTag(OrderStateTags.State_HasQueued);
                if (hasQueued != tagHasQueued)
                {
                    if (hasQueued) tags.AddTag(OrderStateTags.State_HasQueued);
                    else tags.RemoveTag(OrderStateTags.State_HasQueued);
                }
                
                // Sync HasActive tag (branchless-friendly)
                bool hasActive = buffer.HasActive;
                bool tagHasActive = tags.HasTag(OrderStateTags.State_HasActive);
                if (hasActive != tagHasActive)
                {
                    if (hasActive) tags.AddTag(OrderStateTags.State_HasActive);
                    else tags.RemoveTag(OrderStateTags.State_HasActive);
                }
            }
        }
        
        private void ProcessIncomingOrders(int currentStep)
        {
            if (_incomingOrders == null) return;
            
            while (_incomingOrders.TryDequeue(out var order))
            {
                order.SubmitStep = currentStep;
                
                // Chain orders (ResponseChain) bypass per-entity buffer entirely
                // and route directly to the dedicated chain order queue.
                if (_respondChainOrderTagId >= 0 && order.OrderTagId == _respondChainOrderTagId)
                {
                    _chainOrders?.TryEnqueue(order);
                    continue;
                }
                
                // Check if actor entity has OrderBuffer
                if (!World.IsAlive(order.Actor)) continue;
                if (!World.Has<OrderBuffer>(order.Actor)) continue;
                if (!World.Has<GameplayTagContainer>(order.Actor)) continue;
                
                // Look up config once for validation and pending buffer
                var config = _orderTypeRegistry.Get(order.OrderTagId);
                
                // Run optional graph-based validation before submission
                if (config.ValidationGraphId > 0 && _graphProgramRegistry != null && _graphApi != null)
                {
                    if (_graphProgramRegistry.TryGetProgram(config.ValidationGraphId, out var validationProgram))
                    {
                        var targetPos = new IntVector2((int)order.Args.Spatial.WorldCm.X, (int)order.Args.Spatial.WorldCm.Z);
                        bool passed = GasGraphExecutor.ExecuteValidation(
                            World, order.Actor, order.Target, targetPos,
                            validationProgram, _graphApi);
                        if (!passed) continue; // Validation rejected — skip this order
                    }
                }

                // Submit through OrderSubmitter (handles queuing, priority, tag activation)
                var result = OrderSubmitter.Submit(
                    World,
                    order.Actor,
                    in order,
                    _orderTypeRegistry,
                    _tagRuleRegistry,
                    currentStep,
                    _stepRateHz);
                
                // If blocked, store as pending for automatic retry when current order completes
                if (result == OrderSubmitResult.Blocked && config.PendingBufferWindowMs > 0)
                {
                    int pendingExpireStep = currentStep + (config.PendingBufferWindowMs * _stepRateHz) / 1000;
                    ref var buffer = ref World.Get<OrderBuffer>(order.Actor);
                    buffer.SetPending(in order, config.Priority, pendingExpireStep, currentStep);
                }
            }
        }
        
        /// <summary>
        /// Submit an order to an entity.
        /// AI and player use the same interface - no special handling.
        /// </summary>
        public OrderSubmitResult SubmitOrder(Entity entity, in Order order)
        {
            int currentStep = _clock.Now(ClockDomainId.Step);
            return OrderSubmitter.Submit(
                World,
                entity,
                in order,
                _orderTypeRegistry,
                _tagRuleRegistry,
                currentStep,
                _stepRateHz);
        }
        
        /// <summary>
        /// Notify that an entity's current order has completed.
        /// After promoting queued orders, tries to submit any pending (blocked) order.
        /// </summary>
        public void NotifyOrderComplete(Entity entity)
        {
            OrderSubmitter.NotifyOrderComplete(World, entity, _orderTypeRegistry);
            
            // After queued promotion, try submitting the pending order if no order is now active
            TrySubmitPending(entity);
        }
        
        /// <summary>
        /// Try to submit the pending order for an entity.
        /// Called after NotifyOrderComplete when the current order finishes.
        /// </summary>
        private void TrySubmitPending(Entity entity)
        {
            if (!World.IsAlive(entity)) return;
            if (!World.Has<OrderBuffer>(entity)) return;
            
            ref var buffer = ref World.Get<OrderBuffer>(entity);
            if (!buffer.HasPending) return;
            
            // If there's already an active order (promoted from queue), don't retry pending
            if (buffer.HasActive) return;
            
            var pendingOrder = buffer.PendingOrder.Order;
            buffer.ClearPending();
            
            int currentStep = _clock.Now(ClockDomainId.Step);
            OrderSubmitter.Submit(
                World,
                entity,
                in pendingOrder,
                _orderTypeRegistry,
                _tagRuleRegistry,
                currentStep,
                _stepRateHz);
        }
        
        /// <summary>
        /// Get the active order for an entity, if any.
        /// </summary>
        public bool TryGetActiveOrder(Entity entity, out Order order)
        {
            order = default;
            if (!World.IsAlive(entity)) return false;
            if (!World.Has<OrderBuffer>(entity)) return false;
            
            ref var buffer = ref World.Get<OrderBuffer>(entity);
            if (!buffer.HasActive) return false;
            
            order = buffer.ActiveOrder.Order;
            return true;
        }
        
        /// <summary>
        /// Get the OrderTypeRegistry.
        /// </summary>
        public OrderTypeRegistry OrderTypeRegistry => _orderTypeRegistry;
        
        /// <summary>
        /// Get the TagRuleRegistry.
        /// </summary>
        public TagRuleRegistry TagRuleRegistry => _tagRuleRegistry;
    }
}
