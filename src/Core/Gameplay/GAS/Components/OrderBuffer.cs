using System.Runtime.CompilerServices;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// A queued order with metadata for priority and expiration.
    /// </summary>
    public struct QueuedOrder
    {
        /// <summary>
        /// The order data.
        /// </summary>
        public Order Order;
        
        /// <summary>
        /// Priority for queue ordering (higher = processed first).
        /// </summary>
        public int Priority;
        
        /// <summary>
        /// Step at which this order expires (-1 for no expiration).
        /// </summary>
        public int ExpireStep;
        
        /// <summary>
        /// Step at which this order was inserted (for FIFO within same priority).
        /// </summary>
        public int InsertStep;
    }
    
    /// <summary>
    /// Per-Entity order buffer component.
    /// Stores the active order and queued orders.
    /// </summary>
    public struct OrderBuffer
    {
        /// <summary>
        /// Maximum number of queued orders per entity.
        /// </summary>
        public const int MAX_QUEUED_ORDERS = 8;
        
        /// <summary>
        /// Inline fixed-size array of QueuedOrder, sized by the compiler.
        /// No manual sizeof calculation needed.
        /// </summary>
        [InlineArray(MAX_QUEUED_ORDERS)]
        private struct QueuedOrderArray
        {
            private QueuedOrder _element;
        }
        
        /// <summary>
        /// Index of the active order in the queue, or -1 if none.
        /// </summary>
        public int ActiveIndex;
        
        /// <summary>
        /// Number of queued orders (excluding active).
        /// </summary>
        public int QueuedCount;
        
        /// <summary>
        /// The currently active order (if ActiveIndex >= 0).
        /// </summary>
        public QueuedOrder ActiveOrder;
        
        /// <summary>
        /// Pending order slot — stores a blocked order for automatic retry when the
        /// current order completes. Only one pending order per entity (last-write-wins).
        /// Used for "input buffering" (e.g., pressing Q during another skill's GCD).
        /// </summary>
        public QueuedOrder PendingOrder;
        
        /// <summary>
        /// Whether there is a pending order waiting for retry.
        /// </summary>
        public bool HasPending;
        
        /// <summary>
        /// Fixed-size array of queued orders.
        /// Sorted by (Priority DESC, InsertStep ASC).
        /// </summary>
        private QueuedOrderArray _queuedOrders;
        
        /// <summary>
        /// Check if there is an active order.
        /// </summary>
        public readonly bool HasActive => ActiveIndex >= 0;
        
        /// <summary>
        /// Check if there are queued orders.
        /// </summary>
        public readonly bool HasQueued => QueuedCount > 0;
        
        /// <summary>
        /// Check if the buffer is empty (no active and no queued).
        /// </summary>
        public readonly bool IsEmpty => ActiveIndex < 0 && QueuedCount == 0;
        
        /// <summary>
        /// Get a queued order by index.
        /// </summary>
        /// <param name="index">Index in the queue (0 to QueuedCount-1).</param>
        /// <returns>The queued order.</returns>
        public readonly QueuedOrder GetQueued(int index)
        {
            if ((uint)index >= (uint)QueuedCount) return default;
            return _queuedOrders[index];
        }
        
        /// <summary>
        /// Set a queued order at a specific index.
        /// </summary>
        private void SetQueued(int index, in QueuedOrder order)
        {
            if ((uint)index >= MAX_QUEUED_ORDERS) return;
            _queuedOrders[index] = order;
        }
        
        /// <summary>
        /// Enqueue an order with the given priority and expiration.
        /// Maintains sorted order by (Priority DESC, InsertStep ASC).
        /// </summary>
        /// <param name="order">The order to enqueue.</param>
        /// <param name="priority">Priority (higher = processed first).</param>
        /// <param name="expireStep">Step at which the order expires (-1 for no expiration).</param>
        /// <param name="insertStep">Current step for FIFO ordering.</param>
        /// <returns>True if enqueued successfully, false if queue is full.</returns>
        public bool Enqueue(in Order order, int priority, int expireStep, int insertStep)
        {
            if (QueuedCount >= MAX_QUEUED_ORDERS) return false;
            
            var queuedOrder = new QueuedOrder
            {
                Order = order,
                Priority = priority,
                ExpireStep = expireStep,
                InsertStep = insertStep
            };
            
            // Find insertion point to maintain sorted order
            int insertIndex = QueuedCount;
            for (int i = 0; i < QueuedCount; i++)
            {
                var existing = GetQueued(i);
                // Higher priority comes first; same priority uses FIFO (lower InsertStep first)
                if (priority > existing.Priority || 
                    (priority == existing.Priority && insertStep < existing.InsertStep))
                {
                    insertIndex = i;
                    break;
                }
            }
            
            // Shift elements to make room
            for (int i = QueuedCount; i > insertIndex; i--)
            {
                SetQueued(i, GetQueued(i - 1));
            }
            
            SetQueued(insertIndex, queuedOrder);
            QueuedCount++;
            return true;
        }
        
        /// <summary>
        /// Remove a queued order by index.
        /// </summary>
        /// <param name="index">Index in the queue.</param>
        /// <returns>The removed order, or default if invalid index.</returns>
        public QueuedOrder RemoveAt(int index)
        {
            if ((uint)index >= (uint)QueuedCount) return default;
            
            var removed = GetQueued(index);
            
            // Shift elements to fill the gap
            for (int i = index; i < QueuedCount - 1; i++)
            {
                SetQueued(i, GetQueued(i + 1));
            }
            
            QueuedCount--;
            return removed;
        }
        
        /// <summary>
        /// Remove the oldest queued order of a specific type.
        /// </summary>
        /// <param name="orderTagId">The order tag ID to match.</param>
        /// <returns>True if an order was removed, false otherwise.</returns>
        public bool RemoveOldestOfType(int orderTagId)
        {
            // Find the oldest (highest index due to FIFO ordering within priority)
            for (int i = QueuedCount - 1; i >= 0; i--)
            {
                var queued = GetQueued(i);
                if (queued.Order.OrderTagId == orderTagId)
                {
                    RemoveAt(i);
                    return true;
                }
            }
            return false;
        }
        
        /// <summary>
        /// Remove all queued orders of a specific type.
        /// </summary>
        /// <param name="orderTagId">The order tag ID to match.</param>
        /// <returns>Number of orders removed.</returns>
        public int RemoveAllOfType(int orderTagId)
        {
            int removed = 0;
            for (int i = QueuedCount - 1; i >= 0; i--)
            {
                var queued = GetQueued(i);
                if (queued.Order.OrderTagId == orderTagId)
                {
                    RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }
        
        /// <summary>
        /// Count queued orders of a specific type.
        /// </summary>
        /// <param name="orderTagId">The order tag ID to match.</param>
        /// <returns>Number of matching orders.</returns>
        public readonly int CountOfType(int orderTagId)
        {
            int count = 0;
            for (int i = 0; i < QueuedCount; i++)
            {
                if (GetQueued(i).Order.OrderTagId == orderTagId)
                {
                    count++;
                }
            }
            return count;
        }
        
        /// <summary>
        /// Promote the first queued order to active.
        /// </summary>
        /// <returns>True if promoted, false if queue was empty.</returns>
        public bool PromoteNext()
        {
            if (QueuedCount == 0) return false;
            
            ActiveOrder = RemoveAt(0);
            ActiveIndex = 0;
            return true;
        }
        
        /// <summary>
        /// Set the active order directly (for orders activated via HandleImmediateMode
        /// that bypass the queue). Ensures buffer.HasActive stays consistent with tags.
        /// </summary>
        public void SetActiveDirect(in Order order, int priority)
        {
            ActiveOrder = new QueuedOrder
            {
                Order = order,
                Priority = priority,
                ExpireStep = -1,
                InsertStep = 0
            };
            ActiveIndex = 0;
        }
        
        /// <summary>
        /// Clear the active order.
        /// </summary>
        public void ClearActive()
        {
            ActiveIndex = -1;
            ActiveOrder = default;
        }
        
        /// <summary>
        /// Clear all queued orders.
        /// </summary>
        public void ClearQueued()
        {
            QueuedCount = 0;
        }
        
        /// <summary>
        /// Clear active, queued, and pending orders.
        /// </summary>
        public void Clear()
        {
            ClearActive();
            ClearQueued();
            ClearPending();
        }
        
        /// <summary>
        /// Remove expired orders based on current step.
        /// </summary>
        /// <param name="currentStep">The current simulation step.</param>
        /// <returns>Number of orders removed.</returns>
        public int RemoveExpired(int currentStep)
        {
            int removed = 0;
            for (int i = QueuedCount - 1; i >= 0; i--)
            {
                var queued = GetQueued(i);
                if (queued.ExpireStep >= 0 && queued.ExpireStep <= currentStep)
                {
                    RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }
        
        /// <summary>
        /// Store a blocked order as pending (for automatic retry).
        /// Overwrites any previous pending order (last-write-wins).
        /// </summary>
        /// <param name="order">The blocked order.</param>
        /// <param name="priority">The order's priority.</param>
        /// <param name="expireStep">Step at which the pending order expires.</param>
        /// <param name="insertStep">Step at which the order was submitted.</param>
        public void SetPending(in Order order, int priority, int expireStep, int insertStep)
        {
            PendingOrder = new QueuedOrder
            {
                Order = order,
                Priority = priority,
                ExpireStep = expireStep,
                InsertStep = insertStep
            };
            HasPending = true;
        }
        
        /// <summary>
        /// Clear the pending order slot.
        /// </summary>
        public void ClearPending()
        {
            HasPending = false;
            PendingOrder = default;
        }
        
        /// <summary>
        /// Expire the pending order if it has timed out.
        /// </summary>
        /// <param name="currentStep">Current simulation step.</param>
        /// <returns>True if the pending order was expired.</returns>
        public bool ExpirePending(int currentStep)
        {
            if (HasPending && PendingOrder.ExpireStep >= 0 && PendingOrder.ExpireStep <= currentStep)
            {
                ClearPending();
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Create an empty OrderBuffer.
        /// </summary>
        public static OrderBuffer CreateEmpty()
        {
            return new OrderBuffer { ActiveIndex = -1, QueuedCount = 0 };
        }
    }
}
