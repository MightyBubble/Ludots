using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Knowledge;
using Ludots.Core.Layers;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Simulation;
using Ludots.Core.Physics3D;
using Ludots.Core.Physics3DNet.Bridge;
using Ludots.Core.Physics3DNet.Input;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
public sealed class Physics3DNetworkBridgeTests
{
    private const int SchemaId = 41;
    private const ulong SessionEpoch = 99;

    [Test]
    public void PlayerLifecycle_SameGenerationReconnectReusesEntity_StaleGenerationIsRejected()
    {
        using World ecs = World.Create();
        using var physics = new Physics3DWorld(CreateWorldConfig(mobileCapacity: 2));
        var entities = new NetworkEntityTable(capacity: 2);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 4);
        using var lifecycle = CreateLifecycle(ecs, physics, entities, knowledge, seatCapacity: 2);
        SessionSeatBinding first = Seat(slot: 0, generation: 1);

        Assert.That(lifecycle.TryResolveController(in first, out Entity initial), Is.True);
        lifecycle.OnSeatConnected(in first, reconnected: false);
        lifecycle.OnSeatDisconnected(in first);
        Assert.That(lifecycle.TryResolveController(in first, out Entity reconnected), Is.True);
        lifecycle.OnSeatConnected(in first, reconnected: true);

        SessionSeatBinding staleReplacement = Seat(slot: 0, generation: 2);
        Assert.Multiple(() =>
        {
            Assert.That(reconnected, Is.EqualTo(initial));
            Assert.That(lifecycle.TryResolveController(in staleReplacement, out _), Is.False);
            Assert.That(lifecycle.LastFailure, Is.EqualTo(Physics3DNetworkPlayerLifecycleFailure.GenerationMismatch));
            Assert.That(physics.ActiveMobileBodyCount, Is.EqualTo(1));
            Assert.That(entities.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void PlayerLifecycle_NetworkCapacityFailureRollsBackBodyAndEntity_ThenNewGenerationReusesRetiredSlot()
    {
        using World ecs = World.Create();
        using var physics = new Physics3DWorld(CreateWorldConfig(mobileCapacity: 2));
        var entities = new NetworkEntityTable(capacity: 1);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 4);
        using var lifecycle = CreateLifecycle(ecs, physics, entities, knowledge, seatCapacity: 2);
        SessionSeatBinding first = Seat(slot: 0, generation: 1);
        SessionSeatBinding second = Seat(slot: 1, generation: 1);

        Assert.That(lifecycle.TryResolveController(in first, out Entity firstEntity), Is.True);
        lifecycle.OnSeatConnected(in first, reconnected: false);
        Assert.That(lifecycle.TryGetNetworkHandle(in first, out NetworkEntityHandle firstHandle), Is.True);
        Assert.That(lifecycle.TryResolveController(in second, out _), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(lifecycle.LastFailure, Is.EqualTo(Physics3DNetworkPlayerLifecycleFailure.NetworkEntityCapacityExceeded));
            Assert.That(physics.ActiveMobileBodyCount, Is.EqualTo(1));
            Assert.That(entities.Count, Is.EqualTo(1));
            Assert.That(CountPlayers(ecs), Is.EqualTo(1));
        });

        lifecycle.OnSeatDisconnected(in first);
        Assert.That(lifecycle.TryRelease(in first), Is.True);
        Assert.That(ecs.IsAlive(firstEntity), Is.False);

        SessionSeatBinding next = Seat(slot: 0, generation: 2);
        Assert.That(lifecycle.TryResolveController(in next, out _), Is.True);
        Assert.That(lifecycle.TryGetNetworkHandle(in next, out NetworkEntityHandle nextHandle), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(nextHandle.Slot, Is.EqualTo(firstHandle.Slot));
            Assert.That(nextHandle.Generation, Is.GreaterThan(firstHandle.Generation));
            Assert.That(physics.ActiveMobileBodyCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void AoiInterest_EnteringAndLeavingRadiusBuildsRevealThenConceal()
    {
        using World ecs = World.Create();
        using var physics = new Physics3DWorld(CreateWorldConfig(mobileCapacity: 2));
        var entities = new NetworkEntityTable(capacity: 4);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 8);
        using var lifecycle = CreateLifecycle(
            ecs,
            physics,
            entities,
            knowledge,
            seatCapacity: 2,
            spawnSpacingCm: 1_000f);
        SessionSeatBinding viewerSeat = Seat(slot: 0, generation: 1);
        SessionSeatBinding targetSeat = Seat(slot: 1, generation: 1);
        Assert.That(lifecycle.TryResolveController(in viewerSeat, out Entity viewer), Is.True);
        Assert.That(lifecycle.TryResolveController(in targetSeat, out Entity target), Is.True);

        var projectors = CreateProjectors(physics);
        var factory = new Physics3DAuthoritativeReplicationSeatRuntimeFactory(
            ecs,
            entities,
            knowledge,
            projectors,
            seatCapacity: 2,
            replicationEntityCapacityPerSeat: 4,
            baselineCapacity: 4,
            disclosureChangeLogCapacity: 16);
        Assert.That(factory.TryAcquire(in viewerSeat, viewer, out AuthoritativeReplicationSeatRuntime? runtime), Is.True);
        Assert.That(runtime, Is.Not.Null);
        var interest = new Physics3DNetworkAoiInterestPort(
            ecs,
            physics,
            entities,
            lifecycle,
            knowledge,
            replicationEntityCapacityPerSeat: 4,
            new Physics3DNetworkAoiConfig { RadiusCm = 200f, GlobalEntityCapacity = 4 });
        Span<NetworkEntityHandle> handles = stackalloc NetworkEntityHandle[4];

        Assert.That(interest.TryCopyInterest(in viewerSeat, handles, out int farCount), Is.True);
        Assert.That(farCount, Is.EqualTo(1));
        Assert.That(
            runtime!.Bridge.BuildFull(
                runtime.Channel,
                SessionEpoch,
                tick: 1,
                snapshotId: 1,
                handles[..farCount],
                runtime.Projection,
                runtime.Packet),
            Is.EqualTo(ReplicationBridgeResult.Success));

        MoveEntity(ecs, physics, target, new Vector3(100f, 0f, 0f));
        Assert.That(interest.TryCopyInterest(in viewerSeat, handles, out int nearCount), Is.True);
        Assert.That(nearCount, Is.EqualTo(2));
        Assert.That(
            runtime.Bridge.BuildDelta(
                runtime.Channel,
                SessionEpoch,
                tick: 2,
                snapshotId: 2,
                acknowledgedBaselineId: 1,
                handles[..nearCount],
                runtime.Projection,
                runtime.Packet),
            Is.EqualTo(ReplicationBridgeResult.Success));
        Assert.That(runtime.Packet.DisclosureChanges.ToArray(), Has.Some.Matches<ReplicationDisclosureChange>(
            change => change.Kind == ReplicationDisclosureChangeKind.Reveal));

        MoveEntity(ecs, physics, target, new Vector3(1_000f, 0f, 0f));
        Assert.That(interest.TryCopyInterest(in viewerSeat, handles, out int leftCount), Is.True);
        Assert.That(leftCount, Is.EqualTo(1));
        Assert.That(
            runtime.Bridge.BuildDelta(
                runtime.Channel,
                SessionEpoch,
                tick: 3,
                snapshotId: 3,
                acknowledgedBaselineId: 2,
                handles[..leftCount],
                runtime.Projection,
                runtime.Packet),
            Is.EqualTo(ReplicationBridgeResult.Success));
        Assert.That(runtime.Packet.DisclosureChanges.ToArray(), Has.Some.Matches<ReplicationDisclosureChange>(
            change => change.Kind == ReplicationDisclosureChangeKind.Conceal));
    }

    [Test]
    public void AoiInterest_UsesPhysicsBodyPoseWhenEcsPoseIsStale()
    {
        using World ecs = World.Create();
        using var physics = new Physics3DWorld(CreateWorldConfig(mobileCapacity: 2));
        var entities = new NetworkEntityTable(capacity: 4);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 8);
        using var lifecycle = CreateLifecycle(
            ecs,
            physics,
            entities,
            knowledge,
            seatCapacity: 2,
            spawnSpacingCm: 1_000f);
        SessionSeatBinding viewerSeat = Seat(slot: 0, generation: 1);
        SessionSeatBinding targetSeat = Seat(slot: 1, generation: 1);
        Assert.That(lifecycle.TryResolveController(in viewerSeat, out _), Is.True);
        Assert.That(lifecycle.TryResolveController(in targetSeat, out Entity target), Is.True);
        using var interest = new Physics3DNetworkAoiInterestPort(
            ecs,
            physics,
            entities,
            lifecycle,
            knowledge,
            replicationEntityCapacityPerSeat: 4,
            new Physics3DNetworkAoiConfig { RadiusCm = 200f, GlobalEntityCapacity = 4 });
        Span<NetworkEntityHandle> destination = stackalloc NetworkEntityHandle[4];

        Physics3DPoseCm stalePose = ecs.Get<Physics3DPoseCm>(target);
        stalePose.Position = new Vector3(100f, 0f, 0f);
        ecs.Set(target, stalePose);
        Assert.That(interest.TryCopyInterest(in viewerSeat, destination, out int physicallyFarCount), Is.True);
        Assert.That(physicallyFarCount, Is.EqualTo(1));

        Physics3DBodyCm targetBody = ecs.Get<Physics3DBodyCm>(target);
        Physics3DBodyState physicalState = physics.GetBodyState(targetBody.Id);
        physicalState.PositionCm = stalePose.Position;
        physics.SetBodyState(targetBody.Id, in physicalState);
        Assert.That(interest.TryCopyInterest(in viewerSeat, destination, out int physicallyNearCount), Is.True);
        Assert.That(physicallyNearCount, Is.EqualTo(2));
    }

    [Test]
    public void AoiInterest_CapacityAndUnknownSeatFailWithoutPartialSuccess()
    {
        using World ecs = World.Create();
        using var physics = new Physics3DWorld(CreateWorldConfig(mobileCapacity: 2));
        var entities = new NetworkEntityTable(capacity: 2);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 4);
        using var lifecycle = CreateLifecycle(ecs, physics, entities, knowledge, seatCapacity: 2, spawnSpacingCm: 10f);
        SessionSeatBinding first = Seat(slot: 0, generation: 1);
        SessionSeatBinding second = Seat(slot: 1, generation: 1);
        Assert.That(lifecycle.TryResolveController(in first, out _), Is.True);
        Assert.That(lifecycle.TryResolveController(in second, out _), Is.True);
        using var perSeatInterest = new Physics3DNetworkAoiInterestPort(
            ecs,
            physics,
            entities,
            lifecycle,
            knowledge,
            replicationEntityCapacityPerSeat: 1,
            new Physics3DNetworkAoiConfig { RadiusCm = 100f, GlobalEntityCapacity = 2 });
        Span<NetworkEntityHandle> enoughDestination = stackalloc NetworkEntityHandle[2];
        Assert.That(
            perSeatInterest.TryCopyInterest(in first, enoughDestination, out int perSeatRequired),
            Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(perSeatRequired, Is.EqualTo(2));
            Assert.That(
                perSeatInterest.LastFailure,
                Is.EqualTo(Physics3DNetworkAoiFailure.PerSeatCapacityExceeded));
        });

        using var interest = new Physics3DNetworkAoiInterestPort(
            ecs,
            physics,
            entities,
            lifecycle,
            knowledge,
            replicationEntityCapacityPerSeat: 2,
            new Physics3DNetworkAoiConfig { RadiusCm = 100f, GlobalEntityCapacity = 2 });

        var tooSmall = new NetworkEntityHandle[1];
        Assert.Multiple(() =>
        {
            Assert.That(interest.TryCopyInterest(in first, tooSmall, out int required), Is.False);
            Assert.That(required, Is.EqualTo(2));
            Assert.That(interest.LastFailure, Is.EqualTo(Physics3DNetworkAoiFailure.DestinationCapacityExceeded));
            SessionSeatBinding unknown = Seat(slot: 0, generation: 9);
            Assert.That(interest.TryCopyInterest(in unknown, tooSmall, out _), Is.False);
            Assert.That(interest.LastFailure, Is.EqualTo(Physics3DNetworkAoiFailure.UnknownSeat));
        });

        Physics3DShapeId shape = physics.RegisterBoxShape(new Vector3(10f));
        Entity unregistered = ecs.Create();
        _ = physics.CreateBody(new Physics3DBodyDescription(
            unregistered,
            Physics3DBodyKind.Static,
            shape,
            Vector3.Zero,
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            0f,
            LayerMask.All,
            CreateBodyConfig().Material,
            Physics3DContinuousDetectionMode.Discrete));
        Assert.That(
            interest.TryCopyInterest(in first, enoughDestination, out _),
            Is.False);
        Assert.That(
            interest.LastFailure,
            Is.EqualTo(Physics3DNetworkAoiFailure.OverlapScratchCapacityExceeded));
    }

