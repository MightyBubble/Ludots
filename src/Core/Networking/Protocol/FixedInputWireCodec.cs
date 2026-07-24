using System;

namespace Ludots.Core.Networking.Protocol
{
    /// <summary>
    /// Unfragmented fixed-input batch and acknowledgement codecs.
    /// Batches never spill to a reliable channel and never truncate.
    /// </summary>
    public static class FixedInputWireCodec
    {
        public const int TargetTickSizeInBytes = 4;

        /// <summary>
        /// Maximum tick representable by <see cref="Ludots.Core.Networking.Simulation.AuthoritativeSimulationTickState"/>.
        /// Wire and admission reject values above this bound.
        /// </summary>
        public const uint MaxSimulationTick = (uint)int.MaxValue;

        /// <summary>
        /// Tick fields that may be zero (committed-through / latest-received / missing) but must stay
        /// inside the authoritative simulation tick domain.
        /// </summary>
        public static bool IsValidSimulationTickField(uint tick) => tick <= MaxSimulationTick;

        /// <summary>
        /// Input target ticks must be nonzero and inside the authoritative simulation tick domain.
        /// </summary>
        public static bool IsValidInputTargetTick(uint tick) => tick != 0 && tick <= MaxSimulationTick;

        public static int GetFrameSize(int framePayloadBytes)
        {
            if (framePayloadBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(framePayloadBytes));
            }

            return checked(TargetTickSizeInBytes + framePayloadBytes);
        }

