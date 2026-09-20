using System;
using Ludots.Core.Networking.Session;

namespace Ludots.Core.Networking.Protocol
{
    /// <summary>
    /// Little-endian codecs for session handshake request, response, and confirmation payloads.
    /// </summary>
    public static class HandshakeWireCodec
    {
        public const int RequestSizeInBytes =
            2 + 2 + ContentFingerprint.ByteLength + 8 + 8 + 8;

        public const int ResponseSizeInBytes =
            1 + 1 + 2 + 4 + 4 + 4 + 8 + 8 + 2 + 2 + ContentFingerprint.ByteLength + 8 + 8;

        public const int ConfirmationSizeInBytes = 8 + 4 + 4 + 8 + 8;

        public static NetworkWireCodecStatus TryEncodeRequest(
            in SessionHandshakeRequest request,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (!request.IsWellFormed)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            if (destination.Length < RequestSizeInBytes)
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryWriteUInt16(destination, ref offset, request.ProtocolVersion.Major) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, request.ProtocolVersion.Minor))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            Span<byte> fingerprint = destination.Slice(offset, ContentFingerprint.ByteLength);
            request.ContentFingerprint.CopyTo(fingerprint);
            offset += ContentFingerprint.ByteLength;

            if (!NetworkWireBinary.TryWriteUInt64(destination, ref offset, request.ReconnectToken.Low) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, request.ReconnectToken.High) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, request.SessionEpoch.Value))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            bytesWritten = offset;
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryDecodeRequest(
            ReadOnlySpan<byte> source,
            out SessionHandshakeRequest request)
        {
            request = default;
            if (source.Length < RequestSizeInBytes)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort major) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort minor))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            Span<byte> fingerprintBytes = stackalloc byte[ContentFingerprint.ByteLength];
            if (!NetworkWireBinary.TryReadBytes(source, ref offset, fingerprintBytes))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (!NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong tokenLow) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong tokenHigh) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong epoch))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            NetworkWireCodecStatus end = NetworkWireBinary.EnsureExactEnd(source, offset);
            if (end != NetworkWireCodecStatus.Success)
            {
                return end;
            }

            var version = new ProtocolVersion(major, minor);
            if (!version.IsWellFormed)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            request = new SessionHandshakeRequest(
                version,
                ContentFingerprint.FromBytes(fingerprintBytes),
                new ReconnectToken(tokenLow, tokenHigh),
                new SessionEpoch(epoch));
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryEncodeResponse(
            in SessionHandshakeResponse response,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (destination.Length < ResponseSizeInBytes)
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            if (response.Accepted)
            {
                if (response.RejectReason != HandshakeRejectReason.None ||
                    !response.Seat.IsValid ||
                    response.ReconnectToken.IsEmpty ||
                    response.SessionEpoch.IsEmpty ||
                    response.NextClientBatchSequence == 0)
                {
                    return NetworkWireCodecStatus.InvalidInput;
                }
            }
            else if (!IsKnownRejectReason((byte)response.RejectReason))
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            if (!response.ProtocolVersion.IsWellFormed)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryWriteByte(destination, ref offset, response.Accepted ? (byte)1 : (byte)0) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, (byte)response.RejectReason) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, 0) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, response.Accepted ? response.Seat.Slot : -1) ||
                !NetworkWireBinary.TryWriteUInt32(destination, ref offset, response.Accepted ? response.Seat.Generation : 0) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, response.Accepted ? response.PlayerId.Value : 0) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, response.ReconnectToken.Low) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, response.ReconnectToken.High) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, response.ProtocolVersion.Major) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, response.ProtocolVersion.Minor))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            Span<byte> fingerprint = destination.Slice(offset, ContentFingerprint.ByteLength);
            response.ContentFingerprint.CopyTo(fingerprint);
            offset += ContentFingerprint.ByteLength;

            if (!NetworkWireBinary.TryWriteUInt64(destination, ref offset, response.SessionEpoch.Value) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, response.NextClientBatchSequence))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            bytesWritten = offset;
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryDecodeResponse(
            ReadOnlySpan<byte> source,
            out SessionHandshakeResponse response)
        {
            response = default;
            if (source.Length < ResponseSizeInBytes)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryReadByte(source, ref offset, out byte acceptedByte) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out byte rejectByte) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort reserved) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int seatSlot) ||
                !NetworkWireBinary.TryReadUInt32(source, ref offset, out uint seatGeneration) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int playerIdValue) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong tokenLow) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong tokenHigh) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort major) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort minor))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (acceptedByte > 1)
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            if (reserved != 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            Span<byte> fingerprintBytes = stackalloc byte[ContentFingerprint.ByteLength];
            if (!NetworkWireBinary.TryReadBytes(source, ref offset, fingerprintBytes) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong epoch) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong nextClientBatchSequence))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            NetworkWireCodecStatus end = NetworkWireBinary.EnsureExactEnd(source, offset);
            if (end != NetworkWireCodecStatus.Success)
            {
                return end;
            }

            var version = new ProtocolVersion(major, minor);
            if (!version.IsWellFormed)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            ContentFingerprint fingerprint = ContentFingerprint.FromBytes(fingerprintBytes);
            var sessionEpoch = new SessionEpoch(epoch);
            var token = new ReconnectToken(tokenLow, tokenHigh);
            bool accepted = acceptedByte == 1;

            if (accepted)
            {
                if (rejectByte != (byte)HandshakeRejectReason.None)
                {
                    return NetworkWireCodecStatus.InvalidEnum;
                }

                if (seatSlot < 0 || seatGeneration == 0 || playerIdValue <= 0)
                {
                    return NetworkWireCodecStatus.InvalidInput;
                }

                var seat = new SessionSeatBinding(seatSlot, seatGeneration, new PlayerId(playerIdValue));
                if (!seat.IsValid || token.IsEmpty || sessionEpoch.IsEmpty || nextClientBatchSequence == 0)
                {
                    return NetworkWireCodecStatus.InvalidInput;
                }

                response = SessionHandshakeResponse.Accept(
                    in seat,
                    token,
                    version,
                    fingerprint,
                    sessionEpoch,
                    nextClientBatchSequence);
                return NetworkWireCodecStatus.Success;
            }

            if (!IsKnownRejectReason(rejectByte))
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            if (seatSlot != -1 ||
                seatGeneration != 0 ||
                playerIdValue != 0 ||
                !token.IsEmpty ||
                nextClientBatchSequence != 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            response = SessionHandshakeResponse.Reject(
                (HandshakeRejectReason)rejectByte,
                version,
                fingerprint,
                sessionEpoch);
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryEncodeConfirmation(
            in SessionHandshakeConfirmation confirmation,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (!confirmation.IsWellFormed)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            if (destination.Length < ConfirmationSizeInBytes)
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryWriteUInt64(destination, ref offset, confirmation.SessionEpoch.Value) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, confirmation.SeatSlot) ||
                !NetworkWireBinary.TryWriteUInt32(destination, ref offset, confirmation.SeatGeneration) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, confirmation.ReconnectToken.Low) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, confirmation.ReconnectToken.High))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            bytesWritten = offset;
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryDecodeConfirmation(
            ReadOnlySpan<byte> source,
            out SessionHandshakeConfirmation confirmation)
        {
            confirmation = default;
            if (source.Length < ConfirmationSizeInBytes)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong epoch) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int seatSlot) ||
                !NetworkWireBinary.TryReadUInt32(source, ref offset, out uint seatGeneration) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong tokenLow) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong tokenHigh))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            NetworkWireCodecStatus end = NetworkWireBinary.EnsureExactEnd(source, offset);
            if (end != NetworkWireCodecStatus.Success)
            {
                return end;
            }

            confirmation = new SessionHandshakeConfirmation(
                new SessionEpoch(epoch),
                seatSlot,
                seatGeneration,
                new ReconnectToken(tokenLow, tokenHigh));
            return confirmation.IsWellFormed
                ? NetworkWireCodecStatus.Success
                : NetworkWireCodecStatus.InvalidInput;
        }

        private static bool IsKnownRejectReason(byte value) =>
            value is >= (byte)HandshakeRejectReason.ProtocolMismatch
                and <= (byte)HandshakeRejectReason.MatchAlreadyStarted;
    }
}
