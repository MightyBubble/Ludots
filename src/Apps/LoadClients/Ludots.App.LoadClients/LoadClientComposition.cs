using Arch.Core;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Transport;

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
/// Test seam for deterministic host orchestration. Production slots leave <see cref="LoadClientSlot.TestDriver"/> null.
/// Does not replace LiteNetLib production wiring or invent a parallel network runtime.
/// </summary>
public interface ILoadClientSlotTestDriver
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
    /// Test seam for deterministic readiness/tick-rate orchestration. Production leaves this null.
    /// </summary>
    public ILoadClientSlotTestDriver? TestDriver { get; set; }

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
}

public sealed class DeterministicFixedInputPayloadSource : IFixedInputPayloadSource
{
    private readonly int _clientIndex;

    public DeterministicFixedInputPayloadSource(int clientIndex)
    {
        if (clientIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clientIndex));
        }

        _clientIndex = clientIndex;
    }

    public FixedInputPayloadSampleStatus TrySample(uint targetTick, Span<byte> destination)
    {
        if (destination.Length == 0)
        {
            return FixedInputPayloadSampleStatus.Failed;
        }

        destination.Clear();
        // Deterministic synthetic payload: client index + target tick, little-endian where space allows.
        if (destination.Length >= 4)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(destination, _clientIndex);
        }

        if (destination.Length >= 8)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], targetTick);
        }

        return FixedInputPayloadSampleStatus.Sampled;
    }
}

/// <summary>
/// Load-host owned bridge factory. Mirrors host-branch ClientReplicationBridgeFactory composition
/// without pulling authoritative-server composition services into this worktree.
/// </summary>
public sealed class LoadClientReplicationBridgeFactory : IClientReplicationBridgeFactory
{
    private readonly World _world;
    private readonly ClientReplicationSchemaApplierRegistry _appliers;

    public LoadClientReplicationBridgeFactory(
        World world,
        int globalEntityCapacity,
        ClientReplicationSchemaApplierRegistry appliers)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        if (globalEntityCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(globalEntityCapacity));
        }

        _appliers = appliers ?? throw new ArgumentNullException(nameof(appliers));
        if (!appliers.IsFrozen)
        {
            throw new InvalidOperationException(
                "Client replication applier registry must be frozen before load-client bridge composition.");
        }

        GlobalEntityCapacity = globalEntityCapacity;
    }

    public int GlobalEntityCapacity { get; }

    public ClientWorldReplicationBridge Create(ulong sessionEpoch)
    {
        if (sessionEpoch == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionEpoch));
        }

        return new ClientWorldReplicationBridge(
            _world,
            GlobalEntityCapacity,
            sessionEpoch,
            _appliers);
    }
}

/// <summary>
/// Minimal mirror applier so load clients can absorb configured schema ids without owning Physics3D lifecycle.
/// Unregistered schema ids still fail snapshot apply (no silent skip).
/// </summary>
public sealed class LoadClientMirrorSchemaApplier : IClientReplicationSchemaApplier
{
    public bool CanCreate(World world, in ReplicatedEntityState state, in ReplicationApplyContext context) => true;

    public bool CanApply(World world, Entity entity, in ReplicatedEntityState state, in ReplicationApplyContext context) =>
        world.IsAlive(entity);

    public bool CanRelease(
        World world,
        Entity entity,
        ReplicationMirrorLeaveKind leaveKind,
        in ReplicationApplyContext context) => world.IsAlive(entity);

    public Entity Create(
        World world,
        in ReplicationMirrorIdentity identity,
        in ReplicationMirrorState state,
        in ReplicationApplyContext context)
    {
        return world.Create(in identity, in state);
    }

    public void Apply(World world, Entity entity, in ReplicatedEntityState state, in ReplicationApplyContext context)
    {
        var mirror = new ReplicationMirrorState(state.SchemaId, state.Revision, state.Values);
        world.Set(entity, in mirror);
    }

    public void Release(
        World world,
        Entity entity,
        ReplicationMirrorLeaveKind leaveKind,
        in ReplicationApplyContext context)
    {
        world.Destroy(entity);
    }
}
