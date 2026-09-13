using System;
using System.Diagnostics;
using NUnit.Framework;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;

namespace GasTests
{
    /// <summary>
    /// Headless Stopwatch evidence for the SpatialPartitionUpdateSystem movement gate (#1506):
    /// prints per-tick medians for 10K tracked entities in a fully static phase and an
    /// all-movers phase; asserts the static phase performs zero partition ops and zero allocations.
    /// </summary>
    [TestFixture]
    public sealed class SpatialPartitionUpdateBenchmarkTests
    {
        private const int EntityCount = 10_000;
        private const int WarmupTicks = 16;
        private const int MeasuredTicks = 200;
        private const int MoveStepCm = 200;

        private sealed class CountingPartitionWorld : ISpatialPartitionWorld
        {
            private readonly ISpatialPartitionWorld _inner;
            public int AddCount;
            public int RemoveCount;

            public CountingPartitionWorld(ISpatialPartitionWorld inner)
            {
                _inner = inner;
            }

            public void Add(Entity entity, int cellX, int cellY)
            {
                AddCount++;
                _inner.Add(entity, cellX, cellY);
            }

            public void Remove(Entity entity, int cellX, int cellY)
            {
                RemoveCount++;
                _inner.Remove(entity, cellX, cellY);
            }

            public int Query(in IntRect cellRect, Span<Entity> buffer, out int dropped)
            {
                return _inner.Query(in cellRect, buffer, out dropped);
            }

            public void Clear()
            {
                _inner.Clear();
            }
        }

        [Test]
        public void HeadlessBenchmark_SpatialPartitionUpdate_10kEntities_PrintsStaticAndAllMoverMedians()
        {
            using var world = World.Create();
            var partition = new CountingPartitionWorld(new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 64));
            var spec = new WorldSizeSpec(new WorldAabbCm(-200_000, -200_000, 400_000, 400_000), gridCellSizeCm: 100);
            using var save = new SavePreviousWorldPositionSystem(world);
            using var spatial = new SpatialPartitionUpdateSystem(world, partition, spec);

            for (int i = 0; i < EntityCount; i++)
            {
                int x = (i * 37) % 100_000 - 50_000;
                int y = (i * 53) % 100_000 - 50_000;
                world.Create(
                    new WorldPositionCm { Value = Fix64Vec2.FromInt(x, y) },
                    new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(x, y) });
            }

            save.Update(0f);
            spatial.Update(0f);
            Assert.That(partition.AddCount, Is.EqualTo(EntityCount));

            double[] staticMs = MeasurePhase(world, save, spatial, movers: false);
            Assert.That(partition.AddCount, Is.EqualTo(EntityCount), "static ticks must perform zero partition ops");
            Assert.That(partition.RemoveCount, Is.EqualTo(0), "static ticks must perform zero partition ops");

            double[] movingMs = MeasurePhase(world, save, spatial, movers: true);
            Assert.That(partition.RemoveCount, Is.GreaterThan(0), "mover ticks must still cross cells");

            Array.Sort(staticMs);
            Array.Sort(movingMs);
            TestContext.Out.WriteLine(
                $"SpatialPartitionUpdateSystem headless 10K: static median={staticMs[MeasuredTicks / 2]:F4}ms p95={staticMs[(int)(MeasuredTicks * 0.95)]:F4}ms; all-movers median={movingMs[MeasuredTicks / 2]:F4}ms p95={movingMs[(int)(MeasuredTicks * 0.95)]:F4}ms");
        }

        private static double[] MeasurePhase(
            World world,
            SavePreviousWorldPositionSystem save,
            SpatialPartitionUpdateSystem spatial,
            bool movers)
        {
            for (int tick = 0; tick < WarmupTicks; tick++)
            {
                save.Update(0f);
                if (movers)
                {
                    AdvanceMovers(world);
                }

                spatial.Update(0f);
            }

            var samples = new double[MeasuredTicks];
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < MeasuredTicks; tick++)
            {
                save.Update(0f);
                if (movers)
                {
                    AdvanceMovers(world);
                }

                long start = Stopwatch.GetTimestamp();
                spatial.Update(0f);
                samples[tick] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }

            if (!movers)
            {
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                Assert.That(allocated, Is.Zero, "static-phase spatial update must not allocate");
            }

            return samples;
        }

        private static void AdvanceMovers(World world)
        {
            var query = new QueryDescription().WithAll<WorldPositionCm>();
            foreach (ref var chunk in world.Query(in query))
            {
                var positions = chunk.GetSpan<WorldPositionCm>();
                foreach (var index in chunk)
                {
                    positions[index].Value = positions[index].Value + Fix64Vec2.FromInt(MoveStepCm, 0);
                }
            }
        }
    }
}
