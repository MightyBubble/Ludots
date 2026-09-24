using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
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
            seatSlot: 0,
            playerId: new PlayerId(1),
            bridge,
            new AuthoritativeReplicationChannel(entityCapacity: 2, baselineCapacity: 4, disclosureLog),
            disclosureLog,
            new ReplicationProjectionBuffer(entityCapacity: 2),
            new ReplicationPacketBuffer(entityCapacity: 2));

        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 1, 2, 3 });
        var protocol = new ProtocolVersion(1, 0);
        var capacity = Capacity();
        var transport = new InMemoryTransport(new ConnectionId(11));
        var observer = new RecordingObserver();
        var input = new FixedReplicationInput(commandHarness.FirstHandle, commandHarness.SecondHandle);
        var sessions = new AuthoritativeSessionRegistry(
            seatCapacity: 1,
            new SessionEpoch(77),
            protocol,
            fingerprint,
            reconnectWindowTicks: 2,
            readyCountdownTicks: 90);
        var server = new AuthoritativeServerNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            sessions,
            commandHarness.Ingress,
            commandHarness.GameplayGate,
            commandHarness.Results,
            commandHarness.EntityResults,
            new FixedControllerResolver(player),
            input,
            new[] { serverSeat },
            observer);

        var credentials = new MemoryCredentials();
        var clientFactory = new ClientBridgeFactory(clientWorld, entityCapacity: 2);
        var clientAdmissions = new NetworkStagedCommandFeedbackStore(capacity: 16, maxActorsPerCommandBatch: 2);
        var client = new ReplicatedClientNetworkRuntime(
            in capacity,
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
            Assert.That(client.HasRoomSnapshot, Is.True);
            Assert.That(client.LatestRoomSnapshot.Phase, Is.EqualTo(NetworkRoomPhase.WaitingForReady));
            Assert.That(client.LatestRoomSnapshot.ReadySeatCount, Is.Zero);
            Assert.That(observer.InitialSeatConnections, Is.EqualTo(1));
            Assert.That(observer.ServerRoomSnapshots, Is.EqualTo(1));
            Assert.That(observer.ClientRoomSnapshots, Is.EqualTo(1));
            Assert.That(observer.Faults, Is.Zero);
        });

        Assert.That(client.TrySetRoomReady(ready: true), Is.True);
        server.PumpTransport();
        client.PumpTransport();
        Assert.Multiple(() =>
        {
            Assert.That(client.LatestRoomSnapshot.Phase, Is.EqualTo(NetworkRoomPhase.Countdown));
            Assert.That(client.LatestRoomSnapshot.CountdownRemainingTicks, Is.EqualTo(90));
            Assert.That(client.LatestRoomSnapshot.ReadySeatCount, Is.EqualTo(1));
            Assert.That(observer.ClientRoomSnapshots, Is.EqualTo(2));
            Assert.That(observer.ServerRoomSnapshots, Is.EqualTo(2));
        });

        server.BeforeAuthoritativeTick(10);
        server.AfterAuthoritativeCommit(10);
        Assert.That(transport.ServerSnapshotFragmentCount, Is.GreaterThan(1));
        client.PumpTransport();
        server.PumpTransport();

        Assert.That(clientFactory.Bridge, Is.Not.Null);
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
            targetTick: 10,
            acknowledgedCommittedTick: 10,
            entryCount: 2);
        Assert.That(client.TrySubmitCommand(in header, entries), Is.True);
        Assert.That(transport.ClientCommandFragmentCount, Is.GreaterThan(1));
        server.PumpTransport();
        client.PumpTransport();
        Assert.That(clientAdmissions.TryGet(1, out NetworkStagedCommandFeedback scheduled), Is.True);
        Assert.That(scheduled.Result, Is.EqualTo(OrderSubmitResult.NetworkScheduled));

        server.BeforeAuthoritativeTick(10);
        client.PumpTransport();
        Assert.That(clientAdmissions.TryGet(1, out NetworkStagedCommandFeedback queued), Is.True);
        Assert.That(queued.Result, Is.EqualTo(OrderSubmitResult.Queued));
        Span<Order> admitted = stackalloc Order[2];
        Assert.That(commandHarness.Orders.TryDequeueBatch(admitted, out int admittedCount), Is.True);
        Assert.That(admittedCount, Is.EqualTo(2));

        commandHarness.GameplayGate.CompleteMatch();
        var completedHeader = new NetworkCommandBatchHeader(
            client.SessionEpoch.Value,
            clientBatchSequence: 2,
            targetTick: 10,
            acknowledgedCommittedTick: 10,
            entryCount: 2);
        Assert.That(client.TrySubmitCommand(in completedHeader, entries), Is.True);
        server.PumpTransport();
        client.PumpTransport();
        Assert.That(clientAdmissions.TryGet(2, out NetworkStagedCommandFeedback completed), Is.True);
        Assert.That(completed.Result, Is.EqualTo(OrderSubmitResult.NetworkMatchCompleted));

        serverWorld.Set(first, new TestReplicatedData(2, 99));
        server.BeforeAuthoritativeTick(11);
        server.AfterAuthoritativeCommit(11);
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

        server.BeforeAuthoritativeTick(12);
        server.AfterAuthoritativeCommit(12);
        client.PumpTransport();
        server.PumpTransport();
        transport.Disconnect();
        server.PumpTransport();
        client.PumpTransport();
        server.BeforeAuthoritativeTick(13);
        Assert.That(observer.SeatReleases, Is.Zero);
        server.BeforeAuthoritativeTick(16);

        Assert.Multiple(() =>
        {
            Assert.That(observer.SeatReleases, Is.EqualTo(1));
            Assert.That(observer.Faults, Is.Zero);
            Assert.That(server.IsFaulted, Is.False);
            Assert.That(client.IsFaulted, Is.False);
        });
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
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 1f,
            protocol,
            fingerprint,
            new MemoryCredentials(),
            new ClientBridgeFactory(world, 2),
            new NetworkStagedCommandFeedbackStore(4, 2),
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
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 0.5f,
            protocol,
            fingerprint,
            credentials,
            new ClientBridgeFactory(world, 2),
            new NetworkStagedCommandFeedbackStore(4, 2),
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
    public void ClientRuntime_ServerEpochRestart_ClearsOldMirrorAndCompletesFreshJoin()
    {
        var capacity = Capacity();
        var transport = new InMemoryTransport(new ConnectionId(5));
        var observer = new RecordingObserver();
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 6 });
        var credentials = new MemoryCredentials();
        using World world = World.Create();
        var factory = new ClientBridgeFactory(world, 2);
        using var client = new ReplicatedClientNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 0.5f,
            protocol,
            fingerprint,
            credentials,
            factory,
            new NetworkStagedCommandFeedbackStore(4, 2),
            observer);

        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        EnqueueHandshakeResponse(
            transport,
            SessionHandshakeResponse.Accept(
                new SessionSeatBinding(0, 1, new PlayerId(1)),
                new ReconnectToken(10, 11),
                protocol,
                fingerprint,
                new SessionEpoch(5)));
        client.PumpTransport();

        ClientWorldReplicationBridge oldBridge = factory.Bridge!;
        Entity authored = world.Create(new TestAppliedState(42));
        Assert.That(
            oldBridge.BindExisting(new NetworkEntityHandle(0, 1), authored),
            Is.EqualTo(ReplicationBridgeResult.Success));

        transport.Disconnect();
        client.PumpTransport();
        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        EnqueueHandshakeResponse(
            transport,
            SessionHandshakeResponse.Reject(
                HandshakeRejectReason.SessionEpochMismatch,
                protocol,
                fingerprint,
                new SessionEpoch(6)));
        client.PumpTransport();

        Assert.Multiple(() =>
        {
            Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Disconnected));
            Assert.That(world.Has<ReplicationMirrorIdentity>(authored), Is.False);
            Assert.That(world.Has<ReplicationMirrorState>(authored), Is.False);
            Assert.That(credentials.TryLoad(out _), Is.EqualTo(ClientCredentialLoadStatus.Empty));
        });

        client.PumpReplicatedClient(0.5f);
        client.PumpTransport();
        EnqueueHandshakeResponse(
            transport,
            SessionHandshakeResponse.Accept(
                new SessionSeatBinding(0, 1, new PlayerId(1)),
                new ReconnectToken(20, 21),
                protocol,
                fingerprint,
                new SessionEpoch(6)));
        client.PumpTransport();

        Assert.Multiple(() =>
        {
            Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));
            Assert.That(client.SessionEpoch, Is.EqualTo(new SessionEpoch(6)));
            Assert.That(factory.Bridge, Is.Not.SameAs(oldBridge));
            Assert.That(observer.Faults, Is.Zero);
        });
    }

    [Test]
    public void ClientRuntime_ProtocolMismatch_IsReportedAsRejectionAndDisconnects()
    {
        var capacity = Capacity();
        var transport = new InMemoryTransport(new ConnectionId(6));
        var observer = new RecordingObserver();
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 3 });
        using World world = World.Create();
        using var client = new ReplicatedClientNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 1f,
            protocol,
            fingerprint,
            new MemoryCredentials(),
            new ClientBridgeFactory(world, 2),
            new NetworkStagedCommandFeedbackStore(4, 2),
            observer);

        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        EnqueueHandshakeResponse(
            transport,
            SessionHandshakeResponse.Reject(
                HandshakeRejectReason.ProtocolMismatch,
                new ProtocolVersion(2, 0),
                fingerprint,
                new SessionEpoch(9)));
        client.PumpTransport();

        Assert.Multiple(() =>
        {
            Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Rejected));
            Assert.That(transport.State, Is.EqualTo(ClientConnectionControlState.Disconnected));
            Assert.That(observer.Faults, Is.Zero);
            Assert.That(observer.ClientHandshakes, Is.EqualTo(1));
            Assert.That(observer.LastHandshakeRejectReason, Is.EqualTo(HandshakeRejectReason.ProtocolMismatch));
        });
    }

    [Test]
    public void AuthoritativeReplicationSeatRuntime_RejectsMismatchedDisclosureLog()
    {
        using World world = World.Create();
        var entities = new NetworkEntityTable(1);
        var knowledge = new KnowledgeProjectionStore(1);
        var projectors = new ReplicationSchemaProjectorRegistry(1);
        projectors.Freeze();
        Entity viewer = world.Create();
        var bridge = new AuthoritativeWorldReplicationBridge(world, entities, knowledge, viewer, projectors, 1);
        var channelLog = new ReplicationDisclosureChangeLog(2);
        var wrongLog = new ReplicationDisclosureChangeLog(2);
        var channel = new AuthoritativeReplicationChannel(1, 1, channelLog);

        Assert.That(
            () => new AuthoritativeReplicationSeatRuntime(
                0,
                new PlayerId(1),
                bridge,
                channel,
                wrongLog,
                new ReplicationProjectionBuffer(1),
                new ReplicationPacketBuffer(1)),
            Throws.ArgumentException.With.Message.Contains("share one disclosure log"));
    }

    [Test]
    public void ClientRuntime_RejectsUnauthenticatedAndMalformedRoomSnapshots()
    {
        var capacity = Capacity();
        var transport = new InMemoryTransport(new ConnectionId(6));
        var observer = new RecordingObserver();
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 4 });
        using World world = World.Create();
        using var client = new ReplicatedClientNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 1f,
            protocol,
            fingerprint,
            new MemoryCredentials(),
            new ClientBridgeFactory(world, 2),
            new NetworkStagedCommandFeedbackStore(4, 2),
            observer);
        var roomSeats = new[]
        {
            new NetworkRoomSeatSnapshot(
                0,
                NetworkRoomSeatConnectionState.Connected,
                NetworkRoomReadyState.Unready,
                generation: 1,
                new PlayerId(1)),
        };
        var roomHeader = new NetworkRoomSnapshotHeader(
            new SessionEpoch(7),
            revision: 1,
            committedTick: 0,
            countdownRemainingTicks: 0,
            seatCount: 1,
            connectedSeatCount: 1,
            readySeatCount: 0,
            NetworkRoomPhase.WaitingForReady);
        byte[] roomPayload = new byte[RoomControlWireCodec.GetSnapshotPayloadSize(1)];
        Assert.That(
            RoomControlWireCodec.TryEncodeSnapshot(in roomHeader, roomSeats, roomPayload, out _),
            Is.EqualTo(NetworkWireCodecStatus.Success));

        Assert.That(client.TrySetRoomReady(ready: true), Is.False);
        transport.EnqueueServerFrame(new ChannelId(0), NetworkWireKind.RoomSnapshot, roomPayload);
        client.PumpTransport();
        Assert.Multiple(() =>
        {
            Assert.That(observer.Faults, Is.EqualTo(1));
            Assert.That(observer.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.UnauthenticatedMessage));
            Assert.That(client.HasRoomSnapshot, Is.False);
        });

        transport.ConnectClientOnly();
        client.PumpTransport();
        EnqueueHandshakeResponse(
            transport,
            SessionHandshakeResponse.Accept(
                new SessionSeatBinding(0, 1, new PlayerId(1)),
                new ReconnectToken(1, 2),
                protocol,
                fingerprint,
                new SessionEpoch(7)));
        client.PumpTransport();
        roomPayload[RoomControlWireCodec.SnapshotHeaderSizeInBytes - 1] = 1;
        transport.EnqueueServerFrame(new ChannelId(0), NetworkWireKind.RoomSnapshot, roomPayload);
        client.PumpTransport();

        Assert.Multiple(() =>
        {
            Assert.That(observer.Faults, Is.EqualTo(2));
            Assert.That(observer.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.MalformedDatagram));
            Assert.That(observer.LastFault.CodecStatus, Is.EqualTo(NetworkWireCodecStatus.InvalidInput));
            Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));
            Assert.That(client.HasRoomSnapshot, Is.False);
        });
    }

    [Test]
    public void AuthoritativeRuntime_HoldsOneUnackedSnapshot_AvoidsBaselineMismatchResyncLoop()
    {
        using World serverWorld = World.Create();
        using World clientWorld = World.Create();
        Entity player = serverWorld.Create(new PlayerIdentity { PlayerId = 1 });
        Entity first = serverWorld.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 10));
        Entity second = serverWorld.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 20));
        var commandHarness = CreateCommandHarness(serverWorld, player, first, second);
        commandHarness.Knowledge.Upsert(player, first, VisibleDisclosure());
        commandHarness.Knowledge.Upsert(player, second, VisibleDisclosure());

        var projectorRegistry = new ReplicationSchemaProjectorRegistry(schemaCapacity: 1);
        Assert.That(projectorRegistry.Register(1, new TestProjector()), Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
        projectorRegistry.Freeze();
        var bridge = new AuthoritativeWorldReplicationBridge(
            serverWorld,
            commandHarness.Entities,
            commandHarness.Knowledge,
            player,
            projectorRegistry,
            entityCapacity: 2);
        var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 32);
        var serverSeat = new AuthoritativeReplicationSeatRuntime(
            seatSlot: 0,
            playerId: new PlayerId(1),
            bridge,
            new AuthoritativeReplicationChannel(entityCapacity: 2, baselineCapacity: 4, disclosureLog),
            disclosureLog,
            new ReplicationProjectionBuffer(entityCapacity: 2),
            new ReplicationPacketBuffer(entityCapacity: 2));

        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 42 });
        var protocol = new ProtocolVersion(1, 0);
        var capacity = new NetworkRuntimeCapacity(
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
            stateChannel: new ChannelId(2),
            statePublishIntervalTicks: 1);
        var transport = new InMemoryTransport(new ConnectionId(21));
        var observer = new RecordingObserver();
        var input = new FixedReplicationInput(commandHarness.FirstHandle, commandHarness.SecondHandle);
        var sessions = new AuthoritativeSessionRegistry(
            seatCapacity: 1,
            new SessionEpoch(88),
            protocol,
            fingerprint,
            reconnectWindowTicks: 8,
            readyCountdownTicks: 90);
        var server = new AuthoritativeServerNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            sessions,
            commandHarness.Ingress,
            commandHarness.GameplayGate,
            commandHarness.Results,
            commandHarness.EntityResults,
            new FixedControllerResolver(player),
            input,
            new[] { serverSeat },
            observer);
        var clientFactory = new ClientBridgeFactory(clientWorld, entityCapacity: 2);
        var clientAdmissions = new NetworkStagedCommandFeedbackStore(capacity: 16, maxActorsPerCommandBatch: 2);
        var client = new ReplicatedClientNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 0.5f,
            protocol,
            fingerprint,
            new MemoryCredentials(),
            clientFactory,
            clientAdmissions,
            observer);

        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        server.PumpTransport();
        client.PumpTransport();
        Assert.That(client.TrySetRoomReady(ready: true), Is.True);
        server.PumpTransport();
        client.PumpTransport();

        server.BeforeAuthoritativeTick(10);
        server.AfterAuthoritativeCommit(10);
        client.PumpTransport();
        server.PumpTransport();
        Assert.That(clientFactory.Bridge, Is.Not.Null);

        int packetsAfterFull = transport.ServerReplicationPacketCount;
        serverWorld.Set(first, new TestReplicatedData(2, 11));
        server.BeforeAuthoritativeTick(11);
        server.AfterAuthoritativeCommit(11);
        Assert.That(transport.ServerReplicationPacketCount, Is.EqualTo(packetsAfterFull + 1));

        serverWorld.Set(first, new TestReplicatedData(3, 12));
        server.BeforeAuthoritativeTick(12);
        server.AfterAuthoritativeCommit(12);
        Assert.That(
            transport.ServerReplicationPacketCount,
            Is.EqualTo(packetsAfterFull + 1),
            "Second publish must wait for acknowledgement of the in-flight snapshot.");

        client.PumpTransport();
        server.PumpTransport();
        Assert.That(clientWorld.Get<TestAppliedState>(
            clientFactory.Bridge!.TryResolve(commandHarness.FirstHandle, out Entity mirrored) ? mirrored : default).Value,
            Is.EqualTo(11));

        serverWorld.Set(first, new TestReplicatedData(4, 13));
        server.BeforeAuthoritativeTick(13);
        server.AfterAuthoritativeCommit(13);
        client.PumpTransport();
        server.PumpTransport();

        Assert.Multiple(() =>
        {
            Assert.That(observer.ClientResyncRequiredCount, Is.Zero);
            Assert.That(observer.Faults, Is.Zero);
            Assert.That(server.IsFaulted, Is.False);
            Assert.That(client.IsFaulted, Is.False);
            Assert.That(clientWorld.Get<TestAppliedState>(
                clientFactory.Bridge!.TryResolve(commandHarness.FirstHandle, out Entity latest) ? latest : default).Value,
                Is.EqualTo(13));
        });
    }

    [Test]
    public void AuthoritativeRuntime_WiresNetworkGlobalAndEntityAdmissionStages_AndSurvivesBeyondEntityCapacity()
    {
        using World serverWorld = World.Create();
        using World clientWorld = World.Create();
        Entity player = serverWorld.Create(new PlayerIdentity { PlayerId = 1 });
        Entity first = serverWorld.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 10), OrderBuffer.CreateEmpty());
        Entity second = serverWorld.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 20), OrderBuffer.CreateEmpty());
        var commandHarness = CreateCommandHarness(serverWorld, player, first, second);
        commandHarness.Knowledge.Upsert(player, first, VisibleDisclosure());
        commandHarness.Knowledge.Upsert(player, second, VisibleDisclosure());

        var orderTypes = new OrderTypeRegistry();
        orderTypes.Register(new OrderTypeConfig
        {
            Key = "test.move",
            OrderTypeId = TestOrderTypeId,
            Priority = 100,
            CanInterruptSelf = true,
        });
        var orderBuffer = new OrderBufferSystem(
            serverWorld,
            new DiscreteClock(),
            orderTypes,
            new OrderRuleRegistry(),
            commandHarness.Orders,
            admissionResults: commandHarness.EntityResults);

        var projectorRegistry = new ReplicationSchemaProjectorRegistry(schemaCapacity: 1);
        Assert.That(projectorRegistry.Register(1, new TestProjector()), Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
        projectorRegistry.Freeze();
        var bridge = new AuthoritativeWorldReplicationBridge(
            serverWorld,
            commandHarness.Entities,
            commandHarness.Knowledge,
            player,
            projectorRegistry,
            entityCapacity: 2);
        var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 32);
        var serverSeat = new AuthoritativeReplicationSeatRuntime(
            seatSlot: 0,
            playerId: new PlayerId(1),
            bridge,
            new AuthoritativeReplicationChannel(entityCapacity: 2, baselineCapacity: 4, disclosureLog),
            disclosureLog,
            new ReplicationProjectionBuffer(entityCapacity: 2),
            new ReplicationPacketBuffer(entityCapacity: 2));

        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 77 });
        var protocol = new ProtocolVersion(1, 0);
        var capacity = Capacity();
        var transport = new InMemoryTransport(new ConnectionId(31));
        var observer = new RecordingObserver();
        var input = new FixedReplicationInput(commandHarness.FirstHandle, commandHarness.SecondHandle);
        var sessions = new AuthoritativeSessionRegistry(
            seatCapacity: 1,
            new SessionEpoch(91),
            protocol,
            fingerprint,
            reconnectWindowTicks: 8,
            readyCountdownTicks: 90);
        var server = new AuthoritativeServerNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            sessions,
            commandHarness.Ingress,
            commandHarness.GameplayGate,
            commandHarness.Results,
            commandHarness.EntityResults,
            new FixedControllerResolver(player),
            input,
            new[] { serverSeat },
            observer);
        var clientAdmissions = new NetworkStagedCommandFeedbackStore(capacity: 4, maxActorsPerCommandBatch: 2);
        var client = new ReplicatedClientNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 0.5f,
            protocol,
            fingerprint,
            new MemoryCredentials(),
            new ClientBridgeFactory(clientWorld, entityCapacity: 2),
            clientAdmissions,
            observer);

        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        server.PumpTransport();
        client.PumpTransport();
        Assert.That(client.TrySetRoomReady(ready: true), Is.True);
        server.PumpTransport();
        client.PumpTransport();
        server.BeforeAuthoritativeTick(10);
        server.AfterAuthoritativeCommit(10);
        client.PumpTransport();
        server.PumpTransport();

        var stages = new List<OrderAdmissionStage>();
        for (ulong sequence = 1; sequence <= 12; sequence++)
        {
            var entries = new[]
            {
                new NetworkCommandWireEntry(
                    commandHarness.FirstHandle,
                    TestOrderTypeId,
                    NetworkCommandTargetPayload.FromWorldPositionCm(100 + (int)sequence, 0, 0)),
            };
            var header = new NetworkCommandBatchHeader(
                client.SessionEpoch.Value,
                sequence,
                targetTick: 10 + (int)sequence,
                acknowledgedCommittedTick: 10,
                entryCount: 1);
            Assert.That(client.TrySubmitCommand(in header, entries), Is.True);
            server.PumpTransport();
            client.PumpTransport();
            Assert.That(clientAdmissions.TryGet(sequence, out NetworkStagedCommandFeedback network), Is.True);
            Assert.That(network.Stage, Is.EqualTo(OrderAdmissionStage.NetworkIntake));
            Assert.That(network.Result, Is.EqualTo(OrderSubmitResult.NetworkScheduled));
            stages.Add(network.Stage);

            uint tick = 10 + (uint)sequence;
            server.BeforeAuthoritativeTick(tick);
            client.PumpTransport();
            Assert.That(clientAdmissions.TryGet(sequence, out NetworkStagedCommandFeedback global), Is.True);
            Assert.That(global.Stage, Is.EqualTo(OrderAdmissionStage.GlobalIntake));
            Assert.That(global.Result, Is.EqualTo(OrderSubmitResult.Queued));
            stages.Add(global.Stage);

            orderBuffer.Update(0f);
            server.AfterAuthoritativeCommit(tick);
            server.PumpTransport();
            client.PumpTransport();
            Assert.That(clientAdmissions.TryGet(sequence, out NetworkStagedCommandFeedback entity), Is.True);
            Assert.That(entity.Stage, Is.EqualTo(OrderAdmissionStage.EntityIntake));
            Assert.That(entity.Result, Is.EqualTo(OrderSubmitResult.Activated));
            Assert.That(entity.LatestOutcome.AdmissionBatchId, Is.EqualTo(global.LatestOutcome.AdmissionBatchId));
            Assert.That(entity.IsTerminal, Is.True);
            stages.Add(entity.Stage);
        }

        Assert.Multiple(() =>
        {
            Assert.That(stages, Does.Contain(OrderAdmissionStage.NetworkIntake));
            Assert.That(stages, Does.Contain(OrderAdmissionStage.GlobalIntake));
            Assert.That(stages, Does.Contain(OrderAdmissionStage.EntityIntake));
            Assert.That(commandHarness.EntityResults.Count, Is.EqualTo(0));
            Assert.That(observer.Faults, Is.Zero);
            Assert.That(server.IsFaulted, Is.False);
            Assert.That(client.IsFaulted, Is.False);
        });
    }

    [Test]
    public void ClientFeedbackStore_LongRunBeyondConfiguredCapacity_WithoutExternalDrain()
    {
        var capacity = Capacity();
        var transport = new InMemoryTransport(new ConnectionId(41));
        var observer = new RecordingObserver();
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 5 });
        using World world = World.Create();
        var feedback = new NetworkStagedCommandFeedbackStore(capacity: 2, maxActorsPerCommandBatch: 1);
        var client = new ReplicatedClientNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 1f,
            protocol,
            fingerprint,
            new MemoryCredentials(),
            new ClientBridgeFactory(world, 2),
            feedback,
            observer);

        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        EnqueueHandshakeResponse(
            transport,
            SessionHandshakeResponse.Accept(
                new SessionSeatBinding(0, 1, new PlayerId(1)),
                new ReconnectToken(3, 4),
                protocol,
                fingerprint,
                new SessionEpoch(9)));
        client.PumpTransport();
        Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));

        var seat = new NetworkCommandSeat(0, 1, 1);
        Span<byte> payload = stackalloc byte[CommandAdmissionWireCodec.SizeInBytes];
        for (int i = 0; i < 16; i++)
        {
            var network = new NetworkCommandAdmissionOutcome(
                in seat,
                clientBatchSequence: (ulong)(i + 1),
                targetTick: i,
                actorCount: 1,
                orderId: i + 1,
                admissionBatchId: i + 1,
                OrderSubmitResult.NetworkScheduled,
                isReplay: false);
            Assert.That(
                CommandAdmissionWireCodec.TryEncode(client.SessionEpoch.Value, in network, payload, out int written),
                Is.EqualTo(NetworkWireCodecStatus.Success));
            transport.EnqueueServerFrame(new ChannelId(1), NetworkWireKind.CommandAdmissionResult, payload[..written]);
            client.PumpTransport();

            var global = new NetworkCommandAdmissionOutcome(
                in seat,
                clientBatchSequence: (ulong)(i + 1),
                targetTick: i,
                actorCount: 1,
                orderId: i + 1,
                admissionBatchId: i + 1,
                admissionBatchIndex: 0,
                OrderAdmissionStage.GlobalIntake,
                OrderSubmitResult.Queued,
                isReplay: false);
            Assert.That(
                CommandAdmissionWireCodec.TryEncode(client.SessionEpoch.Value, in global, payload, out written),
                Is.EqualTo(NetworkWireCodecStatus.Success));
            transport.EnqueueServerFrame(new ChannelId(1), NetworkWireKind.CommandAdmissionResult, payload[..written]);
            client.PumpTransport();

            var entity = new NetworkCommandAdmissionOutcome(
                in seat,
                clientBatchSequence: (ulong)(i + 1),
                targetTick: i,
                actorCount: 1,
                orderId: i + 1,
                admissionBatchId: i + 1,
                admissionBatchIndex: 0,
                OrderAdmissionStage.EntityIntake,
                OrderSubmitResult.Activated,
                isReplay: false);
            Assert.That(
                CommandAdmissionWireCodec.TryEncode(client.SessionEpoch.Value, in entity, payload, out written),
                Is.EqualTo(NetworkWireCodecStatus.Success));
            transport.EnqueueServerFrame(new ChannelId(1), NetworkWireKind.CommandAdmissionResult, payload[..written]);
            client.PumpTransport();

            Assert.That(feedback.TryGet((ulong)(i + 1), out NetworkStagedCommandFeedback decoded), Is.True);
            Assert.That(decoded.IsTerminal, Is.True);
            Assert.That(decoded.ActivatedCount, Is.EqualTo(1));
        }

        Assert.Multiple(() =>
        {
            Assert.That(client.IsFaulted, Is.False);
            Assert.That(observer.Faults, Is.Zero);
            Assert.That(feedback.Count, Is.EqualTo(2));
            Assert.That(feedback.RetiredCompletedCount, Is.EqualTo(14));
        });
    }

    private static void EnqueueHandshakeResponse(
        InMemoryTransport transport,
        in SessionHandshakeResponse response)
    {
        Span<byte> payload = stackalloc byte[HandshakeWireCodec.ResponseSizeInBytes];
        Assert.That(
            HandshakeWireCodec.TryEncodeResponse(in response, payload, out int payloadBytes),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        transport.EnqueueServerFrame(
            new ChannelId(0),
            NetworkWireKind.SessionHandshakeResponse,
            payload[..payloadBytes]);
    }

    private static NetworkRuntimeCapacity Capacity() => new(
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
        var entityResults = new OrderAdmissionResultBuffer(capacity: 8);
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
        return new CommandHarness(entities, knowledge, orders, results, entityResults, ingress, gameplayGate, firstHandle, secondHandle);
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
        public int ClientHandshakes { get; private set; }
        public int ClientResyncRequiredCount { get; private set; }
        public HandshakeRejectReason LastHandshakeRejectReason { get; private set; }
        public int ClientRoomSnapshots { get; private set; }
        public int ServerRoomSnapshots { get; private set; }
        public NetworkRoomSnapshotHeader LastRoomSnapshot { get; private set; }

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
        public void OnServerRoomSnapshot(
            in NetworkRoomSnapshotHeader snapshot,
            ReadOnlySpan<NetworkRoomSeatSnapshot> seats)
        {
            ServerRoomSnapshots++;
            LastRoomSnapshot = snapshot;
        }
        public void OnClientHandshake(in SessionHandshakeResponse response)
        {
            ClientHandshakes++;
            LastHandshakeRejectReason = response.RejectReason;
        }
        public void OnClientAdmission(in NetworkCommandAdmissionOutcome outcome) { }
        public void OnClientResyncRequired(in NetworkResyncRequired message) => ClientResyncRequiredCount++;
        public void OnClientRoomSnapshot(
            in NetworkRoomSnapshotHeader snapshot,
            ReadOnlySpan<NetworkRoomSeatSnapshot> seats)
        {
            ClientRoomSnapshots++;
            LastRoomSnapshot = snapshot;
        }
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

        public int ServerSnapshotFragmentCount { get; private set; }
        public int ServerReplicationPacketCount { get; private set; }
        public int ClientCommandFragmentCount { get; private set; }
        public int ConnectAttempts { get; private set; }
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

        void IServerConnectionControlPort.Disconnect(ConnectionId connectionId)
        {
            Assert.That(connectionId, Is.EqualTo(_connection));
            Disconnect();
        }

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
            if (TryGetKind(copy, out NetworkWireKind kind))
            {
                if (kind == NetworkWireKind.SnapshotFragment)
                {
                    ServerSnapshotFragmentCount++;
                }
                else if (kind == NetworkWireKind.ReplicationPacket)
                {
                    ServerReplicationPacketCount++;
                }
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
}
