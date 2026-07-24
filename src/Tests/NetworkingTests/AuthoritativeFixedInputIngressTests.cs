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

        Assert.That(AdmitOne(ingress, in seat, 11, 9, out FixedInputAdmissionDisposition conflict), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        Assert.That(conflict, Is.EqualTo(FixedInputAdmissionDisposition.Conflict));

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
    public void Admit_AllOrNothing_OnRingWrap_AndInvalidSeatEpochSchemaPayload()
    {
        var ticks = new AuthoritativeSimulationTickState();
        ticks.RestoreCommittedTick(1);
        var ingress = new AuthoritativeFixedInputIngress(CreateConfig(history: 4, maxFuture: 8), ticks);
        SessionSeatBinding seat = Seat();
        ingress.BindSeat(in seat);

        Assert.That(AdmitOne(ingress, in seat, 2, 1, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        // Same ring cell as tick 2 when history=4: tick 6 maps to index 2.
        Span<uint> wrapTicks = stackalloc uint[2] { 3, 6 };
        Span<byte> wrapPayloads = stackalloc byte[PayloadBytes * 2];
        wrapPayloads.Clear();
        Span<FixedInputAdmissionDisposition> wrapDispositions = stackalloc FixedInputAdmissionDisposition[2];
        var wrapHeader = new NetworkFixedInputBatchHeader(1, SchemaId, PayloadBytes, 1, 2);
        Assert.That(
            ingress.TryAdmitBatch(in seat, in wrapHeader, wrapTicks, wrapPayloads, wrapDispositions),
            Is.EqualTo(FixedInputBatchAdmissionStatus.Rejected));
        Assert.That(wrapDispositions[0], Is.EqualTo(FixedInputAdmissionDisposition.RingWrap));
        Span<byte> stillMissing = stackalloc byte[PayloadBytes];
        Assert.That(ingress.TryGet(in seat, 3, stillMissing, out _), Is.EqualTo(FixedInputLookupResult.Missing));

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
        Assert.That(ack.LatestMissingInputTick, Is.EqualTo(11u));
    }

    [Test]
    public void Supports150SeatsWithoutWaitingForFullPopulation()
    {
        var ticks = new AuthoritativeSimulationTickState();
        FixedInputProtocolConfig config = FixedInputProtocolConfig.CreatePhysics3DDefaultFloor(
            SchemaId,
            sessionEpoch: 1,
            maxFutureTicks: 8,
            maxFramesPerBatch: 8,
            maxDatagramPayloadBytes: 1200);
        var ingress = new AuthoritativeFixedInputIngress(config, ticks);
        SessionSeatBinding only = Seat();
        ingress.BindSeat(in only);
        Assert.That(AdmitOne(ingress, in only, 1, 1, out _), Is.EqualTo(FixedInputBatchAdmissionStatus.Success));
        ticks.Begin(1);
        Span<byte> destination = stackalloc byte[PayloadBytes];
        Assert.That(ingress.TryGet(in only, 1, destination, out _), Is.EqualTo(FixedInputLookupResult.Present));
    }
}
