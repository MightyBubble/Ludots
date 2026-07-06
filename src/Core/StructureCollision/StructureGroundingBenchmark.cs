using System;
using System.Diagnostics;

namespace Ludots.Core.StructureCollision
{
    public sealed class StructureGroundingBenchmarkResult
    {
        public StructureGroundingBenchmarkResult(
            int totalSurfaces,
            int loadedChunks,
            int sampledPoints,
            int visitedChunks,
            int testedCandidateSurfaces,
            int maxCandidateSurfacesPerSample,
            int frames,
            double elapsedMilliseconds,
            double p95FrameMilliseconds,
            long managedAllocationsBytes)
        {
            TotalSurfaces = totalSurfaces;
            LoadedChunks = loadedChunks;
            SampledPoints = sampledPoints;
            VisitedChunks = visitedChunks;
            TestedCandidateSurfaces = testedCandidateSurfaces;
            MaxCandidateSurfacesPerSample = maxCandidateSurfacesPerSample;
            Frames = frames;
            ElapsedMilliseconds = elapsedMilliseconds;
            P95FrameMilliseconds = p95FrameMilliseconds;
            ManagedAllocationsBytes = managedAllocationsBytes;
        }

        public int TotalSurfaces { get; }

        public int LoadedChunks { get; }

        public int SampledPoints { get; }

        public int VisitedChunks { get; }

        public int TestedCandidateSurfaces { get; }

        public int MaxCandidateSurfacesPerSample { get; }

        public int Frames { get; }

        public double ElapsedMilliseconds { get; }

        public double P95FrameMilliseconds { get; }

        public long ManagedAllocationsBytes { get; }
    }

    public static class StructureGroundingBenchmark
    {
        public static StructureGroundingBenchmarkResult RunGridBenchmark(
            int surfaceColumns,
            int surfaceRows,
            int samplesPerFrame,
            int frames,
            int warmupFrames = 4,
            int cellSizeCm = 100)
        {
            if (samplesPerFrame <= 0) throw new ArgumentOutOfRangeException(nameof(samplesPerFrame));
            if (frames <= 0) throw new ArgumentOutOfRangeException(nameof(frames));
            if (warmupFrames < 0) throw new ArgumentOutOfRangeException(nameof(warmupFrames));

            StructureCollisionAsset asset = StructureCollisionAssetBuilder.CreateGridBenchmarkAsset(surfaceColumns, surfaceRows, cellSizeCm);
            var runtime = new StructureCollisionRuntimeState(asset);
            var sampler = new GroundSurfaceSampler(null, asset, runtime);
            var diagnostics = new StructureGroundingDiagnostics();
            var policy = new GroundSurfaceQueryPolicy(layerId: 0, agentMask: uint.MaxValue);

            var x = new float[samplesPerFrame];
            var z = new float[samplesPerFrame];
            FillSamples(x, z, surfaceColumns, surfaceRows, cellSizeCm);

            var h = new float[samplesPerFrame];
            var nx = new float[samplesPerFrame];
            var ny = new float[samplesPerFrame];
            var nz = new float[samplesPerFrame];
            var surfaceIds = new int[samplesPerFrame];
            var layerIds = new int[samplesPerFrame];
            var hitMask = new byte[samplesPerFrame];

            for (int i = 0; i < warmupFrames; i++)
            {
                sampler.ResolveGroundBatch(x, z, h, nx, ny, nz, surfaceIds, layerIds, hitMask, in policy, diagnostics);
            }

            var frameTicks = new long[frames];
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long allocationStart = GC.GetAllocatedBytesForCurrentThread();
            long totalStart = Stopwatch.GetTimestamp();
            int sampledPoints = 0;
            int visitedChunks = 0;
            int testedCandidates = 0;
            int maxCandidates = 0;

            for (int frame = 0; frame < frames; frame++)
            {
                long frameStart = Stopwatch.GetTimestamp();
                sampler.ResolveGroundBatch(x, z, h, nx, ny, nz, surfaceIds, layerIds, hitMask, in policy, diagnostics);
                frameTicks[frame] = Stopwatch.GetTimestamp() - frameStart;
                sampledPoints += diagnostics.SampledPoints;
                visitedChunks += diagnostics.VisitedChunks;
                testedCandidates += diagnostics.TestedCandidateSurfaces;
                if (diagnostics.MaxCandidateSurfacesPerSample > maxCandidates)
                {
                    maxCandidates = diagnostics.MaxCandidateSurfacesPerSample;
                }
            }

            long totalTicks = Stopwatch.GetTimestamp() - totalStart;
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            Array.Sort(frameTicks);
            int p95Index = Math.Clamp((int)Math.Ceiling(frames * 0.95d) - 1, 0, frames - 1);
            double tickToMs = 1000d / Stopwatch.Frequency;
            return new StructureGroundingBenchmarkResult(
                asset.SurfaceCount,
                asset.ChunkCount,
                sampledPoints,
                visitedChunks,
                testedCandidates,
                maxCandidates,
                frames,
                totalTicks * tickToMs,
                frameTicks[p95Index] * tickToMs,
                allocatedBytes);
        }

        private static void FillSamples(float[] x, float[] z, int columns, int rows, int cellSizeCm)
        {
            int total = checked(columns * rows);
            for (int i = 0; i < x.Length; i++)
            {
                int surface = i % total;
                int col = surface % columns;
                int row = surface / columns;
                x[i] = (col * cellSizeCm) + (cellSizeCm * 0.5f);
                z[i] = (row * cellSizeCm) + (cellSizeCm * 0.5f);
            }
        }
    }
}