    [Test]
    public void Replication_QuantizesAuthoritativeBody_AndUsesOwnershipForLocalDynamicMirror()
    {
        using World serverEcs = World.Create();
        using var serverPhysics = new Physics3DWorld(CreateWorldConfig(mobileCapacity: 1));
        var entities = new NetworkEntityTable(capacity: 2);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 2);
        using var lifecycle = CreateLifecycle(serverEcs, serverPhysics, entities, knowledge, seatCapacity: 1);
        SessionSeatBinding seat = Seat(slot: 0, generation: 1);
        Assert.That(lifecycle.TryResolveController(in seat, out Entity serverEntity), Is.True);
        MoveEntity(serverEcs, serverPhysics, serverEntity, new Vector3(123.37f, 45.62f, -89.11f));
        Assert.That(lifecycle.TryGetNetworkHandle(in seat, out NetworkEntityHandle handle), Is.True);
        using var interestPort = new Physics3DNetworkAoiInterestPort(
            serverEcs,
            serverPhysics,
            entities,
            lifecycle,
            knowledge,
            replicationEntityCapacityPerSeat: 2,
            new Physics3DNetworkAoiConfig { RadiusCm = 100f, GlobalEntityCapacity = 2 });
        Span<NetworkEntityHandle> interest = stackalloc NetworkEntityHandle[2];
        Assert.That(interestPort.TryCopyInterest(in seat, interest, out int interestCount), Is.True);
        Assert.That(interestCount, Is.EqualTo(1));
        Assert.That(interest[0], Is.EqualTo(handle));

        var projectors = CreateProjectors(serverPhysics);
        var factory = new Physics3DAuthoritativeReplicationSeatRuntimeFactory(
            serverEcs,
            entities,
            knowledge,
            projectors,
            seatCapacity: 1,
            replicationEntityCapacityPerSeat: 2,
            baselineCapacity: 2,
            disclosureChangeLogCapacity: 4);
        Assert.That(factory.TryAcquire(in seat, serverEntity, out AuthoritativeReplicationSeatRuntime? runtime), Is.True);
        Assert.That(
            runtime!.Bridge.BuildFull(
                runtime.Channel,
                SessionEpoch,
                tick: 1,
                snapshotId: 1,
                interest[..interestCount],
                runtime.Projection,
                runtime.Packet),
            Is.EqualTo(ReplicationBridgeResult.Success));

        Assert.That(runtime.Packet.Upserts.Length, Is.EqualTo(1));
        Assert.That(
            runtime.Packet.Upserts[0].Ownership.Matches(in seat, Physics3DNetworkControlKinds.PlayerBody),
            Is.True);

