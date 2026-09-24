using System;
using Ludots.Core.Gameplay.GAS.Components;
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
            NetworkCommandAdmissionCode code,
            bool isReplay,
            int committedTick)
            : this(
                in seat,
                clientBatchSequence,
                targetTick,
                actorCount,
                orderId,
                admissionBatchId,
                admissionBatchIndex: 0,
                NetworkCommandAdmissionCodeSemantics.DeriveStage(code),
                code,
                isReplay,
                committedTick)
        {
        }

        public NetworkCommandAdmissionOutcome(
            in NetworkCommandSeat seat,
            ulong clientBatchSequence,
            int targetTick,
            int actorCount,
            int orderId,
            int admissionBatchId,
            ushort admissionBatchIndex,
            NetworkCommandAdmissionStage stage,
            NetworkCommandAdmissionCode code,
            bool isReplay,
            int committedTick)
        {
            if (!NetworkCommandAdmissionCodeSemantics.IsKnown(stage))
            {
                throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown network command admission stage.");
            }

            if (!NetworkCommandAdmissionCodeSemantics.IsKnown(code))
            {
                throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown network command admission code.");
            }

            if (!NetworkCommandAdmissionCodeSemantics.IsValidStageCode(stage, code))
            {
                throw new ArgumentException(
                    $"Network command admission stage {stage} cannot carry code {code}.",
                    nameof(code));
            }

            if (!IsValidCommittedTick(stage, committedTick))
            {
                throw new ArgumentOutOfRangeException(nameof(committedTick));
            }

            SeatSlot = seat.Slot;
            SeatGeneration = seat.Generation;
            PlayerId = seat.PlayerId;
            ClientBatchSequence = clientBatchSequence;
            TargetTick = targetTick;
            ActorCount = actorCount;
            OrderId = orderId;
            AdmissionBatchId = admissionBatchId;
            AdmissionBatchIndex = admissionBatchIndex;
            Stage = stage;
            Result = code;
            IsReplay = isReplay;
            CommittedTick = committedTick;
        }

        public int SeatSlot { get; }
        public uint SeatGeneration { get; }
        public int PlayerId { get; }
        public ulong ClientBatchSequence { get; }
        public int TargetTick { get; }
        public int ActorCount { get; }
        public int OrderId { get; }
        public int AdmissionBatchId { get; }
        public ushort AdmissionBatchIndex { get; }
        public NetworkCommandAdmissionStage Stage { get; }
        public NetworkCommandAdmissionCode Result { get; }
        public bool IsReplay { get; }
        public int CommittedTick { get; }

        internal static bool IsValidCommittedTick(NetworkCommandAdmissionStage stage, int committedTick) =>
            committedTick >= 0 &&
            (stage != NetworkCommandAdmissionStage.EntityIntake || committedTick > 0) &&
            (stage != NetworkCommandAdmissionStage.Terminal || committedTick > 0);

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
                AdmissionBatchIndex,
                Stage,
                Result,
                isReplay: true,
                committedTick: CommittedTick);
        }

        public static NetworkCommandAdmissionOutcome FromCoreAdmission(
            in NetworkCommandSeat seat,
            ulong clientBatchSequence,
            int targetTick,
            int actorCount,
            in OrderAdmissionOutcome core,
            bool isReplay,
            int committedTick)
        {
            return new NetworkCommandAdmissionOutcome(
                in seat,
                clientBatchSequence,
                targetTick,
                actorCount,
                core.OrderId,
                core.AdmissionBatchId,
                core.AdmissionBatchIndex,
                NetworkCommandAdmissionCodeSemantics.ProjectCoreAdmissionStage(core.Stage),
                NetworkCommandAdmissionCodeSemantics.ProjectCoreSubmitResult(core.Result),
                isReplay,
                committedTick);
        }

        public static NetworkCommandAdmissionOutcome FromTerminal(
            in NetworkCommandSeat seat,
            ulong clientBatchSequence,
            int targetTick,
            int actorCount,
            int orderId,
            int admissionBatchId,
            ushort admissionBatchIndex,
            OrderTerminalState state,
            bool isReplay,
            int committedTick)
        {
            return new NetworkCommandAdmissionOutcome(
                in seat,
                clientBatchSequence,
                targetTick,
                actorCount,
                orderId,
                admissionBatchId,
                admissionBatchIndex,
                NetworkCommandAdmissionStage.Terminal,
                NetworkCommandAdmissionCodeSemantics.ProjectTerminal(state),
                isReplay,
                committedTick);
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
