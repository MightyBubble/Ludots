using System;
using Ludots.Core.Networking.Protocol;

namespace Ludots.Core.Networking.FixedInput
{
    /// <summary>
    /// Fixed-capacity contract for authoritative fixed-input ingress and client outbox.
    /// Physics3D default sizing (150 seats × 64 ticks × 12-byte payload) is a supported consumer profile.
    /// </summary>
    public readonly struct FixedInputProtocolConfig
    {
        public const int MinimumSupportedSeatCapacity = 150;
        public const int MinimumSupportedHistoryTicks = 64;
        public const int MinimumSupportedPayloadBytes = 12;

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
            SeatCapacity = RequirePositive(seatCapacity, nameof(seatCapacity));
            HistoryTicksPerSeat = RequirePositive(historyTicksPerSeat, nameof(historyTicksPerSeat));
            if (schemaId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaId), "Fixed-input schema id must be non-zero.");
            }

            SchemaId = schemaId;
            if (framePayloadBytes == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(framePayloadBytes),
                    framePayloadBytes,
                    "FramePayloadBytes must be positive.");
            }

            FramePayloadBytes = framePayloadBytes;
            if (maxFutureTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFutureTicks), "MaxFutureTicks must be positive.");
            }

            if (historyTicksPerSeat < maxFutureTicks)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(historyTicksPerSeat),
                    historyTicksPerSeat,
                    $"HistoryTicksPerSeat must be >= MaxFutureTicks ({maxFutureTicks}) so admissible uncommitted ticks cannot alias in the ring.");
            }

            MaxFutureTicks = maxFutureTicks;
            MaxFramesPerBatch = RequirePositive(maxFramesPerBatch, nameof(maxFramesPerBatch));
            MaxDatagramPayloadBytes = RequirePositive(maxDatagramPayloadBytes, nameof(maxDatagramPayloadBytes));
            if (sessionEpoch == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sessionEpoch), "Session epoch must be non-zero.");
            }

            SessionEpoch = sessionEpoch;

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
                    nameof(maxFramesPerBatch),
                    maxFramesPerBatch,
                    $"MaxFramesPerBatch cannot exceed datagram capacity {datagramMaxFrames} for payload {FramePayloadBytes}.");
            }

            // Prove the Physics3D consumer floor remains representable with fixed arrays.
            _ = checked(MinimumSupportedSeatCapacity * MinimumSupportedHistoryTicks * MinimumSupportedPayloadBytes);
            _ = checked((long)SeatCapacity * HistoryTicksPerSeat * FramePayloadBytes);
        }

        public int SeatCapacity { get; }
        public int HistoryTicksPerSeat { get; }
        public ushort SchemaId { get; }
        public ushort FramePayloadBytes { get; }
        public int MaxFutureTicks { get; }
        public int MaxFramesPerBatch { get; }
        public int MaxDatagramPayloadBytes { get; }
        public ulong SessionEpoch { get; }

        public static FixedInputProtocolConfig CreatePhysics3DDefaultFloor(
            ushort schemaId,
            ulong sessionEpoch,
            int maxFutureTicks,
            int maxFramesPerBatch,
            int maxDatagramPayloadBytes) =>
            new(
                MinimumSupportedSeatCapacity,
                MinimumSupportedHistoryTicks,
                schemaId,
                (ushort)MinimumSupportedPayloadBytes,
                maxFutureTicks,
                maxFramesPerBatch,
                maxDatagramPayloadBytes,
                sessionEpoch);

        private static int RequirePositive(int value, string name) =>
            value > 0
                ? value
                : throw new ArgumentOutOfRangeException(name, value, $"{name} must be positive.");
    }
}