        using World clientEcs = World.Create();
        using var clientPhysics = new Physics3DWorld(CreateWorldConfig(mobileCapacity: 1));
        var appliers = new ClientReplicationSchemaApplierRegistry(schemaCapacity: SchemaId);
        var applier = new Physics3DClientBodyReplicationApplier(
            clientPhysics,
            SchemaId,
            new Physics3DReplicationQuantizationConfig(),
            CreateBodyConfig());
        Assert.That(appliers.Register(SchemaId, applier), Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
        appliers.Freeze();
        var clientFactory = new ClientReplicationBridgeFactory(
            clientEcs,
            entities.Capacity,
            entities.Capacity,
            appliers);
        ClientWorldReplicationBridge client = clientFactory.Create(in seat, SessionEpoch);

        Assert.That(client.Apply(runtime.Packet), Is.EqualTo(ReplicationBridgeResult.Success));
        Assert.That(client.TryResolve(handle, out Entity mirror), Is.True);
        Assert.That(
            clientEcs.Get<Physics3DNetworkReplicatedBody>(mirror).Ownership,
            Is.EqualTo(runtime.Packet.Upserts[0].Ownership));
        Physics3DBodyCm clientBody = clientEcs.Get<Physics3DBodyCm>(mirror);
        Physics3DPoseCm clientPose = clientEcs.Get<Physics3DPoseCm>(mirror);
        Assert.Multiple(() =>
        {
            Assert.That(clientBody.Kind, Is.EqualTo(Physics3DBodyKind.Dynamic));
            Assert.That(clientPhysics.GetBodyKind(clientBody.Id), Is.EqualTo(Physics3DBodyKind.Dynamic));
            Assert.That(clientEcs.Get<Physics3DNetworkClientMirror>(mirror).IsLocallyControlled, Is.True);
            Assert.That(clientPose.Position.X, Is.EqualTo(123.5f).Within(0.001f));
            Assert.That(clientPose.Position.Y, Is.EqualTo(45.5f).Within(0.001f));
            Assert.That(clientPose.Position.Z, Is.EqualTo(-89f).Within(0.001f));
        });

        using World remoteEcs = World.Create();
        using var remotePhysics = new Physics3DWorld(CreateWorldConfig(mobileCapacity: 1));
        var remoteAppliers = new ClientReplicationSchemaApplierRegistry(schemaCapacity: SchemaId);
        Assert.That(
            remoteAppliers.Register(
                SchemaId,
                new Physics3DClientBodyReplicationApplier(
                    remotePhysics,
                    SchemaId,
                    new Physics3DReplicationQuantizationConfig(),
                    CreateBodyConfig())),
            Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
        remoteAppliers.Freeze();
        SessionSeatBinding remoteSeat = Seat(slot: 1, generation: 1);
        ClientWorldReplicationBridge remoteClient = new ClientReplicationBridgeFactory(
            remoteEcs,
            entities.Capacity,
            entities.Capacity,
            remoteAppliers).Create(in remoteSeat, SessionEpoch);
        Assert.That(remoteClient.Apply(runtime.Packet), Is.EqualTo(ReplicationBridgeResult.Success));
        Assert.That(remoteClient.TryResolve(handle, out Entity remoteMirror), Is.True);
        Physics3DBodyCm remoteBody = remoteEcs.Get<Physics3DBodyCm>(remoteMirror);
        Assert.Multiple(() =>
        {
            Assert.That(remoteBody.Kind, Is.EqualTo(Physics3DBodyKind.Kinematic));
            Assert.That(remotePhysics.GetBodyKind(remoteBody.Id), Is.EqualTo(Physics3DBodyKind.Kinematic));
            Assert.That(remoteEcs.Get<Physics3DNetworkClientMirror>(remoteMirror).IsLocallyControlled, Is.False);
        });

        var wrongEpoch = new ClientReplicationBridgeFactory(
                clientEcs,
                entities.Capacity,
                entities.Capacity,
                appliers)
            .Create(in seat, SessionEpoch + 1);
        Assert.That(wrongEpoch.Apply(runtime.Packet), Is.EqualTo(ReplicationBridgeResult.EpochMismatch));

        Assert.That(
            runtime.Bridge.BuildDelta(
                runtime.Channel,
                SessionEpoch,
                tick: 2,
                snapshotId: 2,
                acknowledgedBaselineId: 1,
                ReadOnlySpan<NetworkEntityHandle>.Empty,
                runtime.Projection,
                runtime.Packet),
            Is.EqualTo(ReplicationBridgeResult.Success));
        Assert.That(client.Apply(runtime.Packet), Is.EqualTo(ReplicationBridgeResult.Success));
        Assert.Multiple(() =>
        {
            Assert.That(client.TryResolve(handle, out _), Is.False);
            Assert.That(clientEcs.IsAlive(mirror), Is.False);
            Assert.That(clientPhysics.ActiveMobileBodyCount, Is.Zero);
        });
    }

    [TestCase(1, ReplicationBridgeResult.SchemaApplyRejected)]
    [TestCase(2, ReplicationBridgeResult.Success)]
    public void OwnershipTransfer_RequiresTemporaryMobileCapacity_AndNeverDestroysTheCurrentBody(
        int mobileCapacity,
        ReplicationBridgeResult expectedResult)
    {
        SessionSeatBinding localSeat = Seat(slot: 0, generation: 1);
        var handle = new NetworkEntityHandle(slot: 0, generation: 1);
        var localOwnership = new ReplicationControlOwnership(
            localSeat.Slot,
            localSeat.Generation,
            Physics3DNetworkControlKinds.PlayerBody);
        var bodyState = new Physics3DBodyState
        {
            PositionCm = new Vector3(100f, 200f, 300f),
            Orientation = Quaternion.Identity,
            LinearVelocityCmPerSecond = new Vector3(10f, 0f, 0f),
            AngularVelocityRadiansPerSecond = Vector3.Zero,
            Awake = true,
        };
        var quantization = new Physics3DReplicationQuantizationConfig();
        Assert.That(
            Physics3DReplicationStateCodec.TryEncode(
                in bodyState,
                Physics3DBodyKind.Dynamic,
                quantization,
                out ReplicationStateVector values),
            Is.True);

        var channel = new AuthoritativeReplicationChannel(
            new NetworkEntityTable(capacity: 1),
            replicationEntityCapacityPerSeat: 1,
            baselineCapacity: 2,
            new ReplicationDisclosureChangeLog(capacity: 2));
        var packet = new ReplicationPacketBuffer(entityCapacity: 1);
        var visible = new ReplicationDisclosureInput(handle, KnowledgePresence.LiveVisible);
        Assert.That(
            channel.BuildFull(
                SessionEpoch,
                tick: 1,
                snapshotId: 1,
                new[] { new ReplicatedEntityState(handle, SchemaId, 1, values, localOwnership) },
                new[] { visible },
                packet),
            Is.EqualTo(ReplicationBuildResult.Success));

        using World clientEcs = World.Create();
        using var clientPhysics = new Physics3DWorld(CreateWorldConfig(mobileCapacity));
        var appliers = new ClientReplicationSchemaApplierRegistry(schemaCapacity: SchemaId);
        Assert.That(
            appliers.Register(
                SchemaId,
                new Physics3DClientBodyReplicationApplier(
                    clientPhysics,
                    SchemaId,
                    quantization,
                    CreateBodyConfig())),
            Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
        appliers.Freeze();
        ClientWorldReplicationBridge client = new ClientReplicationBridgeFactory(
            clientEcs,
            1,
            1,
            appliers).Create(in localSeat, SessionEpoch);
        Assert.That(client.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
        Assert.That(client.TryResolve(handle, out Entity mirror), Is.True);
        Physics3DBodyId originalBody = clientEcs.Get<Physics3DBodyCm>(mirror).Id;

        Assert.That(
            channel.BuildDelta(
                SessionEpoch,
                tick: 2,
                snapshotId: 2,
                acknowledgedBaselineId: 1,
                new[]
                {
                    new ReplicatedEntityState(
                        handle,
                        SchemaId,
                        revision: 2,
                        values,
                        ReplicationControlOwnership.Unowned),
                },
                new[] { visible },
                packet),
            Is.EqualTo(ReplicationBuildResult.Success));
        Assert.That(client.Apply(packet), Is.EqualTo(expectedResult));

        Physics3DBodyCm committedBody = clientEcs.Get<Physics3DBodyCm>(mirror);
        if (expectedResult == ReplicationBridgeResult.Success)
        {
            Assert.Multiple(() =>
            {
                Assert.That(committedBody.Id, Is.Not.EqualTo(originalBody));
                Assert.That(committedBody.Kind, Is.EqualTo(Physics3DBodyKind.Kinematic));
                Assert.That(clientPhysics.ContainsBody(originalBody), Is.False);
                Assert.That(clientPhysics.ContainsBody(committedBody.Id), Is.True);
                Assert.That(clientPhysics.ActiveMobileBodyCount, Is.EqualTo(1));
                Assert.That(client.LastSnapshotId, Is.EqualTo(2));
                Assert.That(clientEcs.Get<Physics3DNetworkReplicatedBody>(mirror).Ownership.IsOwned, Is.False);
            });
        }
        else
        {
            Assert.Multiple(() =>
            {
                Assert.That(committedBody.Id, Is.EqualTo(originalBody));
                Assert.That(committedBody.Kind, Is.EqualTo(Physics3DBodyKind.Dynamic));
                Assert.That(clientPhysics.ContainsBody(originalBody), Is.True);
                Assert.That(clientPhysics.ActiveMobileBodyCount, Is.EqualTo(1));
                Assert.That(client.LastSnapshotId, Is.EqualTo(1));
                Assert.That(
                    clientEcs.Get<Physics3DNetworkReplicatedBody>(mirror).Ownership,
                    Is.EqualTo(localOwnership));
            });
        }
    }

    [Test]
    public void HeadlessReplication_UsesFormalCodecAndEnforcesSchemaGenerationAndSession()
    {
        var quantization = new Physics3DReplicationQuantizationConfig();
        var state = new Physics3DBodyState
        {
            PositionCm = new Vector3(125.25f, 50f, -75.25f),
            Orientation = Quaternion.Identity,
            LinearVelocityCmPerSecond = new Vector3(10f, 0f, -5f),
            AngularVelocityRadiansPerSecond = Vector3.Zero,
            Awake = true,
        };
        Assert.That(
            Physics3DReplicationStateCodec.TryEncode(
                in state,
                Physics3DBodyKind.Dynamic,
                quantization,
                out ReplicationStateVector values),
            Is.True);

        using World world = World.Create();
        var applier = new Physics3DHeadlessReplicationApplier(SchemaId, quantization);
        SessionSeatBinding clientSeat = Seat(slot: 0, generation: 1);
        ReplicationControlOwnership ownership = ReplicationControlOwnership.Unowned;
        var firstHandle = new NetworkEntityHandle(slot: 0, generation: 1);
        var first = new ReplicatedEntityState(firstHandle, SchemaId, revision: 1, in values, in ownership);
        var createContext = new ReplicationApplyContext(
            in clientSeat,
            SessionEpoch,
            committedTick: 1,
            snapshotId: 1,
            ReplicationPacketKind.Full);
        var identity = new ReplicationMirrorIdentity(firstHandle);
        var mirrorState = new ReplicationMirrorState(SchemaId, revision: 1, in values, in ownership);

        Assert.That(applier.CanCreate(world, in first, in createContext), Is.True);
        Entity mirrorEntity = applier.Create(world, in identity, in mirrorState, in createContext);
        Physics3DHeadlessClientMirror mirror = world.Get<Physics3DHeadlessClientMirror>(mirrorEntity);
        Assert.Multiple(() =>
        {
            Assert.That(mirror.SessionEpoch, Is.EqualTo(SessionEpoch));
            Assert.That(mirror.LastCommittedTick, Is.EqualTo(1));
            Assert.That(mirror.AuthoritativeKind, Is.EqualTo(Physics3DBodyKind.Dynamic));
            Assert.That(mirror.State.PositionCm.X, Is.EqualTo(125.5f).Within(0.001f));
            Assert.That(world.Has<Physics3DBodyCm>(mirrorEntity), Is.False);
        });

        var polluted = new ReplicationStateVector(
            values.Value0,
            values.Value1,
            values.Value2,
            values.Value3 | long.MinValue);
        var invalid = new ReplicatedEntityState(firstHandle, SchemaId, revision: 2, in polluted, in ownership);
        var wrongSchema = new ReplicatedEntityState(firstHandle, SchemaId + 1, revision: 2, in values, in ownership);
        var staleGeneration = new ReplicatedEntityState(
            new NetworkEntityHandle(slot: 0, generation: 2),
            SchemaId,
            revision: 2,
            in values,
            in ownership);
        var updateContext = new ReplicationApplyContext(
            in clientSeat,
            SessionEpoch,
            committedTick: 2,
            snapshotId: 2,
            ReplicationPacketKind.Delta);
        var wrongSession = new ReplicationApplyContext(
            in clientSeat,
            SessionEpoch + 1,
            committedTick: 2,
            snapshotId: 2,
            ReplicationPacketKind.Delta);
        Assert.Multiple(() =>
        {
            Assert.That(applier.CanApply(world, mirrorEntity, in invalid, in updateContext), Is.False);
            Assert.That(applier.CanApply(world, mirrorEntity, in wrongSchema, in updateContext), Is.False);
            Assert.That(applier.CanApply(world, mirrorEntity, in staleGeneration, in updateContext), Is.False);
            Assert.That(applier.CanApply(world, mirrorEntity, in first, in wrongSession), Is.False);
            Assert.That(
                applier.CanRelease(world, mirrorEntity, ReplicationMirrorLeaveKind.Teardown, in wrongSession),
                Is.False);
        });

        applier.Apply(world, mirrorEntity, in first, in updateContext);
        mirror = world.Get<Physics3DHeadlessClientMirror>(mirrorEntity);
        Assert.That(mirror.LastCommittedTick, Is.EqualTo(2));
        applier.Release(world, mirrorEntity, ReplicationMirrorLeaveKind.Teardown, in updateContext);
        Assert.Multiple(() =>
        {
            Assert.That(world.Has<Physics3DHeadlessClientMirror>(mirrorEntity), Is.False);
            Assert.That(world.Has<ReplicationSchemaRef>(mirrorEntity), Is.False);
        });
    }

    [Test]
    public void HeadlessReplication_DecodesStaticKinematicDynamic_AllowsSameGenerationKindChange_AndNeverCreatesPhysicsBodies()
    {
        var quantization = new Physics3DReplicationQuantizationConfig();
        using World world = World.Create();
        var applier = new Physics3DHeadlessReplicationApplier(SchemaId, quantization);
        SessionSeatBinding clientSeat = Seat(slot: 0, generation: 1);
        ReplicationControlOwnership ownership = ReplicationControlOwnership.Unowned;
        var context = new ReplicationApplyContext(
            in clientSeat,
            SessionEpoch,
            committedTick: 1,
            snapshotId: 1,
            ReplicationPacketKind.Full);

        Entity[] mirrors = new Entity[3];
        Physics3DBodyKind[] kinds =
        {
            Physics3DBodyKind.Static,
            Physics3DBodyKind.Kinematic,
            Physics3DBodyKind.Dynamic,
        };
        for (int i = 0; i < kinds.Length; i++)
        {
            Physics3DBodyState bodyState = CreateBodyState(positionX: 10f * (i + 1));
            Assert.That(
                Physics3DReplicationStateCodec.TryEncode(
                    in bodyState,
                    kinds[i],
                    quantization,
                    out ReplicationStateVector values),
                Is.True);
            var kindHandle = new NetworkEntityHandle(slot: i, generation: 1);
            var state = new ReplicatedEntityState(kindHandle, SchemaId, revision: 1, in values, in ownership);
            Assert.That(applier.CanCreate(world, in state, in context), Is.True);
            var identity = new ReplicationMirrorIdentity(kindHandle);
            var mirrorState = new ReplicationMirrorState(SchemaId, revision: 1, in values, in ownership);
            mirrors[i] = applier.Create(world, in identity, in mirrorState, in context);
            Physics3DHeadlessClientMirror mirror = world.Get<Physics3DHeadlessClientMirror>(mirrors[i]);
            Assert.Multiple(() =>
            {
                Assert.That(mirror.AuthoritativeKind, Is.EqualTo(kinds[i]));
                Assert.That(world.Has<Physics3DBodyCm>(mirrors[i]), Is.False);
            });
        }

        Physics3DBodyState staticBody = CreateBodyState(positionX: 77f);
        Assert.That(
            Physics3DReplicationStateCodec.TryEncode(
                in staticBody,
                Physics3DBodyKind.Static,
                quantization,
                out ReplicationStateVector staticValues),
            Is.True);
        var dynamicHandle = new NetworkEntityHandle(slot: 2, generation: 1);
        var kindChange = new ReplicatedEntityState(
            dynamicHandle,
            SchemaId,
            revision: 2,
            in staticValues,
            in ownership);
        var deltaContext = new ReplicationApplyContext(
            in clientSeat,
            SessionEpoch,
            committedTick: 2,
            snapshotId: 2,
            ReplicationPacketKind.Delta);
        Assert.That(applier.CanApply(world, mirrors[2], in kindChange, in deltaContext), Is.True);
        applier.Apply(world, mirrors[2], in kindChange, in deltaContext);
        Physics3DHeadlessClientMirror updated = world.Get<Physics3DHeadlessClientMirror>(mirrors[2]);
        Assert.Multiple(() =>
        {
            Assert.That(updated.AuthoritativeKind, Is.EqualTo(Physics3DBodyKind.Static));
            Assert.That(updated.State.PositionCm.X, Is.EqualTo(77f).Within(0.001f));
            Assert.That(world.Has<Physics3DBodyCm>(mirrors[2]), Is.False);
        });

        var polluted = new ReplicationStateVector(
            staticValues.Value0,
            staticValues.Value1,
            staticValues.Value2,
            staticValues.Value3 | long.MinValue);
        var invalidKind = new ReplicatedEntityState(
            dynamicHandle,
            SchemaId,
            revision: 3,
            in polluted,
            in ownership);
        Assert.That(applier.CanApply(world, mirrors[2], in invalidKind, in deltaContext), Is.False);
        Assert.That(applier.CanCreate(world, in invalidKind, in deltaContext), Is.False);

        applier.Release(world, mirrors[0], ReplicationMirrorLeaveKind.Conceal, in deltaContext);
        applier.Release(world, mirrors[1], ReplicationMirrorLeaveKind.Removal, in deltaContext);
        applier.Release(world, mirrors[2], ReplicationMirrorLeaveKind.Teardown, in deltaContext);
        Assert.Multiple(() =>
        {
            Assert.That(world.Has<Physics3DHeadlessClientMirror>(mirrors[0]), Is.False);
            Assert.That(world.Has<Physics3DHeadlessClientMirror>(mirrors[1]), Is.False);
            Assert.That(world.Has<Physics3DHeadlessClientMirror>(mirrors[2]), Is.False);
        });
    }

    private static Physics3DBodyState CreateBodyState(float positionX) => new()
    {
        PositionCm = new Vector3(positionX, 0f, 0f),
        Orientation = Quaternion.Identity,
        LinearVelocityCmPerSecond = Vector3.Zero,
        AngularVelocityRadiansPerSecond = Vector3.Zero,
        Awake = true,
    };

    [Test]
    public void AuthoritativeProjector_RejectsUnknownEntityAndOutOfRangeQuantizedState()
    {
        using World ecs = World.Create();
        using var physics = new Physics3DWorld(CreateWorldConfig(mobileCapacity: 1));
        var entities = new NetworkEntityTable(capacity: 2);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 2);
        using var lifecycle = CreateLifecycle(ecs, physics, entities, knowledge, seatCapacity: 1);
        SessionSeatBinding seat = Seat(slot: 0, generation: 1);
        Assert.That(lifecycle.TryResolveController(in seat, out Entity player), Is.True);
        using var interestPort = new Physics3DNetworkAoiInterestPort(
            ecs,
            physics,
            entities,
            lifecycle,
            knowledge,
            replicationEntityCapacityPerSeat: 2,
            new Physics3DNetworkAoiConfig { RadiusCm = 100f, GlobalEntityCapacity = 2 });
        Span<NetworkEntityHandle> discovered = stackalloc NetworkEntityHandle[2];
        Assert.That(interestPort.TryCopyInterest(in seat, discovered, out int discoveredCount), Is.True);
        Assert.That(discoveredCount, Is.EqualTo(1));
        var projectors = CreateProjectors(physics);
        var bridge = new AuthoritativeWorldReplicationBridge(
            ecs,
            entities,
            knowledge,
            player,
            projectors,
            replicationEntityCapacityPerSeat: 2);
        var projection = new ReplicationProjectionBuffer(entityCapacity: 2);

        Span<NetworkEntityHandle> unknown = stackalloc NetworkEntityHandle[1]
        {
            new NetworkEntityHandle(slot: 1, generation: 1),
        };
        Assert.That(bridge.Project(unknown, currentTick: 1, projection), Is.EqualTo(ReplicationBridgeResult.EntityUnavailable));

        Assert.That(lifecycle.TryGetNetworkHandle(in seat, out NetworkEntityHandle playerHandle), Is.True);
        MoveEntity(ecs, physics, player, new Vector3(10_000_000f, 0f, 0f));
        Span<NetworkEntityHandle> known = stackalloc NetworkEntityHandle[1] { playerHandle };
        Assert.That(bridge.Project(known, currentTick: 1, projection), Is.EqualTo(ReplicationBridgeResult.ProjectionFailed));
    }

    [Test]
    public void ReplicationCodec_AcceptsExactSignedBounds_AndRejectsOverflowOrReservedBits()
    {
        var config = new Physics3DReplicationQuantizationConfig();
        var boundary = new Physics3DBodyState
        {
            PositionCm = new Vector3(-4_194_304f, 4_194_303.5f, 0f),
            Orientation = Quaternion.Identity,
            LinearVelocityCmPerSecond = new Vector3(-16_384f, 16_383.5f, 0f),
            AngularVelocityRadiansPerSecond = new Vector3(-32.768f, 32.767f, 0f),
            Awake = true,
        };

        Assert.That(
            Physics3DReplicationStateCodec.TryEncode(
                in boundary,
                Physics3DBodyKind.Dynamic,
                config,
                out ReplicationStateVector encoded),
            Is.True);
        Assert.That(
            Physics3DReplicationStateCodec.TryDecode(
                in encoded,
                config,
                out Physics3DBodyState decoded,
                out Physics3DBodyKind decodedKind),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(decodedKind, Is.EqualTo(Physics3DBodyKind.Dynamic));
            Assert.That(decoded.PositionCm, Is.EqualTo(boundary.PositionCm));
            Assert.That(decoded.LinearVelocityCmPerSecond, Is.EqualTo(boundary.LinearVelocityCmPerSecond));
            Assert.That(decoded.AngularVelocityRadiansPerSecond.X, Is.EqualTo(boundary.AngularVelocityRadiansPerSecond.X).Within(0.0001f));
            Assert.That(decoded.AngularVelocityRadiansPerSecond.Y, Is.EqualTo(boundary.AngularVelocityRadiansPerSecond.Y).Within(0.0001f));
        });

        Physics3DBodyState positionOverflow = boundary;
        positionOverflow.PositionCm.X = 4_194_304f;
        Physics3DBodyState linearVelocityOverflow = boundary;
        linearVelocityOverflow.LinearVelocityCmPerSecond.X = 16_384f;
        Physics3DBodyState angularVelocityOverflow = boundary;
        angularVelocityOverflow.AngularVelocityRadiansPerSecond.X = 32.768f;
        Assert.Multiple(() =>
        {
            Assert.That(
                Physics3DReplicationStateCodec.TryEncode(
                    in positionOverflow,
                    Physics3DBodyKind.Dynamic,
                    config,
                    out _),
                Is.False);
            Assert.That(
                Physics3DReplicationStateCodec.TryEncode(
                    in linearVelocityOverflow,
                    Physics3DBodyKind.Dynamic,
                    config,
                    out _),
                Is.False);
            Assert.That(
                Physics3DReplicationStateCodec.TryEncode(
                    in angularVelocityOverflow,
                    Physics3DBodyKind.Dynamic,
                    config,
                    out _),
                Is.False);
        });

        var polluted = new ReplicationStateVector(
            encoded.Value0,
            encoded.Value1,
            encoded.Value2,
            encoded.Value3 | long.MinValue);
        Assert.That(
            Physics3DReplicationStateCodec.TryDecode(
                in polluted,
                config,
                out _,
                out _),
            Is.False);
    }

    [Test]
    public void FixedInput_MissingPlayerIsAtomic_AndPresentInputMovesAt30Hz()
    {
        using World ecs = World.Create();
        using var physics = new Physics3DWorld(CreateWorldConfig(mobileCapacity: 2));
        var entities = new NetworkEntityTable(capacity: 2);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 4);
        using var lifecycle = CreateLifecycle(ecs, physics, entities, knowledge, seatCapacity: 2, spawnSpacingCm: 500f);
        SessionSeatBinding first = Seat(slot: 0, generation: 1);
        SessionSeatBinding second = Seat(slot: 1, generation: 1);
        Assert.That(lifecycle.TryResolveController(in first, out Entity firstPlayer), Is.True);
        Assert.That(lifecycle.TryResolveController(in second, out _), Is.True);
        lifecycle.OnSeatConnected(in first, reconnected: false);
        lifecycle.OnSeatConnected(in second, reconnected: false);

        var ticks = new AuthoritativeSimulationTickState();
        FixedInputProtocolConfig fixedConfig = CreateFixedInputConfig(seatCapacity: 2);
        var ingress = new AuthoritativeFixedInputIngress(in fixedConfig, ticks);
        ingress.BindSeat(in first);
        ingress.BindSeat(in second);
        Span<byte> payload = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        Assert.That(Physics3DFixedInputFrameCodec.TryEncode(new Vector2(1f, 0f), payload), Is.True);
        Admit(ingress, in first, tick: 1, payload);
        Admit(ingress, in second, tick: 1, payload);
        var consumer = new Physics3DAuthoritativeFixedInputConsumer(
            new Physics3DAuthoritativeFixedInputIngressSource(ingress),
            lifecycle,
            physics,
            new Physics3DNetworkMovementConfig
            {
                SchemaId = fixedConfig.SchemaId,
                MaximumSpeedCmPerSecond = 600f,
                MaximumAccelerationCmPerSecondSquared = 1_800f,
                VelocityResponsePerSecond = 20f,
            });

        ticks.Begin(1);
        Assert.That(consumer.TryConsume(1), Is.EqualTo(Physics3DFixedInputConsumeResult.Success));
        var simulation = new Physics3DSimulationSystem(ecs, physics, sourceFixedStepHz: 30, maximumPhysicsStepsPerSourceTick: 1);
        simulation.Update(1f / 30f);
        ticks.Commit(1);

        Span<byte> secondFrame = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        Assert.That(Physics3DFixedInputFrameCodec.TryEncode(new Vector2(1f, 0f), secondFrame), Is.True);
        Admit(ingress, in first, tick: 2, secondFrame);
        ticks.Begin(2);
        Assert.That(consumer.TryConsume(2), Is.EqualTo(Physics3DFixedInputConsumeResult.MissingAtDeadline));
        Assert.That(physics.PendingActuationCommandCount, Is.Zero);
        ticks.Commit(2);

        lifecycle.OnSeatDisconnected(in second);
        Admit(ingress, in first, tick: 3, secondFrame);
        ticks.Begin(3);
        Assert.That(consumer.TryConsume(3), Is.EqualTo(Physics3DFixedInputConsumeResult.Success));
        simulation.Update(1f / 30f);
        ticks.Commit(3);

        Physics3DPoseCm moved = ecs.Get<Physics3DPoseCm>(firstPlayer);
        Assert.Multiple(() =>
        {
            Assert.That(simulation.PhysicsStepsLastUpdate, Is.EqualTo(1));
            Assert.That(moved.Position.X, Is.GreaterThan(0f));
            Assert.That(moved.Position.Z, Is.EqualTo(0f).Within(0.01f));
        });
    }

