using System;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Networking.Commands;

namespace Ludots.Core.Networking.Protocol
{
    /// <summary>
    /// Little-endian codec for server-authored command admission outcomes.
    /// </summary>
    public static class CommandAdmissionWireCodec
    {
        // sessionEpoch u64 | clientBatchSequence u64 | targetTick i32 | committedTick i32 | actorCount i32 |
        // orderId i32 | admissionBatchId i32 | admissionBatchIndex u16 | reserved u16 |
        // stage u8 | result u8 | isReplay u8 | reserved u8
        public const int SizeInBytes = 8 + 8 + 4 + 4 + 4 + 4 + 4 + 2 + 2 + 1 + 1 + 1 + 1;

        public static NetworkWireCodecStatus TryEncode(
            ulong sessionEpoch,
            in NetworkCommandAdmissionOutcome outcome,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (!IsKnownStage(outcome.Stage) || !IsKnownResult(outcome.Result))
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            if (sessionEpoch == 0 ||
                outcome.SeatSlot < 0 ||
                outcome.PlayerId <= 0 ||
                !NetworkCommandAdmissionOutcome.IsValidCommittedTick(outcome.Stage, outcome.CommittedTick) ||
                outcome.ActorCount < 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            if (destination.Length < SizeInBytes)
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryWriteUInt64(destination, ref offset, sessionEpoch) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, outcome.ClientBatchSequence) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, outcome.TargetTick) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, outcome.CommittedTick) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, outcome.ActorCount) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, outcome.OrderId) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, outcome.AdmissionBatchId) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, outcome.AdmissionBatchIndex) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, 0) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, (byte)outcome.Stage) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, (byte)outcome.Result) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, outcome.IsReplay ? (byte)1 : (byte)0) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, 0))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            bytesWritten = offset;
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryDecode(
            ReadOnlySpan<byte> source,
            ulong expectedSessionEpoch,
            in NetworkCommandSeat authenticatedSeat,
            out NetworkCommandAdmissionOutcome outcome)
        {
            outcome = default;
            if (source.Length < SizeInBytes)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong sessionEpoch) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong clientBatchSequence) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int targetTick) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int committedTick) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int actorCount) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int orderId) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int admissionBatchId) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort admissionBatchIndex) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort reserved) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out byte stageByte) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out byte resultByte) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out byte replayByte) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out byte trailingReserved))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            NetworkWireCodecStatus end = NetworkWireBinary.EnsureExactEnd(source, offset);
            if (end != NetworkWireCodecStatus.Success)
            {
                return end;
            }

            if (replayByte > 1 || reserved != 0 || trailingReserved != 0)
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            if (!IsKnownStageByte(stageByte) || !IsKnownResultByte(resultByte))
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            if (expectedSessionEpoch == 0 ||
                sessionEpoch != expectedSessionEpoch ||
                authenticatedSeat.Slot < 0 ||
                authenticatedSeat.PlayerId <= 0 ||
                !NetworkCommandAdmissionOutcome.IsValidCommittedTick(
                    (OrderAdmissionStage)stageByte,
                    committedTick) ||
                actorCount < 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            outcome = new NetworkCommandAdmissionOutcome(
                in authenticatedSeat,
                clientBatchSequence,
                targetTick,
                actorCount,
                orderId,
                admissionBatchId,
                admissionBatchIndex,
                (OrderAdmissionStage)stageByte,
                (OrderSubmitResult)resultByte,
                isReplay: replayByte == 1,
                committedTick: committedTick);
            return NetworkWireCodecStatus.Success;
        }

        private static bool IsKnownStage(OrderAdmissionStage stage) => IsKnownStageByte((byte)stage);

        private static bool IsKnownStageByte(byte value) =>
            value is >= (byte)OrderAdmissionStage.GlobalIntake
                and <= (byte)OrderAdmissionStage.NetworkIntake;

        private static bool IsKnownResult(OrderSubmitResult result) => IsKnownResultByte((byte)result);

        private static bool IsKnownResultByte(byte value) =>
            value <= (byte)OrderSubmitResult.Cancelled;
    }
}
