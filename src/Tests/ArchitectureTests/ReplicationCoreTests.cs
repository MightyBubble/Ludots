using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Replication;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class ReplicationCoreTests
    {
        [Test]
        public void FullSnapshot_ContainsOnlyDisclosedEntitiesAndReplacesClientMirror()
        {
            var visible = new NetworkEntityHandle(slot: 0, generation: 1);
            var hidden = new NetworkEntityHandle(slot: 1, generation: 1);
            var states = new[]
            {
                State(visible, revision: 10, value: 100),
                State(hidden, revision: 20, value: 200),
            };
            var disclosures = new[]
            {
                new ReplicationDisclosureInput(visible, KnowledgePresence.LiveVisible),
                new ReplicationDisclosureInput(hidden, KnowledgePresence.HiddenWithSource),
            };
            var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 4);
            var channel = new AuthoritativeReplicationChannel(
                new NetworkEntityTable(capacity: 4),
                replicationEntityCapacityPerSeat: 4,
                baselineCapacity: 2,
                disclosureLog);
            var packet = new ReplicationPacketBuffer(entityCapacity: 4);

            ReplicationBuildResult built = channel.BuildFull(
                sessionEpoch: 7,
                tick: 100,
                snapshotId: 1,
                states,
                disclosures,
                packet);

            Assert.That(built, Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(packet.Header.Kind, Is.EqualTo(ReplicationPacketKind.Full));
            Assert.That(packet.Upserts.Length, Is.EqualTo(1));
            Assert.That(packet.Upserts[0], Is.EqualTo(states[0]));
            Assert.That(packet.Removals.Length, Is.EqualTo(0));
            Assert.That(packet.DisclosureChanges.Length, Is.EqualTo(1));
            Assert.That(packet.DisclosureChanges[0].Kind, Is.EqualTo(ReplicationDisclosureChangeKind.Reveal));

            var mirror = new ClientReplicationMirror(entityCapacity: 4, sessionEpoch: 7);
            Assert.That(mirror.Apply(packet), Is.EqualTo(ReplicationApplyResult.Success));
            Assert.That(mirror.TryGet(visible, out ReplicatedEntityState mirrored), Is.True);
            Assert.That(mirrored, Is.EqualTo(states[0]));
            Assert.That(mirror.TryGet(hidden, out _), Is.False);
        }

        [Test]
        public void Delta_UsesAcknowledgedBaselineForChangesSpawnAndDespawn()
        {
            var retained = new NetworkEntityHandle(slot: 0, generation: 1);
            var despawned = new NetworkEntityHandle(slot: 1, generation: 1);
            var spawned = new NetworkEntityHandle(slot: 2, generation: 1);
            var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 16);
            var channel = new AuthoritativeReplicationChannel(new NetworkEntityTable(4), 4, 3, disclosureLog);
            var packet = new ReplicationPacketBuffer(entityCapacity: 4);
            var mirror = new ClientReplicationMirror(entityCapacity: 4, sessionEpoch: 7);
            var initialStates = new[]
            {
                State(retained, revision: 1, value: 10),
                State(despawned, revision: 1, value: 20),
            };
            var initialDisclosures = new[]
            {
                Visible(retained),
                Visible(despawned),
            };
            Assert.That(
                channel.BuildFull(7, 100, 1, initialStates, initialDisclosures, packet),
                Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(mirror.Apply(packet), Is.EqualTo(ReplicationApplyResult.Success));

            var nextStates = new[]
            {
                State(retained, revision: 2, value: 11),
                State(spawned, revision: 1, value: 30),
            };
            var nextDisclosures = new[]
            {
                Visible(retained),
                Visible(despawned),
                Visible(spawned),
            };

            ReplicationBuildResult built = channel.BuildDelta(
                sessionEpoch: 7,
                tick: 103,
                snapshotId: 2,
                acknowledgedBaselineId: 1,
                nextStates,
                nextDisclosures,
                packet);

            Assert.That(built, Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(packet.Header.Kind, Is.EqualTo(ReplicationPacketKind.Delta));
            Assert.That(packet.Header.BaselineSnapshotId, Is.EqualTo(1));
            Assert.That(packet.Upserts.Length, Is.EqualTo(2));
            Assert.That(packet.Removals.Length, Is.EqualTo(1));
            Assert.That(packet.Removals[0], Is.EqualTo(despawned));
            Assert.That(packet.DisclosureChanges.Length, Is.EqualTo(1));
            Assert.That(packet.DisclosureChanges[0].Entity, Is.EqualTo(spawned));
            Assert.That(packet.DisclosureChanges[0].Kind, Is.EqualTo(ReplicationDisclosureChangeKind.Reveal));

            Assert.That(mirror.Apply(packet), Is.EqualTo(ReplicationApplyResult.Success));
            Assert.That(mirror.TryGet(retained, out ReplicatedEntityState retainedState), Is.True);
            Assert.That(retainedState, Is.EqualTo(nextStates[0]));
            Assert.That(mirror.TryGet(despawned, out _), Is.False);
            Assert.That(mirror.TryGet(spawned, out ReplicatedEntityState spawnedState), Is.True);
            Assert.That(spawnedState, Is.EqualTo(nextStates[1]));
        }

        [Test]
        public void Delta_MissingAcknowledgedBaselineIsRejectedWithoutCommittingSnapshot()
        {
            var entity = new NetworkEntityHandle(slot: 0, generation: 1);
            var states = new[] { State(entity, revision: 1, value: 10) };
            var disclosures = new[] { Visible(entity) };
            var channel = new AuthoritativeReplicationChannel(
                new NetworkEntityTable(capacity: 2),
                replicationEntityCapacityPerSeat: 2,
                baselineCapacity: 2,
                new ReplicationDisclosureChangeLog(capacity: 8));
            var packet = new ReplicationPacketBuffer(entityCapacity: 2);
            Assert.That(
                channel.BuildFull(7, 100, 1, states, disclosures, packet),
                Is.EqualTo(ReplicationBuildResult.Success));

            Assert.That(
                channel.BuildDelta(7, 103, 2, 99, states, disclosures, packet),
                Is.EqualTo(ReplicationBuildResult.BaselineUnavailable));
            Assert.That(packet.Header.SnapshotId, Is.EqualTo(0));
            Assert.That(packet.Upserts.Length, Is.EqualTo(0));

            Assert.That(
                channel.BuildDelta(7, 103, 2, 1, states, disclosures, packet),
                Is.EqualTo(ReplicationBuildResult.Success));
        }

        [Test]
        public void Delta_LostVisionConcealsKnownEntityWithoutLeakingUndisclosedEnemy()
        {
            using Arch.Core.World world = Arch.Core.World.Create();
            var previouslyVisible = new NetworkEntityHandle(slot: 0, generation: 1);
            var neverDisclosed = new NetworkEntityHandle(slot: 1, generation: 1);
            var entities = new NetworkEntityTable(capacity: 3);
            Assert.That(entities.TryAllocate(world.Create(), out NetworkEntityHandle allocatedVisible), Is.True);
            Assert.That(entities.TryAllocate(world.Create(), out NetworkEntityHandle allocatedHidden), Is.True);
            Assert.That(allocatedVisible, Is.EqualTo(previouslyVisible));
            Assert.That(allocatedHidden, Is.EqualTo(neverDisclosed));
            var channel = new AuthoritativeReplicationChannel(
                entities,
                replicationEntityCapacityPerSeat: 3,
                baselineCapacity: 3,
                new ReplicationDisclosureChangeLog(capacity: 8));
            var packet = new ReplicationPacketBuffer(entityCapacity: 3);
            var mirror = new ClientReplicationMirror(entityCapacity: 3, sessionEpoch: 7);
            var states = new[]
            {
                State(previouslyVisible, revision: 1, value: 10),
                State(neverDisclosed, revision: 1, value: 99),
            };
            var initialDisclosures = new[]
            {
                Visible(previouslyVisible),
                Hidden(neverDisclosed),
            };
            Assert.That(
                channel.BuildFull(7, 100, 1, states, initialDisclosures, packet),
                Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(mirror.Apply(packet), Is.EqualTo(ReplicationApplyResult.Success));

            var concealedDisclosures = new[]
            {
                Remembered(previouslyVisible),
                Hidden(neverDisclosed),
            };
            Assert.That(
                channel.BuildDelta(7, 103, 2, 1, states, concealedDisclosures, packet),
                Is.EqualTo(ReplicationBuildResult.Success));

            Assert.That(packet.Upserts.Length, Is.EqualTo(0));
            Assert.That(packet.Removals.Length, Is.EqualTo(0));
            Assert.That(packet.DisclosureChanges.Length, Is.EqualTo(1));
            Assert.That(packet.DisclosureChanges[0].Entity, Is.EqualTo(previouslyVisible));
            Assert.That(packet.DisclosureChanges[0].Kind, Is.EqualTo(ReplicationDisclosureChangeKind.Conceal));
            Assert.That(mirror.Apply(packet), Is.EqualTo(ReplicationApplyResult.Success));
            Assert.That(mirror.TryGet(previouslyVisible, out _), Is.False);
            Assert.That(mirror.TryGet(neverDisclosed, out _), Is.False);

            var revealDisclosures = new[]
            {
                Remembered(previouslyVisible),
                Visible(neverDisclosed),
            };
            Assert.That(
                channel.BuildDelta(7, 106, 3, 2, states, revealDisclosures, packet),
                Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(packet.Upserts.Length, Is.EqualTo(1));
            Assert.That(packet.Upserts[0].Entity, Is.EqualTo(neverDisclosed));
            Assert.That(packet.Removals.Length, Is.EqualTo(0));
            Assert.That(packet.DisclosureChanges.Length, Is.EqualTo(1));
            Assert.That(packet.DisclosureChanges[0].Kind, Is.EqualTo(ReplicationDisclosureChangeKind.Reveal));
            Assert.That(mirror.Apply(packet), Is.EqualTo(ReplicationApplyResult.Success));
            Assert.That(mirror.TryGet(neverDisclosed, out _), Is.True);
        }

        [Test]
        public void ClientMirror_RejectsWrongEpochBaselineAndOutOfOrderSnapshot()
        {
            var entity = new NetworkEntityHandle(slot: 0, generation: 1);
            var states = new[] { State(entity, revision: 1, value: 10) };
            var disclosures = new[] { Visible(entity) };
            var channel = new AuthoritativeReplicationChannel(
                new NetworkEntityTable(capacity: 2),
                replicationEntityCapacityPerSeat: 2,
                baselineCapacity: 3,
                new ReplicationDisclosureChangeLog(capacity: 8));
            var full = new ReplicationPacketBuffer(entityCapacity: 2);
            var deltaFromOne = new ReplicationPacketBuffer(entityCapacity: 2);
            var laterDeltaFromOne = new ReplicationPacketBuffer(entityCapacity: 2);
            Assert.That(channel.BuildFull(7, 100, 1, states, disclosures, full), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(channel.BuildDelta(7, 103, 2, 1, states, disclosures, deltaFromOne), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(channel.BuildDelta(7, 106, 3, 1, states, disclosures, laterDeltaFromOne), Is.EqualTo(ReplicationBuildResult.Success));

            var wrongEpochChannel = new AuthoritativeReplicationChannel(
                new NetworkEntityTable(capacity: 2),
                replicationEntityCapacityPerSeat: 2,
                baselineCapacity: 1,
                new ReplicationDisclosureChangeLog(capacity: 2));
            var wrongEpoch = new ReplicationPacketBuffer(entityCapacity: 2);
            Assert.That(wrongEpochChannel.BuildFull(8, 100, 1, states, disclosures, wrongEpoch), Is.EqualTo(ReplicationBuildResult.Success));

            var mirror = new ClientReplicationMirror(entityCapacity: 2, sessionEpoch: 7);
            Assert.That(mirror.Apply(full), Is.EqualTo(ReplicationApplyResult.Success));
            Assert.That(mirror.Apply(wrongEpoch), Is.EqualTo(ReplicationApplyResult.EpochMismatch));
            Assert.That(mirror.LastSnapshotId, Is.EqualTo(1));

            Assert.That(mirror.Apply(deltaFromOne), Is.EqualTo(ReplicationApplyResult.Success));
            Assert.That(mirror.Apply(deltaFromOne), Is.EqualTo(ReplicationApplyResult.SnapshotOutOfOrder));
            Assert.That(mirror.LastSnapshotId, Is.EqualTo(2));

            Assert.That(mirror.Apply(laterDeltaFromOne), Is.EqualTo(ReplicationApplyResult.BaselineMismatch));
            Assert.That(mirror.LastSnapshotId, Is.EqualTo(2));
            Assert.That(mirror.TryGet(entity, out ReplicatedEntityState mirrored), Is.True);
            Assert.That(mirrored, Is.EqualTo(states[0]));
        }

        [Test]
        public void FullSnapshot_DisclosureLogCapacityFailureIsExplicitAndAtomic()
        {
            var first = new NetworkEntityHandle(slot: 0, generation: 1);
            var second = new NetworkEntityHandle(slot: 1, generation: 1);
            var states = new[]
            {
                State(first, revision: 1, value: 10),
                State(second, revision: 1, value: 20),
            };
            var disclosures = new[] { Visible(first), Visible(second) };
            var log = new ReplicationDisclosureChangeLog(capacity: 1);
            var channel = new AuthoritativeReplicationChannel(new NetworkEntityTable(2), 2, 1, log);
            var packet = new ReplicationPacketBuffer(entityCapacity: 2);

            Assert.That(
                channel.BuildFull(7, 100, 1, states, disclosures, packet),
                Is.EqualTo(ReplicationBuildResult.DisclosureLogCapacityExceeded));
            Assert.That(log.Count, Is.EqualTo(0));
            Assert.That(packet.Header.SnapshotId, Is.EqualTo(0));
            Assert.That(packet.Upserts.Length, Is.EqualTo(0));
        }

        [Test]
        public void BuildDeltaAndApply_AreZeroAllocAfterConstructionAndWarmup()
        {
            var entity = new NetworkEntityHandle(slot: 0, generation: 1);
            var states = new[] { State(entity, revision: 1, value: 1) };
            var disclosures = new[] { Visible(entity) };
            var channel = new AuthoritativeReplicationChannel(
                new NetworkEntityTable(capacity: 1),
                replicationEntityCapacityPerSeat: 1,
                baselineCapacity: 2,
                new ReplicationDisclosureChangeLog(capacity: 1));
            var packet = new ReplicationPacketBuffer(entityCapacity: 1);
            var mirror = new ClientReplicationMirror(entityCapacity: 1, sessionEpoch: 7);
            bool allSucceeded = channel.BuildFull(7, 1, 1, states, disclosures, packet) == ReplicationBuildResult.Success &&
                                mirror.Apply(packet) == ReplicationApplyResult.Success;
            ulong baselineId = 1;

            for (uint i = 2; i < 258; i++)
            {
                states[0] = State(entity, revision: i, value: i);
                allSucceeded &= channel.BuildDelta(7, i, i, baselineId, states, disclosures, packet) == ReplicationBuildResult.Success;
                allSucceeded &= mirror.Apply(packet) == ReplicationApplyResult.Success;
                baselineId = i;
            }

            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (uint i = 258; i < 10_258; i++)
            {
                states[0] = State(entity, revision: i, value: i);
                allSucceeded &= channel.BuildDelta(7, i, i, baselineId, states, disclosures, packet) == ReplicationBuildResult.Success;
                allSucceeded &= mirror.Apply(packet) == ReplicationApplyResult.Success;
                baselineId = i;
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allSucceeded, Is.True);
            Assert.That(allocated, Is.EqualTo(0));
            Assert.That(mirror.LastSnapshotId, Is.EqualTo(10_257));
            Assert.That(mirror.TryGet(entity, out ReplicatedEntityState final), Is.True);
            Assert.That(final.Revision, Is.EqualTo(10_257));
        }

        private static ReplicatedEntityState State(NetworkEntityHandle handle, uint revision, long value)
        {
            return new ReplicatedEntityState(
                handle,
                schemaId: 1,
                revision,
                new ReplicationStateVector(value, 0, 0, 0));
        }

        private static ReplicationDisclosureInput Visible(NetworkEntityHandle handle)
            => new(handle, KnowledgePresence.LiveVisible);

        private static ReplicationDisclosureInput Hidden(NetworkEntityHandle handle)
            => new(handle, KnowledgePresence.HiddenWithSource);

        private static ReplicationDisclosureInput Remembered(NetworkEntityHandle handle)
            => new(handle, KnowledgePresence.Known);
    }
}
