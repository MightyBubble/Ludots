using System;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;

namespace Ludots.Core.Networking.FixedInput
{
    /// <summary>
    /// Narrow client port used by <see cref="ReplicatedClientFixedInputClock"/>.
    /// Fixed-input send remains outside <see cref="INetworkRuntimePort.PumpReplicatedClient"/>.
    /// </summary>
    public interface IReplicatedClientFixedInputPort
    {
        ReplicatedClientConnectionState State { get; }

        SessionEpoch SessionEpoch { get; }

        FixedInputOutboxEnqueueStatus TrySubmitFixedInput(uint targetTick, ReadOnlySpan<byte> payload);

        bool TryPulseFixedInputSend();
    }
}
