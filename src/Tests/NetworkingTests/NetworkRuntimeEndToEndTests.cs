using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Simulation;
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
            replicationEntityCapacityPerSeat: 2);
        var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 32);
        var serverSeat = new AuthoritativeReplicationSeatRuntime(
            bridge,
            new AuthoritativeReplicationChannel(
                commandHarness.Entities,
                replicationEntityCapacityPerSeat: 2,
                baselineCapacity: 4,
                disclosureLog),
            new ReplicationProjectionBuffer(entityCapacity: 2),
            new ReplicationPacketBuffer(entityCapacity: 2));

        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 1, 2, 3 });
        var protocol = new ProtocolVersion(1, 0);
        var capacity = Capacity(simulationTickRateHz: 30, statePublishRateHz: 10);
        var transport = new InMemoryTransport(new ConnectionId(11));
        var observer = new RecordingObserver();
        var interest = new FixedReplicationInterest(commandHarness.FirstHandle, commandHarness.SecondHandle);
        var sessions = new AuthoritativeSessionRegistry(
            seatCapacity: 1,
            new SessionEpoch(77),
            protocol,
            fingerprint,
            reconnectWindowTicks: 2);
        var tickState = new AuthoritativeSimulationTickState();
        var fixedInput = new AuthoritativeFixedInputIngress(
            capacity.CreateFixedInputProtocolConfig(sessions.SessionEpoch.Value, sessions.SeatCapacity),
            tickState);
        var replicationFactory = new TrackingAuthoritativeReplicationSeatRuntimeFactory(
            seatCapacity: 1,
            capacity.GlobalEntityCapacity,
            capacity.ReplicationEntityCapacityPerSeat,
            (_, _) => serverSeat);
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
            interest,
            replicationFactory,
            fixedInput,
            observer);
        Assert.That(replicationFactory.AcquireCount, Is.Zero, "Server construction must not prebuild a seat runtime.");

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
            Assert.That(replicationFactory.AcquireCount, Is.EqualTo(1));
            Assert.That(replicationFactory.LastViewer, Is.EqualTo(player));
        });

        RunAuthoritativeFrame(server, tickState, 10);
        Assert.That(transport.ServerSnapshotFragmentCount, Is.Zero);
        RunAuthoritativeFrame(server, tickState, 11);
        Assert.That(transport.ServerSnapshotFragmentCount, Is.Zero);
        RunAuthoritativeFrame(server, tickState, 12);
        Assert.That(transport.ServerSnapshotFragmentCount, Is.GreaterThan(1));
        Assert.Multiple(() =>
        {
            Assert.That(interest.CopyCalls, Is.EqualTo(1));
            Assert.That(interest.LastSeat.Slot, Is.EqualTo(0));
            Assert.That(interest.LastSeat.PlayerId.Value, Is.EqualTo(1));
        });
        client.PumpTransport();
        server.PumpTransport();

        Assert.That(clientFactory.Bridge, Is.Not.Null);
        Assert.That(client.LastCommittedTick, Is.EqualTo(12));
        Assert.That(clientFactory.Bridge!.TryResolve(commandHarness.FirstHandle, out Entity mirroredFirst), Is.True);
        Assert.That(clientWorld.Get<TestAppliedState>(mirroredFirst).Value, Is.EqualTo(10));
        ClientWorldReplicationBridge bridgeBeforeReconnect = clientFactory.Bridge;

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

        // Drain the late targetTick=12 batch on the next authoritative frame.
        RunAuthoritativeFrame(server, tickState, 13);
        client.PumpTransport();
        Assert.That(clientAdmissions.TryRead(out NetworkCommandAdmissionOutcome queued), Is.True);
        Assert.That(queued.Result, Is.EqualTo(OrderSubmitResult.Queued));
        Span<Order> admitted = stackalloc Order[2];
        Assert.That(commandHarness.Orders.TryDequeueBatch(admitted, out int admittedCount), Is.True);
        Assert.That(admittedCount, Is.EqualTo(2));

        serverWorld.Set(first, new TestReplicatedData(2, 99));
        RunAuthoritativeFrame(server, tickState, 15);
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
            Assert.That(clientFactory.CreateCount, Is.EqualTo(1));
            Assert.That(clientFactory.Bridge, Is.SameAs(bridgeBeforeReconnect));
            Assert.That(clientFactory.Applier.ReleaseCalls, Is.Zero);
            Assert.That(replicationFactory.AcquireCount, Is.EqualTo(1));
            Assert.That(replicationFactory.ReleaseCount, Is.Zero);
        });

        RunAuthoritativeFrame(server, tickState, 18);
        client.PumpTransport();
        server.PumpTransport();
        transport.Disconnect();
        server.PumpTransport();
        client.PumpTransport();
        RunAuthoritativeFrame(server, tickState, 19);
        Assert.That(observer.SeatReleases, Is.Zero);
        RunAuthoritativeFrame(server, tickState, 22);

        Assert.Multiple(() =>
        {
            Assert.That(observer.SeatReleases, Is.EqualTo(1));
            Assert.That(replicationFactory.ReleaseCount, Is.EqualTo(1));
            Assert.That(replicationFactory.LastReleasedRuntime, Is.SameAs(serverSeat));
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
        Assert.That(replicationFactory.ReleaseCount, Is.EqualTo(1));
        Assert.That(transport.DisposeCalls, Is.EqualTo(1));
    }

    [Test]
    public void ServerResync_DiscardsUnacknowledgedAreaTransitionBeforeFullSnapshot()
    {
        using World serverWorld = World.Create();
        using World clientWorld = World.Create();
        Entity player = serverWorld.Create(new PlayerIdentity { PlayerId = 1 });
        Entity first = serverWorld.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 10));
        Entity second = serverWorld.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 20));
        var commandHarness = CreateCommandHarness(serverWorld, player, first, second);
        commandHarness.Knowledge.Upsert(player, first, VisibleDisclosure());
        commandHarness.Knowledge.Upsert(player, second, VisibleDisclosure());

        var projectors = new ReplicationSchemaProjectorRegistry(schemaCapacity: 1);
        Assert.That(projectors.Register(1, new TestProjector()), Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
        projectors.Freeze();
        var bridge = new AuthoritativeWorldReplicationBridge(
            serverWorld,
            commandHarness.Entities,
            commandHarness.Knowledge,
            player,
            projectors,
            replicationEntityCapacityPerSeat: 1);
        var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 2);
        var serverSeat = new AuthoritativeReplicationSeatRuntime(
            bridge,
            new AuthoritativeReplicationChannel(commandHarness.Entities, 1, baselineCapacity: 4, disclosureLog),
            new ReplicationProjectionBuffer(1),
            new ReplicationPacketBuffer(1));
        int maxSnapshotBytes = ReplicationPacketWireCodec.GetPayloadSize(1, 1, 2);
        var capacity = new NetworkRuntimeCapacity(
            simulationTickRateHz: 30,
            statePublishRateHz: 30,
            maxDatagramPayloadBytes: 128,
            connectionCapacity: 1,
            globalEntityCapacity: 2,
            replicationEntityCapacityPerSeat: 1,
            maxCommandEntries: 1,
            maxCommandPayloadBytes: CommandBatchWireCodec.GetPayloadSize(1),
            maxCommandFragments: 4,
            maxSnapshotBytes,
            maxSnapshotFragments: 4,
            outboundQueueCapacity: 16,
            acknowledgementHistoryCapacity: 4,
            controlChannel: new ChannelId(0),
            commandChannel: new ChannelId(1),
            stateChannel: new ChannelId(2),
            inputChannel: new ChannelId(3),
            fixedInputHistoryTicksPerSeat: 8,
            fixedInputSchemaId: 1,
            fixedInputFramePayloadBytes: 12,
            fixedInputMaxFutureTicks: 4,
            fixedInputLeadTicks: 2,
            fixedInputMaxFramesPerBatch: 4,
            fixedInputPendingFrameCapacity: 8);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 7, 7 });
        var protocol = new ProtocolVersion(1, 0);
        var transport = new InMemoryTransport(new ConnectionId(17));
        var observer = new RecordingObserver();
        var interest = new FixedReplicationInterest(commandHarness.FirstHandle);
        var sessions = new AuthoritativeSessionRegistry(
            seatCapacity: 1,
            new SessionEpoch(77),
            protocol,
            fingerprint,
            reconnectWindowTicks: 2);
        var tickState = new AuthoritativeSimulationTickState();
        var fixedInput = new AuthoritativeFixedInputIngress(
            capacity.CreateFixedInputProtocolConfig(sessions.SessionEpoch.Value, sessions.SeatCapacity),
            tickState);
        var replicationFactory = new TrackingAuthoritativeReplicationSeatRuntimeFactory(
            seatCapacity: 1,
            capacity.GlobalEntityCapacity,
            capacity.ReplicationEntityCapacityPerSeat,
            (_, _) => serverSeat);
        var server = new AuthoritativeServerNetworkRuntime(
            in capacity,
            NetworkTransportPortOwnership.Borrowed,
            transport,
            transport,
            transport,
            sessions,
            commandHarness.Ingress,
            commandHarness.Results,
            new FixedControllerResolver(player),
            interest,
            replicationFactory,
            fixedInput,
            observer);
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
            new ClientBridgeFactory(clientWorld, entityCapacity: 2),
            new NetworkCommandAdmissionResultBuffer(4),
            observer);

        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        server.PumpTransport();
        client.PumpTransport();
        RunAuthoritativeFrame(server, tickState, 1);
        Assert.That(disclosureLog.Count, Is.EqualTo(1));
        client.PumpTransport();
        server.PumpTransport();
        Assert.That(disclosureLog.Count, Is.Zero, "The initial Full acknowledgement must release its disclosure record.");

        interest.Replace(commandHarness.SecondHandle);
        RunAuthoritativeFrame(server, tickState, 2);
        Assert.That(disclosureLog.Count, Is.EqualTo(2), "A complete one-entity area transition must consume conceal plus reveal.");

        var resync = new NetworkResyncRequired(
            sessionEpoch: 77,
            NetworkResyncReason.SnapshotGap,
            latestCommittedTick: 1,
            latestSnapshotId: 1);
        Span<byte> resyncPayload = stackalloc byte[NetworkResyncRequired.SizeInBytes];
        Assert.That(
            SnapshotControlWireCodec.TryEncodeResyncRequired(in resync, resyncPayload, out int resyncBytes),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        transport.EnqueueClientFrame(
            capacity.ControlChannel,
            NetworkWireKind.ResyncRequired,
            resyncPayload[..resyncBytes]);
        server.PumpTransport();
        Assert.That(disclosureLog.Count, Is.Zero, "The Full boundary must discard disclosure history replaced by resync.");

        RunAuthoritativeFrame(server, tickState, 3);
        Assert.Multiple(() =>
        {
            Assert.That(disclosureLog.Count, Is.EqualTo(1));
            Assert.That(observer.Faults, Is.Zero);
            Assert.That(server.IsFaulted, Is.False);
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
    public void ClientRuntime_AppliesHighGlobalSlotThroughSmallPerSeatPacket()
    {
        const int globalEntityCapacity = 100_000;
        const int perSeatCapacity = 1;
        int maxSnapshotBytes = ReplicationPacketWireCodec.GetPayloadSize(
            perSeatCapacity,
            perSeatCapacity,
            perSeatCapacity * 2);
        var capacity = new NetworkRuntimeCapacity(
            simulationTickRateHz: 30,
            statePublishRateHz: 10,
            maxDatagramPayloadBytes: 128,
            connectionCapacity: 2,
            globalEntityCapacity,
            replicationEntityCapacityPerSeat: perSeatCapacity,
            maxCommandEntries: 1,
            maxCommandPayloadBytes: CommandBatchWireCodec.GetPayloadSize(1),
            maxCommandFragments: 4,
            maxSnapshotBytes,
            maxSnapshotFragments: 4,
            outboundQueueCapacity: 16,
            acknowledgementHistoryCapacity: 4,
            controlChannel: new ChannelId(0),
            commandChannel: new ChannelId(1),
            stateChannel: new ChannelId(2),
            inputChannel: new ChannelId(3),
            fixedInputHistoryTicksPerSeat: 8,
            fixedInputSchemaId: 1,
            fixedInputFramePayloadBytes: 12,
            fixedInputMaxFutureTicks: 4,
            fixedInputLeadTicks: 2,
            fixedInputMaxFramesPerBatch: 4,
            fixedInputPendingFrameCapacity: 8);
        var transport = new InMemoryTransport(new ConnectionId(31));
        var observer = new RecordingObserver();
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 4, 2 });
        using World world = World.Create();
        var factory = new ClientBridgeFactory(world, globalEntityCapacity);
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
            factory,
            new NetworkCommandAdmissionResultBuffer(4),
            observer);

        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        var seat = new SessionSeatBinding(0, 1, new PlayerId(1));
        SessionHandshakeResponse response = SessionHandshakeResponse.Accept(
            in seat,
            new ReconnectToken(3, 9),
            protocol,
            fingerprint,
            new SessionEpoch(7));
        Span<byte> handshake = stackalloc byte[HandshakeWireCodec.ResponseSizeInBytes];
        Assert.That(
            HandshakeWireCodec.TryEncodeResponse(in response, handshake, out int handshakeBytes),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        transport.EnqueueServerFrame(
            capacity.ControlChannel,
            NetworkWireKind.SessionHandshakeResponse,
            handshake[..handshakeBytes]);
        client.PumpTransport();
        Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));

        var highHandle = new NetworkEntityHandle(slot: 99_999, generation: 1);
        var channel = new AuthoritativeReplicationChannel(
            new NetworkEntityTable(capacity: 100_000),
            perSeatCapacity,
            baselineCapacity: 2,
            new ReplicationDisclosureChangeLog(perSeatCapacity * 2));
        var packet = new ReplicationPacketBuffer(perSeatCapacity);
        Assert.That(
            channel.BuildFull(
                sessionEpoch: 7,
                tick: 30,
                snapshotId: 1,
                new[]
                {
                    new ReplicatedEntityState(
                        highHandle,
                        1,
                        1,
                        new ReplicationStateVector(99_999, 0, 0, 0),
                        ReplicationControlOwnership.Unowned),
                },
                new[] { new ReplicationDisclosureInput(highHandle, KnowledgePresence.LiveVisible) },
                packet),
            Is.EqualTo(ReplicationBuildResult.Success));
        byte[] snapshot = new byte[maxSnapshotBytes];
        Assert.That(
            ReplicationPacketWireCodec.TryEncode(packet, snapshot, out int snapshotBytes),
            Is.EqualTo(NetworkWireCodecStatus.Success));

        var fragmentEncoder = new SnapshotFragmentEncoder(
            capacity.MaxDatagramPayloadBytes,
            capacity.MaxSnapshotBytes,
            capacity.MaxSnapshotFragments);
        Assert.That(
            fragmentEncoder.TryGetFragmentCount(snapshotBytes, out ushort fragmentCount),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.That(fragmentCount, Is.GreaterThan(1));
        byte[] fragment = new byte[capacity.MaxDatagramPayloadBytes];
        for (ushort fragmentIndex = 0; fragmentIndex < fragmentCount; fragmentIndex++)
        {
            Assert.That(
                fragmentEncoder.TryEncodeFragment(
                    sessionEpoch: 7,
                    snapshotId: 1,
                    snapshot.AsSpan(0, snapshotBytes),
                    fragmentIndex,
                    fragmentCount,
                    fragment,
                    out int fragmentBytes),
                Is.EqualTo(NetworkWireCodecStatus.Success));
            transport.EnqueueServerFrame(
                capacity.ControlChannel,
                NetworkWireKind.SnapshotFragment,
                fragment.AsSpan(0, fragmentBytes));
        }

        client.PumpTransport();
        Assert.That(
            observer.Faults,
            Is.Zero,
            $"Last fault: {observer.LastFault.Code}, detail {observer.LastFault.Detail}.");
        Assert.That(client.LastCommittedTick, Is.EqualTo(30));
        Assert.That(factory.Bridge, Is.Not.Null);
        Assert.That(factory.Bridge!.TryResolve(highHandle, out Entity mirrored), Is.True);
        Assert.That(world.Get<TestAppliedState>(mirrored).Value, Is.EqualTo(99_999));
    }

    [Test]
    public void ClientRuntime_RejectsFactoryThatCreatesDifferentGlobalCapacity()
    {
        var capacity = Capacity();
        var transport = new InMemoryTransport(new ConnectionId(32));
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 5, 1 });
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
            new ClientBridgeFactory(world, entityCapacity: 1, declaredGlobalEntityCapacity: 2),
            new NetworkCommandAdmissionResultBuffer(4),
            new RecordingObserver());

        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        var seat = new SessionSeatBinding(0, 1, new PlayerId(1));
        SessionHandshakeResponse response = SessionHandshakeResponse.Accept(
            in seat,
            new ReconnectToken(3, 10),
            protocol,
            fingerprint,
            new SessionEpoch(8));
        Span<byte> payload = stackalloc byte[HandshakeWireCodec.ResponseSizeInBytes];
        Assert.That(
            HandshakeWireCodec.TryEncodeResponse(in response, payload, out int payloadBytes),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        transport.EnqueueServerFrame(
            capacity.ControlChannel,
            NetworkWireKind.SessionHandshakeResponse,
            payload[..payloadBytes]);

        Assert.That(
            client.PumpTransport,
            Throws.InvalidOperationException.With.Message.Contains("differs from its factory"));
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
    public void ClientRuntime_SessionEpochRollover_ReleasesOwnedAndBorrowedThenAcceptsNewFull()
    {
        var capacity = new NetworkRuntimeCapacity(
            simulationTickRateHz: 30,
            statePublishRateHz: 30,
            maxDatagramPayloadBytes: 512,
            connectionCapacity: 2,
            globalEntityCapacity: 2,
            replicationEntityCapacityPerSeat: 2,
            maxCommandEntries: 2,
            maxCommandPayloadBytes: CommandBatchWireCodec.GetPayloadSize(2),
            maxCommandFragments: 4,
            maxSnapshotBytes: ReplicationPacketWireCodec.GetPayloadSize(2, 2, 4),
            maxSnapshotFragments: 4,
            outboundQueueCapacity: 32,
            acknowledgementHistoryCapacity: 4,
            controlChannel: new ChannelId(0),
            commandChannel: new ChannelId(1),
            stateChannel: new ChannelId(2),
            inputChannel: new ChannelId(3),
            fixedInputHistoryTicksPerSeat: 8,
            fixedInputSchemaId: 1,
            fixedInputFramePayloadBytes: 12,
            fixedInputMaxFutureTicks: 4,
            fixedInputLeadTicks: 2,
            fixedInputMaxFramesPerBatch: 4,
            fixedInputPendingFrameCapacity: 8);
        var transport = new InMemoryTransport(new ConnectionId(41));
        var observer = new RecordingObserver();
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 8, 1 });
        var credentials = new MemoryCredentials();
        using World world = World.Create();
        Entity authored = world.Create(new TestAppliedState(0));
        var factory = new ClientBridgeFactory(world, entityCapacity: 2);
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
            factory,
            new NetworkCommandAdmissionResultBuffer(4),
            observer);

        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        AcceptHandshake(
            transport,
            capacity.ControlChannel,
            protocol,
            fingerprint,
            new SessionEpoch(7),
            reconnectToken: new ReconnectToken(1, 7));
        client.PumpTransport();
        Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));
        Assert.That(factory.CreateCount, Is.EqualTo(1));
        Assert.That(factory.Bridge, Is.Not.Null);

        var ownedHandle = new NetworkEntityHandle(0, 1);
        var borrowedHandle = new NetworkEntityHandle(1, 1);
        Assert.That(factory.Bridge!.BindExisting(borrowedHandle, authored), Is.EqualTo(ReplicationBridgeResult.Success));
        ClientWorldReplicationBridge oldBridge = factory.Bridge;

        DeliverFullSnapshot(
            transport,
            capacity,
            sessionEpoch: 7,
            tick: 10,
            snapshotId: 1,
            new[]
            {
                new ReplicatedEntityState(
                    ownedHandle,
                    1,
                    1,
                    new ReplicationStateVector(11, 0, 0, 0),
                    ReplicationControlOwnership.Unowned),
                new ReplicatedEntityState(
                    borrowedHandle,
                    1,
                    1,
                    new ReplicationStateVector(22, 0, 0, 0),
                    ReplicationControlOwnership.Unowned),
            },
            new[]
            {
                new ReplicationDisclosureInput(ownedHandle, KnowledgePresence.LiveVisible),
                new ReplicationDisclosureInput(borrowedHandle, KnowledgePresence.LiveVisible),
            });
        client.PumpTransport();
        Assert.That(observer.Faults, Is.Zero, $"Last fault: {observer.LastFault.Code}");
        Assert.That(client.LastCommittedTick, Is.EqualTo(10));
        Assert.Multiple(() =>
        {
            Assert.That(observer.ClientReplicationCommits, Is.EqualTo(1));
            Assert.That(observer.LastClientReplicationSeat, Is.EqualTo(new SessionSeatBinding(0, 1, new PlayerId(1))));
            Assert.That(observer.LastClientReplicationHeader.Tick, Is.EqualTo(10));
            Assert.That(observer.LastClientReplicationHeader.SnapshotId, Is.EqualTo(1));
        });
        Assert.That(oldBridge.TryResolve(ownedHandle, out Entity owned), Is.True);
        Assert.That(oldBridge.TryResolve(borrowedHandle, out Entity borrowed), Is.True);
        Assert.That(borrowed, Is.EqualTo(authored));
        Assert.That(world.Get<TestAppliedState>(owned).Value, Is.EqualTo(11));
        Assert.That(world.Get<TestAppliedState>(authored).Value, Is.EqualTo(22));

        transport.Disconnect();
        client.PumpTransport();
        Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Disconnected));

        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        RejectHandshake(
            transport,
            capacity.ControlChannel,
            protocol,
            fingerprint,
            HandshakeRejectReason.SessionEpochMismatch,
            new SessionEpoch(8));
        Assert.DoesNotThrow(client.PumpTransport);
        Assert.Multiple(() =>
        {
            Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Disconnected));
            Assert.That(client.SessionEpoch, Is.EqualTo(SessionEpoch.Empty));
            Assert.That(client.IsFaulted, Is.False);
            Assert.That(observer.Faults, Is.Zero);
            Assert.That(factory.CreateCount, Is.EqualTo(1));
            Assert.That(oldBridge.IsTornDown, Is.True);
            Assert.That(factory.Applier.ReleaseCalls, Is.EqualTo(2));
            Assert.That(factory.Applier.LastLeaveKind, Is.EqualTo(ReplicationMirrorLeaveKind.Teardown));
            Assert.That(observer.ClientReplicationTeardowns, Is.EqualTo(1));
            Assert.That(observer.LastClientReplicationTornDownSeat, Is.EqualTo(new SessionSeatBinding(0, 1, new PlayerId(1))));
            Assert.That(observer.LastClientReplicationTornDownEpoch, Is.EqualTo(7));
            Assert.That(world.IsAlive(owned), Is.False);
            Assert.That(world.IsAlive(authored), Is.True);
            Assert.That(world.Has<ReplicationMirrorIdentity>(authored), Is.False);
            Assert.That(credentials.TryLoad(out _), Is.EqualTo(ClientCredentialLoadStatus.Empty));
        });

        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        AcceptHandshake(
            transport,
            capacity.ControlChannel,
            protocol,
            fingerprint,
            new SessionEpoch(8),
            reconnectToken: new ReconnectToken(2, 8));
        Assert.DoesNotThrow(client.PumpTransport);
        Assert.Multiple(() =>
        {
            Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));
            Assert.That(client.SessionEpoch, Is.EqualTo(new SessionEpoch(8)));
            Assert.That(client.IsFaulted, Is.False);
            Assert.That(observer.Faults, Is.Zero);
            Assert.That(observer.LastFault.Code, Is.Not.EqualTo(NetworkRuntimeFaultCode.SessionContractViolation));
            Assert.That(factory.CreateCount, Is.EqualTo(2));
            Assert.That(factory.Bridge, Is.Not.SameAs(oldBridge));
            Assert.That(credentials.TryLoad(out ClientSessionCredentials stored), Is.EqualTo(ClientCredentialLoadStatus.Loaded));
            Assert.That(stored.SessionEpoch, Is.EqualTo(new SessionEpoch(8)));
        });

        Assert.That(factory.Bridge!.BindExisting(borrowedHandle, authored), Is.EqualTo(ReplicationBridgeResult.Success));
        DeliverFullSnapshot(
            transport,
            capacity,
            sessionEpoch: 8,
            tick: 1,
            snapshotId: 1,
            new[]
            {
                new ReplicatedEntityState(
                    ownedHandle,
                    1,
                    1,
                    new ReplicationStateVector(33, 0, 0, 0),
                    ReplicationControlOwnership.Unowned),
                new ReplicatedEntityState(
                    borrowedHandle,
                    1,
                    1,
                    new ReplicationStateVector(44, 0, 0, 0),
                    ReplicationControlOwnership.Unowned),
            },
            new[]
            {
                new ReplicationDisclosureInput(ownedHandle, KnowledgePresence.LiveVisible),
                new ReplicationDisclosureInput(borrowedHandle, KnowledgePresence.LiveVisible),
            });
        client.PumpTransport();
        Assert.Multiple(() =>
        {
            Assert.That(observer.Faults, Is.Zero, $"Last fault: {observer.LastFault.Code}");
            Assert.That(client.LastCommittedTick, Is.EqualTo(1));
            Assert.That(factory.Bridge.TryResolve(ownedHandle, out Entity newOwned), Is.True);
            Assert.That(factory.Bridge.TryResolve(borrowedHandle, out Entity newBorrowed), Is.True);
            Assert.That(newBorrowed, Is.EqualTo(authored));
            Assert.That(world.Get<TestAppliedState>(newOwned).Value, Is.EqualTo(33));
            Assert.That(world.Get<TestAppliedState>(authored).Value, Is.EqualTo(44));
            Assert.That(observer.ClientReplicationCommits, Is.EqualTo(2));
            Assert.That(observer.LastClientReplicationHeader.Tick, Is.EqualTo(1));
            Assert.That(observer.LastClientReplicationHeader.SnapshotId, Is.EqualTo(1));
        });
    }

    [Test]
    public void ClientRuntime_SameEpochSeatGenerationChange_TearsDownOldBridgeBeforeCreatingReplacement()
    {
        NetworkRuntimeCapacity capacity = Capacity();
        var transport = new InMemoryTransport(new ConnectionId(42));
        var observer = new RecordingObserver();
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 8, 2 });
        var credentials = new MemoryCredentials();
        using World world = World.Create();
        var factory = new ClientBridgeFactory(world, entityCapacity: 2);
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
            factory,
            new NetworkCommandAdmissionResultBuffer(4),
            observer);
        observer.ClientIdentityClearedProbe = () =>
            !client.Seat.IsValid && client.SessionEpoch == SessionEpoch.Empty;
        observer.ClientBridgeCreateCountProbe = () => factory.CreateCount;

        SessionSeatBinding firstSeat = new(0, 1, new PlayerId(1));
        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        AcceptHandshake(
            transport,
            capacity.ControlChannel,
            protocol,
            fingerprint,
            new SessionEpoch(7),
            in firstSeat,
            reconnectToken: new ReconnectToken(1, 7));
        client.PumpTransport();
        ClientWorldReplicationBridge oldBridge = factory.Bridge!;

        transport.Disconnect();
        client.PumpTransport();
        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        SessionSeatBinding replacementSeat = new(0, 2, new PlayerId(1));
        AcceptHandshake(
            transport,
            capacity.ControlChannel,
            protocol,
            fingerprint,
            new SessionEpoch(7),
            in replacementSeat,
            reconnectToken: new ReconnectToken(2, 7));
        client.PumpTransport();

        Assert.Multiple(() =>
        {
            Assert.That(oldBridge.IsTornDown, Is.True);
            Assert.That(factory.CreateCount, Is.EqualTo(2));
            Assert.That(factory.Bridge, Is.Not.SameAs(oldBridge));
            Assert.That(factory.Bridge!.ClientSeat, Is.EqualTo(replacementSeat));
            Assert.That(client.Seat, Is.EqualTo(replacementSeat));
            Assert.That(client.SessionEpoch, Is.EqualTo(new SessionEpoch(7)));
            Assert.That(observer.ClientReplicationTeardowns, Is.EqualTo(1));
            Assert.That(observer.LastClientReplicationTornDownSeat, Is.EqualTo(firstSeat));
            Assert.That(observer.LastClientReplicationTornDownEpoch, Is.EqualTo(7));
            Assert.That(observer.ClientIdentityWasClearedAtLastTeardown, Is.True);
            Assert.That(observer.ClientBridgeCreateCountAtLastTeardown, Is.EqualTo(1));
            Assert.That(observer.Faults, Is.Zero);
        });
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

    private static void AcceptHandshake(
        InMemoryTransport transport,
        ChannelId controlChannel,
        ProtocolVersion protocol,
        ContentFingerprint fingerprint,
        SessionEpoch epoch,
        ReconnectToken reconnectToken)
    {
        var seat = new SessionSeatBinding(0, 1, new PlayerId(1));
        AcceptHandshake(
            transport,
            controlChannel,
            protocol,
            fingerprint,
            epoch,
            in seat,
            reconnectToken);
    }

    private static void AcceptHandshake(
        InMemoryTransport transport,
        ChannelId controlChannel,
        ProtocolVersion protocol,
        ContentFingerprint fingerprint,
        SessionEpoch epoch,
        in SessionSeatBinding seat,
        ReconnectToken reconnectToken)
    {
        SessionHandshakeResponse response = SessionHandshakeResponse.Accept(
            in seat,
            reconnectToken,
            protocol,
            fingerprint,
            epoch);
        Span<byte> payload = stackalloc byte[HandshakeWireCodec.ResponseSizeInBytes];
        Assert.That(
            HandshakeWireCodec.TryEncodeResponse(in response, payload, out int payloadBytes),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        transport.EnqueueServerFrame(controlChannel, NetworkWireKind.SessionHandshakeResponse, payload[..payloadBytes]);
    }

    private static void RejectHandshake(
        InMemoryTransport transport,
        ChannelId controlChannel,
        ProtocolVersion protocol,
        ContentFingerprint fingerprint,
        HandshakeRejectReason reason,
        SessionEpoch epoch)
    {
        SessionHandshakeResponse response = SessionHandshakeResponse.Reject(
            reason,
            protocol,
            fingerprint,
            epoch);
        Span<byte> payload = stackalloc byte[HandshakeWireCodec.ResponseSizeInBytes];
        Assert.That(
            HandshakeWireCodec.TryEncodeResponse(in response, payload, out int payloadBytes),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        transport.EnqueueServerFrame(controlChannel, NetworkWireKind.SessionHandshakeResponse, payload[..payloadBytes]);
    }

    private static void DeliverFullSnapshot(
        InMemoryTransport transport,
        in NetworkRuntimeCapacity capacity,
        ulong sessionEpoch,
        uint tick,
        ulong snapshotId,
        ReplicatedEntityState[] states,
        ReplicationDisclosureInput[] disclosures)
    {
        using World authoritativeWorld = World.Create();
        var entities = new NetworkEntityTable(capacity.GlobalEntityCapacity);
        int maximumSlot = -1;
        for (int i = 0; i < states.Length; i++)
        {
            maximumSlot = Math.Max(maximumSlot, states[i].Entity.Slot);
        }

        for (int slot = 0; slot <= maximumSlot; slot++)
        {
            Assert.That(entities.TryAllocate(authoritativeWorld.Create(), out NetworkEntityHandle allocated), Is.True);
            Assert.That(allocated, Is.EqualTo(new NetworkEntityHandle(slot, generation: 1)));
        }

        var channel = new AuthoritativeReplicationChannel(
            entities,
            capacity.ReplicationEntityCapacityPerSeat,
            baselineCapacity: 2,
            new ReplicationDisclosureChangeLog(capacity.ReplicationEntityCapacityPerSeat * 4));
        var packet = new ReplicationPacketBuffer(capacity.ReplicationEntityCapacityPerSeat);
        Assert.That(
            channel.BuildFull(sessionEpoch, tick, snapshotId, states, disclosures, packet),
            Is.EqualTo(ReplicationBuildResult.Success));
        byte[] snapshot = new byte[capacity.MaxSnapshotBytes];
        Assert.That(
            ReplicationPacketWireCodec.TryEncode(packet, snapshot, out int snapshotBytes),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        transport.EnqueueServerFrame(
            capacity.StateChannel,
            NetworkWireKind.ReplicationPacket,
            snapshot.AsSpan(0, snapshotBytes));
    }

    private static void RunAuthoritativeFrame(
        AuthoritativeServerNetworkRuntime server,
        AuthoritativeSimulationTickState tickState,
        uint tick)
    {
        int expected = tickState.CommittedTick + 1;
        if ((int)tick != expected)
        {
            tickState.RestoreCommittedTick(checked((int)tick) - 1);
        }

        tickState.Begin(checked((int)tick));
        server.BeforeAuthoritativeTick(tick);
        tickState.Commit(checked((int)tick));
        server.AfterAuthoritativeCommit(tick);
    }

    private static NetworkRuntimeCapacity Capacity(
        int simulationTickRateHz = 30,
        int statePublishRateHz = 30) => new(
        simulationTickRateHz,
        statePublishRateHz,
        maxDatagramPayloadBytes: 128,
        connectionCapacity: 2,
        globalEntityCapacity: 2,
        replicationEntityCapacityPerSeat: 2,
        maxCommandEntries: 2,
        maxCommandPayloadBytes: CommandBatchWireCodec.GetPayloadSize(2),
        maxCommandFragments: 4,
        maxSnapshotBytes: ReplicationPacketWireCodec.GetPayloadSize(2, 2, 4),
        maxSnapshotFragments: 4,
        outboundQueueCapacity: 32,
        acknowledgementHistoryCapacity: 4,
        controlChannel: new ChannelId(0),
        commandChannel: new ChannelId(1),
        stateChannel: new ChannelId(2),
        inputChannel: new ChannelId(3),
        fixedInputHistoryTicksPerSeat: 8,
        fixedInputSchemaId: 1,
        fixedInputFramePayloadBytes: 12,
        fixedInputMaxFutureTicks: 4,
        fixedInputLeadTicks: 2,
        fixedInputMaxFramesPerBatch: 4,
        fixedInputPendingFrameCapacity: 8);

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

            state = new ReplicationProjectedState(
                data.Revision,
                new ReplicationStateVector(data.Value, 0, 0, 0),
                ReplicationControlOwnership.Unowned);
            return true;
        }
    }

    private sealed class TestApplier : IClientReplicationSchemaApplier
    {
        public int ReleaseCalls { get; private set; }
        public ReplicationMirrorLeaveKind LastLeaveKind { get; private set; }

        public bool CanCreate(World world, in ReplicatedEntityState state, in ReplicationApplyContext context) => true;
        public bool CanApply(World world, Entity entity, in ReplicatedEntityState state, in ReplicationApplyContext context)
            => world.Has<TestAppliedState>(entity);
        public bool CanRelease(
            World world,
            Entity entity,
            ReplicationMirrorLeaveKind leaveKind,
            in ReplicationApplyContext context)
            => world.Has<TestAppliedState>(entity);

        public Entity Create(
            World world,
            in ReplicationMirrorIdentity identity,
            in ReplicationMirrorState state,
            in ReplicationApplyContext context)
        {
            var applied = new TestAppliedState(state.Values.Value0);
            return world.Create(in identity, in state, in applied);
        }

        public void Apply(World world, Entity entity, in ReplicatedEntityState state, in ReplicationApplyContext context) =>
            world.Set(entity, new TestAppliedState(state.Values.Value0));

        public void Release(
            World world,
            Entity entity,
            ReplicationMirrorLeaveKind leaveKind,
            in ReplicationApplyContext context)
        {
            ReleaseCalls++;
            LastLeaveKind = leaveKind;
            world.Set(entity, new TestAppliedState(0));
        }
    }

    private sealed class ClientBridgeFactory : IClientReplicationBridgeFactory
    {
        private readonly World _world;
        private readonly int _entityCapacity;
        private readonly int _declaredGlobalEntityCapacity;
        private readonly TestApplier _applier;

        public ClientBridgeFactory(
            World world,
            int entityCapacity,
            int? declaredGlobalEntityCapacity = null,
            TestApplier? applier = null)
        {
            _world = world;
            _entityCapacity = entityCapacity;
            _declaredGlobalEntityCapacity = declaredGlobalEntityCapacity ?? entityCapacity;
            _applier = applier ?? new TestApplier();
        }

        public ClientWorldReplicationBridge? Bridge { get; private set; }
        public TestApplier Applier => _applier;
        public int CreateCount { get; private set; }
        public int GlobalEntityCapacity => _declaredGlobalEntityCapacity;

        public ClientWorldReplicationBridge Create(in SessionSeatBinding clientSeat, ulong sessionEpoch)
        {
            var appliers = new ClientReplicationSchemaApplierRegistry(schemaCapacity: 1);
            Assert.That(appliers.Register(1, _applier), Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
            appliers.Freeze();
            CreateCount++;
            Bridge = new ClientWorldReplicationBridge(
                _world,
                _entityCapacity,
                in clientSeat,
                sessionEpoch,
                appliers);
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

    private sealed class FixedReplicationInterest : IAuthoritativeReplicationInterestPort
    {
        private NetworkEntityHandle[] _handles;
        public FixedReplicationInterest(params NetworkEntityHandle[] handles) => _handles = handles;

        public void Replace(params NetworkEntityHandle[] handles) => _handles = handles;

        public int CopyCalls { get; private set; }
        public SessionSeatBinding LastSeat { get; private set; }

        public bool TryCopyInterest(
            in SessionSeatBinding seat,
            Span<NetworkEntityHandle> destination,
            out int count)
        {
            CopyCalls++;
            LastSeat = seat;
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
        public int ClientReplicationCommits { get; private set; }
        public int ClientReplicationTeardowns { get; private set; }
        public SessionSeatBinding LastClientReplicationSeat { get; private set; }
        public ReplicationPacketHeader LastClientReplicationHeader { get; private set; }
        public SessionSeatBinding LastClientReplicationTornDownSeat { get; private set; }
        public ulong LastClientReplicationTornDownEpoch { get; private set; }
        public bool ClientIdentityWasClearedAtLastTeardown { get; private set; }
        public int ClientBridgeCreateCountAtLastTeardown { get; private set; }
        public NetworkRuntimeFault LastFault { get; private set; }
        public Func<bool>? ClientIdentityClearedProbe { get; set; }
        public Func<int>? ClientBridgeCreateCountProbe { get; set; }

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

        public void OnClientReplicationCommitted(
            in SessionSeatBinding seat,
            in ReplicationPacketHeader header)
        {
            ClientReplicationCommits++;
            LastClientReplicationSeat = seat;
            LastClientReplicationHeader = header;
        }

        public void OnClientReplicationTornDown(in SessionSeatBinding seat, ulong sessionEpoch)
        {
            ClientReplicationTeardowns++;
            LastClientReplicationTornDownSeat = seat;
            LastClientReplicationTornDownEpoch = sessionEpoch;
            ClientIdentityWasClearedAtLastTeardown = ClientIdentityClearedProbe?.Invoke() ?? false;
            ClientBridgeCreateCountAtLastTeardown = ClientBridgeCreateCountProbe?.Invoke() ?? 0;
        }
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

        public void EnqueueClientFrame(ChannelId channel, NetworkWireKind kind, ReadOnlySpan<byte> payload)
        {
            byte[] framed = new byte[NetworkWireEnvelopeCodec.GetFramedLength(payload.Length)];
            Assert.That(NetworkWireEnvelopeCodec.TryEncode(kind, payload, framed, out _), Is.EqualTo(NetworkWireCodecStatus.Success));
            _serverInbound.Enqueue(new Frame(channel, framed));
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
