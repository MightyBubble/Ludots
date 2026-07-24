using System;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;

namespace Ludots.Core.Networking.FixedInput
{
    public enum FixedInputSendPulseStatus : byte
    {
        Invalid = 0,
        Accepted = 1,
        NotConnected = 2,
        NoData = 3,
        BatchBuildRejected = 4,
        TransportRejected = 5,
    }

    /// <summary>
    /// Result for one fixed-input send pulse. Accepted bounds describe the exact sorted batch
    /// accepted by transport or its bounded send queue; non-accepted results carry zero bounds.
    /// </summary>
    public readonly struct FixedInputSendPulseResult
    {
        public FixedInputSendPulseResult(
            FixedInputSendPulseStatus status,
            uint firstAcceptedTargetTick,
            uint highestAcceptedTargetTick,
            int acceptedFrameCount)
        {
            Status = status;
            FirstAcceptedTargetTick = firstAcceptedTargetTick;
            HighestAcceptedTargetTick = highestAcceptedTargetTick;
            AcceptedFrameCount = acceptedFrameCount;
        }

        public FixedInputSendPulseStatus Status { get; }
        public uint FirstAcceptedTargetTick { get; }
        public uint HighestAcceptedTargetTick { get; }
        public int AcceptedFrameCount { get; }
        public bool IsAccepted => Status == FixedInputSendPulseStatus.Accepted;
    }

    /// <summary>
    /// Narrow client port used by <see cref="ReplicatedClientFixedInputClock"/>.
    /// Fixed-input send remains outside <see cref="INetworkRuntimePort.PumpReplicatedClient"/>.
    /// Target-tick SSOT is owned by the fixed-input outbox and applied ACK truth exposed here —
    /// never by replication snapshot <c>LastCommittedTick</c>.
    /// </summary>
    public interface IReplicatedClientFixedInputPort
    {
        ReplicatedClientConnectionState State { get; }

        SessionEpoch SessionEpoch { get; }

        /// <summary>
        /// Latest successfully applied fixed-input ACK <c>CommittedThroughTick</c>.
        /// Zero when no ACK has been applied in the current outbox generation.
        /// </summary>
        uint FixedInputAcknowledgedCommittedTick { get; }

        /// <summary>
        /// Monotonic observation counter that advances on every successfully applied fixed-input ACK.
        /// Used to require a fresh ACK after each Connected edge.
        /// </summary>
        ulong FixedInputAcknowledgementObservationVersion { get; }

        /// <summary>
        /// True when the current outbox generation has successfully enqueued at least one target tick.
        /// </summary>
        bool HasEnqueuedFixedInputTargetTick { get; }

        /// <summary>
        /// Highest target tick successfully owned by the outbox. Undefined when
        /// <see cref="HasEnqueuedFixedInputTargetTick"/> is false.
        /// </summary>
        uint LastEnqueuedFixedInputTargetTick { get; }

        FixedInputOutboxEnqueueStatus TrySubmitFixedInput(uint targetTick, ReadOnlySpan<byte> payload);

        FixedInputSendPulseResult TryPulseFixedInputSend();
    }
}
