using System.Text.Json.Serialization;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    /// <summary>
    /// Policy for handling multiple same-type orders.
    /// </summary>
    public enum SameTypePolicy
    {
        /// <summary>
        /// Queue all orders (e.g., pressing Q Q Q queues 3 orders).
        /// </summary>
        Queue = 0,
        
        /// <summary>
        /// Replace queued orders with the new one (only keep the latest).
        /// </summary>
        Replace = 1,
        
        /// <summary>
        /// Ignore new orders (only execute the first one).
        /// </summary>
        Ignore = 2
    }
    
    /// <summary>
    /// Policy for handling queue overflow.
    /// </summary>
    public enum QueueFullPolicy
    {
        /// <summary>
        /// Drop the oldest queued order to make room for the new one.
        /// </summary>
        DropOldest = 0,
        
        /// <summary>
        /// Reject the new order when queue is full.
        /// </summary>
        RejectNew = 1
    }
    
    /// <summary>
    /// Configuration for an order type.
    /// Defines queuing behavior, priority, and tag associations.
    /// </summary>
    public class OrderTypeConfig
    {
        /// <summary>
        /// The order tag ID (matches OrderTagId in Order struct).
        /// </summary>
        public int OrderTagId { get; set; }
        
        /// <summary>
        /// Human-readable label for this order type.
        /// </summary>
        public string Label { get; set; } = string.Empty;
        
        /// <summary>
        /// Maximum number of same-type orders that can be queued.
        /// </summary>
        public int MaxQueueSize { get; set; } = 3;
        
        /// <summary>
        /// How to handle multiple same-type orders.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SameTypePolicy SameTypePolicy { get; set; } = SameTypePolicy.Queue;
        
        /// <summary>
        /// How to handle queue overflow.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public QueueFullPolicy QueueFullPolicy { get; set; } = QueueFullPolicy.DropOldest;
        
        /// <summary>
        /// Priority for queue ordering (higher = processed first).
        /// </summary>
        public int Priority { get; set; } = 100;
        
        /// <summary>
        /// Buffer window in milliseconds (order expires if not executed within this time).
        /// Set to 0 or negative for no expiration.
        /// </summary>
        public int BufferWindowMs { get; set; } = 500;
        
        /// <summary>
        /// Pending buffer window in milliseconds. When an order is blocked (e.g., during GCD),
        /// it is stored as "pending" and retried when the current order completes.
        /// Set to 0 or negative to disable pending buffer for this order type.
        /// Defaults to 400ms (typical GCD input buffer for action games).
        /// </summary>
        public int PendingBufferWindowMs { get; set; } = 400;
        
        /// <summary>
        /// Whether a new order of this type can interrupt the current one of the same type.
        /// </summary>
        public bool CanInterruptSelf { get; set; } = false;
        
        /// <summary>
        /// The OrderState tag ID to add when this order is active.
        /// Should be in the Order.Active.* range (100-109).
        /// A value of 0 means "not set" and should be explicitly configured per order type.
        /// </summary>
        public int OrderStateTagId { get; set; } = 0;
        
        // ========== Queued Mode Settings ==========
        
        /// <summary>
        /// Maximum queue size for Queued mode (modifier+action) commands.
        /// Typically larger than MaxQueueSize since it's explicit user intent.
        /// </summary>
        public int QueuedModeMaxSize { get; set; } = 16;
        
        /// <summary>
        /// Whether this order type allows Queued mode.
        /// </summary>
        public bool AllowQueuedMode { get; set; } = true;
        
        // ========== Interrupt Settings ==========
        
        /// <summary>
        /// Whether this order clears the queue when activated via Immediate mode.
        /// If true, activating this order clears all queued orders.
        /// Typically true for movement (right-click clears Shift+ queue).
        /// </summary>
        public bool ClearQueueOnActivate { get; set; } = true;
        
        // ========== Blackboard Key Mapping (data-driven) ==========
        
        /// <summary>
        /// Blackboard key for spatial data (waypoints, target positions).
        /// Set to -1 if this order type has no spatial data.
        /// </summary>
        public int SpatialBlackboardKey { get; set; } = OrderBlackboardKeys.Generic_TargetPosition;
        
        /// <summary>
        /// Blackboard key for target entity reference.
        /// Set to -1 if this order type has no entity target.
        /// </summary>
        public int EntityBlackboardKey { get; set; } = OrderBlackboardKeys.Generic_TargetEntity;
        
        /// <summary>
        /// Blackboard key for the primary int argument (I0).
        /// Set to -1 if unused.
        /// </summary>
        public int IntArg0BlackboardKey { get; set; } = -1;
        
        // ========== Validation Settings ==========
        
        /// <summary>
        /// Optional graph program ID for client-side order validation.
        /// When set (> 0), the graph program is executed before the order is submitted.
        /// Context: E[0] = caster, E[1] = target, targetPos = spatial data.
        /// Result: B[0] = 1 → pass, B[0] = 0 → reject.
        /// Set to 0 to disable validation for this order type.
        /// </summary>
        public int ValidationGraphId { get; set; } = 0;
    }
}
