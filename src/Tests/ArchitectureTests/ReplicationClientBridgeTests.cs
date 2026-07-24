using Arch.Core;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Replication;
using NUnit.Framework;
using System.Runtime.CompilerServices;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class ReplicationClientBridgeTests
    {
        [Test]
        public void Apply_CreatesUpdatesConcealsAndRecreatesOwnedEntity()
        {
            using World world = World.Create();
            var channel = Channel(capacity: 1);
            var packet = new ReplicationPacketBuffer(1);
            var tracking = new TrackingSchemaApplier();
            var bridge = Bridge(world, entityCapacity: 1, sessionEpoch: 7, tracking);
            var handle = new NetworkEntityHandle(0, 1);
            var states = new[] { State(handle, 1, 10) };
            var visible = new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.LiveVisible) };

            Assert.That(channel.BuildFull(7, 1, 1, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(handle, out Entity created), Is.True);
            Assert.That(world.IsAlive(created), Is.True);
            Assert.That(world.Get<ReplicationMirrorState>(created).Values.Value0, Is.EqualTo(10));
            Assert.That(world.Get<TestAppliedState>(created).Value, Is.EqualTo(10));

            states[0] = State(handle, 2, 20);
            Assert.That(channel.BuildDelta(7, 2, 2, 1, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(handle, out Entity updated), Is.True);
            Assert.That(updated, Is.EqualTo(created));
            Assert.That(world.Get<ReplicationMirrorState>(updated).Values.Value0, Is.EqualTo(20));
            Assert.That(world.Get<TestAppliedState>(updated).Value, Is.EqualTo(20));

            var remembered = new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.Known) };
            Assert.That(channel.BuildDelta(7, 3, 3, 2, states, remembered, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(handle, out _), Is.False);
            Assert.That(world.IsAlive(created), Is.False);
            Assert.That(tracking.ReleaseCalls, Is.EqualTo(1));
            Assert.That(tracking.LastLeaveKind, Is.EqualTo(ReplicationMirrorLeaveKind.Conceal));

            states[0] = State(handle, 3, 30);
            Assert.That(channel.BuildDelta(7, 4, 4, 3, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(handle, out Entity revealed), Is.True);
            Assert.That(revealed, Is.Not.EqualTo(created));
            Assert.That(world.Get<ReplicationMirrorState>(revealed).Values.Value0, Is.EqualTo(30));
            Assert.Multiple(() =>
            {
                Assert.That(tracking.LastContext.SessionEpoch, Is.EqualTo(7));
                Assert.That(tracking.LastContext.CommittedTick, Is.EqualTo(4));
                Assert.That(tracking.LastContext.SnapshotId, Is.EqualTo(4));
                Assert.That(tracking.LastContext.PacketKind, Is.EqualTo(ReplicationPacketKind.Delta));
            });
        }

        [Test]
        public void Apply_PermanentRemovalReleasesOwnedMirrorWithoutCallingConceal()
        {
            using World world = World.Create();
            var channel = Channel(capacity: 1);
            var packet = new ReplicationPacketBuffer(1);
            var tracking = new TrackingSchemaApplier();
            var bridge = Bridge(world, 1, 7, tracking);
            var first = new NetworkEntityHandle(0, 1);
            var second = new NetworkEntityHandle(0, 2);
            var states = new[] { State(first, 1, 10) };
            var disclosures = new[] { new ReplicationDisclosureInput(first, KnowledgePresence.LiveVisible) };

            Assert.That(channel.BuildFull(7, 1, 1, states, disclosures, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(first, out Entity oldEntity), Is.True);

            states[0] = State(second, 1, 20);
            disclosures[0] = new ReplicationDisclosureInput(second, KnowledgePresence.LiveVisible);
            Assert.That(channel.BuildDelta(7, 2, 2, 1, states, disclosures, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(world.IsAlive(oldEntity), Is.False);
            Assert.That(tracking.ReleaseCalls, Is.EqualTo(1));
            Assert.That(tracking.LastLeaveKind, Is.EqualTo(ReplicationMirrorLeaveKind.Removal));
            Assert.That(bridge.TryResolve(second, out _), Is.True);
        }

        [Test]
        public void Apply_ReusesSlotForNewGenerationAndRejectsOlderGenerationWithoutMutation()
        {
            using World world = World.Create();
            var channel = Channel(capacity: 1);
            var packet = new ReplicationPacketBuffer(1);
            var bridge = Bridge(world, entityCapacity: 1, sessionEpoch: 7);
            var first = new NetworkEntityHandle(0, 1);
            var second = new NetworkEntityHandle(0, 2);
            var states = new[] { State(first, 1, 10) };
            var disclosures = new[] { new ReplicationDisclosureInput(first, KnowledgePresence.LiveVisible) };

            Assert.That(channel.BuildFull(7, 1, 1, states, disclosures, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(first, out Entity oldEntity), Is.True);

            states[0] = State(second, 1, 20);
            disclosures[0] = new ReplicationDisclosureInput(second, KnowledgePresence.LiveVisible);
            Assert.That(channel.BuildDelta(7, 2, 2, 1, states, disclosures, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(first, out _), Is.False);
            Assert.That(bridge.TryResolve(second, out Entity currentEntity), Is.True);
            Assert.That(world.IsAlive(oldEntity), Is.False);

            states[0] = State(first, 2, 999);
            disclosures[0] = new ReplicationDisclosureInput(first, KnowledgePresence.LiveVisible);
            Assert.That(channel.BuildDelta(7, 3, 3, 2, states, disclosures, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.ResyncRequired));
            Assert.That(bridge.TryResolve(second, out Entity unchanged), Is.True);
            Assert.That(unchanged, Is.EqualTo(currentEntity));
            Assert.That(world.Get<ReplicationMirrorState>(unchanged).Values.Value0, Is.EqualTo(20));
        }

        [Test]
        public void BindExisting_ConcealsAndUnbindsBorrowedEntityWithoutDestroyingIt()
        {
            using World world = World.Create();
            Entity authored = world.Create(new AuthoredMapEntity(marker: 77), new TestAppliedState(value: 0));
            var handle = new NetworkEntityHandle(0, 1);
            var tracking = new TrackingSchemaApplier();
            var bridge = Bridge(world, entityCapacity: 1, sessionEpoch: 7, tracking);
            Assert.That(bridge.BindExisting(handle, authored), Is.EqualTo(ReplicationBridgeResult.Success));

            var channel = Channel(capacity: 1);
            var packet = new ReplicationPacketBuffer(1);
            var states = new[] { State(handle, 1, 55) };
            var visible = new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.LiveVisible) };
            Assert.That(channel.BuildFull(7, 1, 1, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(handle, out Entity resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(authored));
            Assert.That(world.Get<ReplicationMirrorState>(authored).Values.Value0, Is.EqualTo(55));
            Assert.That(world.Get<TestAppliedState>(authored).Value, Is.EqualTo(55));

            Assert.That(
                channel.BuildDelta(7, 2, 2, 1, states,
                    new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.Known) }, packet),
                Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(world.IsAlive(authored), Is.True);
            Assert.That(world.Get<AuthoredMapEntity>(authored).Marker, Is.EqualTo(77));
            Assert.That(world.Has<ReplicationMirrorIdentity>(authored), Is.False);
            Assert.That(world.Has<ReplicationMirrorState>(authored), Is.False);
            Assert.That(world.Get<TestAppliedState>(authored).Value, Is.EqualTo(0));
            Assert.That(bridge.TryResolve(handle, out _), Is.False);
            Assert.That(tracking.ReleaseCalls, Is.EqualTo(1));
            Assert.That(tracking.LastLeaveKind, Is.EqualTo(ReplicationMirrorLeaveKind.Conceal));
        }

        [Test]
        public void BindExisting_PermanentRemovalReleasesBorrowedEntityExactlyOnce()
        {
            using World world = World.Create();
            Entity authored = world.Create(new AuthoredMapEntity(marker: 88), new TestAppliedState(value: 0));
            var handle = new NetworkEntityHandle(0, 1);
            var tracking = new TrackingSchemaApplier();
            var bridge = Bridge(world, entityCapacity: 1, sessionEpoch: 7, tracking);
            Assert.That(bridge.BindExisting(handle, authored), Is.EqualTo(ReplicationBridgeResult.Success));

            var channel = Channel(capacity: 1);
            var packet = new ReplicationPacketBuffer(1);
            var visible = new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.LiveVisible) };
            Assert.That(
                channel.BuildFull(7, 1, 1, new[] { State(handle, 1, 55) }, visible, packet),
                Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(
                channel.BuildDelta(
                    7,
                    2,
                    2,
                    1,
                    ReadOnlySpan<ReplicatedEntityState>.Empty,
                    visible,
                    packet),
                Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(packet.Removals.ToArray(), Is.EqualTo(new[] { handle }));

            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(world.IsAlive(authored), Is.True);
            Assert.That(world.Has<ReplicationMirrorIdentity>(authored), Is.False);
            Assert.That(bridge.TryResolve(handle, out _), Is.False);
            Assert.That(tracking.ReleaseCalls, Is.EqualTo(1));
            Assert.That(tracking.LastLeaveKind, Is.EqualTo(ReplicationMirrorLeaveKind.Removal));
        }

        [Test]
        public void Teardown_ReleasesOwnedAndBorrowedThenIsIdempotent()
        {
            using World world = World.Create();
            Entity authored = world.Create(new AuthoredMapEntity(marker: 9), new TestAppliedState(value: 0));
            var ownedHandle = new NetworkEntityHandle(0, 1);
            var borrowedHandle = new NetworkEntityHandle(1, 1);
            var tracking = new TrackingSchemaApplier();
            var bridge = Bridge(world, entityCapacity: 2, sessionEpoch: 7, tracking);
            Assert.That(bridge.BindExisting(borrowedHandle, authored), Is.EqualTo(ReplicationBridgeResult.Success));

            var channel = Channel(capacity: 2);
            var packet = new ReplicationPacketBuffer(2);
            var states = new[]
            {
                State(ownedHandle, 1, 11),
                State(borrowedHandle, 1, 22),
            };
            var visible = new[]
            {
                new ReplicationDisclosureInput(ownedHandle, KnowledgePresence.LiveVisible),
                new ReplicationDisclosureInput(borrowedHandle, KnowledgePresence.LiveVisible),
            };
            Assert.That(channel.BuildFull(7, 1, 1, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(ownedHandle, out Entity owned), Is.True);

            tracking.RejectedReleaseValue = 22;
            Assert.That(bridge.Teardown(), Is.EqualTo(ReplicationBridgeResult.SchemaApplyRejected));
            Assert.That(bridge.IsTornDown, Is.False);
            Assert.That(bridge.TryResolve(ownedHandle, out Entity unchangedOwned), Is.True);
            Assert.That(unchangedOwned, Is.EqualTo(owned));
            Assert.That(bridge.TryResolve(borrowedHandle, out Entity unchangedBorrowed), Is.True);
            Assert.That(unchangedBorrowed, Is.EqualTo(authored));
            Assert.That(tracking.ReleaseCalls, Is.Zero);

            tracking.RejectedReleaseValue = long.MinValue;

            Assert.That(bridge.Teardown(), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.IsTornDown, Is.True);
            Assert.That(world.IsAlive(owned), Is.False);
            Assert.That(world.IsAlive(authored), Is.True);
            Assert.That(world.Has<ReplicationMirrorIdentity>(authored), Is.False);
            Assert.That(tracking.ReleaseCalls, Is.EqualTo(2));
            Assert.That(tracking.TeardownReleaseCalls, Is.EqualTo(2));
            Assert.That(bridge.TryResolve(ownedHandle, out _), Is.False);
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.TornDown));
            Assert.That(bridge.Teardown(), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(tracking.ReleaseCalls, Is.EqualTo(2));
        }

        [Test]
        public void Apply_MapsBaselineMismatchToResyncRequired()
        {
            using World world = World.Create();
            var channel = Channel(capacity: 1);
            var full = new ReplicationPacketBuffer(1);
            var delta = new ReplicationPacketBuffer(1);
            var handle = new NetworkEntityHandle(0, 1);
            var states = new[] { State(handle, 1, 10) };
            var visible = new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.LiveVisible) };
            Assert.That(channel.BuildFull(7, 1, 1, states, visible, full), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(channel.BuildDelta(7, 2, 2, 1, states, visible, delta), Is.EqualTo(ReplicationBuildResult.Success));
            var tracking = new TrackingSchemaApplier();
            var bridge = Bridge(world, 1, 7, tracking);

            Assert.That(bridge.Apply(delta), Is.EqualTo(ReplicationBridgeResult.ResyncRequired));
            Assert.That(bridge.TryResolve(handle, out _), Is.False);
            Assert.That(tracking.ValidationCalls, Is.Zero);
            Assert.That(tracking.CreateCalls, Is.Zero);
        }

        [Test]
        public void Apply_MissingClientSchemaFailsBeforeMirrorOrWorldMutation()
        {
            using World world = World.Create();
            var appliers = new ClientReplicationSchemaApplierRegistry(schemaCapacity: 1);
            appliers.Freeze();
            var bridge = new ClientWorldReplicationBridge(world, 1, 7, appliers);
            var handle = new NetworkEntityHandle(0, 1);
            var packet = new ReplicationPacketBuffer(1);
            var channel = Channel(capacity: 1);
            Assert.That(
                channel.BuildFull(
                    7,
                    1,
                    1,
                    new[] { State(handle, 1, 10) },
                    new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.LiveVisible) },
                    packet),
                Is.EqualTo(ReplicationBuildResult.Success));

            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.SchemaNotRegistered));
            Assert.That(bridge.LastSnapshotId, Is.EqualTo(0));
            Assert.That(bridge.TryResolve(handle, out _), Is.False);
            var query = new QueryDescription().WithAll<ReplicationMirrorIdentity>();
            Assert.That(world.CountEntities(in query), Is.EqualTo(0));
        }

        [Test]
        public void MirrorComponents_AreBlittable()
        {
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<ReplicationMirrorIdentity>(), Is.False);
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<ReplicationMirrorState>(), Is.False);
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<ReplicationApplyContext>(), Is.False);
        }

        [Test]
        public void ApplyUpdate_IsZeroAllocForTenThousandOperationsAfterWarmup()
        {
            using World world = World.Create();
            var channel = Channel(capacity: 1);
            var packet = new ReplicationPacketBuffer(1);
            var bridge = Bridge(world, 1, 7);
            var handle = new NetworkEntityHandle(0, 1);
            var states = new[] { State(handle, 1, 1) };
            var visible = new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.LiveVisible) };
            Assert.That(channel.BuildFull(7, 1, 1, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            ulong baseline = 1;
            bool succeeded = true;

            for (uint i = 2; i < 258; i++)
            {
                states[0] = State(handle, i, i);
                succeeded &= channel.BuildDelta(7, i, i, baseline, states, visible, packet) == ReplicationBuildResult.Success;
                succeeded &= bridge.Apply(packet) == ReplicationBridgeResult.Success;
                baseline = i;
            }

            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (uint i = 258; i < 10_258; i++)
            {
                states[0] = State(handle, i, i);
                succeeded &= channel.BuildDelta(7, i, i, baseline, states, visible, packet) == ReplicationBuildResult.Success;
                succeeded &= bridge.Apply(packet) == ReplicationBridgeResult.Success;
                baseline = i;
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(succeeded, Is.True);
            Assert.That(allocated, Is.EqualTo(0));
            Assert.That(bridge.TryResolve(handle, out Entity entity), Is.True);
            Assert.That(world.Get<ReplicationMirrorState>(entity).Revision, Is.EqualTo(10_257));
        }

        private static AuthoritativeReplicationChannel Channel(int capacity)
        {
            using World world = World.Create();
            var entities = new NetworkEntityTable(capacity);
            for (int i = 0; i < capacity; i++)
            {
                if (!entities.TryAllocate(world.Create(), out NetworkEntityHandle handle) || handle.Slot != i)
                {
                    throw new InvalidOperationException("Failed to seed the authoritative entity table for replication tests.");
                }
            }

            return new AuthoritativeReplicationChannel(
                entities,
                capacity,
                baselineCapacity: 2,
                new ReplicationDisclosureChangeLog(capacity * 4));
        }

        private static ClientWorldReplicationBridge Bridge(
            World world,
            int entityCapacity,
            ulong sessionEpoch,
            TrackingSchemaApplier? tracking = null)
        {
            var appliers = new ClientReplicationSchemaApplierRegistry(schemaCapacity: 1);
            Assert.That(
                appliers.Register(1, tracking ?? new TrackingSchemaApplier()),
                Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
            appliers.Freeze();
            return new ClientWorldReplicationBridge(world, entityCapacity, sessionEpoch, appliers);
        }

        private static ReplicatedEntityState State(NetworkEntityHandle handle, uint revision, long value)
            => new(handle, schemaId: 1, revision, new ReplicationStateVector(value, 0, 0, 0));

        private readonly struct AuthoredMapEntity
        {
            public AuthoredMapEntity(int marker) => Marker = marker;

            public readonly int Marker;
        }

        private readonly struct TestAppliedState
        {
            public TestAppliedState(long value) => Value = value;

            public readonly long Value;
        }

        private sealed class TrackingSchemaApplier : IClientReplicationSchemaApplier
        {
            public int ValidationCalls { get; private set; }
            public int CreateCalls { get; private set; }
            public int ReleaseCalls { get; private set; }
            public int TeardownReleaseCalls { get; private set; }
            public ReplicationMirrorLeaveKind LastLeaveKind { get; private set; }
            public ReplicationApplyContext LastContext { get; private set; }
            public long RejectedReleaseValue { get; set; } = long.MinValue;

            public bool CanCreate(World world, in ReplicatedEntityState state, in ReplicationApplyContext context)
            {
                ValidationCalls++;
                return context.SessionEpoch != 0;
            }

            public bool CanApply(World world, Entity entity, in ReplicatedEntityState state, in ReplicationApplyContext context)
            {
                ValidationCalls++;
                return world.Has<TestAppliedState>(entity);
            }

            public bool CanRelease(
                World world,
                Entity entity,
                ReplicationMirrorLeaveKind leaveKind,
                in ReplicationApplyContext context)
            {
                ValidationCalls++;
                return world.TryGet(entity, out TestAppliedState state) &&
                    state.Value != RejectedReleaseValue &&
                    leaveKind != 0;
            }

            public Entity Create(
                World world,
                in ReplicationMirrorIdentity identity,
                in ReplicationMirrorState state,
                in ReplicationApplyContext context)
            {
                CreateCalls++;
                LastContext = context;
                var applied = new TestAppliedState(state.Values.Value0);
                return world.Create(in identity, in state, in applied);
            }

            public void Apply(World world, Entity entity, in ReplicatedEntityState state, in ReplicationApplyContext context)
            {
                LastContext = context;
                world.Set(entity, new TestAppliedState(state.Values.Value0));
            }

            public void Release(
                World world,
                Entity entity,
                ReplicationMirrorLeaveKind leaveKind,
                in ReplicationApplyContext context)
            {
                ReleaseCalls++;
                LastLeaveKind = leaveKind;
                LastContext = context;
                if (leaveKind == ReplicationMirrorLeaveKind.Teardown)
                {
                    TeardownReleaseCalls++;
                }
                world.Set(entity, new TestAppliedState(value: 0));
            }
        }
    }
}
