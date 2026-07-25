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
        public void Apply_CreatesUpdatesConcealsAndRecreatesRealEcsEntity()
        {
            using World world = World.Create();
            var channel = Channel(capacity: 1);
            var packet = new ReplicationPacketBuffer(1);
            var bridge = Bridge(
                world,
                entityCapacity: 1,
                sessionEpoch: 7,
                out KnowledgeProjectionStore knowledge,
                out Entity viewer);
            var handle = new NetworkEntityHandle(0, 1);
            var states = new[] { State(handle, 1, 10) };
            var visible = new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.LiveVisible) };

            Assert.That(channel.BuildFull(7, 1, 1, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(handle, out Entity created), Is.True);
            Assert.That(world.IsAlive(created), Is.True);
            Assert.That(world.Get<ReplicationMirrorState>(created).Values.Value0, Is.EqualTo(10));
            Assert.That(world.Get<TestAppliedState>(created).Value, Is.EqualTo(10));
            Assert.That(knowledge.TryGet(viewer, created, 1, out KnowledgeDisclosureRecord createdDisclosure), Is.True);
            Assert.That(createdDisclosure.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
            Assert.That(createdDisclosure.Position, Is.EqualTo(KnowledgePositionAccess.Live));

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
            Assert.That(knowledge.TryGet(viewer, created, 3, out _), Is.False);

            states[0] = State(handle, 3, 30);
            Assert.That(channel.BuildDelta(7, 4, 4, 3, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(handle, out Entity recreated), Is.True);
            Assert.That(recreated, Is.Not.EqualTo(created));
            Assert.That(world.Get<ReplicationMirrorState>(recreated).Values.Value0, Is.EqualTo(30));
            Assert.That(knowledge.TryGet(viewer, recreated, 4, out _), Is.True);
        }

        [Test]
        public void Prepare_DoesNotMutateWorldUntilCommitPrepared()
        {
            using World world = World.Create();
            var channel = Channel(capacity: 1);
            var packet = new ReplicationPacketBuffer(1);
            var bridge = Bridge(world, entityCapacity: 1, sessionEpoch: 7);
            var handle = new NetworkEntityHandle(0, 1);
            var states = new[] { State(handle, 1, 10) };
            var visible = new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.LiveVisible) };

            Assert.That(channel.BuildFull(7, 1, 1, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Prepare(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.HasPreparedBatch, Is.True);
            Assert.That(bridge.LastSnapshotId, Is.Zero);
            Assert.That(bridge.TryResolve(handle, out _), Is.False);

            Assert.That(bridge.CommitPrepared(), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.LastSnapshotId, Is.EqualTo(1));
            Assert.That(bridge.TryResolve(handle, out Entity created), Is.True);
            Assert.That(world.Get<TestAppliedState>(created).Value, Is.EqualTo(10));

            states[0] = State(handle, 2, 20);
            Assert.That(channel.BuildDelta(7, 2, 2, 1, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Prepare(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.LastSnapshotId, Is.EqualTo(1));
            Assert.That(world.Get<TestAppliedState>(created).Value, Is.EqualTo(10));

            Assert.That(bridge.CommitPrepared(), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.LastSnapshotId, Is.EqualTo(2));
            Assert.That(world.Get<TestAppliedState>(created).Value, Is.EqualTo(20));

            var remembered = new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.Known) };
            Assert.That(channel.BuildDelta(7, 3, 3, 2, states, remembered, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Prepare(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(handle, out Entity beforeConceal), Is.True);
            Assert.That(beforeConceal, Is.EqualTo(created));
            Assert.That(world.IsAlive(created), Is.True);

            Assert.That(bridge.CommitPrepared(), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(handle, out _), Is.False);
            Assert.That(world.IsAlive(created), Is.False);
        }

        [Test]
        public void DiscardPrepared_DoesNotAdvanceMirrorBaseline()
        {
            using World world = World.Create();
            var channel = Channel(capacity: 1);
            var packet = new ReplicationPacketBuffer(1);
            var bridge = Bridge(world, entityCapacity: 1, sessionEpoch: 7);
            var handle = new NetworkEntityHandle(0, 1);
            var states = new[] { State(handle, 1, 10) };
            var visible = new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.LiveVisible) };

            Assert.That(channel.BuildFull(7, 1, 1, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));

            states[0] = State(handle, 2, 20);
            Assert.That(channel.BuildDelta(7, 2, 2, 1, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Prepare(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            bridge.DiscardPrepared();

            states[0] = State(handle, 3, 30);
            Assert.That(channel.BuildDelta(7, 3, 3, 1, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.LastSnapshotId, Is.EqualTo(3));
            Assert.That(bridge.TryResolve(handle, out Entity entity), Is.True);
            Assert.That(world.Get<TestAppliedState>(entity).Value, Is.EqualTo(30));
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
        public void BindExisting_UpdatesAuthoredEntityAndNeverDestroysItWhenReplicationLeaves()
        {
            using World world = World.Create();
            Entity authored = world.Create(new AuthoredMapEntity(marker: 77), new TestAppliedState(value: 0));
            var handle = new NetworkEntityHandle(0, 1);
            var bridge = Bridge(world, entityCapacity: 1, sessionEpoch: 7);
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
            var bridge = Bridge(world, 1, 7);

            Assert.That(bridge.Apply(delta), Is.EqualTo(ReplicationBridgeResult.ResyncRequired));
            Assert.That(bridge.TryResolve(handle, out _), Is.False);
        }

        [Test]
        public void Apply_MissingClientSchemaFailsBeforeMirrorOrWorldMutation()
        {
            using World world = World.Create();
            var appliers = new ClientReplicationSchemaApplierRegistry(schemaCapacity: 1);
            appliers.Freeze();
            Entity viewer = world.Create();
            var bridge = new ClientWorldReplicationBridge(
                world,
                1,
                7,
                appliers,
                new KnowledgeProjectionStore(initialCapacity: 1),
                viewer);
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
            => new(capacity, baselineCapacity: 2, new ReplicationDisclosureChangeLog(capacity * 4));

        private static ClientWorldReplicationBridge Bridge(World world, int entityCapacity, ulong sessionEpoch)
            => Bridge(world, entityCapacity, sessionEpoch, out _, out _);

        private static ClientWorldReplicationBridge Bridge(
            World world,
            int entityCapacity,
            ulong sessionEpoch,
            out KnowledgeProjectionStore knowledge,
            out Entity viewer)
        {
            var appliers = new ClientReplicationSchemaApplierRegistry(schemaCapacity: 1);
            Assert.That(appliers.Register(1, new TestSchemaApplier()), Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
            appliers.Freeze();
            knowledge = new KnowledgeProjectionStore(initialCapacity: entityCapacity);
            viewer = world.Create();
            return new ClientWorldReplicationBridge(
                world,
                entityCapacity,
                sessionEpoch,
                appliers,
                knowledge,
                viewer);
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

        private sealed class TestSchemaApplier : IClientReplicationSchemaApplier
        {
            public bool CanCreate(World world, in ReplicatedEntityState state) => true;

            public bool CanApply(World world, Entity entity, in ReplicatedEntityState state)
                => world.Has<TestAppliedState>(entity);

            public bool CanConceal(World world, Entity entity)
                => world.Has<TestAppliedState>(entity);

            public Entity Create(
                World world,
                in ReplicationMirrorIdentity identity,
                in ReplicationMirrorState state)
            {
                var applied = new TestAppliedState(state.Values.Value0);
                return world.Create(in identity, in state, in applied);
            }

            public void Apply(World world, Entity entity, in ReplicatedEntityState state)
            {
                world.Set(entity, new TestAppliedState(state.Values.Value0));
            }

            public void Conceal(World world, Entity entity)
            {
                world.Set(entity, new TestAppliedState(value: 0));
            }
        }
    }
}
