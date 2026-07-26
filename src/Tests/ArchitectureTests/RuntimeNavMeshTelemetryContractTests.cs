using System;
using System.Diagnostics;
using Ludots.Core.Navigation.NavMesh.Bake;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class RuntimeNavMeshTelemetryContractTests
    {
        [Test]
        public void RecordHotUpdate_AfterWarmup_AllocatesExactlyZeroManagedBytes()
        {
            var telemetry = new RuntimeNavMeshTelemetryService(sampleCapacity: 16);
            RuntimeNavMeshRebuildBatchStats stats = CreateStats(generation: 1UL, checksum: 11UL);
            telemetry.RecordHotUpdate(1, 2, 3, 0, in stats, 100, 200, 0, 0, 0);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.GetAllocatedBytesForCurrentThread();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 8; i++)
            {
                stats = CreateStats(generation: (ulong)(i + 2), checksum: (ulong)(20 + i));
                telemetry.RecordHotUpdate(4, 5, 6, 0, in stats, 100, 200, 0, 0, 0);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0L), $"RecordHotUpdate allocated {allocated} bytes.");
        }

        [Test]
        public void CaptureSnapshot_ReportsSeparateCollectBakeCommitPercentiles()
        {
            var telemetry = new RuntimeNavMeshTelemetryService(sampleCapacity: 8);
            for (int i = 0; i < 4; i++)
            {
                var stats = CreateStats(generation: (ulong)(i + 1), checksum: (ulong)(100 + i));
                telemetry.RecordHotUpdate(
                    collectTicks: 10 + i,
                    bakeTicks: 100 + (i * 10),
                    commitTicks: 20 + i,
                    allocatedBytes: 0,
                    in stats,
                    peakWorkerScratchBytes: 1024,
                    peakResidentBytes: 2048,
                    droppedDirtyCommandCount: 0,
                    capacityGrowthCount: 0,
                    fallbackCount: 0);
            }

            RuntimeNavMeshTelemetrySnapshot snap = telemetry.CaptureSnapshot();
            Assert.That(snap.SampleCount, Is.EqualTo(4));
            Assert.That(snap.CollectTicksP50, Is.GreaterThan(0));
            Assert.That(snap.BakeTicksP50, Is.GreaterThan(snap.CollectTicksP50));
            Assert.That(snap.CommitTicksP50, Is.GreaterThan(0));
            Assert.That(snap.DirtyPublishTicksP95, Is.EqualTo(13 + 130 + 23));
            Assert.That(snap.PeakWorkerScratchBytes, Is.EqualTo(1024));
            Assert.That(snap.PeakResidentBytes, Is.EqualTo(2048));
        }

        [Test]
        public void SampleCapacityExhausted_FailsFast_PreservesOpenGeneration()
        {
            var telemetry = new RuntimeNavMeshTelemetryService(sampleCapacity: 2);
            RuntimeNavMeshRebuildBatchStats first = CreateStats(1, 1);
            RuntimeNavMeshRebuildBatchStats second = CreateStats(2, 2);
            telemetry.RecordHotUpdate(1, 1, 1, 0, in first, 1, 1, 0, 0, 0);
            telemetry.RecordHotUpdate(2, 2, 2, 0, in second, 1, 1, 0, 0, 0);

            // Start a third generation and accumulate a partial slice, then commit — overflow must preserve open.
            var partial = new RuntimeNavMeshRebuildBatchStats(
                requestedTileBudget: 4,
                rebuiltTileCount: 1,
                failedEntryCount: 0,
                pendingTileCount: 1,
                sealedRemainingCount: 1,
                committed: false,
                aborted: false,
                generation: 0UL,
                publishedCount: 0,
                bakeTicks: 3,
                commitTicks: 0,
                generationChecksum: 0UL,
                peakResidentTileCount: 1,
                workerCount: 1);
            telemetry.RecordHotUpdate(1, 1, 0, 0, in partial, 1, 1, 0, 0, 0);
            Assert.That(telemetry.HasOpenGeneration, Is.True);

            RuntimeNavMeshRebuildBatchStats third = CreateStats(3, 3);
            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
                () => telemetry.RecordHotUpdate(1, 1, 1, 0, in third, 1, 1, 0, 0, 0));
            Assert.That(ex!.Message, Does.Contain("sample capacity"));
            Assert.That(telemetry.HasOpenGeneration, Is.True, "Overflow must preserve the open generation.");
            Assert.That(telemetry.DroppedSampleCount, Is.EqualTo(1));
            Assert.That(telemetry.SampleCount, Is.EqualTo(2));
            Assert.That(telemetry.CaptureSnapshot().LastGeneration, Is.EqualTo(2UL));
        }

        [Test]
        public void Abort_DoesNotOverwriteLastCommittedGeneration_AndDoesNotPublishSample()
        {
            var telemetry = new RuntimeNavMeshTelemetryService(sampleCapacity: 4);
            RuntimeNavMeshRebuildBatchStats committed = CreateStats(7, 0xABC);
            telemetry.RecordHotUpdate(1, 1, 1, 0, in committed, 10, 20, 0, 0, 0);
            Assert.That(telemetry.CaptureSnapshot().LastGeneration, Is.EqualTo(7UL));
            Assert.That(telemetry.SampleCount, Is.EqualTo(1));

            var failed = new RuntimeNavMeshRebuildBatchStats(
                requestedTileBudget: 1,
                rebuiltTileCount: 0,
                failedEntryCount: 1,
                pendingTileCount: 0,
                sealedRemainingCount: 0,
                committed: false,
                aborted: true,
                generation: 0UL,
                publishedCount: 0);
            telemetry.RecordHotUpdate(1, 1, 0, 0, in failed, 0, 0, droppedDirtyCommandCount: 0, capacityGrowthCount: 0, fallbackCount: 0);

            RuntimeNavMeshTelemetrySnapshot snap = telemetry.CaptureSnapshot();
            Assert.That(snap.SampleCount, Is.EqualTo(1), "Abort must not publish a complete-generation sample.");
            Assert.That(snap.LastGeneration, Is.EqualTo(7UL), "Abort must not overwrite last committed generation.");
            Assert.That(snap.LastGenerationChecksum, Is.EqualTo(0xABCUL));
            Assert.That(snap.LastAborted, Is.True);
            Assert.That(snap.LastCommitted, Is.False);
            Assert.That(snap.FailedBatchCount, Is.EqualTo(1));
            Assert.That(telemetry.HasOpenGeneration, Is.False);
        }

        [Test]
        public void ResetSamples_BaselinesEveryFailureCounter_AndClearsLastCommitted()
        {
            var telemetry = new RuntimeNavMeshTelemetryService(sampleCapacity: 4);
            RuntimeNavMeshRebuildBatchStats stats = CreateStats(1, 9);
            telemetry.RecordHotUpdate(1, 1, 1, 0, in stats, 1, 1, droppedDirtyCommandCount: 2, capacityGrowthCount: 3, fallbackCount: 4);
            telemetry.RecordMixedGenerationObservation();
            Assert.That(telemetry.CaptureSnapshot().FailedBatchCount, Is.EqualTo(0));
            Assert.That(telemetry.DroppedDirtyCommandCount, Is.EqualTo(2));
            Assert.That(telemetry.MixedGenerationCount, Is.EqualTo(1));

            telemetry.ResetSamples();
            RuntimeNavMeshTelemetrySnapshot snap = telemetry.CaptureSnapshot();
            Assert.That(snap.SampleCount, Is.EqualTo(0));
            Assert.That(snap.DroppedSampleCount, Is.EqualTo(0));
            Assert.That(snap.FailedBatchCount, Is.EqualTo(0));
            Assert.That(snap.DroppedDirtyCommandCount, Is.EqualTo(0));
            Assert.That(snap.CapacityGrowthCount, Is.EqualTo(0));
            Assert.That(snap.FallbackCount, Is.EqualTo(0));
            Assert.That(snap.MixedGenerationCount, Is.EqualTo(0));
            Assert.That(snap.LastGeneration, Is.EqualTo(0UL));
            Assert.That(snap.PeakWorkerScratchBytes, Is.EqualTo(RuntimeNavMeshTelemetryService.AdapterScratchNotOwned));
        }

        [Test]
        public void FailureCounters_AreRecordedExplicitly_NotSilentlyZeroedWhenObserved()
        {
            var telemetry = new RuntimeNavMeshTelemetryService(sampleCapacity: 4);
            var failed = new RuntimeNavMeshRebuildBatchStats(
                requestedTileBudget: 1,
                rebuiltTileCount: 0,
                failedEntryCount: 1,
                pendingTileCount: 0,
                sealedRemainingCount: 0,
                committed: false,
                aborted: true,
                generation: 0UL,
                publishedCount: 0);
            telemetry.RecordHotUpdate(1, 1, 0, 0, in failed, 0, 0, droppedDirtyCommandCount: 2, capacityGrowthCount: 1, fallbackCount: 1);
            telemetry.RecordMixedGenerationObservation();

            RuntimeNavMeshTelemetrySnapshot snap = telemetry.CaptureSnapshot();
            Assert.That(snap.FailedBatchCount, Is.EqualTo(1));
            Assert.That(snap.DroppedDirtyCommandCount, Is.EqualTo(2));
            Assert.That(snap.CapacityGrowthCount, Is.EqualTo(1));
            Assert.That(snap.FallbackCount, Is.EqualTo(1));
            Assert.That(snap.MixedGenerationCount, Is.EqualTo(1));
        }

        [Test]
        public void MixedGenerationDetected_OnCommit_IncrementsCounter()
        {
            var telemetry = new RuntimeNavMeshTelemetryService(sampleCapacity: 4);
            var mixed = new RuntimeNavMeshRebuildBatchStats(
                requestedTileBudget: 4,
                rebuiltTileCount: 1,
                failedEntryCount: 0,
                pendingTileCount: 0,
                sealedRemainingCount: 0,
                committed: true,
                aborted: false,
                generation: 3UL,
                publishedCount: 1,
                bakeTicks: 10,
                commitTicks: 2,
                generationChecksum: 33UL,
                peakResidentTileCount: 1,
                workerCount: 1,
                mixedGenerationDetected: true);
            telemetry.RecordHotUpdate(1, 1, 1, 0, in mixed, 0, 0, 0, 0, 0);
            Assert.That(telemetry.MixedGenerationCount, Is.EqualTo(1));
            Assert.That(telemetry.SampleCount, Is.EqualTo(1));
        }

        [Test]
        public void CopyGenerationChecksumSequence_RejectsUndersizedDestination()
        {
            var telemetry = new RuntimeNavMeshTelemetryService(sampleCapacity: 4);
            RuntimeNavMeshRebuildBatchStats stats = CreateStats(1, 9);
            telemetry.RecordHotUpdate(1, 1, 1, 0, in stats, 0, 0, 0, 0, 0);
            var tiny = new ulong[0];
            Assert.Throws<ArgumentException>(() => telemetry.CopyGenerationChecksumSequence(tiny));
        }

        [Test]
        public void CopyProcessedTileCountSequence_CopiesEpochRebuiltCounts()
        {
            var telemetry = new RuntimeNavMeshTelemetryService(sampleCapacity: 4);
            RuntimeNavMeshRebuildBatchStats first = CreateStats(1, 11, rebuiltTileCount: 16);
            RuntimeNavMeshRebuildBatchStats second = CreateStats(2, 22, rebuiltTileCount: 20);
            telemetry.RecordHotUpdate(1, 1, 1, 0, in first, 0, 0, 0, 0, 0);
            telemetry.RecordHotUpdate(1, 1, 1, 0, in second, 0, 0, 0, 0, 0);

            var counts = new int[2];
            Assert.That(telemetry.CopyProcessedTileCountSequence(counts), Is.EqualTo(2));
            Assert.That(counts, Is.EqualTo(new[] { 16, 20 }));
            Assert.Throws<ArgumentException>(() => telemetry.CopyProcessedTileCountSequence(new int[1]));
        }

        [Test]
        public void SampleCapacity_OutOfRange_FailsFast()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeNavMeshTelemetryService(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeNavMeshTelemetryService(4097));
        }

        private static RuntimeNavMeshRebuildBatchStats CreateStats(
            ulong generation,
            ulong checksum,
            int rebuiltTileCount = 1)
            => new(
                requestedTileBudget: 4,
                rebuiltTileCount: rebuiltTileCount,
                failedEntryCount: 0,
                pendingTileCount: 0,
                sealedRemainingCount: 0,
                committed: true,
                aborted: false,
                generation: generation,
                publishedCount: rebuiltTileCount,
                bakeTicks: 10,
                commitTicks: 2,
                generationChecksum: checksum,
                peakResidentTileCount: rebuiltTileCount,
                workerCount: 1);
    }
}
