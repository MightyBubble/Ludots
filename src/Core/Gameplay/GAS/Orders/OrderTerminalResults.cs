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
    /// Fixed-capacity current-frame snapshot plus stable per-order terminal ledger.
    /// Current-frame iteration remains frame-local. Cross-frame OrderId lookups require
    /// Retain(orderId) before the terminal result is written and remain until consume/release.
    /// </summary>
    public sealed class OrderTerminalResultBuffer
    {
        public const int DefaultCapacity = 4096;

        private OrderTerminalOutcome[] _items;
        private readonly OrderTerminalOutcome[] _ledgerItems;
        private readonly int[] _ledgerOrderIds;
        private readonly byte[] _ledgerStates;
        private readonly byte[] _ledgerRetains;
        private readonly int[] _pendingRetainOrderIds;
        private readonly byte[] _pendingRetainStates;
        private int _count;
        private int _ledgerCount;
        private int _pendingRetainCount;

        private const byte LedgerEmpty = 0;
        private const byte LedgerAvailable = 1;
        private const byte LedgerConsumed = 2;
        private const byte LedgerReleased = 3;

        private const byte PendingRetainEmpty = 0;
        private const byte PendingRetainActive = 1;
        private const byte PendingRetainReleased = 2;

        public OrderTerminalResultBuffer(int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "capacity must be positive.");
            }

            _items = new OrderTerminalOutcome[capacity];
            _ledgerItems = new OrderTerminalOutcome[capacity];
            _ledgerOrderIds = new int[capacity];
            _ledgerStates = new byte[capacity];
            _ledgerRetains = new byte[capacity];
            _pendingRetainOrderIds = new int[capacity];
            _pendingRetainStates = new byte[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public int LedgerCount => _ledgerCount;
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

            if (additionalCount > _items.Length - _count ||
                additionalCount > _ledgerItems.Length - _ledgerCount)
            {
                throw new InvalidOperationException(
                    $"ORDER.TERMINAL.ERR.ResultCapacityExceeded: capacity={_items.Length}, count={_count}, ledgerCount={_ledgerCount}, requested={additionalCount}.");
            }
        }

        internal void Write(in OrderTerminalOutcome outcome)
        {
            if (outcome.OrderId <= 0)
            {
                throw new InvalidOperationException(
                    $"ORDER.TERMINAL.ERR.InvalidOrderId: orderTypeId={outcome.OrderTypeId}, orderId={outcome.OrderId}.");
            }

            EnsureCanWrite();
            int slot = FindSlot(outcome.OrderId);
            byte state = _ledgerStates[slot];
            if (state != LedgerEmpty && state != LedgerReleased)
            {
                throw new InvalidOperationException(
                    $"ORDER.TERMINAL.ERR.DuplicateOrderId: orderId={outcome.OrderId}.");
            }

            _items[_count++] = outcome;
            _ledgerItems[slot] = outcome;
            _ledgerOrderIds[slot] = outcome.OrderId;
            _ledgerStates[slot] = LedgerAvailable;
            _ledgerRetains[slot] = ConsumePendingRetain(outcome.OrderId) ? (byte)1 : (byte)0;
            _ledgerCount++;
        }

        public void Retain(int orderId)
        {
            if (orderId <= 0)
            {
                throw new InvalidOperationException(
                    $"ORDER.TERMINAL.ERR.InvalidOrderId: orderId={orderId}.");
            }

            if (TryFindExistingSlot(orderId, out int slot))
            {
                byte state = _ledgerStates[slot];
                if (state == LedgerAvailable)
                {
                    if (_ledgerRetains[slot] != 0)
                    {
                        throw new InvalidOperationException(
                            $"ORDER.TERMINAL.ERR.DuplicateOrderId: orderId={orderId}.");
                    }

                    _ledgerRetains[slot] = 1;
                    return;
                }

                if (state != LedgerEmpty && state != LedgerReleased)
                {
                    throw new InvalidOperationException(
                        $"ORDER.TERMINAL.ERR.DuplicateOrderId: orderId={orderId}.");
                }
            }

            RetainPending(orderId);
        }

        public bool TryGet(int orderId, out OrderTerminalOutcome outcome)
        {
            if (TryFindExistingSlot(orderId, out int slot) &&
                _ledgerStates[slot] == LedgerAvailable)
            {
                outcome = _ledgerItems[slot];
                return true;
            }

            outcome = default;
            return false;
        }

        public bool TryConsume(int orderId, out OrderTerminalOutcome outcome)
        {
            if (!TryFindExistingSlot(orderId, out int slot))
            {
                outcome = default;
                return false;
            }

            byte state = _ledgerStates[slot];
            if (state == LedgerConsumed)
            {
                outcome = default;
                return false;
            }

            if (state != LedgerAvailable)
            {
                outcome = default;
                return false;
            }

            outcome = _ledgerItems[slot];
            _ledgerStates[slot] = LedgerConsumed;
            return true;
        }

        public OrderTerminalOutcome Consume(int orderId)
        {
            if (!TryFindExistingSlot(orderId, out int slot))
            {
                throw new InvalidOperationException(
                    $"ORDER.TERMINAL.ERR.UnknownOrderId: orderId={orderId}.");
            }

            byte state = _ledgerStates[slot];
            if (state == LedgerConsumed)
            {
                throw new InvalidOperationException(
                    $"ORDER.TERMINAL.ERR.AlreadyConsumed: orderId={orderId}.");
            }

            if (state != LedgerAvailable)
            {
                throw new InvalidOperationException(
                    $"ORDER.TERMINAL.ERR.UnknownOrderId: orderId={orderId}.");
            }

            OrderTerminalOutcome outcome = _ledgerItems[slot];
            _ledgerStates[slot] = LedgerConsumed;
            return outcome;
        }

        public bool Release(int orderId)
        {
            if (!TryFindExistingSlot(orderId, out int slot))
            {
                return ReleasePendingRetain(orderId);
            }

            byte state = _ledgerStates[slot];
            if (state != LedgerAvailable && state != LedgerConsumed)
            {
                return ReleasePendingRetain(orderId);
            }

            _ledgerStates[slot] = LedgerReleased;
            _ledgerOrderIds[slot] = 0;
            _ledgerItems[slot] = default;
            _ledgerRetains[slot] = 0;
            _ledgerCount--;
            return true;
        }

        public bool ReleaseConsumed(int orderId) => Release(orderId);

        public void Clear()
        {
            _count = 0;
            ReleaseUnretainedAvailableOutcomes();
            Generation++;
        }

        private int FindSlot(int orderId)
        {
            int start = PositiveMod(Hash(orderId), _ledgerOrderIds.Length);
            int firstReleased = -1;
            for (int offset = 0; offset < _ledgerOrderIds.Length; offset++)
            {
                int slot = start + offset;
                if (slot >= _ledgerOrderIds.Length)
                {
                    slot -= _ledgerOrderIds.Length;
                }

                byte state = _ledgerStates[slot];
                if (state == LedgerEmpty)
                {
                    return firstReleased >= 0 ? firstReleased : slot;
                }

                if (state == LedgerReleased)
                {
                    if (firstReleased < 0)
                    {
                        firstReleased = slot;
                    }

                    continue;
                }

                if (_ledgerOrderIds[slot] == orderId)
                {
                    return slot;
                }
            }

            if (firstReleased >= 0)
            {
                return firstReleased;
            }

            throw new InvalidOperationException(
                $"ORDER.TERMINAL.ERR.ResultCapacityExceeded: capacity={_ledgerItems.Length}, count={_count}, ledgerCount={_ledgerCount}, requested=1.");
        }

        private bool TryFindExistingSlot(int orderId, out int slot)
        {
            if (orderId <= 0)
            {
                slot = -1;
                return false;
            }

            int start = PositiveMod(Hash(orderId), _ledgerOrderIds.Length);
            for (int offset = 0; offset < _ledgerOrderIds.Length; offset++)
            {
                int candidate = start + offset;
                if (candidate >= _ledgerOrderIds.Length)
                {
                    candidate -= _ledgerOrderIds.Length;
                }

                byte state = _ledgerStates[candidate];
                if (state == LedgerEmpty)
                {
                    slot = -1;
                    return false;
                }

                if (state == LedgerReleased)
                {
                    continue;
                }

                if (_ledgerOrderIds[candidate] == orderId)
                {
                    slot = candidate;
                    return true;
                }
            }

            slot = -1;
            return false;
        }

        private static int Hash(int orderId)
        {
            unchecked
            {
                uint x = (uint)orderId;
                x ^= x >> 16;
                x *= 0x7feb352dU;
                x ^= x >> 15;
                x *= 0x846ca68bU;
                x ^= x >> 16;
                return (int)x;
            }
        }

        private static int PositiveMod(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private void RetainPending(int orderId)
        {
            int slot = FindPendingRetainSlot(orderId);
            byte state = _pendingRetainStates[slot];
            if (state == PendingRetainActive)
            {
                throw new InvalidOperationException(
                    $"ORDER.TERMINAL.ERR.DuplicateOrderId: orderId={orderId}.");
            }

            if (_pendingRetainCount >= _pendingRetainOrderIds.Length)
            {
                throw new InvalidOperationException(
                    $"ORDER.TERMINAL.ERR.RetainCapacityExceeded: capacity={_pendingRetainOrderIds.Length}, count={_pendingRetainCount}, orderId={orderId}.");
            }

            _pendingRetainOrderIds[slot] = orderId;
            _pendingRetainStates[slot] = PendingRetainActive;
            _pendingRetainCount++;
        }

        private bool ConsumePendingRetain(int orderId)
        {
            if (!TryFindPendingRetainSlot(orderId, out int slot))
            {
                return false;
            }

            _pendingRetainStates[slot] = PendingRetainReleased;
            _pendingRetainOrderIds[slot] = 0;
            _pendingRetainCount--;
            return true;
        }

        private bool ReleasePendingRetain(int orderId) => ConsumePendingRetain(orderId);

        private int FindPendingRetainSlot(int orderId)
        {
            int start = PositiveMod(Hash(orderId), _pendingRetainOrderIds.Length);
            int firstReleased = -1;
            for (int offset = 0; offset < _pendingRetainOrderIds.Length; offset++)
            {
                int slot = start + offset;
                if (slot >= _pendingRetainOrderIds.Length)
                {
                    slot -= _pendingRetainOrderIds.Length;
                }

                byte state = _pendingRetainStates[slot];
                if (state == PendingRetainEmpty)
                {
                    return firstReleased >= 0 ? firstReleased : slot;
                }

                if (state == PendingRetainReleased)
                {
                    if (firstReleased < 0)
                    {
                        firstReleased = slot;
                    }

                    continue;
                }

                if (_pendingRetainOrderIds[slot] == orderId)
                {
                    return slot;
                }
            }

            if (firstReleased >= 0)
            {
                return firstReleased;
            }

            throw new InvalidOperationException(
                $"ORDER.TERMINAL.ERR.RetainCapacityExceeded: capacity={_pendingRetainOrderIds.Length}, count={_pendingRetainCount}, orderId={orderId}.");
        }

        private bool TryFindPendingRetainSlot(int orderId, out int slot)
        {
            int start = PositiveMod(Hash(orderId), _pendingRetainOrderIds.Length);
            for (int offset = 0; offset < _pendingRetainOrderIds.Length; offset++)
            {
                int candidate = start + offset;
                if (candidate >= _pendingRetainOrderIds.Length)
                {
                    candidate -= _pendingRetainOrderIds.Length;
                }

                byte state = _pendingRetainStates[candidate];
                if (state == PendingRetainEmpty)
                {
                    slot = -1;
                    return false;
                }

                if (state == PendingRetainReleased)
                {
                    continue;
                }

                if (_pendingRetainOrderIds[candidate] == orderId)
                {
                    slot = candidate;
                    return true;
                }
            }

            slot = -1;
            return false;
        }

        private void ReleaseUnretainedAvailableOutcomes()
        {
            for (int slot = 0; slot < _ledgerStates.Length; slot++)
            {
                if (_ledgerStates[slot] != LedgerAvailable ||
                    _ledgerRetains[slot] != 0)
                {
                    continue;
                }

                _ledgerStates[slot] = LedgerReleased;
                _ledgerOrderIds[slot] = 0;
                _ledgerItems[slot] = default;
                _ledgerCount--;
            }
        }
    }
}
