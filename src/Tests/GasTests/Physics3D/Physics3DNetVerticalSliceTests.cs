using System;
using System.Numerics;
using System.Reflection;
using Arch.Core;
using Ludots.Core.Knowledge;
using Ludots.Core.Layers;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Simulation;
using Ludots.Core.Physics3D;
using Ludots.Core.Physics3DNet;
using Ludots.Core.Physics3DNet.Bridge;
using Ludots.Core.Physics3DNet.Input;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
public sealed class Physics3DNetVerticalSliceTests
{
    private const ushort FixedInputSchemaId = 7;
    private const ulong SessionEpoch = 1;
    private const int ReplicationSchemaId = 41;

    private static Physics3DNetConfig CreateLocalConfig(
        int localPredictionHistoryTicks = 16,
        int remoteInterpolationHistoryTicks = 8,
        int replayEventCapacity = 128)
    {
        var config = new Physics3DNetConfig
        {
            AuthoritativeHz = 30,
            SnapshotHz = 10,
            LocalPredictionHistoryTicks = localPredictionHistoryTicks,
            RemoteInterpolationHistoryTicks = remoteInterpolationHistoryTicks,
            ReplayEventCapacity = replayEventCapacity
        };
        config.Validate();
        return config;
    }

    private static FixedInputProtocolConfig CreateFixedInputConfig(
        int seats = 4,
        int history = 16,
        int maxFuture = 8) =>
        new(
            seats,
            history,
            FixedInputSchemaId,
            Physics3DFixedInputFrameCodec.PayloadBytes,
            maxFuture,
            maxFramesPerBatch: 4,
            maxDatagramPayloadBytes: 1200,
            SessionEpoch);

    private static SessionSeatBinding Seat(int slot, uint generation = 1, int player = -1) =>
        new(slot, generation, new PlayerId(player > 0 ? player : 1000 + slot));

    private static void AdvanceCommitted(AuthoritativeSimulationTickState ticks, int committedInclusive)
    {
        while (ticks.CommittedTick < committedInclusive)
        {
            int next = ticks.CommittedTick + 1;
            ticks.Begin(next);
            ticks.Commit(next);
        }
    }

    private static void EncodeMovement(Span<byte> destination, float x, float y)
    {
        if (!Physics3DFixedInputFrameCodec.TryEncode(new Vector2(x, y), destination))
        {
            throw new InvalidOperationException($"Failed to encode Physics3D fixed input ({x}, {y}).");
        }
    }

    private static FixedInputBatchAdmissionStatus AdmitOne(
        AuthoritativeFixedInputIngress ingress,
        in SessionSeatBinding seat,
        uint tick,
        float moveX,
        float moveY,
        out FixedInputAdmissionDisposition disposition,
        uint ackCommitted = 0)
    {
        Span<uint> ticks = stackalloc uint[1] { tick };
        Span<byte> payloads = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        EncodeMovement(payloads, moveX, moveY);
        Span<FixedInputAdmissionDisposition> dispositions = stackalloc FixedInputAdmissionDisposition[1];
        var header = new NetworkFixedInputBatchHeader(
            ingress.Config.SessionEpoch,
            ingress.Config.SchemaId,
            ingress.Config.FramePayloadBytes,
            ackCommitted,
            frameCount: 1);
        FixedInputBatchAdmissionStatus status = ingress.TryAdmitBatch(in seat, in header, ticks, payloads, dispositions);
        disposition = dispositions[0];
        return status;
    }

    [Test]
    public void Config_RequiresHard30Hz_AndFixedInputHistoryCoversFutureWindow()
    {
        var ok = new Physics3DNetConfig { AuthoritativeHz = 30, SnapshotHz = 10 };
        Assert.That(ok.SnapshotIntervalTicks, Is.EqualTo(3));

        var badHz = new Physics3DNetConfig { AuthoritativeHz = 60, SnapshotHz = 10 };
        Assert.Throws<ArgumentOutOfRangeException>(() => badHz.Validate());

        var badDivisor = new Physics3DNetConfig { AuthoritativeHz = 30, SnapshotHz = 7 };
        Assert.Throws<ArgumentOutOfRangeException>(() => badDivisor.Validate());

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new FixedInputProtocolConfig(
                seatCapacity: 2,
                historyTicksPerSeat: 3,
                FixedInputSchemaId,
                Physics3DFixedInputFrameCodec.PayloadBytes,
                maxFutureTicks: 4,
                maxFramesPerBatch: 1,
                maxDatagramPayloadBytes: 1200,
                SessionEpoch));

