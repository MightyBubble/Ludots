using System;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Networking.Commands
{
    public readonly struct NetworkCommandSeat
    {
        public NetworkCommandSeat(int slot, uint generation, int playerId)
        {
            Slot = slot;
            Generation = generation;
            PlayerId = playerId;
        }

        public int Slot { get; }
        public uint Generation { get; }
        public int PlayerId { get; }
    }

    public readonly struct NetworkCommandAdmissionOutcome
    {
        public NetworkCommandAdmissionOutcome(
            in NetworkCommandSeat seat,
            ulong clientBatchSequence,
            int targetTick,
            int actorCount,
            int orderId,
            int admissionBatchId,
            OrderSubmitResult result,
            bool isReplay)
        {
            SeatSlot = seat.Slot;
            SeatGeneration = seat.Generation;
            PlayerId = seat.PlayerId;
            ClientBatchSequence = clientBatchSequence;
            TargetTick = targetTick;
            ActorCount = actorCount;
            OrderId = orderId;
            AdmissionBatchId = admissionBatchId;
            Stage = result == OrderSubmitResult.Queued
                ? OrderAdmissionStage.GlobalIntake
                : OrderAdmissionStage.NetworkIntake;
            Result = result;
            IsReplay = isReplay;
        }

        public int SeatSlot { get; }
        public uint SeatGeneration { get; }
        public int PlayerId { get; }
        public ulong ClientBatchSequence { get; }
        public int TargetTick { get; }
        public int ActorCount { get; }
        public int OrderId { get; }
        public int AdmissionBatchId { get; }
        public OrderAdmissionStage Stage { get; }
        public OrderSubmitResult Result { get; }
        public bool IsReplay { get; }

        public NetworkCommandAdmissionOutcome AsReplay()
        {
            var seat = new NetworkCommandSeat(SeatSlot, SeatGeneration, PlayerId);
            return new NetworkCommandAdmissionOutcome(
                in seat,
                ClientBatchSequence,
                TargetTick,
                ActorCount,
                OrderId,
                AdmissionBatchId,
                Result,
                isReplay: true);
        }
    }

    public sealed class NetworkCommandAdmissionResultBuffer
    {
        private readonly NetworkCommandAdmissionOutcome[] _items;
        private int _head;
        private int _count;

        public NetworkCommandAdmissionResultBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Result capacity must be positive.");
            }

            _items = new NetworkCommandAdmissionOutcome[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public int AvailableCapacity => _items.Length - _count;

        public bool TryWrite(in NetworkCommandAdmissionOutcome outcome)
        {
            if (_count == _items.Length)
            {
                return false;
            }

            int tail = (_head + _count) % _items.Length;
            _items[tail] = outcome;
            _count++;
            return true;
        }

        public bool TryRead(out NetworkCommandAdmissionOutcome outcome)
        {
            if (_count == 0)
            {
                outcome = default;
                return false;
            }

            outcome = _items[_head];
            _head = (_head + 1) % _items.Length;
            _count--;
            return true;
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }
    }
}
