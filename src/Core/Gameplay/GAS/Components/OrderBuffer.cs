using System.Runtime.CompilerServices;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.GAS.Components
{
    public struct QueuedOrder
    {
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
    
    public struct OrderBuffer
    {
        public const int MAX_QUEUED_ORDERS = 8;
        
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
        
        public QueuedOrder ActiveOrder;

        /// <summary>
        /// Runtime-owned integer slot for the active order's execution cursor.
        /// Must not be used to rewrite authored order payload.
        /// </summary>
        public int ActiveRuntimeInt0;
        
        /// <summary>
        /// Pending order slot - stores a blocked order for automatic retry when the
        /// current order completes. Only one pending order per entity (last-write-wins).
        /// Used for "input buffering" (e.g., pressing Q during another skill's GCD).
        /// </summary>
        public QueuedOrder PendingOrder;
        
        public bool HasPending;
        
        /// <summary>
        /// Sorted by (Priority DESC, InsertStep ASC).
        /// </summary>
        private QueuedOrderArray _queuedOrders;
        
        public readonly bool HasActive => ActiveIndex >= 0;
        
        public readonly bool HasQueued => QueuedCount > 0;
        
        public readonly bool IsEmpty => ActiveIndex < 0 && QueuedCount == 0;
        
        public readonly QueuedOrder GetQueued(int index)
        {
            if ((uint)index >= (uint)QueuedCount) return default;
            return _queuedOrders[index];
        }
        
        internal void SetQueued(int index, in QueuedOrder order)
        {
            if ((uint)index >= MAX_QUEUED_ORDERS) return;
            _queuedOrders[index] = order;
        }
        
        /// <summary>
        /// Maintains sorted order by (Priority DESC, InsertStep ASC).
        /// </summary>
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
            
            int insertIndex = QueuedCount;
            for (int i = 0; i < QueuedCount; i++)
            {
                var existing = GetQueued(i);
                if (priority > existing.Priority || 
                    (priority == existing.Priority && insertStep < existing.InsertStep))
                {
                    insertIndex = i;
                    break;
                }
            }
            
            for (int i = QueuedCount; i > insertIndex; i--)
            {
                SetQueued(i, GetQueued(i - 1));
            }
            
            SetQueued(insertIndex, queuedOrder);
            QueuedCount++;
            return true;
        }
        
        /// <returns>The removed order, or default if invalid index.</returns>
        public QueuedOrder RemoveAt(int index)
        {
            QueuedOrder candidate = GetQueued(index);
            if (candidate.Order.Args.Spatial.Payload.IsValid)
            {
                throw new System.InvalidOperationException(
                    "ORDER.BUFFER.ERR.QueuedPayloadRemoveRequiresOwner");
            }

            return RemoveAtTransferred(index);
        }

        internal QueuedOrder RemoveAtTransferred(int index)
        {
            if ((uint)index >= (uint)QueuedCount) return default;
            
            var removed = GetQueued(index);
            
            for (int i = index; i < QueuedCount - 1; i++)
            {
                SetQueued(i, GetQueued(i + 1));
            }
            
            QueuedCount--;
            return removed;
        }
        
        public bool RemoveOldestOfType(int orderTypeId)
        {
            // Find the oldest (highest index due to FIFO ordering within priority)
            for (int i = QueuedCount - 1; i >= 0; i--)
            {
                var queued = GetQueued(i);
                if (queued.Order.OrderTypeId == orderTypeId)
                {
                    RemoveAt(i);
                    return true;
                }
            }
            return false;
        }
        
        public int RemoveAllOfType(int orderTypeId)
        {
            int removed = 0;
            for (int i = QueuedCount - 1; i >= 0; i--)
            {
                var queued = GetQueued(i);
                if (queued.Order.OrderTypeId == orderTypeId)
                {
                    RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }
        
        public readonly int CountOfType(int orderTypeId)
        {
            int count = 0;
            for (int i = 0; i < QueuedCount; i++)
            {
                if (GetQueued(i).Order.OrderTypeId == orderTypeId)
                {
                    count++;
                }
            }
            return count;
        }
        
        public bool PromoteNext()
        {
            if (QueuedCount == 0) return false;
            
            ActiveOrder = RemoveAtTransferred(0);
            ActiveIndex = 0;
            ActiveRuntimeInt0 = 0;
            return true;
        }
        
        /// <summary>
        /// Set the active order directly (for orders activated via HandleImmediateMode
        /// that bypass the queue). Ensures buffer.HasActive stays consistent with the active-order slot.
        /// </summary>
        public void SetActiveDirect(in Order order, int priority)
        {
            if (HasActive && ActiveOrder.Order.Args.Spatial.Payload.IsValid)
            {
                throw new System.InvalidOperationException(
                    "ORDER.BUFFER.ERR.ActivePayloadOverwriteRequiresOwner");
            }

            ActiveOrder = new QueuedOrder
            {
                Order = order,
                Priority = priority,
                ExpireStep = -1,
                InsertStep = 0
            };
            ActiveIndex = 0;
            ActiveRuntimeInt0 = 0;
        }
        
        public void ClearActive()
        {
            if (HasActive && ActiveOrder.Order.Args.Spatial.Payload.IsValid)
            {
                throw new System.InvalidOperationException(
                    "ORDER.BUFFER.ERR.ActivePayloadClearRequiresOwner");
            }

            ClearActiveTransferred();
        }

        internal void ClearActiveTransferred()
        {
            ActiveIndex = -1;
            ActiveOrder = default;
            ActiveRuntimeInt0 = 0;
        }
        
        public void ClearQueued()
        {
            for (int i = 0; i < QueuedCount; i++)
            {
                if (GetQueued(i).Order.Args.Spatial.Payload.IsValid)
                {
                    throw new System.InvalidOperationException(
                        "ORDER.BUFFER.ERR.QueuedPayloadClearRequiresOwner");
                }
            }

            QueuedCount = 0;
        }
        
        public void Clear()
        {
            ClearActive();
            ClearQueued();
            ClearPending();
        }
        
        public int RemoveExpired(int currentStep)
        {
            int removed = 0;
            for (int i = QueuedCount - 1; i >= 0; i--)
            {
                var queued = GetQueued(i);
                if (queued.ExpireStep >= 0 && queued.ExpireStep <= currentStep)
                {
                    if (queued.Order.Args.Spatial.Payload.IsValid)
                    {
                        throw new System.InvalidOperationException(
                            "ORDER.BUFFER.ERR.QueuedPayloadExpireRequiresOwner");
                    }

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
        /// <param name="expireStep">Step at which the pending order expires.</param>
        /// <param name="insertStep">Step at which the order was submitted.</param>
        public void SetPending(in Order order, int priority, int expireStep, int insertStep)
        {
            if (HasPending && PendingOrder.Order.Args.Spatial.Payload.IsValid)
            {
                throw new System.InvalidOperationException(
                    "ORDER.BUFFER.ERR.PendingPayloadOverwriteRequiresOwner");
            }

            PendingOrder = new QueuedOrder
            {
                Order = order,
                Priority = priority,
                ExpireStep = expireStep,
                InsertStep = insertStep
            };
            HasPending = true;
        }
        
        public void ClearPending()
        {
            if (HasPending && PendingOrder.Order.Args.Spatial.Payload.IsValid)
            {
                throw new System.InvalidOperationException(
                    "ORDER.BUFFER.ERR.PendingPayloadClearRequiresOwner");
            }

            ClearPendingTransferred();
        }

        internal void ClearPendingTransferred()
        {
            HasPending = false;
            PendingOrder = default;
        }
        
        public bool ExpirePending(int currentStep)
        {
            if (HasPending && PendingOrder.ExpireStep >= 0 && PendingOrder.ExpireStep <= currentStep)
            {
                if (PendingOrder.Order.Args.Spatial.Payload.IsValid)
                {
                    throw new System.InvalidOperationException(
                        "ORDER.BUFFER.ERR.PendingPayloadExpireRequiresOwner");
                }

                ClearPending();
                return true;
            }
            return false;
        }
        
        public static OrderBuffer CreateEmpty()
        {
            return new OrderBuffer { ActiveIndex = -1, QueuedCount = 0 };
        }
    }
}