        Assert.DoesNotThrow(
            () => _ = new FixedInputProtocolConfig(
                seatCapacity: 2,
                historyTicksPerSeat: 4,
                FixedInputSchemaId,
                Physics3DFixedInputFrameCodec.PayloadBytes,
                maxFutureTicks: 4,
                maxFramesPerBatch: 1,
                maxDatagramPayloadBytes: 1200,
                SessionEpoch));
    }

    [Test]
    public void AuthoritativeTick_IsMonotonicBeginCommit_AndRejectsSkips()
    {
        var ticks = new AuthoritativeSimulationTickState();
        Assert.That(ticks.IsExecuting, Is.False);
        Assert.That(ticks.CommittedTick, Is.EqualTo(0));

        ticks.Begin(1);
        Assert.That(ticks.IsExecuting, Is.True);
        Assert.That(ticks.ExecutingTick, Is.EqualTo(1));
        ticks.Commit(1);
        Assert.That(ticks.CommittedTick, Is.EqualTo(1));
        Assert.That(ticks.IsExecuting, Is.False);

        for (int tick = 2; tick <= 3; tick++)
        {
            ticks.Begin(tick);
            ticks.Commit(tick);
        }

        Assert.That(ticks.CommittedTick, Is.EqualTo(3));
        Assert.That(3 % CreateLocalConfig().SnapshotIntervalTicks, Is.EqualTo(0));
        Assert.Throws<InvalidOperationException>(() => ticks.Begin(5));
    }

    [Test]
    public void FixedInput_Supports150Seats_AndClassifiesReorderDuplicateLateMissingConflict()
    {
        var ticks = new AuthoritativeSimulationTickState();
        ticks.RestoreCommittedTick(10);
        var ingress = new AuthoritativeFixedInputIngress(CreateFixedInputConfig(seats: 150, history: 16, maxFuture: 4), ticks);
        for (int i = 0; i < 150; i++)
        {
            ingress.BindSeat(Seat(i));
        }

        Assert.That(AdmitOne(ingress, Seat(0), 11, 1f, 0f, out FixedInputAdmissionDisposition accepted), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(accepted, Is.EqualTo(FixedInputAdmissionDisposition.Accepted));

        Assert.That(AdmitOne(ingress, Seat(0), 11, 1f, 0f, out FixedInputAdmissionDisposition duplicate), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(duplicate, Is.EqualTo(FixedInputAdmissionDisposition.Duplicate));

        Assert.That(AdmitOne(ingress, Seat(0), 10, 0.5f, 0f, out FixedInputAdmissionDisposition late), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(late, Is.EqualTo(FixedInputAdmissionDisposition.Late));

        Assert.That(AdmitOne(ingress, Seat(0), 20, 0.5f, 0f, out FixedInputAdmissionDisposition tooFar), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(tooFar, Is.EqualTo(FixedInputAdmissionDisposition.TooFarFuture));

        Assert.That(AdmitOne(ingress, Seat(0), 12, 0.25f, 0f, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(AdmitOne(ingress, Seat(0), 11, 0f, 1f, out FixedInputAdmissionDisposition conflict), Is.EqualTo(FixedInputBatchAdmissionStatus.Rejected));
        Assert.That(conflict, Is.EqualTo(FixedInputAdmissionDisposition.Conflict));

        Span<byte> kept = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        Assert.That(ingress.TryGet(Seat(0), 11, kept, out _), Is.EqualTo(FixedInputLookupResult.Present));
        Assert.That(Physics3DFixedInputFrameCodec.TryDecode(kept, out Physics3DFixedInputFrame keptFrame), Is.True);
        Assert.That(keptFrame.Movement.X, Is.EqualTo(1f).Within(0.001f));

        Assert.That(ingress.TryGet(Seat(0), 13, kept, out _), Is.EqualTo(FixedInputLookupResult.Missing));
        Assert.That(ingress.DuplicateCount, Is.EqualTo(1));
        Assert.That(ingress.LateCount, Is.EqualTo(1));
        Assert.That(ingress.TooFarFutureCount, Is.EqualTo(1));
        Assert.That(ingress.ConflictCount, Is.EqualTo(1));

        for (int i = 1; i < 150; i++)
        {
            Assert.That(AdmitOne(ingress, Seat(i), 11, 1f, 0f, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        }

        ticks.Begin(11);
        for (int i = 0; i < 150; i++)
        {
            Assert.That(ingress.TryGet(Seat(i), 11, kept, out _), Is.EqualTo(FixedInputLookupResult.Present));
        }

        ticks.Commit(11);
        NetworkFixedInputAcknowledgement ack = ingress.BuildAcknowledgement(Seat(0));
        Assert.That(ack.CommittedThroughTick, Is.EqualTo(11u));
        Assert.That(ticks.CommittedTick, Is.EqualTo(11));
    }

    [Test]
    public void FixedInput_AcceptsEmptyCellReorder_AsAcceptedOutOfOrder()
    {
        var ticks = new AuthoritativeSimulationTickState();
        ticks.RestoreCommittedTick(10);
        var ingress = new AuthoritativeFixedInputIngress(CreateFixedInputConfig(seats: 2), ticks);
        SessionSeatBinding seat = Seat(0, player: 1);
        ingress.BindSeat(in seat);

        Assert.That(AdmitOne(ingress, in seat, 12, 0.5f, 0f, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(AdmitOne(ingress, in seat, 11, 1f, 0f, out FixedInputAdmissionDisposition reorder), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(reorder, Is.EqualTo(FixedInputAdmissionDisposition.AcceptedOutOfOrder));
        Assert.That(ingress.AcceptedOutOfOrderCount, Is.EqualTo(1));

        Span<byte> payload = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        Assert.That(ingress.TryGet(in seat, 11, payload, out _), Is.EqualTo(FixedInputLookupResult.Present));
        Assert.That(Physics3DFixedInputFrameCodec.TryDecode(payload, out Physics3DFixedInputFrame frame), Is.True);
        Assert.That(frame.Movement.X, Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void FixedInput_Conflict_DoesNotOverwrite_AndIsNotDuplicate()
    {
        var ticks = new AuthoritativeSimulationTickState();
        var ingress = new AuthoritativeFixedInputIngress(CreateFixedInputConfig(seats: 2), ticks);
        SessionSeatBinding seat = Seat(0, player: 1);
        ingress.BindSeat(in seat);

        Assert.That(AdmitOne(ingress, in seat, 1, 1f, 0f, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(AdmitOne(ingress, in seat, 1, 0f, 1f, out FixedInputAdmissionDisposition conflict), Is.EqualTo(FixedInputBatchAdmissionStatus.Rejected));
        Assert.That(conflict, Is.EqualTo(FixedInputAdmissionDisposition.Conflict));

        Span<byte> payload = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        Assert.That(ingress.TryGet(in seat, 1, payload, out _), Is.EqualTo(FixedInputLookupResult.Present));
        Assert.That(Physics3DFixedInputFrameCodec.TryDecode(payload, out Physics3DFixedInputFrame frame), Is.True);
        Assert.That(frame.Movement.X, Is.EqualTo(1f).Within(0.001f));
        Assert.That(ingress.ConflictCount, Is.EqualTo(1));
        Assert.That(ingress.DuplicateCount, Is.EqualTo(0));
    }

    [Test]
    public void FixedInput_ExecutionCutoffRejectsSameTick_ButAllowsFutureTick()
    {
        var ticks = new AuthoritativeSimulationTickState();
        var ingress = new AuthoritativeFixedInputIngress(CreateFixedInputConfig(seats: 3), ticks);
        for (int slot = 0; slot < 3; slot++)
        {
            ingress.BindSeat(Seat(slot, player: slot));
            Assert.That(AdmitOne(ingress, Seat(slot, player: slot), 1, 1f, 0f, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        }

        ticks.Begin(1);

        Assert.That(AdmitOne(ingress, Seat(0, player: 0), 1, 1f, 0f, out FixedInputAdmissionDisposition duplicate), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(AdmitOne(ingress, Seat(1, player: 1), 1, 0f, 1f, out FixedInputAdmissionDisposition conflict), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(AdmitOne(ingress, Seat(0, player: 0), 2, 0.5f, 0f, out FixedInputAdmissionDisposition future), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));

        Assert.Multiple(() =>
        {
            Assert.That(duplicate, Is.EqualTo(FixedInputAdmissionDisposition.RejectedAtExecutionCutoff));
            Assert.That(conflict, Is.EqualTo(FixedInputAdmissionDisposition.RejectedAtExecutionCutoff));
            Assert.That(future, Is.EqualTo(FixedInputAdmissionDisposition.Accepted));
            Assert.That(ingress.ExecutionCutoffRejectionCount, Is.EqualTo(2));
            Assert.That(ingress.DuplicateCount, Is.Zero);
            Assert.That(ingress.ConflictCount, Is.Zero);
            Assert.That(ticks.ExecutingTick, Is.EqualTo(1));
        });
    }

    [Test]
    public void FixedInput_SeatReleaseClearsHistory_AndReuseRequiresNewerGeneration()
    {
        var ticks = new AuthoritativeSimulationTickState();
        var ingress = new AuthoritativeFixedInputIngress(CreateFixedInputConfig(seats: 4, history: 8, maxFuture: 4), ticks);
        SessionSeatBinding first = Seat(0, generation: 1, player: 10);
        ingress.BindSeat(in first);

        Assert.That(AdmitOne(ingress, in first, 1, 1f, 0f, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Span<byte> payload = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        Assert.That(ingress.TryGet(in first, 1, payload, out _), Is.EqualTo(FixedInputLookupResult.Present));

        Assert.That(ingress.TryReleaseSeat(in first), Is.True);
        Assert.That(ingress.TryGet(in first, 1, payload, out _), Is.EqualTo(FixedInputLookupResult.InvalidSeat));

        Assert.Throws<InvalidOperationException>(() => ingress.BindSeat(Seat(0, generation: 1, player: 10)));
        SessionSeatBinding reused = Seat(0, generation: 2, player: 10);
        ingress.BindSeat(in reused);
        Assert.That(ingress.TryGet(in reused, 1, payload, out _), Is.EqualTo(FixedInputLookupResult.Missing));
        Assert.That(AdmitOne(ingress, in reused, 1, 1f, 0f, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
    }

    [Test]
    public void FixedInput_HasNoSecondCommitPath_AndAcknowledgementNeverAdvancesAuthority()
    {
        var ticks = new AuthoritativeSimulationTickState();
        var ingress = new AuthoritativeFixedInputIngress(CreateFixedInputConfig(seats: 2), ticks);
        ingress.BindSeat(Seat(0, player: 1));
        ingress.BindSeat(Seat(1, player: 2));

        FieldInfo[] fields = typeof(AuthoritativeFixedInputIngress).GetFields(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        foreach (FieldInfo field in fields)
        {
            Assert.That(
                field.FieldType == typeof(AuthoritativeSimulationTickState) ||
                !field.Name.Contains("committed", StringComparison.OrdinalIgnoreCase) ||
                field.Name.Contains("Acknowledged", StringComparison.OrdinalIgnoreCase) ||
                field.Name.Contains("_ticks", StringComparison.OrdinalIgnoreCase),
                Is.True,
                $"Fixed-input ingress must not own a second committed-tick authority field '{field.Name}'.");
        }

        Assert.That(ingress.TickState, Is.SameAs(ticks));
        Assert.That(AdmitOne(ingress, Seat(0, player: 1), 1, 1f, 0f, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(AdmitOne(ingress, Seat(1, player: 2), 1, 1f, 0f, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        ticks.Begin(1);
        Assert.That(ticks.ExecutingTick, Is.EqualTo(1));
        Assert.That(ticks.CommittedTick, Is.EqualTo(0));
        Assert.Throws<InvalidOperationException>(() => ingress.BuildAcknowledgement(Seat(0, player: 1)));
        Assert.That(ticks.CommittedTick, Is.EqualTo(0));

        ticks.Commit(1);
        NetworkFixedInputAcknowledgement ack = ingress.BuildAcknowledgement(Seat(0, player: 1));
        Assert.That(ack.CommittedThroughTick, Is.EqualTo(1u));
        Assert.That(ticks.CommittedTick, Is.EqualTo(1));
        NetworkFixedInputAcknowledgement repeated = ingress.BuildAcknowledgement(Seat(0, player: 1));
        Assert.That(repeated.CommittedThroughTick, Is.EqualTo(1u));
        Assert.That(ticks.CommittedTick, Is.EqualTo(1), "Repeated acknowledgement must not advance authority.");
    }

    [Test]
    public void MissingInputs_AreReportedAtDeadline_WithoutFabrication()
    {
        var ticks = new AuthoritativeSimulationTickState();
        var ingress = new AuthoritativeFixedInputIngress(CreateFixedInputConfig(seats: 150), ticks);
        for (int i = 0; i < 150; i++)
        {
            ingress.BindSeat(Seat(i, player: i));
        }

        for (int i = 0; i < 149; i++)
        {
            Assert.That(AdmitOne(ingress, Seat(i, player: i), 1, 1f, 0f, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        }

        ticks.Begin(1);
        Span<byte> destination = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        Assert.That(ingress.TryGet(Seat(149, player: 149), 1, destination, out _), Is.EqualTo(FixedInputLookupResult.MissingAtDeadline));
        Assert.That(ingress.MissingAtDeadlineCount, Is.EqualTo(1));
        Assert.That(ingress.TryGet(Seat(0, player: 0), 1, destination, out _), Is.EqualTo(FixedInputLookupResult.Present));
        Assert.Throws<InvalidOperationException>(() => ingress.BuildAcknowledgement(Seat(0, player: 0)));
        Assert.That(ticks.ExecutingTick, Is.EqualTo(1));
        Assert.That(ticks.CommittedTick, Is.EqualTo(0));

        ticks.Commit(1);
        NetworkFixedInputAcknowledgement ack = ingress.BuildAcknowledgement(Seat(149, player: 149));
        Assert.That(ack.LatestMissingInputTick, Is.EqualTo(1u));
        Assert.That(ack.CommittedThroughTick, Is.EqualTo(1u));
    }

    [Test]
    public void AcknowledgeBeforeCommit_IsRejected_AndArrivalWindowAdvancesOnlyViaTickCommit()
    {
        var ticks = new AuthoritativeSimulationTickState();
        var ingress = new AuthoritativeFixedInputIngress(CreateFixedInputConfig(seats: 2, maxFuture: 4), ticks);
        SessionSeatBinding seat = Seat(0, player: 1);
        ingress.BindSeat(in seat);

        Assert.That(AdmitOne(ingress, in seat, 5, 1f, 0f, out FixedInputAdmissionDisposition tooFar), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(tooFar, Is.EqualTo(FixedInputAdmissionDisposition.TooFarFuture));
        Assert.That(AdmitOne(ingress, in seat, 1, 1f, 0f, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));

        ticks.Begin(1);
        Assert.Throws<InvalidOperationException>(() => ingress.BuildAcknowledgement(in seat));
        Assert.That(AdmitOne(ingress, in seat, 5, 0.5f, 0f, out FixedInputAdmissionDisposition stillTooFar), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(stillTooFar, Is.EqualTo(FixedInputAdmissionDisposition.TooFarFuture));

        ticks.Commit(1);
        ingress.BuildAcknowledgement(in seat);

        Assert.That(AdmitOne(ingress, in seat, 1, 0.25f, 0f, out FixedInputAdmissionDisposition late), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(late, Is.EqualTo(FixedInputAdmissionDisposition.Late));
        Assert.That(AdmitOne(ingress, in seat, 5, 0.5f, 0f, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(AdmitOne(ingress, in seat, 6, 0.75f, 0f, out FixedInputAdmissionDisposition future), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(future, Is.EqualTo(FixedInputAdmissionDisposition.TooFarFuture));

        AdvanceCommitted(ticks, 2);
        Assert.That(AdmitOne(ingress, in seat, 6, 0.75f, 0f, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(AdmitOne(ingress, in seat, 7, 1f, 0f, out FixedInputAdmissionDisposition nextFuture), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(nextFuture, Is.EqualTo(FixedInputAdmissionDisposition.TooFarFuture));
    }

    [Test]
    public void ReplicationPacket_OverCapacityInterest_IsRejected_AndPriorBaselineRemainsUsable()
    {
        var entities = new NetworkEntityTable(capacity: 4);
        var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 8);
        var channel = new AuthoritativeReplicationChannel(entities, replicationEntityCapacityPerSeat: 2, baselineCapacity: 2, disclosureLog);
        var packet = new ReplicationPacketBuffer(entityCapacity: 2);
        var h0 = new NetworkEntityHandle(0, 1);
        var h1 = new NetworkEntityHandle(1, 1);
        var h2 = new NetworkEntityHandle(2, 1);
        ReplicatedEntityState[] first =
        [
            State(h0, 1, 10),
            State(h1, 1, 20)
        ];
        ReplicationDisclosureInput[] firstDisclosures =
        [
            Visible(h0),
            Visible(h1)
        ];
        Assert.That(channel.BuildFull(SessionEpoch, 3, 1, first, firstDisclosures, packet), Is.EqualTo(ReplicationBuildResult.Success));
        Assert.That(packet.UpsertCount, Is.EqualTo(2));

        ReplicatedEntityState[] overflow =
        [
            State(h0, 2, 1),
            State(h1, 2, 2),
            State(h2, 2, 3)
        ];
        ReplicationDisclosureInput[] overflowDisclosures =
        [
            Visible(h0),
            Visible(h1),
            Visible(h2)
        ];
        Assert.That(
            channel.BuildFull(SessionEpoch, 6, 2, overflow, overflowDisclosures, packet),
            Is.EqualTo(ReplicationBuildResult.InvalidInput));
        ReplicatedEntityState[] changed =
        [
            State(h0, 2, 11),
            State(h1, 2, 21)
        ];
        Assert.That(
            channel.BuildDelta(SessionEpoch, 6, 2, acknowledgedBaselineId: 1, changed, firstDisclosures, packet),
            Is.EqualTo(ReplicationBuildResult.Success));
        Assert.That(packet.Header.BaselineSnapshotId, Is.EqualTo(1ul));
        Assert.That(packet.UpsertCount, Is.EqualTo(2));
    }

    [Test]
    public void ReplicationDelta_EnterLeaveReenter_EmitsRevealConcealAndBaselineMissRequiresFull()
    {
        using World ecs = World.Create();
        var entities = new NetworkEntityTable(capacity: 4);
        var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 16);
        var channel = new AuthoritativeReplicationChannel(entities, replicationEntityCapacityPerSeat: 4, baselineCapacity: 4, disclosureLog);
        var packet = new ReplicationPacketBuffer(entityCapacity: 4);
        Entity entity = ecs.Create();
        Assert.That(entities.TryAllocate(entity, out NetworkEntityHandle handle), Is.True);

        Assert.That(
            channel.BuildDelta(SessionEpoch, 3, 1, acknowledgedBaselineId: 99, ReadOnlySpan<ReplicatedEntityState>.Empty, ReadOnlySpan<ReplicationDisclosureInput>.Empty, packet),
            Is.EqualTo(ReplicationBuildResult.BaselineUnavailable));

        Assert.That(
            channel.BuildFull(SessionEpoch, 3, 1, [State(handle, 1, 10)], [Visible(handle)], packet),
            Is.EqualTo(ReplicationBuildResult.Success));
        Assert.That(packet.UpsertCount, Is.EqualTo(1));
        Assert.That(packet.DisclosureChanges[0].Kind, Is.EqualTo(ReplicationDisclosureChangeKind.Reveal));

        Assert.That(
            channel.BuildDelta(SessionEpoch, 6, 2, 1, [State(handle, 2, 20)], [Visible(handle)], packet),
            Is.EqualTo(ReplicationBuildResult.Success));
        Assert.That(packet.UpsertCount, Is.EqualTo(1));

        Assert.That(
            channel.BuildDelta(SessionEpoch, 9, 3, 2, ReadOnlySpan<ReplicatedEntityState>.Empty, ReadOnlySpan<ReplicationDisclosureInput>.Empty, packet),
            Is.EqualTo(ReplicationBuildResult.Success));
        Assert.That(packet.DisclosureChanges.ToArray(), Has.Some.Matches<ReplicationDisclosureChange>(
            change => change.Kind == ReplicationDisclosureChangeKind.Conceal));

        Assert.That(entities.TryRelease(handle), Is.True);
        Assert.That(entities.TryAllocate(entity, out NetworkEntityHandle nextGen), Is.True);
        Assert.That(nextGen.Slot, Is.EqualTo(handle.Slot));
        Assert.That(nextGen.Generation, Is.GreaterThan(handle.Generation));
        Assert.That(
            channel.BuildDelta(SessionEpoch, 12, 4, 3, [State(nextGen, 1, 30)], [Visible(nextGen)], packet),
            Is.EqualTo(ReplicationBuildResult.Success));
        Assert.That(packet.UpsertCount, Is.EqualTo(1));
        Assert.That(packet.Upserts[0].Entity.Generation, Is.EqualTo(nextGen.Generation));
    }

    [Test]
    public void AoiInterest_RejectsUnsortedOrOverCapacityInterest_WithoutPartialSuccess()
    {
        using World ecs = World.Create();
        using var physics = new Physics3DWorld(new Physics3DWorldConfig
        {
            MobileBodyCapacity = 2,
            StaticBodyCapacity = 1,
            ShapeCapacity = 8,
            InactiveIslandCapacity = 2,
            ConstraintCapacity = 8,
            ConstraintsPerTypeBatchCapacity = 8,
            ConstraintCountPerBodyEstimate = 4,
            ContactPairCapacityPerWorker = 32,
            ActuationCommandCapacity = 8,
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
        });
        var entities = new NetworkEntityTable(capacity: 2);
        var knowledge = new KnowledgeProjectionStore(initialCapacity: 4);
        using var lifecycle = new Physics3DNetworkPlayerLifecycle(
            ecs,
            physics,
            entities,
            knowledge,
            seatCapacity: 2,
            ReplicationSchemaId,
            new Physics3DNetworkPlayerBodyConfig
            {
                RadiusCm = 30f,
                CylinderLengthCm = 100f,
                Mass = 80f,
                CollisionLayer = LayerMask.All,
                Material = new Physics3DMaterial(0.8f, 200f, 30f, 1f),
                ContinuousDetection = Physics3DContinuousDetectionMode.Passive
            },
            new Physics3DNetworkPlayerSpawnConfig
            {
                OriginCm = Vector3.Zero,
                ColumnSpacingCm = 10f,
                RowSpacingCm = 10f,
                Columns = 2
            });
        SessionSeatBinding first = Seat(0);
        SessionSeatBinding second = Seat(1);
        Assert.That(lifecycle.TryResolveController(in first, out _), Is.True);
        Assert.That(lifecycle.TryResolveController(in second, out _), Is.True);
        var interest = new Physics3DNetworkAoiInterestPort(
            ecs,
            entities,
            lifecycle,
            new Physics3DNetworkAoiConfig { RadiusCm = 100f, GlobalEntityCapacity = 2 });

        var tooSmall = new NetworkEntityHandle[1];
        Assert.That(interest.TryCopyInterest(in first, tooSmall, out int required), Is.False);
        Assert.That(required, Is.EqualTo(2));
        Assert.That(interest.LastFailure, Is.EqualTo(Physics3DNetworkAoiFailure.DestinationCapacityExceeded));

        SessionSeatBinding unknown = Seat(0, generation: 9);
        Assert.That(interest.TryCopyInterest(in unknown, tooSmall, out _), Is.False);
        Assert.That(interest.LastFailure, Is.EqualTo(Physics3DNetworkAoiFailure.UnknownSeat));
    }

    [Test]
    public void LocalPrediction_StoresExactFixedInputPayload_AndRejectsRemoteRollback()
    {
        Physics3DNetConfig config = CreateLocalConfig(localPredictionHistoryTicks: 8);
        var history = new Physics3DNetLocalPredictionHistory(config);
        history.BindLocalDriven(networkEntityId: 42, generation: 1, Physics3DNetLocalDrivenKind.Vehicle);

        Span<byte> payload = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        for (long tick = 1; tick <= 5; tick++)
        {
            EncodeMovement(payload, tick / 10f, 0f);
            history.Record(
                new Physics3DNetPredictedPose(
                    tick,
                    new Vector3(tick * 10f, 0f, 0f),
                    Quaternion.Identity,
                    Vector3.UnitX,
                    Vector3.Zero),
                payload);
        }

        Assert.Throws<InvalidOperationException>(() => history.RejectRemoteOrWorldRollback(99, 1));

        var poses = new Physics3DNetPredictedPose[8];
        var inputs = new byte[8 * Physics3DFixedInputFrameCodec.PayloadBytes];
        Physics3DNetCorrectionReplayRange range = history.BeginCorrectionReplay(
            networkEntityId: 42,
            generation: 1,
            authoritativeConfirmedTick: 3,
            poses,
            inputs);
        Assert.That(range.FromTickInclusive, Is.EqualTo(4));
        Assert.That(range.ToTickInclusive, Is.EqualTo(5));
        Assert.That(range.FrameCount, Is.EqualTo(2));
        Assert.That(Physics3DFixedInputFrameCodec.TryDecode(inputs.AsSpan(0, 8), out Physics3DFixedInputFrame first), Is.True);
        Assert.That(first.Movement.X, Is.EqualTo(0.4f).Within(0.001f));
        Assert.That(Physics3DFixedInputFrameCodec.TryDecode(inputs.AsSpan(8, 8), out Physics3DFixedInputFrame second), Is.True);
        Assert.That(second.Movement.X, Is.EqualTo(0.5f).Within(0.001f));

        Assert.Throws<InvalidOperationException>(
            () => history.BeginCorrectionReplay(99, 1, 3, poses, inputs));
    }

    [Test]
    public void RemoteInterpolation_UsesHandleSlotAsLane_AndReportsUnderflowOverflowExplicitly()
    {
        Physics3DNetConfig config = CreateLocalConfig(remoteInterpolationHistoryTicks: 4);
        var buffer = new Physics3DNetRemoteInterpolationBuffer(config, remoteEntityCapacity: 8);
        var handle = new NetworkEntityHandle(slot: 7, generation: 1);
        buffer.Track(in handle);
        buffer.Push(in handle, new Physics3DNetRemoteSample(10, new Vector3(0f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        buffer.Push(in handle, new Physics3DNetRemoteSample(12, new Vector3(20f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));

        Physics3DNetInterpolationSample under = buffer.Sample(in handle, renderTick: 9f);
        Assert.That(under.Kind, Is.EqualTo(Physics3DNetInterpolationResultKind.Underflow));

        Physics3DNetInterpolationSample mid = buffer.Sample(in handle, renderTick: 11f);
        Assert.That(mid.Kind, Is.EqualTo(Physics3DNetInterpolationResultKind.Sampled));
        Assert.That(mid.PositionCm.X, Is.EqualTo(10f).Within(0.001f));

        Physics3DNetInterpolationSample over = buffer.Sample(in handle, renderTick: 13f);
        Assert.That(over.Kind, Is.EqualTo(Physics3DNetInterpolationResultKind.Overflow));

        var temporal = Assert.Throws<Physics3DNetTemporalOrderException>(
            () => buffer.Push(in handle, new Physics3DNetRemoteSample(11, Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero)));
        Assert.That(temporal!.NetworkEntitySlot, Is.EqualTo(7));
        Assert.That(temporal.NewestTick, Is.EqualTo(12));
        Assert.That(temporal.AttemptedTick, Is.EqualTo(11));

        var mismatchedNewer = new NetworkEntityHandle(slot: 7, generation: 2);
        Assert.Throws<InvalidOperationException>(() => buffer.Push(in mismatchedNewer, new Physics3DNetRemoteSample(13, Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero)));
        Assert.Throws<InvalidOperationException>(() => buffer.Untrack(in mismatchedNewer));
        buffer.Untrack(in handle);
        Assert.Throws<InvalidOperationException>(() => buffer.Untrack(in handle));
    }

    [Test]
    public void RemoteInterpolation_RejectsStaleGenerationTrackAndSampleUpdates()
    {
        Physics3DNetConfig config = CreateLocalConfig(remoteInterpolationHistoryTicks: 4);
        var buffer = new Physics3DNetRemoteInterpolationBuffer(config, remoteEntityCapacity: 4);
        var gen1 = new NetworkEntityHandle(slot: 2, generation: 1);
        var gen2 = new NetworkEntityHandle(slot: 2, generation: 2);

        buffer.Track(in gen1);
        buffer.Push(in gen1, new Physics3DNetRemoteSample(10, new Vector3(10f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        Assert.That(buffer.GetSampleCount(in gen1), Is.EqualTo(1));

        // Newer generation may reclaim the same slot and clears prior samples.
        buffer.Track(in gen2);
        Assert.That(buffer.GetSampleCount(in gen2), Is.EqualTo(0));
        buffer.Push(in gen2, new Physics3DNetRemoteSample(20, new Vector3(20f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        Assert.That(buffer.GetSampleCount(in gen2), Is.EqualTo(1));

        // Stale Track for the older generation must not replace the newer occupant.
        Assert.Throws<InvalidOperationException>(() => buffer.Track(in gen1));
        Assert.That(buffer.GetSampleCount(in gen2), Is.EqualTo(1));
        Assert.That(buffer.TryGetSampleTick(in gen2, 20, out Physics3DNetRemoteSample kept), Is.True);
        Assert.That(kept.PositionCm.X, Is.EqualTo(20f));

        // Stale sample/update paths also reject the older generation identity.
        Assert.Throws<InvalidOperationException>(
            () => buffer.Push(in gen1, new Physics3DNetRemoteSample(21, Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero)));
        Assert.Throws<InvalidOperationException>(() => _ = buffer.Sample(in gen1, renderTick: 20f));
        Assert.Throws<InvalidOperationException>(() => _ = buffer.GetSampleCount(in gen1));
        Assert.Throws<InvalidOperationException>(() => _ = buffer.TryGetSampleTick(in gen1, 20, out _));
        Assert.Throws<InvalidOperationException>(() => buffer.Untrack(in gen1));

        Physics3DNetInterpolationSample sample = buffer.Sample(in gen2, renderTick: 20f);
        Assert.That(sample.Kind, Is.EqualTo(Physics3DNetInterpolationResultKind.Sampled));
        Assert.That(sample.PositionCm.X, Is.EqualTo(20f));
        buffer.Untrack(in gen2);
    }

    [Test]
    public void RemoteInterpolation_TickJumpPurgesStaleWindowSamples()
    {
        Physics3DNetConfig config = CreateLocalConfig(remoteInterpolationHistoryTicks: 4);
        var buffer = new Physics3DNetRemoteInterpolationBuffer(config, remoteEntityCapacity: 2);
        var handle = new NetworkEntityHandle(1, 1);
        buffer.Track(in handle);
        for (long tick = 1; tick <= 4; tick++)
        {
            buffer.Push(in handle, new Physics3DNetRemoteSample(tick, new Vector3(tick, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        }

        buffer.Push(in handle, new Physics3DNetRemoteSample(100, new Vector3(100f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        Assert.That(buffer.GetSampleCount(in handle), Is.EqualTo(1));
        Assert.That(buffer.TryGetSampleTick(in handle, 1, out _), Is.False);
        Assert.That(buffer.TryGetSampleTick(in handle, 100, out Physics3DNetRemoteSample kept), Is.True);
        Assert.That(kept.PositionCm.X, Is.EqualTo(100f));

        Physics3DNetInterpolationSample sample = buffer.Sample(in handle, 100f);
        Assert.That(sample.Kind, Is.EqualTo(Physics3DNetInterpolationResultKind.Sampled));
        Assert.That(sample.LowerTick, Is.EqualTo(100));
    }

    [Test]
    public void PublicStateConstructors_RejectNonFiniteAndNonUnitQuaternion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Physics3DNetRemoteSample(1, new Vector3(float.NaN, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Physics3DNetRemoteSample(1, Vector3.Zero, default, Vector3.Zero, Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Physics3DNetRemoteSample(
                1,
                Vector3.Zero,
                new Quaternion(2f, 0f, 0f, 0f),
                Vector3.Zero,
                Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Physics3DNetPredictedPose(0, Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new NetworkEntityHandle(slot: 0, generation: 0));
    }

    [Test]
    public void ReplayTimeline_ReportsFirstDivergentTick_AsDeterminismCheckNotWorldRollback()
    {
        Physics3DNetConfig config = CreateLocalConfig(replayEventCapacity: 16);
        var timeline = new Physics3DNetReplayTimeline(config);
        timeline.RecordInputAccepted(1);
        timeline.RecordSnapshotPublished(3);
        timeline.RecordHashComparison(3, leftHash: 0x11, rightHash: 0x11);
        timeline.RecordHashComparison(6, leftHash: 0x22, rightHash: 0x33);
        timeline.RecordHashComparison(9, leftHash: 0x44, rightHash: 0x55);

        Physics3DNetReplayDivergence divergence = timeline.FindFirstDivergence();
        Assert.That(divergence.Found, Is.True);
        Assert.That(divergence.FirstDivergentTick, Is.EqualTo(6));
        Assert.That(divergence.LeftHash, Is.EqualTo(0x22ul));
        Assert.That(divergence.RightHash, Is.EqualTo(0x33ul));
    }

    [Test]
    public void CompatibilityGate_RejectsExactReplayTakeoverOnMismatch()
    {
        Physics3DNetConfig config = CreateLocalConfig();
        var required = new Physics3DNetCompatibilityFingerprint(
            buildId: "build-a",
            configHash: Physics3DNetCompatibilityFingerprint.HashConfig(config),
            kernelId: "bepu-2.4",
            simdProfile: "avx2",
            workerCount: 4,
            scenarioId: "vertical-slice");
        var gate = new Physics3DNetCompatibilityGate(required);

        var mismatched = new Physics3DNetCompatibilityFingerprint(
            buildId: "build-b",
            configHash: required.ConfigHash,
            kernelId: "bepu-2.4",
            simdProfile: "avx2",
            workerCount: 4,
            scenarioId: "vertical-slice");

        var ex = Assert.Throws<Physics3DNetCompatibilityMismatchException>(() => gate.RequireMatch(mismatched));
        Assert.That(ex!.Expected, Is.EqualTo(required));
        Assert.That(ex.Actual, Is.EqualTo(mismatched));

        gate.RequireMatch(required);
    }

    [Test]
    public void WorldRestorePort_IsExplicitlyUnsupported_WithoutFallback_AndReportOwnsCopy()
    {
        var port = new Physics3DNetUnsupportedWorldRestorePort();
        Assert.That(port.IsSupported, Is.False);
        Assert.That(port.Coverage.AllSupported, Is.False);
        Assert.That(port.Coverage.Items.Length, Is.EqualTo(7));

        var mutable =
            new[]
            {
                new Physics3DNetWorldRestoreCoverageItem("bodies", supported: true, reason: "temp"),
                new Physics3DNetWorldRestoreCoverageItem("stable slots", supported: true, reason: "temp")
            };
        var report = new Physics3DNetWorldRestoreCoverageReport(mutable);
        Assert.That(report.AllSupported, Is.True);
        mutable[0] = new Physics3DNetWorldRestoreCoverageItem("bodies", supported: false, reason: "mutated");
        Assert.That(report.Items[0].Supported, Is.True);

        var ex = Assert.Throws<Physics3DNetWorldRestoreUnsupportedException>(
            () => port.RestoreExactWorldState(30));
        Assert.That(ex!.SnapshotTick, Is.EqualTo(30));
        Assert.That(ex.Message, Does.Contain("unsupported"));
    }

    [Test]
    public void Assembly_DoesNotReferenceBepuPackages()
    {
        Assembly assembly = typeof(Physics3DNetLocalPredictionHistory).Assembly;
        foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
        {
            Assert.That(
                reference.Name,
                Is.Not.EqualTo("BepuPhysics").And.Not.EqualTo("BepuUtilities"),
                $"Physics3DNet must not reference Bepu package '{reference.Name}'.");
        }
    }

    [Test]
    public void WarmedNetworkingHotPaths_HaveZeroManagedAllocationsOnCallingThread()
    {
        Physics3DNetConfig local = CreateLocalConfig(
            localPredictionHistoryTicks: 16,
            remoteInterpolationHistoryTicks: 8,
            replayEventCapacity: 256);
        var ticks = new AuthoritativeSimulationTickState();
        var ingress = new AuthoritativeFixedInputIngress(CreateFixedInputConfig(seats: 150, history: 16, maxFuture: 8), ticks);
        var entities = new NetworkEntityTable(capacity: 16);
        var disclosureLog = new ReplicationDisclosureChangeLog(capacity: 64);
        var channel = new AuthoritativeReplicationChannel(entities, replicationEntityCapacityPerSeat: 8, baselineCapacity: 8, disclosureLog);
        var packet = new ReplicationPacketBuffer(entityCapacity: 8);
        var prediction = new Physics3DNetLocalPredictionHistory(local);
        var remote = new Physics3DNetRemoteInterpolationBuffer(local, remoteEntityCapacity: 16);
        var timeline = new Physics3DNetReplayTimeline(local);

        var seats = new SessionSeatBinding[150];
        for (int i = 0; i < 150; i++)
        {
            seats[i] = Seat(i, player: i);
            ingress.BindSeat(in seats[i]);
        }

        prediction.BindLocalDriven(0, 1, Physics3DNetLocalDrivenKind.Character);
        var remoteA = new NetworkEntityHandle(1, 1);
        var remoteB = new NetworkEntityHandle(2, 1);
        remote.Track(in remoteA);
        remote.Track(in remoteB);

        var payload = new byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        EncodeMovement(payload, 1f, 0f);
        var handles = new NetworkEntityHandle[4];
        for (int i = 0; i < handles.Length; i++)
        {
            handles[i] = new NetworkEntityHandle(i, 1);
        }

        var states = new ReplicatedEntityState[4];
        var disclosures = new ReplicationDisclosureInput[4];
        var poses = new Physics3DNetPredictedPose[16];
        var replayInputs = new byte[16 * Physics3DFixedInputFrameCodec.PayloadBytes];
        ulong snapshotId = 0;
        int snapshotInterval = local.SnapshotIntervalTicks;

        void DriveTick(int tick)
        {
            for (int p = 0; p < 150; p++)
            {
                if (AdmitOne(ingress, in seats[p], (uint)tick, 1f, 0f, out _) != FixedInputBatchAdmissionStatus.Success)
                {
                    throw new InvalidOperationException($"Input admit failed for seat {p} tick {tick}.");
                }
            }

            ticks.Begin(tick);
            for (int p = 0; p < 150; p++)
            {
                if (ingress.TryGet(in seats[p], (uint)tick, payload, out _) != FixedInputLookupResult.Present)
                {
                    throw new InvalidOperationException($"Expected complete inputs for tick {tick}.");
                }
            }

            ticks.Commit(tick);
            _ = ingress.BuildAcknowledgement(in seats[0]);
            timeline.RecordInputAccepted(tick);

            if (tick % snapshotInterval == 0)
            {
                snapshotId++;
                for (int i = 0; i < states.Length; i++)
                {
                    states[i] = State(handles[i], revision: (uint)tick, value: i + tick);
                    disclosures[i] = Visible(handles[i]);
                }

                ReplicationBuildResult result = snapshotId == 1
                    ? channel.BuildFull(SessionEpoch, (uint)tick, snapshotId, states, disclosures, packet)
                    : channel.BuildDelta(SessionEpoch, (uint)tick, snapshotId, snapshotId - 1, states, disclosures, packet);
                if (result != ReplicationBuildResult.Success)
                {
                    throw new InvalidOperationException($"Replication build failed: {result}");
                }

                timeline.RecordSnapshotPublished(tick);
                timeline.RecordHashComparison(tick, leftHash: (ulong)tick, rightHash: (ulong)tick);
            }

            if (!Physics3DFixedInputFrameCodec.TryEncode(new Vector2((tick % 10) / 10f, 0f), payload))
            {
                throw new InvalidOperationException($"Failed to encode prediction payload for tick {tick}.");
            }

            prediction.Record(
                new Physics3DNetPredictedPose(tick, new Vector3(tick, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero),
                payload);
            remote.Push(in remoteA, new Physics3DNetRemoteSample(tick, new Vector3(tick, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
            _ = remote.Sample(in remoteA, tick - 0.5f);
        }

        for (int tick = 1; tick <= 32; tick++)
        {
            DriveTick(tick);
        }

        remote.Push(in remoteB, new Physics3DNetRemoteSample(1, new Vector3(1f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        remote.Push(in remoteB, new Physics3DNetRemoteSample(2, new Vector3(2f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        remote.Push(in remoteB, new Physics3DNetRemoteSample(3, new Vector3(3f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        remote.Push(in remoteB, new Physics3DNetRemoteSample(4, new Vector3(4f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        remote.Push(in remoteB, new Physics3DNetRemoteSample(100, new Vector3(100f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        _ = remote.Sample(in remoteB, 100f);

        prediction.BeginCorrectionReplay(0, 1, 28, poses, replayInputs);
        _ = timeline.FindFirstDivergence();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 33; tick <= 64; tick++)
        {
            DriveTick(tick);
        }

        remote.Push(in remoteB, new Physics3DNetRemoteSample(200, new Vector3(200f, 0f, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        _ = remote.Sample(in remoteB, 200f);
        _ = timeline.FindFirstDivergence();

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Physics3DNet warmed paths allocated {allocated} managed bytes.");
    }

    private static ReplicatedEntityState State(NetworkEntityHandle handle, uint revision, int value) =>
        new(handle, ReplicationSchemaId, revision, new ReplicationStateVector(value, 0, 0, 0));

    private static ReplicationDisclosureInput Visible(NetworkEntityHandle handle) =>
        new(handle, KnowledgePresence.LiveVisible);
}
