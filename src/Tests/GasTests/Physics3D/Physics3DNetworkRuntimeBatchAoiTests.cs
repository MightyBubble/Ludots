using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Knowledge;
using Ludots.Core.Layers;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Simulation;
using Ludots.Core.Networking.Transport;
using Ludots.Core.Physics3D;
using Ludots.Core.Physics3DNet.Bridge;
using Ludots.Core.Physics3DNet.Input;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

/// <summary>
/// Authoritative Physics3D full-publish gate: production AOI -> knowledge -> projection ->
/// encode -> fragment -> TrySend chain under 150 seats, with atomic capacity and determinism checks.
/// </summary>
[TestFixture]
public sealed class Physics3DNetworkRuntimeBatchAoiTests
{
    private static class GateConfig
    {
        public const int SeatCount = 150;
        public const int GlobalEntityCapacity = 100_000;
        public const int ReplicationEntityCapacityPerSeat = 512;
        public const int FixedStepHz = 30;
        public const int WarmupFrames = 16;
        public const int MeasuredFrames = 128;
        public const int ScaleQueryWorkerCount = 4;
        public const int DeterminismParallelWorkerCount = 3;
        public const int DeterminismSingleWorkerCount = 1;
        public const int ClusterColumns = 15;
        public const float ClusterSpacingCm = 10_000f;
        public const float InterestRadiusCm = 1_000f;
        public const int SchemaId = 41;
        public const ulong SessionEpochValue = 99;
        public const int MaxDatagramPayloadBytes = 1_200;
        public const int AcknowledgementHistoryCapacity = 8;
        public const int BaselineCapacity = 8;
        public const int FixedInputHistoryTicksPerSeat = 8;
        public const ushort FixedInputSchemaId = 7;
        public const int FixedInputMaxFutureTicks = 4;
        public const int FixedInputLeadTicks = 2;
        public const int FixedInputMaxFramesPerBatch = 1;
        public const int OrdinaryBodyCount10K = 10_000;
        public const int OrdinaryBodyCount25K = 25_000;
        public const int AtomicSeatCount = 2;
        public const float AtomicSpawnSpacingCm = 10_000f;
        public const float AtomicInterestRadiusCm = 100f;
        public const int AtomicReplicationCapacityPerSeat = 1;
        public const int AtomicKnowledgeReplicationCapacityPerSeat = 2;
        public const int WireKindCardinality = 12;
        public const int FirstConnectionValue = 1;
        public const int HandshakeInboundCapacityPerConnection = 2;
        public const int AckInboundCapacityPerConnection = 4;
        public const int InboundFrameCapacityMultiplier = HandshakeInboundCapacityPerConnection + AckInboundCapacityPerConnection;
        public const int SendRecordCapacityPerSeat = 2_048;
        public const int KnowledgeSnapshotCapacity = 16;
        public const int TestOrderTypeId = 1;
    }

    [Test]
    [NonParallelizable]
    public void FullPublish_150SeatsAnd10KRegisteredBodies_Meets30HzBudgetAndZeroAllocation()
        => RunFullPublishScale(
            ordinaryBodyCount: GateConfig.OrdinaryBodyCount10K,
            queryWorkerCount: GateConfig.ScaleQueryWorkerCount,
            measuredFrames: GateConfig.MeasuredFrames,
            assertBudget: true);

    [Test]
    [Explicit("150-seat and 25K registered-body full-publish pressure measurement.")]
    [NonParallelizable]
    public void FullPublish_150SeatsAnd25KRegisteredBodies_Meets30HzBudgetAndZeroAllocation()
        => RunFullPublishScale(
            ordinaryBodyCount: GateConfig.OrdinaryBodyCount25K,
            queryWorkerCount: GateConfig.ScaleQueryWorkerCount,
            measuredFrames: GateConfig.MeasuredFrames,
            assertBudget: true);

