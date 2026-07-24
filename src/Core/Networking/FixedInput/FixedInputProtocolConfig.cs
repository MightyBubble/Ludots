using System;
using Ludots.Core.Networking.Protocol;

namespace Ludots.Core.Networking.FixedInput
{
    /// <summary>
    /// Fixed-capacity contract for authoritative fixed-input ingress and client outbox.
    /// Consumer-specific floors (for example Physics3D 150×64×12) belong to that consumer's config,
    /// not this generic Core type.
    /// </summary>
    public readonly struct FixedInputProtocolConfig
    {
        public FixedInputProtocolConfig(
            int seatCapacity,
            int historyTicksPerSeat,
            ushort schemaId,
            ushort framePayloadBytes,
            int maxFutureTicks,
            int maxFramesPerBatch,
            int maxDatagramPayloadBytes,
            ulong sessionEpoch)
        {
            SeatCapacity = seatCapacity;
            HistoryTicksPerSeat = historyTicksPerSeat;
            SchemaId = schemaId;
            FramePayloadBytes = framePayloadBytes;
            MaxFutureTicks = maxFutureTicks;
            MaxFramesPerBatch = maxFramesPerBatch;
            MaxDatagramPayloadBytes = maxDatagramPayloadBytes;
            SessionEpoch = sessionEpoch;
            EnsureValid();
        }

        public int SeatCapacity { get; }
        public int HistoryTicksPerSeat { get; }
        public ushort SchemaId { get; }
        public ushort FramePayloadBytes { get; }
        public int MaxFutureTicks { get; }
        public int MaxFramesPerBatch { get; }
        public int MaxDatagramPayloadBytes { get; }
        public ulong SessionEpoch { get; }

        /// <summary>
        /// Allocation-free SSOT for every constructor invariant.
        /// Required because <c>default(FixedInputProtocolConfig)</c> bypasses the constructor.
        /// </summary>
        public void EnsureValid()
        {
            if (SeatCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(SeatCapacity),
                    SeatCapacity,
                    "SeatCapacity must be positive.");
            }

            if (HistoryTicksPerSeat <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(HistoryTicksPerSeat),
                    HistoryTicksPerSeat,
                    "HistoryTicksPerSeat must be positive.");
            }

            if (SchemaId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(SchemaId), "Fixed-input schema id must be non-zero.");
            }

            if (FramePayloadBytes == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(FramePayloadBytes),
                    FramePayloadBytes,
                    "FramePayloadBytes must be positive.");
            }

            if (MaxFutureTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxFutureTicks), "MaxFutureTicks must be positive.");
            }

            if (HistoryTicksPerSeat < MaxFutureTicks)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(HistoryTicksPerSeat),
                    HistoryTicksPerSeat,
                    $"HistoryTicksPerSeat must be >= MaxFutureTicks ({MaxFutureTicks}) so admissible uncommitted ticks cannot alias in the ring.");
            }

            if (MaxFramesPerBatch <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxFramesPerBatch),
                    MaxFramesPerBatch,
                    "MaxFramesPerBatch must be positive.");
            }

            if (MaxDatagramPayloadBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxDatagramPayloadBytes),
                    MaxDatagramPayloadBytes,
                    "MaxDatagramPayloadBytes must be positive.");
            }

            if (SessionEpoch == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(SessionEpoch), "Session epoch must be non-zero.");
            }

            FixedInputWireCodec.ValidateAcknowledgementFitsDatagram(MaxDatagramPayloadBytes);
            FixedInputWireCodec.ValidateBatchFitsDatagram(
                MaxDatagramPayloadBytes,
                FramePayloadBytes,
                MaxFramesPerBatch);

            int datagramMaxFrames = FixedInputWireCodec.GetMaxFrameCountForDatagram(
                MaxDatagramPayloadBytes,
                FramePayloadBytes);
            if (MaxFramesPerBatch > datagramMaxFrames)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxFramesPerBatch),
                    MaxFramesPerBatch,
                    $"MaxFramesPerBatch cannot exceed datagram capacity {datagramMaxFrames} for payload {FramePayloadBytes}.");
            }

            _ = checked((long)SeatCapacity * HistoryTicksPerSeat * FramePayloadBytes);
        }
    }
}
