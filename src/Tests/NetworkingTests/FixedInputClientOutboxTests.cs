using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class FixedInputClientOutboxTests
{
    private const ushort SchemaId = 5;
    private const ushort PayloadBytes = 12;

    private static FixedInputProtocolConfig CreateConfig(ulong epoch = 1) =>
        new(
            seatCapacity: 2,
            historyTicksPerSeat: 8,
            schemaId: SchemaId,
            framePayloadBytes: PayloadBytes,
            maxFutureTicks: 8,
            maxFramesPerBatch: 4,
            maxDatagramPayloadBytes: 1200,
            sessionEpoch: epoch);

    private static void Fill(Span<byte> payload, byte marker)
    {
        payload.Clear();
        payload[0] = marker;
    }

    [Test]
    public void Outbox_BuildsStrictlyOrderedRedundantBatches_AndStopsResendingReceivedFrames()
    {
        var outbox = new FixedInputClientOutbox(CreateConfig(), pendingFrameCapacity: 16);
        Span<byte> payload = stackalloc byte[PayloadBytes];
        for (uint tick = 1; tick <= 4; tick++)
        {
            Fill(payload, (byte)tick);
            Assert.That(outbox.TryEnqueue(tick, payload), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));
        }

        Span<uint> ticks = stackalloc uint[4];
        Span<byte> payloads = stackalloc byte[PayloadBytes * 4];
        Assert.That(
            outbox.TryBuildBatch(0, ticks, payloads, out NetworkFixedInputBatchHeader header, out int count),
            Is.EqualTo(FixedInputBatchBuildStatus.Built));
        Assert.That(count, Is.EqualTo(4));
        Assert.That(header.FrameCount, Is.EqualTo(4));
        Assert.That(ticks[0], Is.EqualTo(1u));
        Assert.That(ticks[3], Is.EqualTo(4u));

        var ack = new NetworkFixedInputAcknowledgement(
            sessionEpoch: 1,
            schemaId: SchemaId,
            committedThroughTick: 1,
            latestReceivedTick: 3,
            receivedMask: 0b111UL, // 3,2,1 present
            latestMissingInputTick: 0);
        Assert.That(outbox.TryApplyAcknowledgement(in ack), Is.EqualTo(FixedInputAckApplyStatus.Applied));
        Assert.That(outbox.PendingCount, Is.EqualTo(3)); // tick 1 removed; 2,3 received kept; 4 needs send

        Assert.That(
            outbox.TryBuildBatch(1, ticks, payloads, out _, out int remaining),
            Is.EqualTo(FixedInputBatchBuildStatus.Built));
        Assert.That(remaining, Is.EqualTo(1));
        Assert.That(ticks[0], Is.EqualTo(4u));
    }

    [Test]
    public void Outbox_WhenRedundancyBatchIsFull_IncludesNewestFrameAndRetainsOldestFrames()
    {
        var outbox = new FixedInputClientOutbox(CreateConfig(), pendingFrameCapacity: 8);
        Span<byte> payload = stackalloc byte[PayloadBytes];
        for (uint tick = 1; tick <= 5; tick++)
        {
            Fill(payload, (byte)tick);
            Assert.That(outbox.TryEnqueue(tick, payload), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));
        }

        Span<uint> ticks = stackalloc uint[4];
        Span<byte> payloads = stackalloc byte[PayloadBytes * 4];
        Assert.That(
            outbox.TryBuildBatch(0, ticks, payloads, out _, out int count),
            Is.EqualTo(FixedInputBatchBuildStatus.Built));

        Assert.That(count, Is.EqualTo(4));
        Assert.That(ticks.ToArray(), Is.EqualTo(new uint[] { 1, 2, 3, 5 }));
        Assert.That(payloads[0], Is.EqualTo(1));
        Assert.That(payloads[PayloadBytes], Is.EqualTo(2));
        Assert.That(payloads[PayloadBytes * 2], Is.EqualTo(3));
        Assert.That(payloads[PayloadBytes * 3], Is.EqualTo(5));
    }

    [Test]
    public void Outbox_NoPendingInput_ReturnsNoData_NotSuccessfulEmptyBatch()
    {
        var outbox = new FixedInputClientOutbox(CreateConfig(), pendingFrameCapacity: 8);
        Span<uint> ticks = stackalloc uint[4];
        Span<byte> payloads = stackalloc byte[PayloadBytes * 4];

        Assert.That(
            outbox.TryBuildBatch(0, ticks, payloads, out NetworkFixedInputBatchHeader header, out int count),
            Is.EqualTo(FixedInputBatchBuildStatus.NoData));
        Assert.That(count, Is.EqualTo(0));
        Assert.That(header.FrameCount, Is.EqualTo(0));
        Assert.That(header.SessionEpoch, Is.EqualTo(0UL));
    }

    [Test]
    public void Outbox_RejectsAckRegression_AndEpochSchemaMismatch()
    {
        var outbox = new FixedInputClientOutbox(CreateConfig(), pendingFrameCapacity: 8);
        Span<byte> payload = stackalloc byte[PayloadBytes];
        Fill(payload, 1);
        Assert.That(outbox.TryEnqueue(1, payload), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));

        var first = new NetworkFixedInputAcknowledgement(1, SchemaId, 0, 1, 1UL, 0);
        Assert.That(outbox.TryApplyAcknowledgement(in first), Is.EqualTo(FixedInputAckApplyStatus.Applied));

        var regressCommitted = new NetworkFixedInputAcknowledgement(1, SchemaId, 0, 0, 0UL, 0);
        Assert.That(outbox.TryApplyAcknowledgement(in regressCommitted), Is.EqualTo(FixedInputAckApplyStatus.RejectedRegression));

        var epoch = new NetworkFixedInputAcknowledgement(99, SchemaId, 0, 1, 1UL, 0);
        Assert.That(outbox.TryApplyAcknowledgement(in epoch), Is.EqualTo(FixedInputAckApplyStatus.EpochMismatch));

        var schema = new NetworkFixedInputAcknowledgement(1, 99, 0, 1, 1UL, 0);
        Assert.That(outbox.TryApplyAcknowledgement(in schema), Is.EqualTo(FixedInputAckApplyStatus.SchemaMismatch));
    }

    [Test]
    public void Outbox_RejectsLatestReceivedRegressionEvenWhenCommittedAdvances_WithoutMutation()
    {
        var outbox = new FixedInputClientOutbox(CreateConfig(), pendingFrameCapacity: 8);
        Span<byte> payload = stackalloc byte[PayloadBytes];
        Fill(payload, 1);
        Assert.That(outbox.TryEnqueue(1, payload), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));
        Fill(payload, 2);
        Assert.That(outbox.TryEnqueue(2, payload), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));
        Fill(payload, 3);
        Assert.That(outbox.TryEnqueue(3, payload), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));

        var first = new NetworkFixedInputAcknowledgement(1, SchemaId, 0, 2, 0b11UL, 0);
        Assert.That(outbox.TryApplyAcknowledgement(in first), Is.EqualTo(FixedInputAckApplyStatus.Applied));
        Assert.That(outbox.PendingCount, Is.EqualTo(3));
        Assert.That(outbox.AppliedLatestReceived, Is.EqualTo(2u));
        Assert.That(outbox.AppliedCommittedThrough, Is.EqualTo(0u));

        // Committed advances, but LatestReceived regresses — must reject and leave outbox untouched.
        var regressLatest = new NetworkFixedInputAcknowledgement(1, SchemaId, 1, 1, 1UL, 0);
        Assert.That(outbox.TryApplyAcknowledgement(in regressLatest), Is.EqualTo(FixedInputAckApplyStatus.RejectedRegression));
        Assert.That(outbox.PendingCount, Is.EqualTo(3));
        Assert.That(outbox.AppliedLatestReceived, Is.EqualTo(2u));
        Assert.That(outbox.AppliedCommittedThrough, Is.EqualTo(0u));
    }

    [Test]
    public void Outbox_RejectsAckWhenLatestReceivedNonZeroButMaskBit0Clear_WithoutMutation()
    {
        var outbox = new FixedInputClientOutbox(CreateConfig(), pendingFrameCapacity: 8);
        Span<byte> payload = stackalloc byte[PayloadBytes];
        Fill(payload, 1);
        Assert.That(outbox.TryEnqueue(1, payload), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));

        var invalid = new NetworkFixedInputAcknowledgement(1, SchemaId, 0, 1, 0UL, 0);
        Assert.That(outbox.TryApplyAcknowledgement(in invalid), Is.EqualTo(FixedInputAckApplyStatus.InvalidInput));
        Assert.That(outbox.PendingCount, Is.EqualTo(1));
        Assert.That(outbox.AppliedLatestReceived, Is.EqualTo(0u));
        Assert.That(outbox.AppliedCommittedThrough, Is.EqualTo(0u));
    }

    [Test]
    public void Outbox_RejectsAckWhenLatestReceivedZeroButMaskNonZero_WithoutMutation()
    {
        var outbox = new FixedInputClientOutbox(CreateConfig(), pendingFrameCapacity: 8);
        Span<byte> payload = stackalloc byte[PayloadBytes];
        Fill(payload, 1);
        Assert.That(outbox.TryEnqueue(1, payload), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));

        var invalid = new NetworkFixedInputAcknowledgement(1, SchemaId, 0, 0, 1UL, 0);
        Assert.That(outbox.TryApplyAcknowledgement(in invalid), Is.EqualTo(FixedInputAckApplyStatus.InvalidInput));
        Assert.That(outbox.PendingCount, Is.EqualTo(1));
    }

    [Test]
    public void Outbox_EnqueueRejectsNonIncreasingAndCapacityExceeded()
    {
        var outbox = new FixedInputClientOutbox(CreateConfig(), pendingFrameCapacity: 2);
        Span<byte> payload = stackalloc byte[PayloadBytes];
        Fill(payload, 1);
        Assert.That(outbox.TryEnqueue(2, payload), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));
        Assert.That(outbox.TryEnqueue(2, payload), Is.EqualTo(FixedInputOutboxEnqueueStatus.TickNotIncreasing));
        Assert.That(outbox.TryEnqueue(3, payload), Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));
        Assert.That(outbox.TryEnqueue(4, payload), Is.EqualTo(FixedInputOutboxEnqueueStatus.CapacityExceeded));
        Assert.That(outbox.TryEnqueue(5, stackalloc byte[3]), Is.EqualTo(FixedInputOutboxEnqueueStatus.PayloadMismatch));
        Assert.That(outbox.TryEnqueue(0, payload), Is.EqualTo(FixedInputOutboxEnqueueStatus.InvalidInput));
        Assert.That(
            outbox.TryEnqueue(unchecked((uint)int.MaxValue) + 1u, payload),
            Is.EqualTo(FixedInputOutboxEnqueueStatus.InvalidInput));
    }
}
