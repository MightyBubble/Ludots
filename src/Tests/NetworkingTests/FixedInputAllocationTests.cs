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

    [Test]
    public void SteadyState_AdmitBuildAckOutbox_10000Operations_AllocatesZeroManagedBytes()
    {
        var tickState = new AuthoritativeSimulationTickState();
        tickState.RestoreCommittedTick(100);
        var config = new FixedInputProtocolConfig(
            seatCapacity: 4,
            historyTicksPerSeat: 64,
            schemaId: SchemaId,
            framePayloadBytes: PayloadBytes,
            maxFutureTicks: 16,
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
        var dispositions = new FixedInputAdmissionDisposition[8];
        var batchBuffer = new byte[FixedInputWireCodec.GetBatchPayloadSize(PayloadBytes, 8)];
        var ackBuffer = new byte[NetworkFixedInputAcknowledgement.SizeInBytes];
        var framed = new byte[NetworkWireEnvelope.SizeInBytes + batchBuffer.Length];
        var lookup = new byte[PayloadBytes];
        var enqueuePayload = new byte[PayloadBytes];

        // Warmup / JIT.
        for (int i = 0; i < 64; i++)
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
                dispositions,
                batchBuffer,
                ackBuffer,
                framed,
                lookup,
                enqueuePayload,
                warmupIndex: i);
        }

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
                dispositions,
                batchBuffer,
                ackBuffer,
                framed,
                lookup,
                enqueuePayload,
                warmupIndex: 64 + i);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0), $"Expected 0 B managed allocation over 10,000 steady-state fixed-input ops; observed {allocated} B.");
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
        FixedInputAdmissionDisposition[] dispositions,
        byte[] batchBuffer,
        byte[] ackBuffer,
        byte[] framed,
        byte[] lookup,
        byte[] enqueuePayload,
        int warmupIndex)
    {
        uint baseTick = (uint)(tickState.CommittedTick + 1 + (warmupIndex % 8));
        encodeTicks[0] = baseTick;
        encodePayloads.AsSpan().Clear();
        encodePayloads[0] = (byte)(warmupIndex & 0xFF);

        enqueuePayload.AsSpan().Clear();
        enqueuePayload[0] = encodePayloads[0];
        uint outboxTick = (uint)(warmupIndex + 1);
        if (outbox.TryEnqueue(outboxTick, enqueuePayload) == FixedInputOutboxEnqueueStatus.Enqueued)
        {
            var drainAck = new NetworkFixedInputAcknowledgement(
                1,
                SchemaId,
                outboxTick,
                outboxTick,
                1UL,
                0);
            _ = outbox.TryApplyAcknowledgement(in drainAck);
        }

        var header = new NetworkFixedInputBatchHeader(
            1,
            SchemaId,
            PayloadBytes,
            (uint)tickState.CommittedTick,
            1);
        AssertSuccess(FixedInputWireCodec.TryEncodeBatch(
            in header,
            encodeTicks.AsSpan(0, 1),
            encodePayloads.AsSpan(0, PayloadBytes),
            batchBuffer,
            out int written));
        AssertSuccess(FixedInputWireCodec.TryDecodeBatch(
            batchBuffer.AsSpan(0, written),
            decodeTicks,
            decodePayloads,
            out NetworkFixedInputBatchHeader decoded,
            out int frameCount));
        _ = ingress.TryAdmitBatch(
            in seat,
            in decoded,
            decodeTicks.AsSpan(0, frameCount),
            decodePayloads.AsSpan(0, frameCount * PayloadBytes),
            dispositions.AsSpan(0, frameCount));
        _ = ingress.TryGet(in seat, baseTick, lookup, out _);

        NetworkFixedInputAcknowledgement ack = ingress.BuildAcknowledgement(in seat);
        AssertSuccess(FixedInputWireCodec.TryEncodeAcknowledgement(in ack, ackBuffer, out int ackBytes));
        AssertSuccess(FixedInputWireCodec.TryDecodeAcknowledgement(ackBuffer.AsSpan(0, ackBytes), out _));
        _ = outbox.TryBuildBatch((uint)tickState.CommittedTick, encodeTicks, encodePayloads, out _, out _);

        AssertSuccess(NetworkWireEnvelopeCodec.TryEncode(
            NetworkWireKind.FixedInputBatch,
            batchBuffer.AsSpan(0, written),
            framed,
            out int framedBytes));
        AssertSuccess(NetworkWireEnvelopeCodec.TryDecode(framed.AsSpan(0, framedBytes), out _, out _));
    }

    private static void AssertSuccess(NetworkWireCodecStatus status)
    {
        if (status != NetworkWireCodecStatus.Success)
        {
            throw new InvalidOperationException($"Expected Success, got {status}.");
        }
    }
}
