using Arch.Core;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Session;
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

            var channel = Channel(capacity: 1, out NetworkEntityTable entities);
            var packet = new ReplicationPacketBuffer(1);
            var visible = new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.LiveVisible) };
            Assert.That(
                channel.BuildFull(7, 1, 1, new[] { State(handle, 1, 55) }, visible, packet),
                Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(entities.TryRelease(handle), Is.True);
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
            SessionSeatBinding clientSeat = Seat();
            var bridge = new ClientWorldReplicationBridge(
                world,
                globalEntityCapacity: 1,
                activeMirrorCapacity: 1,
                in clientSeat,
                sessionEpoch: 7,
                appliers);
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

        [Test]
        public void SparseMirror_GlobalOneHundredThousand_ActiveFiveHundredTwelve_HandlesHighSlotAndRejectsOverflowWithoutPartialCommit()
        {
            const int globalCapacity = 100_000;
            const int activeCapacity = 512;
            using World world = World.Create();
            var entities = new NetworkEntityTable(capacity: globalCapacity);
            var channel = new AuthoritativeReplicationChannel(
                entities,
                activeCapacity,
                baselineCapacity: 2,
                new ReplicationDisclosureChangeLog(activeCapacity * 4));
            var packet = new ReplicationPacketBuffer(activeCapacity);
            SessionSeatBinding clientSeat = Seat();
            var bridge = new ClientWorldReplicationBridge(
                world,
                globalCapacity,
                activeCapacity,
                in clientSeat,
                sessionEpoch: 7,
                CreateAppliers());

            Assert.Multiple(() =>
            {
                Assert.That(bridge.GlobalEntityCapacity, Is.EqualTo(globalCapacity));
                Assert.That(bridge.ActiveMirrorCapacity, Is.EqualTo(activeCapacity));
            });

            var high = new NetworkEntityHandle(slot: 99_999, generation: 1);
            var highStates = new[] { State(high, revision: 1, value: 999) };
            var highVisible = new[] { new ReplicationDisclosureInput(high, KnowledgePresence.LiveVisible) };
            Assert.That(channel.BuildFull(7, 1, 1, highStates, highVisible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(high, out Entity highEntity), Is.True);
            Assert.That(world.Get<ReplicationMirrorState>(highEntity).Values.Value0, Is.EqualTo(999));

            highStates[0] = State(high, revision: 2, value: 1001);
            Assert.That(
                channel.BuildDelta(7, 2, 2, 1, highStates, highVisible, packet),
                Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(high, out Entity updatedHigh), Is.True);
            Assert.That(updatedHigh, Is.EqualTo(highEntity));
            Assert.That(world.Get<ReplicationMirrorState>(updatedHigh).Values.Value0, Is.EqualTo(1001));

            Assert.That(
                channel.BuildDelta(
                    7,
                    3,
                    3,
                    2,
                    highStates,
                    new[] { new ReplicationDisclosureInput(high, KnowledgePresence.Known) },
                    packet),
                Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(bridge.TryResolve(high, out _), Is.False);
            Assert.That(world.IsAlive(highEntity), Is.False);

            var handles = new NetworkEntityHandle[activeCapacity];
            var states = new ReplicatedEntityState[activeCapacity];
            var visible = new ReplicationDisclosureInput[activeCapacity];
            for (int i = 0; i < activeCapacity; i++)
            {
                handles[i] = new NetworkEntityHandle(slot: i, generation: 1);
                states[i] = State(handles[i], revision: 1, value: i);
                visible[i] = new ReplicationDisclosureInput(handles[i], KnowledgePresence.LiveVisible);
            }

            Assert.That(channel.BuildFull(7, 4, 4, states, visible, packet), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            for (int i = 0; i < activeCapacity; i++)
            {
                Assert.That(bridge.TryResolve(handles[i], out _), Is.True);
            }

            var overflow = new NetworkEntityHandle(slot: activeCapacity, generation: 1);
            packet.Reset(new ReplicationPacketHeader(
                ReplicationPacketKind.Delta,
                sessionEpoch: 7,
                tick: 5,
                snapshotId: 5,
                baselineSnapshotId: 4));
            packet.AddUpsert(State(overflow, revision: 1, value: 513));
            packet.AddDisclosureChange(new ReplicationDisclosureChange(
                sequence: 1,
                snapshotId: 5,
                overflow,
                ReplicationDisclosureChangeKind.Reveal));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.CapacityContractViolated));
            Assert.That(bridge.LastSnapshotId, Is.EqualTo(4));
            Assert.That(bridge.TryResolve(overflow, out _), Is.False);
            for (int i = 0; i < activeCapacity; i++)
            {
                Assert.That(bridge.TryResolve(handles[i], out Entity retained), Is.True);
                Assert.That(world.Get<ReplicationMirrorState>(retained).Values.Value0, Is.EqualTo(i));
            }

            var replacement = new NetworkEntityHandle(slot: 0, generation: 2);
            var replaceStates = new ReplicatedEntityState[activeCapacity];
            var replaceVisible = new ReplicationDisclosureInput[activeCapacity];
            replaceStates[0] = State(replacement, revision: 1, value: 777);
            replaceVisible[0] = new ReplicationDisclosureInput(replacement, KnowledgePresence.LiveVisible);
            for (int i = 1; i < activeCapacity; i++)
            {
                replaceStates[i] = State(handles[i], revision: 1, value: i);
                replaceVisible[i] = visible[i];
            }

            Assert.That(bridge.TryResolve(handles[0], out Entity beforeReplace), Is.True);
            Assert.That(
                channel.BuildDelta(7, 6, 6, 4, replaceStates, replaceVisible, packet),
                Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.Success));
            Assert.That(world.IsAlive(beforeReplace), Is.False);
            Assert.That(bridge.TryResolve(handles[0], out _), Is.False);
            Assert.That(bridge.TryResolve(replacement, out Entity replaced), Is.True);
            Assert.That(world.Get<ReplicationMirrorState>(replaced).Values.Value0, Is.EqualTo(777));
            for (int i = 1; i < activeCapacity; i++)
            {
                Assert.That(bridge.TryResolve(handles[i], out _), Is.True);
            }

            var stale = State(handles[0], revision: 9, value: -1);
            packet.Reset(new ReplicationPacketHeader(
                ReplicationPacketKind.Delta,
                sessionEpoch: 7,
                tick: 7,
                snapshotId: 7,
                baselineSnapshotId: 6));
            packet.AddUpsert(stale);
            Assert.That(bridge.Apply(packet), Is.EqualTo(ReplicationBridgeResult.ResyncRequired));
            Assert.That(bridge.TryResolve(replacement, out Entity unchanged), Is.True);
            Assert.That(unchanged, Is.EqualTo(replaced));
            Assert.That(world.Get<ReplicationMirrorState>(unchanged).Values.Value0, Is.EqualTo(777));
        }

        [Test]
        public void SparseMirror_WarmedHighSlotDeltaApply_IsZeroAlloc()
        {
            const int globalCapacity = 100_000;
            const int activeCapacity = 512;
            using World world = World.Create();
            var channel = new AuthoritativeReplicationChannel(
                new NetworkEntityTable(capacity: globalCapacity),
                activeCapacity,
                baselineCapacity: 2,
                new ReplicationDisclosureChangeLog(activeCapacity * 4));
            SessionSeatBinding clientSeat = Seat();
            var bridge = new ClientWorldReplicationBridge(
                world,
                globalCapacity,
                activeCapacity,
                in clientSeat,
                sessionEpoch: 7,
                CreateAppliers());
            var handle = new NetworkEntityHandle(slot: 99_999, generation: 1);
            var states = new[] { State(handle, 1, 1) };
            var visible = new[] { new ReplicationDisclosureInput(handle, KnowledgePresence.LiveVisible) };
            var warmupPacket = new ReplicationPacketBuffer(activeCapacity);
            Assert.That(channel.BuildFull(7, 1, 1, states, visible, warmupPacket), Is.EqualTo(ReplicationBuildResult.Success));
            Assert.That(bridge.Apply(warmupPacket), Is.EqualTo(ReplicationBridgeResult.Success));

            const int warmupCount = 64;
            const int measureCount = 256;
            var packets = new ReplicationPacketBuffer[warmupCount + measureCount];
            ulong baseline = 1;
            for (int i = 0; i < packets.Length; i++)
            {
                uint snapshotId = (uint)(i + 2);
                states[0] = State(handle, snapshotId, snapshotId);
                packets[i] = new ReplicationPacketBuffer(activeCapacity);
                Assert.That(
                    channel.BuildDelta(7, snapshotId, snapshotId, baseline, states, visible, packets[i]),
                    Is.EqualTo(ReplicationBuildResult.Success));
                baseline = snapshotId;
            }

            for (int i = 0; i < warmupCount; i++)
            {
                Assert.That(bridge.Apply(packets[i]), Is.EqualTo(ReplicationBridgeResult.Success));
            }

            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool succeeded = true;
            for (int i = warmupCount; i < packets.Length; i++)
            {
                succeeded &= bridge.Apply(packets[i]) == ReplicationBridgeResult.Success;
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(succeeded, Is.True);
            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public void SparseMirror_ConstructingOneHundredFortyNineBridges_StaysWithinPerSeatAllocationCeiling()
        {
            const int globalCapacity = 100_000;
            const int activeCapacity = 512;
            const int bridgeCount = 149;
            // Conservative ceiling: allocations must scale with active=512, not global=100000.
            // Prior global-sized arrays were multi-GB for 149 seats; 256MB is still far below that.
            const long allocationCeilingBytes = 256L * 1024L * 1024L;
            var appliers = CreateAppliers();
            var worlds = new World[bridgeCount];
            var bridges = new ClientWorldReplicationBridge[bridgeCount];
            SessionSeatBinding clientSeat = Seat();

            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < bridgeCount; i++)
            {
                worlds[i] = World.Create();
                bridges[i] = new ClientWorldReplicationBridge(
                    worlds[i],
                    globalCapacity,
                    activeCapacity,
                    in clientSeat,
                    sessionEpoch: 7,
                    appliers);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.WriteLine(
                $"Measured allocation for {bridgeCount} bridges at global={globalCapacity}, active={activeCapacity}: {allocated} bytes");
            Assert.That(allocated, Is.GreaterThan(0));
            Assert.That(allocated, Is.LessThan(allocationCeilingBytes));
            Assert.That(bridges[0].GlobalEntityCapacity, Is.EqualTo(globalCapacity));
            Assert.That(bridges[0].ActiveMirrorCapacity, Is.EqualTo(activeCapacity));
            Assert.That(bridges[bridgeCount - 1].ActiveMirrorCapacity, Is.EqualTo(activeCapacity));

            for (int i = 0; i < bridgeCount; i++)
            {
                worlds[i].Dispose();
            }
        }

        private static AuthoritativeReplicationChannel Channel(int capacity) => Channel(capacity, out _);

        private static AuthoritativeReplicationChannel Channel(
            int capacity,
            out NetworkEntityTable entities)
        {
            using World world = World.Create();
            entities = new NetworkEntityTable(capacity);
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

        private static ClientReplicationSchemaApplierRegistry CreateAppliers(
            TrackingSchemaApplier? tracking = null)
        {
            var appliers = new ClientReplicationSchemaApplierRegistry(schemaCapacity: 1);
            Assert.That(
                appliers.Register(1, tracking ?? new TrackingSchemaApplier()),
                Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
            appliers.Freeze();
            return appliers;
        }

        private static ClientWorldReplicationBridge Bridge(
            World world,
            int entityCapacity,
            ulong sessionEpoch,
            TrackingSchemaApplier? tracking = null)
        {
            SessionSeatBinding clientSeat = Seat();
            return new ClientWorldReplicationBridge(
                world,
                globalEntityCapacity: entityCapacity,
                activeMirrorCapacity: entityCapacity,
                in clientSeat,
                sessionEpoch,
                CreateAppliers(tracking));
        }

        private static ReplicatedEntityState State(NetworkEntityHandle handle, uint revision, long value)
            => new(
                handle,
                schemaId: 1,
                revision,
                new ReplicationStateVector(value, 0, 0, 0),
                ReplicationControlOwnership.Unowned);

        private static SessionSeatBinding Seat() => new(0, 1, new PlayerId(1));

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
