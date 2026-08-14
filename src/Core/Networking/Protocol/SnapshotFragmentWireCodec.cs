using System;

namespace Ludots.Core.Networking.Protocol
{
    /// <summary>
    /// Binary little-endian codec for one <see cref="NetworkWireKind.SnapshotFragment"/> envelope payload.
    /// Fragmentation planning validates config-derived datagram and fixed fragment-count contracts.
    /// </summary>
    public static class SnapshotFragmentWireCodec
    {
        /// <summary>
        /// Maximum fragment data bytes that fit in one framed datagram:
        /// <c>maxDatagramPayloadBytes - envelope - fragment header</c>.
        /// </summary>
        public static int GetMaxFragmentDataBytes(int maxDatagramPayloadBytes)
        {
            if (maxDatagramPayloadBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDatagramPayloadBytes));
            }

            int overhead = NetworkWireEnvelope.SizeInBytes + NetworkSnapshotFragmentHeader.SizeInBytes;
            if (maxDatagramPayloadBytes <= overhead)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxDatagramPayloadBytes),
                    maxDatagramPayloadBytes,
                    $"Datagram payload must exceed framing overhead of {overhead} bytes.");
            }

            return maxDatagramPayloadBytes - overhead;
        }

        public static int GetWirePayloadSize(int fragmentDataLength)
        {
            if (fragmentDataLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fragmentDataLength));
            }

            return checked(NetworkSnapshotFragmentHeader.SizeInBytes + fragmentDataLength);
        }

        public static NetworkWireCodecStatus TryGetRequiredFragmentCount(
            int snapshotLength,
            int maxFragmentDataBytes,
            int maxFragments,
            out ushort fragmentCount)
        {
            fragmentCount = 0;
            if (snapshotLength < 0 || maxFragmentDataBytes <= 0 || maxFragments <= 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            if (maxFragments > ushort.MaxValue)
            {
                return NetworkWireCodecStatus.Overflow;
            }

            long required = snapshotLength == 0
                ? 1L
                : ((long)snapshotLength + maxFragmentDataBytes - 1L) / maxFragmentDataBytes;

            if (required > maxFragments || required > ushort.MaxValue)
            {
                return NetworkWireCodecStatus.CapacityExhausted;
            }

            fragmentCount = (ushort)required;
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryGetFragmentDataRange(
            int snapshotLength,
            int maxFragmentDataBytes,
            ushort fragmentIndex,
            ushort fragmentCount,
            out int offset,
            out int length)
        {
            offset = 0;
            length = 0;
            if (snapshotLength < 0 || maxFragmentDataBytes <= 0 || fragmentCount == 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            if (fragmentIndex >= fragmentCount)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            NetworkWireCodecStatus planned = TryGetRequiredFragmentCount(
                snapshotLength,
                maxFragmentDataBytes,
                maxFragments: ushort.MaxValue,
                out ushort expectedCount);
            if (planned != NetworkWireCodecStatus.Success)
            {
                return planned;
            }

            if (fragmentCount != expectedCount)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            long longOffset = (long)fragmentIndex * maxFragmentDataBytes;
            if (longOffset > snapshotLength)
            {
                return NetworkWireCodecStatus.Overflow;
            }

            offset = (int)longOffset;
            int remaining = snapshotLength - offset;
            if (fragmentIndex + 1 < fragmentCount)
            {
                if (remaining < maxFragmentDataBytes)
                {
                    return NetworkWireCodecStatus.InvalidInput;
                }

                length = maxFragmentDataBytes;
            }
            else
            {
                length = remaining;
                if (length > maxFragmentDataBytes)
                {
                    return NetworkWireCodecStatus.InvalidInput;
                }
            }

            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryEncode(
            in NetworkSnapshotFragmentHeader header,
            ReadOnlySpan<byte> fragmentData,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            NetworkWireCodecStatus validation = ValidateHeaderAgainstData(in header, fragmentData);
            if (validation != NetworkWireCodecStatus.Success)
            {
                return validation;
            }

            int required = GetWirePayloadSize(fragmentData.Length);
            if (destination.Length < required)
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryWriteUInt64(destination, ref offset, header.SessionEpoch) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, header.SnapshotId) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, header.FragmentIndex) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, header.FragmentCount) ||
                !NetworkWireBinary.TryWriteUInt32(destination, ref offset, header.TotalPayloadLength) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, header.FragmentPayloadLength) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, 0) ||
                !NetworkWireBinary.TryWriteBytes(destination, ref offset, fragmentData))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            bytesWritten = offset;
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryDecode(
            ReadOnlySpan<byte> source,
            out NetworkSnapshotFragmentHeader header,
            out ReadOnlySpan<byte> fragmentData)
        {
            header = default;
            fragmentData = default;
            if (source.Length < NetworkSnapshotFragmentHeader.SizeInBytes)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong sessionEpoch) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong snapshotId) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort fragmentIndex) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort fragmentCount) ||
                !NetworkWireBinary.TryReadUInt32(source, ref offset, out uint totalPayloadLength) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort fragmentPayloadLength) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort reserved))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (reserved != 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            int remaining = source.Length - offset;
            if (remaining < fragmentPayloadLength)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (remaining > fragmentPayloadLength)
            {
                return NetworkWireCodecStatus.TrailingBytes;
            }

            ReadOnlySpan<byte> data = source.Slice(offset, fragmentPayloadLength);
            var decoded = new NetworkSnapshotFragmentHeader(
                sessionEpoch,
                snapshotId,
                fragmentIndex,
                fragmentCount,
                totalPayloadLength,
                fragmentPayloadLength);
            NetworkWireCodecStatus validation = ValidateHeaderAgainstData(in decoded, data);
            if (validation != NetworkWireCodecStatus.Success)
            {
                return validation;
            }

            header = decoded;
            fragmentData = data;
            return NetworkWireCodecStatus.Success;
        }

        private static NetworkWireCodecStatus ValidateHeaderAgainstData(
            in NetworkSnapshotFragmentHeader header,
            ReadOnlySpan<byte> fragmentData)
        {
            if (header.SessionEpoch == 0 || header.SnapshotId == 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            if (header.FragmentCount == 0 || header.FragmentIndex >= header.FragmentCount)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            if (header.FragmentPayloadLength != fragmentData.Length)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            if (header.FragmentPayloadLength > header.TotalPayloadLength)
            {
                return NetworkWireCodecStatus.Overflow;
            }

            if (header.FragmentCount == 1)
            {
                if (header.FragmentPayloadLength != header.TotalPayloadLength)
                {
                    return NetworkWireCodecStatus.InvalidInput;
                }

                return NetworkWireCodecStatus.Success;
            }

            // Non-final fragments must be non-empty; final fragment carries the remainder.
            if (header.FragmentIndex + 1 < header.FragmentCount)
            {
                if (header.FragmentPayloadLength == 0)
                {
                    return NetworkWireCodecStatus.InvalidInput;
                }

                ulong prefixExclusive = (ulong)(header.FragmentIndex + 1u) * header.FragmentPayloadLength;
                if (prefixExclusive > header.TotalPayloadLength)
                {
                    return NetworkWireCodecStatus.Overflow;
                }
            }
            else
            {
                ulong prefix = header.TotalPayloadLength - header.FragmentPayloadLength;
                ushort nonFinalCount = (ushort)(header.FragmentCount - 1);
                if (prefix % nonFinalCount != 0)
                {
                    return NetworkWireCodecStatus.InvalidInput;
                }

                uint chunk = (uint)(prefix / nonFinalCount);
                if (chunk == 0 || header.FragmentPayloadLength > chunk)
                {
                    return NetworkWireCodecStatus.InvalidInput;
                }
            }

            return NetworkWireCodecStatus.Success;
        }
    }

    /// <summary>
    /// Fixed-capacity snapshot fragmentation planner/encoder.
    /// Construction pre-validates datagram and fragment contracts; steady-state encode allocates 0 B.
    /// </summary>
    public sealed class SnapshotFragmentEncoder
    {
        private readonly int _maxDatagramPayloadBytes;
        private readonly int _maxFragmentDataBytes;
        private readonly int _maxSnapshotBytes;
        private readonly int _maxFragments;

        public SnapshotFragmentEncoder(int maxDatagramPayloadBytes, int maxSnapshotBytes, int maxFragments)
        {
            if (maxDatagramPayloadBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDatagramPayloadBytes));
            }

            if (maxSnapshotBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSnapshotBytes));
            }

            if (maxFragments <= 0 || maxFragments > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFragments));
            }

            _maxDatagramPayloadBytes = maxDatagramPayloadBytes;
            _maxFragmentDataBytes = SnapshotFragmentWireCodec.GetMaxFragmentDataBytes(maxDatagramPayloadBytes);
            _maxSnapshotBytes = maxSnapshotBytes;
            _maxFragments = maxFragments;

            long maxEncodable = (long)_maxFragmentDataBytes * _maxFragments;
            if (maxEncodable < _maxSnapshotBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxFragments),
                    maxFragments,
                    "Fragment capacity cannot cover maxSnapshotBytes at the configured datagram size.");
            }
        }

        public int MaxDatagramPayloadBytes => _maxDatagramPayloadBytes;
        public int MaxFragmentDataBytes => _maxFragmentDataBytes;
        public int MaxSnapshotBytes => _maxSnapshotBytes;
        public int MaxFragments => _maxFragments;

        public NetworkWireCodecStatus TryGetFragmentCount(int snapshotLength, out ushort fragmentCount)
        {
            fragmentCount = 0;
            if ((uint)snapshotLength > (uint)_maxSnapshotBytes)
            {
                return NetworkWireCodecStatus.CapacityExhausted;
            }

            return SnapshotFragmentWireCodec.TryGetRequiredFragmentCount(
                snapshotLength,
                _maxFragmentDataBytes,
                _maxFragments,
                out fragmentCount);
        }

        public NetworkWireCodecStatus TryEncodeFragment(
            ulong sessionEpoch,
            ulong snapshotId,
            ReadOnlySpan<byte> snapshotPayload,
            ushort fragmentIndex,
            ushort fragmentCount,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (sessionEpoch == 0 || snapshotId == 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            if ((uint)snapshotPayload.Length > (uint)_maxSnapshotBytes)
            {
                return NetworkWireCodecStatus.CapacityExhausted;
            }

            NetworkWireCodecStatus countStatus = TryGetFragmentCount(snapshotPayload.Length, out ushort expectedCount);
            if (countStatus != NetworkWireCodecStatus.Success)
            {
                return countStatus;
            }

            if (fragmentCount != expectedCount)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            NetworkWireCodecStatus rangeStatus = SnapshotFragmentWireCodec.TryGetFragmentDataRange(
                snapshotPayload.Length,
                _maxFragmentDataBytes,
                fragmentIndex,
                fragmentCount,
                out int offset,
                out int length);
            if (rangeStatus != NetworkWireCodecStatus.Success)
            {
                return rangeStatus;
            }

            if (length > ushort.MaxValue)
            {
                return NetworkWireCodecStatus.Overflow;
            }

            int wirePayloadSize = SnapshotFragmentWireCodec.GetWirePayloadSize(length);
            int framed = NetworkWireEnvelope.SizeInBytes + wirePayloadSize;
            if (framed > _maxDatagramPayloadBytes)
            {
                return NetworkWireCodecStatus.CapacityExhausted;
            }

            if (destination.Length < wirePayloadSize)
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            var header = new NetworkSnapshotFragmentHeader(
                sessionEpoch,
                snapshotId,
                fragmentIndex,
                fragmentCount,
                (uint)snapshotPayload.Length,
                (ushort)length);

            return SnapshotFragmentWireCodec.TryEncode(
                in header,
                snapshotPayload.Slice(offset, length),
                destination,
                out bytesWritten);
        }
    }
}
