using System.Reflection;
using Arch.Core;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Transport;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class SparseReplicationTests
{
    [Test]
    public void Capacity_GlobalOneHundredThousand_PerSeatFiveHundredTwelve_ReservesOnlyPerSeatBaselines()
    {
        int perSeatCapacity = 512;
        var capacity = new NetworkRuntimeCapacity(
            simulationTickRateHz: 30,
            statePublishRateHz: 10,
            maxDatagramPayloadBytes: 1200,
            connectionCapacity: 150,
            globalEntityCapacity: 100_000,
            replicationEntityCapacityPerSeat: perSeatCapacity,
            maxCommandEntries: 1,
            maxCommandPayloadBytes: CommandBatchWireCodec.GetPayloadSize(1),
            maxCommandFragments: 1,
            maxSnapshotBytes: ReplicationPacketWireCodec.GetPayloadSize(
                perSeatCapacity,
                perSeatCapacity,
                perSeatCapacity * 2),
            maxSnapshotFragments: 64,
            outboundQueueCapacity: 150,
            acknowledgementHistoryCapacity: 32,
            controlChannel: new ChannelId(0),
            commandChannel: new ChannelId(1),
            stateChannel: new ChannelId(2));
        var channel = new AuthoritativeReplicationChannel(
            perSeatCapacity,
            baselineCapacity: 32,
            new ReplicationDisclosureChangeLog(perSeatCapacity));

        Assert.Multiple(() =>
        {
            Assert.That(capacity.GlobalEntityCapacity, Is.EqualTo(100_000));
            Assert.That(capacity.ReplicationEntityCapacityPerSeat, Is.EqualTo(perSeatCapacity));
            Assert.That(channel.ReservedCurrentStateCapacity, Is.EqualTo(perSeatCapacity));
            Assert.That(channel.ReservedBaselineStateCapacity, Is.EqualTo(perSeatCapacity * 32));
            Assert.That(channel.ReservedBaselineStateCapacity, Is.LessThan(100_000));
        });
    }

    [Test]
    public void Delta_WhenEntireFiveHundredTwelveEntityAreaChanges_EmitsAllConcealsAndReveals()
    {
        const int perSeatCapacity = 512;
        var initialStates = new ReplicatedEntityState[perSeatCapacity];
        var initialDisclosures = new ReplicationDisclosureInput[perSeatCapacity];
        var nextStates = new ReplicatedEntityState[perSeatCapacity];
        var nextDisclosures = new ReplicationDisclosureInput[perSeatCapacity];
        for (int i = 0; i < perSeatCapacity; i++)
        {
            var initial = new NetworkEntityHandle(slot: i, generation: 1);
            var next = new NetworkEntityHandle(slot: perSeatCapacity + i, generation: 1);
            initialStates[i] = State(initial, revision: 1, value: i);
            initialDisclosures[i] = Visible(initial);
            nextStates[i] = State(next, revision: 1, value: perSeatCapacity + i);
            nextDisclosures[i] = Visible(next);
        }

        var channel = new AuthoritativeReplicationChannel(
            perSeatCapacity,
            baselineCapacity: 2,
            new ReplicationDisclosureChangeLog(capacity: perSeatCapacity * 3));
        var packet = new ReplicationPacketBuffer(perSeatCapacity);
        Assert.That(packet.DisclosureCapacity, Is.EqualTo(perSeatCapacity * 2));
        Assert.That(
            channel.BuildFull(7, 100, 1, initialStates, initialDisclosures, packet),
            Is.EqualTo(ReplicationBuildResult.Success));

        Assert.That(
            channel.BuildDelta(7, 103, 2, 1, nextStates, nextDisclosures, packet),
            Is.EqualTo(ReplicationBuildResult.Success));
        Assert.Multiple(() =>
        {
            Assert.That(packet.UpsertCount, Is.EqualTo(perSeatCapacity));
            Assert.That(packet.RemovalCount, Is.Zero);
            Assert.That(packet.DisclosureChangeCount, Is.EqualTo(perSeatCapacity * 2));
            Assert.That(
                packet.DisclosureChanges[..perSeatCapacity].ToArray(),
                Has.All.Property(nameof(ReplicationDisclosureChange.Kind))
                    .EqualTo(ReplicationDisclosureChangeKind.Conceal));
            Assert.That(
                packet.DisclosureChanges[perSeatCapacity..].ToArray(),
                Has.All.Property(nameof(ReplicationDisclosureChange.Kind))
                    .EqualTo(ReplicationDisclosureChangeKind.Reveal));
        });

        byte[] wire = new byte[ReplicationPacketWireCodec.GetPayloadSize(
            packet.UpsertCount,
            packet.RemovalCount,
            packet.DisclosureChangeCount)];
        Assert.That(
            ReplicationPacketWireCodec.TryEncode(packet, wire, out int bytesWritten),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        var decoded = new ReplicationPacketBuffer(perSeatCapacity);
        Assert.That(
            ReplicationPacketWireCodec.TryDecode(wire.AsSpan(0, bytesWritten), decoded),
            Is.EqualTo(NetworkWireCodecStatus.Success));
        Assert.Multiple(() =>
        {
            Assert.That(decoded.UpsertCount, Is.EqualTo(perSeatCapacity));
            Assert.That(decoded.RemovalCount, Is.Zero);
            Assert.That(decoded.DisclosureChangeCount, Is.EqualTo(perSeatCapacity * 2));
        });
    }

    [Test]
    public void CompactMerge_PreservesGenerationRevealConcealRemovalAndResyncSemantics()
    {
        var replaced = new NetworkEntityHandle(slot: 99_998, generation: 1);
        var replacement = new NetworkEntityHandle(slot: 99_998, generation: 2);
        var concealed = new NetworkEntityHandle(slot: 99_999, generation: 1);
        var channel = new AuthoritativeReplicationChannel(
            replicationEntityCapacityPerSeat: 512,
            baselineCapacity: 2,
            new ReplicationDisclosureChangeLog(capacity: 16));
        var packet = new ReplicationPacketBuffer(entityCapacity: 512);
        var initialStates = new[]
        {
            State(replaced, revision: 1, value: 10),
            State(concealed, revision: 1, value: 20),
        };
        var initialDisclosures = new[]
        {
            Visible(replaced),
            Visible(concealed),
        };

        Assert.That(
            channel.BuildFull(7, 100, 1, initialStates, initialDisclosures, packet),
            Is.EqualTo(ReplicationBuildResult.Success));
        Assert.Multiple(() =>
        {
            Assert.That(packet.Upserts.Length, Is.EqualTo(2));
            Assert.That(packet.DisclosureChanges.ToArray(),
                Has.All.Property(nameof(ReplicationDisclosureChange.Kind)).EqualTo(ReplicationDisclosureChangeKind.Reveal));
        });

        var nextStates = new[] { State(replacement, revision: 1, value: 30) };
        var nextDisclosures = new[]
        {
            Visible(replacement),
            Remembered(concealed),
        };
        Assert.That(
            channel.BuildDelta(7, 103, 2, 1, nextStates, nextDisclosures, packet),
            Is.EqualTo(ReplicationBuildResult.Success));

        Assert.Multiple(() =>
        {
            Assert.That(packet.Upserts.Length, Is.EqualTo(1));
            Assert.That(packet.Upserts[0].Entity, Is.EqualTo(replacement));
            Assert.That(packet.Removals.ToArray(), Is.EqualTo(new[] { replaced }));
            Assert.That(packet.DisclosureChanges.Length, Is.EqualTo(2));
            Assert.That(packet.DisclosureChanges[0].Entity, Is.EqualTo(replacement));
            Assert.That(packet.DisclosureChanges[0].Kind, Is.EqualTo(ReplicationDisclosureChangeKind.Reveal));
            Assert.That(packet.DisclosureChanges[1].Entity, Is.EqualTo(concealed));
            Assert.That(packet.DisclosureChanges[1].Kind, Is.EqualTo(ReplicationDisclosureChangeKind.Conceal));
        });

        Assert.That(
            channel.BuildDelta(7, 106, 3, 99, nextStates, nextDisclosures, packet),
            Is.EqualTo(ReplicationBuildResult.BaselineUnavailable));
        Assert.That(packet.Upserts.Length, Is.Zero);

        Assert.That(
            channel.BuildDelta(7, 106, 3, 2, ReadOnlySpan<ReplicatedEntityState>.Empty, nextDisclosures, packet),
            Is.EqualTo(ReplicationBuildResult.Success));
        Assert.Multiple(() =>
        {
            Assert.That(packet.Upserts.Length, Is.Zero);
            Assert.That(packet.Removals.ToArray(), Is.EqualTo(new[] { replacement }));
            Assert.That(packet.DisclosureChanges.Length, Is.Zero);
        });
    }

    [Test]
    public void CompactView_RejectsUnsortedDuplicateAndOverCapacityInputWithoutCommit()
    {
        var first = new NetworkEntityHandle(slot: 1, generation: 1);
        var second = new NetworkEntityHandle(slot: 2, generation: 1);
        var channel = new AuthoritativeReplicationChannel(
            replicationEntityCapacityPerSeat: 2,
            baselineCapacity: 1,
            new ReplicationDisclosureChangeLog(capacity: 2));
        var packet = new ReplicationPacketBuffer(entityCapacity: 2);

        Assert.That(
            channel.BuildFull(
                7,
                1,
                1,
                new[] { State(second, 1, 2), State(first, 1, 1) },
                new[] { Visible(first), Visible(second) },
                packet),
            Is.EqualTo(ReplicationBuildResult.InvalidInput));
        Assert.That(packet.Header.SnapshotId, Is.Zero);

        Assert.That(
            channel.BuildFull(
                7,
                1,
                1,
                new[] { State(first, 1, 1) },
                new[] { Visible(first), Visible(first) },
                packet),
            Is.EqualTo(ReplicationBuildResult.InvalidInput));

        Assert.That(
            new AuthoritativeReplicationChannel(
                    replicationEntityCapacityPerSeat: 1,
                    baselineCapacity: 1,
                    new ReplicationDisclosureChangeLog(capacity: 2))
                .BuildFull(
                7,
                1,
                1,
                new[] { State(first, 1, 1), State(second, 1, 2) },
                new[] { Visible(first), Visible(second) },
                packet),
            Is.EqualTo(ReplicationBuildResult.InvalidInput));
    }

    [Test]
    public void Bridge_GlobalOneHundredThousand_ProjectsOnlyOrderedInterestWithoutGlobalTrackingArray()
    {
        using World world = World.Create();
        Entity viewer = world.Create();
        Entity target = world.Create(
            new ReplicationSchemaRef(schemaId: 1),
            new TestReplicatedData(revision: 1, value: 42));
        var entities = new NetworkEntityTable(capacity: 100_000);
        Assert.That(entities.TryAllocate(target, out NetworkEntityHandle handle), Is.True);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 1);
        knowledge.Upsert(viewer, target, VisibleDisclosure());
        var projector = new CountingProjector();
        var projectors = new ReplicationSchemaProjectorRegistry(schemaCapacity: 1);
        Assert.That(projectors.Register(1, projector), Is.EqualTo(ReplicationSchemaRegistrationResult.Success));
        projectors.Freeze();
        var bridge = new AuthoritativeWorldReplicationBridge(
            world,
            entities,
            knowledge,
            viewer,
            projectors,
            replicationEntityCapacityPerSeat: 512);
        var output = new ReplicationProjectionBuffer(entityCapacity: 512);

        Assert.That(
            bridge.Project(new[] { handle }, currentTick: 1, output),
            Is.EqualTo(ReplicationBridgeResult.Success));

        FieldInfo[] fields = typeof(AuthoritativeWorldReplicationBridge).GetFields(
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Multiple(() =>
        {
            Assert.That(bridge.GlobalEntityCapacity, Is.EqualTo(100_000));
            Assert.That(bridge.ReplicationEntityCapacityPerSeat, Is.EqualTo(512));
            Assert.That(output.States.Length, Is.EqualTo(1));
            Assert.That(projector.ProjectCalls, Is.EqualTo(1));
            Assert.That(fields, Has.None.Matches<FieldInfo>(field => field.FieldType.IsArray));
        });
    }

    private static ReplicatedEntityState State(NetworkEntityHandle entity, uint revision, long value) =>
        new(entity, schemaId: 1, revision, new ReplicationStateVector(value, 0, 0, 0));

    private static ReplicationDisclosureInput Visible(NetworkEntityHandle entity) =>
        new(entity, KnowledgePresence.LiveVisible);

    private static ReplicationDisclosureInput Remembered(NetworkEntityHandle entity) =>
        new(entity, KnowledgePresence.Known);

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

    private sealed class CountingProjector : IReplicationSchemaProjector
    {
        public int ProjectCalls { get; private set; }

        public bool TryProject(
            World world,
            Entity entity,
            in KnowledgeDisclosureRecord disclosure,
            out ReplicationProjectedState state)
        {
            ProjectCalls++;
            if (!world.TryGet(entity, out TestReplicatedData data))
            {
                state = default;
                return false;
            }

            state = new ReplicationProjectedState(
                data.Revision,
                new ReplicationStateVector(data.Value, 0, 0, 0));
            return true;
        }
    }
}
