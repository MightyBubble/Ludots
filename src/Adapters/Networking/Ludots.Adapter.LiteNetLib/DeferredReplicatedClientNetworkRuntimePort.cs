using System;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;

namespace Ludots.Adapter.LiteNetLib;

/// <summary>
/// Deferred replicated-client composite. Transport and fixed input always forward to the same
/// materialized client runtime, while authoritative ports never advertise this client contract.
/// </summary>
internal sealed class DeferredReplicatedClientNetworkRuntimePort :
    DeferredNetworkRuntimePort,
    IReplicatedClientNetworkRuntimePort
{
    private IReplicatedClientNetworkRuntimePort? _clientRuntime;

    public DeferredReplicatedClientNetworkRuntimePort(Func<INetworkRuntimePort> factory)
        : base(NetworkProcessRole.ReplicatedClient, factory)
    {
    }

    public ReplicatedClientConnectionState State => RequireClient().State;

    public SessionEpoch SessionEpoch => RequireClient().SessionEpoch;

    public uint FixedInputAcknowledgedCommittedTick => RequireClient().FixedInputAcknowledgedCommittedTick;

    public ulong FixedInputAcknowledgementObservationVersion =>
        RequireClient().FixedInputAcknowledgementObservationVersion;

    public bool HasEnqueuedFixedInputTargetTick => RequireClient().HasEnqueuedFixedInputTargetTick;

    public uint LastEnqueuedFixedInputTargetTick => RequireClient().LastEnqueuedFixedInputTargetTick;

    public FixedInputOutboxEnqueueStatus TrySubmitFixedInput(uint targetTick, ReadOnlySpan<byte> payload) =>
        RequireClient().TrySubmitFixedInput(targetTick, payload);

    public bool TryPulseFixedInputSend() => RequireClient().TryPulseFixedInputSend();

    protected override void ValidateRuntime(INetworkRuntimePort runtime)
    {
        _clientRuntime = runtime as IReplicatedClientNetworkRuntimePort ??
            throw new InvalidOperationException(
                "Replicated client factory must return IReplicatedClientNetworkRuntimePort.");
    }

    protected override void OnRuntimeCleared() => _clientRuntime = null;

    private IReplicatedClientNetworkRuntimePort RequireClient()
    {
        _ = GetRuntime();
        return _clientRuntime ??
            throw new InvalidOperationException(
                "Replicated client composite runtime was not materialized.");
    }
}