        public static int GetBatchPayloadSize(int framePayloadBytes, int frameCount)
        {
            if (frameCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameCount));
            }

            return checked(
                NetworkFixedInputBatchHeader.SizeInBytes +
                (frameCount * GetFrameSize(framePayloadBytes)));
        }

        public static int GetFramedBatchSize(int framePayloadBytes, int frameCount) =>
            checked(NetworkWireEnvelope.SizeInBytes + GetBatchPayloadSize(framePayloadBytes, frameCount));

        /// <summary>
        /// Fail-fast capacity contract: envelope + header + frames must fit one datagram.
        /// </summary>
        public static void ValidateBatchFitsDatagram(
            int maxDatagramPayloadBytes,
            int framePayloadBytes,
            int frameCount)
        {
            if (maxDatagramPayloadBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDatagramPayloadBytes));
            }

            if (framePayloadBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(framePayloadBytes));
            }

            if (frameCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameCount));
            }

            long framed = (long)NetworkWireEnvelope.SizeInBytes +
                NetworkFixedInputBatchHeader.SizeInBytes +
                ((long)frameCount * GetFrameSize(framePayloadBytes));
            if (framed > maxDatagramPayloadBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frameCount),
                    frameCount,
                    $"Fixed-input batch framed size {framed} exceeds MaxDatagramPayloadBytes {maxDatagramPayloadBytes}. Fragmentation and reliable fallback are forbidden.");
            }

            if (framed > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frameCount),
                    frameCount,
                    $"Fixed-input batch framed size {framed} exceeds envelope payload length capacity.");
            }
        }

        public static int GetMaxFrameCountForDatagram(int maxDatagramPayloadBytes, int framePayloadBytes)
        {
            if (maxDatagramPayloadBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDatagramPayloadBytes));
            }

            if (framePayloadBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(framePayloadBytes));
            }

            int overhead = NetworkWireEnvelope.SizeInBytes + NetworkFixedInputBatchHeader.SizeInBytes;
            if (maxDatagramPayloadBytes < overhead)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxDatagramPayloadBytes),
                    maxDatagramPayloadBytes,
                    $"Datagram payload must cover fixed-input framing overhead of {overhead} bytes.");
            }

            int frameSize = GetFrameSize(framePayloadBytes);
            if (frameSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(framePayloadBytes));
            }

            return (maxDatagramPayloadBytes - overhead) / frameSize;
        }

        public static void ValidateAcknowledgementFitsDatagram(int maxDatagramPayloadBytes)
        {
            if (maxDatagramPayloadBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDatagramPayloadBytes));
            }

            int framed = NetworkWireEnvelope.SizeInBytes + NetworkFixedInputAcknowledgement.SizeInBytes;
            if (framed > maxDatagramPayloadBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxDatagramPayloadBytes),
                    maxDatagramPayloadBytes,
                    $"Fixed-input acknowledgement framed size {framed} exceeds MaxDatagramPayloadBytes.");
            }
        }

        /// <summary>
        /// Allocation-free SSOT for batch header + frame tick invariants shared by encode and decode.
        /// Rejects zero epoch/schema/payload/count, target tick 0, ticks above <see cref="MaxSimulationTick"/>,
        /// and non-strictly-increasing frame ticks.
        /// </summary>
        public static bool IsValidBatchSemantics(
            in NetworkFixedInputBatchHeader header,
            ReadOnlySpan<uint> targetTicks)
        {
            if (header.SessionEpoch == 0 ||
                header.SchemaId == 0 ||
                header.FramePayloadBytes == 0 ||
                header.FrameCount == 0)
            {
                return false;
            }

            if (header.FrameCount != targetTicks.Length)
            {
                return false;
            }

            if (!IsValidSimulationTickField(header.AcknowledgedCommittedTick))
            {
                return false;
            }

            for (int i = 0; i < targetTicks.Length; i++)
            {
                uint tick = targetTicks[i];
                if (!IsValidInputTargetTick(tick))
                {
                    return false;
                }

                if (i > 0 && tick <= targetTicks[i - 1])
                {
                    return false;
                }
            }

            return true;
        }

        public static NetworkWireCodecStatus ValidateBatchSemantics(
            in NetworkFixedInputBatchHeader header,
            ReadOnlySpan<uint> targetTicks) =>
            IsValidBatchSemantics(in header, targetTicks)
                ? NetworkWireCodecStatus.Success
                : NetworkWireCodecStatus.InvalidInput;

        /// <summary>
        /// Allocation-free SSOT for acknowledgement invariants shared by encode and decode.
        /// <para>
        /// Valid ACK-mask invariant: when <see cref="NetworkFixedInputAcknowledgement.LatestReceivedTick"/> is 0,
        /// <see cref="NetworkFixedInputAcknowledgement.ReceivedMask"/> must be 0; when LatestReceivedTick is nonzero,
        /// bit 0 of ReceivedMask must be set (bit 0 represents LatestReceivedTick itself).
        /// <see cref="NetworkFixedInputAcknowledgement.CommittedThroughTick"/> may exceed LatestReceivedTick after
        /// observed deadline misses. All tick fields must be &lt;= <see cref="MaxSimulationTick"/>.
        /// SessionEpoch and SchemaId must be nonzero.
        /// </para>
        /// </summary>
        public static bool IsValidAcknowledgementSemantics(in NetworkFixedInputAcknowledgement acknowledgement)
        {
            if (acknowledgement.SessionEpoch == 0 || acknowledgement.SchemaId == 0)
            {
                return false;
            }

            if (!IsValidSimulationTickField(acknowledgement.CommittedThroughTick) ||
                !IsValidSimulationTickField(acknowledgement.LatestReceivedTick) ||
                !IsValidSimulationTickField(acknowledgement.LatestMissingInputTick))
            {
                return false;
            }

            if (acknowledgement.LatestReceivedTick == 0)
            {
                return acknowledgement.ReceivedMask == 0UL;
            }

            return (acknowledgement.ReceivedMask & 1UL) != 0UL;
        }

        public static NetworkWireCodecStatus ValidateAcknowledgementSemantics(
            in NetworkFixedInputAcknowledgement acknowledgement) =>
            IsValidAcknowledgementSemantics(in acknowledgement)
                ? NetworkWireCodecStatus.Success
                : NetworkWireCodecStatus.InvalidInput;

        public static NetworkWireCodecStatus TryEncodeBatch(
            in NetworkFixedInputBatchHeader header,
            ReadOnlySpan<uint> targetTicks,
            ReadOnlySpan<byte> payloads,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (!IsValidBatchSemantics(in header, targetTicks))
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            int framePayloadBytes = header.FramePayloadBytes;
            long expectedPayloadBytes = (long)header.FrameCount * framePayloadBytes;
            if (payloads.Length != expectedPayloadBytes)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            int required = GetBatchPayloadSize(framePayloadBytes, header.FrameCount);
            if (destination.Length < required)
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryWriteUInt64(destination, ref offset, header.SessionEpoch) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, header.SchemaId) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, header.FramePayloadBytes) ||
                !NetworkWireBinary.TryWriteUInt32(destination, ref offset, header.AcknowledgedCommittedTick) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, header.FrameCount) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, 0))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            for (int i = 0; i < header.FrameCount; i++)
            {
                if (!NetworkWireBinary.TryWriteUInt32(destination, ref offset, targetTicks[i]))
                {
                    return NetworkWireCodecStatus.BufferTooSmall;
                }

                ReadOnlySpan<byte> framePayload = payloads.Slice(i * framePayloadBytes, framePayloadBytes);
                if (!NetworkWireBinary.TryWriteBytes(destination, ref offset, framePayload))
                {
                    return NetworkWireCodecStatus.BufferTooSmall;
                }
            }

            bytesWritten = offset;
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryDecodeBatch(
            ReadOnlySpan<byte> source,
            Span<uint> targetTicks,
            Span<byte> payloads,
            out NetworkFixedInputBatchHeader header,
            out int frameCount)
        {
            header = default;
            frameCount = 0;
            if (source.Length < NetworkFixedInputBatchHeader.SizeInBytes)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong sessionEpoch) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort schemaId) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort framePayloadBytes) ||
                !NetworkWireBinary.TryReadUInt32(source, ref offset, out uint acknowledgedCommittedTick) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort declaredCount) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort reserved))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (reserved != 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            long required = NetworkFixedInputBatchHeader.SizeInBytes +
                ((long)declaredCount * GetFrameSize(framePayloadBytes));
            if (required > source.Length)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (required < source.Length)
            {
                return NetworkWireCodecStatus.TrailingBytes;
            }

            if (declaredCount > targetTicks.Length)
            {
                return NetworkWireCodecStatus.CapacityExhausted;
            }

            long payloadBytes = (long)declaredCount * framePayloadBytes;
            if (payloadBytes > payloads.Length)
            {
                return NetworkWireCodecStatus.CapacityExhausted;
            }

            for (int i = 0; i < declaredCount; i++)
            {
                if (!NetworkWireBinary.TryReadUInt32(source, ref offset, out uint tick))
                {
                    return NetworkWireCodecStatus.MalformedLength;
                }

                targetTicks[i] = tick;
                Span<byte> framePayload = payloads.Slice(i * framePayloadBytes, framePayloadBytes);
                if (!NetworkWireBinary.TryReadBytes(source, ref offset, framePayload))
                {
                    ClearDecodeDestinations(targetTicks, payloads, declaredCount, framePayloadBytes);
                    return NetworkWireCodecStatus.MalformedLength;
                }
            }

            NetworkWireCodecStatus end = NetworkWireBinary.EnsureExactEnd(source, offset);
            if (end != NetworkWireCodecStatus.Success)
            {
                ClearDecodeDestinations(targetTicks, payloads, declaredCount, framePayloadBytes);
                return end;
            }

            var candidate = new NetworkFixedInputBatchHeader(
                sessionEpoch,
                schemaId,
                framePayloadBytes,
                acknowledgedCommittedTick,
                declaredCount);
            if (!IsValidBatchSemantics(in candidate, targetTicks.Slice(0, declaredCount)))
            {
                ClearDecodeDestinations(targetTicks, payloads, declaredCount, framePayloadBytes);
                return NetworkWireCodecStatus.InvalidInput;
            }

            header = candidate;
            frameCount = declaredCount;
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryEncodeAcknowledgement(
            in NetworkFixedInputAcknowledgement acknowledgement,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (!IsValidAcknowledgementSemantics(in acknowledgement))
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            if (destination.Length < NetworkFixedInputAcknowledgement.SizeInBytes)
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryWriteUInt64(destination, ref offset, acknowledgement.SessionEpoch) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, acknowledgement.SchemaId) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, 0) ||
                !NetworkWireBinary.TryWriteUInt32(destination, ref offset, acknowledgement.CommittedThroughTick) ||
                !NetworkWireBinary.TryWriteUInt32(destination, ref offset, acknowledgement.LatestReceivedTick) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, acknowledgement.ReceivedMask) ||
                !NetworkWireBinary.TryWriteUInt32(destination, ref offset, acknowledgement.LatestMissingInputTick))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            bytesWritten = offset;
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryDecodeAcknowledgement(
            ReadOnlySpan<byte> source,
            out NetworkFixedInputAcknowledgement acknowledgement)
        {
            acknowledgement = default;
            if (source.Length < NetworkFixedInputAcknowledgement.SizeInBytes)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong sessionEpoch) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort schemaId) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort reserved) ||
                !NetworkWireBinary.TryReadUInt32(source, ref offset, out uint committedThroughTick) ||
                !NetworkWireBinary.TryReadUInt32(source, ref offset, out uint latestReceivedTick) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong receivedMask) ||
                !NetworkWireBinary.TryReadUInt32(source, ref offset, out uint latestMissingInputTick))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (reserved != 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            NetworkWireCodecStatus end = NetworkWireBinary.EnsureExactEnd(source, offset);
            if (end != NetworkWireCodecStatus.Success)
            {
                return end;
            }

            var candidate = new NetworkFixedInputAcknowledgement(
                sessionEpoch,
                schemaId,
                committedThroughTick,
                latestReceivedTick,
                receivedMask,
                latestMissingInputTick);
            if (!IsValidAcknowledgementSemantics(in candidate))
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            acknowledgement = candidate;
            return NetworkWireCodecStatus.Success;
        }

        private static void ClearDecodeDestinations(
            Span<uint> targetTicks,
            Span<byte> payloads,
            int frameCount,
            int framePayloadBytes)
        {
            if (frameCount <= 0)
            {
                return;
            }

            targetTicks.Slice(0, frameCount).Clear();
            long payloadBytes = (long)frameCount * framePayloadBytes;
            if (payloadBytes > 0 && payloadBytes <= payloads.Length)
            {
                payloads.Slice(0, (int)payloadBytes).Clear();
            }
        }
    }
}
