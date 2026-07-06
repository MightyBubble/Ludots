using System;
using System.Diagnostics;
using Ludots.Core.Presentation.Terrain;

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
            var policy = new GroundSurfaceQueryPolicy(layerId: 0, agentMask: uint.MaxValue);

            var x = new float[samplesPerFrame];
            var z = new float[samplesPerFrame];
            FillSamples(x, z, surfaceColumns, surfaceRows, cellSizeCm);

            return RunBenchmark(asset, terrain: null, x, z, in policy, frames, warmupFrames);
        }

        public static StructureGroundingBenchmarkResult RunNonIdealBenchmark(
            int samplesPerFrame,
            int frames,
            int warmupFrames = 4,
            int chunkColumns = 10,
            int chunkRows = 6,
            int chunkSizeCm = 1000)
        {
            if (samplesPerFrame <= 0) throw new ArgumentOutOfRangeException(nameof(samplesPerFrame));
            if (frames <= 0) throw new ArgumentOutOfRangeException(nameof(frames));
            if (warmupFrames < 0) throw new ArgumentOutOfRangeException(nameof(warmupFrames));

            StructureCollisionAsset asset = StructureCollisionAssetBuilder.CreateNonIdealBenchmarkAsset(chunkColumns, chunkRows, chunkSizeCm);
            var policy = new GroundSurfaceQueryPolicy(
                layerId: -1,
                agentMask: uint.MaxValue,
                minHeightCm: -100f,
                maxHeightCm: 800f);

            var x = new float[samplesPerFrame];
            var z = new float[samplesPerFrame];
            FillNonIdealSamples(x, z, chunkColumns, chunkRows, chunkSizeCm);

            return RunBenchmark(asset, new FlatVisualHeightmap(), x, z, in policy, frames, warmupFrames);
        }

        private static StructureGroundingBenchmarkResult RunBenchmark(
            StructureCollisionAsset asset,
            IVisualHeightmap? terrain,
            float[] x,
            float[] z,
            in GroundSurfaceQueryPolicy policy,
            int frames,
            int warmupFrames)
        {
            var runtime = new StructureCollisionRuntimeState(asset);
            var sampler = new GroundSurfaceSampler(terrain, asset, runtime);
            var diagnostics = new StructureGroundingDiagnostics();
            int samplesPerFrame = x.Length;

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

        private static void FillNonIdealSamples(float[] x, float[] z, int chunkColumns, int chunkRows, int chunkSizeCm)
        {
            int worldWidth = checked(chunkColumns * chunkSizeCm);
            int worldHeight = checked(chunkRows * chunkSizeCm);
            for (int i = 0; i < x.Length; i++)
            {
                float offset = (i % 97) * 8f;
                switch (i % 5)
                {
                    case 0:
                        x[i] = MathF.Min(worldWidth - 700f, 3000f + offset);
                        z[i] = 1200f;
                        break;
                    case 1:
                        x[i] = MathF.Min(worldWidth - 700f, 5600f + offset);
                        z[i] = 1200f;
                        break;
                    case 2:
                        x[i] = MathF.Min(worldWidth - 900f, 2600f + offset);
                        z[i] = 2800f;
                        break;
                    case 3:
                        x[i] = 900f;
                        z[i] = MathF.Min(worldHeight - 700f, 700f + offset);
                        break;
                    default:
                        x[i] = MathF.Min(worldWidth - 700f, 3300f + offset);
                        z[i] = 1450f;
                        break;
                }
            }
        }
    }
}
