using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public enum OrderSubmitMode : byte
    {
        Immediate = 0,
        Queued = 1,
        PersistentQueued = 2
    }

    public enum OrderAdmissionSource : byte
    {
        Local = 0,
        Network = 1
    }

    public struct Order
    {
        public Order()
        {
            Target = Entity.Null;
            TargetContext = Entity.Null;
            CommandSource = Entity.Null;
        }

        public int OrderId;
        public int OrderTypeId;
        public int PlayerId;
        public Entity Actor;
        public Entity Target;
        public Entity TargetContext;
        public Entity CommandSource;
        public OrderArgs Args;
        public int SubmitStep;
        public OrderSubmitMode SubmitMode;
        public OrderAdmissionSource AdmissionSource;
        public int AdmissionBatchId;
        public ushort AdmissionBatchSize;
        public ushort AdmissionBatchIndex;
    }

    public sealed class OrderQueue
    {
        private readonly Order[] _items;
        private int _head;
        private int _tail;
        private int _count;
        private int _nextOrderId = 1;
        private int _nextAdmissionBatchId = 1;

        public OrderQueue(int capacity = 4096)
        {
            if (capacity < 64) capacity = 64;
            _items = new Order[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public int AvailableCapacity => _items.Length - _count;

        public bool TryEnqueue(in Order order)
        {
            var value = order;
            return TryEnqueueAssigned(ref value);
        }

        public bool TryEnqueueAssigned(ref Order order)
        {
            return TryEnqueueAssigned(ref order, out _);
        }

        public bool TryEnqueueAssigned(ref Order order, out OrderAdmissionOutcome outcome)
        {
            ValidateOrderTypeId(order.OrderTypeId);
            OrderEntityReferenceContract.Validate(in order, nameof(OrderQueue));
            if (_count >= _items.Length)
            {
                outcome = new OrderAdmissionOutcome(
                    in order,
                    OrderAdmissionStage.GlobalIntake,
                    OrderSubmitResult.QueueFull);
                return false;
            }

            EnsureOrderId(ref order);
            order.AdmissionSource = OrderAdmissionSource.Local;
            order.AdmissionBatchId = 0;
            order.AdmissionBatchSize = 0;
            order.AdmissionBatchIndex = 0;

            _items[_tail] = order;
            _tail = (_tail + 1) % _items.Length;
            _count++;
            outcome = new OrderAdmissionOutcome(
                in order,
                OrderAdmissionStage.GlobalIntake,
                OrderSubmitResult.Queued);
            return true;
        }

        /// <summary>
        /// Atomically admits a caller-owned batch into this queue. Capacity and order type ids are
        /// validated before any queue state or order id is changed, so rejection never leaves a
        /// partially submitted command group.
        /// </summary>
        public bool TryEnqueueBatch(Span<Order> orders)
        {
            if (orders.IsEmpty)
            {
                return true;
            }

            for (int i = 0; i < orders.Length; i++)
            {
                ValidateOrderTypeId(orders[i].OrderTypeId);
                OrderEntityReferenceContract.Validate(in orders[i], nameof(OrderQueue));
            }

            if (orders.Length > AvailableCapacity)
            {
                return false;
            }

            for (int i = 0; i < orders.Length; i++)
            {
                EnsureOrderId(ref orders[i]);
                orders[i].AdmissionSource = OrderAdmissionSource.Local;
                orders[i].AdmissionBatchId = 0;
                orders[i].AdmissionBatchSize = 0;
                orders[i].AdmissionBatchIndex = 0;
                _items[_tail] = orders[i];
                _tail = (_tail + 1) % _items.Length;
            }

            _count += orders.Length;
            return true;
        }

        /// <summary>
        /// Atomically admits a fan-out batch whose rows represent one logical order. The queue owns
        /// the shared id so producers cannot create ids that collide with other intake paths.
        /// </summary>
        public bool TryEnqueueSharedBatch(
            Span<Order> orders,
            OrderAdmissionSource admissionSource = OrderAdmissionSource.Local)
        {
            if (orders.IsEmpty)
            {
                return true;
            }

            ValidateAdmissionSource(admissionSource);
            for (int i = 0; i < orders.Length; i++)
            {
                ValidateOrderTypeId(orders[i].OrderTypeId);
                OrderEntityReferenceContract.Validate(in orders[i], nameof(OrderQueue));
                if (orders[i].OrderId != 0)
                {
                    throw new System.InvalidOperationException(
                        "OrderQueue shared batch requires caller order ids to be zero.");
                }

                ValidateUniqueActor(orders, i);
            }

            if (orders.Length > AvailableCapacity)
            {
                return false;
            }

            if (orders.Length > ushort.MaxValue)
            {
                throw new System.InvalidOperationException(
                    $"OrderQueue shared batch size {orders.Length} exceeds {ushort.MaxValue}.");
            }

            int sharedOrderId = TakeNextOrderId();
            int admissionBatchId = TakeNextAdmissionBatchId();
            for (int i = 0; i < orders.Length; i++)
            {
                orders[i].OrderId = sharedOrderId;
                orders[i].AdmissionSource = admissionSource;
                orders[i].AdmissionBatchId = admissionBatchId;
                orders[i].AdmissionBatchSize = (ushort)orders.Length;
                orders[i].AdmissionBatchIndex = (ushort)i;
                _items[_tail] = orders[i];
                _tail = (_tail + 1) % _items.Length;
            }

            _count += orders.Length;
            return true;
        }

        /// <summary>
        /// Atomically admits contiguous command clusters. Rows with the same non-null CommandSource
        /// receive one shared id; a CommandSource change starts the next cluster.
        /// </summary>
        public bool TryEnqueueClusteredBatch(Span<Order> orders)
        {
            if (orders.IsEmpty)
            {
                return true;
            }

            Entity previousCluster = Entity.Null;
            for (int i = 0; i < orders.Length; i++)
            {
                ValidateOrderTypeId(orders[i].OrderTypeId);
                OrderEntityReferenceContract.Validate(in orders[i], nameof(OrderQueue));
                if (orders[i].OrderId != 0 || orders[i].CommandSource == Entity.Null)
                {
                    throw new System.InvalidOperationException(
                        "OrderQueue clustered batch requires zero order ids and a non-null CommandSource on every row.");
                }

                ValidateUniqueActor(orders, i);
                if (orders[i].CommandSource != previousCluster)
                {
                    for (int prior = 0; prior < i; prior++)
                    {
                        if (orders[prior].CommandSource == orders[i].CommandSource)
                        {
                            throw new System.InvalidOperationException(
                                "OrderQueue clustered batch requires rows for each CommandSource to be contiguous.");
                        }
                    }

                    previousCluster = orders[i].CommandSource;
                }
            }

            if (orders.Length > AvailableCapacity)
            {
                return false;
            }

            if (orders.Length > ushort.MaxValue)
            {
                throw new System.InvalidOperationException(
                    $"OrderQueue clustered batch size {orders.Length} exceeds {ushort.MaxValue}.");
            }

            previousCluster = Entity.Null;
            int clusterOrderId = 0;
            int admissionBatchId = TakeNextAdmissionBatchId();
            for (int i = 0; i < orders.Length; i++)
            {
                if (orders[i].CommandSource != previousCluster)
                {
                    previousCluster = orders[i].CommandSource;
                    clusterOrderId = TakeNextOrderId();
                }

                orders[i].OrderId = clusterOrderId;
                orders[i].AdmissionSource = OrderAdmissionSource.Local;
                orders[i].AdmissionBatchId = admissionBatchId;
                orders[i].AdmissionBatchSize = (ushort)orders.Length;
                orders[i].AdmissionBatchIndex = (ushort)i;
                _items[_tail] = orders[i];
                _tail = (_tail + 1) % _items.Length;
            }

            _count += orders.Length;
            return true;
        }

        public bool TryDequeueBatch(Span<Order> destination, out int count)
        {
            if (!TryPeekBatch(destination, out int batchSize))
            {
                count = 0;
                return false;
            }

            _head = (_head + batchSize) % _items.Length;
            _count -= batchSize;
            count = batchSize;
            return true;
        }

        public bool TryPeekBatch(Span<Order> destination, out int count)
        {
            count = 0;
            if (!TryPeekBatchSize(out int batchSize))
            {
                return false;
            }

            if (batchSize > destination.Length)
            {
                throw new System.InvalidOperationException(
                    $"OrderQueue admission batch size {batchSize} is invalid for count {_count} and destination capacity {destination.Length}.");
            }

            ref readonly Order first = ref _items[_head];
            int batchId = first.AdmissionBatchId;
            for (int i = 0; i < batchSize; i++)
            {
                int sourceIndex = (_head + i) % _items.Length;
                Order item = _items[sourceIndex];
                if (batchId > 0 &&
                    (item.AdmissionBatchId != batchId ||
                     item.AdmissionSource != first.AdmissionSource ||
                     item.AdmissionBatchSize != batchSize ||
                     item.AdmissionBatchIndex != i))
                {
                    throw new System.InvalidOperationException(
                        $"OrderQueue admission batch {batchId} is not contiguous at row {i}.");
                }

                destination[i] = item;
            }

            count = batchSize;
            return true;
        }

        public bool TryPeekBatchSize(out int batchSize)
        {
            if (_count == 0)
            {
                batchSize = 0;
                return false;
            }

            ref readonly Order first = ref _items[_head];
            batchSize = first.AdmissionBatchId > 0 ? first.AdmissionBatchSize : 1;
            if (batchSize <= 0 || batchSize > _count)
            {
                throw new System.InvalidOperationException(
                    $"OrderQueue admission batch size {batchSize} is invalid for count {_count}.");
            }

            return true;
        }

        public void EnsureOrderId(ref Order order)
        {
            if (order.OrderId == 0)
            {
                order.OrderId = TakeNextOrderId();
            }
        }

        private int TakeNextOrderId()
        {
            if (_nextOrderId <= 0 || _nextOrderId == int.MaxValue)
            {
                throw new System.InvalidOperationException("OrderQueue order id space is exhausted.");
            }

            return _nextOrderId++;
        }

        private int TakeNextAdmissionBatchId()
        {
            if (_nextAdmissionBatchId <= 0 || _nextAdmissionBatchId == int.MaxValue)
            {
                throw new System.InvalidOperationException("OrderQueue admission batch id space is exhausted.");
            }

            return _nextAdmissionBatchId++;
        }

        private static void ValidateOrderTypeId(int orderTypeId)
        {
            if (orderTypeId <= 0 || orderTypeId >= OrderTypeRegistry.MaxOrderTypes)
            {
                throw new System.InvalidOperationException(
                    $"OrderQueue requires a positive order type id below {OrderTypeRegistry.MaxOrderTypes}; got {orderTypeId}.");
            }
        }

        private static void ValidateAdmissionSource(OrderAdmissionSource admissionSource)
        {
            if (admissionSource is not OrderAdmissionSource.Local and not OrderAdmissionSource.Network)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(admissionSource),
                    admissionSource,
                    "Unknown order admission source.");
            }
        }

        private static void ValidateUniqueActor(ReadOnlySpan<Order> orders, int index)
        {
            if (orders[index].Actor == Entity.Null)
            {
                throw new System.InvalidOperationException("OrderQueue atomic batches require a non-null actor on every row.");
            }

            for (int prior = 0; prior < index; prior++)
            {
                if (orders[prior].Actor == orders[index].Actor)
                {
                    throw new System.InvalidOperationException(
                        $"OrderQueue atomic batch contains duplicate actor {orders[index].Actor.Id} at rows {prior} and {index}.");
                }
            }
        }

        public bool TryDequeue(out Order order)
        {
            if (_count == 0)
            {
                order = default;
                return false;
            }

            order = _items[_head];
            _head = (_head + 1) % _items.Length;
            _count--;
            return true;
        }

        public void Clear()
        {
            _head = 0;
            _tail = 0;
            _count = 0;
        }

    }
}
