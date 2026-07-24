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
        // seatSlot i32 | seatGeneration u32 | playerId i32 | clientBatchSequence u64 |
        // targetTick i32 | actorCount i32 | orderId i32 | admissionBatchId i32 |
        // stage u8 | result u8 | isReplay u8 | reserved u8
        public const int SizeInBytes = 4 + 4 + 4 + 8 + 4 + 4 + 4 + 4 + 1 + 1 + 1 + 1;

        public static NetworkWireCodecStatus TryEncode(
            in NetworkCommandAdmissionOutcome outcome,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (!IsKnownStage(outcome.Stage) || !IsKnownResult(outcome.Result))
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            if (outcome.SeatSlot < 0 || outcome.PlayerId <= 0 || outcome.ActorCount < 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            if (destination.Length < SizeInBytes)
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryWriteInt32(destination, ref offset, outcome.SeatSlot) ||
                !NetworkWireBinary.TryWriteUInt32(destination, ref offset, outcome.SeatGeneration) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, outcome.PlayerId) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, outcome.ClientBatchSequence) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, outcome.TargetTick) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, outcome.ActorCount) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, outcome.OrderId) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, outcome.AdmissionBatchId) ||
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
            out NetworkCommandAdmissionOutcome outcome)
        {
            outcome = default;
            if (source.Length < SizeInBytes)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryReadInt32(source, ref offset, out int seatSlot) ||
                !NetworkWireBinary.TryReadUInt32(source, ref offset, out uint seatGeneration) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int playerId) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong clientBatchSequence) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int targetTick) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int actorCount) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int orderId) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int admissionBatchId) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out byte stageByte) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out byte resultByte) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out byte replayByte) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out _))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            NetworkWireCodecStatus end = NetworkWireBinary.EnsureExactEnd(source, offset);
            if (end != NetworkWireCodecStatus.Success)
            {
                return end;
            }

            if (replayByte > 1)
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            if (!IsKnownStageByte(stageByte) || !IsKnownResultByte(resultByte))
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            if (seatSlot < 0 || playerId <= 0 || actorCount < 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            var seat = new NetworkCommandSeat(seatSlot, seatGeneration, playerId);
            outcome = new NetworkCommandAdmissionOutcome(
                in seat,
                clientBatchSequence,
                targetTick,
                actorCount,
                orderId,
                admissionBatchId,
                (OrderSubmitResult)resultByte,
                isReplay: replayByte == 1);

            // Constructor derives Stage from Result; reject wires that disagree.
            if (outcome.Stage != (OrderAdmissionStage)stageByte)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            return NetworkWireCodecStatus.Success;
        }

        private static bool IsKnownStage(OrderAdmissionStage stage) => IsKnownStageByte((byte)stage);

        private static bool IsKnownStageByte(byte value) =>
            value is >= (byte)OrderAdmissionStage.GlobalIntake
                and <= (byte)OrderAdmissionStage.NetworkIntake;

        private static bool IsKnownResult(OrderSubmitResult result) => IsKnownResultByte((byte)result);

        private static bool IsKnownResultByte(byte value) =>
            value <= (byte)OrderSubmitResult.NetworkSequenceOutsideHistory;
    }
}
