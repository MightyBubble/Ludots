using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class AuthoritativeReplicationPublicationTransactionTests
{
    [Test]
    public void PrepareFull_Cancel_ThenPrepareSameSnapshot_YieldsIdenticalWireBytes()
    {
        var entity = new NetworkEntityHandle(slot: 0, generation: 1);
        var states = new[] { State(entity, revision: 3, value: 42) };
        var disclosures = new[] { Visible(entity) };
        var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 4);
        var channel = new AuthoritativeReplicationChannel(
            new NetworkEntityTable(capacity: 4),
            replicationEntityCapacityPerSeat: 4,
            baselineCapacity: 2,
            disclosureLog);
        var packet = new ReplicationPacketBuffer(entityCapacity: 4);
        byte[] first = new byte[ReplicationPacketWireCodec.GetPayloadSize(4, 4, 8)];
        byte[] second = new byte[first.Length];

        Assert.That(
            channel.PrepareFull(7, 100, 1, states, disclosures, packet),
            Is.EqualTo(ReplicationBuildResult.Success));
        Assert.That(channel.IsPrepared, Is.True);
        Assert.That(disclosureLog.Count, Is.EqualTo(0));
        Assert.That(
            ReplicationPacketWireCodec.TryEncode(packet, first, out int firstBytes),
            Is.EqualTo(NetworkWireCodecStatus.Success));

        channel.CancelPrepared();
        Assert.That(channel.IsPrepared, Is.False);
        Assert.That(disclosureLog.Count, Is.EqualTo(0));
        Assert.That(disclosureLog.NextSequence, Is.EqualTo(1uL));

        Assert.That(
            channel.PrepareFull(7, 100, 1, states, disclosures, packet),
            Is.EqualTo(ReplicationBuildResult.Success));
        Assert.That(
            ReplicationPacketWireCodec.TryEncode(packet, second, out int secondBytes),
            Is.EqualTo(NetworkWireCodecStatus.Success));

        Assert.Multiple(() =>
        {
            Assert.That(secondBytes, Is.EqualTo(firstBytes));
            Assert.That(second.AsSpan(0, secondBytes).SequenceEqual(first.AsSpan(0, firstBytes)), Is.True);
            Assert.That(disclosureLog.Count, Is.EqualTo(0));
        });

        channel.CommitPrepared();
        Assert.That(disclosureLog.Count, Is.EqualTo(1));
        Assert.That(channel.IsPrepared, Is.False);
    }

    [Test]
    public void PrepareFull_DisclosureCapacityFailure_LeavesLogAndBaselineUnchanged()
    {
        var first = new NetworkEntityHandle(slot: 0, generation: 1);
        var second = new NetworkEntityHandle(slot: 1, generation: 1);
        var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 1);
        var channel = new AuthoritativeReplicationChannel(
            new NetworkEntityTable(capacity: 4),
            replicationEntityCapacityPerSeat: 4,
            baselineCapacity: 2,
            disclosureLog);
        var packet = new ReplicationPacketBuffer(entityCapacity: 4);
        var states = new[]
        {
            State(first, revision: 1, value: 1),
            State(second, revision: 1, value: 2),
        };
        var disclosures = new[] { Visible(first), Visible(second) };

        Assert.That(
            channel.PrepareFull(7, 100, 1, states, disclosures, packet),
            Is.EqualTo(ReplicationBuildResult.DisclosureLogCapacityExceeded));
        Assert.Multiple(() =>
        {
            Assert.That(channel.IsPrepared, Is.False);
            Assert.That(disclosureLog.Count, Is.EqualTo(0));
            Assert.That(disclosureLog.NextSequence, Is.EqualTo(1uL));
            Assert.That(packet.Header.SnapshotId, Is.EqualTo(0uL));
        });
    }

    [Test]
    public void TryAcknowledgeDisclosure_WhilePrepared_IsRejected()
    {
        var entity = new NetworkEntityHandle(slot: 0, generation: 1);
        var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 4);
        var channel = new AuthoritativeReplicationChannel(
            new NetworkEntityTable(capacity: 4),
            replicationEntityCapacityPerSeat: 4,
            baselineCapacity: 2,
            disclosureLog);
        var packet = new ReplicationPacketBuffer(entityCapacity: 4);
        Assert.That(
            channel.PrepareFull(7, 100, 1, new[] { State(entity, 1, 1) }, new[] { Visible(entity) }, packet),
            Is.EqualTo(ReplicationBuildResult.Success));

        Assert.Throws<InvalidOperationException>(() => channel.TryAcknowledgeDisclosureChangesThrough(1));
        channel.CancelPrepared();
    }

    private static ReplicatedEntityState State(NetworkEntityHandle entity, uint revision, long value) =>
        new(entity, schemaId: 1, revision, new ReplicationStateVector(value, 0, 0, 0), default);

    private static ReplicationDisclosureInput Visible(NetworkEntityHandle entity) =>
        new(entity, KnowledgePresence.LiveVisible);
}
