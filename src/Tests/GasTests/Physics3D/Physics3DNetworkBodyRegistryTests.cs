using System.Numerics;
using Arch.Core;
using Ludots.Core.Knowledge;
using Ludots.Core.Layers;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Simulation;
using Ludots.Core.Physics3D;
using Ludots.Core.Physics3DNet.Bridge;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Physics3DNetworkBodyRegistryTests
{
    private const int SchemaId = 17;

    [Test]
    public void BulkRegister_10K_UsesDirectSlotsAndWarmedSteadyPathIsZeroAllocation()
    {
        const int bodyCount = 10_000;
        using World ecs = World.Create();
        using var physics = CreatePhysics(mobileCapacity: 1, staticCapacity: bodyCount);
        Physics3DShapeId shape = physics.RegisterBoxShape(new Vector3(10f));
        var entities = new NetworkEntityTable(bodyCount);
        var ticks = new AuthoritativeSimulationTickState();
        using var registry = new Physics3DNetworkBodyRegistry(
            ecs,
            physics,
            entities,
            ticks,
            SchemaId,
            commandCapacity: bodyCount);
        var bodies = new Entity[bodyCount];
        for (int index = 0; index < bodyCount; index++)
        {
            bodies[index] = CreateBody(
                ecs,
                physics,
                shape,
                Physics3DBodyKind.Static,
                new Vector3((index % 100) * 20f, 0f, (index / 100) * 20f));
        }

        Assert.That(registry.TryApplyPendingStructuralChanges(), Is.False);
        Assert.That(registry.LastFailure, Is.EqualTo(Physics3DNetworkBodyRegistryFailure.StructuralTickRequired));
        ticks.Begin(1);
        Assert.That(registry.TryQueueEligibleBodies(out int queued), Is.True);
        Assert.That(queued, Is.EqualTo(bodyCount));
        Assert.That(registry.TryApplyPendingStructuralChanges(), Is.True);
        ticks.Commit(1);

        Assert.Multiple(() =>
        {
            Assert.That(registry.Count, Is.EqualTo(bodyCount));
            Assert.That(entities.Count, Is.EqualTo(bodyCount));
            Assert.That(registry.PendingRegistrationCount, Is.Zero);
            Assert.That(registry.PendingReleaseCount, Is.Zero);
        });

        for (int index = 0; index < bodyCount; index++)
        {
            Assert.That(registry.TryGetHandle(bodies[index], out NetworkEntityHandle handle), Is.True);
            Assert.That(registry.TryResolve(handle, out Entity resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(bodies[index]));
            Assert.That(ecs.Get<Physics3DNetworkReplicatedBody>(resolved).Handle, Is.EqualTo(handle));
        }

        ticks.Begin(2);
        for (int warmup = 0; warmup < 16; warmup++)
        {
            Assert.That(registry.TryQueueEligibleBodies(out int warmQueued), Is.True);
            Assert.That(warmQueued, Is.Zero);
            Assert.That(registry.TryApplyPendingStructuralChanges(), Is.True);
        }

        _ = GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool succeeded = true;
        int unexpectedlyQueued = 0;
        for (int iteration = 0; iteration < 128; iteration++)
        {
            succeeded &= registry.TryQueueEligibleBodies(out int steadyQueued);
            unexpectedlyQueued += steadyQueued;
            succeeded &= registry.TryApplyPendingStructuralChanges();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        ticks.Commit(2);
        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.True);
            Assert.That(unexpectedlyQueued, Is.Zero);
            Assert.That(allocated, Is.Zero, $"Warmed Physics3D body registry allocated {allocated} bytes.");
        });
    }

    [Test]
    public void Release_ValidatesGenerationAndReusesTheDirectSlotWithANewerGeneration()
    {
        using World ecs = World.Create();
        using var physics = CreatePhysics(mobileCapacity: 1, staticCapacity: 2);
        Physics3DShapeId shape = physics.RegisterBoxShape(new Vector3(10f));
        var entities = new NetworkEntityTable(1);
        var ticks = new AuthoritativeSimulationTickState();
        using var registry = new Physics3DNetworkBodyRegistry(
            ecs,
            physics,
            entities,
            ticks,
            SchemaId,
            commandCapacity: 1);
        Entity first = CreateBody(ecs, physics, shape, Physics3DBodyKind.Static, Vector3.Zero);

        ApplySingleRegistration(registry, ticks, first, tick: 1);
        Assert.That(registry.TryGetHandle(first, out NetworkEntityHandle firstHandle), Is.True);
        var stale = new NetworkEntityHandle(firstHandle.Slot, firstHandle.Generation + 1);
        Assert.That(registry.TryQueueRelease(stale), Is.False);
        Assert.That(registry.LastFailure, Is.EqualTo(Physics3DNetworkBodyRegistryFailure.StaleHandle));

        ecs.Remove<ReplicationSchemaRef>(first);
        Assert.That(registry.TryQueueRelease(firstHandle), Is.True);
        Assert.That(registry.TryQueueRelease(firstHandle), Is.False);
        Assert.That(registry.LastFailure, Is.EqualTo(Physics3DNetworkBodyRegistryFailure.DuplicateReleaseCommand));
        ticks.Begin(2);
        Assert.That(registry.TryApplyPendingStructuralChanges(), Is.True);
        ticks.Commit(2);
        Assert.Multiple(() =>
        {
            Assert.That(entities.Count, Is.Zero);
            Assert.That(registry.Count, Is.Zero);
            Assert.That(ecs.Has<Physics3DNetworkReplicatedBody>(first), Is.False);
            Assert.That(registry.TryQueueRelease(firstHandle), Is.False);
            Assert.That(registry.LastFailure, Is.EqualTo(Physics3DNetworkBodyRegistryFailure.StaleHandle));
        });

        Entity second = CreateBody(ecs, physics, shape, Physics3DBodyKind.Static, new Vector3(100f, 0f, 0f));
        ApplySingleRegistration(registry, ticks, second, tick: 3);
        Assert.That(registry.TryGetHandle(second, out NetworkEntityHandle secondHandle), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(secondHandle.Slot, Is.EqualTo(firstHandle.Slot));
            Assert.That(secondHandle.Generation, Is.GreaterThan(firstHandle.Generation));
            Assert.That(registry.TryResolve(firstHandle, out _), Is.False);
            Assert.That(registry.TryResolve(secondHandle, out Entity resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(second));
        });
    }

    [Test]
    public void RegistrationAndReleaseFailures_AreExplicitAndDoNotPublishPartialState()
    {
        using World ecs = World.Create();
        using var physics = CreatePhysics(mobileCapacity: 2, staticCapacity: 2);
        Physics3DShapeId shape = physics.RegisterBoxShape(new Vector3(10f));
        var entities = new NetworkEntityTable(4);
        var ticks = new AuthoritativeSimulationTickState();
        using var registry = new Physics3DNetworkBodyRegistry(
            ecs,
            physics,
            entities,
            ticks,
            SchemaId,
            commandCapacity: 4);
        var pose = Pose(Vector3.Zero);
        var schema = new ReplicationSchemaRef(SchemaId);
        var validBody = new Physics3DBodyCm
        {
            Id = physics.CreateBody(BodyDescription(Entity.Null, shape, Physics3DBodyKind.Dynamic, Vector3.Zero)),
            Kind = Physics3DBodyKind.Dynamic,
        };

        Entity missingBody = ecs.Create(in pose, in schema);
        AssertFailure(
            registry,
            missingBody,
            Physics3DNetworkBodyRegistryFailure.MissingBody);
        Entity missingPose = ecs.Create(in validBody, in schema);
        AssertFailure(
            registry,
            missingPose,
            Physics3DNetworkBodyRegistryFailure.MissingPose);
        Entity missingSchema = ecs.Create(in validBody, in pose);
        AssertFailure(
            registry,
            missingSchema,
            Physics3DNetworkBodyRegistryFailure.MissingSchema);
        var invalidBody = new Physics3DBodyCm { Kind = Physics3DBodyKind.Dynamic };
        Entity invalid = ecs.Create(in invalidBody, in pose, in schema);
        AssertFailure(
            registry,
            invalid,
            Physics3DNetworkBodyRegistryFailure.InvalidBody);
        ReplicationSchemaRef wrongSchema = new(SchemaId + 1);
        Entity wrongSchemaEntity = ecs.Create(in validBody, in pose, in wrongSchema);
        AssertFailure(
            registry,
            wrongSchemaEntity,
            Physics3DNetworkBodyRegistryFailure.SchemaMismatch);

        Entity valid = CreateBody(ecs, physics, shape, Physics3DBodyKind.Static, new Vector3(200f, 0f, 0f));
        ApplySingleRegistration(registry, ticks, valid, tick: 1);
        Assert.That(registry.TryQueueRegister(valid), Is.False);
        Assert.That(registry.LastFailure, Is.EqualTo(Physics3DNetworkBodyRegistryFailure.DuplicateReplicatedBody));
        Assert.Multiple(() =>
        {
            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(entities.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void NetworkCapacityAndEntityTableMismatch_FailWithoutSilentCleanup()
    {
        using World ecs = World.Create();
        using var physics = CreatePhysics(mobileCapacity: 1, staticCapacity: 2);
        Physics3DShapeId shape = physics.RegisterBoxShape(new Vector3(10f));
        var ticks = new AuthoritativeSimulationTickState();
        var fullTable = new NetworkEntityTable(1);
        Entity foreign = ecs.Create();
        Assert.That(fullTable.TryAllocate(foreign, out _), Is.True);
        var capacityRegistry = new Physics3DNetworkBodyRegistry(
            ecs,
            physics,
            fullTable,
            ticks,
            SchemaId,
            commandCapacity: 1);
        Entity candidate = CreateBody(ecs, physics, shape, Physics3DBodyKind.Static, Vector3.Zero);
        Assert.That(capacityRegistry.TryQueueRegister(candidate), Is.True);
        ticks.Begin(1);
        Assert.That(capacityRegistry.TryApplyPendingStructuralChanges(), Is.False);
        Assert.That(
            capacityRegistry.LastFailure,
            Is.EqualTo(Physics3DNetworkBodyRegistryFailure.NetworkEntityCapacityExceeded));
        ticks.Commit(1);
        Assert.Multiple(() =>
        {
            Assert.That(capacityRegistry.Count, Is.Zero);
            Assert.That(ecs.Has<Physics3DNetworkReplicatedBody>(candidate), Is.False);
            Assert.That(fullTable.Count, Is.EqualTo(1));
        });

        Assert.Throws<InvalidOperationException>(capacityRegistry.Dispose);

        var table = new NetworkEntityTable(1);
        var mismatchTicks = new AuthoritativeSimulationTickState();
        var registry = new Physics3DNetworkBodyRegistry(
            ecs,
            physics,
            table,
            mismatchTicks,
            SchemaId,
            commandCapacity: 1);
        Entity registered = CreateBody(ecs, physics, shape, Physics3DBodyKind.Static, new Vector3(100f, 0f, 0f));
        ApplySingleRegistration(registry, mismatchTicks, registered, tick: 1);
        Assert.That(registry.TryGetHandle(registered, out NetworkEntityHandle handle), Is.True);
        Assert.That(table.TryRelease(handle), Is.True);
        Assert.That(registry.TryQueueRelease(handle), Is.False);
        Assert.That(registry.LastFailure, Is.EqualTo(Physics3DNetworkBodyRegistryFailure.EntityTableMismatch));
        Assert.That(ecs.Has<Physics3DNetworkReplicatedBody>(registered), Is.True);
    }

    [Test]
    public void AoiKnowledge_EnterUpdateExitAndSeatGenerationChange_AreTrackedAndZeroAllocation()
    {
        using World ecs = World.Create();
        using var physics = CreatePhysics(mobileCapacity: 2, staticCapacity: 1);
        Physics3DShapeId shape = physics.RegisterBoxShape(new Vector3(10f));
        var entities = new NetworkEntityTable(8);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 4);
        var ticks = new AuthoritativeSimulationTickState();
        using var registry = new Physics3DNetworkBodyRegistry(
            ecs,
            physics,
            entities,
            ticks,
            SchemaId,
            commandCapacity: 8);
        using var players = CreatePlayers(ecs, physics, entities, knowledge, seatCapacity: 1);
        SessionSeatBinding firstSeat = Seat(generation: 1);
        Assert.That(players.TryResolveController(in firstSeat, out Entity firstViewer), Is.True);
        Entity target = CreateBody(
            ecs,
            physics,
            shape,
            Physics3DBodyKind.Static,
            new Vector3(1_000f, 0f, 0f));
        ApplySingleRegistration(registry, ticks, target, tick: 1);
        using var aoi = new Physics3DNetworkAoiInterestPort(
            ecs,
            entities,
            players,
            knowledge,
            replicationEntityCapacityPerSeat: 4,
            new Physics3DNetworkAoiConfig
            {
                GlobalEntityCapacity = entities.Capacity,
                RadiusCm = 200f,
            });
        Span<NetworkEntityHandle> interest = stackalloc NetworkEntityHandle[4];

        Assert.That(aoi.TryCopyInterest(in firstSeat, interest, out int farCount), Is.True);
        Assert.That(farCount, Is.EqualTo(1));
        Assert.That(knowledge.TryGet(firstViewer, target, 0, out _), Is.False);

        SetPose(ecs, target, new Vector3(100f, 0f, 0f));
        Assert.That(aoi.TryCopyInterest(in firstSeat, interest, out int nearCount), Is.True);
        Assert.That(nearCount, Is.EqualTo(2));
        Assert.That(knowledge.TryGet(firstViewer, target, 0, out KnowledgeDisclosureRecord entered), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(entered.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
            Assert.That(entered.Position, Is.EqualTo(KnowledgePositionAccess.Live));
            Assert.That(entered.Source, Is.EqualTo(firstViewer));
        });

        SetPose(ecs, target, new Vector3(150f, 0f, 0f));
        Assert.That(aoi.TryCopyInterest(in firstSeat, interest, out int updatedCount), Is.True);
        Assert.That(updatedCount, Is.EqualTo(2));
        Assert.That(knowledge.TryGet(firstViewer, target, 0, out KnowledgeDisclosureRecord updated), Is.True);
        Assert.That(updated.Revision, Is.EqualTo(entered.Revision));

        SetPose(ecs, target, new Vector3(1_000f, 0f, 0f));
        Assert.That(aoi.TryCopyInterest(in firstSeat, interest, out int exitedCount), Is.True);
        Assert.That(exitedCount, Is.EqualTo(1));
        Assert.That(knowledge.TryGet(firstViewer, target, 0, out _), Is.False);

        SetPose(ecs, target, new Vector3(100f, 0f, 0f));
        Assert.That(aoi.TryCopyInterest(in firstSeat, interest, out _), Is.True);
        Assert.That(knowledge.TryGet(firstViewer, target, 0, out _), Is.True);
        Assert.That(players.TryRelease(in firstSeat), Is.True);
        SessionSeatBinding secondSeat = Seat(generation: 2);
        Assert.That(players.TryResolveController(in secondSeat, out Entity secondViewer), Is.True);
        Assert.That(aoi.TryCopyInterest(in secondSeat, interest, out int reconnectCount), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reconnectCount, Is.EqualTo(2));
            Assert.That(knowledge.TryGet(firstViewer, target, 0, out _), Is.False);
            Assert.That(knowledge.TryGet(secondViewer, target, 0, out _), Is.True);
        });

        Assert.That(players.TryRelease(in secondSeat), Is.True);
        SessionSeatBinding thirdSeat = Seat(generation: 3);
        Assert.That(players.TryResolveController(in thirdSeat, out Entity thirdViewer), Is.True);
        _ = GC.GetAllocatedBytesForCurrentThread();
        long reconnectBefore = GC.GetAllocatedBytesForCurrentThread();
        bool reconnectSucceeded = aoi.TryCopyInterest(in thirdSeat, interest, out int secondReconnectCount);
        long reconnectAllocated = GC.GetAllocatedBytesForCurrentThread() - reconnectBefore;
        Assert.Multiple(() =>
        {
            Assert.That(reconnectSucceeded, Is.True);
            Assert.That(secondReconnectCount, Is.EqualTo(2));
            Assert.That(knowledge.TryGet(secondViewer, target, 0, out _), Is.False);
            Assert.That(knowledge.TryGet(thirdViewer, target, 0, out _), Is.True);
            Assert.That(knowledge.PhysicalRecordCount, Is.LessThanOrEqualTo(knowledge.RecordCapacity));
            Assert.That(reconnectAllocated, Is.Zero, $"Physics3D AOI reconnect allocated {reconnectAllocated} bytes.");
        });

        for (int warmup = 0; warmup < 32; warmup++)
        {
            Assert.That(aoi.TryCopyInterest(in thirdSeat, interest, out int warmCount), Is.True);
            Assert.That(warmCount, Is.EqualTo(2));
        }

        _ = GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool succeeded = true;
        int countTotal = 0;
        for (int iteration = 0; iteration < 128; iteration++)
        {
            succeeded &= aoi.TryCopyInterest(in thirdSeat, interest, out int count);
            countTotal += count;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.True);
            Assert.That(countTotal, Is.EqualTo(256));
            Assert.That(allocated, Is.Zero, $"Warmed Physics3D AOI knowledge path allocated {allocated} bytes.");
        });

        aoi.Dispose();
        Assert.That(players.TryRelease(in thirdSeat), Is.True);
        ecs.Remove<ReplicationSchemaRef>(target);
        Assert.That(registry.TryGetHandle(target, out NetworkEntityHandle targetHandle), Is.True);
        Assert.That(registry.TryQueueRelease(targetHandle), Is.True);
        ticks.Begin(2);
        Assert.That(registry.TryApplyPendingStructuralChanges(), Is.True);
        ticks.Commit(2);
    }

    private static void AssertFailure(
        Physics3DNetworkBodyRegistry registry,
        Entity entity,
        Physics3DNetworkBodyRegistryFailure expected)
    {
        Assert.That(registry.TryQueueRegister(entity), Is.False);
        Assert.That(registry.LastFailure, Is.EqualTo(expected));
        Assert.That(registry.PendingRegistrationCount, Is.Zero);
    }

    private static void ApplySingleRegistration(
        Physics3DNetworkBodyRegistry registry,
        AuthoritativeSimulationTickState ticks,
        Entity entity,
        int tick)
    {
        Assert.That(registry.TryQueueRegister(entity), Is.True);
        ticks.Begin(tick);
        Assert.That(registry.TryApplyPendingStructuralChanges(), Is.True);
        ticks.Commit(tick);
    }

    private static Physics3DNetworkPlayerLifecycle CreatePlayers(
        World world,
        IPhysics3DWorld physics,
        NetworkEntityTable entities,
        KnowledgeProjectionStore knowledge,
        int seatCapacity) => new(
        world,
        physics,
        entities,
        knowledge,
        seatCapacity,
        SchemaId,
        new Physics3DNetworkPlayerBodyConfig
        {
            RadiusCm = 30f,
            CylinderLengthCm = 100f,
            Mass = 80f,
            CollisionLayer = LayerMask.All,
            Material = Material(),
            ContinuousDetection = Physics3DContinuousDetectionMode.Passive,
        },
        new Physics3DNetworkPlayerSpawnConfig
        {
            OriginCm = Vector3.Zero,
            ColumnSpacingCm = 500f,
            RowSpacingCm = 500f,
            Columns = seatCapacity,
        });

    private static SessionSeatBinding Seat(uint generation) =>
        new(slot: 0, generation, new PlayerId(1));

    private static Physics3DWorld CreatePhysics(int mobileCapacity, int staticCapacity) => new(
        Physics3DWorldTests.CreateConfig(
            mobileCapacity,
            staticCapacity,
            workerCount: 1,
            fixedStepHz: 30,
            gravityCmPerSecondSquared: Vector3.Zero));

    private static Entity CreateBody(
        World ecs,
        IPhysics3DWorld physics,
        Physics3DShapeId shape,
        Physics3DBodyKind kind,
        Vector3 position)
    {
        Physics3DPoseCm pose = Pose(position);
        var schema = new ReplicationSchemaRef(SchemaId);
        Entity entity = ecs.Create(in pose, in schema);
        Physics3DBodyId bodyId = physics.CreateBody(BodyDescription(entity, shape, kind, position));
        var body = new Physics3DBodyCm { Id = bodyId, Kind = kind };
        ecs.Add(entity, body);
        return entity;
    }

    private static Physics3DBodyDescription BodyDescription(
        Entity entity,
        Physics3DShapeId shape,
        Physics3DBodyKind kind,
        Vector3 position) => new(
        entity,
        kind,
        shape,
        position,
        Quaternion.Identity,
        Vector3.Zero,
        Vector3.Zero,
        kind == Physics3DBodyKind.Dynamic ? 1f : 0f,
        LayerMask.All,
        Material(),
        Physics3DContinuousDetectionMode.Passive);

    private static Physics3DMaterial Material() => new(
        frictionCoefficient: 0.8f,
        maximumRecoveryVelocityCmPerSecond: 200f,
        springAngularFrequency: 30f,
        springTwiceDampingRatio: 1f);

    private static Physics3DPoseCm Pose(Vector3 position) => new()
    {
        Position = position,
        Orientation = Quaternion.Identity,
    };

    private static void SetPose(World world, Entity entity, Vector3 position)
    {
        Physics3DPoseCm pose = world.Get<Physics3DPoseCm>(entity);
        pose.Position = position;
        world.Set(entity, pose);
    }
}
