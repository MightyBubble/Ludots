using System;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;

namespace Ludots.Core.Networking.FixedInput
{
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

        bool TryPulseFixedInputSend();
    }
}