    [Test]
    public void FixedInput_FirstConnectAndReconnectWaitForLeadThenFailStrictlyAfterActivation()
    {
        using World ecs = World.Create();
        using var physics = new Physics3DWorld(CreateWorldConfig(mobileCapacity: 1));
        var entities = new NetworkEntityTable(capacity: 1);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 1);
        using var lifecycle = CreateLifecycle(ecs, physics, entities, knowledge, seatCapacity: 1);
        SessionSeatBinding seat = Seat(slot: 0, generation: 1);
        Assert.That(lifecycle.TryResolveController(in seat, out _), Is.True);
        lifecycle.OnSeatConnected(in seat, reconnected: false);

        var ticks = new AuthoritativeSimulationTickState();
        FixedInputProtocolConfig fixedConfig = CreateFixedInputConfig(seatCapacity: 1);
        var ingress = new AuthoritativeFixedInputIngress(in fixedConfig, ticks);
        ingress.BindSeat(in seat);
        Span<byte> payload = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        Assert.That(Physics3DFixedInputFrameCodec.TryEncode(new Vector2(1f, 0f), payload), Is.True);
        Admit(ingress, in seat, tick: 3, payload);
        var consumer = CreateConsumer(ingress, lifecycle, physics, fixedConfig.SchemaId);

