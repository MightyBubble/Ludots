using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Simulation;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class FixedInputAllocationTests
{
    private const ushort SchemaId = 11;
    private const ushort PayloadBytes = 12;
    private const int HistoryTicks = 64;
    private const int MaxFutureTicks = 16;

    [Test]
    public void SteadyState_AdmitBuildAckOutbox_10000Operations_AllocatesZeroManagedBytes()
    {
        var tickState = new AuthoritativeSimulationTickState();
        var config = new FixedInputProtocolConfig(
            seatCapacity: 4,
            historyTicksPerSeat: HistoryTicks,
            schemaId: SchemaId,
            framePayloadBytes: PayloadBytes,
            maxFutureTicks: MaxFutureTicks,
            maxFramesPerBatch: 8,
            maxDatagramPayloadBytes: 1200,
            sessionEpoch: 1);
        var ingress = new AuthoritativeFixedInputIngress(config, tickState);
        var seat = new SessionSeatBinding(0, 1, new PlayerId(1));
        ingress.BindSeat(in seat);
        var outbox = new FixedInputClientOutbox(config, pendingFrameCapacity: 64);

        var encodeTicks = new uint[8];
        var encodePayloads = new byte[PayloadBytes * 8];
        var decodeTicks = new uint[8];
        var decodePayloads = new byte[PayloadBytes * 8];
        var outboxTicks = new uint[8];
        var outboxPayloads = new byte[PayloadBytes * 8];
        var dispositions = new FixedInputAdmissionDisposition[8];
        var batchBuffer = new byte[FixedInputWireCodec.GetBatchPayloadSize(PayloadBytes, 8)];
        var outboxBatchBuffer = new byte[FixedInputWireCodec.GetBatchPayloadSize(PayloadBytes, 8)];
        var ackBuffer = new byte[NetworkFixedInputAcknowledgement.SizeInBytes];
        var framed = new byte[NetworkWireEnvelope.SizeInBytes + batchBuffer.Length];
        var lookup = new byte[PayloadBytes];
        var enqueuePayload = new byte[PayloadBytes];

        int acceptedWrites = 0;
        int ringReuses = 0;
        int outboxBuilds = 0;

        // Warmup / JIT — exercise the same progression path as the measured loop.
        for (int i = 0; i < 128; i++)
        {
            RunOnce(
                ingress,
                outbox,
                in seat,
                tickState,
                encodeTicks,
                encodePayloads,
                decodeTicks,
                decodePayloads,
                outboxTicks,
                outboxPayloads,
                dispositions,
                batchBuffer,
                outboxBatchBuffer,
                ackBuffer,
                framed,
                lookup,
                enqueuePayload,
                stepIndex: i,
                ref acceptedWrites,
                ref ringReuses,
                ref outboxBuilds,
                trackEvidence: true);
        }

        Assert.That(acceptedWrites, Is.GreaterThan(0), "Warmup must exercise successful accepted writes.");
        Assert.That(ringReuses, Is.GreaterThan(0), "Warmup must exercise safe committed ring reuse.");
        Assert.That(outboxBuilds, Is.GreaterThan(0), "Warmup must exercise non-empty outbox batch construction/encode.");

        acceptedWrites = 0;
        ringReuses = 0;
        outboxBuilds = 0;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            RunOnce(
                ingress,
                outbox,
                in seat,
                tickState,
                encodeTicks,
                encodePayloads,
                decodeTicks,
                decodePayloads,
                outboxTicks,
                outboxPayloads,
                dispositions,
                batchBuffer,
                outboxBatchBuffer,
                ackBuffer,
                framed,
                lookup,
                enqueuePayload,
                stepIndex: 128 + i,
                ref acceptedWrites,
                ref ringReuses,
                ref outboxBuilds,
                trackEvidence: true);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0), $"Expected 0 B managed allocation over 10,000 steady-state fixed-input ops; observed {allocated} B.");
        Assert.That(acceptedWrites, Is.EqualTo(10_000), "Measured loop must perform successful accepted writes each iteration.");
        Assert.That(ringReuses, Is.GreaterThan(0), "Measured loop must perform safe committed ring reuse.");
        Assert.That(outboxBuilds, Is.EqualTo(10_000), "Measured loop must build and encode a non-empty outbox batch each iteration.");
    }

    private static void RunOnce(
        AuthoritativeFixedInputIngress ingress,
        FixedInputClientOutbox outbox,
        in SessionSeatBinding seat,
        AuthoritativeSimulationTickState tickState,
        uint[] encodeTicks,
        byte[] encodePayloads,
        uint[] decodeTicks,
        byte[] decodePayloads,
        uint[] outboxTicks,
        byte[] outboxPayloads,
        FixedInputAdmissionDisposition[] dispositions,
        byte[] batchBuffer,
        byte[] outboxBatchBuffer,
        byte[] ackBuffer,
        byte[] framed,
        byte[] lookup,
        byte[] enqueuePayload,
        int stepIndex,
        ref int acceptedWrites,
        ref int ringReuses,
        ref int outboxBuilds,
        bool trackEvidence)
    {
        // Advance the authoritative timeline so committed cells become safely reusable.
        int nextBegin = tickState.CommittedTick + 1;
        if (!tickState.IsExecuting)
        {
            tickState.Begin(nextBegin);
            tickState.Commit(nextBegin);
        }

        uint targetTick = (uint)(tickState.CommittedTick + 1);
        encodeTicks[0] = targetTick;
        encodePayloads.AsSpan().Clear();
        encodePayloads[0] = (byte)(stepIndex & 0xFF);

        enqueuePayload.AsSpan().Clear();
        enqueuePayload[0] = encodePayloads[0];
        uint outboxTick = (uint)(stepIndex + 1);
        if (outbox.TryEnqueue(outboxTick, enqueuePayload) != FixedInputOutboxEnqueueStatus.Enqueued)
        {
            FailStatus("outbox-enqueue", (int)outboxTick);
        }

        FixedInputBatchBuildStatus buildStatus = outbox.TryBuildBatch(
            (uint)tickState.CommittedTick,
            outboxTicks,
            outboxPayloads,
            out NetworkFixedInputBatchHeader outboxHeader,
            out int outboxFrameCount);
        if (buildStatus != FixedInputBatchBuildStatus.Built || outboxFrameCount <= 0 || outboxHeader.FrameCount == 0)
        {
            FailStatus("outbox-build", (int)buildStatus);
        }

        RequireSuccess(FixedInputWireCodec.TryEncodeBatch(
            in outboxHeader,
            outboxTicks.AsSpan(0, outboxFrameCount),
            outboxPayloads.AsSpan(0, outboxFrameCount * PayloadBytes),
            outboxBatchBuffer,
            out int outboxWritten));
        if (outboxWritten <= 0)
        {
            FailStatus("outbox-encode-empty", outboxWritten);
        }

        if (trackEvidence)
        {
            outboxBuilds++;
        }

        var drainAck = new NetworkFixedInputAcknowledgement(
            1,
            SchemaId,
            outboxTick,
            outboxTick,
            1UL,
            0);
        FixedInputAckApplyStatus ackStatus = outbox.TryApplyAcknowledgement(in drainAck);
        if (ackStatus != FixedInputAckApplyStatus.Applied)
        {
            FailStatus("outbox ack", (int)ackStatus);
        }

        var header = new NetworkFixedInputBatchHeader(
            1,
            SchemaId,
            PayloadBytes,
            (uint)tickState.CommittedTick,
            1);
        RequireSuccess(FixedInputWireCodec.TryEncodeBatch(
            in header,
            encodeTicks.AsSpan(0, 1),
            encodePayloads.AsSpan(0, PayloadBytes),
            batchBuffer,
            out int written));
        RequireSuccess(FixedInputWireCodec.TryDecodeBatch(
            batchBuffer.AsSpan(0, written),
            decodeTicks,
            decodePayloads,
            out NetworkFixedInputBatchHeader decoded,
            out int frameCount));

        FixedInputBatchAdmissionStatus admitStatus = ingress.TryAdmitBatch(
            in seat,
            in decoded,
            decodeTicks.AsSpan(0, frameCount),
            decodePayloads.AsSpan(0, frameCount * PayloadBytes),
            dispositions.AsSpan(0, frameCount));
        if (admitStatus != FixedInputBatchAdmissionStatus.Success
            || dispositions[0] is not (FixedInputAdmissionDisposition.Accepted or FixedInputAdmissionDisposition.AcceptedOutOfOrder))
        {
            FailStatus("admit", (int)admitStatus * 100 + (int)dispositions[0]);
        }

        if (trackEvidence)
        {
            acceptedWrites++;
            // Past one full history depth, each admit reuses a modulo ring cell whose prior tick is committed.
            if (targetTick > (uint)HistoryTicks)
            {
                ringReuses++;
            }
        }

        FixedInputLookupResult lookupResult = ingress.TryGet(in seat, targetTick, lookup, out int bytesWritten);
        if (lookupResult != FixedInputLookupResult.Present || bytesWritten != PayloadBytes || lookup[0] != encodePayloads[0])
        {
            FailStatus("lookup", (int)lookupResult);
        }

        NetworkFixedInputAcknowledgement ack = ingress.BuildAcknowledgement(in seat);
        if (ack.LatestReceivedTick == 0 || (ack.ReceivedMask & 1UL) == 0UL)
        {
            FailStatus("ack-mask", 0);
        }

        RequireSuccess(FixedInputWireCodec.TryEncodeAcknowledgement(in ack, ackBuffer, out int ackBytes));
        RequireSuccess(FixedInputWireCodec.TryDecodeAcknowledgement(ackBuffer.AsSpan(0, ackBytes), out _));

        RequireSuccess(NetworkWireEnvelopeCodec.TryEncode(
            NetworkWireKind.FixedInputBatch,
            batchBuffer.AsSpan(0, written),
            framed,
            out int framedBytes));
        RequireSuccess(NetworkWireEnvelopeCodec.TryDecode(framed.AsSpan(0, framedBytes), out _, out _));
    }

    private static void RequireSuccess(NetworkWireCodecStatus status)
    {
        if (status != NetworkWireCodecStatus.Success)
        {
            FailStatus("codec", (int)status);
        }
    }

    private static void FailStatus(string label, int code)
    {
        // Allocation-free on the success path; throw only on failure (outside steady-state evidence).
        throw new InvalidOperationException($"Fixed-input allocation harness failed at {label}:{code}.");
    }
}
