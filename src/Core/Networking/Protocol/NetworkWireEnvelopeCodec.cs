using System;
using System.Buffers.Binary;

namespace Ludots.Core.Networking.Protocol
{
    /// <summary>
    /// Fixed little-endian envelope codec. Payload codecs operate on the envelope payload span.
    /// </summary>
    public static class NetworkWireEnvelopeCodec
    {
        public static int GetFramedLength(int payloadLength)
        {
            if ((uint)payloadLength > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadLength));
            }

            return NetworkWireEnvelope.SizeInBytes + payloadLength;
        }

        public static NetworkWireCodecStatus TryEncode(
            NetworkWireKind kind,
            ReadOnlySpan<byte> payload,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (!IsKnownKind(kind))
            {
                return NetworkWireCodecStatus.UnknownKind;
            }

            if ((uint)payload.Length > ushort.MaxValue)
            {
                return NetworkWireCodecStatus.Overflow;
            }

            int total = NetworkWireEnvelope.SizeInBytes + payload.Length;
            if (destination.Length < total)
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), NetworkWireEnvelope.Magic);
            destination[4] = NetworkWireEnvelope.CurrentVersion;
            destination[5] = (byte)kind;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), (ushort)payload.Length);
            payload.CopyTo(destination.Slice(NetworkWireEnvelope.SizeInBytes, payload.Length));
            bytesWritten = total;
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryDecode(
            ReadOnlySpan<byte> source,
            out NetworkWireEnvelope envelope,
            out ReadOnlySpan<byte> payload)
        {
            envelope = default;
            payload = default;
            if (source.Length < NetworkWireEnvelope.SizeInBytes)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(0, 4));
            if (magic != NetworkWireEnvelope.Magic)
            {
                return NetworkWireCodecStatus.UnknownSchema;
            }

            byte version = source[4];
            if (version != NetworkWireEnvelope.CurrentVersion)
            {
                return NetworkWireCodecStatus.UnknownVersion;
            }

            byte kindByte = source[5];
            if (!IsKnownKindByte(kindByte))
            {
                return NetworkWireCodecStatus.UnknownKind;
            }

            ushort payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(6, 2));
            int total = NetworkWireEnvelope.SizeInBytes + payloadLength;
            if (source.Length < total)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (source.Length > total)
            {
                return NetworkWireCodecStatus.TrailingBytes;
            }

            envelope = new NetworkWireEnvelope(version, (NetworkWireKind)kindByte, payloadLength);
            payload = source.Slice(NetworkWireEnvelope.SizeInBytes, payloadLength);
            return NetworkWireCodecStatus.Success;
        }

        public static bool IsKnownKind(NetworkWireKind kind) => IsKnownKindByte((byte)kind);

        private static bool IsKnownKindByte(byte kind) =>
            kind is >= (byte)NetworkWireKind.SessionHandshakeRequest
                and <= (byte)NetworkWireKind.SnapshotFragment;
    }
}