    [Test]
    [NonParallelizable]
    public void FullPublish_LateSeatAoiCapacityFailure_LeavesKnowledgeAndTransportUnchanged()
    {
        using FullPublishHarness harness = FullPublishHarness.Create(
            seatCount: GateConfig.AtomicSeatCount,
            ordinaryBodyCount: 0,
            queryWorkerCount: GateConfig.DeterminismSingleWorkerCount,
            replicationEntityCapacityPerSeat: GateConfig.AtomicReplicationCapacityPerSeat,
            knowledgeCapacity: GateConfig.AtomicSeatCount * GateConfig.AtomicSeatCount,
            interestRadiusCm: GateConfig.AtomicInterestRadiusCm,
            spawnSpacingCm: GateConfig.AtomicSpawnSpacingCm,
            clusterColumns: GateConfig.AtomicSeatCount);

        harness.EstablishAllSeatsThroughProductionHandshake();
        harness.Transport.ClearCounters();

        uint tick = checked((uint)(harness.TickState.CommittedTick + 1));
        harness.RunAuthoritativeFrame(tick);
        harness.AcknowledgePublishedSeats(tick);
        harness.Server.PumpTransport();
        Assert.That(harness.Knowledge.RecordCount, Is.EqualTo(GateConfig.AtomicSeatCount));

        CaptureKnowledgeSnapshot(
            harness.Knowledge,
            harness.ViewerEntities,
            harness.KnowledgeSnapshotScratch,
            out int snapshotCountBefore);
        int recordCountBefore = harness.Knowledge.RecordCount;

        // Late body only near seat 1 tips that seat over per-seat replication capacity.
        Vector3 lateSeatPosition = new(GateConfig.AtomicSpawnSpacingCm, 0f, 0f);
        AddLateOrdinaryBodyOnIdleTick(harness, position: lateSeatPosition + new Vector3(5f, 0f, 0f));
        harness.Transport.ClearCounters();
        tick = checked((uint)(harness.TickState.CommittedTick + 1));

        NetworkRuntimeException? fault = Assert.Throws<NetworkRuntimeException>(
            () => harness.RunAuthoritativeFrameWithPendingStructuralChanges(tick));
        Assert.Multiple(() =>
        {
            Assert.That(fault, Is.Not.Null);
            Assert.That(fault!.Fault.Code, Is.EqualTo(NetworkRuntimeFaultCode.ReplicationInputRejected));
            Assert.That(harness.Server.IsFaulted, Is.True);
            Assert.That(harness.Server.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.ReplicationInputRejected));
            Assert.That(harness.Interest.LastFailure, Is.EqualTo(Physics3DNetworkAoiFailure.PerSeatCapacityExceeded));
            Assert.That(harness.Interest.FailedSeatSlot, Is.EqualTo(GateConfig.AtomicSeatCount - 1));
            Assert.That(harness.Knowledge.RecordCount, Is.EqualTo(recordCountBefore));
            AssertKnowledgeSnapshotUnchanged(
                harness.Knowledge,
                harness.ViewerEntities,
                harness.KnowledgeSnapshotScratch,
                snapshotCountBefore);
            AssertZeroTransportSendsOfEveryWireKind(harness.Transport);
        });
    }

    [Test]
    [NonParallelizable]
    public void FullPublish_KnowledgeCapacityFailure_LeavesKnowledgeAndTransportUnchanged()
    {
        int replicationCapacity = GateConfig.AtomicKnowledgeReplicationCapacityPerSeat;
        int knowledgeCapacity = GateConfig.AtomicSeatCount * replicationCapacity;
        using FullPublishHarness harness = FullPublishHarness.Create(
            seatCount: GateConfig.AtomicSeatCount,
            ordinaryBodyCount: 0,
            queryWorkerCount: GateConfig.DeterminismSingleWorkerCount,
            replicationEntityCapacityPerSeat: replicationCapacity,
            knowledgeCapacity: knowledgeCapacity,
            interestRadiusCm: GateConfig.AtomicInterestRadiusCm,
            spawnSpacingCm: GateConfig.AtomicSpawnSpacingCm,
            clusterColumns: GateConfig.AtomicSeatCount);

        harness.EstablishAllSeatsThroughProductionHandshake();
        PrefillUnrelatedKnowledge(harness);
        harness.Transport.ClearCounters();

        uint tick = checked((uint)(harness.TickState.CommittedTick + 1));
        harness.RunAuthoritativeFrame(tick);
        harness.AcknowledgePublishedSeats(tick);
        harness.Server.PumpTransport();
        Assert.That(harness.Knowledge.RecordCount, Is.EqualTo(knowledgeCapacity));

        CaptureKnowledgeSnapshot(
            harness.Knowledge,
            harness.ViewerEntities,
            harness.KnowledgeSnapshotScratch,
            out int snapshotCountBefore);
        int recordCountBefore = harness.Knowledge.RecordCount;

        Vector3 lateSeatPosition = new(GateConfig.AtomicSpawnSpacingCm, 0f, 0f);
        AddLateOrdinaryBodyOnIdleTick(harness, position: lateSeatPosition + new Vector3(5f, 0f, 0f));
        harness.Transport.ClearCounters();
        tick = checked((uint)(harness.TickState.CommittedTick + 1));

        NetworkRuntimeException? fault = Assert.Throws<NetworkRuntimeException>(
            () => harness.RunAuthoritativeFrameWithPendingStructuralChanges(tick));
        Assert.Multiple(() =>
        {
            Assert.That(fault, Is.Not.Null);
            Assert.That(fault!.Fault.Code, Is.EqualTo(NetworkRuntimeFaultCode.ReplicationInputRejected));
            Assert.That(harness.Server.IsFaulted, Is.True);
            Assert.That(harness.Server.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.ReplicationInputRejected));
            Assert.That(harness.Interest.LastFailure, Is.EqualTo(Physics3DNetworkAoiFailure.KnowledgeCapacityExceeded));
            Assert.That(harness.Knowledge.RecordCount, Is.EqualTo(recordCountBefore));
            AssertKnowledgeSnapshotUnchanged(
                harness.Knowledge,
                harness.ViewerEntities,
                harness.KnowledgeSnapshotScratch,
                snapshotCountBefore);
            AssertZeroTransportSendsOfEveryWireKind(harness.Transport);
        });
    }

    [Test]
    [NonParallelizable]
    public void FullPublish_LateSeatProjectionFailure_LeavesKnowledgeAndTransportUnchanged()
    {
        using FullPublishHarness harness = FullPublishHarness.Create(
            seatCount: GateConfig.AtomicSeatCount,
            ordinaryBodyCount: 0,
            queryWorkerCount: GateConfig.DeterminismSingleWorkerCount,
            replicationEntityCapacityPerSeat: GateConfig.AtomicReplicationCapacityPerSeat,
            knowledgeCapacity: GateConfig.AtomicSeatCount * GateConfig.AtomicSeatCount,
            interestRadiusCm: GateConfig.AtomicInterestRadiusCm,
            spawnSpacingCm: GateConfig.AtomicSpawnSpacingCm,
            clusterColumns: GateConfig.AtomicSeatCount);

        harness.EstablishAllSeatsThroughProductionHandshake();
        Entity lateSeatViewer = harness.ViewerEntities[GateConfig.AtomicSeatCount - 1];
        harness.Ecs.Remove<Physics3DNetworkReplicatedBody>(lateSeatViewer);
        harness.Transport.ClearCounters();

        uint tick = checked((uint)(harness.TickState.CommittedTick + 1));
        NetworkRuntimeException? fault = Assert.Throws<NetworkRuntimeException>(
            () => harness.RunAuthoritativeFrame(tick));

        Assert.Multiple(() =>
        {
            Assert.That(fault, Is.Not.Null);
            Assert.That(fault!.Fault.Code, Is.EqualTo(NetworkRuntimeFaultCode.ReplicationBuildRejected));
            Assert.That(harness.Server.IsFaulted, Is.True);
            Assert.That(harness.Server.LastFault.Code, Is.EqualTo(NetworkRuntimeFaultCode.ReplicationBuildRejected));
            Assert.That(harness.Knowledge.RecordCount, Is.Zero);
            AssertZeroTransportSendsOfEveryWireKind(harness.Transport);
        });
    }

    [Test]
    [NonParallelizable]
    public void FullPublish_LateSeatOutboundCapacityFailure_LeavesKnowledgeAndTransportUnchanged()
    {
        using FullPublishHarness harness = FullPublishHarness.Create(
            seatCount: GateConfig.AtomicSeatCount,
            ordinaryBodyCount: 0,
            queryWorkerCount: GateConfig.DeterminismSingleWorkerCount,
            replicationEntityCapacityPerSeat: GateConfig.AtomicReplicationCapacityPerSeat,
            knowledgeCapacity: GateConfig.AtomicSeatCount * GateConfig.AtomicSeatCount,
            interestRadiusCm: GateConfig.AtomicInterestRadiusCm,
            spawnSpacingCm: GateConfig.AtomicSpawnSpacingCm,
            clusterColumns: GateConfig.AtomicSeatCount,
            outboundQueueCapacity: 1);

        harness.EstablishAllSeatsThroughProductionHandshake();
        harness.Transport.ClearCounters();

        uint tick = checked((uint)(harness.TickState.CommittedTick + 1));
        NetworkRuntimeException? fault = Assert.Throws<NetworkRuntimeException>(
            () => harness.RunAuthoritativeFrame(tick));

        Assert.Multiple(() =>
        {
            Assert.That(fault, Is.Not.Null);
            Assert.That(fault!.Fault.Code, Is.EqualTo(NetworkRuntimeFaultCode.OutboundQueueCapacityExceeded));
            Assert.That(harness.Server.IsFaulted, Is.True);
            Assert.That(harness.Knowledge.RecordCount, Is.Zero);
            AssertZeroTransportSendsOfEveryWireKind(harness.Transport);
        });
    }

    [Test]
    [NonParallelizable]
    public void Delta_BaselineUnavailable_SendsOnlyResyncThenFullWithoutSkippingSnapshotId()
    {
        using FullPublishHarness harness = FullPublishHarness.Create(
            seatCount: 1,
            ordinaryBodyCount: 0,
            queryWorkerCount: GateConfig.DeterminismSingleWorkerCount,
            replicationEntityCapacityPerSeat: GateConfig.AtomicReplicationCapacityPerSeat,
            knowledgeCapacity: GateConfig.AtomicReplicationCapacityPerSeat,
            interestRadiusCm: GateConfig.AtomicInterestRadiusCm,
            spawnSpacingCm: GateConfig.AtomicSpawnSpacingCm,
            clusterColumns: 1);

        harness.EstablishAllSeatsThroughProductionHandshake();
        harness.Transport.ClearCounters();

        uint tick = checked((uint)(harness.TickState.CommittedTick + 1));
        harness.RunAuthoritativeFrame(tick);
        harness.AcknowledgePublishedSeats(tick);
        harness.Server.PumpTransport();

        for (int index = 0; index < GateConfig.BaselineCapacity; index++)
        {
            tick = checked((uint)(harness.TickState.CommittedTick + 1));
            harness.RunAuthoritativeFrame(tick);
        }

        Assert.That(harness.Transport.TryGetLastReplicationSnapshotId(0, out ulong beforeResync), Is.True);
        harness.Transport.ClearCounters();
        tick = checked((uint)(harness.TickState.CommittedTick + 1));
        harness.RunAuthoritativeFrame(tick);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Transport.GetSendCount(NetworkWireKind.ResyncRequired), Is.EqualTo(1));
            Assert.That(harness.Transport.GetSendCount(NetworkWireKind.ReplicationPacket), Is.Zero);
            Assert.That(harness.Transport.GetSendCount(NetworkWireKind.SnapshotFragment), Is.Zero);
            Assert.That(harness.Transport.TryGetLastReplicationSnapshotId(0, out ulong duringResync), Is.True);
            Assert.That(duringResync, Is.EqualTo(beforeResync));
        });

        harness.Transport.ClearCounters();
        tick = checked((uint)(harness.TickState.CommittedTick + 1));
        harness.RunAuthoritativeFrame(tick);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Transport.GetSendCount(NetworkWireKind.SnapshotFragment), Is.GreaterThan(0));
            Assert.That(harness.Transport.GetSendCount(NetworkWireKind.ResyncRequired), Is.Zero);
            Assert.That(harness.Transport.TryGetLastReplicationSnapshotId(0, out ulong recovered), Is.True);
            Assert.That(recovered, Is.EqualTo(beforeResync + 1));
        });
    }

    [Test]
    [NonParallelizable]
    public void FullPublish_1Vs3QueryWorkers_YieldsIdenticalPerSeatWireDigestsAndOrder()
    {
        const int ordinaryBodyCount = 300;
        const int measuredFrames = 8;
        const int warmupFrames = 4;

        ulong[] digests1;
        int[] counts1;
        ulong[] digests3;
        int[] counts3;

        using (FullPublishHarness harness1 = FullPublishHarness.Create(
                   seatCount: GateConfig.SeatCount,
                   ordinaryBodyCount: ordinaryBodyCount,
                   queryWorkerCount: GateConfig.DeterminismSingleWorkerCount,
                   replicationEntityCapacityPerSeat: GateConfig.ReplicationEntityCapacityPerSeat,
                   knowledgeCapacity: GateConfig.SeatCount * GateConfig.ReplicationEntityCapacityPerSeat,
                   interestRadiusCm: GateConfig.InterestRadiusCm,
                   spawnSpacingCm: GateConfig.ClusterSpacingCm,
                   clusterColumns: GateConfig.ClusterColumns))
        {
            RunDeterminismCapture(harness1, warmupFrames, measuredFrames, out digests1, out counts1);
        }

        using (FullPublishHarness harness3 = FullPublishHarness.Create(
                   seatCount: GateConfig.SeatCount,
                   ordinaryBodyCount: ordinaryBodyCount,
                   queryWorkerCount: GateConfig.DeterminismParallelWorkerCount,
                   replicationEntityCapacityPerSeat: GateConfig.ReplicationEntityCapacityPerSeat,
                   knowledgeCapacity: GateConfig.SeatCount * GateConfig.ReplicationEntityCapacityPerSeat,
                   interestRadiusCm: GateConfig.InterestRadiusCm,
                   spawnSpacingCm: GateConfig.ClusterSpacingCm,
                   clusterColumns: GateConfig.ClusterColumns))
        {
            RunDeterminismCapture(harness3, warmupFrames, measuredFrames, out digests3, out counts3);
        }

        Assert.Multiple(() =>
        {
            for (int seat = 0; seat < GateConfig.SeatCount; seat++)
            {
                Assert.That(
                    counts3[seat],
                    Is.EqualTo(counts1[seat]),
                    $"Seat {seat} send count diverged between 1 and 3 query workers.");
                int offset = seat * GateConfig.SendRecordCapacityPerSeat;
                for (int index = 0; index < counts1[seat]; index++)
                {
                    Assert.That(
                        digests3[offset + index],
                        Is.EqualTo(digests1[offset + index]),
                        $"Seat {seat} digest[{index}] diverged between 1 and 3 query workers.");
                }
            }
        });
    }

    private static void RunFullPublishScale(
        int ordinaryBodyCount,
        int queryWorkerCount,
        int measuredFrames,
        bool assertBudget)
    {
        if (ordinaryBodyCount <= 0 || ordinaryBodyCount > GateConfig.GlobalEntityCapacity - GateConfig.SeatCount)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinaryBodyCount));
        }

        using FullPublishHarness harness = FullPublishHarness.Create(
            seatCount: GateConfig.SeatCount,
            ordinaryBodyCount: ordinaryBodyCount,
            queryWorkerCount: queryWorkerCount,
            replicationEntityCapacityPerSeat: GateConfig.ReplicationEntityCapacityPerSeat,
            knowledgeCapacity: GateConfig.SeatCount * GateConfig.ReplicationEntityCapacityPerSeat,
            interestRadiusCm: GateConfig.InterestRadiusCm,
            spawnSpacingCm: GateConfig.ClusterSpacingCm,
            clusterColumns: GateConfig.ClusterColumns);

        harness.EstablishAllSeatsThroughProductionHandshake();
        harness.Transport.ClearCounters();

        uint tick = checked((uint)(harness.TickState.CommittedTick + 1));
        for (int warmup = 0; warmup < GateConfig.WarmupFrames; warmup++)
        {
            harness.RunAuthoritativeFrame(tick);
            Assert.That(
                harness.Transport.PublishedSeatCountForCurrentFrame,
                Is.EqualTo(GateConfig.SeatCount),
                $"Warmup frame {warmup} did not publish all seats.");
            harness.AcknowledgePublishedSeats(tick);
            harness.Server.PumpTransport();
            harness.Transport.BeginFrame();
            tick++;
        }

        harness.Transport.ClearCounters();

        var samples = new long[measuredFrames];
        var allocationSamples = new long[measuredFrames];
        var interestPrepareSamples = new long[measuredFrames];
        var interestValidationSamples = new long[measuredFrames];
        var knowledgeCommitSamples = new long[measuredFrames];
        var projectionSamples = new long[measuredFrames];
        var channelBuildSamples = new long[measuredFrames];
        var packetEncodeSamples = new long[measuredFrames];
        var transportSendSamples = new long[measuredFrames];
        var acknowledgementAndFlushSamples = new long[measuredFrames];
        var unattributedSamples = new long[measuredFrames];
        var workerAllocatedBytes = new long[harness.Physics.WorkerCount];
        long maximumWorkerAllocatedBytes = 0;
        long allocatedBytes = 0;
        int failedPublishFrame = -1;
        int failedPublishSeatCount = -1;

        _ = Stopwatch.GetTimestamp();
        _ = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < measuredFrames; frame++)
        {
            harness.Transport.BeginFrame();
            harness.TickState.Begin(checked((int)tick));
            harness.Server.BeforeAuthoritativeTick(tick);
            harness.TickState.Commit(checked((int)tick));

            long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            harness.Server.AfterAuthoritativeCommit(tick);
            samples[frame] = Stopwatch.GetTimestamp() - started;
            allocationSamples[frame] = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
            allocatedBytes += allocationSamples[frame];

            AuthoritativeNetworkPublishMetrics metrics = harness.Server.LastPublishMetrics;
            interestPrepareSamples[frame] = metrics.InterestPrepareElapsedTimestampTicks;
            interestValidationSamples[frame] = metrics.InterestValidationElapsedTimestampTicks;
            knowledgeCommitSamples[frame] = metrics.KnowledgeCommitElapsedTimestampTicks;
            projectionSamples[frame] = metrics.ProjectionElapsedTimestampTicks;
            channelBuildSamples[frame] = metrics.ChannelBuildElapsedTimestampTicks;
            packetEncodeSamples[frame] = metrics.PacketEncodeElapsedTimestampTicks;
            transportSendSamples[frame] = metrics.TransportSendElapsedTimestampTicks;
            acknowledgementAndFlushSamples[frame] = metrics.AcknowledgementAndFlushElapsedTimestampTicks;
            unattributedSamples[frame] = metrics.UnattributedElapsedTimestampTicks;

            harness.Physics.CopyLastParallelQueryWorkerAllocatedBytes(workerAllocatedBytes);
            for (int worker = 0; worker < workerAllocatedBytes.Length; worker++)
            {
                maximumWorkerAllocatedBytes = Math.Max(maximumWorkerAllocatedBytes, workerAllocatedBytes[worker]);
            }

            int published = harness.Transport.PublishedSeatCountForCurrentFrame;
            if (published != GateConfig.SeatCount && failedPublishFrame < 0)
            {
                failedPublishFrame = frame;
                failedPublishSeatCount = published;
            }

            // ACK + pump are outside measured allocation/latency accounting.
            harness.AcknowledgePublishedSeats(tick);
            harness.Server.PumpTransport();
            tick++;
        }

        samples.AsSpan().Sort();
        interestPrepareSamples.AsSpan().Sort();
        interestValidationSamples.AsSpan().Sort();
        knowledgeCommitSamples.AsSpan().Sort();
        projectionSamples.AsSpan().Sort();
        channelBuildSamples.AsSpan().Sort();
        packetEncodeSamples.AsSpan().Sort();
        transportSendSamples.AsSpan().Sort();
        acknowledgementAndFlushSamples.AsSpan().Sort();
        unattributedSamples.AsSpan().Sort();
        double millisecondsPerTimestamp = 1_000d / Stopwatch.Frequency;
        double p95 = Percentile(samples, 0.95) * millisecondsPerTimestamp;
        double p99 = Percentile(samples, 0.99) * millisecondsPerTimestamp;
        double budgetMilliseconds = 1_000d / GateConfig.FixedStepHz;
        int allocatedFrameCount = 0;
        int firstAllocatedFrame = -1;
        long maximumFrameAllocation = 0;
        int maximumFrameAllocationIndex = -1;
        for (int frame = 0; frame < allocationSamples.Length; frame++)
        {
            if (allocationSamples[frame] == 0)
            {
                continue;
            }

            allocatedFrameCount++;
            if (firstAllocatedFrame < 0)
            {
                firstAllocatedFrame = frame;
            }

            if (allocationSamples[frame] > maximumFrameAllocation)
            {
                maximumFrameAllocation = allocationSamples[frame];
                maximumFrameAllocationIndex = frame;
            }
        }

        TestContext.Out.WriteLine(
            $"Physics3D full-publish chain: {GateConfig.SeatCount} seats + {ordinaryBodyCount:N0} registered bodies, " +
            $"{measuredFrames} measured frames, workers={queryWorkerCount}, " +
            $"P95={p95:F3}ms, P99={p99:F3}ms, budget={budgetMilliseconds:F3}ms, " +
            $"calling-thread allocations={allocatedBytes}B, " +
            $"maximum physics fixed-query worker allocation={maximumWorkerAllocatedBytes}B, " +
            $"sends snapshotFragment={harness.Transport.GetSendCount(NetworkWireKind.SnapshotFragment)}, " +
            $"replicationPacket={harness.Transport.GetSendCount(NetworkWireKind.ReplicationPacket)}, " +
            $"fixedInputAck={harness.Transport.GetSendCount(NetworkWireKind.FixedInputAcknowledgement)}.");
        TestContext.Out.WriteLine(
            $"Publish stages P95: interest prepare/validate/knowledge=" +
            $"{Percentile(interestPrepareSamples, 0.95) * millisecondsPerTimestamp:F3}/" +
            $"{Percentile(interestValidationSamples, 0.95) * millisecondsPerTimestamp:F3}/" +
            $"{Percentile(knowledgeCommitSamples, 0.95) * millisecondsPerTimestamp:F3}ms, " +
            $"projection/channel/encode/send/ack-flush=" +
            $"{Percentile(projectionSamples, 0.95) * millisecondsPerTimestamp:F3}/" +
            $"{Percentile(channelBuildSamples, 0.95) * millisecondsPerTimestamp:F3}/" +
            $"{Percentile(packetEncodeSamples, 0.95) * millisecondsPerTimestamp:F3}/" +
            $"{Percentile(transportSendSamples, 0.95) * millisecondsPerTimestamp:F3}/" +
            $"{Percentile(acknowledgementAndFlushSamples, 0.95) * millisecondsPerTimestamp:F3}ms, " +
            $"unattributed={Percentile(unattributedSamples, 0.95) * millisecondsPerTimestamp:F3}ms; " +
            $"allocated frames={allocatedFrameCount}/{measuredFrames}, first allocated frame={firstAllocatedFrame}, " +
            $"maximum frame allocation={maximumFrameAllocation}B at frame {maximumFrameAllocationIndex}.");

        Assert.Multiple(() =>
        {
            Assert.That(
                failedPublishFrame,
                Is.EqualTo(-1),
                $"Frame {failedPublishFrame} published {failedPublishSeatCount}/{GateConfig.SeatCount} seats.");
            Assert.That(harness.Registry.Count, Is.EqualTo(ordinaryBodyCount));
            Assert.That(harness.Server.IsFaulted, Is.False);
            if (assertBudget)
            {
                Assert.That(
                    p95,
                    Is.LessThanOrEqualTo(budgetMilliseconds),
                    $"Full-publish P95 exceeded the {GateConfig.FixedStepHz}Hz budget.");
                Assert.That(
                    p99,
                    Is.LessThanOrEqualTo(budgetMilliseconds),
                    $"Full-publish P99 exceeded the {GateConfig.FixedStepHz}Hz budget.");
            }

            Assert.That(
                allocatedBytes,
                Is.Zero,
                $"Full-publish calling thread allocated {allocatedBytes}B after warmup.");
            Assert.That(
                maximumWorkerAllocatedBytes,
                Is.Zero,
                $"Full-publish physics fixed-query worker allocated {maximumWorkerAllocatedBytes}B after warmup.");
        });
    }

    private static void RunDeterminismCapture(
        FullPublishHarness harness,
        int warmupFrames,
        int measuredFrames,
        out ulong[] digests,
        out int[] counts)
    {
        harness.EstablishAllSeatsThroughProductionHandshake();
        harness.Transport.ClearCounters();
        harness.Transport.EnableDigestCapture();

        uint tick = checked((uint)(harness.TickState.CommittedTick + 1));
        for (int warmup = 0; warmup < warmupFrames; warmup++)
        {
            harness.RunAuthoritativeFrame(tick);
            harness.AcknowledgePublishedSeats(tick);
            harness.Server.PumpTransport();
            harness.Transport.BeginFrame();
            tick++;
        }

        harness.Transport.ClearDigestCapture();
        for (int frame = 0; frame < measuredFrames; frame++)
        {
            harness.Transport.BeginFrame();
            harness.RunAuthoritativeFrame(tick);
            harness.AcknowledgePublishedSeats(tick);
            harness.Server.PumpTransport();
            tick++;
        }

        digests = harness.Transport.CopyDigestCapture(out counts);
    }

    private static void AddLateOrdinaryBodyOnIdleTick(FullPublishHarness harness, Vector3 position)
    {
        Physics3DShapeId bodyShape = harness.Physics.RegisterBoxShape(new Vector3(10f));
        Physics3DMaterial bodyMaterial = CreateBodyConfig().Material;
        var pose = new Physics3DPoseCm
        {
            Position = position,
            Orientation = Quaternion.Identity,
        };
        var schema = new ReplicationSchemaRef(GateConfig.SchemaId);
        Entity entity = harness.Ecs.Create(in pose, in schema);
        Physics3DBodyId bodyId = harness.Physics.CreateBody(new Physics3DBodyDescription(
            entity,
            Physics3DBodyKind.Static,
            bodyShape,
            position,
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            mass: 0f,
            LayerMask.All,
            bodyMaterial,
            Physics3DContinuousDetectionMode.Discrete));
        var body = new Physics3DBodyCm
        {
            Id = bodyId,
            Kind = Physics3DBodyKind.Static,
        };
        harness.Ecs.Add(entity, body);

        if (!harness.Registry.TryQueueEligibleBodies(out int queuedCount) || queuedCount != 1)
        {
            throw new InvalidOperationException(
                $"Late body queue failed: failure={harness.Registry.LastFailure}, queued={queuedCount}.");
        }
    }

    private static void PrefillUnrelatedKnowledge(FullPublishHarness harness)
    {
        for (int viewerIndex = 0; viewerIndex < harness.ViewerEntities.Length; viewerIndex++)
        {
            Entity viewer = harness.ViewerEntities[viewerIndex];
            Entity target = harness.Ecs.Create();
            var disclosure = new KnowledgeDisclosureRecord(
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                default,
                default,
                default,
                viewer,
                observedTick: 0,
                expiryTick: 0,
                confidencePermille: 1_000,
                revision: 1);
            harness.Knowledge.Upsert(viewer, target, in disclosure);
        }
    }

    private static void CaptureKnowledgeSnapshot(
        KnowledgeProjectionStore knowledge,
        ReadOnlySpan<Entity> viewers,
        KnowledgeSnapshotEntry[] destination,
        out int count)
    {
        count = 0;
        Span<Entity> targets = stackalloc Entity[GateConfig.KnowledgeSnapshotCapacity];
        Span<KnowledgeDisclosureRecord> records = stackalloc KnowledgeDisclosureRecord[GateConfig.KnowledgeSnapshotCapacity];
        for (int viewerIndex = 0; viewerIndex < viewers.Length; viewerIndex++)
        {
            Entity viewer = viewers[viewerIndex];
            int copied = knowledge.CopyRecords(viewer, currentTick: 0, targets, records);
            for (int index = 0; index < copied; index++)
            {
                if (count >= destination.Length)
                {
                    throw new InvalidOperationException("Knowledge snapshot capacity exceeded.");
                }

                destination[count++] = new KnowledgeSnapshotEntry(
                    viewer,
                    targets[index],
                    records[index].Revision,
                    records[index].Presence,
                    records[index].Position,
                    records[index].ObservedTick,
                    records[index].ExpiryTick,
                    records[index].ConfidencePermille,
                    records[index].Source);
            }
        }
    }

    private static void AssertKnowledgeSnapshotUnchanged(
        KnowledgeProjectionStore knowledge,
        ReadOnlySpan<Entity> viewers,
        KnowledgeSnapshotEntry[] before,
        int beforeCount)
    {
        var after = new KnowledgeSnapshotEntry[before.Length];
        CaptureKnowledgeSnapshot(knowledge, viewers, after, out int afterCount);
        Assert.That(afterCount, Is.EqualTo(beforeCount));
        for (int index = 0; index < beforeCount; index++)
        {
            Assert.That(after[index], Is.EqualTo(before[index]));
        }
    }

    private static void AssertZeroTransportSendsOfEveryWireKind(FixedCapacityMultiConnectionTransport transport)
    {
        for (int kind = 0; kind < GateConfig.WireKindCardinality; kind++)
        {
            Assert.That(
                transport.GetSendCount((NetworkWireKind)kind),
                Is.Zero,
                $"Expected zero sends for wire kind {(NetworkWireKind)kind}.");
        }
    }

    private static double Percentile(long[] sortedValues, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sortedValues.Length) - 1;
        return sortedValues[Math.Clamp(index, 0, sortedValues.Length - 1)];
    }

    private static Physics3DNetworkPlayerBodyConfig CreateBodyConfig() => new()
    {
        RadiusCm = 30f,
        CylinderLengthCm = 100f,
        Mass = 80f,
        CollisionLayer = LayerMask.All,
        Material = new Physics3DMaterial(
            frictionCoefficient: 0.8f,
            maximumRecoveryVelocityCmPerSecond: 200f,
            springAngularFrequency: 30f,
            springTwiceDampingRatio: 1f),
        ContinuousDetection = Physics3DContinuousDetectionMode.Passive,
    };

    private readonly struct KnowledgeSnapshotEntry : IEquatable<KnowledgeSnapshotEntry>
    {
        public KnowledgeSnapshotEntry(
            Entity viewer,
            Entity target,
            uint revision,
            KnowledgePresence presence,
            KnowledgePositionAccess position,
            int observedTick,
            int expiryTick,
            int confidencePermille,
            Entity source)
        {
            Viewer = viewer;
            Target = target;
            Revision = revision;
            Presence = presence;
            Position = position;
            ObservedTick = observedTick;
            ExpiryTick = expiryTick;
            ConfidencePermille = confidencePermille;
            Source = source;
        }

        public Entity Viewer { get; }
        public Entity Target { get; }
        public uint Revision { get; }
        public KnowledgePresence Presence { get; }
        public KnowledgePositionAccess Position { get; }
        public int ObservedTick { get; }
        public int ExpiryTick { get; }
        public int ConfidencePermille { get; }
        public Entity Source { get; }

        public bool Equals(KnowledgeSnapshotEntry other) =>
            Viewer == other.Viewer &&
            Target == other.Target &&
            Revision == other.Revision &&
            Presence == other.Presence &&
            Position == other.Position &&
            ObservedTick == other.ObservedTick &&
            ExpiryTick == other.ExpiryTick &&
            ConfidencePermille == other.ConfidencePermille &&
            Source == other.Source;

        public override bool Equals(object? obj) => obj is KnowledgeSnapshotEntry other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Viewer, Target, Revision, Presence, Position);
    }

    private sealed class FullPublishHarness : IDisposable
    {
        private readonly Physics3DNetworkPlayerLifecycle _lifecycle;
        private readonly Physics3DAuthoritativeReplicationSeatRuntimeFactory _replicationFactory;
        private readonly Physics3DNetworkAoiInterestPort _interest;
        private readonly NetworkRuntimeObserverFanout _observer;
        private readonly byte[] _ackPayloadScratch;
        private bool _disposed;

        private FullPublishHarness(
            World ecs,
            Physics3DWorld physics,
            NetworkEntityTable entities,
            Physics3DNetworkReplicatedBindingStore bindings,
            KnowledgeProjectionStore knowledge,
            Physics3DNetworkPlayerLifecycle lifecycle,
            Physics3DNetworkBodyRegistry registry,
            Physics3DAuthoritativeReplicationSeatRuntimeFactory replicationFactory,
            Physics3DNetworkAoiInterestPort interest,
            AuthoritativeServerNetworkRuntime server,
            AuthoritativeSimulationTickState tickState,
            FixedCapacityMultiConnectionTransport transport,
            NetworkRuntimeCapacity capacity,
            NetworkRuntimeObserverFanout observer,
            Entity[] viewerEntities,
            KnowledgeSnapshotEntry[] knowledgeSnapshotScratch,
            ContentFingerprint fingerprint,
            ProtocolVersion protocol)
        {
            Ecs = ecs;
            Physics = physics;
            Entities = entities;
            Bindings = bindings;
            Knowledge = knowledge;
            _lifecycle = lifecycle;
            Registry = registry;
            _replicationFactory = replicationFactory;
            _interest = interest;
            Server = server;
            TickState = tickState;
            Transport = transport;
            Capacity = capacity;
            _observer = observer;
            ViewerEntities = viewerEntities;
            KnowledgeSnapshotScratch = knowledgeSnapshotScratch;
            Fingerprint = fingerprint;
            Protocol = protocol;
            _ackPayloadScratch = new byte[NetworkSnapshotAcknowledgement.SizeInBytes];
        }

        public World Ecs { get; }
        public Physics3DWorld Physics { get; }
        public NetworkEntityTable Entities { get; }
        public Physics3DNetworkReplicatedBindingStore Bindings { get; }
        public KnowledgeProjectionStore Knowledge { get; }
        public Physics3DNetworkBodyRegistry Registry { get; }
        public Physics3DNetworkAoiInterestPort Interest => _interest;
        public AuthoritativeServerNetworkRuntime Server { get; }
        public AuthoritativeSimulationTickState TickState { get; }
        public FixedCapacityMultiConnectionTransport Transport { get; }
        public NetworkRuntimeCapacity Capacity { get; }
        public Entity[] ViewerEntities { get; }
        public KnowledgeSnapshotEntry[] KnowledgeSnapshotScratch { get; }
        public ContentFingerprint Fingerprint { get; }
        public ProtocolVersion Protocol { get; }
        public SessionEpoch SessionEpoch => new(GateConfig.SessionEpochValue);

        public static FullPublishHarness Create(
            int seatCount,
            int ordinaryBodyCount,
            int queryWorkerCount,
            int replicationEntityCapacityPerSeat,
            int knowledgeCapacity,
            float interestRadiusCm,
            float spawnSpacingCm,
            int clusterColumns,
            int? outboundQueueCapacity = null)
        {
            World ecs = World.Create();
            var physics = new Physics3DWorld(CreateWorldConfig(
                mobileCapacity: seatCount,
                staticBodyCapacity: Math.Max(1, ordinaryBodyCount + 1),
                workerCount: queryWorkerCount));
            var entities = new NetworkEntityTable(capacity: GateConfig.GlobalEntityCapacity);
            var bindings = new Physics3DNetworkReplicatedBindingStore(physics.BodySlotCapacity, entities.Capacity);
            var knowledge = new KnowledgeProjectionStore(initialCapacity: knowledgeCapacity);
            var lifecycle = new Physics3DNetworkPlayerLifecycle(
                ecs,
                physics,
                entities,
                bindings,
                knowledge,
                seatCount,
                GateConfig.SchemaId,
                CreateBodyConfig(),
                new Physics3DNetworkPlayerSpawnConfig
                {
                    OriginCm = Vector3.Zero,
                    ColumnSpacingCm = spawnSpacingCm,
                    RowSpacingCm = spawnSpacingCm,
                    Columns = clusterColumns,
                });
            var tickState = new AuthoritativeSimulationTickState();
            var registry = new Physics3DNetworkBodyRegistry(
                ecs,
                physics,
                entities,
                bindings,
                tickState,
                GateConfig.SchemaId,
                commandCapacity: Math.Max(1, ordinaryBodyCount + 1));

            Physics3DShapeId bodyShape = physics.RegisterBoxShape(new Vector3(10f));
            Physics3DMaterial bodyMaterial = CreateBodyConfig().Material;
            for (int bodyIndex = 0; bodyIndex < ordinaryBodyCount; bodyIndex++)
            {
                int clusterSlot = bodyIndex % seatCount;
                Vector3 position = new(
                    (clusterSlot % clusterColumns) * spawnSpacingCm,
                    0f,
                    (clusterSlot / clusterColumns) * spawnSpacingCm);
                var pose = new Physics3DPoseCm
                {
                    Position = position,
                    Orientation = Quaternion.Identity,
                };
                var schema = new ReplicationSchemaRef(GateConfig.SchemaId);
                Entity entity = ecs.Create(in pose, in schema);
                Physics3DBodyId bodyId = physics.CreateBody(new Physics3DBodyDescription(
                    entity,
                    Physics3DBodyKind.Static,
                    bodyShape,
                    position,
                    Quaternion.Identity,
                    Vector3.Zero,
                    Vector3.Zero,
                    mass: 0f,
                    LayerMask.All,
                    bodyMaterial,
                    Physics3DContinuousDetectionMode.Discrete));
                var body = new Physics3DBodyCm
                {
                    Id = bodyId,
                    Kind = Physics3DBodyKind.Static,
                };
                ecs.Add(entity, body);
            }

            if (ordinaryBodyCount > 0)
            {
                if (!registry.TryQueueEligibleBodies(out int queuedCount) || queuedCount != ordinaryBodyCount)
                {
                    throw new InvalidOperationException(
                        $"Scale setup failed to queue {ordinaryBodyCount:N0} bodies: " +
                        $"failure={registry.LastFailure}, queued={queuedCount}.");
                }

                tickState.Begin(1);
                if (!registry.TryApplyPendingStructuralChanges())
                {
                    throw new InvalidOperationException(
                        $"Scale setup failed to register bodies: failure={registry.LastFailure}.");
                }

                tickState.Commit(1);
            }

            var projectors = CreateProjectors(physics);
            var replicationFactory = new Physics3DAuthoritativeReplicationSeatRuntimeFactory(
                ecs,
                entities,
                knowledge,
                projectors,
                seatCount,
                replicationEntityCapacityPerSeat,
                GateConfig.BaselineCapacity,
                disclosureChangeLogCapacity: checked(replicationEntityCapacityPerSeat * 2));
            var interest = new Physics3DNetworkAoiInterestPort(
                physics,
                entities,
                lifecycle,
                bindings,
                knowledge,
                replicationEntityCapacityPerSeat,
                new Physics3DNetworkAoiConfig
                {
                    RadiusCm = interestRadiusCm,
                    GlobalEntityCapacity = GateConfig.GlobalEntityCapacity,
                });

            NetworkRuntimeCapacity capacity = CreateCapacity(
                seatCount,
                replicationEntityCapacityPerSeat,
                outboundQueueCapacity);
            ContentFingerprint fingerprint = ContentFingerprintBuilder.FromCanonicalBytes("physics3d-full-publish-gate"u8);
            var protocol = new ProtocolVersion(1, 0);
            var transport = new FixedCapacityMultiConnectionTransport(
                connectionCapacity: seatCount,
                maxDatagramPayloadBytes: capacity.MaxDatagramPayloadBytes,
                inboundFrameCapacity: checked(seatCount * GateConfig.InboundFrameCapacityMultiplier),
                connectionEventCapacity: seatCount,
                sendRecordCapacityPerSeat: GateConfig.SendRecordCapacityPerSeat);
            var sessions = new AuthoritativeSessionRegistry(
                seatCount,
                new SessionEpoch(GateConfig.SessionEpochValue),
                protocol,
                fingerprint,
                reconnectWindowTicks: 8);
            var commandResults = new NetworkCommandAdmissionResultBuffer(capacity: Math.Max(8, seatCount));
            NetworkCommandIngress commandIngress = CreateCommandIngress(
                ecs,
                entities,
                knowledge,
                seatCount,
                commandResults);
            var fixedInput = new AuthoritativeFixedInputIngress(
                capacity.CreateFixedInputProtocolConfig(sessions.SessionEpoch.Value, sessions.SeatCapacity),
                tickState);
            var stateObserver = new NetworkRuntimeStateObserver(seatCount);
            var lifecycleObserver = new Physics3DNetworkPlayerLifecycleObserver(lifecycle);
            var observer = new NetworkRuntimeObserverFanout(stateObserver, lifecycleObserver);
            var server = new AuthoritativeServerNetworkRuntime(
                in capacity,
                NetworkTransportPortOwnership.Borrowed,
                transport,
                transport,
                transport,
                sessions,
                commandIngress,
                commandResults,
                lifecycle,
                interest,
                replicationFactory,
                fixedInput,
                observer);

            return new FullPublishHarness(
                ecs,
                physics,
                entities,
                bindings,
                knowledge,
                lifecycle,
                registry,
                replicationFactory,
                interest,
                server,
                tickState,
                transport,
                capacity,
                observer,
                new Entity[seatCount],
                new KnowledgeSnapshotEntry[GateConfig.KnowledgeSnapshotCapacity],
                fingerprint,
                protocol);
        }

        public void EstablishAllSeatsThroughProductionHandshake()
        {
            int seatCount = Transport.ConnectionCapacity;
            for (int index = 0; index < seatCount; index++)
            {
                Transport.EnqueueConnected(new ConnectionId(GateConfig.FirstConnectionValue + index));
            }

            Server.PumpTransport();

            Span<byte> requestPayload = stackalloc byte[HandshakeWireCodec.RequestSizeInBytes];
            var request = new SessionHandshakeRequest(Protocol, Fingerprint);
            Assert.That(
                HandshakeWireCodec.TryEncodeRequest(in request, requestPayload, out int requestBytes),
                Is.EqualTo(NetworkWireCodecStatus.Success));
            for (int index = 0; index < seatCount; index++)
            {
                Transport.EnqueueClientFrame(
                    new ConnectionId(GateConfig.FirstConnectionValue + index),
                    Capacity.ControlChannel,
                    NetworkWireKind.SessionHandshakeRequest,
                    requestPayload[..requestBytes]);
            }

            Server.PumpTransport();

            for (int slot = 0; slot < seatCount; slot++)
            {
                SessionSeatBinding seat = new(slot, generation: 1, new PlayerId(slot + 1));
                Assert.That(
                    _lifecycle.TryGetExistingController(in seat, out Entity controller),
                    Is.True,
                    $"Seat {slot} controller missing after production handshake.");
                ViewerEntities[slot] = controller;
            }

            Assert.That(_lifecycle.ConnectedPlayerCount, Is.EqualTo(seatCount));
        }

        public void RunAuthoritativeFrame(uint tick)
        {
            int expected = TickState.CommittedTick + 1;
            if ((int)tick != expected)
            {
                throw new InvalidOperationException(
                    $"Authoritative frame must advance tick {expected}; got {tick}.");
            }

            TickState.Begin(checked((int)tick));
            Server.BeforeAuthoritativeTick(tick);
            TickState.Commit(checked((int)tick));
            Server.AfterAuthoritativeCommit(tick);
        }

        public void RunAuthoritativeFrameWithPendingStructuralChanges(uint tick)
        {
            int expected = TickState.CommittedTick + 1;
            if ((int)tick != expected)
            {
                throw new InvalidOperationException(
                    $"Authoritative frame must advance tick {expected}; got {tick}.");
            }

            TickState.Begin(checked((int)tick));
            Server.BeforeAuthoritativeTick(tick);
            if (!Registry.TryApplyPendingStructuralChanges())
            {
                throw new InvalidOperationException(
                    $"Pending body registration failed: {Registry.LastFailure}.");
            }

            TickState.Commit(checked((int)tick));
            Server.AfterAuthoritativeCommit(tick);
        }

        public void AcknowledgePublishedSeats(uint committedTick)
        {
            int seatCount = Transport.ConnectionCapacity;
            for (int seat = 0; seat < seatCount; seat++)
            {
                if (!Transport.TryGetLastReplicationSnapshotId(seat, out ulong snapshotId) || snapshotId == 0)
                {
                    continue;
                }

                var acknowledgement = new NetworkSnapshotAcknowledgement(
                    GateConfig.SessionEpochValue,
                    snapshotId,
                    committedTick);
                Assert.That(
                    SnapshotControlWireCodec.TryEncodeAcknowledgement(
                        in acknowledgement,
                        _ackPayloadScratch,
                        out int payloadBytes),
                    Is.EqualTo(NetworkWireCodecStatus.Success));
                Transport.EnqueueClientFrame(
                    new ConnectionId(GateConfig.FirstConnectionValue + seat),
                    Capacity.ControlChannel,
                    NetworkWireKind.SnapshotAcknowledgement,
                    _ackPayloadScratch.AsSpan(0, payloadBytes));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Server.Dispose();
            _interest.Dispose();
            Registry.Dispose();
            _lifecycle.Dispose();
            Physics.Dispose();
            Ecs.Dispose();
            Transport.Dispose();
            _ = _replicationFactory;
            _ = _observer;
        }

        private static NetworkRuntimeCapacity CreateCapacity(
            int seatCount,
            int replicationEntityCapacityPerSeat,
            int? outboundQueueCapacity = null)
        {
            int maxSnapshotBytes = ReplicationPacketWireCodec.GetPayloadSize(
                replicationEntityCapacityPerSeat,
                replicationEntityCapacityPerSeat,
                checked(replicationEntityCapacityPerSeat * 2));
            int fragmentDataBytes = SnapshotFragmentWireCodec.GetMaxFragmentDataBytes(GateConfig.MaxDatagramPayloadBytes);
            int maxSnapshotFragments = checked((maxSnapshotBytes + fragmentDataBytes - 1) / fragmentDataBytes);
            int maxCommandEntries = 2;
            int maxCommandPayloadBytes = CommandBatchWireCodec.GetPayloadSize(maxCommandEntries);
            int commandFragmentDataBytes = CommandFragmentWireCodec.GetMaxFragmentDataBytes(GateConfig.MaxDatagramPayloadBytes);
            int maxCommandFragments = checked((maxCommandPayloadBytes + commandFragmentDataBytes - 1) / commandFragmentDataBytes);
            int resolvedOutboundQueueCapacity = outboundQueueCapacity ??
                checked((seatCount * maxSnapshotFragments) + seatCount);
            return new NetworkRuntimeCapacity(
                simulationTickRateHz: GateConfig.FixedStepHz,
                statePublishRateHz: GateConfig.FixedStepHz,
                maxDatagramPayloadBytes: GateConfig.MaxDatagramPayloadBytes,
                connectionCapacity: seatCount,
                globalEntityCapacity: GateConfig.GlobalEntityCapacity,
                replicationEntityCapacityPerSeat: replicationEntityCapacityPerSeat,
                maxCommandEntries: maxCommandEntries,
                maxCommandPayloadBytes: maxCommandPayloadBytes,
                maxCommandFragments: Math.Max(1, maxCommandFragments),
                maxSnapshotBytes: maxSnapshotBytes,
                maxSnapshotFragments: Math.Max(1, maxSnapshotFragments),
                outboundQueueCapacity: resolvedOutboundQueueCapacity,
                acknowledgementHistoryCapacity: GateConfig.AcknowledgementHistoryCapacity,
                controlChannel: new ChannelId(0),
                commandChannel: new ChannelId(1),
                stateChannel: new ChannelId(2),
                inputChannel: new ChannelId(3),
                fixedInputHistoryTicksPerSeat: GateConfig.FixedInputHistoryTicksPerSeat,
                fixedInputSchemaId: GateConfig.FixedInputSchemaId,
                fixedInputFramePayloadBytes: Physics3DFixedInputFrameCodec.PayloadBytes,
                fixedInputMaxFutureTicks: GateConfig.FixedInputMaxFutureTicks,
                fixedInputLeadTicks: GateConfig.FixedInputLeadTicks,
                fixedInputMaxFramesPerBatch: GateConfig.FixedInputMaxFramesPerBatch,
                fixedInputPendingFrameCapacity: GateConfig.FixedInputMaxFutureTicks);
        }

        private static NetworkCommandIngress CreateCommandIngress(
            World world,
            NetworkEntityTable entities,
            KnowledgeProjectionStore knowledge,
            int seatCapacity,
            NetworkCommandAdmissionResultBuffer results)
        {
            var relationshipTypes = new RelationshipTypeRegistry();
            var relationships = new RelationshipRuntime(
                world,
                relationshipTypes,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(capacity: 16),
                new RelationshipReverseIndex(world));
            int ownsType = relationshipTypes.Register("Owns");
            int controlsType = relationshipTypes.Register("Controls");
            var ownership = new OwnershipResolver(relationships, ownsType);
            var control = new ControlDomainQuery(world, relationships, ownership, ownsType, controlsType);
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig { Key = "test.move", OrderTypeId = GateConfig.TestOrderTypeId });
            var schemas = new NetworkCommandSchemaRegistry();
            schemas.Register(new NetworkCommandSchema(
                GateConfig.TestOrderTypeId,
                NetworkCommandTargetKind.WorldPositionCm,
                allowArg0: false,
                allowArg1: false,
                OrderSubmitMode.Immediate,
                KnowledgePositionAccess.None));
            schemas.Freeze();
            var orders = new OrderQueue(capacity: 8);
            var ingressConfig = new NetworkCommandIngressConfig(
                seatCapacity,
                simulationTickRateHz: GateConfig.FixedStepHz,
                maxBatchesPerSecond: GateConfig.FixedStepHz,
                burstBatchCapacity: 4,
                maxActorsPerBatch: 2,
                sequenceHistoryCapacity: 4,
                maxPastTargetTicks: 2,
                maxFutureTargetTicks: 2,
                scheduledBatchCapacity: 4);
            return new NetworkCommandIngress(
                in ingressConfig,
                world,
                entities,
                control,
                new KnowledgeProjectionResolver(knowledge),
                orderTypes,
                schemas,
                orders,
                results);
        }

        private static ReplicationSchemaProjectorRegistry CreateProjectors(IPhysics3DWorld physics)
        {
            var projectors = new ReplicationSchemaProjectorRegistry(schemaCapacity: GateConfig.SchemaId);
            if (projectors.Register(
                    GateConfig.SchemaId,
                    new Physics3DBodyReplicationProjector(
                        physics,
                        GateConfig.SchemaId,
                        new Physics3DReplicationQuantizationConfig())) != ReplicationSchemaRegistrationResult.Success)
            {
                throw new InvalidOperationException("Failed to register Physics3D body projector.");
            }

            projectors.Freeze();
            return projectors;
        }

        private static Physics3DWorldConfig CreateWorldConfig(
            int mobileCapacity,
            int staticBodyCapacity,
            int workerCount) => new()
        {
            MobileBodyCapacity = mobileCapacity,
            StaticBodyCapacity = staticBodyCapacity,
            ShapeCapacity = 8,
            InactiveIslandCapacity = Math.Max(1, mobileCapacity),
            ConstraintCapacity = 8,
            ConstraintsPerTypeBatchCapacity = 8,
            ConstraintCountPerBodyEstimate = 4,
            ContactPairCapacityPerWorker = 32,
            ActuationCommandCapacity = Math.Max(8, mobileCapacity * 4),
            WorkerCount = workerCount,
            FixedStepHz = GateConfig.FixedStepHz,
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
        };
    }

    private sealed class FixedCapacityMultiConnectionTransport :
        IServerConnectionEventPort,
        IServerDatagramPort,
        IServerConnectionControlPort,
        IDisposable
    {
        private readonly ConnectionId[] _connectionIds;
        private readonly ServerConnectionEvent[] _connectionEvents;
        private readonly InboundFrame[] _inboundFrames;
        private readonly int[] _sendCountsByKind;
        private readonly ulong[] _lastReplicationSnapshotIds;
        private readonly bool[] _publishedSeatsThisFrame;
        private readonly ulong[] _digestCapture;
        private readonly int[] _digestCounts;
        private readonly byte[] _decodeScratch;
        private int _connectionEventHead;
        private int _connectionEventTail;
        private int _connectionEventCount;
        private int _inboundHead;
        private int _inboundTail;
        private int _inboundCount;
        private bool _digestCaptureEnabled;

        public FixedCapacityMultiConnectionTransport(
            int connectionCapacity,
            int maxDatagramPayloadBytes,
            int inboundFrameCapacity,
            int connectionEventCapacity,
            int sendRecordCapacityPerSeat)
        {
            if (connectionCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionCapacity));
            }

            ConnectionCapacity = connectionCapacity;
            MaxDatagramPayloadBytes = maxDatagramPayloadBytes;
            _connectionIds = new ConnectionId[connectionCapacity];
            for (int index = 0; index < connectionCapacity; index++)
            {
                _connectionIds[index] = new ConnectionId(GateConfig.FirstConnectionValue + index);
            }

            _connectionEvents = new ServerConnectionEvent[connectionEventCapacity];
            _inboundFrames = new InboundFrame[inboundFrameCapacity];
            for (int index = 0; index < _inboundFrames.Length; index++)
            {
                _inboundFrames[index] = new InboundFrame(maxDatagramPayloadBytes);
            }

            _sendCountsByKind = new int[GateConfig.WireKindCardinality];
            _lastReplicationSnapshotIds = new ulong[connectionCapacity];
            _publishedSeatsThisFrame = new bool[connectionCapacity];
            _digestCapture = new ulong[checked(connectionCapacity * sendRecordCapacityPerSeat)];
            _digestCounts = new int[connectionCapacity];
            _decodeScratch = new byte[maxDatagramPayloadBytes];
        }

        public int ConnectionCapacity { get; }
        public int MaxDatagramPayloadBytes { get; }
        public int PublishedSeatCountForCurrentFrame { get; private set; }

        public void BeginFrame()
        {
            Array.Clear(_publishedSeatsThisFrame);
            PublishedSeatCountForCurrentFrame = 0;
        }

        public void ClearCounters()
        {
            Array.Clear(_sendCountsByKind);
            BeginFrame();
        }

        public void EnableDigestCapture() => _digestCaptureEnabled = true;

        public void ClearDigestCapture()
        {
            Array.Clear(_digestCounts);
            Array.Clear(_digestCapture);
        }

        public ulong[] CopyDigestCapture(out int[] counts)
        {
            counts = new int[_digestCounts.Length];
            _digestCounts.CopyTo(counts, 0);
            var digests = new ulong[_digestCapture.Length];
            _digestCapture.CopyTo(digests, 0);
            return digests;
        }

        public int GetSendCount(NetworkWireKind kind)
        {
            int index = (int)kind;
            return (uint)index < (uint)_sendCountsByKind.Length ? _sendCountsByKind[index] : 0;
        }

        public bool TryGetLastReplicationSnapshotId(int seatSlot, out ulong snapshotId)
        {
            if ((uint)seatSlot >= (uint)_lastReplicationSnapshotIds.Length)
            {
                snapshotId = 0;
                return false;
            }

            snapshotId = _lastReplicationSnapshotIds[seatSlot];
            return snapshotId != 0;
        }

        public void EnqueueConnected(ConnectionId connectionId)
        {
            if (_connectionEventCount >= _connectionEvents.Length)
            {
                throw new InvalidOperationException("Connection event capacity exceeded.");
            }

            _connectionEvents[_connectionEventTail] = new ServerConnectionEvent(
                connectionId,
                TransportConnectionEventKind.Connected);
            _connectionEventTail = (_connectionEventTail + 1) % _connectionEvents.Length;
            _connectionEventCount++;
        }

        public void EnqueueClientFrame(
            ConnectionId connectionId,
            ChannelId channelId,
            NetworkWireKind kind,
            ReadOnlySpan<byte> payload)
        {
            if (_inboundCount >= _inboundFrames.Length)
            {
                throw new InvalidOperationException("Inbound frame capacity exceeded.");
            }

            int framedLength = NetworkWireEnvelopeCodec.GetFramedLength(payload.Length);
            if (framedLength > MaxDatagramPayloadBytes)
            {
                throw new InvalidOperationException("Framed inbound payload exceeds datagram capacity.");
            }

            Span<byte> framed = _decodeScratch.AsSpan(0, framedLength);
            if (NetworkWireEnvelopeCodec.TryEncode(kind, payload, framed, out int bytes) != NetworkWireCodecStatus.Success)
            {
                throw new InvalidOperationException("Failed to encode inbound framed datagram.");
            }

            InboundFrame frame = _inboundFrames[_inboundTail];
            frame.Connection = connectionId;
            frame.Channel = channelId;
            frame.Length = bytes;
            framed[..bytes].CopyTo(frame.Payload);
            _inboundFrames[_inboundTail] = frame;
            _inboundTail = (_inboundTail + 1) % _inboundFrames.Length;
            _inboundCount++;
        }

        public void Pump()
        {
        }

        public bool TryReceiveConnectionEvent(out ServerConnectionEvent connectionEvent)
        {
            if (_connectionEventCount == 0)
            {
                connectionEvent = default;
                return false;
            }

            connectionEvent = _connectionEvents[_connectionEventHead];
            _connectionEvents[_connectionEventHead] = default;
            _connectionEventHead = (_connectionEventHead + 1) % _connectionEvents.Length;
            _connectionEventCount--;
            return true;
        }

        public bool TryReceive(
            Span<byte> buffer,
            out int bytesReceived,
            out ConnectionId connectionId,
            out ChannelId channelId)
        {
            if (_inboundCount == 0)
            {
                bytesReceived = 0;
                connectionId = default;
                channelId = default;
                return false;
            }

            InboundFrame frame = _inboundFrames[_inboundHead];
            if (buffer.Length < frame.Length)
            {
                throw new InvalidOperationException("Receive buffer is too small for inbound frame.");
            }

            frame.Payload.AsSpan(0, frame.Length).CopyTo(buffer);
            bytesReceived = frame.Length;
            connectionId = frame.Connection;
            channelId = frame.Channel;
            _inboundHead = (_inboundHead + 1) % _inboundFrames.Length;
            _inboundCount--;
            return true;
        }

        public DatagramSendStatus TrySend(ConnectionId connectionId, ChannelId channelId, ReadOnlySpan<byte> payload)
        {
            _ = channelId;
            int seatSlot = connectionId.Value - GateConfig.FirstConnectionValue;
            if ((uint)seatSlot >= (uint)ConnectionCapacity)
            {
                throw new InvalidOperationException("TrySend received an unknown connection id.");
            }

            if (NetworkWireEnvelopeCodec.TryDecode(payload, out NetworkWireEnvelope envelope, out ReadOnlySpan<byte> body) !=
                NetworkWireCodecStatus.Success)
            {
                throw new InvalidOperationException("TrySend received a malformed framed datagram.");
            }

            NetworkWireKind kind = envelope.Kind;
            int kindIndex = (int)kind;
            if ((uint)kindIndex < (uint)_sendCountsByKind.Length)
            {
                _sendCountsByKind[kindIndex]++;
            }

            ulong digest = Fnv1a64(payload);
            if (_digestCaptureEnabled)
            {
                int count = _digestCounts[seatSlot];
                int offset = checked(seatSlot * GateConfig.SendRecordCapacityPerSeat + count);
                if (count >= GateConfig.SendRecordCapacityPerSeat)
                {
                    throw new InvalidOperationException("Per-seat digest capture capacity exceeded.");
                }

                _digestCapture[offset] = digest;
                _digestCounts[seatSlot] = count + 1;
            }

            if (kind is NetworkWireKind.SnapshotFragment or NetworkWireKind.ReplicationPacket)
            {
                if (!TryReadSnapshotId(kind, body, out ulong snapshotId) || snapshotId == 0)
                {
                    throw new InvalidOperationException("Replication send missing snapshot id.");
                }

                _lastReplicationSnapshotIds[seatSlot] = snapshotId;
                if (!_publishedSeatsThisFrame[seatSlot])
                {
                    _publishedSeatsThisFrame[seatSlot] = true;
                    PublishedSeatCountForCurrentFrame++;
                }
            }

            return DatagramSendStatus.Sent;
        }

        public void DisconnectAfterReliableFlush(ConnectionId connectionId)
        {
            _ = connectionId;
        }

        public void Dispose()
        {
        }

        private static bool TryReadSnapshotId(NetworkWireKind kind, ReadOnlySpan<byte> body, out ulong snapshotId)
        {
            snapshotId = 0;
            if (kind == NetworkWireKind.SnapshotFragment)
            {
                if (SnapshotFragmentWireCodec.TryDecode(body, out NetworkSnapshotFragmentHeader header, out _) !=
                    NetworkWireCodecStatus.Success)
                {
                    return false;
                }

                snapshotId = header.SnapshotId;
                return true;
            }

            if (kind == NetworkWireKind.ReplicationPacket)
            {
                // Header layout: kind u8 | reserved 3 | sessionEpoch u64 | tick u32 | snapshotId u64 | ...
                if (body.Length < 1 + 3 + 8 + 4 + 8)
                {
                    return false;
                }

                snapshotId = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(body.Slice(1 + 3 + 8 + 4, 8));
                return snapshotId != 0;
            }

            return false;
        }

        private static ulong Fnv1a64(ReadOnlySpan<byte> data)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            for (int index = 0; index < data.Length; index++)
            {
                hash ^= data[index];
                hash *= prime;
            }

            return hash;
        }

        private struct InboundFrame
        {
            public InboundFrame(int capacity)
            {
                Connection = default;
                Channel = default;
                Length = 0;
                Payload = new byte[capacity];
            }

            public ConnectionId Connection;
            public ChannelId Channel;
            public int Length;
            public byte[] Payload;
        }
    }
}
