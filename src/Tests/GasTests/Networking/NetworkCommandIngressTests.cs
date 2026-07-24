using System.Numerics;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Networking;

[TestFixture]
public sealed class NetworkCommandIngressTests
{
    [Test]
    public void Schedule_DrainsDueBatchesInTickSeatSequenceOrder()
    {
        using World world = World.Create();
        using var harness = Harness.Create(world, scheduledBatchCapacity: 4);
        Entity playerOne = world.Create(new PlayerIdentity { PlayerId = 1 });
        Entity playerTwo = world.Create(new PlayerIdentity { PlayerId = 2 });
        Entity actorOne = world.Create();
        Entity actorTwo = world.Create();
        harness.Ownership.EnsureOwnership(playerOne, actorOne);
        harness.Ownership.EnsureOwnership(playerTwo, actorTwo);
        Assert.That(harness.Entities.TryAllocate(actorOne, out NetworkEntityHandle actorOneHandle), Is.True);
        Assert.That(harness.Entities.TryAllocate(actorTwo, out NetworkEntityHandle actorTwoHandle), Is.True);
        var seatOne = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 1);
        var seatTwo = new NetworkCommandSeat(slot: 1, generation: 1, playerId: 2);
        harness.Ingress.BindSeat(in seatOne, playerOne, serverTick: 10);
        harness.Ingress.BindSeat(in seatTwo, playerTwo, serverTick: 10);

        NetworkCommandWireEntry secondArrival = WorldCommand(actorTwoHandle, x: 200);
        NetworkCommandBatchHeader secondHeader = Batch(sequence: 1, targetTick: 12, entryCount: 1);
        Assert.That(
            harness.Ingress.Schedule(in seatTwo, in secondHeader, serverTick: 10, new[] { secondArrival }).Result,
            Is.EqualTo(OrderSubmitResult.NetworkScheduled));
        NetworkCommandWireEntry firstArrival = WorldCommand(actorOneHandle, x: 100);
        NetworkCommandBatchHeader firstHeader = Batch(sequence: 1, targetTick: 12, entryCount: 1);
        Assert.That(
            harness.Ingress.Schedule(in seatOne, in firstHeader, serverTick: 10, new[] { firstArrival }).Result,
            Is.EqualTo(OrderSubmitResult.NetworkScheduled));