        AssertConsumeAndCommit(ticks, consumer, tick: 1, Physics3DFixedInputConsumeResult.Success);
        AssertConsumeAndCommit(ticks, consumer, tick: 2, Physics3DFixedInputConsumeResult.Success);
        AssertConsumeAndCommit(ticks, consumer, tick: 3, Physics3DFixedInputConsumeResult.Success);

        lifecycle.OnSeatDisconnected(in seat);
        ingress.RebindSeat(in seat);
        lifecycle.OnSeatConnected(in seat, reconnected: true);
        Assert.That(
            ingress.GetSeatActivationState(in seat, out _),
            Is.EqualTo(FixedInputSeatActivationState.AwaitingFirstInput));
        AssertConsumeAndCommit(ticks, consumer, tick: 4, Physics3DFixedInputConsumeResult.Success);
        Admit(ingress, in seat, tick: 6, payload);
        AssertConsumeAndCommit(ticks, consumer, tick: 5, Physics3DFixedInputConsumeResult.Success);
        AssertConsumeAndCommit(ticks, consumer, tick: 6, Physics3DFixedInputConsumeResult.Success);

        ticks.Begin(7);
        Assert.That(consumer.TryConsume(7), Is.EqualTo(Physics3DFixedInputConsumeResult.MissingAtDeadline));
        ticks.Commit(7);
    }

    [Test]
    public void LazyFixedInputSource_BindsOnceAndRejectsMissingOrReplacedIngress()
    {
        var ticks = new AuthoritativeSimulationTickState();
        FixedInputProtocolConfig config = CreateFixedInputConfig(seatCapacity: 1);
        AuthoritativeFixedInputIngress? published = null;
        var source = new Physics3DLazyAuthoritativeFixedInputSource(
            config.SeatCapacity,
            config.SchemaId,
            config.FramePayloadBytes,
            () => published);

        Assert.Throws<InvalidOperationException>(source.EnsureReady);
        published = new AuthoritativeFixedInputIngress(in config, ticks);
        Assert.DoesNotThrow(source.EnsureReady);
        Assert.That(source.IsBound, Is.True);
        Assert.DoesNotThrow(source.EnsureReady);
        published = new AuthoritativeFixedInputIngress(in config, ticks);
        Assert.Throws<InvalidOperationException>(source.EnsureReady);
    }

    [Test]
    public void AoiAndFixedInput_SteadyStateRemainZeroAllocationAfterWarmup()
    {
        using World ecs = World.Create();
        using var physics = new Physics3DWorld(CreateWorldConfig(mobileCapacity: 1));
        var entities = new NetworkEntityTable(capacity: 2);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 1);
        using var lifecycle = CreateLifecycle(ecs, physics, entities, knowledge, seatCapacity: 1);
        SessionSeatBinding seat = Seat(slot: 0, generation: 1);
        Assert.That(lifecycle.TryResolveController(in seat, out _), Is.True);
        lifecycle.OnSeatConnected(in seat, reconnected: false);

        var interest = new Physics3DNetworkAoiInterestPort(
            ecs,
            physics,
            entities,
            lifecycle,
            knowledge,
            replicationEntityCapacityPerSeat: 1,
            new Physics3DNetworkAoiConfig { RadiusCm = 100f, GlobalEntityCapacity = entities.Capacity });
        var handles = new NetworkEntityHandle[entities.Capacity];
        var ticks = new AuthoritativeSimulationTickState();
        FixedInputProtocolConfig fixedConfig = CreateFixedInputConfig(seatCapacity: 1);
        var ingress = new AuthoritativeFixedInputIngress(in fixedConfig, ticks);
        ingress.BindSeat(in seat);
        Span<byte> payload = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        Assert.That(Physics3DFixedInputFrameCodec.TryEncode(new Vector2(0.5f, -0.25f), payload), Is.True);
        Admit(ingress, in seat, tick: 1, payload);
        var consumer = new Physics3DAuthoritativeFixedInputConsumer(
            new Physics3DAuthoritativeFixedInputIngressSource(ingress),
            lifecycle,
            physics,
            new Physics3DNetworkMovementConfig
            {
                SchemaId = fixedConfig.SchemaId,
                MaximumSpeedCmPerSecond = 600f,
                MaximumAccelerationCmPerSecondSquared = 1_800f,
                VelocityResponsePerSecond = 20f,
            });

        ticks.Begin(1);
        Assert.That(interest.TryCopyInterest(in seat, handles, out int warmCount), Is.True);
        Assert.That(warmCount, Is.EqualTo(1));
        Assert.That(consumer.TryConsume(1), Is.EqualTo(Physics3DFixedInputConsumeResult.Success));
        for (int iteration = 0; iteration < 64; iteration++)
        {
            Assert.That(interest.TryCopyInterest(in seat, handles, out warmCount), Is.True);
        }

        _ = GC.GetAllocatedBytesForCurrentThread();
        bool aoiSucceeded = true;
        long aoiBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 64; iteration++)
        {
            aoiSucceeded &= interest.TryCopyInterest(in seat, handles, out int count) && count == 1;
        }

        long aoiAllocated = GC.GetAllocatedBytesForCurrentThread() - aoiBefore;
        bool inputSucceeded = true;
        long inputBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 6; iteration++)
        {
            inputSucceeded &= consumer.TryConsume(1) == Physics3DFixedInputConsumeResult.Success;
        }

        long inputAllocated = GC.GetAllocatedBytesForCurrentThread() - inputBefore;
        ticks.Commit(1);
        Assert.Multiple(() =>
        {
            Assert.That(aoiSucceeded, Is.True);
            Assert.That(inputSucceeded, Is.True);
            Assert.That(aoiAllocated, Is.Zero, $"Physics3D network AOI allocated {aoiAllocated} bytes after warmup.");
            Assert.That(inputAllocated, Is.Zero, $"Physics3D fixed input allocated {inputAllocated} bytes after warmup.");
        });
    }

    [Test]
    public void SeatRuntimeFactory_RequiresExactGenerationAndSingleRelease()
    {
        using World ecs = World.Create();
        using var physics = new Physics3DWorld(CreateWorldConfig(mobileCapacity: 1));
        Entity viewer = ecs.Create();
        var entities = new NetworkEntityTable(capacity: 2);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 1);
        var factory = new Physics3DAuthoritativeReplicationSeatRuntimeFactory(
            ecs,
            entities,
            knowledge,
            CreateProjectors(physics),
            seatCapacity: 1,
            replicationEntityCapacityPerSeat: 2,
            baselineCapacity: 2,
            disclosureChangeLogCapacity: 4);
        SessionSeatBinding seat = Seat(slot: 0, generation: 1);

        Assert.That(factory.TryAcquire(in seat, viewer, out AuthoritativeReplicationSeatRuntime? runtime), Is.True);
        Assert.That(factory.TryAcquire(in seat, viewer, out _), Is.False);
        SessionSeatBinding wrong = Seat(slot: 0, generation: 2);
        Assert.That(factory.TryRelease(in wrong, runtime!), Is.False);
        Assert.That(factory.TryRelease(in seat, runtime!), Is.True);
        Assert.That(factory.TryRelease(in seat, runtime!), Is.False);
        Assert.That(factory.TryAcquire(in seat, viewer, out _), Is.False);
        Assert.That(factory.LastFailure, Is.EqualTo(Physics3DReplicationSeatFactoryFailure.GenerationNotNewer));
    }

    [Test]
    public void SeatRuntimeFactory_RejectsMutableProjectorsThenAcquiresAfterHostFreeze()
    {
        using World ecs = World.Create();
        using var physics = new Physics3DWorld(CreateWorldConfig(mobileCapacity: 1));
        Entity viewer = ecs.Create();
        var entities = new NetworkEntityTable(capacity: 2);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 1);
        var projectors = new ReplicationSchemaProjectorRegistry(schemaCapacity: SchemaId);
        Assert.That(
            projectors.Register(
                SchemaId,
                new Physics3DBodyReplicationProjector(
                    physics,
                    SchemaId,
                    new Physics3DReplicationQuantizationConfig())),
            Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
        var factory = new Physics3DAuthoritativeReplicationSeatRuntimeFactory(
            ecs,
            entities,
            knowledge,
            projectors,
            seatCapacity: 1,
            replicationEntityCapacityPerSeat: 2,
            baselineCapacity: 2,
            disclosureChangeLogCapacity: 4);
        SessionSeatBinding seat = Seat(slot: 0, generation: 1);

        Assert.Multiple(() =>
        {
            Assert.That(factory.TryAcquire(in seat, viewer, out _), Is.False);
            Assert.That(
                factory.LastFailure,
                Is.EqualTo(Physics3DReplicationSeatFactoryFailure.ProjectorRegistryNotFrozen));
        });

        projectors.Freeze();
        Assert.That(
            factory.TryAcquire(in seat, viewer, out AuthoritativeReplicationSeatRuntime? runtime),
            Is.True);
        Assert.That(runtime, Is.Not.Null);
    }

    [Test]
    [NonParallelizable]
    public void AoiInterest_150SeatsAnd10KRegisteredBodies_ReportsBroadphaseTailLatencyAndZeroAllocation()
        => RunAoiBroadphaseScale(ordinaryBodyCount: 10_000, measuredRounds: 30);

    [Test]
    [Explicit("150-seat and 25K registered-body AOI broadphase pressure measurement.")]
    [NonParallelizable]
    public void AoiInterest_150SeatsAnd25KRegisteredBodies_ReportsBroadphaseTailLatencyAndZeroAllocation()
        => RunAoiBroadphaseScale(ordinaryBodyCount: 25_000, measuredRounds: 10);

    private static void RunAoiBroadphaseScale(int ordinaryBodyCount, int measuredRounds)
    {
        const int seatCount = 150;
        const int globalEntityCapacity = 100_000;
        const int replicationEntityCapacityPerSeat = 512;
        const int fixedStepHz = 30;
        const int clusterColumns = 15;
        const float clusterSpacingCm = 10_000f;
        const float interestRadiusCm = 1_000f;
        const int warmupRounds = 8;
        if (ordinaryBodyCount <= 0 || ordinaryBodyCount > globalEntityCapacity - seatCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ordinaryBodyCount),
                ordinaryBodyCount,
                $"Ordinary body count must be between 1 and {globalEntityCapacity - seatCount}.");
        }

        if (measuredRounds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(measuredRounds));
        }

        using World ecs = World.Create();
        using var physics = new Physics3DWorld(CreateWorldConfig(
            mobileCapacity: seatCount,
            staticBodyCapacity: ordinaryBodyCount));
        var entities = new NetworkEntityTable(capacity: globalEntityCapacity);
        var knowledge = new KnowledgeProjectionStore(
            initialCapacity: seatCount * replicationEntityCapacityPerSeat);
        using var lifecycle = new Physics3DNetworkPlayerLifecycle(
            ecs,
            physics,
            entities,
            knowledge,
            seatCount,
            SchemaId,
            CreateBodyConfig(),
            new Physics3DNetworkPlayerSpawnConfig
            {
                OriginCm = Vector3.Zero,
                ColumnSpacingCm = clusterSpacingCm,
                RowSpacingCm = clusterSpacingCm,
                Columns = clusterColumns,
            });
        var simulationTicks = new AuthoritativeSimulationTickState();
        using var registry = new Physics3DNetworkBodyRegistry(
            ecs,
            physics,
            entities,
            simulationTicks,
            SchemaId,
            commandCapacity: ordinaryBodyCount);
        var seats = new SessionSeatBinding[seatCount];
        var expectedSelectedCounts = new int[seatCount];
        for (int slot = 0; slot < seatCount; slot++)
        {
            seats[slot] = Seat(slot, generation: 1);
            if (!lifecycle.TryResolveController(in seats[slot], out _))
            {
                throw new InvalidOperationException(
                    $"AOI scale setup failed to create seat {slot}: {lifecycle.LastFailure}.");
            }

            expectedSelectedCounts[slot] = 1;
        }

        Physics3DShapeId bodyShape = physics.RegisterBoxShape(new Vector3(10f));
        Physics3DMaterial bodyMaterial = CreateBodyConfig().Material;
        for (int bodyIndex = 0; bodyIndex < ordinaryBodyCount; bodyIndex++)
        {
            int clusterSlot = bodyIndex % seatCount;
            Vector3 position = new(
                (clusterSlot % clusterColumns) * clusterSpacingCm,
                0f,
                (clusterSlot / clusterColumns) * clusterSpacingCm);
            var pose = new Physics3DPoseCm
            {
                Position = position,
                Orientation = Quaternion.Identity,
            };
            var schema = new ReplicationSchemaRef(SchemaId);
            Entity entity = ecs.Create(in pose, in schema);
            Physics3DBodyId bodyId = physics.CreateBody(new Physics3DBodyDescription(
                entity,
                Physics3DBodyKind.Static,
                bodyShape,
                position,
                Quaternion.Identity,
                Vector3.Zero,
                Vector3.Zero,
                mass: 0f,
                LayerMask.All,
                bodyMaterial,
                Physics3DContinuousDetectionMode.Discrete));
            var body = new Physics3DBodyCm
            {
                Id = bodyId,
                Kind = Physics3DBodyKind.Static,
            };
            ecs.Add(entity, body);
            expectedSelectedCounts[clusterSlot]++;
        }

        if (!registry.TryQueueEligibleBodies(out int queuedCount) ||
            queuedCount != ordinaryBodyCount)
        {
            throw new InvalidOperationException(
                $"AOI scale setup failed to queue {ordinaryBodyCount:N0} registered bodies: " +
                $"failure={registry.LastFailure}, queued={queuedCount}.");
        }

        simulationTicks.Begin(1);
        if (!registry.TryApplyPendingStructuralChanges())
        {
            throw new InvalidOperationException(
                $"AOI scale setup failed to register {ordinaryBodyCount:N0} bodies: " +
                $"failure={registry.LastFailure}.");
        }

        simulationTicks.Commit(1);

        using var interest = new Physics3DNetworkAoiInterestPort(
            ecs,
            physics,
            entities,
            lifecycle,
            knowledge,
            replicationEntityCapacityPerSeat,
            new Physics3DNetworkAoiConfig
            {
                RadiusCm = interestRadiusCm,
                GlobalEntityCapacity = globalEntityCapacity,
            });
        var destination = new NetworkEntityHandle[replicationEntityCapacityPerSeat];
        int maximumSelectedCount = 0;
        for (int seatIndex = 0; seatIndex < seatCount; seatIndex++)
        {
            maximumSelectedCount = Math.Max(maximumSelectedCount, expectedSelectedCounts[seatIndex]);
        }

        if (destination.Length < maximumSelectedCount)
        {
            throw new InvalidOperationException(
                $"AOI per-seat destination capacity {destination.Length} is below selected count {maximumSelectedCount}.");
        }

        for (int round = 0; round < warmupRounds; round++)
        {
            for (int seatIndex = 0; seatIndex < seatCount; seatIndex++)
            {
                if (!interest.TryCopyInterest(in seats[seatIndex], destination, out int selectedCount) ||
                    selectedCount != expectedSelectedCounts[seatIndex])
                {
                    throw new InvalidOperationException(
                        $"AOI warmup failed at round {round}, seat {seatIndex}: " +
                        $"failure={interest.LastFailure}, selected={selectedCount}, " +
                        $"expected={expectedSelectedCounts[seatIndex]}.");
                }
            }
        }

        var seatCallSamples = new long[checked(seatCount * measuredRounds)];
        var fullRoundSamples = new long[measuredRounds];
        int sampleIndex = 0;
        int failedRound = -1;
        int failedSeat = -1;
        int failedCount = -1;
        int failedExpectedCount = -1;
        int maximumBroadphaseHitCount = 0;
        long totalBroadphaseHitCount = 0;
        Physics3DNetworkAoiFailure failure = Physics3DNetworkAoiFailure.None;
        _ = Stopwatch.GetTimestamp();
        _ = GC.GetAllocatedBytesForCurrentThread();
        long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < measuredRounds; round++)
        {
            long roundStarted = Stopwatch.GetTimestamp();
            for (int seatIndex = 0; seatIndex < seatCount; seatIndex++)
            {
                long started = Stopwatch.GetTimestamp();
                bool copied = interest.TryCopyInterest(
                    in seats[seatIndex],
                    destination,
                    out int selectedCount);
                maximumBroadphaseHitCount = Math.Max(
                    maximumBroadphaseHitCount,
                    interest.LastOverlapHitCount);
                totalBroadphaseHitCount += interest.LastOverlapHitCount;
                seatCallSamples[sampleIndex++] = Stopwatch.GetTimestamp() - started;
                if ((!copied || selectedCount != expectedSelectedCounts[seatIndex]) && failedRound < 0)
                {
                    failedRound = round;
                    failedSeat = seatIndex;
                    failedCount = selectedCount;
                    failedExpectedCount = expectedSelectedCounts[seatIndex];
                    failure = interest.LastFailure;
                }
            }

            fullRoundSamples[round] = Stopwatch.GetTimestamp() - roundStarted;
        }

        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        seatCallSamples.AsSpan().Sort();
        fullRoundSamples.AsSpan().Sort();
        double millisecondsPerTimestamp = 1_000d / Stopwatch.Frequency;
        double seatCallP95Milliseconds = Percentile(seatCallSamples, 0.95) * millisecondsPerTimestamp;
        double seatCallP99Milliseconds = Percentile(seatCallSamples, 0.99) * millisecondsPerTimestamp;
        double fullRoundP95Milliseconds = Percentile(fullRoundSamples, 0.95) * millisecondsPerTimestamp;
        double fullRoundP99Milliseconds = Percentile(fullRoundSamples, 0.99) * millisecondsPerTimestamp;
        double fixedStepBudgetMilliseconds = 1_000d / fixedStepHz;
        TestContext.Out.WriteLine(
            $"Physics3D AOI broadphase: 150 seats + {ordinaryBodyCount:N0} registered bodies, " +
            $"{measuredRounds} measured rounds, {sampleIndex:N0} seat calls, " +
            $"selected-per-seat max={maximumSelectedCount}/{destination.Length}, " +
            $"broadphase-hits max={maximumBroadphaseHitCount}, total={totalBroadphaseHitCount:N0}, " +
            $"seat-call P95={seatCallP95Milliseconds:F3}ms, P99={seatCallP99Milliseconds:F3}ms, " +
            $"full-round P95={fullRoundP95Milliseconds:F3}ms, P99={fullRoundP99Milliseconds:F3}ms, " +
            $"budget={fixedStepBudgetMilliseconds:F3}ms, " +
            $"calling-thread allocations={allocatedBytes}B.");

        Assert.Multiple(() =>
        {
            Assert.That(sampleIndex, Is.EqualTo(seatCount * measuredRounds));
            Assert.That(
                failedRound,
                Is.EqualTo(-1),
                $"AOI failed at round {failedRound}, seat {failedSeat}: " +
                $"failure={failure}, selected={failedCount}, expected={failedExpectedCount}.");
            Assert.That(destination.Length, Is.GreaterThanOrEqualTo(maximumSelectedCount));
            Assert.That(registry.Count, Is.EqualTo(ordinaryBodyCount));
            Assert.That(interest.OverlapScratchCapacity, Is.EqualTo(globalEntityCapacity));
            Assert.That(maximumBroadphaseHitCount, Is.EqualTo(maximumSelectedCount));
            Assert.That(
                totalBroadphaseHitCount,
                Is.LessThan(checked((long)seatCount * measuredRounds * ordinaryBodyCount)),
                "AOI query surfaced the global registered-body set instead of local broadphase hits.");
            Assert.That(
                fullRoundP95Milliseconds,
                Is.LessThanOrEqualTo(fixedStepBudgetMilliseconds),
                $"AOI 150 seats + {ordinaryBodyCount:N0} bodies full-round P95 exceeded the 30Hz fixed-step budget.");
            Assert.That(
                fullRoundP99Milliseconds,
                Is.LessThanOrEqualTo(fixedStepBudgetMilliseconds),
                $"AOI 150 seats + {ordinaryBodyCount:N0} bodies full-round P99 exceeded the 30Hz fixed-step budget.");
            Assert.That(
                allocatedBytes,
                Is.Zero,
                $"AOI 150 seats + {ordinaryBodyCount:N0} bodies allocated on the calling thread after warmup.");
        });
    }

    private static Physics3DNetworkPlayerLifecycle CreateLifecycle(
        World ecs,
        IPhysics3DWorld physics,
        NetworkEntityTable entities,
        KnowledgeProjectionStore knowledge,
        int seatCapacity,
        float spawnSpacingCm = 500f)
    {
        return new Physics3DNetworkPlayerLifecycle(
            ecs,
            physics,
            entities,
            knowledge,
            seatCapacity,
            SchemaId,
            CreateBodyConfig(),
            new Physics3DNetworkPlayerSpawnConfig
            {
                OriginCm = Vector3.Zero,
                ColumnSpacingCm = spawnSpacingCm,
                RowSpacingCm = spawnSpacingCm,
                Columns = seatCapacity,
            });
    }

    private static Physics3DAuthoritativeFixedInputConsumer CreateConsumer(
        AuthoritativeFixedInputIngress ingress,
        Physics3DNetworkPlayerLifecycle lifecycle,
        IPhysics3DWorld physics,
        ushort schemaId) => new(
            new Physics3DAuthoritativeFixedInputIngressSource(ingress),
            lifecycle,
            physics,
            new Physics3DNetworkMovementConfig
            {
                SchemaId = schemaId,
                MaximumSpeedCmPerSecond = 600f,
                MaximumAccelerationCmPerSecondSquared = 1_800f,
                VelocityResponsePerSecond = 20f,
            });

    private static void AssertConsumeAndCommit(
        AuthoritativeSimulationTickState ticks,
        Physics3DAuthoritativeFixedInputConsumer consumer,
        uint tick,
        Physics3DFixedInputConsumeResult expected)
    {
        ticks.Begin(checked((int)tick));
        Assert.That(consumer.TryConsume(tick), Is.EqualTo(expected));
        ticks.Commit(checked((int)tick));
    }

    private static Physics3DNetworkPlayerBodyConfig CreateBodyConfig() => new()
    {
        RadiusCm = 30f,
        CylinderLengthCm = 100f,
        Mass = 80f,
        CollisionLayer = LayerMask.All,
        Material = new Physics3DMaterial(
            frictionCoefficient: 0.8f,
            maximumRecoveryVelocityCmPerSecond: 200f,
            springAngularFrequency: 30f,
            springTwiceDampingRatio: 1f),
        ContinuousDetection = Physics3DContinuousDetectionMode.Passive,
    };

    private static ReplicationSchemaProjectorRegistry CreateProjectors(IPhysics3DWorld physics)
    {
        var projectors = new ReplicationSchemaProjectorRegistry(schemaCapacity: SchemaId);
        Assert.That(
            projectors.Register(
                SchemaId,
                new Physics3DBodyReplicationProjector(physics, SchemaId, new Physics3DReplicationQuantizationConfig())),
            Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
        projectors.Freeze();
        return projectors;
    }

    private static FixedInputProtocolConfig CreateFixedInputConfig(int seatCapacity) => new(
        seatCapacity,
        historyTicksPerSeat: 8,
        schemaId: 7,
        framePayloadBytes: Physics3DFixedInputFrameCodec.PayloadBytes,
        maxFutureTicks: 4,
        maxFramesPerBatch: 1,
        maxDatagramPayloadBytes: 1_200,
        sessionEpoch: SessionEpoch);

    private static void Admit(
        AuthoritativeFixedInputIngress ingress,
        in SessionSeatBinding seat,
        uint tick,
        ReadOnlySpan<byte> payload)
    {
        Span<uint> ticks = stackalloc uint[1] { tick };
        Span<FixedInputAdmissionDisposition> dispositions = stackalloc FixedInputAdmissionDisposition[1];
        var header = new NetworkFixedInputBatchHeader(
            ingress.Config.SessionEpoch,
            ingress.Config.SchemaId,
            ingress.Config.FramePayloadBytes,
            acknowledgedCommittedTick: 0,
            frameCount: 1);
        Assert.That(
            ingress.TryAdmitBatch(in seat, in header, ticks, payload, dispositions),
            Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(dispositions[0], Is.EqualTo(FixedInputAdmissionDisposition.Accepted));
    }

    private static void MoveEntity(World ecs, IPhysics3DWorld physics, Entity entity, Vector3 position)
    {
        Physics3DBodyCm body = ecs.Get<Physics3DBodyCm>(entity);
        Physics3DPoseCm pose = ecs.Get<Physics3DPoseCm>(entity);
        pose.Position = position;
        ecs.Set(entity, pose);
        Physics3DBodyState state = physics.GetBodyState(body.Id);
        state.PositionCm = position;
        physics.SetBodyState(body.Id, in state);
    }

    private static SessionSeatBinding Seat(int slot, uint generation) =>
        new(slot, generation, new PlayerId(slot + 1));

    private static int CountPlayers(World world)
    {
        var query = new QueryDescription().WithAll<Physics3DNetworkPlayer>();
        int count = 0;
        foreach (ref Chunk chunk in world.Query(in query))
        {
            count += chunk.Count;
        }

        return count;
    }

    private static double Percentile(ReadOnlySpan<long> sortedValues, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sortedValues.Length) - 1;
        return sortedValues[Math.Clamp(index, 0, sortedValues.Length - 1)];
    }

    private static Physics3DWorldConfig CreateWorldConfig(
        int mobileCapacity,
        int staticBodyCapacity = 1) => new()
    {
        MobileBodyCapacity = mobileCapacity,
        StaticBodyCapacity = staticBodyCapacity,
        ShapeCapacity = 8,
        InactiveIslandCapacity = Math.Max(1, mobileCapacity),
        ConstraintCapacity = 8,
        ConstraintsPerTypeBatchCapacity = 8,
        ConstraintCountPerBodyEstimate = 4,
        ContactPairCapacityPerWorker = 32,
        ActuationCommandCapacity = Math.Max(8, mobileCapacity * 4),
        WorkerCount = 1,
        FixedStepHz = 30,
        MaximumPhysicsStepsPerSourceTick = 1,
        SolverSubstepCount = 1,
        SolverVelocityIterationCount = 8,
        GravityCmPerSecondSquared = Vector3.Zero,
        LinearDamping = 0f,
        AngularDamping = 0f,
        MaximumSpeculativeMarginCm = 10f,
        SleepThreshold = 0.01f,
        MinimumTimestepCountUnderSleepThreshold = 32,
        ContinuousMinimumSweepTimestep = 0.001f,
        ContinuousSweepConvergenceThreshold = 0.001f,
        MaterialCombineMode = Physics3DMaterialCombineMode.GeometricMean,
    };
}
