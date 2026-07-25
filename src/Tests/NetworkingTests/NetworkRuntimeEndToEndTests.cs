using System.Buffers.Binary;
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

    [TestCase(200u, 0f, 180, 30, 6, 206u)]
    [TestCase(200u, 0.1f, 180, 30, 6, 206u)]
    [TestCase(200u, 0.5f, 0, 30, 20, 216u)]
    [TestCase(200u, 0f, 0, 30, 0, 200u)]
    [TestCase(0u, 0.5f, 180, 30, 6, 0u)]
    public void ClientCommandTargetEstimate_AccountsForTimingAndHonorsFutureWindow(
        uint committedTick,
        float snapshotAgeSeconds,
        int roundTripMilliseconds,
        int simulationTickRateHz,
        int maxFutureTargetTicks,
        uint expected)
    {
        Assert.That(
            ReplicatedClientNetworkRuntime.EstimateCommandTargetTick(
                committedTick,
                snapshotAgeSeconds,
                roundTripMilliseconds,
                simulationTickRateHz,
                maxFutureTargetTicks),
            Is.EqualTo(expected));
    }

    [Test]
    public void ClientCommandTargetEstimate_RejectsInvalidTimingInputs()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => ReplicatedClientNetworkRuntime.EstimateCommandTargetTick(1, -0.1f, 0, 30, 6),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => ReplicatedClientNetworkRuntime.EstimateCommandTargetTick(1, 0f, -1, 30, 6),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => ReplicatedClientNetworkRuntime.EstimateCommandTargetTick(1, 0f, 0, 0, 6),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => ReplicatedClientNetworkRuntime.EstimateCommandTargetTick(1, 0f, 0, 30, -1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void ReplicatedClientCommandPort_DefaultOptionalEntityReferences_FailFastAtSubmissionBoundary()
    {
        using World world = World.Create();
        var transport = new InMemoryTransport(new ConnectionId(31));
        var observer = new RecordingObserver();
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 3, 1, 4 });
        using ReplicatedClientNetworkRuntime client = CreateClientRuntime(
            world,
            transport,
            observer,
            protocol,
            fingerprint,
            out _);
        var schemas = new NetworkCommandSchemaRegistry();
        schemas.Register(new NetworkCommandSchema(
            TestOrderTypeId,
            NetworkCommandTargetKind.None,
            allowArg0: false,
            allowArg1: false,
            NetworkCommandSubmitModeMask.Immediate,
            KnowledgePositionAccess.None));
        schemas.Freeze();
        var commands = new ReplicatedClientCommandPort(world, client, schemas, maxActorsPerBatch: 1);
        Entity actor = world.Create();
        var invalidOrders = new[]
        {
            (Field: nameof(Order.Target), Order: new Order { Actor = actor, OrderTypeId = TestOrderTypeId, Target = default }),
            (Field: nameof(Order.TargetContext), Order: new Order { Actor = actor, OrderTypeId = TestOrderTypeId, TargetContext = default }),
            (Field: nameof(Order.CommandSource), Order: new Order { Actor = actor, OrderTypeId = TestOrderTypeId, CommandSource = default }),
        };

        foreach ((string field, Order invalidOrder) in invalidOrders)
        {
            Order candidate = invalidOrder;
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => commands.Submit(in candidate),
                field)!;
            Assert.That(error.Message, Does.Contain(field));
            Assert.That(error.Message, Does.Contain("Entity.Null"));
        }

        Assert.Multiple(() =>
        {
            Assert.That(commands.SubmissionRevision, Is.Zero);
            Assert.That(transport.LastClientCommandBatchSequence, Is.Zero);
        });
    }

    [Test]
    public void ReplicatedClientCommandPort_NoneTarget_SubmitsCanonicalTargetlessOrder()
    {
        using World world = World.Create();
        var transport = new InMemoryTransport(new ConnectionId(32));
        var observer = new RecordingObserver();
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 2, 7, 1, 8 });
        using ReplicatedClientNetworkRuntime client = CreateClientRuntime(
            world,
            transport,
            observer,
            protocol,
            fingerprint,
            out ClientBridgeFactory factory);
        ConnectAndAccept(client, transport, protocol, fingerprint, seatGeneration: 1, tokenLow: 40, nextClientBatchSequence: 1);
        var actorHandle = new NetworkEntityHandle(0, 1);
        EnqueueFullReplication(transport, sessionEpoch: 9, tick: 1, snapshotId: 1, actorHandle, revision: 1);
        client.PumpTransport();
        client.PumpReplicatedClient(0f);
        Assert.That(factory.Bridge!.TryResolve(actorHandle, out Entity actor), Is.True);

        var schemas = new NetworkCommandSchemaRegistry();
        schemas.Register(new NetworkCommandSchema(
            TestOrderTypeId,
            NetworkCommandTargetKind.None,
            allowArg0: false,
            allowArg1: false,
            NetworkCommandSubmitModeMask.Immediate,
            KnowledgePositionAccess.None));
        schemas.Freeze();
        var commands = new ReplicatedClientCommandPort(world, client, schemas, maxActorsPerBatch: 1);
        var order = new Order
        {
            Actor = actor,
            OrderTypeId = TestOrderTypeId,
            SubmitMode = OrderSubmitMode.Immediate,
        };

        Assert.Multiple(() =>
        {
            Assert.That(order.Target, Is.EqualTo(Entity.Null));
            Assert.That(order.TargetContext, Is.EqualTo(Entity.Null));
            Assert.That(order.CommandSource, Is.EqualTo(Entity.Null));
            Assert.That(order.Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.None));
        });
        Assert.That(commands.Submit(in order), Is.EqualTo(ReplicatedClientCommandSubmitResult.Submitted));
        Assert.Multiple(() =>
        {
            Assert.That(commands.LastSubmittedBatchSequence, Is.EqualTo(1));
            Assert.That(transport.LastClientCommandBatchSequence, Is.EqualTo(1));
        });
    }

    [Test]
    public void TwoRuntimePorts_HandshakeCommandsRecoverDroppedDeltaContinueReplicatingReconnectAndReleaseSeat()
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
        NetworkCommandAdmissionResultBuffer clientAdmissions = observer.ClientAdmissions;
        var client = new ReplicatedClientNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 0.5f,
            reconnectWindowSeconds: 30f,
            protocol,
            fingerprint,
            credentials,
            clientFactory,
            observer);

        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        server.PumpTransport();
        client.PumpTransport();
        server.PumpTransport();
        client.PumpTransport();

        Assert.Multiple(() =>
        {
            Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));
            Assert.That(client.HasEstablishedSession, Is.True);
            Assert.That(client.IsAwaitingFullSnapshot, Is.True);
            Assert.That(client.RoundTripTimeMilliseconds, Is.EqualTo(24));
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
        Assert.That(clientFactory.Bridge, Is.Not.Null);
        Assert.That(clientFactory.Bridge!.TryResolve(commandHarness.FirstHandle, out _), Is.False,
            "Transport pumping may decode replication but must not mutate the client ECS world.");
        client.PumpReplicatedClient(0f);
        server.PumpTransport();

        Assert.That(clientFactory.Bridge!.TryResolve(commandHarness.FirstHandle, out Entity mirroredFirst), Is.True);
        Assert.That(clientFactory.Bridge.TryResolve(commandHarness.SecondHandle, out Entity mirroredSecond), Is.True);
        Assert.That(clientWorld.Get<TestAppliedState>(mirroredFirst).Value, Is.EqualTo(10));
        Assert.That(client.IsAwaitingFullSnapshot, Is.False);
        Assert.That(client.InterpolationAlpha, Is.EqualTo(1f));
        client.PumpReplicatedClient(0.1f);
        Assert.That(client.InterpolationAlpha, Is.EqualTo(1f),
            "The first snapshot has no prior arrival interval and must render at its authoritative position.");

        var commandSchemas = new NetworkCommandSchemaRegistry();
        commandSchemas.Register(new NetworkCommandSchema(
            TestOrderTypeId,
            NetworkCommandTargetKind.WorldPositionCm,
            allowArg0: false,
            allowArg1: false,
            NetworkCommandSubmitModeMask.Queued | NetworkCommandSubmitModeMask.Immediate,
            KnowledgePositionAccess.None));
        commandSchemas.Freeze();
        var commandPort = new ReplicatedClientCommandPort(
            clientWorld,
            client,
            commandSchemas,
            maxActorsPerBatch: 2);
        var clientOrders = new[]
        {
            new Order
            {
                Actor = mirroredFirst,
                Target = Entity.Null,
                OrderTypeId = TestOrderTypeId,
                Args = OrderArgs.CreateSingleWorldCm(new System.Numerics.Vector3(100, 0, 0)),
                SubmitMode = OrderSubmitMode.Queued,
            },
            new Order
            {
                Actor = mirroredSecond,
                Target = Entity.Null,
                OrderTypeId = TestOrderTypeId,
                Args = OrderArgs.CreateSingleWorldCm(new System.Numerics.Vector3(200, 0, 0)),
                SubmitMode = OrderSubmitMode.Queued,
            },
        };
        clientOrders[0].SubmitMode = OrderSubmitMode.Immediate;
        Assert.That(
            commandPort.Submit(clientOrders),
            Is.EqualTo(ReplicatedClientCommandSubmitResult.MixedSubmitModes));
        clientOrders[1].SubmitMode = OrderSubmitMode.Immediate;
        clientOrders[0].SubmitMode = OrderSubmitMode.PersistentQueued;
        Assert.That(commandPort.Submit(clientOrders), Is.EqualTo(ReplicatedClientCommandSubmitResult.SubmitModeNotAllowed));
        clientOrders[0].SubmitMode = OrderSubmitMode.Queued;
        clientOrders[1].SubmitMode = OrderSubmitMode.Queued;
        Assert.That(commandPort.Submit(clientOrders), Is.EqualTo(ReplicatedClientCommandSubmitResult.Submitted));
        Assert.Multiple(() =>
        {
            Assert.That(commandPort.SubmissionRevision, Is.EqualTo(3));
            Assert.That(commandPort.LastSubmittedBatchSequence, Is.EqualTo(1));
            Assert.That(commandPort.LastSubmitResult, Is.EqualTo(ReplicatedClientCommandSubmitResult.Submitted));
        });
        Assert.That(transport.ClientCommandFragmentCount, Is.GreaterThan(1));
        server.PumpTransport();
        client.PumpTransport();
        Assert.That(clientAdmissions.TryRead(out NetworkCommandAdmissionOutcome scheduled), Is.True);
        Assert.That(scheduled.Result, Is.EqualTo(OrderSubmitResult.NetworkScheduled));
        Assert.That(scheduled.CommittedTick, Is.EqualTo(10));

        server.BeforeAuthoritativeTick(11);
        client.PumpTransport();
        Assert.That(clientAdmissions.TryRead(out NetworkCommandAdmissionOutcome queued), Is.True);
        Assert.That(queued.Result, Is.EqualTo(OrderSubmitResult.Queued));
        Assert.That(queued.CommittedTick, Is.EqualTo(10));
        Span<Order> admitted = stackalloc Order[2];
        Assert.That(commandHarness.Orders.TryDequeueBatch(admitted, out int admittedCount), Is.True);
        Assert.That(admittedCount, Is.EqualTo(2));
        Assert.That(admitted[0].SubmitMode, Is.EqualTo(OrderSubmitMode.Queued));
        Assert.That(admitted[1].SubmitMode, Is.EqualTo(OrderSubmitMode.Queued));

        var firstQueued = new OrderAdmissionOutcome(
            in admitted[0],
            OrderAdmissionStage.EntityIntake,
            OrderSubmitResult.Queued);
        var secondActivated = new OrderAdmissionOutcome(
            in admitted[1],
            OrderAdmissionStage.EntityIntake,
            OrderSubmitResult.Activated);
        Assert.That(commandHarness.EntityResults.TryWrite(in firstQueued), Is.True);
        Assert.That(commandHarness.EntityResults.TryWrite(in secondActivated), Is.True);
        server.PumpTransport();
        client.PumpTransport();
        Assert.That(
            clientAdmissions.TryRead(out _),
            Is.False,
            "Entity admission outcomes must remain private until their authoritative tick commits.");

        serverWorld.Set(first, new TestReplicatedData(2, 99));
        ulong droppedSnapshotId = clientFactory.Bridge!.LastSnapshotId + 1;
        transport.DropNextServerDatagram(capacity.StateChannel, NetworkWireKind.ReplicationPacket);
        server.AfterAuthoritativeCommit(11);
        Assert.Multiple(() =>
        {
            Assert.That(transport.DroppedServerDatagrams, Is.EqualTo(1));
            Assert.That(transport.ServerReplicationPacketSendCount, Is.EqualTo(1));
        });
        client.PumpTransport();
        Assert.That(clientAdmissions.TryRead(out NetworkCommandAdmissionOutcome firstEntityQueued), Is.True);
        Assert.That(clientAdmissions.TryRead(out NetworkCommandAdmissionOutcome secondEntityActivated), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(firstEntityQueued.Stage, Is.EqualTo(OrderAdmissionStage.EntityIntake));
            Assert.That(firstEntityQueued.Result, Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(firstEntityQueued.AdmissionBatchIndex, Is.Zero);
            Assert.That(firstEntityQueued.CommittedTick, Is.EqualTo(11));
            Assert.That(secondEntityActivated.Stage, Is.EqualTo(OrderAdmissionStage.EntityIntake));
            Assert.That(secondEntityActivated.Result, Is.EqualTo(OrderSubmitResult.Activated));
            Assert.That(secondEntityActivated.AdmissionBatchIndex, Is.EqualTo(1));
            Assert.That(secondEntityActivated.CommittedTick, Is.EqualTo(11));
        });
        int fragmentsWithUnacknowledgedDelta = transport.ServerSnapshotFragmentCount;
        server.PumpTransport();
        Assert.That(clientWorld.Get<TestAppliedState>(mirroredFirst).Value, Is.EqualTo(10));

        Assert.That(commandPort.Submit(clientOrders), Is.EqualTo(ReplicatedClientCommandSubmitResult.Submitted));
        server.PumpTransport();
        client.PumpTransport();
        Assert.That(clientAdmissions.TryRead(out NetworkCommandAdmissionOutcome pendingScheduled), Is.True);
        Assert.That(pendingScheduled.ClientBatchSequence, Is.EqualTo(2));
        Assert.That(pendingScheduled.CommittedTick, Is.EqualTo(11));
        server.BeforeAuthoritativeTick(12);
        client.PumpTransport();
        Assert.That(clientAdmissions.TryRead(out NetworkCommandAdmissionOutcome pendingQueued), Is.True);
        Assert.That(pendingQueued.Result, Is.EqualTo(OrderSubmitResult.Queued));
        Assert.That(pendingQueued.CommittedTick, Is.EqualTo(11));
        Span<Order> abandonedOrders = stackalloc Order[2];
        Assert.That(commandHarness.Orders.TryDequeueBatch(abandonedOrders, out int abandonedCount), Is.True);
        Assert.That(abandonedCount, Is.EqualTo(2));

        var firstActivated = new OrderAdmissionOutcome(
            in admitted[0],
            OrderAdmissionStage.EntityIntake,
            OrderSubmitResult.Activated);
        Assert.That(commandHarness.EntityResults.TryWrite(in firstActivated), Is.True);
        server.PumpTransport();
        client.PumpTransport();
        Assert.That(
            clientAdmissions.TryRead(out _),
            Is.False,
            "A later activation must wait for the later authoritative tick to commit.");

        commandHarness.GameplayGate.CompleteMatch();
        Assert.That(commandPort.Submit(clientOrders), Is.EqualTo(ReplicatedClientCommandSubmitResult.Submitted));
        Assert.Multiple(() =>
        {
            Assert.That(commandPort.SubmissionRevision, Is.EqualTo(5));
            Assert.That(commandPort.LastSubmittedBatchSequence, Is.EqualTo(3));
        });
        server.PumpTransport();
        client.PumpTransport();
        Assert.That(clientAdmissions.TryRead(out NetworkCommandAdmissionOutcome completed), Is.True);
        Assert.That(completed.Result, Is.EqualTo(OrderSubmitResult.NetworkMatchCompleted));
        Assert.That(completed.CommittedTick, Is.EqualTo(11));

        server.AfterAuthoritativeCommit(12);
        client.PumpTransport();
        Assert.That(clientAdmissions.TryRead(out NetworkCommandAdmissionOutcome firstEntityActivated), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(firstEntityActivated.Result, Is.EqualTo(OrderSubmitResult.Activated));
            Assert.That(firstEntityActivated.AdmissionBatchIndex, Is.Zero);
            Assert.That(firstEntityActivated.ClientBatchSequence, Is.EqualTo(1));
            Assert.That(firstEntityActivated.CommittedTick, Is.EqualTo(12));
        });
        client.PumpReplicatedClient(0f);
        Assert.Multiple(() =>
        {
            Assert.That(observer.ClientResyncs, Is.EqualTo(1));
            Assert.That(observer.LastClientResync.Reason, Is.EqualTo(NetworkResyncReason.SnapshotAcknowledgementTimeout));
            Assert.That(observer.LastClientResync.LatestSnapshotId, Is.EqualTo(droppedSnapshotId));
            Assert.That(transport.ServerSnapshotFragmentCount, Is.GreaterThan(fragmentsWithUnacknowledgedDelta));
            Assert.That(transport.LastServerSnapshotFragmentChannel, Is.EqualTo(capacity.ControlChannel));
            Assert.That(client.IsAwaitingFullSnapshot, Is.False);
            Assert.That(clientWorld.Get<TestAppliedState>(mirroredFirst).Value, Is.EqualTo(99));
        });
        server.PumpTransport();

        var lateAcknowledgement = new NetworkSnapshotAcknowledgement(
            client.SessionEpoch.Value,
            droppedSnapshotId,
            committedTick: 11);
        Span<byte> lateAcknowledgementPayload = stackalloc byte[NetworkSnapshotAcknowledgement.SizeInBytes];
        Assert.That(
            SnapshotControlWireCodec.TryEncodeAcknowledgement(
                in lateAcknowledgement,
                lateAcknowledgementPayload,
                out int lateAcknowledgementBytes),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        transport.EnqueueClientFrame(
            capacity.ControlChannel,
            NetworkWireKind.SnapshotAcknowledgement,
            lateAcknowledgementPayload[..lateAcknowledgementBytes]);
        server.PumpTransport();
        Assert.That(observer.Faults, Is.Zero, "A late acknowledgement for the discarded delta is stale, not a session fault.");

        serverWorld.Set(first, new TestReplicatedData(3, 100));
        server.BeforeAuthoritativeTick(13);
        server.AfterAuthoritativeCommit(13);
        client.PumpTransport();
        client.PumpReplicatedClient(0f);
        server.PumpTransport();
        Assert.That(clientWorld.Get<TestAppliedState>(mirroredFirst).Value, Is.EqualTo(100));
        Assert.That(client.InterpolationAlpha, Is.Zero,
            "A subsequent authoritative snapshot starts interpolation from the previous replicated position.");
        client.PumpReplicatedClient(0.05f);
        Assert.That(client.InterpolationAlpha, Is.EqualTo(0.5f).Within(0.001f));

        transport.Disconnect();
        server.PumpTransport();
        client.PumpTransport();
        Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Disconnected));
        Assert.That(observer.SeatDisconnections, Is.EqualTo(1));
        Assert.That(client.ReconnectWindowRemainingSeconds, Is.EqualTo(30f));
        serverWorld.Set(first, new TestReplicatedData(4, 123));

        client.PumpReplicatedClient(0.25f);
        Assert.That(transport.ConnectAttempts, Is.EqualTo(1));
        Assert.That(client.ReconnectWindowRemainingSeconds, Is.EqualTo(29.75f).Within(0.001f));
        client.PumpReplicatedClient(0.25f);
        Assert.That(transport.ConnectAttempts, Is.EqualTo(2));
        client.PumpTransport();
        Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Handshaking));
        Assert.That(client.ReconnectWindowRemainingSeconds, Is.EqualTo(29.5f).Within(0.001f));
        client.PumpReplicatedClient(0.5f);
        Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Handshaking));
        Assert.That(client.ReconnectWindowRemainingSeconds, Is.EqualTo(29f).Within(0.001f));
        server.PumpTransport();
        client.PumpTransport();
        server.PumpTransport();
        client.PumpTransport();
        Assert.Multiple(() =>
        {
            Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));
            Assert.That(client.IsAwaitingFullSnapshot, Is.True);
            Assert.That(observer.SeatReconnections, Is.EqualTo(1));
            Assert.That(client.Seat.Generation, Is.EqualTo(1));
            Assert.That(client.NextClientBatchSequence, Is.EqualTo(4));
        });

        Assert.That(
            commandPort.Submit(clientOrders),
            Is.EqualTo(ReplicatedClientCommandSubmitResult.SnapshotUnavailable),
            "A reconnected client must restore a full snapshot before gameplay commands resume.");
        Assert.Multiple(() =>
        {
            Assert.That(commandPort.SubmissionRevision, Is.EqualTo(6));
            Assert.That(commandPort.LastSubmittedBatchSequence, Is.Zero);
            Assert.That(commandPort.LastSubmitResult, Is.EqualTo(ReplicatedClientCommandSubmitResult.SnapshotUnavailable));
        });

        server.BeforeAuthoritativeTick(14);
        server.AfterAuthoritativeCommit(14);
        client.PumpTransport();
        client.PumpReplicatedClient(0f);
        Assert.Multiple(() =>
        {
            Assert.That(client.IsAwaitingFullSnapshot, Is.False);
            Assert.That(clientWorld.Get<TestAppliedState>(mirroredFirst).Value, Is.EqualTo(123));
        });

        var restartedProcessCommandPort = new ReplicatedClientCommandPort(
            clientWorld,
            client,
            commandSchemas,
            maxActorsPerBatch: 2);
        Assert.That(
            restartedProcessCommandPort.Submit(clientOrders),
            Is.EqualTo(ReplicatedClientCommandSubmitResult.Submitted));
        Assert.That(restartedProcessCommandPort.LastSubmittedBatchSequence, Is.EqualTo(4),
            "A recreated client command port must resume from the server handshake cursor, not local memory.");
        server.PumpTransport();
        client.PumpTransport();
        Assert.That(clientAdmissions.TryRead(out NetworkCommandAdmissionOutcome resumedOutcome), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(resumedOutcome.ClientBatchSequence, Is.EqualTo(4));
            Assert.That(resumedOutcome.Result, Is.EqualTo(OrderSubmitResult.NetworkMatchCompleted));
        });

        server.PumpTransport();
        transport.Disconnect();
        server.PumpTransport();
        client.PumpTransport();
        server.BeforeAuthoritativeTick(15);
        server.AfterAuthoritativeCommit(15);
        Assert.That(observer.SeatReleases, Is.Zero);
        server.BeforeAuthoritativeTick(16);
        server.AfterAuthoritativeCommit(16);
        Assert.That(observer.SeatReleases, Is.Zero);
        server.BeforeAuthoritativeTick(17);

        var abandonedFirst = new OrderAdmissionOutcome(
            in abandonedOrders[0],
            OrderAdmissionStage.EntityIntake,
            OrderSubmitResult.Cancelled);
        var abandonedSecond = new OrderAdmissionOutcome(
            in abandonedOrders[1],
            OrderAdmissionStage.EntityIntake,
            OrderSubmitResult.Cancelled);
        Assert.That(commandHarness.EntityResults.TryWrite(in abandonedFirst), Is.True);
        Assert.That(commandHarness.EntityResults.TryWrite(in abandonedSecond), Is.True);
        server.AfterAuthoritativeCommit(17);

        Assert.Multiple(() =>
        {
            Assert.That(observer.SeatReleases, Is.EqualTo(1));
            Assert.That(observer.Faults, Is.Zero);
            Assert.That(server.IsFaulted, Is.False);
            Assert.That(client.IsFaulted, Is.False);
        });
    }

    [Test]
    public void ServerRuntime_RejectsDuplicateCommitAndOpeningNextTickBeforeCommit()
    {
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes("authoritative_tick_lifecycle"u8);

        var duplicateObserver = new RecordingObserver();
        using (HandshakeServerHarness duplicateHarness = CreateHandshakeServerHarness(
                   new InMemoryTransport(new ConnectionId(21)),
                   duplicateObserver,
                   protocol,
                   fingerprint))
        {
            duplicateHarness.Server.BeforeAuthoritativeTick(10);
            duplicateHarness.Server.AfterAuthoritativeCommit(10);
            NetworkRuntimeException duplicate = Assert.Throws<NetworkRuntimeException>(
                () => duplicateHarness.Server.AfterAuthoritativeCommit(10))!;
            Assert.That(duplicate.Fault.Code, Is.EqualTo(NetworkRuntimeFaultCode.ReplicationBuildRejected));
        }

        var openTickObserver = new RecordingObserver();
        using HandshakeServerHarness openTickHarness = CreateHandshakeServerHarness(
            new InMemoryTransport(new ConnectionId(22)),
            openTickObserver,
            protocol,
            fingerprint);
        openTickHarness.Server.BeforeAuthoritativeTick(10);
        NetworkRuntimeException skippedCommit = Assert.Throws<NetworkRuntimeException>(
            () => openTickHarness.Server.BeforeAuthoritativeTick(11))!;
        Assert.That(skippedCommit.Fault.Code, Is.EqualTo(NetworkRuntimeFaultCode.SessionContractViolation));
    }

    [Test]
    public void ServerRuntime_LostReconnectResponse_AllowsRetryWithOldCredential()
    {
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes("lost_reconnect_response"u8);
        var transport = new InMemoryTransport(new ConnectionId(21));
        var observer = new RecordingObserver();
        using HandshakeServerHarness harness = CreateHandshakeServerHarness(
            transport,
            observer,
            protocol,
            fingerprint);
        using World clientWorld = World.Create();
        var credentials = new MemoryCredentials();
        var client = new ReplicatedClientNetworkRuntime(
            harness.Capacity,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 0.5f,
            reconnectWindowSeconds: 30f,
            protocol,
            fingerprint,
            credentials,
            new ClientBridgeFactory(clientWorld, entityCapacity: 2),
            observer);

        CompleteRuntimeHandshake(client, harness.Server);
        Assert.That(credentials.TryLoad(out ClientSessionCredentials initial), Is.EqualTo(ClientCredentialLoadStatus.Loaded));

        transport.Disconnect();
        harness.Server.PumpTransport();
        client.PumpTransport();
        transport.DropNextServerDatagram(harness.Capacity.ControlChannel, NetworkWireKind.SessionHandshakeResponse);
        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        harness.Server.PumpTransport();
        Assert.Multiple(() =>
        {
            Assert.That(transport.DroppedServerDatagrams, Is.EqualTo(1));
            Assert.That(transport.ClientHandshakeConfirmationCount, Is.EqualTo(1));
            Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Handshaking));
            Assert.That(credentials.TryLoad(out ClientSessionCredentials preserved), Is.EqualTo(ClientCredentialLoadStatus.Loaded));
            Assert.That(preserved, Is.EqualTo(initial));
        });

        transport.Disconnect();
        harness.Server.PumpTransport();
        client.PumpTransport();
        CompleteRuntimeHandshake(client, harness.Server);

        Assert.Multiple(() =>
        {
            Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));
            Assert.That(client.Seat, Is.EqualTo(new SessionSeatBinding(0, 1, new PlayerId(1))));
            Assert.That(observer.InitialSeatConnections, Is.EqualTo(1));
            Assert.That(observer.SeatReconnections, Is.EqualTo(1));
            Assert.That(transport.ClientHandshakeConfirmationCount, Is.EqualTo(2));
            Assert.That(credentials.TryLoad(out ClientSessionCredentials rotated), Is.EqualTo(ClientCredentialLoadStatus.Loaded));
            Assert.That(rotated.SessionEpoch, Is.EqualTo(initial.SessionEpoch));
            Assert.That(rotated.ReconnectToken, Is.Not.EqualTo(initial.ReconnectToken));
            Assert.That(observer.Faults, Is.Zero);
        });
    }

    [Test]
    public void ClientRuntime_LostCandidateConfirmation_RestartRecoversWithPersistedCandidate()
    {
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes("lost_candidate_confirmation"u8);
        var transport = new InMemoryTransport(new ConnectionId(23));
        var observer = new RecordingObserver();
        using HandshakeServerHarness harness = CreateHandshakeServerHarness(
            transport,
            observer,
            protocol,
            fingerprint);
        using World firstClientWorld = World.Create();
        var credentials = new MemoryCredentials();
        var firstClient = new ReplicatedClientNetworkRuntime(
            harness.Capacity,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 0.5f,
            reconnectWindowSeconds: 30f,
            protocol,
            fingerprint,
            credentials,
            new ClientBridgeFactory(firstClientWorld, entityCapacity: 2),
            observer);

        CompleteRuntimeHandshake(firstClient, harness.Server);
        Assert.That(credentials.TryLoad(out ClientSessionCredentials initial), Is.EqualTo(ClientCredentialLoadStatus.Loaded));
        transport.Disconnect();
        harness.Server.PumpTransport();
        firstClient.PumpTransport();

        transport.DropNextClientDatagram(
            harness.Capacity.ControlChannel,
            NetworkWireKind.SessionHandshakeConfirmation);
        Assert.That(firstClient.TryConnectNow(), Is.True);
        firstClient.PumpTransport();
        harness.Server.PumpTransport();
        firstClient.PumpTransport();
        harness.Server.PumpTransport();

        Assert.That(credentials.TryLoad(out ClientSessionCredentials candidate), Is.EqualTo(ClientCredentialLoadStatus.Loaded));
        Assert.Multiple(() =>
        {
            Assert.That(candidate.SessionEpoch, Is.EqualTo(initial.SessionEpoch));
            Assert.That(candidate.ReconnectToken, Is.Not.EqualTo(initial.ReconnectToken));
            Assert.That(firstClient.ReconnectToken, Is.EqualTo(candidate.ReconnectToken));
            Assert.That(firstClient.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));
            Assert.That(transport.ClientHandshakeConfirmationCount, Is.EqualTo(2));
            Assert.That(transport.DroppedClientDatagrams, Is.EqualTo(1));
            Assert.That(observer.InitialSeatConnections, Is.EqualTo(1));
            Assert.That(observer.SeatReconnections, Is.Zero);
        });

        transport.Disconnect();
        harness.Server.PumpTransport();
        firstClient.PumpTransport();
        firstClient.Dispose();

        using World recoveredClientWorld = World.Create();
        using var recoveredClient = new ReplicatedClientNetworkRuntime(
            harness.Capacity,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 0.5f,
            reconnectWindowSeconds: 30f,
            protocol,
            fingerprint,
            credentials,
            new ClientBridgeFactory(recoveredClientWorld, entityCapacity: 2),
            observer);
        CompleteRuntimeHandshake(recoveredClient, harness.Server);

        Assert.That(credentials.TryLoad(out ClientSessionCredentials committed), Is.EqualTo(ClientCredentialLoadStatus.Loaded));
        Assert.Multiple(() =>
        {
            Assert.That(recoveredClient.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));
            Assert.That(recoveredClient.Seat, Is.EqualTo(new SessionSeatBinding(0, 1, new PlayerId(1))));
            Assert.That(committed.SessionEpoch, Is.EqualTo(candidate.SessionEpoch));
            Assert.That(committed.ReconnectToken, Is.EqualTo(candidate.ReconnectToken));
            Assert.That(transport.ClientHandshakeConfirmationCount, Is.EqualTo(3));
            Assert.That(transport.DroppedClientDatagrams, Is.EqualTo(1));
            Assert.That(observer.InitialSeatConnections, Is.EqualTo(1));
            Assert.That(observer.SeatReconnections, Is.EqualTo(1));
            Assert.That(observer.Faults, Is.Zero);
        });
    }

    [Test]
    public void ClientRuntime_CredentialStoreFailure_SendsNoConfirmation_AndOldCredentialCanRecover()
    {
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes("credential_store_failure"u8);
        var transport = new InMemoryTransport(new ConnectionId(22));
        var observer = new RecordingObserver();
        using HandshakeServerHarness harness = CreateHandshakeServerHarness(
            transport,
            observer,
            protocol,
            fingerprint);
        using World firstClientWorld = World.Create();
        var credentials = new MemoryCredentials();
        var firstClient = new ReplicatedClientNetworkRuntime(
            harness.Capacity,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 0.5f,
            reconnectWindowSeconds: 30f,
            protocol,
            fingerprint,
            credentials,
            new ClientBridgeFactory(firstClientWorld, entityCapacity: 2),
            observer);

        CompleteRuntimeHandshake(firstClient, harness.Server);
        Assert.That(credentials.TryLoad(out ClientSessionCredentials initial), Is.EqualTo(ClientCredentialLoadStatus.Loaded));
        Assert.That(transport.ClientHandshakeConfirmationCount, Is.EqualTo(1));
        transport.Disconnect();
        harness.Server.PumpTransport();
        firstClient.PumpTransport();

        credentials.FailStore = true;
        Assert.That(firstClient.TryConnectNow(), Is.True);
        firstClient.PumpTransport();
        harness.Server.PumpTransport();
        Assert.That(
            () => firstClient.PumpTransport(),
            Throws.TypeOf<NetworkRuntimeException>());
        Assert.Multiple(() =>
        {
            Assert.That(firstClient.IsFaulted, Is.True);
            Assert.That(observer.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.CredentialStoreFailed));
            Assert.That(transport.ClientHandshakeConfirmationCount, Is.EqualTo(1));
            Assert.That(credentials.TryLoad(out ClientSessionCredentials preserved), Is.EqualTo(ClientCredentialLoadStatus.Loaded));
            Assert.That(preserved, Is.EqualTo(initial));
            Assert.That(observer.SeatReconnections, Is.Zero);
        });

        transport.Disconnect();
        harness.Server.PumpTransport();
        Assert.That(
            ((IClientConnectionEventPort)transport).TryReceiveConnectionEvent(out ClientConnectionEvent abandonedDisconnect),
            Is.True);
        Assert.That(abandonedDisconnect.Kind, Is.EqualTo(TransportConnectionEventKind.Disconnected));
        credentials.FailStore = false;

        using World recoveredClientWorld = World.Create();
        var recoveredClient = new ReplicatedClientNetworkRuntime(
            harness.Capacity,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 0.5f,
            reconnectWindowSeconds: 30f,
            protocol,
            fingerprint,
            credentials,
            new ClientBridgeFactory(recoveredClientWorld, entityCapacity: 2),
            observer);
        CompleteRuntimeHandshake(recoveredClient, harness.Server);

        Assert.Multiple(() =>
        {
            Assert.That(recoveredClient.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));
            Assert.That(recoveredClient.Seat, Is.EqualTo(new SessionSeatBinding(0, 1, new PlayerId(1))));
            Assert.That(transport.ClientHandshakeConfirmationCount, Is.EqualTo(2));
            Assert.That(observer.InitialSeatConnections, Is.EqualTo(1));
            Assert.That(observer.SeatReconnections, Is.EqualTo(1));
            Assert.That(observer.Faults, Is.EqualTo(1));
            Assert.That(credentials.TryLoad(out ClientSessionCredentials rotated), Is.EqualTo(ClientCredentialLoadStatus.Loaded));
            Assert.That(rotated.SessionEpoch, Is.EqualTo(initial.SessionEpoch));
            Assert.That(rotated.ReconnectToken, Is.Not.EqualTo(initial.ReconnectToken));
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
            reconnectWindowSeconds: 30f,
            protocol,
            fingerprint,
            new MemoryCredentials(),
            new ClientBridgeFactory(world, 2),
            observer);

        transport.ConnectClientOnly();
        client.PumpTransport();
        var seat = new SessionSeatBinding(0, 1, new PlayerId(1));
        SessionHandshakeResponse response = SessionHandshakeResponse.Accept(
            in seat,
            new ReconnectToken(1, 2),
            protocol,
            fingerprint,
            new SessionEpoch(7),
            nextClientBatchSequence: 1);
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

    [TestCase(HandshakeRejectReason.StaleOrInvalidReconnectToken, 5UL)]
    [TestCase(HandshakeRejectReason.SessionEpochMismatch, 6UL)]
    public void ClientRuntime_RecoveryCredentialRejection_IsTerminalAndDoesNotScheduleFreshJoin(
        HandshakeRejectReason rejectReason,
        ulong responseEpoch)
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
            reconnectWindowSeconds: 30f,
            protocol,
            fingerprint,
            credentials,
            new ClientBridgeFactory(world, 2),
            observer);

        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        SessionHandshakeResponse rejected = SessionHandshakeResponse.Reject(
            rejectReason,
            protocol,
            fingerprint,
            new SessionEpoch(responseEpoch));
        Span<byte> payload = stackalloc byte[HandshakeWireCodec.ResponseSizeInBytes];
        Assert.That(HandshakeWireCodec.TryEncodeResponse(in rejected, payload, out int payloadBytes), Is.EqualTo(NetworkWireCodecStatus.Success));
        transport.EnqueueServerFrame(new ChannelId(0), NetworkWireKind.SessionHandshakeResponse, payload[..payloadBytes]);
        client.PumpTransport();

        Assert.Multiple(() =>
        {
            Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.RecoveryRejected));
            Assert.That(credentials.TryLoad(out ClientSessionCredentials preserved), Is.EqualTo(ClientCredentialLoadStatus.Loaded));
            Assert.That(preserved.SessionEpoch, Is.EqualTo(new SessionEpoch(5)));
            Assert.That(preserved.ReconnectToken, Is.EqualTo(new ReconnectToken(8, 9)));
            Assert.That(transport.State, Is.EqualTo(ClientConnectionControlState.Disconnected));
            Assert.That(observer.Faults, Is.Zero);
        });

        client.PumpReplicatedClient(0.5f);
        Assert.Multiple(() =>
        {
            Assert.That(transport.ConnectAttempts, Is.EqualTo(1));
            Assert.That(client.TryConnectNow(), Is.False);
            Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.RecoveryRejected));
        });
    }

    [Test]
    public void ClientRuntime_ServerEpochRestart_PreservesPriorSeatAndStopsInsteadOfChangingSides()
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
            reconnectWindowSeconds: 30f,
            protocol,
            fingerprint,
            credentials,
            factory,
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
                new SessionEpoch(5),
                nextClientBatchSequence: 1));
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
            Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.RecoveryRejected));
            Assert.That(client.Seat, Is.EqualTo(new SessionSeatBinding(0, 1, new PlayerId(1))));
            Assert.That(client.SessionEpoch, Is.EqualTo(new SessionEpoch(5)));
            Assert.That(world.Has<ReplicationMirrorIdentity>(authored), Is.True);
            Assert.That(world.Has<ReplicationMirrorState>(authored), Is.True);
            Assert.That(credentials.TryLoad(out ClientSessionCredentials preserved), Is.EqualTo(ClientCredentialLoadStatus.Loaded));
            Assert.That(preserved.SessionEpoch, Is.EqualTo(new SessionEpoch(5)));
            Assert.That(preserved.ReconnectToken, Is.EqualTo(new ReconnectToken(10, 11)));
        });

        client.PumpReplicatedClient(0.5f);
        Assert.Multiple(() =>
        {
            Assert.That(transport.ConnectAttempts, Is.EqualTo(2));
            Assert.That(client.TryConnectNow(), Is.False);
            Assert.That(factory.Bridge, Is.SameAs(oldBridge));
            Assert.That(observer.Faults, Is.Zero);
        });
    }

    [Test]
    public void ClientCommandStream_SameIdentityReconnectContinuesCursor_NewSeatGenerationStartsAtOne()
    {
        var capacity = Capacity();
        var transport = new InMemoryTransport(new ConnectionId(7));
        var observer = new RecordingObserver();
        var protocol = new ProtocolVersion(1, 0);
        ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes(new byte[] { 2, 4, 6 });
        using World world = World.Create();
        var factory = new ClientBridgeFactory(world, entityCapacity: 2);
        using var client = new ReplicatedClientNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 1f,
            reconnectWindowSeconds: 30f,
            protocol,
            fingerprint,
            new MemoryCredentials(),
            factory,
            observer);

        ConnectAndAccept(client, transport, protocol, fingerprint, seatGeneration: 1, tokenLow: 10, nextClientBatchSequence: 1);
        var actorHandle = new NetworkEntityHandle(0, 1);
        EnqueueFullReplication(transport, sessionEpoch: 9, tick: 1, snapshotId: 1, actorHandle, revision: 1);
        client.PumpTransport();
        client.PumpReplicatedClient(0f);
        Assert.That(factory.Bridge!.TryResolve(actorHandle, out Entity actor), Is.True);

        var schemas = new NetworkCommandSchemaRegistry();
        schemas.Register(new NetworkCommandSchema(
            TestOrderTypeId,
            NetworkCommandTargetKind.WorldPositionCm,
            allowArg0: false,
            allowArg1: false,
            NetworkCommandSubmitModeMask.Immediate | NetworkCommandSubmitModeMask.Queued,
            KnowledgePositionAccess.None));
        schemas.Freeze();
        var commands = new ReplicatedClientCommandPort(world, client, schemas, maxActorsPerBatch: 1);
        var order = new Order
        {
            Actor = actor,
            Target = Entity.Null,
            OrderTypeId = TestOrderTypeId,
            Args = OrderArgs.CreateSingleWorldCm(new System.Numerics.Vector3(25, 0, 50)),
            SubmitMode = OrderSubmitMode.Immediate,
        };

        Assert.That(commands.Submit(in order), Is.EqualTo(ReplicatedClientCommandSubmitResult.Submitted));
        Assert.That(transport.LastClientCommandBatchSequence, Is.EqualTo(1));
        Assert.That(commands.Submit(in order), Is.EqualTo(ReplicatedClientCommandSubmitResult.Submitted));
        Assert.That(transport.LastClientCommandBatchSequence, Is.EqualTo(2));

        transport.Disconnect();
        client.PumpTransport();
        ConnectAndAccept(client, transport, protocol, fingerprint, seatGeneration: 1, tokenLow: 20, nextClientBatchSequence: 3);
        EnqueueFullReplication(transport, sessionEpoch: 9, tick: 2, snapshotId: 2, actorHandle, revision: 2);
        client.PumpTransport();
        client.PumpReplicatedClient(0f);
        Assert.That(commands.Submit(in order), Is.EqualTo(ReplicatedClientCommandSubmitResult.Submitted));
        Assert.That(
            transport.LastClientCommandBatchSequence,
            Is.EqualTo(3),
            "A reconnect to the same epoch, seat slot, and generation must continue its command cursor.");

        transport.Disconnect();
        client.PumpTransport();
        ConnectAndAccept(client, transport, protocol, fingerprint, seatGeneration: 2, tokenLow: 30, nextClientBatchSequence: 1);
        EnqueueFullReplication(transport, sessionEpoch: 9, tick: 3, snapshotId: 3, actorHandle, revision: 3);
        client.PumpTransport();
        client.PumpReplicatedClient(0f);
        Assert.That(commands.Submit(in order), Is.EqualTo(ReplicatedClientCommandSubmitResult.Submitted));
        Assert.Multiple(() =>
        {
            Assert.That(client.CommandStreamIdentity.SeatGeneration, Is.EqualTo(2));
            Assert.That(commands.LastSubmittedBatchSequence, Is.EqualTo(1));
            Assert.That(transport.LastClientCommandBatchSequence, Is.EqualTo(1));
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
            reconnectWindowSeconds: 30f,
            protocol,
            fingerprint,
            new MemoryCredentials(),
            new ClientBridgeFactory(world, 2),
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
            reconnectWindowSeconds: 30f,
            protocol,
            fingerprint,
            new MemoryCredentials(),
            new ClientBridgeFactory(world, 2),
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
                new SessionEpoch(7),
                nextClientBatchSequence: 1));
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

    private static void ConnectAndAccept(
        ReplicatedClientNetworkRuntime client,
        InMemoryTransport transport,
        ProtocolVersion protocol,
        ContentFingerprint fingerprint,
        uint seatGeneration,
        ulong tokenLow,
        ulong nextClientBatchSequence)
    {
        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        EnqueueHandshakeResponse(
            transport,
            SessionHandshakeResponse.Accept(
                new SessionSeatBinding(0, seatGeneration, new PlayerId(1)),
                new ReconnectToken(tokenLow, checked(tokenLow + 1)),
                protocol,
                fingerprint,
                new SessionEpoch(9),
                nextClientBatchSequence));
        client.PumpTransport();
        Assert.That(client.State, Is.EqualTo(ReplicatedClientConnectionState.Connected));
    }

    private static ReplicatedClientNetworkRuntime CreateClientRuntime(
        World world,
        InMemoryTransport transport,
        RecordingObserver observer,
        ProtocolVersion protocol,
        ContentFingerprint fingerprint,
        out ClientBridgeFactory factory)
    {
        NetworkRuntimeCapacity capacity = Capacity();
        factory = new ClientBridgeFactory(world, entityCapacity: 2);
        return new ReplicatedClientNetworkRuntime(
            in capacity,
            transport,
            transport,
            transport,
            reconnectRetrySeconds: 1f,
            reconnectWindowSeconds: 30f,
            protocol,
            fingerprint,
            new MemoryCredentials(),
            factory,
            observer);
    }

    private static void EnqueueFullReplication(
        InMemoryTransport transport,
        ulong sessionEpoch,
        uint tick,
        ulong snapshotId,
        NetworkEntityHandle actor,
        uint revision)
    {
        byte[] payload = new byte[
            ReplicationPacketWireCodec.HeaderSizeInBytes + ReplicationPacketWireCodec.UpsertSizeInBytes];
        int offset = 0;
        payload[offset++] = (byte)ReplicationPacketKind.Full;
        offset += 3;
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(offset, 8), sessionEpoch);
        offset += 8;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset, 4), tick);
        offset += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(offset, 8), snapshotId);
        offset += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(offset, 8), 0);
        offset += 8;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset, 2), 1);
        offset += 2;
        offset += 6;

        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset, 4), actor.Slot);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset, 4), actor.Generation);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset, 4), 1);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset, 4), revision);
        offset += 4;
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(offset, 8), revision);
        offset += 8;
        offset += 24;

        Assert.That(offset, Is.EqualTo(payload.Length));
        transport.EnqueueServerFrame(
            new ChannelId(2),
            NetworkWireKind.ReplicationPacket,
            payload);
    }

    private static void CompleteRuntimeHandshake(
        ReplicatedClientNetworkRuntime client,
        AuthoritativeServerNetworkRuntime server)
    {
        Assert.That(client.TryConnectNow(), Is.True);
        client.PumpTransport();
        server.PumpTransport();
        client.PumpTransport();
        server.PumpTransport();
        client.PumpTransport();
    }

    private static HandshakeServerHarness CreateHandshakeServerHarness(
        InMemoryTransport transport,
        RecordingObserver observer,
        ProtocolVersion protocol,
        ContentFingerprint fingerprint)
    {
        World serverWorld = World.Create();
        try
        {
            Entity player = serverWorld.Create(new PlayerIdentity { PlayerId = 1 });
            Entity first = serverWorld.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 10));
            Entity second = serverWorld.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 20));
            CommandHarness commandHarness = CreateCommandHarness(serverWorld, player, first, second);
            commandHarness.Knowledge.Upsert(player, first, VisibleDisclosure());
            commandHarness.Knowledge.Upsert(player, second, VisibleDisclosure());

            var projectors = new ReplicationSchemaProjectorRegistry(schemaCapacity: 1);
            Assert.That(
                projectors.Register(1, new TestProjector()),
                Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
            projectors.Freeze();
            var bridge = new AuthoritativeWorldReplicationBridge(
                serverWorld,
                commandHarness.Entities,
                commandHarness.Knowledge,
                player,
                projectors,
                entityCapacity: 2);
            var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 32);
            var replicationSeat = new AuthoritativeReplicationSeatRuntime(
                seatSlot: 0,
                playerId: new PlayerId(1),
                bridge,
                new AuthoritativeReplicationChannel(entityCapacity: 2, baselineCapacity: 4, disclosureLog),
                disclosureLog,
                new ReplicationProjectionBuffer(entityCapacity: 2),
                new ReplicationPacketBuffer(entityCapacity: 2));
            NetworkRuntimeCapacity capacity = Capacity();
            var sessions = new AuthoritativeSessionRegistry(
                seatCapacity: 1,
                new SessionEpoch(77),
                protocol,
                fingerprint,
                reconnectWindowTicks: 30,
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
                new FixedReplicationInput(commandHarness.FirstHandle, commandHarness.SecondHandle),
                new[] { replicationSeat },
                observer);
            return new HandshakeServerHarness(serverWorld, in capacity, server);
        }
        catch
        {
            serverWorld.Dispose();
            throw;
        }
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
        snapshotAcknowledgementTimeoutTicks: 1,
        commandCorrelationCapacity: 16,
        controlChannel: new ChannelId(0),
        commandChannel: new ChannelId(1),
        stateChannel: new ChannelId(2),
        simulationTickRateHz: 1,
        maxFutureTargetTicks: 2);

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
            NetworkCommandSubmitModeMask.Immediate | NetworkCommandSubmitModeMask.Queued,
            KnowledgePositionAccess.None));
        schemas.Freeze();
        var orders = new OrderQueue(capacity: 8);
        var entityResults = new OrderAdmissionResultBuffer(capacity: 8);
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
        return new CommandHarness(
            entities,
            knowledge,
            orders,
            results,
            entityResults,
            ingress,
            gameplayGate,
            firstHandle,
            secondHandle);
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

    private sealed class HandshakeServerHarness : IDisposable
    {
        private readonly World _serverWorld;

        public HandshakeServerHarness(
            World serverWorld,
            in NetworkRuntimeCapacity capacity,
            AuthoritativeServerNetworkRuntime server)
        {
            _serverWorld = serverWorld;
            Capacity = capacity;
            Server = server;
        }

        public NetworkRuntimeCapacity Capacity { get; }

        public AuthoritativeServerNetworkRuntime Server { get; }

        public void Dispose() => _serverWorld.Dispose();
    }

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
        private readonly KnowledgeProjectionStore _knowledge;
        private readonly Entity _viewer;

        public ClientBridgeFactory(World world, int entityCapacity)
        {
            _world = world;
            _entityCapacity = entityCapacity;
            _knowledge = new KnowledgeProjectionStore(initialCapacity: entityCapacity);
            _viewer = world.Create();
        }

        public ClientWorldReplicationBridge? Bridge { get; private set; }

        public ClientWorldReplicationBridge Create(ulong sessionEpoch)
        {
            var appliers = new ClientReplicationSchemaApplierRegistry(schemaCapacity: 1);
            Assert.That(appliers.Register(1, new TestApplier()), Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
            appliers.Freeze();
            Bridge = new ClientWorldReplicationBridge(
                _world,
                _entityCapacity,
                sessionEpoch,
                appliers,
                _knowledge,
                _viewer);
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

        public bool FailStore { get; set; }

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
            if (FailStore)
            {
                return false;
            }

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
        public NetworkCommandAdmissionResultBuffer ClientAdmissions { get; } = new(capacity: 64);
        public int Faults { get; private set; }
        public int InitialSeatConnections { get; private set; }
        public int SeatReconnections { get; private set; }
        public int SeatDisconnections { get; private set; }
        public int SeatReleases { get; private set; }
        public NetworkRuntimeFault LastFault { get; private set; }
        public int ClientHandshakes { get; private set; }
        public HandshakeRejectReason LastHandshakeRejectReason { get; private set; }
        public int ClientRoomSnapshots { get; private set; }
        public int ServerRoomSnapshots { get; private set; }
        public int ClientResyncs { get; private set; }
        public NetworkRoomSnapshotHeader LastRoomSnapshot { get; private set; }
        public NetworkResyncRequired LastClientResync { get; private set; }

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
        public void OnClientAdmission(in NetworkCommandAdmissionOutcome outcome)
        {
            Assert.That(ClientAdmissions.TryWrite(in outcome), Is.True);
        }
        public void OnClientResyncRequired(in NetworkResyncRequired message)
        {
            ClientResyncs++;
            LastClientResync = message;
        }
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
        private bool _dropNextServerDatagram;
        private ChannelId _dropServerChannel;
        private NetworkWireKind _dropServerKind;
        private bool _dropNextClientDatagram;
        private ChannelId _dropClientChannel;
        private NetworkWireKind _dropClientKind;

        public InMemoryTransport(ConnectionId connection) => _connection = connection;

        public int ServerSnapshotFragmentCount { get; private set; }
        public int ServerReplicationPacketSendCount { get; private set; }
        public int DroppedServerDatagrams { get; private set; }
        public int DroppedClientDatagrams { get; private set; }
        public ChannelId LastServerSnapshotFragmentChannel { get; private set; }
        public int ClientCommandFragmentCount { get; private set; }
        public int ClientHandshakeConfirmationCount { get; private set; }
        public ulong LastClientCommandBatchSequence { get; private set; }
        public int ConnectAttempts { get; private set; }
        public ClientConnectionControlState State { get; private set; }
        public int RoundTripTimeMilliseconds => State == ClientConnectionControlState.Connected ? 24 : 0;

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

        public void EnqueueClientFrame(ChannelId channel, NetworkWireKind kind, ReadOnlySpan<byte> payload)
        {
            byte[] framed = new byte[NetworkWireEnvelopeCodec.GetFramedLength(payload.Length)];
            Assert.That(NetworkWireEnvelopeCodec.TryEncode(kind, payload, framed, out _), Is.EqualTo(NetworkWireCodecStatus.Success));
            _serverInbound.Enqueue(new Frame(channel, framed));
        }

        public void DropNextServerDatagram(ChannelId channel, NetworkWireKind kind)
        {
            Assert.That(_dropNextServerDatagram, Is.False);
            _dropNextServerDatagram = true;
            _dropServerChannel = channel;
            _dropServerKind = kind;
        }

        public void DropNextClientDatagram(ChannelId channel, NetworkWireKind kind)
        {
            Assert.That(_dropNextClientDatagram, Is.False);
            _dropNextClientDatagram = true;
            _dropClientChannel = channel;
            _dropClientKind = kind;
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
            bool decoded = TryGetKind(copy, out NetworkWireKind kind);
            if (decoded && kind == NetworkWireKind.ReplicationPacket)
            {
                ServerReplicationPacketSendCount++;
            }

            if (_dropNextServerDatagram && channelId == _dropServerChannel && decoded && kind == _dropServerKind)
            {
                _dropNextServerDatagram = false;
                DroppedServerDatagrams++;
                return DatagramSendStatus.Sent;
            }

            _clientInbound.Enqueue(new Frame(channelId, copy));
            if (decoded && kind == NetworkWireKind.SnapshotFragment)
            {
                ServerSnapshotFragmentCount++;
                LastServerSnapshotFragmentChannel = channelId;
            }

            return DatagramSendStatus.Sent;
        }

        public DatagramSendStatus TrySend(ChannelId channelId, ReadOnlySpan<byte> payload)
        {
            byte[] copy = payload.ToArray();
            bool decoded = TryGetKind(copy, out NetworkWireKind kind);
            if (decoded && kind == NetworkWireKind.SessionHandshakeConfirmation)
            {
                ClientHandshakeConfirmationCount++;
            }
            else if (decoded && kind == NetworkWireKind.CommandFragment)
            {
                ClientCommandFragmentCount++;
                Assert.That(
                    NetworkWireEnvelopeCodec.TryDecode(
                        copy,
                        out _,
                        out ReadOnlySpan<byte> fragmentPayload),
                    Is.EqualTo(NetworkWireCodecStatus.Success));
                Assert.That(
                    CommandFragmentWireCodec.TryDecode(
                        fragmentPayload,
                        out NetworkCommandFragmentHeader header,
                        out _),
                    Is.EqualTo(NetworkWireCodecStatus.Success));
                LastClientCommandBatchSequence = header.ClientBatchSequence;
            }

            if (_dropNextClientDatagram &&
                channelId == _dropClientChannel &&
                decoded &&
                kind == _dropClientKind)
            {
                _dropNextClientDatagram = false;
                DroppedClientDatagrams++;
                return DatagramSendStatus.Sent;
            }

            _serverInbound.Enqueue(new Frame(channelId, copy));

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
