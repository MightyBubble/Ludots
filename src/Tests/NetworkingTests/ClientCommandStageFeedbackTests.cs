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
public sealed class ClientCommandStageFeedbackTests
{
    private const int TestOrderTypeId = 1;

    [Test]
    public void ClientRuntime_AutoDrainsAdmissionsIntoStageFeedback()
    {
        using World serverWorld = World.Create();
        using World clientWorld = World.Create();
        Entity player = serverWorld.Create(new PlayerIdentity { PlayerId = 1 });
        Entity actor = serverWorld.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 10));
        var harness = CreateHarness(serverWorld, player, actor);
        harness.Knowledge.Upsert(player, actor, VisibleDisclosure());

        ContentIdentityManifest contentIdentity = ContentIdentityTestFixtures.CreateManifest("stage-feedback-1");
        var capacity = Capacity();
        var transport = new InMemoryTransport(new ConnectionId(21));
        var observer = new RecordingObserver();
        var sessions = new AuthoritativeSessionRegistry(
            seatCapacity: 1,
            new SessionEpoch(41),
            new ProtocolVersion(1, 0),
            contentIdentity,
            reconnectWindowTicks: 2,
            readyCountdownTicks: 1);
        var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 8);
        var serverSeat = CreateServerSeat(serverWorld, harness, player, disclosureLog);
        var server = new AuthoritativeServerNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            sessions,
            harness.Ingress,
            harness.GameplayGate,
            harness.Results,
            harness.EntityResults,
            new FixedControllerResolver(player),
            new FixedReplicationInput(harness.ActorHandle),
            new[] { serverSeat },
            observer);

        var admissions = new NetworkCommandAdmissionResultBuffer(8);
        var feedback = new ClientCommandStageFeedbackBuffer(8);
        var client = new ReplicatedClientNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 0.5f,
            new ProtocolVersion(1, 0),
            contentIdentity,
            new MemoryCredentials(),
            new FixedBridgeFactory(clientWorld),
            admissions,
            feedback,
            observer);

        ConnectThroughSnapshot(client, server, transport);

        var entries = new[]
        {
            new NetworkCommandWireEntry(
                harness.ActorHandle,
                TestOrderTypeId,
                NetworkCommandTargetPayload.FromWorldPositionCm(50, 0, 0)),
        };
        var header = new NetworkCommandBatchHeader(
            client.SessionEpoch.Value,
            clientBatchSequence: 1,
            targetTick: 1,
            acknowledgedCommittedTick: 1,
            entryCount: 1);
        Assert.That(client.TrySubmitCommand(in header, entries), Is.True);

        // Local Sending is recorded by the command port; simulate the same production write here.
        Assert.That(
            feedback.TryWrite(new ClientCommandStageFeedback(
                1,
                ClientCommandStage.Sending,
                OrderSubmitResult.NetworkScheduled,
                OrderAdmissionStage.NetworkIntake)),
            Is.True);
        Assert.That(feedback.TryRead(out ClientCommandStageFeedback sending), Is.True);
        Assert.That(sending.Stage, Is.EqualTo(ClientCommandStage.Sending));

        server.PumpTransport();
        client.PumpTransport();
        Assert.That(admissions.Count, Is.Zero);
        Assert.That(feedback.Count, Is.Zero);

        server.BeforeAuthoritativeTick(1);
        client.PumpTransport();
        Assert.That(feedback.TryRead(out ClientCommandStageFeedback accepted), Is.True);
        Assert.That(accepted.Stage, Is.EqualTo(ClientCommandStage.ServerAccepted));
        Assert.That(accepted.AdmissionStage, Is.EqualTo(OrderAdmissionStage.GlobalIntake));
        Assert.That(admissions.Count, Is.Zero);

        Span<Order> admitted = stackalloc Order[1];
        Assert.That(harness.Orders.TryDequeueBatch(admitted, out int count), Is.True);
        Assert.That(count, Is.EqualTo(1));
        var entityOutcome = new OrderAdmissionOutcome(
            in admitted[0],
            OrderAdmissionStage.EntityIntake,
            OrderSubmitResult.Activated);
        Assert.That(harness.EntityResults.TryWrite(in entityOutcome), Is.True);
        server.PumpTransport();
        client.PumpTransport();
        Assert.That(feedback.TryRead(out ClientCommandStageFeedback activated), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(activated.Stage, Is.EqualTo(ClientCommandStage.Activated));
            Assert.That(activated.AdmissionStage, Is.EqualTo(OrderAdmissionStage.EntityIntake));
            Assert.That(activated.IsTerminal, Is.True);
            Assert.That(admissions.Count, Is.Zero);
            Assert.That(harness.EntityResults.Count, Is.Zero);
            Assert.That(observer.Faults, Is.Zero);
        });
    }

    [Test]
    public void ServerBridge_DrainsEntityResultsAcrossTicksWithoutLeavingBufferFull()
    {
        using World serverWorld = World.Create();
        using World clientWorld = World.Create();
        Entity player = serverWorld.Create(new PlayerIdentity { PlayerId = 1 });
        Entity actor = serverWorld.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 10));
        var harness = CreateHarness(serverWorld, player, actor, entityResultCapacity: 2);
        harness.Knowledge.Upsert(player, actor, VisibleDisclosure());

        ContentIdentityManifest contentIdentity = ContentIdentityTestFixtures.CreateManifest("stage-feedback-2");
        var capacity = Capacity();
        var transport = new InMemoryTransport(new ConnectionId(22));
        var observer = new RecordingObserver();
        var sessions = new AuthoritativeSessionRegistry(
            seatCapacity: 1,
            new SessionEpoch(42),
            new ProtocolVersion(1, 0),
            contentIdentity,
            reconnectWindowTicks: 2,
            readyCountdownTicks: 1);
        var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 4);
        var serverSeat = CreateServerSeat(serverWorld, harness, player, disclosureLog);
        var server = new AuthoritativeServerNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            sessions,
            harness.Ingress,
            harness.GameplayGate,
            harness.Results,
            harness.EntityResults,
            new FixedControllerResolver(player),
            new FixedReplicationInput(harness.ActorHandle),
            new[] { serverSeat },
            observer);

        var admissions = new NetworkCommandAdmissionResultBuffer(8);
        var feedback = new ClientCommandStageFeedbackBuffer(8);
        var client = new ReplicatedClientNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            0.5f,
            new ProtocolVersion(1, 0),
            contentIdentity,
            new MemoryCredentials(),
            new FixedBridgeFactory(clientWorld),
            admissions,
            feedback,
            observer);

        ConnectThroughSnapshot(client, server, transport);
        Span<Order> admitted = stackalloc Order[1];

        for (int round = 0; round < 3; round++)
        {
            var entries = new[]
            {
                new NetworkCommandWireEntry(
                    harness.ActorHandle,
                    TestOrderTypeId,
                    NetworkCommandTargetPayload.FromWorldPositionCm(10 + round, 0, 0)),
            };
            var header = new NetworkCommandBatchHeader(
                client.SessionEpoch.Value,
                clientBatchSequence: (ulong)(round + 1),
                targetTick: 1,
                acknowledgedCommittedTick: 1,
                entryCount: 1);
            Assert.That(client.TrySubmitCommand(in header, entries), Is.True);
            server.PumpTransport();
            client.PumpTransport();
            server.BeforeAuthoritativeTick(1);
            client.PumpTransport();

            Assert.That(harness.Orders.TryDequeueBatch(admitted, out int count), Is.True);
            Assert.That(count, Is.EqualTo(1));
            var entityOutcome = new OrderAdmissionOutcome(
                in admitted[0],
                OrderAdmissionStage.EntityIntake,
                OrderSubmitResult.Activated);
            Assert.That(harness.EntityResults.TryWrite(in entityOutcome), Is.True);
            Assert.That(harness.EntityResults.Count, Is.EqualTo(1));
            server.PumpTransport();
            Assert.That(harness.EntityResults.Count, Is.Zero);
            client.PumpTransport();
            while (feedback.TryRead(out _))
            {
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(harness.EntityResults.Count, Is.Zero);
            Assert.That(harness.EntityResults.AvailableCapacity, Is.EqualTo(harness.EntityResults.Capacity));
            Assert.That(server.IsFaulted, Is.False);
            Assert.That(observer.Faults, Is.Zero);
        });
    }

    [Test]
    public void CommandPort_SubmitRecordsSendingIntoSharedFeedbackBuffer()
    {
        using World serverWorld = World.Create();
        using World clientWorld = World.Create();
        Entity player = serverWorld.Create(new PlayerIdentity { PlayerId = 1 });
        Entity actor = serverWorld.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 10));
        var harness = CreateHarness(serverWorld, player, actor);
        harness.Knowledge.Upsert(player, actor, VisibleDisclosure());

        ContentIdentityManifest contentIdentity = ContentIdentityTestFixtures.CreateManifest("stage-feedback-3");
        var capacity = Capacity();
        var transport = new InMemoryTransport(new ConnectionId(23));
        var observer = new RecordingObserver();
        var sessions = new AuthoritativeSessionRegistry(
            seatCapacity: 1,
            new SessionEpoch(43),
            new ProtocolVersion(1, 0),
            contentIdentity,
            reconnectWindowTicks: 2,
            readyCountdownTicks: 1);
        var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 8);
        var serverSeat = CreateServerSeat(serverWorld, harness, player, disclosureLog);
        var server = new AuthoritativeServerNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            sessions,
            harness.Ingress,
            harness.GameplayGate,
            harness.Results,
            harness.EntityResults,
            new FixedControllerResolver(player),
            new FixedReplicationInput(harness.ActorHandle),
            new[] { serverSeat },
            observer);

        var admissions = new NetworkCommandAdmissionResultBuffer(8);
        var feedback = new ClientCommandStageFeedbackBuffer(8);
        var factory = new FixedBridgeFactory(clientWorld);
        var client = new ReplicatedClientNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            0.5f,
            new ProtocolVersion(1, 0),
            contentIdentity,
            new MemoryCredentials(),
            factory,
            admissions,
            feedback,
            observer);
        ConnectThroughSnapshot(client, server, transport);

        Assert.That(factory.Bridge!.TryResolve(harness.ActorHandle, out Entity mirrored), Is.True);
        Assert.That(clientWorld.TryGet(mirrored, out ReplicationMirrorIdentity identity), Is.True);
        Assert.That(identity.Handle, Is.EqualTo(harness.ActorHandle));
        var port = new ReplicatedClientCommandPort(clientWorld, client, CreateSchemas(), feedback, maxActorsPerBatch: 1);
        var order = new Order
        {
            OrderTypeId = TestOrderTypeId,
            PlayerId = 1,
            Actor = mirrored,
            Target = Entity.Null,
            Args = OrderArgs.CreateSingleWorldCm(new System.Numerics.Vector3(10, 0, 0)),
            SubmitMode = OrderSubmitMode.Immediate,
        };
        Assert.That(port.Submit(in order), Is.EqualTo(ReplicatedClientCommandSubmitResult.Submitted));
        Assert.That(feedback.TryRead(out ClientCommandStageFeedback sending), Is.True);
        Assert.That(sending.Stage, Is.EqualTo(ClientCommandStage.Sending));
        Assert.That(sending.ClientBatchSequence, Is.EqualTo(1UL));
    }

    private static void ConnectThroughSnapshot(
        ReplicatedClientNetworkRuntime client,
        AuthoritativeServerNetworkRuntime server,
        InMemoryTransport transport)
    {
        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        server.PumpTransport();
        client.PumpTransport();
        Assert.That(client.TrySetRoomReady(true), Is.True);
        server.PumpTransport();
        client.PumpTransport();
        server.BeforeAuthoritativeTick(1);
        server.AfterAuthoritativeCommit(1);
        client.PumpTransport();
        server.PumpTransport();
        Assert.That(client.LastCommittedTick, Is.GreaterThan(0u));
    }

    private static AuthoritativeReplicationSeatRuntime CreateServerSeat(
        World world,
        CommandHarness harness,
        Entity player,
        ReplicationDisclosureChangeLog disclosureLog)
    {
        var projectors = new ReplicationSchemaProjectorRegistry(1);
        Assert.That(projectors.Register(1, new TestProjector()), Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
        projectors.Freeze();
        return new AuthoritativeReplicationSeatRuntime(
            0,
            new PlayerId(1),
            new AuthoritativeWorldReplicationBridge(world, harness.Entities, harness.Knowledge, player, projectors, 1),
            new AuthoritativeReplicationChannel(1, 2, disclosureLog),
            disclosureLog,
            new ReplicationProjectionBuffer(1),
            new ReplicationPacketBuffer(1));
    }

    private static NetworkRuntimeCapacity Capacity() => new(
        maxDatagramPayloadBytes: 1024,
        connectionCapacity: 2,
        entityCapacity: 1,
        maxCommandEntries: 1,
        maxCommandPayloadBytes: CommandBatchWireCodec.GetPayloadSize(1),
        maxCommandFragments: 4,
        maxSnapshotBytes: 512,
        maxSnapshotFragments: 8,
        outboundQueueCapacity: 32,
        acknowledgementHistoryCapacity: 4,
        controlChannel: new ChannelId(0),
        commandChannel: new ChannelId(1),
        stateChannel: new ChannelId(2));

    private static NetworkCommandSchemaRegistry CreateSchemas()
    {
        var schemas = new NetworkCommandSchemaRegistry();
        schemas.Register(new NetworkCommandSchema(
            TestOrderTypeId,
            NetworkCommandTargetKind.WorldPositionCm,
            allowArg0: false,
            allowArg1: false,
            OrderSubmitMode.Immediate,
            KnowledgePositionAccess.None));
        schemas.Freeze();
        return schemas;
    }

    private static CommandHarness CreateHarness(
        World world,
        Entity player,
        Entity actor,
        int entityResultCapacity = 8)
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
        ownership.EnsureOwnership(player, actor);
        var control = new ControlDomainQuery(world, relationships, ownership, ownsType, controlsType);
        var entities = new NetworkEntityTable(capacity: 1);
        Assert.That(entities.TryAllocate(actor, out NetworkEntityHandle actorHandle), Is.True);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 4);
        var orderTypes = new OrderTypeRegistry();
        orderTypes.Register(new OrderTypeConfig { Key = "test.move", OrderTypeId = TestOrderTypeId });
        var schemas = CreateSchemas();
        var orders = new OrderQueue(capacity: 8);
        var results = new NetworkCommandAdmissionResultBuffer(capacity: 8);
        var entityResults = new OrderAdmissionResultBuffer(capacity: entityResultCapacity);
        var config = new NetworkCommandIngressConfig(
            seatCapacity: 1,
            simulationTickRateHz: 30,
            maxBatchesPerSecond: 30,
            burstBatchCapacity: 4,
            maxActorsPerBatch: 1,
            sequenceHistoryCapacity: 4,
            maxPastTargetTicks: 2,
            maxFutureTargetTicks: 2,
            scheduledBatchCapacity: 4);
        var gameplayGate = new NetworkGameplayCommandGate();
        gameplayGate.StartMatch();
        var ingress = new NetworkCommandIngress(
            in config,
            world,
            entities,
            control,
            new KnowledgeProjectionResolver(knowledge),
            orderTypes,
            schemas,
            gameplayGate,
            orders,
            results);
        return new CommandHarness(entities, knowledge, orders, results, entityResults, ingress, gameplayGate, actorHandle);
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
        OrderAdmissionResultBuffer EntityResults,
        NetworkCommandIngress Ingress,
        NetworkGameplayCommandGate GameplayGate,
        NetworkEntityHandle ActorHandle);

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

    private sealed class FixedBridgeFactory : IClientReplicationBridgeFactory
    {
        private readonly World _world;
        public FixedBridgeFactory(World world) => _world = world;
        public ClientWorldReplicationBridge? Bridge { get; private set; }

        public ClientWorldReplicationBridge Create(ulong sessionEpoch)
        {
            var appliers = new ClientReplicationSchemaApplierRegistry(1);
            appliers.Register(1, new NoopApplier());
            appliers.Freeze();
            Bridge = new ClientWorldReplicationBridge(_world, 1, sessionEpoch, appliers);
            return Bridge;
        }

        private sealed class NoopApplier : IClientReplicationSchemaApplier
        {
            public bool CanCreate(World world, in ReplicatedEntityState state) => true;
            public bool CanApply(World world, Entity entity, in ReplicatedEntityState state) => true;
            public bool CanConceal(World world, Entity entity) => true;
            public Entity Create(World world, in ReplicationMirrorIdentity identity, in ReplicationMirrorState state) =>
                world.Create(in identity, in state);
            public void Apply(World world, Entity entity, in ReplicatedEntityState state) { }
            public void Conceal(World world, Entity entity) { }
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
        private readonly NetworkEntityHandle _handle;
        public FixedReplicationInput(NetworkEntityHandle handle) => _handle = handle;
        public bool TryCopyActiveHandles(Span<NetworkEntityHandle> destination, out int count)
        {
            count = 1;
            if (destination.Length < 1)
            {
                return false;
            }

            destination[0] = _handle;
            return true;
        }
    }

    private sealed class MemoryCredentials : IClientSessionCredentialPort
    {
        private bool _hasValue;
        private ClientSessionCredentials _value;

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
        public void OnFault(in NetworkRuntimeFault fault) => Faults++;
        public void OnServerSeatConnected(in SessionSeatBinding seat, bool reconnected) { }
        public void OnServerSeatDisconnected(in SessionSeatBinding seat, TransportDisconnectReason reason) { }
        public void OnServerSeatReleased(in SessionSeatBinding seat) { }
        public void OnServerRoomSnapshot(in NetworkRoomSnapshotHeader snapshot, ReadOnlySpan<NetworkRoomSeatSnapshot> seats) { }
        public void OnClientHandshake(in SessionHandshakeResponse response) { }
        public void OnClientAdmission(in NetworkCommandAdmissionOutcome outcome) { }
        public void OnClientResyncRequired(in NetworkResyncRequired message) { }
        public void OnClientRoomSnapshot(in NetworkRoomSnapshotHeader snapshot, ReadOnlySpan<NetworkRoomSeatSnapshot> seats) { }
    }

    private sealed class InMemoryTransport :
        IServerConnectionEventPort,
        IClientConnectionEventPort,
        IServerDatagramPort,
        IClientDatagramPort,
        IServerConnectionControlPort,
        IClientConnectionControlPort
    {
        private readonly ConnectionId _connection;
        private readonly Queue<ServerConnectionEvent> _serverEvents = new();
        private readonly Queue<ClientConnectionEvent> _clientEvents = new();
        private readonly Queue<Frame> _serverInbound = new();
        private readonly Queue<Frame> _clientInbound = new();

        public InMemoryTransport(ConnectionId connection) => _connection = connection;
        public ClientConnectionControlState State { get; private set; }

        public void EnqueueServerFrame(ChannelId channel, NetworkWireKind kind, ReadOnlySpan<byte> payload)
        {
            byte[] framed = new byte[NetworkWireEnvelopeCodec.GetFramedLength(payload.Length)];
            Assert.That(NetworkWireEnvelopeCodec.TryEncode(kind, payload, framed, out _), Is.EqualTo(NetworkWireCodecStatus.Success));
            _clientInbound.Enqueue(new Frame(channel, framed));
        }

        public bool TryConnect()
        {
            if (State != ClientConnectionControlState.Disconnected)
            {
                return false;
            }

            State = ClientConnectionControlState.Connecting;
            State = ClientConnectionControlState.Connected;
            _serverEvents.Enqueue(new ServerConnectionEvent(_connection, TransportConnectionEventKind.Connected));
            _clientEvents.Enqueue(new ClientConnectionEvent(TransportConnectionEventKind.Connected));
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
        void IServerConnectionControlPort.Disconnect(ConnectionId connectionId) => Disconnect();
        public void Pump() { }
        public bool TryReceiveConnectionEvent(out ServerConnectionEvent connectionEvent) => _serverEvents.TryDequeue(out connectionEvent);
        public bool TryReceiveConnectionEvent(out ClientConnectionEvent connectionEvent) => _clientEvents.TryDequeue(out connectionEvent);

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
            _clientInbound.Enqueue(new Frame(channelId, payload.ToArray()));
            return DatagramSendStatus.Sent;
        }

        public DatagramSendStatus TrySend(ChannelId channelId, ReadOnlySpan<byte> payload)
        {
            _serverInbound.Enqueue(new Frame(channelId, payload.ToArray()));
            return DatagramSendStatus.Sent;
        }

        private readonly record struct Frame(ChannelId Channel, byte[] Payload);
    }
}
