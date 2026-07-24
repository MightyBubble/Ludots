using Arch.Core;
using System.Numerics;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Transport;
using Ludots.Core.Physics3DNet.Input;

namespace Ludots.App.LoadClients;

public enum LoadClientRunOutcome : byte
{
    Passed = 0,
    Failed = 1,
    Cancelled = 2,
}

public enum LoadClientFaultKind : byte
{
    None = 0,
    Config = 1,
    Construction = 2,
    PartialConnect = 3,
    Rejection = 4,
    UnexpectedDisconnect = 5,
    FixedInputSourceFailed = 6,
    EnqueueRejected = 7,
    PulseFailed = 8,
    CatchUpBacklogExceeded = 9,
    CapacityFailure = 10,
    DisposalFailure = 11,
    RuntimeFault = 12,
    ConnectTimeout = 13,
    ReadyTimeout = 14,
    TickRateContractBroken = 15,
    Cancelled = 16,
}

/// <summary>
/// Narrow factory so tests can inject construction/connect faults without replacing production LiteNetLib wiring.
/// </summary>
public interface ILoadClientSlotFactory
{
    LoadClientSlot Create(int clientIndex, LoadClientHostConfig config, string credentialDirectory);
}

/// <summary>
/// Internal scripted seam for deterministic host-orchestration tests. It bypasses the real runtime and
/// transport pumps, so results obtained through it are not production networking evidence.
/// </summary>
internal interface ILoadClientSlotTestDriver
{
    void Pump(float deltaSeconds);
    ReplicatedClientConnectionState ConnectionState { get; }
    bool IsFaulted { get; }
    NetworkRuntimeFault LastFault { get; }
    bool IsWaitingForAuthoritativeAcknowledgement { get; }
    ulong FixedInputAcknowledgementObservationVersion { get; }
    uint FixedInputAcknowledgedCommittedTick { get; }
    ReplicatedClientFixedInputClockAdvanceResult Advance(float deltaSeconds);
}

public sealed class LoadClientSlot : IDisposable
{
    private bool _disposed;

    public LoadClientSlot(
        int clientIndex,
        int boundPort,
        string credentialPath,
        World world,
        ReplicatedClientNetworkRuntime runtime,
        ReplicatedClientFixedInputClock clock,
        LoadClientNetworkObserver observer)
    {
        ClientIndex = clientIndex;
        BoundPort = boundPort;
        CredentialPath = credentialPath;
        World = world ?? throw new ArgumentNullException(nameof(world));
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Observer = observer ?? throw new ArgumentNullException(nameof(observer));
    }

    public int ClientIndex { get; }
    public int BoundPort { get; }
    public string CredentialPath { get; }
    public World World { get; }
    public ReplicatedClientNetworkRuntime Runtime { get; }
    public ReplicatedClientFixedInputClock Clock { get; }
    public LoadClientNetworkObserver Observer { get; }
    public bool IsReady { get; set; }
    public long FixedInputsGenerated { get; set; }
    public long FixedInputsPulsed { get; set; }
    public uint HighestAcknowledgedCommittedTick { get; set; }
    public bool DisconnectAfterReady { get; set; }
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Test seam for fatal partial-connect injection. Production leaves this null and uses runtime TryConnectNow.
    /// </summary>
    public Func<bool>? ConnectOverride { get; set; }

    /// <summary>
    /// Optional dispose observer for tests verifying exactly-once disposal.
    /// </summary>
    public Action? OnDisposed { get; set; }

    /// <summary>
    /// Internal scripted orchestration seam. Production leaves this null and uses the real runtime and clock.
    /// </summary>
    internal ILoadClientSlotTestDriver? TestDriver { get; set; }

    public bool TryConnect() => ConnectOverride?.Invoke() ?? Runtime.TryConnectNow();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        OnDisposed?.Invoke();
        Runtime.Dispose();
        World.Dispose();
    }
}

public sealed class LoadClientNetworkObserver : INetworkRuntimeObserver
{
    public int FaultCount { get; private set; }
    public NetworkRuntimeFault LastFault { get; private set; }
    public bool HandshakeSeen { get; private set; }
    public bool HandshakeAccepted { get; private set; }
    public HandshakeRejectReason RejectReason { get; private set; }

    public void OnFault(in NetworkRuntimeFault fault)
    {
        FaultCount++;
        LastFault = fault;
    }

    public void OnServerSeatConnected(in SessionSeatBinding seat, bool reconnected)
    {
    }

    public void OnServerSeatDisconnected(in SessionSeatBinding seat, TransportDisconnectReason reason)
    {
    }

    public void OnServerSeatReleased(in SessionSeatBinding seat)
    {
    }

    public void OnClientHandshake(in SessionHandshakeResponse response)
    {
        HandshakeSeen = true;
        HandshakeAccepted = response.Accepted;
        RejectReason = response.RejectReason;
    }

    public void OnClientAdmission(in NetworkCommandAdmissionOutcome outcome)
    {
    }

    public void OnClientResyncRequired(in NetworkResyncRequired message)
    {
    }

    public void OnClientReplicationCommitted(
        in SessionSeatBinding seat,
        in ReplicationPacketHeader header)
    {
    }

    public void OnClientReplicationTornDown(in SessionSeatBinding seat, ulong sessionEpoch)
    {
    }
}

public sealed class Physics3DLoadClientFixedInputPayloadSource : IFixedInputPayloadSource
{
    private readonly Vector2 _movement;

    public Physics3DLoadClientFixedInputPayloadSource(Vector2 movement)
    {
        Span<byte> validation = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        if (!Physics3DFixedInputFrameCodec.TryEncode(movement, validation))
        {
            throw new ArgumentOutOfRangeException(nameof(movement));
        }

        _movement = movement;
    }

    public FixedInputPayloadSampleStatus TrySample(uint targetTick, Span<byte> destination)
    {
        return destination.Length == Physics3DFixedInputFrameCodec.PayloadBytes &&
            Physics3DFixedInputFrameCodec.TryEncode(_movement, destination)
                ? FixedInputPayloadSampleStatus.Sampled
                : FixedInputPayloadSampleStatus.Failed;
    }

    public FixedInputPayloadCommitStatus TryCommit(uint targetTick, ReadOnlySpan<byte> sentPayload)
    {
        Span<byte> expected = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        return sentPayload.Length == Physics3DFixedInputFrameCodec.PayloadBytes &&
            Physics3DFixedInputFrameCodec.TryEncode(_movement, expected) &&
            sentPayload.SequenceEqual(expected)
                ? FixedInputPayloadCommitStatus.Committed
                : FixedInputPayloadCommitStatus.Failed;
    }
}
