using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Simulation;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class AuthoritativeFixedInputIngressTests
{
    private const ushort SchemaId = 3;
    private const ushort PayloadBytes = 12;

    private static FixedInputProtocolConfig CreateConfig(
        int seats = 4,
        int history = 8,
        int maxFuture = 4,
        ulong epoch = 1) =>
        new(
            seats,
            history,
            SchemaId,
            PayloadBytes,
            maxFuture,
            maxFramesPerBatch: 4,
            maxDatagramPayloadBytes: 1200,
            epoch);

    private static SessionSeatBinding Seat(int slot = 0, uint generation = 1, int player = 1) =>
        new(slot, generation, new PlayerId(player));

    private static void FillPayload(Span<byte> payload, byte marker)
    {
        payload.Clear();
        payload[0] = marker;
    }

    private static FixedInputBatchAdmissionStatus AdmitOne(
        AuthoritativeFixedInputIngress ingress,
        in SessionSeatBinding seat,
        uint tick,
        byte marker,
        out FixedInputAdmissionDisposition disposition,
        uint ackCommitted = 0)
    {
        Span<uint> ticks = stackalloc uint[1] { tick };
        Span<byte> payloads = stackalloc byte[PayloadBytes];
        FillPayload(payloads, marker);
        Span<FixedInputAdmissionDisposition> dispositions = stackalloc FixedInputAdmissionDisposition[1];
        var header = new NetworkFixedInputBatchHeader(1, SchemaId, PayloadBytes, ackCommitted, 1);
        FixedInputBatchAdmissionStatus status = ingress.TryAdmitBatch(in seat, in header, ticks, payloads, dispositions);
        disposition = dispositions[0];
        return status;
    }

    [Test]
    public void Admit_ClassifiesAcceptedDuplicateConflictLateFutureCutoffAndOutOfOrder()
    {
        var ticks = new AuthoritativeSimulationTickState();
        ticks.RestoreCommittedTick(10);
        var ingress = new AuthoritativeFixedInputIngress(CreateConfig(), ticks);
        SessionSeatBinding seat = Seat();
        ingress.BindSeat(in seat);

        Assert.That(AdmitOne(ingress, in seat, 11, 1, out FixedInputAdmissionDisposition accepted), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(accepted, Is.EqualTo(FixedInputAdmissionDisposition.Accepted));

        Assert.That(AdmitOne(ingress, in seat, 11, 1, out FixedInputAdmissionDisposition duplicate), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(duplicate, Is.EqualTo(FixedInputAdmissionDisposition.Duplicate));

        Assert.That(AdmitOne(ingress, in seat, 11, 9, out FixedInputAdmissionDisposition conflict), Is.EqualTo(FixedInputBatchAdmissionStatus.Rejected));
        Assert.That(conflict, Is.EqualTo(FixedInputAdmissionDisposition.Conflict));
        Assert.That(ingress.ConflictCount, Is.EqualTo(1));

        Assert.That(AdmitOne(ingress, in seat, 10, 2, out FixedInputAdmissionDisposition late), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(late, Is.EqualTo(FixedInputAdmissionDisposition.Late));

        Assert.That(AdmitOne(ingress, in seat, 20, 2, out FixedInputAdmissionDisposition tooFar), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(tooFar, Is.EqualTo(FixedInputAdmissionDisposition.TooFarFuture));

        Assert.That(AdmitOne(ingress, in seat, 13, 3, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(AdmitOne(ingress, in seat, 12, 4, out FixedInputAdmissionDisposition reorder), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(reorder, Is.EqualTo(FixedInputAdmissionDisposition.AcceptedOutOfOrder));

        Span<byte> kept = stackalloc byte[PayloadBytes];
        Assert.That(ingress.TryGet(in seat, 11, kept, out _), Is.EqualTo(FixedInputLookupResult.Present));
        Assert.That(kept[0], Is.EqualTo(1));
    }

    [Test]
    public void Admit_ConflictInMixedBatch_RejectsEntireBatchWithoutPartialMutation()
    {
        var ticks = new AuthoritativeSimulationTickState();
        ticks.RestoreCommittedTick(10);
        var ingress = new AuthoritativeFixedInputIngress(CreateConfig(maxFuture: 8), ticks);
        SessionSeatBinding seat = Seat();
        ingress.BindSeat(in seat);

        Assert.That(AdmitOne(ingress, in seat, 11, 1, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));

        Span<uint> batchTicks = stackalloc uint[2] { 11, 12 };
        Span<byte> batchPayloads = stackalloc byte[PayloadBytes * 2];
        FillPayload(batchPayloads.Slice(0, PayloadBytes), 9); // conflicts with accepted tick 11
        FillPayload(batchPayloads.Slice(PayloadBytes, PayloadBytes), 2); // would otherwise be Accepted
        Span<FixedInputAdmissionDisposition> dispositions = stackalloc FixedInputAdmissionDisposition[2];
        var header = new NetworkFixedInputBatchHeader(1, SchemaId, PayloadBytes, 10, 2);

        Assert.That(
            ingress.TryAdmitBatch(in seat, in header, batchTicks, batchPayloads, dispositions),
            Is.EqualTo(FixedInputBatchAdmissionStatus.Rejected));
        Assert.That(dispositions[0], Is.EqualTo(FixedInputAdmissionDisposition.Conflict));
        Assert.That(dispositions[1], Is.EqualTo(FixedInputAdmissionDisposition.Conflict));
        Assert.That(ingress.ConflictCount, Is.EqualTo(1));
        Assert.That(ingress.AcceptedCount, Is.EqualTo(1)); // only the pre-batch accept

        Span<byte> kept = stackalloc byte[PayloadBytes];
        Assert.That(ingress.TryGet(in seat, 11, kept, out _), Is.EqualTo(FixedInputLookupResult.Present));
        Assert.That(kept[0], Is.EqualTo(1));
        Assert.That(ingress.TryGet(in seat, 12, kept, out _), Is.EqualTo(FixedInputLookupResult.Missing));
    }

    [Test]
    public void Admit_RejectsAtExecutionCutoff_AndReaderReportsMissingAtDeadline()
    {
        var ticks = new AuthoritativeSimulationTickState();
        var ingress = new AuthoritativeFixedInputIngress(CreateConfig(), ticks);
        SessionSeatBinding seat = Seat();
        ingress.BindSeat(in seat);

        Assert.That(AdmitOne(ingress, in seat, 1, 1, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        ticks.Begin(1);

        Assert.That(AdmitOne(ingress, in seat, 1, 1, out FixedInputAdmissionDisposition cutoff), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(cutoff, Is.EqualTo(FixedInputAdmissionDisposition.RejectedAtExecutionCutoff));

        SessionSeatBinding other = Seat(slot: 1, generation: 1, player: 2);
        ingress.BindSeat(in other);
        Span<byte> destination = stackalloc byte[PayloadBytes];
        Assert.That(
            ingress.TryGet(in other, 1, destination, out _),
            Is.EqualTo(FixedInputLookupResult.MissingAtDeadline));
        Assert.That(ingress.MissingAtDeadlineCount, Is.EqualTo(1));

        ticks.Commit(1);
        Assert.That(ticks.CommittedTick, Is.EqualTo(1));
    }

    [Test]
    public void BuildAcknowledgement_DoesNotInferMissingFromFutureGaps_AndAdvancesOnlyOnObservedDeadlineMisses()
    {
        var ticks = new AuthoritativeSimulationTickState();
        ticks.RestoreCommittedTick(10);
        var ingress = new AuthoritativeFixedInputIngress(CreateConfig(maxFuture: 8), ticks);
        SessionSeatBinding seat = Seat();
        ingress.BindSeat(in seat);

        Assert.That(AdmitOne(ingress, in seat, 12, 1, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(AdmitOne(ingress, in seat, 13, 2, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));

        NetworkFixedInputAcknowledgement beforeMiss = ingress.BuildAcknowledgement(in seat);
        Assert.That(beforeMiss.LatestReceivedTick, Is.EqualTo(13u));
        Assert.That(beforeMiss.ReceivedMask & 1UL, Is.EqualTo(1UL));
        Assert.That((beforeMiss.ReceivedMask >> 1) & 1UL, Is.EqualTo(1UL));
        // Gap at tick 11 must not be reported as missing — only observed MissingAtDeadline counts.
        Assert.That(beforeMiss.LatestMissingInputTick, Is.EqualTo(0u));

        ticks.Begin(11);
        Span<byte> destination = stackalloc byte[PayloadBytes];
        Assert.That(
            ingress.TryGet(in seat, 11, destination, out _),
            Is.EqualTo(FixedInputLookupResult.MissingAtDeadline));
        NetworkFixedInputAcknowledgement afterOneMiss = ingress.BuildAcknowledgement(in seat);
        Assert.That(afterOneMiss.LatestMissingInputTick, Is.EqualTo(11u));
        ticks.Commit(11);

        ticks.Begin(12);
        Assert.That(ingress.TryGet(in seat, 12, destination, out _), Is.EqualTo(FixedInputLookupResult.Present));
        ticks.Commit(12);

        ticks.Begin(13);
        Assert.That(ingress.TryGet(in seat, 13, destination, out _), Is.EqualTo(FixedInputLookupResult.Present));
        ticks.Commit(13);

        ticks.Begin(14);
        Assert.That(
            ingress.TryGet(in seat, 14, destination, out _),
            Is.EqualTo(FixedInputLookupResult.MissingAtDeadline));
        NetworkFixedInputAcknowledgement afterLaterMiss = ingress.BuildAcknowledgement(in seat);
        Assert.That(afterLaterMiss.LatestMissingInputTick, Is.EqualTo(14u));
        Assert.That(ingress.MissingAtDeadlineCount, Is.EqualTo(2));
    }

    [Test]
    public void ReleaseSeat_ClearsMissingAtDeadlineTracking()
    {
        var ticks = new AuthoritativeSimulationTickState();
        var ingress = new AuthoritativeFixedInputIngress(CreateConfig(), ticks);
        SessionSeatBinding seat = Seat();
        ingress.BindSeat(in seat);

        ticks.Begin(1);
        Span<byte> destination = stackalloc byte[PayloadBytes];
        Assert.That(ingress.TryGet(in seat, 1, destination, out _), Is.EqualTo(FixedInputLookupResult.MissingAtDeadline));
        Assert.That(ingress.BuildAcknowledgement(in seat).LatestMissingInputTick, Is.EqualTo(1u));
        ticks.Commit(1);

        Assert.That(ingress.TryReleaseSeat(in seat), Is.True);
        SessionSeatBinding next = Seat(generation: 2);
        ingress.BindSeat(in next);
        Assert.That(ingress.BuildAcknowledgement(in next).LatestMissingInputTick, Is.EqualTo(0u));
    }

    [Test]
    public void Admit_AllOrNothing_OnHardRejects_AndInvalidSeatEpochSchemaPayload()
    {
        var ticks = new AuthoritativeSimulationTickState();
        ticks.RestoreCommittedTick(1);
        var ingress = new AuthoritativeFixedInputIngress(CreateConfig(history: 8, maxFuture: 8), ticks);
        SessionSeatBinding seat = Seat();
        ingress.BindSeat(in seat);

        SessionSeatBinding stale = Seat(generation: 99);
        Assert.That(AdmitOne(ingress, in stale, 3, 1, out FixedInputAdmissionDisposition badSeat), Is.EqualTo(FixedInputBatchAdmissionStatus.Rejected));
        Assert.That(badSeat, Is.EqualTo(FixedInputAdmissionDisposition.InvalidSeatGeneration));

        Span<uint> one = stackalloc uint[1] { 3 };
        Span<byte> payload = stackalloc byte[PayloadBytes];
        Span<FixedInputAdmissionDisposition> oneDisposition = stackalloc FixedInputAdmissionDisposition[1];
        Assert.That(
            ingress.TryAdmitBatch(
                in seat,
                new NetworkFixedInputBatchHeader(99, SchemaId, PayloadBytes, 1, 1),
                one,
                payload,
                oneDisposition),
            Is.EqualTo(FixedInputBatchAdmissionStatus.Rejected));
        Assert.That(oneDisposition[0], Is.EqualTo(FixedInputAdmissionDisposition.EpochMismatch));

        Assert.That(
            ingress.TryAdmitBatch(
                in seat,
                new NetworkFixedInputBatchHeader(1, 99, PayloadBytes, 1, 1),
                one,
                payload,
                oneDisposition),
            Is.EqualTo(FixedInputBatchAdmissionStatus.Rejected));
        Assert.That(oneDisposition[0], Is.EqualTo(FixedInputAdmissionDisposition.SchemaMismatch));

        Assert.That(
            ingress.TryAdmitBatch(
                in seat,
                new NetworkFixedInputBatchHeader(1, SchemaId, 8, 1, 1),
                one,
                payload.Slice(0, 8),
                oneDisposition),
            Is.EqualTo(FixedInputBatchAdmissionStatus.Rejected));
        Assert.That(oneDisposition[0], Is.EqualTo(FixedInputAdmissionDisposition.PayloadMismatch));

        // Invalid frame order rejects without writing earlier frames in the same batch.
        Span<uint> unordered = stackalloc uint[2] { 4, 3 };
        Span<byte> unorderedPayloads = stackalloc byte[PayloadBytes * 2];
        Span<FixedInputAdmissionDisposition> unorderedDispositions = stackalloc FixedInputAdmissionDisposition[2];
        Assert.That(
            ingress.TryAdmitBatch(
                in seat,
                new NetworkFixedInputBatchHeader(1, SchemaId, PayloadBytes, 1, 2),
                unordered,
                unorderedPayloads,
                unorderedDispositions),
            Is.EqualTo(FixedInputBatchAdmissionStatus.Rejected));
        Assert.That(unorderedDispositions[0], Is.EqualTo(FixedInputAdmissionDisposition.InvalidFrameOrder));
        Span<byte> stillMissing = stackalloc byte[PayloadBytes];
        Assert.That(ingress.TryGet(in seat, 4, stillMissing, out _), Is.EqualTo(FixedInputLookupResult.Missing));
    }

    [Test]
    public void Admit_AfterCommit_SafelyReusesRingCellForModuloEquivalentTick()
    {
        const int history = 8;
        var ticks = new AuthoritativeSimulationTickState();
        var ingress = new AuthoritativeFixedInputIngress(CreateConfig(history: history, maxFuture: history), ticks);
        SessionSeatBinding seat = Seat();
        ingress.BindSeat(in seat);

        Assert.That(AdmitOne(ingress, in seat, 1, 1, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(AdmitOne(ingress, in seat, 2, 2, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));

        ticks.Begin(1);
        ticks.Commit(1);

        uint reused = 1u + (uint)history; // same ring cell as tick 1
        Assert.That(AdmitOne(ingress, in seat, reused, 9, out FixedInputAdmissionDisposition accepted), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(accepted, Is.EqualTo(FixedInputAdmissionDisposition.Accepted));
        Assert.That(ingress.RingWrapCount, Is.EqualTo(0));

        Span<byte> destination = stackalloc byte[PayloadBytes];
        Assert.That(ingress.TryGet(in seat, reused, destination, out _), Is.EqualTo(FixedInputLookupResult.Present));
        Assert.That(destination[0], Is.EqualTo(9));
        Assert.That(ingress.TryGet(in seat, 2, destination, out _), Is.EqualTo(FixedInputLookupResult.Present));
        Assert.That(destination[0], Is.EqualTo(2));
    }

    [Test]
    public void ReleaseSeat_ClearsHistory_AndRequiresNewerGeneration()
    {
        var ticks = new AuthoritativeSimulationTickState();
        var ingress = new AuthoritativeFixedInputIngress(CreateConfig(), ticks);
        SessionSeatBinding seat = Seat();
        ingress.BindSeat(in seat);
        Assert.That(AdmitOne(ingress, in seat, 1, 7, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(ingress.TryReleaseSeat(in seat), Is.True);

        Span<byte> destination = stackalloc byte[PayloadBytes];
        Assert.That(ingress.TryGet(in seat, 1, destination, out _), Is.EqualTo(FixedInputLookupResult.InvalidSeat));

        Assert.Throws<InvalidOperationException>(() => ingress.BindSeat(in seat));
        SessionSeatBinding next = Seat(generation: 2);
        Assert.DoesNotThrow(() => ingress.BindSeat(in next));
    }

    [Test]
    public void BuildAcknowledgement_ExposesMaskWithBit0AsLatestReceived()
    {
        var ticks = new AuthoritativeSimulationTickState();
        ticks.RestoreCommittedTick(10);
        var ingress = new AuthoritativeFixedInputIngress(CreateConfig(maxFuture: 8), ticks);
        SessionSeatBinding seat = Seat();
        ingress.BindSeat(in seat);

        Assert.That(AdmitOne(ingress, in seat, 12, 1, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(AdmitOne(ingress, in seat, 13, 2, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));

        NetworkFixedInputAcknowledgement ack = ingress.BuildAcknowledgement(in seat);
        Assert.That(ack.CommittedThroughTick, Is.EqualTo(10u));
        Assert.That(ack.LatestReceivedTick, Is.EqualTo(13u));
        Assert.That(ack.ReceivedMask & 1UL, Is.EqualTo(1UL)); // tick 13
        Assert.That((ack.ReceivedMask >> 1) & 1UL, Is.EqualTo(1UL)); // tick 12
        Assert.That(ack.LatestMissingInputTick, Is.EqualTo(0u));
    }

    [Test]
    public void DefaultConfig_FailsImmediatelyAtConstruction()
    {
        var ticks = new AuthoritativeSimulationTickState();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AuthoritativeFixedInputIngress(default, ticks));
    }

    [Test]
    public void Admit_RejectsTickZeroAndOverIntMax_Atomically_AndAcceptsIntMax()
    {
        var ticks = new AuthoritativeSimulationTickState();
        ticks.RestoreCommittedTick(int.MaxValue - 2);
        var ingress = new AuthoritativeFixedInputIngress(CreateConfig(history: 8, maxFuture: 8), ticks);
        SessionSeatBinding seat = Seat();
        ingress.BindSeat(in seat);

        Assert.That(
            AdmitOne(ingress, in seat, 0, 1, out FixedInputAdmissionDisposition zero),
            Is.EqualTo(FixedInputBatchAdmissionStatus.Rejected));
        Assert.That(zero, Is.EqualTo(FixedInputAdmissionDisposition.TickOutOfRange));

        Assert.That(
            AdmitOne(ingress, in seat, (uint)int.MaxValue + 1u, 2, out FixedInputAdmissionDisposition over),
            Is.EqualTo(FixedInputBatchAdmissionStatus.Rejected));
        Assert.That(over, Is.EqualTo(FixedInputAdmissionDisposition.TickOutOfRange));

        Span<uint> mix = stackalloc uint[2] { (uint)int.MaxValue - 1, (uint)int.MaxValue + 1u };
        Span<byte> payloads = stackalloc byte[PayloadBytes * 2];
        payloads.Clear();
        payloads[0] = 9;
        payloads[PayloadBytes] = 8;
        Span<FixedInputAdmissionDisposition> dispositions = stackalloc FixedInputAdmissionDisposition[2];
        var header = new NetworkFixedInputBatchHeader(1, SchemaId, PayloadBytes, (uint)(int.MaxValue - 2), 2);
        Assert.That(
            ingress.TryAdmitBatch(in seat, in header, mix, payloads, dispositions),
            Is.EqualTo(FixedInputBatchAdmissionStatus.Rejected));
        Assert.That(dispositions[0], Is.EqualTo(FixedInputAdmissionDisposition.TickOutOfRange));
        Assert.That(dispositions[1], Is.EqualTo(FixedInputAdmissionDisposition.TickOutOfRange));

        Span<byte> missing = stackalloc byte[PayloadBytes];
        Assert.That(
            ingress.TryGet(in seat, (uint)int.MaxValue - 1, missing, out _),
            Is.EqualTo(FixedInputLookupResult.Missing));

        Assert.That(
            AdmitOne(ingress, in seat, (uint)int.MaxValue - 1, 3, out FixedInputAdmissionDisposition accepted),
            Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(accepted, Is.EqualTo(FixedInputAdmissionDisposition.Accepted));
        Assert.That(
            AdmitOne(ingress, in seat, (uint)int.MaxValue, 4, out FixedInputAdmissionDisposition maxAccepted),
            Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(maxAccepted, Is.EqualTo(FixedInputAdmissionDisposition.Accepted));

        Span<byte> destination = stackalloc byte[PayloadBytes];
        Assert.That(
            ingress.TryGet(in seat, (uint)int.MaxValue, destination, out _),
            Is.EqualTo(FixedInputLookupResult.Present));
        Assert.That(destination[0], Is.EqualTo(4));
    }

    [Test]
    public void Capacity150x64x12_BindsWritesAndReadsAllSeatsWithDistinctInput()
    {
        const int seatCapacity = 150;
        const int history = 64;
        var ticks = new AuthoritativeSimulationTickState();
        var config = new FixedInputProtocolConfig(
            seatCapacity,
            history,
            SchemaId,
            PayloadBytes,
            maxFutureTicks: 8,
            maxFramesPerBatch: 8,
            maxDatagramPayloadBytes: 1200,
            sessionEpoch: 1);
        var ingress = new AuthoritativeFixedInputIngress(config, ticks);

        Span<byte> destination = stackalloc byte[PayloadBytes];
        for (int slot = 0; slot < seatCapacity; slot++)
        {
            var seat = new SessionSeatBinding(slot, 1, new PlayerId(slot + 1));
            ingress.BindSeat(in seat);
            byte marker = (byte)((slot * 17) & 0xFF);
            Assert.That(
                AdmitOne(ingress, in seat, 1, marker, out FixedInputAdmissionDisposition disposition),
                Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
            Assert.That(disposition, Is.EqualTo(FixedInputAdmissionDisposition.Accepted));
        }

        ticks.Begin(1);
        for (int slot = 0; slot < seatCapacity; slot++)
        {
            var seat = new SessionSeatBinding(slot, 1, new PlayerId(slot + 1));
            byte expected = (byte)((slot * 17) & 0xFF);
            Assert.That(
                ingress.TryGet(in seat, 1, destination, out int written),
                Is.EqualTo(FixedInputLookupResult.Present));
            Assert.That(written, Is.EqualTo(PayloadBytes));
            Assert.That(destination[0], Is.EqualTo(expected), $"Seat {slot} payload mismatch.");
        }
    }
}
