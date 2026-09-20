using Arch.Core;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Replication;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class ReplicationServerBridgeTests
    {
        [Test]
        public void Projection_TracksEnterLeaveAndReenterVisibilityWithoutLeakingHiddenState()
        {
            using World world = World.Create();
            Entity viewer = world.Create();
            Entity target = world.Create(
                new ReplicationSchemaRef(schemaId: 1),
                new TestReplicatedData(revision: 7, value: 41));
            var table = new NetworkEntityTable(capacity: 2);
            Assert.That(table.TryAllocate(target, out NetworkEntityHandle handle), Is.True);
            var knowledge = new KnowledgeProjectionStore(initialCapacity: 2);
            var bridge = CreateBridge(world, table, knowledge, viewer, capacity: 2);
            var output = new ReplicationProjectionBuffer(entityCapacity: 2);
            var handles = new[] { handle };

            knowledge.Upsert(viewer, target, Disclosure(KnowledgePresence.LiveVisible));
            Assert.That(bridge.Project(handles, currentTick: 10, output), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(output.States.Length, Is.EqualTo(1));
            Assert.That(output.States[0].Values.Value0, Is.EqualTo(41));
            Assert.That(output.Disclosures.Length, Is.EqualTo(1));
            Assert.That(output.Disclosures[0].CanReplicateLiveState, Is.True);

            knowledge.Upsert(viewer, target, Disclosure(KnowledgePresence.Known));
            Assert.That(bridge.Project(handles, currentTick: 11, output), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(output.States.Length, Is.EqualTo(0));
            Assert.That(output.Disclosures.Length, Is.EqualTo(1));
            Assert.That(output.Disclosures[0].CanReplicateLiveState, Is.False);

            world.Set(target, new TestReplicatedData(revision: 8, value: 99));
            knowledge.Upsert(viewer, target, Disclosure(KnowledgePresence.LiveVisible));
            Assert.That(bridge.Project(handles, currentTick: 12, output), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(output.States.Length, Is.EqualTo(1));
            Assert.That(output.States[0].Revision, Is.EqualTo(8));
            Assert.That(output.States[0].Values.Value0, Is.EqualTo(99));
        }

        [Test]
        public void Projection_ConcealsUnknownAndExpired_AndFailsClosedForBrokenVisibleState()
        {
            using World world = World.Create();
            Entity viewer = world.Create();
            Entity target = world.Create(
                new ReplicationSchemaRef(schemaId: 1),
                new TestReplicatedData(revision: 1, value: 123));
            Entity missingSchema = world.Create(new TestReplicatedData(revision: 1, value: 456));
            var table = new NetworkEntityTable(capacity: 2);
            Assert.That(table.TryAllocate(target, out NetworkEntityHandle targetHandle), Is.True);
            Assert.That(table.TryAllocate(missingSchema, out NetworkEntityHandle missingSchemaHandle), Is.True);
            var knowledge = new KnowledgeProjectionStore(initialCapacity: 4);
            var output = new ReplicationProjectionBuffer(entityCapacity: 2);
            var bridge = CreateBridge(world, table, knowledge, viewer, capacity: 2);

            knowledge.Upsert(viewer, target, Disclosure(KnowledgePresence.Unknown));
            Assert.That(bridge.Project(new[] { targetHandle }, 10, output), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(output.States.Length, Is.EqualTo(0));
            Assert.That(output.Disclosures.Length, Is.EqualTo(1));
            Assert.That(output.Disclosures[0].Presence, Is.EqualTo(KnowledgePresence.Unknown));

            knowledge.Upsert(viewer, target, Disclosure(KnowledgePresence.LiveVisible, expiryTick: 12));
            Assert.That(bridge.Project(new[] { targetHandle }, 12, output), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(output.States.Length, Is.EqualTo(0));
            Assert.That(output.Disclosures.Length, Is.EqualTo(1));
            Assert.That(output.Disclosures[0].Presence, Is.EqualTo(KnowledgePresence.Unknown));

            knowledge.Upsert(viewer, missingSchema, Disclosure(KnowledgePresence.LiveVisible));
            Assert.That(bridge.Project(new[] { missingSchemaHandle }, 10, output), Is.EqualTo(ReplicationBridgeResult.SchemaMissing));
            Assert.That(output.States.Length, Is.EqualTo(0));

            var failingRegistry = new ReplicationSchemaProjectorRegistry(schemaCapacity: 1);
            Assert.That(failingRegistry.Register(1, new FailingProjector()), Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
            failingRegistry.Freeze();
            var failingBridge = new AuthoritativeWorldReplicationBridge(world, table, knowledge, viewer, failingRegistry, entityCapacity: 2);
            knowledge.Upsert(viewer, target, Disclosure(KnowledgePresence.LiveVisible));
            Assert.That(failingBridge.Project(new[] { targetHandle }, 10, output), Is.EqualTo(ReplicationBridgeResult.ProjectionFailed));
            Assert.That(output.States.Length, Is.EqualTo(0));
        }

        [Test]
        public void Projection_ReportsMissingEntityAndCapacityContractViolationExplicitly()
        {
            using World world = World.Create();
            Entity viewer = world.Create();
            Entity first = world.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 10));
            Entity second = world.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 20));
            var table = new NetworkEntityTable(capacity: 2);
            Assert.That(table.TryAllocate(first, out NetworkEntityHandle firstHandle), Is.True);
            Assert.That(table.TryAllocate(second, out NetworkEntityHandle secondHandle), Is.True);
            var knowledge = new KnowledgeProjectionStore(initialCapacity: 2);
            knowledge.Upsert(viewer, first, Disclosure(KnowledgePresence.LiveVisible));
            knowledge.Upsert(viewer, second, Disclosure(KnowledgePresence.LiveVisible));
            var bridge = CreateBridge(world, table, knowledge, viewer, capacity: 2);

            var tooSmall = new ReplicationProjectionBuffer(entityCapacity: 1);
            Assert.That(
                bridge.Project(new[] { firstHandle, secondHandle }, 10, tooSmall),
                Is.EqualTo(ReplicationBridgeResult.CapacityContractViolated));
            Assert.That(tooSmall.States.Length, Is.EqualTo(0));

            Assert.That(table.TryRelease(firstHandle), Is.True);
            var output = new ReplicationProjectionBuffer(entityCapacity: 2);
            Assert.That(bridge.Project(new[] { firstHandle }, 10, output), Is.EqualTo(ReplicationBridgeResult.EntityUnavailable));
            Assert.That(output.States.Length, Is.EqualTo(0));
        }

        [Test]
        public void BuildDelta_MapsMissingBaselineToResyncRequired()
        {
            using World world = World.Create();
            Entity viewer = world.Create();
            Entity target = world.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 10));
            var table = new NetworkEntityTable(capacity: 1);
            Assert.That(table.TryAllocate(target, out NetworkEntityHandle handle), Is.True);
            var knowledge = new KnowledgeProjectionStore(initialCapacity: 1);
            knowledge.Upsert(viewer, target, Disclosure(KnowledgePresence.LiveVisible));
            var bridge = CreateBridge(world, table, knowledge, viewer, capacity: 1);
            var output = new ReplicationProjectionBuffer(entityCapacity: 1);
            var channel = new AuthoritativeReplicationChannel(1, 1, new ReplicationDisclosureChangeLog(2));
            var packet = new ReplicationPacketBuffer(1);

            Assert.That(
                bridge.BuildDelta(channel, sessionEpoch: 7, tick: 10, snapshotId: 2, acknowledgedBaselineId: 99,
                    new[] { handle }, output, packet),
                Is.EqualTo(ReplicationBridgeResult.ResyncRequired));
            Assert.That(output.States.Length, Is.EqualTo(0));
            Assert.That(packet.Upserts.Length, Is.EqualTo(0));
        }

        [Test]
        public void Projection_IsZeroAllocForTenThousandOperationsAfterWarmup()
        {
            using World world = World.Create();
            Entity viewer = world.Create();
            Entity target = world.Create(new ReplicationSchemaRef(1), new TestReplicatedData(1, 10));
            var table = new NetworkEntityTable(capacity: 1);
            Assert.That(table.TryAllocate(target, out NetworkEntityHandle handle), Is.True);
            var knowledge = new KnowledgeProjectionStore(initialCapacity: 1);
            knowledge.Upsert(viewer, target, Disclosure(KnowledgePresence.LiveVisible));
            var bridge = CreateBridge(world, table, knowledge, viewer, capacity: 1);
            var output = new ReplicationProjectionBuffer(entityCapacity: 1);
            var handles = new[] { handle };
            bool succeeded = true;

            for (int i = 0; i < 256; i++)
            {
                succeeded &= bridge.Project(handles, i, output) == ReplicationBridgeResult.Success;
            }

            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                succeeded &= bridge.Project(handles, i, output) == ReplicationBridgeResult.Success;
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(succeeded, Is.True);
            Assert.That(allocated, Is.EqualTo(0));
        }

        private static AuthoritativeWorldReplicationBridge CreateBridge(
            World world,
            NetworkEntityTable table,
            KnowledgeProjectionStore knowledge,
            Entity viewer,
            int capacity)
        {
            var registry = new ReplicationSchemaProjectorRegistry(schemaCapacity: 1);
            Assert.That(registry.Register(1, new TestProjector()), Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
            registry.Freeze();
            return new AuthoritativeWorldReplicationBridge(world, table, knowledge, viewer, registry, capacity);
        }

        private static KnowledgeDisclosureRecord Disclosure(KnowledgePresence presence, int expiryTick = 0)
            => new(
                presence,
                presence == KnowledgePresence.LiveVisible ? KnowledgePositionAccess.Live : KnowledgePositionAccess.None,
                default,
                default,
                default,
                Entity.Null,
                observedTick: 1,
                expiryTick,
                confidencePermille: 1000,
                revision: 0);

        private readonly struct TestReplicatedData
        {
            public TestReplicatedData(uint revision, long value)
            {
                Revision = revision;
                Value = value;
            }

            public readonly uint Revision;
            public readonly long Value;
        }

        private sealed class TestProjector : IReplicationSchemaProjector
        {
            public bool TryProject(
                World world,
                Entity entity,
                in KnowledgeDisclosureRecord disclosure,
                out ReplicationProjectedState state)
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

        private sealed class FailingProjector : IReplicationSchemaProjector
        {
            public bool TryProject(
                World world,
                Entity entity,
                in KnowledgeDisclosureRecord disclosure,
                out ReplicationProjectedState state)
            {
                state = default;
                return false;
            }
        }
    }
}
