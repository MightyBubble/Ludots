using System;

namespace Ludots.Core.Networking.Protocol
{
    /// <summary>
    /// Snapshot acknowledgement and explicit ResyncRequired message codecs.
    /// </summary>
    public static class SnapshotControlWireCodec
    {
        public static NetworkWireCodecStatus TryEncodeAcknowledgement(
            in NetworkSnapshotAcknowledgement acknowledgement,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (destination.Length < NetworkSnapshotAcknowledgement.SizeInBytes)
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryWriteUInt64(destination, ref offset, acknowledgement.SessionEpoch) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, acknowledgement.SnapshotId) ||
                !NetworkWireBinary.TryWriteUInt32(destination, ref offset, acknowledgement.CommittedTick))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            bytesWritten = offset;
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryDecodeAcknowledgement(
            ReadOnlySpan<byte> source,
            out NetworkSnapshotAcknowledgement acknowledgement)
        {
            acknowledgement = default;
            if (source.Length < NetworkSnapshotAcknowledgement.SizeInBytes)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong sessionEpoch) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong snapshotId) ||
                !NetworkWireBinary.TryReadUInt32(source, ref offset, out uint committedTick))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            NetworkWireCodecStatus end = NetworkWireBinary.EnsureExactEnd(source, offset);
            if (end != NetworkWireCodecStatus.Success)
            {
                return end;
            }

            acknowledgement = new NetworkSnapshotAcknowledgement(sessionEpoch, snapshotId, committedTick);
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryEncodeResyncRequired(
            in NetworkResyncRequired message,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (destination.Length < NetworkResyncRequired.SizeInBytes)
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            if (!IsKnownResyncReason(message.Reason))
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryWriteUInt64(destination, ref offset, message.SessionEpoch) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, (byte)message.Reason) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, 0) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, 0) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, 0) ||
                !NetworkWireBinary.TryWriteUInt32(destination, ref offset, message.LatestCommittedTick) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, message.LatestSnapshotId))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            bytesWritten = offset;
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryDecodeResyncRequired(
            ReadOnlySpan<byte> source,
            out NetworkResyncRequired message)
        {
            message = default;
            if (source.Length < NetworkResyncRequired.SizeInBytes)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong sessionEpoch) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out byte reasonByte) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out _) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out _) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out _) ||
                !NetworkWireBinary.TryReadUInt32(source, ref offset, out uint latestTick) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong latestSnapshotId))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            NetworkWireCodecStatus end = NetworkWireBinary.EnsureExactEnd(source, offset);
            if (end != NetworkWireCodecStatus.Success)
            {
                return end;
            }

            if (!IsKnownResyncReasonByte(reasonByte))
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            message = new NetworkResyncRequired(
                sessionEpoch,
                (NetworkResyncReason)reasonByte,
                latestTick,
                latestSnapshotId);
            return NetworkWireCodecStatus.Success;
        }

        private static bool IsKnownResyncReason(NetworkResyncReason reason) =>
            IsKnownResyncReasonByte((byte)reason);

        private static bool IsKnownResyncReasonByte(byte value) =>
            value is >= (byte)NetworkResyncReason.BaselineUnavailable
                and <= (byte)NetworkResyncReason.SnapshotAcknowledgementTimeout;
    }
}
