using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public readonly struct OrderTerminalOutcome
    {
        public readonly int OrderId;
        public readonly int OrderTypeId;
        public readonly OrderTerminalState State;
        public readonly OrderFailureReason FailureReason;
        public readonly Entity Actor;

        public OrderTerminalOutcome(
            int orderId,
            int orderTypeId,
            OrderTerminalState state,
            OrderFailureReason failureReason,
            Entity actor)
        {
            OrderId = orderId;
            OrderTypeId = orderTypeId;
            State = state;
            FailureReason = failureReason;
            Actor = actor;
        }
    }

    /// <summary>
    /// Fixed-capacity snapshot of order terminal outcomes published during the current frame.
    /// Iteration only exposes current-frame outcomes; id lookup also includes the immediately
    /// retired frame so systems that submit before GAS phases can observe the next-frame verdict
    /// without re-playing terminal events for continuation consumers.
    /// </summary>
    public sealed class OrderTerminalResultBuffer
    {
        public const int DefaultCapacity = 4096;

        private OrderTerminalOutcome[] _items;
        private OrderTerminalOutcome[] _previousItems;
        private int _count;
        private int _previousCount;

        public OrderTerminalResultBuffer(int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "capacity must be positive.");
            }

            _items = new OrderTerminalOutcome[capacity];
            _previousItems = new OrderTerminalOutcome[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public uint Generation { get; private set; }

        public ref readonly OrderTerminalOutcome this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return ref _items[index];
            }
        }

        internal void EnsureCanWrite(int additionalCount = 1)
        {
            if (additionalCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(additionalCount));
            }

            if (additionalCount > _items.Length - _count)
            {
                throw new InvalidOperationException(
                    $"ORDER.TERMINAL.ERR.ResultCapacityExceeded: capacity={_items.Length}, count={_count}, requested={additionalCount}.");
            }
        }

        internal void Write(in OrderTerminalOutcome outcome)
        {
            EnsureCanWrite();
            _items[_count++] = outcome;
        }

        public bool TryGet(int orderId, out OrderTerminalOutcome outcome)
        {
            for (int i = _count - 1; i >= 0; i--)
            {
                if (_items[i].OrderId == orderId)
                {
                    outcome = _items[i];
                    return true;
                }
            }

            for (int i = _previousCount - 1; i >= 0; i--)
            {
                if (_previousItems[i].OrderId == orderId)
                {
                    outcome = _previousItems[i];
                    return true;
                }
            }

            outcome = default;
            return false;
        }

        public void Clear()
        {
            OrderTerminalOutcome[] retired = _previousItems;
            _previousItems = _items;
            _previousCount = _count;
            _items = retired;
            _count = 0;
            Generation++;
        }
    }
}
