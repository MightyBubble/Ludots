using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Vision;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    [Category("benchmark")]
    public sealed class FogBenchmarkTests
    {
        [Test]
        public void Benchmark_FogField_DenseOneMillionCellsCopyIsZeroAlloc()
        {
            const int side = 1024;
            const int cellCount = side * side;
            const int iterations = 8;
            FogField field = CreateDenseField(side, out _);
            var states = new FogCellState[cellCount];

            Assert.That(field.NonDefaultCount, Is.EqualTo(cellCount));
            field.CopyCells(states);
            field.ClearDirty();
            WarmUpGC();

            long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            int copied = 0;
            for (int i = 0; i < iterations; i++)
            {
                copied += field.CopyCells(states);
            }

            long stop = Stopwatch.GetTimestamp();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
            double elapsedSeconds = ElapsedSeconds(start, stop);
            double cellsPerSecond = copied / elapsedSeconds;
            double fullFieldHz = iterations / elapsedSeconds;

            PrintBenchmark(
                "FogField.CopyCells.Dense1M",
                ("Cells", cellCount),
                ("Chunks", field.ChunkCount),
                ("Iterations", iterations),
                ("CopiedCells", copied),
                ("TotalMs", ElapsedMs(start, stop)),
                ("CellsPerSecond", cellsPerSecond),
                ("FullFieldHz", fullFieldHz),
                ("AllocatedBytes", allocated));

            Assert.That(copied, Is.EqualTo(cellCount * iterations));
            Assert.That(allocated, Is.EqualTo(0));
            Assert.That(cellsPerSecond, Is.GreaterThan(1_000_000d));
            Assert.That(fullFieldHz, Is.GreaterThan(10d));
        }

        [Test]
        public void Benchmark_GlobalFieldProjection_QuarterMillionCellsIsZeroAlloc()
        {
            const int side = 512;
            const int cellCount = side * side;
            const int frames = 20;
            FogField field = CreateDenseField(side, out FogLayerDefinition layer);
            var store = new FogFieldStore(initialCapacity: 2, chunkSizeCells: 16);
            FogField stored = store.GetOrCreate(field.ScopeKeyId, in layer);
            CopyFieldInto(stored, field, cellCount);

            var buffer = new GlobalFieldVisualBuffer(recordCapacity: 4, cellCapacity: cellCount, dirtyRectCapacity: 4);
            var projector = new FogGlobalFieldVisualProjector();
            for (int i = 0; i < 3; i++)
            {
                buffer.BeginFrame();
                projector.Project(store, buffer);
            }

            WarmUpGC();

            long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            int projectedCells = 0;
            for (int frame = 0; frame < frames; frame++)
            {
                buffer.BeginFrame();
                projector.Project(store, buffer);
                projectedCells += buffer.CellCount;
            }

            long stop = Stopwatch.GetTimestamp();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
            double elapsedSeconds = ElapsedSeconds(start, stop);
            double cellsPerSecond = projectedCells / elapsedSeconds;
            double projectionHz = frames / elapsedSeconds;

            PrintBenchmark(
                "FogGlobalFieldVisualProjector.Project.262k",
                ("Fields", projector.LastProjectedFieldCount),
                ("CellsPerFrame", buffer.CellCount),
                ("Frames", frames),
                ("ProjectedCells", projectedCells),
                ("TotalMs", ElapsedMs(start, stop)),
                ("CellsPerSecond", cellsPerSecond),
                ("ProjectionHz", projectionHz),
                ("AllocatedBytes", allocated));

            Assert.That(buffer.ActiveRecordCount, Is.EqualTo(1));
            Assert.That(buffer.CellCount, Is.EqualTo(cellCount));
            Assert.That(projectedCells, Is.EqualTo(cellCount * frames));
            Assert.That(allocated, Is.EqualTo(0));
            Assert.That(cellsPerSecond, Is.GreaterThan(500_000d));
            Assert.That(projectionHz, Is.GreaterThan(30d));
        }

        [Test]
        public void Benchmark_VisionSystem_TwentyFourEmittersAndFiveHundredOccupantsReportsSixtyHzTargetZeroAlloc()
        {
            RunVisionSystemBenchmark(
                emitterCount: 24,
                occupantCount: 512,
                frames: 120,
                name: "VisionSystem.Tick.24x512",
                targetTickHz: 60d,
                minimumCiTickHz: 50d);
        }

        [Test]
        public void Benchmark_VisionSystem_StressSixtyFourEmittersAndOneThousandOccupantsReportsLimitZeroAlloc()
        {
            RunVisionSystemBenchmark(
                emitterCount: 64,
                occupantCount: 1024,
                frames: 60,
                name: "VisionSystem.Tick.64x1024.Stress",
                targetTickHz: 60d,
                minimumCiTickHz: 10d);
        }

        private static void RunVisionSystemBenchmark(
            int emitterCount,
            int occupantCount,
            int frames,
            string name,
            double targetTickHz,
            double minimumCiTickHz)
        {
            using World world = World.Create();
            var session = new GameSession();
            var registry = new FogLayerRegistry();
            FogLayerId layerId = registry.Register("ground", cellSizeCm: 100, updateHz: 60);
            uint layerMask = registry.ToMask(layerId);
            var fields = new FogFieldStore(initialCapacity: 2, chunkSizeCells: 16);
            var knowledge = new KnowledgeProjectionStore(initialCapacity: emitterCount * occupantCount);
            var cellMap = new FogCellMap();
            var resolver = new VisionResolver(registry, fields, elevation: cellMap, occlusion: cellMap);
            var projector = new FogKnowledgeProjector(knowledge, cellMap);
            var system = new VisionSystem(world, session, registry, fields, resolver, projector, knowledge);

            CreateEmitters(world, emitterCount, layerMask);
            CreateOccupants(world, occupantCount, layerMask);
            for (int i = 0; i < 6; i++)
            {
                system.Update(1f / 60f);
            }

            WarmUpGC();

            long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int frame = 0; frame < frames; frame++)
            {
                system.Update(1f / 60f);
            }

            long stop = Stopwatch.GetTimestamp();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
            double elapsedSeconds = ElapsedSeconds(start, stop);
            double tickHz = frames / elapsedSeconds;
            double perTickMs = ElapsedMs(start, stop) / frames;
            double emitterResolvesPerSecond = (emitterCount * frames) / elapsedSeconds;
            double occupantTestsPerSecond = ((double)emitterCount * occupantCount * frames) / elapsedSeconds;

            Assert.That(fields.TryGet(1, layerId, out FogField field), Is.True);
            PrintBenchmark(
                name,
                ("Emitters", emitterCount),
                ("Occupants", occupantCount),
                ("Frames", frames),
                ("FogCells", field.NonDefaultCount),
                ("KnowledgeRecords", knowledge.RecordCount),
                ("TotalMs", ElapsedMs(start, stop)),
                ("PerTickMs", perTickMs),
                ("TickHz", tickHz),
                ("TargetTickHz", targetTickHz),
                ("MinimumCiTickHz", minimumCiTickHz),
                ("EmitterResolvesPerSecond", emitterResolvesPerSecond),
                ("OccupantTestsPerSecond", occupantTestsPerSecond),
                ("AllocatedBytes", allocated));

            Assert.That(field.NonDefaultCount, Is.GreaterThan(0));
            Assert.That(knowledge.RecordCount, Is.GreaterThan(0));
            Assert.That(allocated, Is.EqualTo(0));
            Assert.That(tickHz, Is.GreaterThan(minimumCiTickHz));
        }

        private static FogField CreateDenseField(int side, out FogLayerDefinition layer)
        {
            var registry = new FogLayerRegistry();
            FogLayerId layerId = registry.Register("ground", cellSizeCm: 100, updateHz: 60);
            layer = registry.Get(layerId);
            var field = new FogField(scopeKeyId: 1, in layer, chunkSizeCells: 16, initialChunkCapacity: 16);
            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    CellVisibility visibility = ((x + y) & 3) == 0
                        ? CellVisibility.Visible
                        : CellVisibility.Explored;
                    field.SetVisibility(new FogCell(x, y), visibility);
                }
            }

            return field;
        }

        private static void CopyFieldInto(FogField destination, FogField source, int cellCapacity)
        {
            var cells = new FogCellState[cellCapacity];
            int count = source.CopyCells(cells);
            for (int i = 0; i < count; i++)
            {
                destination.SetVisibility(cells[i].Cell, cells[i].Visibility);
            }

            destination.ClearDirty();
        }

        private static void CreateEmitters(World world, int count, uint layerMask)
        {
            for (int i = 0; i < count; i++)
            {
                int x = ((i & 7) - 4) * 900;
                int y = (((i >> 3) & 7) - 4) * 900;
                world.Create(
                    WorldPositionCm.FromCm(x, y),
                    new VisionEmitterCm
                    {
                        ScopeKeyId = 1,
                        LayerMask = layerMask,
                        Polarity = VisionPolarity.Reveal,
                        Aperture = VisionAperture.Disk(850),
                        DetectionStrength = 1
                    });
            }
        }

        private static void CreateOccupants(World world, int count, uint layerMask)
        {
            for (int i = 0; i < count; i++)
            {
                int x = ((i & 63) - 32) * 140;
                int y = (((i >> 6) & 31) - 16) * 140;
                world.Create(
                    WorldPositionCm.FromCm(x, y),
                    new FogOccupantCm
                    {
                        ExposeLayerMask = layerMask,
                        StealthLevel = 0
                    });
            }
        }

        private static void WarmUpGC()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();
        }

        private static double ElapsedSeconds(long start, long stop)
        {
            double elapsed = Stopwatch.GetElapsedTime(start, stop).TotalSeconds;
            return Math.Max(elapsed, 0.000001d);
        }

        private static double ElapsedMs(long start, long stop)
        {
            return Stopwatch.GetElapsedTime(start, stop).TotalMilliseconds;
        }

        private static void PrintBenchmark(string name, params (string Label, object Value)[] metrics)
        {
            Console.WriteLine($"[Benchmark] {name}:");
            for (int i = 0; i < metrics.Length; i++)
            {
                Console.WriteLine($"  {metrics[i].Label}: {metrics[i].Value}");
            }
        }
    }
}
