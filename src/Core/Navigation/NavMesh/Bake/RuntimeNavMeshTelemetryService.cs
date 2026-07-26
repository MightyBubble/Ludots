using System;
using System.Diagnostics;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Fixed-capacity telemetry for runtime-incremental navmesh dirty generations.
    /// Samples are generation-level: collect/bake/commit/allocation/rebuilt/published
    /// accumulate across every ProcessBudgetInto slice until commit or abort, then one
    /// sample is published. No rolling overwrite — capacity exhaustion fails fast.
    /// No per-sample managed allocation after construction.
    /// </summary>
    public sealed class RuntimeNavMeshTelemetryService
    {
        /// <summary>
        /// Sentinel for adapters that do not own a fixed preallocated scratch pool.
        /// Zero is reserved for a measured empty LayeredSpan pool proof and must not mean "unknown".
        /// </summary>
        public const long AdapterScratchNotOwned = -1L;

        private readonly long[] _collectTicks;
        private readonly long[] _bakeTicks;
        private readonly long[] _commitTicks;
        private readonly long[] _allocatedBytes;
        private readonly ulong[] _generationChecksums;
        private readonly int[] _processedTileCounts;
        private readonly int[] _publishedTileCounts;
        private readonly long[] _scratch;
        private readonly int _capacity;
        private int _count;
        private int _next;

        private bool _hasOpenGeneration;
        private long _openCollectTicks;
        private long _openBakeTicks;
        private long _openCommitTicks;
        private long _openAllocatedBytes;
        private int _openRebuiltTileCount;
        private int _openPublishedCount;
        private long _openPeakWorkerScratchBytes;
        private long _openPeakResidentBytes;
        private int _openPeakResidentTileCount;

        private long _lastCollectTicks;
        private long _lastBakeTicks;
        private long _lastCommitTicks;
        private long _lastAllocatedBytes;
        private int _lastRebuiltTileCount;
        private int _lastPublishedCount;
        private bool _lastCommitted;
        private bool _lastAborted;
        private ulong _lastGeneration;
        private ulong _lastGenerationChecksum;
        private int _lastWorkerCount;
        private int _lastPeakResidentTileCount;
        private long _lastPeakWorkerScratchBytes;
        private long _lastPeakResidentBytes;
        private int _droppedSampleCount;
        private int _failedBatchCount;
        private int _droppedDirtyCommandCount;
        private int _capacityGrowthCount;
        private int _fallbackCount;
        private int _mixedGenerationCount;
        private long _peakWorkerScratchBytes;
        private long _peakResidentBytes;
        private int _peakResidentTileCount;
        private long _totalProcessedTiles;
        private long _totalBakeTicks;

        public RuntimeNavMeshTelemetryService(int sampleCapacity)
        {
            if (sampleCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleCapacity),
                    "RuntimeNavMeshTelemetryService sampleCapacity must be > 0.");
            }

            if (sampleCapacity > 4096)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleCapacity),
                    "RuntimeNavMeshTelemetryService sampleCapacity must be <= 4096 to keep CaptureSnapshot scratch bounded.");
            }

            _capacity = sampleCapacity;
            _collectTicks = new long[sampleCapacity];
            _bakeTicks = new long[sampleCapacity];
            _commitTicks = new long[sampleCapacity];
            _allocatedBytes = new long[sampleCapacity];
            _generationChecksums = new ulong[sampleCapacity];
            _processedTileCounts = new int[sampleCapacity];
            _publishedTileCounts = new int[sampleCapacity];
            _scratch = new long[sampleCapacity];
            _openPeakWorkerScratchBytes = AdapterScratchNotOwned;
            _lastPeakWorkerScratchBytes = AdapterScratchNotOwned;
            _peakWorkerScratchBytes = AdapterScratchNotOwned;
        }

        public int SampleCapacity => _capacity;

        public int SampleCount => _count;

        public bool HasOpenGeneration => _hasOpenGeneration;

        public int DroppedSampleCount => _droppedSampleCount;

        public int FailedBatchCount => _failedBatchCount;

        public int DroppedDirtyCommandCount => _droppedDirtyCommandCount;

        public int CapacityGrowthCount => _capacityGrowthCount;

        /// <summary>
        /// Fallback path does not exist in production; increments only if a caller records one.
        /// </summary>
        public int FallbackCount => _fallbackCount;

        public int MixedGenerationCount => _mixedGenerationCount;

        public long PeakWorkerScratchBytes => _peakWorkerScratchBytes;

        public long PeakResidentBytes => _peakResidentBytes;

        public int PeakResidentTileCount => _peakResidentTileCount;

        /// <summary>
        /// Begins a new evidence epoch: clears committed samples and baselines every failure counter.
        /// Fails fast if a generation is still open (would silently lose partial work).
        /// Last committed generation fields are cleared so a prior epoch cannot leak into the next.
        /// </summary>
        public void ResetSamples()
        {
            if (_hasOpenGeneration)
            {
                throw new InvalidOperationException(
                    "RuntimeNavMeshTelemetryService.ResetSamples cannot discard an open dirty generation; " +
                    "wait for commit/abort or the sample would be silently lost.");
            }

            _count = 0;
            _next = 0;
            _droppedSampleCount = 0;
            _failedBatchCount = 0;
            _mixedGenerationCount = 0;
            _droppedDirtyCommandCount = 0;
            _capacityGrowthCount = 0;
            _fallbackCount = 0;
            _totalProcessedTiles = 0;
            _totalBakeTicks = 0;
            _peakWorkerScratchBytes = AdapterScratchNotOwned;
            _peakResidentBytes = 0;
            _peakResidentTileCount = 0;
            _lastCollectTicks = 0L;
            _lastBakeTicks = 0L;
            _lastCommitTicks = 0L;
            _lastAllocatedBytes = 0L;
            _lastRebuiltTileCount = 0;
            _lastPublishedCount = 0;
            _lastCommitted = false;
            _lastAborted = false;
            _lastGeneration = 0UL;
            _lastGenerationChecksum = 0UL;
            _lastWorkerCount = 0;
            _lastPeakResidentTileCount = 0;
            _lastPeakWorkerScratchBytes = AdapterScratchNotOwned;
            _lastPeakResidentBytes = 0L;
        }

        /// <summary>
        /// Records one fixed-tick slice of a dirty generation. Accumulates until
        /// <paramref name="stats"/> reports committed or aborted, then publishes exactly one sample.
        /// </summary>
        public void RecordHotUpdate(
            long collectTicks,
            long bakeTicks,
            long commitTicks,
            long allocatedBytes,
            in RuntimeNavMeshRebuildBatchStats stats,
            long peakWorkerScratchBytes,
            long peakResidentBytes,
            int droppedDirtyCommandCount,
            int capacityGrowthCount,
            int fallbackCount)
        {
            if (collectTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(collectTicks));
            }

            if (bakeTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bakeTicks));
            }

            if (commitTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(commitTicks));
            }

            if (allocatedBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(allocatedBytes));
            }

            if (peakWorkerScratchBytes < AdapterScratchNotOwned)
            {
                throw new ArgumentOutOfRangeException(nameof(peakWorkerScratchBytes));
            }

            if (peakResidentBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(peakResidentBytes));
            }

            if (droppedDirtyCommandCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(droppedDirtyCommandCount));
            }

            if (capacityGrowthCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacityGrowthCount));
            }

            if (fallbackCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fallbackCount));
            }

            _droppedDirtyCommandCount = droppedDirtyCommandCount;
            _capacityGrowthCount = capacityGrowthCount;
            _fallbackCount = fallbackCount;

            if (!_hasOpenGeneration)
            {
                BeginOpenGeneration();
            }

            _openCollectTicks = checked(_openCollectTicks + collectTicks);
            _openBakeTicks = checked(_openBakeTicks + bakeTicks);
            _openCommitTicks = checked(_openCommitTicks + commitTicks);
            _openAllocatedBytes = checked(_openAllocatedBytes + allocatedBytes);
            _openRebuiltTileCount = checked(_openRebuiltTileCount + stats.RebuiltTileCount);
            if (stats.PublishedCount > _openPublishedCount)
            {
                _openPublishedCount = stats.PublishedCount;
            }

            if (peakWorkerScratchBytes > _openPeakWorkerScratchBytes)
            {
                _openPeakWorkerScratchBytes = peakWorkerScratchBytes;
            }

            if (peakResidentBytes > _openPeakResidentBytes)
            {
                _openPeakResidentBytes = peakResidentBytes;
            }

            if (stats.PeakResidentTileCount > _openPeakResidentTileCount)
            {
                _openPeakResidentTileCount = stats.PeakResidentTileCount;
            }

            if (stats.MixedGenerationDetected)
            {
                _mixedGenerationCount = checked(_mixedGenerationCount + 1);
            }

            if (!stats.Committed && !stats.Aborted)
            {
                return;
            }

            if (stats.Aborted)
            {
                // Abort fails the open generation without publishing a sample and without
                // overwriting last committed generation evidence.
                _failedBatchCount = checked(_failedBatchCount + 1);
                _hasOpenGeneration = false;
                _lastAborted = true;
                _lastCommitted = false;
                return;
            }

            PublishOpenGeneration(in stats);
        }

        public void RecordMixedGenerationObservation()
        {
            _mixedGenerationCount = checked(_mixedGenerationCount + 1);
        }

        /// <summary>
        /// Captures committed-sample percentiles. Runtime UI may call this while a generation is open;
        /// last-* fields remain the last committed sample. Partial open work is never silently published.
        /// </summary>
        public RuntimeNavMeshTelemetrySnapshot CaptureSnapshot()
        {
            Span<long> scratch = _scratch.AsSpan(0, _count);
            CopyRingSorted(_collectTicks, scratch);
            long collectP50 = Percentile(scratch, 0.50);
            long collectP95 = Percentile(scratch, 0.95);

            CopyRingSorted(_bakeTicks, scratch);
            long bakeP50 = Percentile(scratch, 0.50);
            long bakeP95 = Percentile(scratch, 0.95);

            CopyRingSorted(_commitTicks, scratch);
            long commitP50 = Percentile(scratch, 0.50);
            long commitP95 = Percentile(scratch, 0.95);

            CopyRing(_collectTicks, scratch);
            for (int i = 0; i < _count; i++)
            {
                scratch[i] = checked(scratch[i] + _bakeTicks[i] + _commitTicks[i]);
            }

            SortAscending(scratch);
            long dirtyPublishP50 = Percentile(scratch, 0.50);
            long dirtyPublishP95 = Percentile(scratch, 0.95);

            CopyRingSorted(_allocatedBytes, scratch);
            long allocP50 = Percentile(scratch, 0.50);
            long allocP95 = Percentile(scratch, 0.95);

            double tilesPerSecond = 0d;
            if (_totalBakeTicks > 0 && Stopwatch.Frequency > 0)
            {
                tilesPerSecond = _totalProcessedTiles * (double)Stopwatch.Frequency / _totalBakeTicks;
            }

            return new RuntimeNavMeshTelemetrySnapshot(
                sampleCount: _count,
                sampleCapacity: _capacity,
                collectTicksP50: collectP50,
                collectTicksP95: collectP95,
                bakeTicksP50: bakeP50,
                bakeTicksP95: bakeP95,
                commitTicksP50: commitP50,
                commitTicksP95: commitP95,
                dirtyPublishTicksP50: dirtyPublishP50,
                dirtyPublishTicksP95: dirtyPublishP95,
                allocatedBytesP50: allocP50,
                allocatedBytesP95: allocP95,
                lastCollectTicks: _lastCollectTicks,
                lastBakeTicks: _lastBakeTicks,
                lastCommitTicks: _lastCommitTicks,
                lastAllocatedBytes: _lastAllocatedBytes,
                lastRebuiltTileCount: _lastRebuiltTileCount,
                lastPublishedCount: _lastPublishedCount,
                lastCommitted: _lastCommitted,
                lastAborted: _lastAborted,
                lastGeneration: _lastGeneration,
                lastGenerationChecksum: _lastGenerationChecksum,
                lastWorkerCount: _lastWorkerCount,
                lastPeakResidentTileCount: _lastPeakResidentTileCount,
                lastPeakWorkerScratchBytes: _lastPeakWorkerScratchBytes,
                lastPeakResidentBytes: _lastPeakResidentBytes,
                peakWorkerScratchBytes: _peakWorkerScratchBytes,
                peakResidentBytes: _peakResidentBytes,
                peakResidentTileCount: _peakResidentTileCount,
                totalProcessedTiles: _totalProcessedTiles,
                steadyStateTilesPerSecond: tilesPerSecond,
                droppedSampleCount: _droppedSampleCount,
                failedBatchCount: _failedBatchCount,
                droppedDirtyCommandCount: _droppedDirtyCommandCount,
                capacityGrowthCount: _capacityGrowthCount,
                fallbackCount: _fallbackCount,
                mixedGenerationCount: _mixedGenerationCount,
                stopwatchFrequency: Stopwatch.Frequency,
                hasOpenGeneration: _hasOpenGeneration);
        }

        public int CopyGenerationChecksumSequence(Span<ulong> destination)
        {
            if (destination.Length < _count)
            {
                throw new ArgumentException(
                    $"Generation checksum destination length {destination.Length} is below sample count {_count}.",
                    nameof(destination));
            }

            for (int i = 0; i < _count; i++)
            {
                destination[i] = _generationChecksums[i];
            }

            return _count;
        }

        /// <summary>
        /// Per-generation rebuilt tile counts for the current evidence epoch (same order as checksums).
        /// Proves compared scenes performed equal dirty tile work, not only equal wall geometry authoring.
        /// </summary>
        public int CopyProcessedTileCountSequence(Span<int> destination)
        {
            if (destination.Length < _count)
            {
                throw new ArgumentException(
                    $"Processed tile count destination length {destination.Length} is below sample count {_count}.",
                    nameof(destination));
            }

            for (int i = 0; i < _count; i++)
            {
                destination[i] = _processedTileCounts[i];
            }

            return _count;
        }

        private void BeginOpenGeneration()
        {
            _hasOpenGeneration = true;
            _openCollectTicks = 0L;
            _openBakeTicks = 0L;
            _openCommitTicks = 0L;
            _openAllocatedBytes = 0L;
            _openRebuiltTileCount = 0;
            _openPublishedCount = 0;
            _openPeakWorkerScratchBytes = AdapterScratchNotOwned;
            _openPeakResidentBytes = 0L;
            _openPeakResidentTileCount = 0;
        }

        private void PublishOpenGeneration(in RuntimeNavMeshRebuildBatchStats stats)
        {
            if (_count >= _capacity)
            {
                // Overflow must preserve the open generation so callers can observe partial work;
                // never silently drop or clear the in-flight sample.
                _droppedSampleCount = checked(_droppedSampleCount + 1);
                throw new InvalidOperationException(
                    $"RuntimeNavMeshTelemetryService sample capacity {_capacity} exhausted; " +
                    "generation-level sample would be silently lost. Increase evidenceSampleCount or ResetSamples between windows.");
            }

            int slot = _next;
            _collectTicks[slot] = _openCollectTicks;
            _bakeTicks[slot] = _openBakeTicks;
            _commitTicks[slot] = _openCommitTicks;
            _allocatedBytes[slot] = _openAllocatedBytes;
            _generationChecksums[slot] = stats.GenerationChecksum;
            _processedTileCounts[slot] = _openRebuiltTileCount;
            _publishedTileCounts[slot] = _openPublishedCount;
            _next = checked(slot + 1);
            _count = checked(_count + 1);

            _lastCollectTicks = _openCollectTicks;
            _lastBakeTicks = _openBakeTicks;
            _lastCommitTicks = _openCommitTicks;
            _lastAllocatedBytes = _openAllocatedBytes;
            _lastRebuiltTileCount = _openRebuiltTileCount;
            _lastPublishedCount = _openPublishedCount;
            _lastCommitted = true;
            _lastAborted = false;
            _lastGeneration = stats.Generation;
            _lastGenerationChecksum = stats.GenerationChecksum;
            _lastWorkerCount = stats.WorkerCount;
            _lastPeakResidentTileCount = _openPeakResidentTileCount;
            _lastPeakWorkerScratchBytes = _openPeakWorkerScratchBytes;
            _lastPeakResidentBytes = _openPeakResidentBytes;

            if (_openPeakWorkerScratchBytes > _peakWorkerScratchBytes)
            {
                _peakWorkerScratchBytes = _openPeakWorkerScratchBytes;
            }

            if (_openPeakResidentBytes > _peakResidentBytes)
            {
                _peakResidentBytes = _openPeakResidentBytes;
            }

            if (_openPeakResidentTileCount > _peakResidentTileCount)
            {
                _peakResidentTileCount = _openPeakResidentTileCount;
            }

            _totalProcessedTiles = checked(_totalProcessedTiles + _openRebuiltTileCount);
            _totalBakeTicks = checked(_totalBakeTicks + _openBakeTicks);

            if (stats.FailedEntryCount > 0)
            {
                _failedBatchCount = checked(_failedBatchCount + 1);
            }

            _hasOpenGeneration = false;
        }

        private void CopyRing(long[] source, Span<long> destination)
        {
            for (int i = 0; i < destination.Length; i++)
            {
                destination[i] = source[i];
            }
        }

        private void CopyRingSorted(long[] source, Span<long> destination)
        {
            CopyRing(source, destination);
            SortAscending(destination);
        }

        private static void SortAscending(Span<long> destination)
        {
            for (int i = 1; i < destination.Length; i++)
            {
                long value = destination[i];
                int j = i - 1;
                while (j >= 0 && destination[j] > value)
                {
                    destination[j + 1] = destination[j];
                    j--;
                }

                destination[j + 1] = value;
            }
        }

        private static long Percentile(ReadOnlySpan<long> sortedAscending, double percentile)
        {
            if (sortedAscending.Length == 0)
            {
                return 0L;
            }

            if (percentile < 0.0 || percentile > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(percentile));
            }

            int index = (int)Math.Ceiling(percentile * sortedAscending.Length) - 1;
            if (index < 0)
            {
                index = 0;
            }

            if (index >= sortedAscending.Length)
            {
                index = sortedAscending.Length - 1;
            }

            return sortedAscending[index];
        }
    }

    public readonly struct RuntimeNavMeshTelemetrySnapshot
    {
        public RuntimeNavMeshTelemetrySnapshot(
            int sampleCount,
            int sampleCapacity,
            long collectTicksP50,
            long collectTicksP95,
            long bakeTicksP50,
            long bakeTicksP95,
            long commitTicksP50,
            long commitTicksP95,
            long dirtyPublishTicksP50,
            long dirtyPublishTicksP95,
            long allocatedBytesP50,
            long allocatedBytesP95,
            long lastCollectTicks,
            long lastBakeTicks,
            long lastCommitTicks,
            long lastAllocatedBytes,
            int lastRebuiltTileCount,
            int lastPublishedCount,
            bool lastCommitted,
            bool lastAborted,
            ulong lastGeneration,
            ulong lastGenerationChecksum,
            int lastWorkerCount,
            int lastPeakResidentTileCount,
            long lastPeakWorkerScratchBytes,
            long lastPeakResidentBytes,
            long peakWorkerScratchBytes,
            long peakResidentBytes,
            int peakResidentTileCount,
            long totalProcessedTiles,
            double steadyStateTilesPerSecond,
            int droppedSampleCount,
            int failedBatchCount,
            int droppedDirtyCommandCount,
            int capacityGrowthCount,
            int fallbackCount,
            int mixedGenerationCount,
            long stopwatchFrequency,
            bool hasOpenGeneration = false)
        {
            SampleCount = sampleCount;
            SampleCapacity = sampleCapacity;
            CollectTicksP50 = collectTicksP50;
            CollectTicksP95 = collectTicksP95;
            BakeTicksP50 = bakeTicksP50;
            BakeTicksP95 = bakeTicksP95;
            CommitTicksP50 = commitTicksP50;
            CommitTicksP95 = commitTicksP95;
            DirtyPublishTicksP50 = dirtyPublishTicksP50;
            DirtyPublishTicksP95 = dirtyPublishTicksP95;
            AllocatedBytesP50 = allocatedBytesP50;
            AllocatedBytesP95 = allocatedBytesP95;
            LastCollectTicks = lastCollectTicks;
            LastBakeTicks = lastBakeTicks;
            LastCommitTicks = lastCommitTicks;
            LastAllocatedBytes = lastAllocatedBytes;
            LastRebuiltTileCount = lastRebuiltTileCount;
            LastPublishedCount = lastPublishedCount;
            LastCommitted = lastCommitted;
            LastAborted = lastAborted;
            LastGeneration = lastGeneration;
            LastGenerationChecksum = lastGenerationChecksum;
            LastWorkerCount = lastWorkerCount;
            LastPeakResidentTileCount = lastPeakResidentTileCount;
            LastPeakWorkerScratchBytes = lastPeakWorkerScratchBytes;
            LastPeakResidentBytes = lastPeakResidentBytes;
            PeakWorkerScratchBytes = peakWorkerScratchBytes;
            PeakResidentBytes = peakResidentBytes;
            PeakResidentTileCount = peakResidentTileCount;
            TotalProcessedTiles = totalProcessedTiles;
            SteadyStateTilesPerSecond = steadyStateTilesPerSecond;
            DroppedSampleCount = droppedSampleCount;
            FailedBatchCount = failedBatchCount;
            DroppedDirtyCommandCount = droppedDirtyCommandCount;
            CapacityGrowthCount = capacityGrowthCount;
            FallbackCount = fallbackCount;
            MixedGenerationCount = mixedGenerationCount;
            StopwatchFrequency = stopwatchFrequency;
            HasOpenGeneration = hasOpenGeneration;
        }

        public int SampleCount { get; }
        public int SampleCapacity { get; }
        public long CollectTicksP50 { get; }
        public long CollectTicksP95 { get; }
        public long BakeTicksP50 { get; }
        public long BakeTicksP95 { get; }
        public long CommitTicksP50 { get; }
        public long CommitTicksP95 { get; }
        public long DirtyPublishTicksP50 { get; }
        public long DirtyPublishTicksP95 { get; }
        public long AllocatedBytesP50 { get; }
        public long AllocatedBytesP95 { get; }
        public long LastCollectTicks { get; }
        public long LastBakeTicks { get; }
        public long LastCommitTicks { get; }
        public long LastAllocatedBytes { get; }
        public int LastRebuiltTileCount { get; }
        public int LastPublishedCount { get; }
        public bool LastCommitted { get; }
        public bool LastAborted { get; }
        public ulong LastGeneration { get; }
        public ulong LastGenerationChecksum { get; }
        public int LastWorkerCount { get; }
        public int LastPeakResidentTileCount { get; }
        public long LastPeakWorkerScratchBytes { get; }
        public long LastPeakResidentBytes { get; }
        public long PeakWorkerScratchBytes { get; }
        public long PeakResidentBytes { get; }
        public int PeakResidentTileCount { get; }
        public long TotalProcessedTiles { get; }
        public double SteadyStateTilesPerSecond { get; }
        public int DroppedSampleCount { get; }
        public int FailedBatchCount { get; }
        public int DroppedDirtyCommandCount { get; }
        public int CapacityGrowthCount { get; }
        public int FallbackCount { get; }
        public int MixedGenerationCount { get; }
        public long StopwatchFrequency { get; }
        public bool HasOpenGeneration { get; }

        public double CollectMsP50 => TicksToMs(CollectTicksP50);
        public double CollectMsP95 => TicksToMs(CollectTicksP95);
        public double BakeMsP50 => TicksToMs(BakeTicksP50);
        public double BakeMsP95 => TicksToMs(BakeTicksP95);
        public double CommitMsP50 => TicksToMs(CommitTicksP50);
        public double CommitMsP95 => TicksToMs(CommitTicksP95);
        public double DirtyPublishMsP50 => TicksToMs(DirtyPublishTicksP50);
        public double DirtyPublishMsP95 => TicksToMs(DirtyPublishTicksP95);
        public double DurationMsP50 => DirtyPublishMsP50;
        public double DurationMsP95 => DirtyPublishMsP95;
        public double LastDurationMs => TicksToMs(checked(LastCollectTicks + LastBakeTicks + LastCommitTicks));
        public long LastDurationTicks => checked(LastCollectTicks + LastBakeTicks + LastCommitTicks);

        private double TicksToMs(long ticks)
            => StopwatchFrequency <= 0 ? 0d : ticks * 1000d / StopwatchFrequency;
    }
}
