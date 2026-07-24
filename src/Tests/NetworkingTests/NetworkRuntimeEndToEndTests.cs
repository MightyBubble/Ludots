using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Transport;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class NetworkRuntimeEndToEndTests
{
    private const int TestOrderTypeId = 1;

    [Test]
    public void TwoRuntimePorts_HandshakeFragmentCommandsReplicateReconnectAndReleaseSeat()
    {
        using World serverWorld = World.Create();
        using World clientWorld = World.Create();
        Entity player = serverWorld.Create(new PlayerIdentity { PlayerId = 1 });
        Entity first = serverWorld.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 10));
        Entity second = serverWorld.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 20));

        var commandHarness = CreateCommandHarness(serverWorld, player, first, second);
        var knowledge = commandHarness.Knowledge;
        knowledge.Upsert(player, first, VisibleDisclosure());
        knowledge.Upsert(player, second, VisibleDisclosure());

        var projectorRegistry = new ReplicationSchemaProjectorRegistry(schemaCapacity: 1);
        Assert.That(projectorRegistry.Register(1, new TestProjector()), Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
        projectorRegistry.Freeze();
        var bridge = new AuthoritativeWorldReplicationBridge(
            serverWorld,
            commandHarness.Entities,
            knowledge,
            player,
            projectorRegistry,
            entityCapacity: 2);
        var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 32);
        var serverSeat = new AuthoritativeReplicationSeatRuntime(
            bridge,
            new AuthoritativeReplicationChannel(entityCapacity: 2, baselineCapacity: 4, disclosureLog),
            disclosureLog,
            new ReplicationProjectionBuffer(entityCapacity: 2),
            new ReplicationPacketBuffer(entityCapacity: 2));

        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 1, 2, 3 });
        var protocol = new ProtocolVersion(1, 0);
        var capacity = Capacity(simulationTickRateHz: 30, statePublishRateHz: 10);
        var transport = new InMemoryTransport(new ConnectionId(11));
        var observer = new RecordingObserver();
        var input = new FixedReplicationInput(commandHarness.FirstHandle, commandHarness.SecondHandle);
        var sessions = new AuthoritativeSessionRegistry(
            seatCapacity: 1,
            new SessionEpoch(77),
            protocol,
            fingerprint,
            reconnectWindowTicks: 2);
        var server = new AuthoritativeServerNetworkRuntime(
            in capacity,
            NetworkTransportPortOwnership.Owned,
            transport,
            transport,
            transport,
            sessions,
            commandHarness.Ingress,
            commandHarness.Results,
            new FixedControllerResolver(player),
            input,
            new[] { serverSeat },
            observer);

        var credentials = new MemoryCredentials();
        var clientFactory = new ClientBridgeFactory(clientWorld, entityCapacity: 2);
        var clientAdmissions = new NetworkCommandAdmissionResultBuffer(capacity: 16);
        var client = new ReplicatedClientNetworkRuntime(
            in capacity,
            NetworkTransportPortOwnership.Borrowed,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 0.5f,
            protocol,
            fingerprint,
            credentials,
            clientFactory,
            clientAdmissions,
            observer);

        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        server.PumpTransport();
        client.PumpTransport();

        Assert.Multiple(() =>
        {
            Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));
            Assert.That(client.Seat.PlayerId.Value, Is.EqualTo(1));
            Assert.That(observer.InitialSeatConnections, Is.EqualTo(1));
            Assert.That(observer.Faults, Is.Zero);
        });

        server.BeforeAuthoritativeTick(10);
        server.AfterAuthoritativeCommit(10);
        Assert.That(transport.ServerSnapshotFragmentCount, Is.Zero);
        server.BeforeAuthoritativeTick(11);
        server.AfterAuthoritativeCommit(11);
        Assert.That(transport.ServerSnapshotFragmentCount, Is.Zero);
        server.BeforeAuthoritativeTick(12);
        server.AfterAuthoritativeCommit(12);
        Assert.That(transport.ServerSnapshotFragmentCount, Is.GreaterThan(1));
        client.PumpTransport();
        server.PumpTransport();

        Assert.That(clientFactory.Bridge, Is.Not.Null);
        Assert.That(client.LastCommittedTick, Is.EqualTo(12));
        Assert.That(clientFactory.Bridge!.TryResolve(commandHarness.FirstHandle, out Entity mirroredFirst), Is.True);
        Assert.That(clientWorld.Get<TestAppliedState>(mirroredFirst).Value, Is.EqualTo(10));

        var firstTarget = NetworkCommandTargetPayload.FromWorldPositionCm(100, 0, 0);
        var secondTarget = NetworkCommandTargetPayload.FromWorldPositionCm(200, 0, 0);
        var entries = new[]
        {
            new NetworkCommandWireEntry(commandHarness.FirstHandle, TestOrderTypeId, in firstTarget),
            new NetworkCommandWireEntry(commandHarness.SecondHandle, TestOrderTypeId, in secondTarget),
        };
        var header = new NetworkCommandBatchHeader(
            client.SessionEpoch.Value,
            clientBatchSequence: 1,
            targetTick: 12,
            acknowledgedCommittedTick: 12,
            entryCount: 2);
        Assert.That(client.TrySubmitCommand(in header, entries), Is.True);
        Assert.That(transport.ClientCommandFragmentCount, Is.GreaterThan(1));
        server.PumpTransport();
        client.PumpTransport();
        Assert.That(clientAdmissions.TryRead(out NetworkCommandAdmissionOutcome scheduled), Is.True);
        Assert.That(scheduled.Result, Is.EqualTo(OrderSubmitResult.NetworkScheduled));

        server.BeforeAuthoritativeTick(12);
        client.PumpTransport();
        Assert.That(clientAdmissions.TryRead(out NetworkCommandAdmissionOutcome queued), Is.True);
        Assert.That(queued.Result, Is.EqualTo(OrderSubmitResult.Queued));
        Span<Order> admitted = stackalloc Order[2];
        Assert.That(commandHarness.Orders.TryDequeueBatch(admitted, out int admittedCount), Is.True);
        Assert.That(admittedCount, Is.EqualTo(2));

        serverWorld.Set(first, new TestReplicatedData(2, 99));
        server.BeforeAuthoritativeTick(15);
        server.AfterAuthoritativeCommit(15);
        client.PumpTransport();
        server.PumpTransport();
        Assert.That(clientWorld.Get<TestAppliedState>(mirroredFirst).Value, Is.EqualTo(99));

        transport.Disconnect();
        server.PumpTransport();
        client.PumpTransport();
        Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Disconnected));
        Assert.That(observer.SeatDisconnections, Is.EqualTo(1));

        client.PumpReplicatedClient(0.25f);
        Assert.That(transport.ConnectAttempts, Is.EqualTo(1));
        client.PumpReplicatedClient(0.25f);
        Assert.That(transport.ConnectAttempts, Is.EqualTo(2));
        client.PumpTransport();
        server.PumpTransport();
        client.PumpTransport();
        Assert.Multiple(() =>
        {
            Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));
            Assert.That(observer.SeatReconnections, Is.EqualTo(1));
            Assert.That(client.Seat.Generation, Is.EqualTo(1));
        });

        server.BeforeAuthoritativeTick(18);
        server.AfterAuthoritativeCommit(18);
        client.PumpTransport();
        server.PumpTransport();
        transport.Disconnect();
        server.PumpTransport();
        client.PumpTransport();
        server.BeforeAuthoritativeTick(19);
        Assert.That(observer.SeatReleases, Is.Zero);
        server.BeforeAuthoritativeTick(22);

        Assert.Multiple(() =>
        {
            Assert.That(observer.SeatReleases, Is.EqualTo(1));
            Assert.That(observer.Faults, Is.Zero);
            Assert.That(server.IsFaulted, Is.False);
            Assert.That(client.IsFaulted, Is.False);
        });

        var rejectedClient = new ReplicatedClientNetworkRuntime(
            in capacity,
            NetworkTransportPortOwnership.Borrowed,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 0.5f,
            new ProtocolVersion(2, 0),
            fingerprint,
            new MemoryCredentials(),
            new ClientBridgeFactory(clientWorld, entityCapacity: 2),
            new NetworkCommandAdmissionResultBuffer(capacity: 4),
            observer);
        Assert.That(rejectedClient.TryConnectNow(), Is.True);
        rejectedClient.PumpTransport();
        server.PumpTransport();
        int attemptsAfterRejectedHandshake = transport.ConnectAttempts;
        rejectedClient.PumpTransport();
        rejectedClient.PumpReplicatedClient(1f);

        Assert.Multiple(() =>
        {
            Assert.That(transport.ServerDisconnectAttempts, Is.EqualTo(1));
            Assert.That(rejectedClient.State, Is.EqualTo(ReplicatedClientConnectionState.Rejected));
            Assert.That(transport.ConnectAttempts, Is.EqualTo(attemptsAfterRejectedHandshake));
        });
        client.Dispose();
        rejectedClient.Dispose();
        Assert.That(transport.DisposeCalls, Is.Zero);
        server.Dispose();
        server.Dispose();
        Assert.That(transport.DisposeCalls, Is.EqualTo(1));
    }

    [Test]
    public void ClientRuntime_ReportsWrongChannelAndDoesNotAcceptHandshake()
    {
        var capacity = Capacity();
        var transport = new InMemoryTransport(new ConnectionId(3));
        var observer = new RecordingObserver();
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 9 });
        using World world = World.Create();
        var client = new ReplicatedClientNetworkRuntime(
            in capacity,
            NetworkTransportPortOwnership.Borrowed,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 1f,
            protocol,
            fingerprint,
            new MemoryCredentials(),
            new ClientBridgeFactory(world, 2),
            new NetworkCommandAdmissionResultBuffer(4),
            observer);

        transport.ConnectClientOnly();
        client.PumpTransport();
        var seat = new SessionSeatBinding(0, 1, new PlayerId(1));
        SessionHandshakeResponse response = SessionHandshakeResponse.Accept(
            in seat,
            new ReconnectToken(1, 2),
            protocol,
            fingerprint,
            new SessionEpoch(7));
        Span<byte> payload = stackalloc byte[HandshakeWireCodec.ResponseSizeInBytes];
        Assert.That(HandshakeWireCodec.TryEncodeResponse(in response, payload, out int payloadBytes), Is.EqualTo(NetworkWireCodecStatus.Success));
        transport.EnqueueServerFrame(new ChannelId(2), NetworkWireKind.SessionHandshakeResponse, payload[..payloadBytes]);
        client.PumpTransport();

        Assert.Multiple(() =>
        {
            Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Handshaking));
            Assert.That(observer.Faults, Is.EqualTo(1));
            Assert.That(observer.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.UnexpectedChannel));
        });
    }

    [Test]
    public void ClientRuntime_ClearsStaleReconnectCredentialAndSchedulesFreshJoin()
    {
        var capacity = Capacity();
        var transport = new InMemoryTransport(new ConnectionId(4));
        var observer = new RecordingObserver();
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 7 });
        var credentials = new MemoryCredentials();
        credentials.Seed(new ClientSessionCredentials(new SessionEpoch(5), new ReconnectToken(8, 9)));
        using World world = World.Create();
        var client = new ReplicatedClientNetworkRuntime(
            in capacity,
            NetworkTransportPortOwnership.Borrowed,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 0.5f,
            protocol,
            fingerprint,
            credentials,
            new ClientBridgeFactory(world, 2),
            new NetworkCommandAdmissionResultBuffer(4),
            observer);

        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        SessionHandshakeResponse rejected = SessionHandshakeResponse.Reject(
            HandshakeRejectReason.SessionEpochMismatch,
            protocol,
            fingerprint,
            new SessionEpoch(6));
        Span<byte> payload = stackalloc byte[HandshakeWireCodec.ResponseSizeInBytes];
        Assert.That(HandshakeWireCodec.TryEncodeResponse(in rejected, payload, out int payloadBytes), Is.EqualTo(NetworkWireCodecStatus.Success));
        transport.EnqueueServerFrame(new ChannelId(0), NetworkWireKind.SessionHandshakeResponse, payload[..payloadBytes]);
        transport.Disconnect();
        client.PumpTransport();

        Assert.Multiple(() =>
        {
            Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Disconnected));
            Assert.That(credentials.TryLoad(out _), Is.EqualTo(ClientCredentialLoadStatus.Empty));
            Assert.That(transport.State, Is.EqualTo(ClientConnectionControlState.Disconnected));
            Assert.That(observer.Faults, Is.Zero);
        });

        client.PumpReplicatedClient(0.5f);
        Assert.That(transport.ConnectAttempts, Is.EqualTo(2));
    }

    [Test]
    public void ClientRuntime_Dispose_RespectsDeclaredTransportOwnership()
    {
        var capacity = Capacity();
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 4 });
        using World borrowedWorld = World.Create();
        var borrowedTransport = new InMemoryTransport(new ConnectionId(8));
        var borrowed = new ReplicatedClientNetworkRuntime(
            in capacity,
            NetworkTransportPortOwnership.Borrowed,
            borrowedTransport,
            borrowedTransport,
            borrowedTransport,
            reconnectRetrySeconds: 1f,
            protocol,
            fingerprint,
            new MemoryCredentials(),
            new ClientBridgeFactory(borrowedWorld, 2),
            new NetworkCommandAdmissionResultBuffer(4),
            new RecordingObserver());

        borrowed.Dispose();
        Assert.That(borrowedTransport.DisposeCalls, Is.Zero);

        using World ownedWorld = World.Create();
        var ownedTransport = new InMemoryTransport(new ConnectionId(9));
        var owned = new ReplicatedClientNetworkRuntime(
            in capacity,
            NetworkTransportPortOwnership.Owned,
            ownedTransport,
            ownedTransport,
            ownedTransport,
            reconnectRetrySeconds: 1f,
            protocol,
            fingerprint,
            new MemoryCredentials(),
            new ClientBridgeFactory(ownedWorld, 2),
            new NetworkCommandAdmissionResultBuffer(4),
            new RecordingObserver());

        owned.Dispose();
        owned.Dispose();
        Assert.That(ownedTransport.DisposeCalls, Is.EqualTo(1));
    }

    [Test]
    public void ClientRuntime_Dispose_AttemptsEveryDistinctOwnedPortAndAggregatesFailures()
    {
        var capacity = Capacity();
        var first = new DisposableClientPort(throwOnDispose: true);
        var second = new DisposableClientPort(throwOnDispose: false);
        var third = new DisposableClientPort(throwOnDispose: true);
        using World world = World.Create();
        var runtime = new ReplicatedClientNetworkRuntime(
            in capacity,
            NetworkTransportPortOwnership.Owned,
            first,
            second,
            third,
            reconnectRetrySeconds: 1f,
            new ProtocolVersion(1, 0),
            ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 5 }),
            new MemoryCredentials(),
            new ClientBridgeFactory(world, 2),
            new NetworkCommandAdmissionResultBuffer(4),
            new RecordingObserver());

        AggregateException exception = Assert.Throws<AggregateException>(runtime.Dispose)!;
        Assert.Multiple(() =>
        {
            Assert.That(exception.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(first.DisposeCalls, Is.EqualTo(1));
            Assert.That(second.DisposeCalls, Is.EqualTo(1));
            Assert.That(third.DisposeCalls, Is.EqualTo(1));
        });

        Assert.DoesNotThrow(runtime.Dispose);
        Assert.Multiple(() =>
        {
            Assert.That(first.DisposeCalls, Is.EqualTo(1));
            Assert.That(second.DisposeCalls, Is.EqualTo(1));
            Assert.That(third.DisposeCalls, Is.EqualTo(1));
        });
    }

    private static NetworkRuntimeCapacity Capacity(
        int simulationTickRateHz = 30,
        int statePublishRateHz = 30) => new(
        simulationTickRateHz,
        statePublishRateHz,
        maxDatagramPayloadBytes: 128,
        connectionCapacity: 2,
        entityCapacity: 2,
        maxCommandEntries: 2,
        maxCommandPayloadBytes: CommandBatchWireCodec.GetPayloadSize(2),
        maxCommandFragments: 4,
        maxSnapshotBytes: 256,
        maxSnapshotFragments: 4,
        outboundQueueCapacity: 32,
        acknowledgementHistoryCapacity: 4,
        controlChannel: new ChannelId(0),
        commandChannel: new ChannelId(1),
        stateChannel: new ChannelId(2));

    private static CommandHarness CreateCommandHarness(World world, Entity player, Entity first, Entity second)
    {
        var relationshipTypes = new RelationshipTypeRegistry();
        var relationships = new RelationshipRuntime(
            world,
            relationshipTypes,
            new RelationshipMetricRegistry(),
            new RelationshipFlagRegistry(),
            new RelationshipBandRegistry(),
            new RelationshipChangeBuffer(capacity: 16),
            new RelationshipReverseIndex(world));
        int ownsType = relationshipTypes.Register("Owns");
        int controlsType = relationshipTypes.Register("Controls");
        var ownership = new OwnershipResolver(relationships, ownsType);
        ownership.EnsureOwnership(player, first);
        ownership.EnsureOwnership(player, second);
        var control = new ControlDomainQuery(world, relationships, ownership, ownsType, controlsType);
        var entities = new NetworkEntityTable(capacity: 2);
        Assert.That(entities.TryAllocate(first, out NetworkEntityHandle firstHandle), Is.True);
        Assert.That(entities.TryAllocate(second, out NetworkEntityHandle secondHandle), Is.True);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 4);
        var orderTypes = new OrderTypeRegistry();
        orderTypes.Register(new OrderTypeConfig { Key = "test.move", OrderTypeId = TestOrderTypeId });
        var schemas = new NetworkCommandSchemaRegistry();
        schemas.Register(new NetworkCommandSchema(
            TestOrderTypeId,
            NetworkCommandTargetKind.WorldPositionCm,
            allowArg0: false,
            allowArg1: false,
            OrderSubmitMode.Immediate,
            KnowledgePositionAccess.None));
        schemas.Freeze();
        var orders = new OrderQueue(capacity: 8);
        var results = new NetworkCommandAdmissionResultBuffer(capacity: 8);
        var config = new NetworkCommandIngressConfig(
            seatCapacity: 1,
            simulationTickRateHz: 30,
            maxBatchesPerSecond: 30,
            burstBatchCapacity: 4,
            maxActorsPerBatch: 2,
            sequenceHistoryCapacity: 4,
            maxPastTargetTicks: 2,
            maxFutureTargetTicks: 2,
            scheduledBatchCapacity: 4);
        var ingress = new NetworkCommandIngress(
            in config,
            world,
            entities,
            control,
            new KnowledgeProjectionResolver(knowledge),
            orderTypes,
            schemas,
            orders,
            results);
        return new CommandHarness(entities, knowledge, orders, results, ingress, firstHandle, secondHandle);
    }

    private static KnowledgeDisclosureRecord VisibleDisclosure() => new(
        KnowledgePresence.LiveVisible,
        KnowledgePositionAccess.Live,
        default,
        default,
        default,
        Entity.Null,
        observedTick: 1,
        expiryTick: 0,
        confidencePermille: 1000,
        revision: 1);

    private sealed record CommandHarness(
        NetworkEntityTable Entities,
        KnowledgeProjectionStore Knowledge,
        OrderQueue Orders,
        NetworkCommandAdmissionResultBuffer Results,
        NetworkCommandIngress Ingress,
        NetworkEntityHandle FirstHandle,
        NetworkEntityHandle SecondHandle);

    private readonly struct TestReplicatedData
    {
        public TestReplicatedData(uint revision, long value)
        {
            Revision = revision;
            Value = value;
        }

        public uint Revision { get; }
        public long Value { get; }
    }

    private readonly struct TestAppliedState
    {
        public TestAppliedState(long value) => Value = value;
        public long Value { get; }
    }

    private sealed class TestProjector : IReplicationSchemaProjector
    {
        public bool TryProject(World world, Entity entity, in KnowledgeDisclosureRecord disclosure, out ReplicationProjectedState state)
        {
            if (!world.TryGet(entity, out TestReplicatedData data))
            {
                state = default;
                return false;
            }

            state = new ReplicationProjectedState(data.Revision, new ReplicationStateVector(data.Value, 0, 0, 0));
            return true;
        }
    }

    private sealed class TestApplier : IClientReplicationSchemaApplier
    {
        public bool CanCreate(World world, in ReplicatedEntityState state) => true;
        public bool CanApply(World world, Entity entity, in ReplicatedEntityState state) => world.Has<TestAppliedState>(entity);
        public bool CanConceal(World world, Entity entity) => world.Has<TestAppliedState>(entity);

        public Entity Create(World world, in ReplicationMirrorIdentity identity, in ReplicationMirrorState state)
        {
            var applied = new TestAppliedState(state.Values.Value0);
            return world.Create(in identity, in state, in applied);
        }

        public void Apply(World world, Entity entity, in ReplicatedEntityState state) =>
            world.Set(entity, new TestAppliedState(state.Values.Value0));

        public void Conceal(World world, Entity entity) => world.Set(entity, new TestAppliedState(0));
    }

    private sealed class ClientBridgeFactory : IClientReplicationBridgeFactory
    {
        private readonly World _world;
        private readonly int _entityCapacity;

        public ClientBridgeFactory(World world, int entityCapacity)
        {
            _world = world;
            _entityCapacity = entityCapacity;
        }

        public ClientWorldReplicationBridge? Bridge { get; private set; }

        public ClientWorldReplicationBridge Create(ulong sessionEpoch)
        {
            var appliers = new ClientReplicationSchemaApplierRegistry(schemaCapacity: 1);
            Assert.That(appliers.Register(1, new TestApplier()), Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
            appliers.Freeze();
            Bridge = new ClientWorldReplicationBridge(_world, _entityCapacity, sessionEpoch, appliers);
            return Bridge;
        }
    }

    private sealed class FixedControllerResolver : IAuthoritativeSeatControllerResolver
    {
        private readonly Entity _controller;
        public FixedControllerResolver(Entity controller) => _controller = controller;

        public bool TryResolveController(in SessionSeatBinding seat, out Entity controller)
        {
            controller = _controller;
            return true;
        }
    }

    private sealed class FixedReplicationInput : IAuthoritativeReplicationInputPort
    {
        private readonly NetworkEntityHandle[] _handles;
        public FixedReplicationInput(params NetworkEntityHandle[] handles) => _handles = handles;

        public bool TryCopyActiveHandles(Span<NetworkEntityHandle> destination, out int count)
        {
            count = _handles.Length;
            if (destination.Length < count)
            {
                return false;
            }

            _handles.CopyTo(destination);
            return true;
        }
    }

    private sealed class MemoryCredentials : IClientSessionCredentialPort
    {
        private bool _hasValue;
        private ClientSessionCredentials _value;

        public void Seed(in ClientSessionCredentials credentials)
        {
            _value = credentials;
            _hasValue = true;
        }

        public ClientCredentialLoadStatus TryLoad(out ClientSessionCredentials credentials)
        {
            credentials = _value;
            return _hasValue ? ClientCredentialLoadStatus.Loaded : ClientCredentialLoadStatus.Empty;
        }

        public bool TryStore(in ClientSessionCredentials credentials)
        {
            _value = credentials;
            _hasValue = true;
            return true;
        }

        public bool TryClear()
        {
            _value = default;
            _hasValue = false;
            return true;
        }
    }

    private sealed class RecordingObserver : INetworkRuntimeObserver
    {
        public int Faults { get; private set; }
        public int InitialSeatConnections { get; private set; }
        public int SeatReconnections { get; private set; }
        public int SeatDisconnections { get; private set; }
        public int SeatReleases { get; private set; }
        public NetworkRuntimeFault LastFault { get; private set; }

        public void OnFault(in NetworkRuntimeFault fault)
        {
            Faults++;
            LastFault = fault;
        }

        public void OnServerSeatConnected(in SessionSeatBinding seat, bool reconnected)
        {
            if (reconnected) SeatReconnections++; else InitialSeatConnections++;
        }

        public void OnServerSeatDisconnected(in SessionSeatBinding seat, TransportDisconnectReason reason) => SeatDisconnections++;
        public void OnServerSeatReleased(in SessionSeatBinding seat) => SeatReleases++;
        public void OnClientHandshake(in SessionHandshakeResponse response) { }
        public void OnClientAdmission(in NetworkCommandAdmissionOutcome outcome) { }
        public void OnClientResyncRequired(in NetworkResyncRequired message) { }
    }

    private sealed class InMemoryTransport :
        IServerConnectionEventPort,
        IClientConnectionEventPort,
        IServerDatagramPort,
        IClientDatagramPort,
        IServerConnectionControlPort,
        IClientConnectionControlPort,
        IDisposable
    {
        private readonly ConnectionId _connection;
        private readonly Queue<ServerConnectionEvent> _serverEvents = new();
        private readonly Queue<ClientConnectionEvent> _clientEvents = new();
        private readonly Queue<Frame> _serverInbound = new();
        private readonly Queue<Frame> _clientInbound = new();

        public InMemoryTransport(ConnectionId connection) => _connection = connection;

        public int ServerSnapshotFragmentCount { get; private set; }
        public int ClientCommandFragmentCount { get; private set; }
        public int ConnectAttempts { get; private set; }
        public int ServerDisconnectAttempts { get; private set; }
        public int DisposeCalls { get; private set; }
        public ClientConnectionControlState State { get; private set; }

        public void Connect()
        {
            State = ClientConnectionControlState.Connected;
            _serverEvents.Enqueue(new ServerConnectionEvent(_connection, TransportConnectionEventKind.Connected));
            _clientEvents.Enqueue(new ClientConnectionEvent(TransportConnectionEventKind.Connected));
        }

        public void ConnectClientOnly() =>
            _clientEvents.Enqueue(new ClientConnectionEvent(TransportConnectionEventKind.Connected));

        public bool TryConnect()
        {
            ConnectAttempts++;
            if (State != ClientConnectionControlState.Disconnected)
            {
                return false;
            }

            State = ClientConnectionControlState.Connecting;
            Connect();
            return true;
        }

        public void Disconnect()
        {
            State = ClientConnectionControlState.Disconnected;
            _serverEvents.Enqueue(new ServerConnectionEvent(
                _connection,
                TransportConnectionEventKind.Disconnected,
                TransportDisconnectReason.RemoteClosed));
            _clientEvents.Enqueue(new ClientConnectionEvent(
                TransportConnectionEventKind.Disconnected,
                TransportDisconnectReason.RemoteClosed));
        }

        void IClientConnectionControlPort.Disconnect() => Disconnect();

        void IServerConnectionControlPort.DisconnectAfterReliableFlush(ConnectionId connectionId)
        {
            Assert.That(connectionId, Is.EqualTo(_connection));
            ServerDisconnectAttempts++;
            Disconnect();
        }

        public void Dispose() => DisposeCalls++;

        public void EnqueueServerFrame(ChannelId channel, NetworkWireKind kind, ReadOnlySpan<byte> payload)
        {
            byte[] framed = new byte[NetworkWireEnvelopeCodec.GetFramedLength(payload.Length)];
            Assert.That(NetworkWireEnvelopeCodec.TryEncode(kind, payload, framed, out _), Is.EqualTo(NetworkWireCodecStatus.Success));
            _clientInbound.Enqueue(new Frame(channel, framed));
        }

        public void Pump() { }

        public bool TryReceiveConnectionEvent(out ServerConnectionEvent connectionEvent) =>
            _serverEvents.TryDequeue(out connectionEvent);

        public bool TryReceiveConnectionEvent(out ClientConnectionEvent connectionEvent) =>
            _clientEvents.TryDequeue(out connectionEvent);

        public bool TryReceive(Span<byte> buffer, out int bytesReceived, out ConnectionId connectionId, out ChannelId channelId)
        {
            if (!_serverInbound.TryDequeue(out Frame frame))
            {
                bytesReceived = 0;
                connectionId = default;
                channelId = default;
                return false;
            }

            frame.Payload.CopyTo(buffer);
            bytesReceived = frame.Payload.Length;
            connectionId = _connection;
            channelId = frame.Channel;
            return true;
        }

        public bool TryReceive(Span<byte> buffer, out int bytesReceived, out ChannelId channelId)
        {
            if (!_clientInbound.TryDequeue(out Frame frame))
            {
                bytesReceived = 0;
                channelId = default;
                return false;
            }

            frame.Payload.CopyTo(buffer);
            bytesReceived = frame.Payload.Length;
            channelId = frame.Channel;
            return true;
        }

        public DatagramSendStatus TrySend(ConnectionId connectionId, ChannelId channelId, ReadOnlySpan<byte> payload)
        {
            byte[] copy = payload.ToArray();
            _clientInbound.Enqueue(new Frame(channelId, copy));
            if (TryGetKind(copy, out NetworkWireKind kind) && kind == NetworkWireKind.SnapshotFragment)
            {
                ServerSnapshotFragmentCount++;
            }

            return DatagramSendStatus.Sent;
        }

        public DatagramSendStatus TrySend(ChannelId channelId, ReadOnlySpan<byte> payload)
        {
            byte[] copy = payload.ToArray();
            _serverInbound.Enqueue(new Frame(channelId, copy));
            if (TryGetKind(copy, out NetworkWireKind kind) && kind == NetworkWireKind.CommandFragment)
            {
                ClientCommandFragmentCount++;
            }

            return DatagramSendStatus.Sent;
        }

        private static bool TryGetKind(byte[] payload, out NetworkWireKind kind)
        {
            NetworkWireCodecStatus decoded = NetworkWireEnvelopeCodec.TryDecode(payload, out NetworkWireEnvelope envelope, out _);
            kind = envelope.Kind;
            return decoded == NetworkWireCodecStatus.Success;
        }

        private readonly record struct Frame(ChannelId Channel, byte[] Payload);
    }

    private sealed class DisposableClientPort :
        IClientConnectionEventPort,
        IClientDatagramPort,
        IClientConnectionControlPort,
        IDisposable
    {
        private readonly bool _throwOnDispose;

        public DisposableClientPort(bool throwOnDispose) => _throwOnDispose = throwOnDispose;

        public int DisposeCalls { get; private set; }
        public ClientConnectionControlState State => ClientConnectionControlState.Disconnected;

        public void Pump() { }

        public bool TryReceiveConnectionEvent(out ClientConnectionEvent connectionEvent)
        {
            connectionEvent = default;
            return false;
        }

        public bool TryReceive(Span<byte> buffer, out int bytesReceived, out ChannelId channelId)
        {
            bytesReceived = 0;
            channelId = default;
            return false;
        }

        public DatagramSendStatus TrySend(ChannelId channelId, ReadOnlySpan<byte> payload) =>
            DatagramSendStatus.Closed;

        public bool TryConnect() => false;

        public void Disconnect() => throw new InvalidOperationException("No connection is active.");

        public void Dispose()
        {
            DisposeCalls++;
            if (_throwOnDispose)
            {
                throw new InvalidOperationException("Configured disposal failure.");
            }
        }
    }
}
