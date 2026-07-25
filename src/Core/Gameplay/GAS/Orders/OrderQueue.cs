using System;
using Arch.Core;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public enum OrderSubmitMode : byte
    {
        Immediate = 0,
        Queued = 1
    }

    public struct Order
    {
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
        public int AdmissionBatchId;
        public ushort AdmissionBatchSize;
        public ushort AdmissionBatchIndex;
    }

    public sealed class OrderQueue
    {
        private readonly Order[] _items;
        private readonly OrderAdmissionReservation[] _admissionReservationsScratch;
        private readonly OrderAdmissionResultBuffer _admissionResults;
        private int _head;
        private int _tail;
        private int _count;
        private int _nextAdmissionBatchId = 1;

        public OrderQueue(int capacity, OrderAdmissionResultBuffer admissionResults)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "capacity must be positive.");
            }

            _admissionResults = admissionResults
                ?? throw new ArgumentNullException(nameof(admissionResults));
            _items = new Order[capacity];
            _admissionReservationsScratch = new OrderAdmissionReservation[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public int AvailableCapacity => _items.Length - _count;
        public OrderAdmissionResultBuffer AdmissionResults => _admissionResults;

        public bool TryEnqueue(in Order order)
        {
            return OrderSubmitResultSemantics.IsAccepted(Submit(in order));
        }

        public bool TryEnqueueAssigned(ref Order order)
        {
            return OrderSubmitResultSemantics.IsAccepted(SubmitAssigned(ref order));
        }

        public OrderSubmitResult Submit(in Order order)
        {
            var value = order;
            return SubmitAssigned(ref value);
        }

        public OrderSubmitResult SubmitAssigned(ref Order order)
        {
            EnsureOrderId(ref order);
            order.AdmissionBatchId = 0;
            order.AdmissionBatchSize = 0;
            order.AdmissionBatchIndex = 0;

            if (!_admissionResults.CanReserve(OrderAdmissionStage.GlobalIntake, 1))
            {
                Span<Order> rejected = stackalloc Order[1];
                rejected[0] = order;
                _admissionResults.RecordCapacityFailures(rejected, OrderAdmissionStage.GlobalIntake);
                return OrderSubmitResult.RejectedAdmissionCapacity;
            }

            OrderAdmissionReservation reservation = _admissionResults.Reserve(
                OrderAdmissionStage.GlobalIntake,
                order.OrderId,
                order.OrderTypeId);
            bool committed = false;
            try
            {
                OrderSubmitResult result;
                if (!IsValidOrderTypeId(order.OrderTypeId))
                {
                    result = OrderSubmitResult.RejectedInvalidOrderType;
                }
                else if (_count >= _items.Length)
                {
                    result = OrderSubmitResult.RejectedQueueFull;
                }
                else
                {
                    _items[_tail] = order;
                    _tail = (_tail + 1) % _items.Length;
                    _count++;
                    result = OrderSubmitResult.Queued;
                }

                CommitAdmission(in reservation, in order, result);
                committed = true;
                return result;
            }
            finally
            {
                if (!committed && reservation.IsValid)
                {
                    _admissionResults.Cancel(in reservation);
                }
            }
        }

        public OrderSubmitResult TryEnqueueBatch(Span<Order> orders)
        {
            if (orders.IsEmpty)
            {
                return OrderSubmitResult.Queued;
            }

            for (int i = 0; i < orders.Length; i++)
            {
                ValidateOrderTypeId(orders[i].OrderTypeId);
            }

            _admissionResults.EnsureWritableForNewOrders(OrderAdmissionStage.GlobalIntake, orders.Length);
            for (int i = 0; i < orders.Length; i++)
            {
                EnsureOrderId(ref orders[i]);
                orders[i].AdmissionBatchId = 0;
                orders[i].AdmissionBatchSize = 0;
                orders[i].AdmissionBatchIndex = 0;
            }

            if (!_admissionResults.CanReserve(OrderAdmissionStage.GlobalIntake, orders.Length))
            {
                return RejectBatchForAdmissionCapacityWithoutQueueMutation(orders);
            }
            if (orders.Length > AvailableCapacity)
            {
                return RejectBatchWithoutQueueMutation(orders, OrderSubmitResult.RejectedQueueFull);
            }

            ReserveBatch(orders);
            for (int i = 0; i < orders.Length; i++)
            {
                _items[_tail] = orders[i];
                _tail = (_tail + 1) % _items.Length;
            }

            _count += orders.Length;
            CommitReservedBatch(orders, OrderSubmitResult.Queued);
            return OrderSubmitResult.Queued;
        }

        public OrderSubmitResult TryEnqueueSharedBatch(Span<Order> orders)
        {
            if (orders.IsEmpty)
            {
                return OrderSubmitResult.Queued;
            }

            for (int i = 0; i < orders.Length; i++)
            {
                ValidateOrderTypeId(orders[i].OrderTypeId);
                if (orders[i].OrderId != 0)
                {
                    throw new InvalidOperationException(
                        "OrderQueue shared batch requires caller order ids to be zero.");
                }

                ValidateUniqueActor(orders, i);
            }

            if (orders.Length > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"OrderQueue shared batch size {orders.Length} exceeds {ushort.MaxValue}.");
            }

            Order idSource = orders[0];
            EnsureOrderId(ref idSource);
            int sharedOrderId = idSource.OrderId;
            int admissionBatchId = NextAdmissionBatchId();
            ushort batchSize = (ushort)orders.Length;
            _admissionResults.EnsureWritableForNewOrders(OrderAdmissionStage.GlobalIntake, orders.Length);
            for (int i = 0; i < orders.Length; i++)
            {
                orders[i].OrderId = sharedOrderId;
                orders[i].AdmissionBatchId = admissionBatchId;
                orders[i].AdmissionBatchSize = batchSize;
                orders[i].AdmissionBatchIndex = (ushort)i;
            }

            if (!_admissionResults.CanReserve(OrderAdmissionStage.GlobalIntake, orders.Length))
            {
                return RejectBatchForAdmissionCapacityWithoutQueueMutation(orders);
            }
            if (orders.Length > AvailableCapacity)
            {
                return RejectBatchWithoutQueueMutation(orders, OrderSubmitResult.RejectedQueueFull);
            }

            ReserveBatch(orders);
            for (int i = 0; i < orders.Length; i++)
            {
                _items[_tail] = orders[i];
                _tail = (_tail + 1) % _items.Length;
            }

            _count += orders.Length;
            CommitReservedBatch(orders, OrderSubmitResult.Queued);
            return OrderSubmitResult.Queued;
        }

        public OrderSubmitResult TryEnqueueClusteredBatch(Span<Order> orders)
        {
            if (orders.IsEmpty)
            {
                return OrderSubmitResult.Queued;
            }

            Entity previousCluster = Entity.Null;
            for (int i = 0; i < orders.Length; i++)
            {
                ValidateOrderTypeId(orders[i].OrderTypeId);
                if (orders[i].OrderId != 0 || orders[i].CommandSource == Entity.Null)
                {
                    throw new InvalidOperationException(
                        "OrderQueue clustered batch requires zero order ids and a non-null CommandSource on every row.");
                }

                ValidateUniqueActor(orders, i);
                if (orders[i].CommandSource != previousCluster)
                {
                    for (int prior = 0; prior < i; prior++)
                    {
                        if (orders[prior].CommandSource == orders[i].CommandSource)
                        {
                            throw new InvalidOperationException(
                                "OrderQueue clustered batch requires rows for each CommandSource to be contiguous.");
                        }
                    }

                    previousCluster = orders[i].CommandSource;
                }
            }

            if (orders.Length > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"OrderQueue clustered batch size {orders.Length} exceeds {ushort.MaxValue}.");
            }

            previousCluster = Entity.Null;
            int clusterOrderId = 0;
            int admissionBatchId = NextAdmissionBatchId();
            ushort batchSize = (ushort)orders.Length;
            _admissionResults.EnsureWritableForNewOrders(OrderAdmissionStage.GlobalIntake, orders.Length);
            for (int i = 0; i < orders.Length; i++)
            {
                if (orders[i].CommandSource != previousCluster)
                {
                    previousCluster = orders[i].CommandSource;
                    EnsureOrderId(ref orders[i]);
                    clusterOrderId = orders[i].OrderId;
                }
                else
                {
                    orders[i].OrderId = clusterOrderId;
                }

                orders[i].AdmissionBatchId = admissionBatchId;
                orders[i].AdmissionBatchSize = batchSize;
                orders[i].AdmissionBatchIndex = (ushort)i;
            }

            if (!_admissionResults.CanReserve(OrderAdmissionStage.GlobalIntake, orders.Length))
            {
                return RejectBatchForAdmissionCapacityWithoutQueueMutation(orders);
            }
            if (orders.Length > AvailableCapacity)
            {
                return RejectBatchWithoutQueueMutation(orders, OrderSubmitResult.RejectedQueueFull);
            }

            ReserveBatch(orders);
            for (int i = 0; i < orders.Length; i++)
            {
                _items[_tail] = orders[i];
                _tail = (_tail + 1) % _items.Length;
            }

            _count += orders.Length;
            CommitReservedBatch(orders, OrderSubmitResult.Queued);
            return OrderSubmitResult.Queued;
        }

        public bool TryDequeueBatch(Span<Order> destination, out int count)
        {
            count = 0;
            if (_count == 0)
            {
                return false;
            }

            ref readonly Order first = ref _items[_head];
            int batchSize = first.AdmissionBatchId > 0 ? first.AdmissionBatchSize : 1;
            if (batchSize <= 0 || batchSize > _count || batchSize > destination.Length)
            {
                throw new InvalidOperationException(
                    $"OrderQueue admission batch size {batchSize} is invalid for count {_count} and destination capacity {destination.Length}.");
            }

            int batchId = first.AdmissionBatchId;
            for (int i = 0; i < batchSize; i++)
            {
                int sourceIndex = (_head + i) % _items.Length;
                Order item = _items[sourceIndex];
                if (batchId > 0 &&
                    (item.AdmissionBatchId != batchId ||
                     item.AdmissionBatchSize != batchSize ||
                     item.AdmissionBatchIndex != i))
                {
                    throw new InvalidOperationException(
                        $"OrderQueue admission batch {batchId} is not contiguous at row {i}.");
                }

                destination[i] = item;
            }

            _head = (_head + batchSize) % _items.Length;
            _count -= batchSize;
            count = batchSize;
            return true;
        }

        public bool TryPeekBatch(Span<Order> destination, out int count)
        {
            count = 0;
            if (_count == 0)
            {
                return false;
            }

            ref readonly Order first = ref _items[_head];
            int batchSize = first.AdmissionBatchId > 0 ? first.AdmissionBatchSize : 1;
            if (batchSize <= 0 || batchSize > _count || batchSize > destination.Length)
            {
                throw new InvalidOperationException(
                    $"OrderQueue admission batch size {batchSize} is invalid for count {_count} and destination capacity {destination.Length}.");
            }

            int batchId = first.AdmissionBatchId;
            for (int i = 0; i < batchSize; i++)
            {
                int sourceIndex = (_head + i) % _items.Length;
                Order item = _items[sourceIndex];
                if (batchId > 0 &&
                    (item.AdmissionBatchId != batchId ||
                     item.AdmissionBatchSize != batchSize ||
                     item.AdmissionBatchIndex != i))
                {
                    throw new InvalidOperationException(
                        $"OrderQueue admission batch {batchId} is not contiguous at row {i}.");
                }

                destination[i] = item;
            }

            count = batchSize;
            return true;
        }

        public void EnsureOrderId(ref Order order)
        {
            _admissionResults.EnsureOrderId(ref order);
        }

        private static bool IsValidOrderTypeId(int orderTypeId)
        {
            return orderTypeId > 0 && orderTypeId < OrderTypeRegistry.MaxOrderTypes;
        }

        private static void ValidateOrderTypeId(int orderTypeId)
        {
            if (!IsValidOrderTypeId(orderTypeId))
            {
                throw new InvalidOperationException(
                    $"OrderQueue requires a positive order type id below {OrderTypeRegistry.MaxOrderTypes}; got {orderTypeId}.");
            }
        }

        private void ReserveBatch(ReadOnlySpan<Order> orders)
        {
            for (int i = 0; i < orders.Length; i++)
            {
                try
                {
                    _admissionReservationsScratch[i] = _admissionResults.Reserve(
                        OrderAdmissionStage.GlobalIntake,
                        orders[i].OrderId,
                        orders[i].OrderTypeId);
                }
                catch
                {
                    CancelReservedBatch(i);
                    throw;
                }
            }
        }

        private void CommitReservedBatch(ReadOnlySpan<Order> orders, OrderSubmitResult result)
        {
            for (int i = 0; i < orders.Length; i++)
            {
                CommitAdmission(in _admissionReservationsScratch[i], in orders[i], result);
                _admissionReservationsScratch[i] = default;
            }
        }

        private void CancelReservedBatch(int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                if (_admissionReservationsScratch[i].IsValid)
                {
                    _admissionResults.Cancel(in _admissionReservationsScratch[i]);
                    _admissionReservationsScratch[i] = default;
                }
            }
        }

        private OrderSubmitResult RejectBatchForAdmissionCapacityWithoutQueueMutation(ReadOnlySpan<Order> orders)
        {
            _admissionResults.RecordCapacityFailures(orders, OrderAdmissionStage.GlobalIntake);
            return OrderSubmitResult.RejectedAdmissionCapacity;
        }

        private OrderSubmitResult RejectBatchWithoutQueueMutation(ReadOnlySpan<Order> orders, OrderSubmitResult result)
        {
            if (!_admissionResults.CanReserve(OrderAdmissionStage.GlobalIntake, orders.Length))
            {
                return RejectBatchForAdmissionCapacityWithoutQueueMutation(orders);
            }

            OrderAdmissionReservation reservation = default;
            int reservedCount = 0;
            int committedCount = 0;
            try
            {
                for (int i = 0; i < orders.Length; i++)
                {
                    OrderAdmissionReservation next = _admissionResults.Reserve(
                        OrderAdmissionStage.GlobalIntake,
                        orders[i].OrderId,
                        orders[i].OrderTypeId);
                    if (reservedCount == 0)
                    {
                        reservation = next;
                    }
                    else if (next.IsPendingGeneration != reservation.IsPendingGeneration)
                    {
                        throw new InvalidOperationException("ORDER.ADMISSION.ERR.BatchReservationGenerationChanged");
                    }

                    reservedCount++;
                }

                for (int i = 0; i < orders.Length; i++)
                {
                    CommitAdmission(in reservation, in orders[i], result);
                    committedCount++;
                }

                return result;
            }
            finally
            {
                int remainingReservations = reservedCount - committedCount;
                for (int i = 0; i < remainingReservations; i++)
                {
                    _admissionResults.Cancel(in reservation);
                }
            }
        }

        private int NextAdmissionBatchId()
        {
            if (_nextAdmissionBatchId <= 0)
            {
                throw new InvalidOperationException("ORDER.ADMISSION.ERR.BatchIdCapacityExceeded");
            }

            int value = _nextAdmissionBatchId;
            _nextAdmissionBatchId = value == int.MaxValue ? 0 : value + 1;
            return value;
        }

        private void CommitAdmission(
            in OrderAdmissionReservation reservation,
            in Order order,
            OrderSubmitResult result)
        {
            var outcome = new OrderAdmissionOutcome(order.OrderId, order.OrderTypeId, OrderAdmissionStage.GlobalIntake, result);
            _admissionResults.Commit(in reservation, in outcome);
        }

        public bool TryPeek(out Order order)
        {
            if (_count == 0)
            {
                order = default;
                return false;
            }

            order = _items[_head];
            return true;
        }

        private static void ValidateUniqueActor(ReadOnlySpan<Order> orders, int index)
        {
            if (orders[index].Actor == Entity.Null)
            {
                throw new InvalidOperationException("OrderQueue atomic batches require a non-null actor on every row.");
            }

            for (int prior = 0; prior < index; prior++)
            {
                if (orders[prior].Actor == orders[index].Actor)
                {
                    throw new InvalidOperationException(
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
            for (int i = 0, index = _head; i < _count; i++, index = (index + 1) % _items.Length)
            {
                if (_items[index].Args.Spatial.Payload.IsValid)
                {
                    throw new InvalidOperationException(
                        "ORDER.QUEUE.ERR.PayloadClearRequiresOwner");
                }
            }

            _head = 0;
            _tail = 0;
            _count = 0;
        }
    }
}