        Assert.That(harness.Ingress.DrainScheduled(serverTick: 11), Is.Zero);
        Assert.That(harness.Ingress.DrainScheduled(serverTick: 12), Is.EqualTo(2));
        Span<Order> first = stackalloc Order[1];
        Span<Order> second = stackalloc Order[1];
        Assert.That(harness.Orders.TryDequeueBatch(first, out int firstCount), Is.True);
        Assert.That(harness.Orders.TryDequeueBatch(second, out int secondCount), Is.True);
        Order firstOrder = first[0];
        Order secondOrder = second[0];
        Assert.Multiple(() =>
        {
            Assert.That(firstCount, Is.EqualTo(1));
            Assert.That(secondCount, Is.EqualTo(1));
            Assert.That(firstOrder.Actor, Is.EqualTo(actorOne));
            Assert.That(firstOrder.PlayerId, Is.EqualTo(1));
            Assert.That(firstOrder.Args.Spatial.WorldCm, Is.EqualTo(new Vector3(100, 0, 0)));
            Assert.That(secondOrder.Actor, Is.EqualTo(actorTwo));
            Assert.That(secondOrder.PlayerId, Is.EqualTo(2));
            Assert.That(secondOrder.Args.Spatial.WorldCm, Is.EqualTo(new Vector3(200, 0, 0)));
        });
    }

    [Test]
    public void Schedule_RejectsStaleHandleAndForeignControlDomainWithoutPartialAdmission()
    {
        using World world = World.Create();
        using var harness = Harness.Create(world, scheduledBatchCapacity: 4);
        Entity playerOne = world.Create(new PlayerIdentity { PlayerId = 1 });
        Entity playerTwo = world.Create(new PlayerIdentity { PlayerId = 2 });
        Entity owned = world.Create();
        Entity foreign = world.Create();
        harness.Ownership.EnsureOwnership(playerOne, owned);
        harness.Ownership.EnsureOwnership(playerTwo, foreign);
        Assert.That(harness.Entities.TryAllocate(owned, out NetworkEntityHandle stale), Is.True);
        Assert.That(harness.Entities.TryRelease(stale), Is.True);
        Assert.That(harness.Entities.TryAllocate(owned, out NetworkEntityHandle current), Is.True);
        Assert.That(harness.Entities.TryAllocate(foreign, out NetworkEntityHandle foreignHandle), Is.True);
        var seat = new NetworkCommandSeat(0, 1, 1);
        harness.Ingress.BindSeat(in seat, playerOne, serverTick: 10);

        NetworkCommandWireEntry staleEntry = WorldCommand(stale, x: 1);
        NetworkCommandBatchHeader firstHeader = Batch(1, 10, 1);
        NetworkCommandAdmissionOutcome staleResult = harness.Ingress.Schedule(
            in seat,
            in firstHeader,
            serverTick: 10,
            new[] { staleEntry });
        NetworkCommandWireEntry foreignEntry = WorldCommand(foreignHandle, x: 2);
        NetworkCommandBatchHeader secondHeader = Batch(2, 10, 1);
        NetworkCommandAdmissionOutcome foreignResult = harness.Ingress.Schedule(
            in seat,
            in secondHeader,
            serverTick: 10,
            new[] { foreignEntry });

        Assert.Multiple(() =>
        {
            Assert.That(current.Generation, Is.Not.EqualTo(stale.Generation));
            Assert.That(staleResult.Result, Is.EqualTo(OrderSubmitResult.NetworkStaleActorGeneration));
            Assert.That(foreignResult.Result, Is.EqualTo(OrderSubmitResult.NetworkActorNotControlled));
            Assert.That(harness.Ingress.ScheduledBatchCount, Is.Zero);
            Assert.That(harness.Orders.Count, Is.Zero);
        });
    }

    [Test]
    public void Schedule_EntityTargetRequiresConfiguredSchemaAndViewerKnowledge()
    {
        using World world = World.Create();
        using var harness = Harness.Create(world, scheduledBatchCapacity: 4, includeEntityTargetSchema: true);
        Entity player = world.Create(new PlayerIdentity { PlayerId = 1 });
        Entity actor = world.Create();
        Entity target = world.Create();
        harness.Ownership.EnsureOwnership(player, actor);
        Assert.That(harness.Entities.TryAllocate(actor, out NetworkEntityHandle actorHandle), Is.True);
        Assert.That(harness.Entities.TryAllocate(target, out NetworkEntityHandle targetHandle), Is.True);
        var seat = new NetworkCommandSeat(0, 1, 1);
        harness.Ingress.BindSeat(in seat, player, serverTick: 10);
        var targetPayload = NetworkCommandTargetPayload.FromNetworkEntity(targetHandle.Slot, targetHandle.Generation);
        var entry = new NetworkCommandWireEntry(actorHandle, EntityOrderTypeId, in targetPayload);
        NetworkCommandBatchHeader firstHeader = Batch(1, 10, 1);

        NetworkCommandAdmissionOutcome unknown = harness.Ingress.Schedule(
            in seat,
            in firstHeader,
            serverTick: 10,
            new[] { entry });
        harness.Knowledge.Upsert(
            player,
            target,
            new KnowledgeDisclosureRecord(
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                default,
                default,
                default,
                player,
                observedTick: 10,
                expiryTick: 0,
                confidencePermille: 1000,
                revision: 1));
        NetworkCommandBatchHeader secondHeader = Batch(2, 10, 1);
        NetworkCommandAdmissionOutcome visible = harness.Ingress.Schedule(
            in seat,
            in secondHeader,
            serverTick: 10,
            new[] { entry });

        Assert.Multiple(() =>
        {
            Assert.That(unknown.Result, Is.EqualTo(OrderSubmitResult.NetworkTargetNotKnown));
            Assert.That(visible.Result, Is.EqualTo(OrderSubmitResult.NetworkScheduled));
            Assert.That(harness.Ingress.DrainScheduled(10), Is.EqualTo(1));
        });
        Span<Order> orders = stackalloc Order[1];
        Assert.That(harness.Orders.TryDequeueBatch(orders, out int count), Is.True);
        Assert.That(count, Is.EqualTo(1));
        Assert.That(orders[0].Target, Is.EqualTo(target));
    }

    [Test]
    public void UnbindAndRebind_PreservesAcceptedFutureBatchAndSequenceHistory()
    {
        using World world = World.Create();
        using var harness = Harness.Create(world, scheduledBatchCapacity: 4);
        Entity player = world.Create(new PlayerIdentity { PlayerId = 1 });
        Entity actor = world.Create();
        harness.Ownership.EnsureOwnership(player, actor);
        Assert.That(harness.Entities.TryAllocate(actor, out NetworkEntityHandle actorHandle), Is.True);
        var seat = new NetworkCommandSeat(0, 4, 1);
        harness.Ingress.BindSeat(in seat, player, serverTick: 10);
        NetworkCommandWireEntry entry = WorldCommand(actorHandle, x: 10);
        NetworkCommandBatchHeader firstHeader = Batch(1, 12, 1);
        NetworkCommandAdmissionOutcome accepted = harness.Ingress.Schedule(
            in seat,
            in firstHeader,
            serverTick: 10,
            new[] { entry });

        Assert.That(harness.Ingress.UnbindSeat(in seat), Is.True);
        NetworkCommandBatchHeader disconnectedHeader = Batch(2, 12, 1);
        Assert.That(
            harness.Ingress.Schedule(in seat, in disconnectedHeader, 10, new[] { entry }).Result,
            Is.EqualTo(OrderSubmitResult.NetworkInvalidConnectionSeat));
        harness.Ingress.RebindSeat(in seat, player, serverTick: 11);
        NetworkCommandAdmissionOutcome replay = harness.Ingress.Schedule(
            in seat,
            in firstHeader,
            serverTick: 11,
            new[] { entry });

        Assert.Multiple(() =>
        {
            Assert.That(accepted.Result, Is.EqualTo(OrderSubmitResult.NetworkScheduled));
            Assert.That(replay.Result, Is.EqualTo(OrderSubmitResult.NetworkScheduled));
            Assert.That(replay.IsReplay, Is.True);
            Assert.That(harness.Ingress.DrainScheduled(12), Is.EqualTo(1));
            Assert.That(harness.Orders.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void Schedule_WhenFixedFutureQueueIsFull_RejectsWholeBatchAndConsumesSequence()
    {
        using World world = World.Create();
        using var harness = Harness.Create(world, scheduledBatchCapacity: 1);
        Entity player = world.Create(new PlayerIdentity { PlayerId = 1 });
        Entity actor = world.Create();
        harness.Ownership.EnsureOwnership(player, actor);
        Assert.That(harness.Entities.TryAllocate(actor, out NetworkEntityHandle handle), Is.True);
        var seat = new NetworkCommandSeat(0, 1, 1);
        harness.Ingress.BindSeat(in seat, player, serverTick: 10);
        NetworkCommandWireEntry entry = WorldCommand(handle, x: 1);
        NetworkCommandBatchHeader firstHeader = Batch(1, 12, 1);
        NetworkCommandBatchHeader secondHeader = Batch(2, 12, 1);

        NetworkCommandAdmissionOutcome first = harness.Ingress.Schedule(in seat, in firstHeader, 10, new[] { entry });
        NetworkCommandAdmissionOutcome full = harness.Ingress.Schedule(in seat, in secondHeader, 10, new[] { entry });
        NetworkCommandAdmissionOutcome replay = harness.Ingress.Schedule(in seat, in secondHeader, 10, new[] { entry });

        Assert.Multiple(() =>
        {
            Assert.That(first.Result, Is.EqualTo(OrderSubmitResult.NetworkScheduled));
            Assert.That(full.Result, Is.EqualTo(OrderSubmitResult.NetworkScheduleFull));
            Assert.That(replay.Result, Is.EqualTo(OrderSubmitResult.NetworkScheduleFull));
            Assert.That(replay.IsReplay, Is.True);
            Assert.That(harness.Ingress.ScheduledBatchCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Schedule_RejectsNonCanonicalPayloadForRegisteredSchema()
    {
        using World world = World.Create();
        using var harness = Harness.Create(world, scheduledBatchCapacity: 2);
        Entity player = world.Create(new PlayerIdentity { PlayerId = 1 });
        Entity actor = world.Create();
        harness.Ownership.EnsureOwnership(player, actor);
        Assert.That(harness.Entities.TryAllocate(actor, out NetworkEntityHandle actorHandle), Is.True);
        var seat = new NetworkCommandSeat(0, 1, 1);
        harness.Ingress.BindSeat(in seat, player, 10);
        var nonCanonicalTarget = new NetworkCommandTargetPayload(
            NetworkCommandTargetKind.WorldPositionCm,
            positionXCm: 1,
            positionYCm: 2,
            positionZCm: 3,
            targetSlot: 4,
            targetGeneration: 1,
            arg0: 0,
            arg1: 0);
        var entry = new NetworkCommandWireEntry(actorHandle, WorldOrderTypeId, in nonCanonicalTarget);
        NetworkCommandBatchHeader header = Batch(1, 10, 1);

        NetworkCommandAdmissionOutcome outcome = harness.Ingress.Schedule(
            in seat,
            in header,
            serverTick: 10,
            new[] { entry });

        Assert.That(outcome.Result, Is.EqualTo(OrderSubmitResult.NetworkCommandSchemaMismatch));
        Assert.That(harness.Ingress.ScheduledBatchCount, Is.Zero);
    }

    [Test]
    public void DrainScheduled_WhenGlobalQueueIsFull_RejectsWholeBatchAtGlobalIntake()
    {
        using World world = World.Create();
        using var harness = Harness.Create(world, scheduledBatchCapacity: 2);
        Entity player = world.Create(new PlayerIdentity { PlayerId = 1 });
        Entity actor = world.Create();
        harness.Ownership.EnsureOwnership(player, actor);
        Assert.That(harness.Entities.TryAllocate(actor, out NetworkEntityHandle actorHandle), Is.True);
        var seat = new NetworkCommandSeat(0, 1, 1);
        harness.Ingress.BindSeat(in seat, player, 10);
        Order filler = new() { OrderTypeId = WorldOrderTypeId };
        while (harness.Orders.TryEnqueue(in filler))
        {
        }

        NetworkCommandWireEntry entry = WorldCommand(actorHandle, x: 10);
        NetworkCommandBatchHeader header = Batch(1, 10, 1);
        Assert.That(
            harness.Ingress.Schedule(in seat, in header, 10, new[] { entry }).Result,
            Is.EqualTo(OrderSubmitResult.NetworkScheduled));
        Assert.That(harness.Ingress.DrainScheduled(10), Is.EqualTo(1));
        Assert.That(harness.Results.TryRead(out _), Is.True);
        Assert.That(harness.Results.TryRead(out NetworkCommandAdmissionOutcome rejected), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(rejected.Result, Is.EqualTo(OrderSubmitResult.QueueFull));
            Assert.That(rejected.Stage, Is.EqualTo(OrderAdmissionStage.GlobalIntake));
            Assert.That(harness.Ingress.ScheduledBatchCount, Is.Zero);
        });
    }

    [Test]
    public void ScheduleAndDrain_SteadyStateAllocatesZeroManagedBytes()
    {
        using World world = World.Create();
        using var harness = Harness.Create(world, scheduledBatchCapacity: 2);
        Entity player = world.Create(new PlayerIdentity { PlayerId = 1 });
        Entity actor = world.Create();
        harness.Ownership.EnsureOwnership(player, actor);
        Assert.That(harness.Entities.TryAllocate(actor, out NetworkEntityHandle actorHandle), Is.True);
        var seat = new NetworkCommandSeat(0, 1, 1);
        harness.Ingress.BindSeat(in seat, player, 1);
        Span<NetworkCommandWireEntry> entries = stackalloc NetworkCommandWireEntry[1];
        Span<Order> drainedOrders = stackalloc Order[1];

        for (ulong sequence = 1; sequence <= 32; sequence++)
        {
            Cycle(harness, in seat, actorHandle, sequence, (int)sequence, entries, drainedOrders);
        }

        _ = GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong sequence = 33; sequence <= 1032; sequence++)
        {
            Cycle(harness, in seat, actorHandle, sequence, (int)sequence, entries, drainedOrders);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
    }

    private const int WorldOrderTypeId = 1;
    private const int EntityOrderTypeId = 2;

    private static NetworkCommandWireEntry WorldCommand(NetworkEntityHandle actor, int x)
    {
        NetworkCommandTargetPayload target = NetworkCommandTargetPayload.FromWorldPositionCm(x, 0, 0);
        return new NetworkCommandWireEntry(actor, WorldOrderTypeId, in target);
    }

    private static NetworkCommandBatchHeader Batch(ulong sequence, int targetTick, ushort entryCount)
        => new(sessionEpoch: 7, sequence, targetTick, acknowledgedCommittedTick: targetTick - 1, entryCount);

    private static void Cycle(
        Harness harness,
        in NetworkCommandSeat seat,
        NetworkEntityHandle actor,
        ulong sequence,
        int tick,
        Span<NetworkCommandWireEntry> entries,
        Span<Order> drainedOrders)
    {
        entries[0] = WorldCommand(actor, tick);
        NetworkCommandBatchHeader header = Batch(sequence, tick, 1);
        if (harness.Ingress.Schedule(in seat, in header, tick, entries).Result != OrderSubmitResult.NetworkScheduled ||
            harness.Ingress.DrainScheduled(tick) != 1 ||
            !harness.Orders.TryDequeueBatch(drainedOrders, out int count) ||
            count != 1 ||
            !harness.Results.TryRead(out NetworkCommandAdmissionOutcome scheduled) ||
            scheduled.Result != OrderSubmitResult.NetworkScheduled ||
            !harness.Results.TryRead(out NetworkCommandAdmissionOutcome queued) ||
            queued.Result != OrderSubmitResult.Queued)
        {
            throw new InvalidOperationException("Network command steady-state test setup failed.");
        }
    }

    private sealed class Harness : IDisposable
    {
        private Harness()
        {
        }

        public required RelationshipRuntime Relationships { get; init; }
        public required OwnershipResolver Ownership { get; init; }
        public required NetworkEntityTable Entities { get; init; }
        public required KnowledgeProjectionStore Knowledge { get; init; }
        public required OrderQueue Orders { get; init; }
        public required NetworkCommandAdmissionResultBuffer Results { get; init; }
        public required NetworkCommandIngress Ingress { get; init; }

        public static Harness Create(
            World world,
            int scheduledBatchCapacity,
            bool includeEntityTargetSchema = false)
        {
            var relationshipTypes = new RelationshipTypeRegistry();
            var relationships = new RelationshipRuntime(
                world,
                relationshipTypes,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(capacity: 64),
                new RelationshipReverseIndex(world));
            int ownsTypeId = relationshipTypes.Register("Owns");
            int controlsTypeId = relationshipTypes.Register("Controls");
            var ownership = new OwnershipResolver(relationships, ownsTypeId);
            var controlDomains = new ControlDomainQuery(
                world,
                relationships,
                ownership,
                ownsTypeId,
                controlsTypeId);
            var entities = new NetworkEntityTable(capacity: 16);
            var knowledge = new KnowledgeProjectionStore(initialCapacity: 16);
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig { Key = "test.world", OrderTypeId = WorldOrderTypeId });
            if (includeEntityTargetSchema)
            {
                orderTypes.Register(new OrderTypeConfig { Key = "test.entity", OrderTypeId = EntityOrderTypeId });
            }

            var schemas = new NetworkCommandSchemaRegistry();
            schemas.Register(new NetworkCommandSchema(
                WorldOrderTypeId,
                NetworkCommandTargetKind.WorldPositionCm,
                allowArg0: false,
                allowArg1: false,
                OrderSubmitMode.Immediate,
                KnowledgePositionAccess.None));
            if (includeEntityTargetSchema)
            {
                schemas.Register(new NetworkCommandSchema(
                    EntityOrderTypeId,
                    NetworkCommandTargetKind.NetworkEntity,
                    allowArg0: false,
                    allowArg1: false,
                    OrderSubmitMode.Immediate,
                    KnowledgePositionAccess.Live));
            }

            schemas.Freeze();
            var orders = new OrderQueue(capacity: 64);
            var results = new NetworkCommandAdmissionResultBuffer(capacity: 64);
            var config = new NetworkCommandIngressConfig(
                seatCapacity: 2,
                simulationTickRateHz: 30,
                maxBatchesPerSecond: 32,
                burstBatchCapacity: 32,
                maxActorsPerBatch: 8,
                sequenceHistoryCapacity: 16,
                maxPastTargetTicks: 3,
                maxFutureTargetTicks: 6,
                scheduledBatchCapacity);
            var ingress = new NetworkCommandIngress(
                in config,
                world,
                entities,
                controlDomains,
                new KnowledgeProjectionResolver(knowledge),
                orderTypes,
                schemas,
                orders,
                results);
            return new Harness
            {
                Relationships = relationships,
                Ownership = ownership,
                Entities = entities,
                Knowledge = knowledge,
                Orders = orders,
                Results = results,
                Ingress = ingress,
            };
        }

        public void Dispose()
        {
        }
    }
}
