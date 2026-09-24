using System;
using System.Collections.Generic;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// Headless Stopwatch evidence for region-volume evaluation on a tens-of-km map:
    /// 320 city rings and 10K units. Prints the partition-filtered tick against a
    /// full precise pass over every unit, plus a small fight and an all-mover tick.
    /// The all-mover tick pairs each trip with rings along that trip, and must stay
    /// well under the full precise pass.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class RegionVolumeBroadphaseBenchmarkTests
    {
        private const int RingCount = 320;
        private const int UnitCount = 10_000;
        private const int FightMovers = 200;
        private const int RadiusCm = 2400;
        private const int CellCm = 100;
        private const int MapOriginCm = -2_000_000;
        private const int MapSpanCm = 4_000_000;
        private const int WarmupTicks = 8;
        private const int MeasuredTicks = 30;
        private const int FightStepCm = 1_500;

        [Test]
        public void HeadlessBenchmark_320Rings_10kUnits_PrintsBroadphaseAgainstFullScan()
        {
            using World world = World.Create();
            var partition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 64);
            var spec = new WorldSizeSpec(new WorldAabbCm(MapOriginCm, MapOriginCm, MapSpanCm, MapSpanCm), CellCm);
            var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));
            var sessions = new MapSessionManager();
            var session = sessions.CreateSession(new MapId("bench_region"), new MapConfig { Id = "bench_region" });
            var system = new RegionVolumeTriggerSystem(
                world,
                () => sessions,
                new TriggerManager(),
                () => new ScriptContext(),
                spatial);
            system.Initialize();

            var rings = new (RegionVolumeShape Shape, Fix64Vec2 Anchor)[RingCount];
            var cells = new Dictionary<Entity, (int X, int Y)>(UnitCount + RingCount);
            int columns = 20;
            int rows = 16;
            int spacingX = MapSpanCm / columns;
            int spacingY = MapSpanCm / rows;
            var shape = new RegionVolumeShape
            {
                Kind = RegionVolumeShapeKind.Circle,
                Radius = Fix64.FromInt(RadiusCm),
            };
            for (int i = 0; i < RingCount; i++)
            {
                int x = MapOriginCm + (spacingX / 2) + (i % columns) * spacingX;
                int y = MapOriginCm + (spacingY / 2) + (i / columns) * spacingY;
                var anchor = Fix64Vec2.FromInt(x, y);
                rings[i] = (shape, anchor);
                Place(world, partition, cells, world.Create(
                    new MapEntity { MapId = session.MapId },
                    new WorldPositionCm { Value = anchor },
                    new RegionVolumeCm { VolumeKey = "ring_" + i, Shape = shape }));
            }

            var units = new Entity[UnitCount];
            for (int i = 0; i < UnitCount; i++)
            {
                int x = i == 0 ? rings[0].Anchor.X.RoundToInt() : (i * 37) % MapSpanCm + MapOriginCm;
                int y = i == 0 ? rings[0].Anchor.Y.RoundToInt() : (i * 53) % MapSpanCm + MapOriginCm;
                var position = Fix64Vec2.FromInt(x, y);
                units[i] = world.Create(
                    new MapEntity { MapId = session.MapId },
                    new WorldPositionCm { Value = position },
                    new PreviousWorldPositionCm { Value = position });
                Place(world, partition, cells, units[i]);
            }

            double[] stationary = MeasureSystem(system, WarmupTicks, MeasuredTicks, out long stationaryAlloc);
            double[] fullScan = MeasureFullScan(world, rings, WarmupTicks, MeasuredTicks, out int inside);
            double[] fight = MeasureSystem(system, WarmupTicks, MeasuredTicks, out long fightAlloc, () =>
                ShiftMovers(world, partition, cells, units, FightMovers, FightStepCm));
            double[] allMovers = MeasureSystem(system, WarmupTicks, MeasuredTicks, out long allAlloc, () =>
                ShiftMovers(world, partition, cells, units, UnitCount, FightStepCm));

            Array.Sort(stationary);
            Array.Sort(fullScan);
            Array.Sort(fight);
            Array.Sort(allMovers);
            double stationaryMedian = stationary[MeasuredTicks / 2];
            double fullMedian = fullScan[MeasuredTicks / 2];
            string report =
                $"RegionVolume 320 rings x 10K units on a 40km map: " +
                $"broadphase stationary median={stationaryMedian:F4}ms p95={stationary[(int)(MeasuredTicks * 0.95)]:F4}ms alloc/tick={stationaryAlloc / MeasuredTicks}; " +
                $"full precise median={fullMedian:F4}ms p95={fullScan[(int)(MeasuredTicks * 0.95)]:F4}ms inside={inside}; " +
                $"200 movers median={fight[MeasuredTicks / 2]:F4}ms p95={fight[(int)(MeasuredTicks * 0.95)]:F4}ms alloc/tick={fightAlloc / MeasuredTicks}; " +
                $"all movers median={allMovers[MeasuredTicks / 2]:F4}ms p95={allMovers[(int)(MeasuredTicks * 0.95)]:F4}ms alloc/tick={allAlloc / MeasuredTicks}";
            TestContext.Out.WriteLine(report);
            Console.WriteLine(report);

            Assert.That(inside, Is.GreaterThan(0), "The map must place someone inside a ring, or the precise loop can be deleted by the compiler.");
            Assert.That(stationaryAlloc, Is.EqualTo(0),
                "A tick where nobody enters or leaves must not allocate.");
            Assert.That(stationaryMedian * 2, Is.LessThan(fullMedian),
                "Standing units must be cheaper through the partition than a precise test of every unit against every ring.");
            Assert.That(allMovers[MeasuredTicks / 2] * 4, Is.LessThan(fullMedian),
                "When every unit moves, pairing each trip with the rings along its path must stay well under a precise test of every unit against every ring.");
        }

        private static double[] MeasureSystem(
            RegionVolumeTriggerSystem system,
            int warmup,
            int measured,
            out long allocated,
            Action? beforeTick = null)
        {
            for (int tick = 0; tick < warmup; tick++)
            {
                beforeTick?.Invoke();
                system.Update(0f);
            }

            var samples = new double[measured];
            allocated = 0;
            for (int tick = 0; tick < measured; tick++)
            {
                beforeTick?.Invoke();
                long before = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                system.Update(0f);
                samples[tick] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            return samples;
        }

        private static double[] MeasureFullScan(
            World world,
            (RegionVolumeShape Shape, Fix64Vec2 Anchor)[] rings,
            int warmup,
            int measured,
            out int inside)
        {
            var query = new QueryDescription()
                .WithAll<MapEntity, WorldPositionCm>()
                .WithNone<RegionVolumeCm>();
            var positions = new List<Fix64Vec2>(UnitCount);
            inside = 0;
            for (int tick = 0; tick < warmup; tick++)
            {
                inside = ScanOnce(world, in query, rings, positions);
            }

            var samples = new double[measured];
            for (int tick = 0; tick < measured; tick++)
            {
                long start = Stopwatch.GetTimestamp();
                inside = ScanOnce(world, in query, rings, positions);
                samples[tick] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }

            return samples;
        }

        private static int ScanOnce(
            World world,
            in QueryDescription query,
            (RegionVolumeShape Shape, Fix64Vec2 Anchor)[] rings,
            List<Fix64Vec2> positions)
        {
            positions.Clear();
            foreach (ref var chunk in world.Query(in query))
            {
                var span = chunk.GetSpan<WorldPositionCm>();
                foreach (var index in chunk)
                {
                    positions.Add(span[index].Value);
                }
            }

            int inside = 0;
            for (int ring = 0; ring < rings.Length; ring++)
            {
                RegionVolumeShape shape = rings[ring].Shape;
                Fix64Vec2 anchor = rings[ring].Anchor;
                for (int unit = 0; unit < positions.Count; unit++)
                {
                    if (shape.Contains(positions[unit], anchor))
                    {
                        inside++;
                    }
                }
            }

            return inside;
        }

        private static void ShiftMovers(
            World world,
            ChunkedGridSpatialPartitionWorld partition,
            Dictionary<Entity, (int X, int Y)> cells,
            Entity[] units,
            int count,
            int stepCm)
        {
            for (int i = 0; i < count; i++)
            {
                Entity entity = units[i];
                Fix64Vec2 current = world.Get<WorldPositionCm>(entity).Value;
                world.Set(entity, new PreviousWorldPositionCm { Value = current });
                int x = current.X.RoundToInt() + stepCm;
                if (x >= MapOriginCm + MapSpanCm)
                {
                    x = MapOriginCm + (x - (MapOriginCm + MapSpanCm));
                }

                world.Set(entity, new WorldPositionCm { Value = Fix64Vec2.FromInt(x, current.Y.RoundToInt()) });
                MoveCell(world, partition, cells, entity);
            }
        }

        private static void Place(
            World world,
            ChunkedGridSpatialPartitionWorld partition,
            Dictionary<Entity, (int X, int Y)> cells,
            Entity entity)
        {
            WorldCmInt2 cm = world.Get<WorldPositionCm>(entity).Value.ToWorldCmInt2();
            int cellX = MathUtil.FloorDiv(cm.X, CellCm);
            int cellY = MathUtil.FloorDiv(cm.Y, CellCm);
            partition.Add(entity, cellX, cellY);
            cells[entity] = (cellX, cellY);
            world.Add(entity, new SpatialCellRef
            {
                CellX = cellX,
                CellY = cellY,
                State = SpatialMembershipState.Active,
            });
        }

        private static void MoveCell(
            World world,
            ChunkedGridSpatialPartitionWorld partition,
            Dictionary<Entity, (int X, int Y)> cells,
            Entity entity)
        {
            WorldCmInt2 cm = world.Get<WorldPositionCm>(entity).Value.ToWorldCmInt2();
            int cellX = MathUtil.FloorDiv(cm.X, CellCm);
            int cellY = MathUtil.FloorDiv(cm.Y, CellCm);
            (int X, int Y) old = cells[entity];
            if (old.X != cellX || old.Y != cellY)
            {
                partition.Remove(entity, old.X, old.Y);
                partition.Add(entity, cellX, cellY);
                cells[entity] = (cellX, cellY);
            }

            world.Set(entity, new SpatialCellRef
            {
                CellX = cellX,
                CellY = cellY,
                State = SpatialMembershipState.Active,
            });
        }
    }
}
