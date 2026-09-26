using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace GasTests;

/// <summary>
/// 16km map radius-query cost for 320 pylon circles, and for 320 non-overlapping
/// city circles placed by hard-core Poisson (hundreds of meters apart, tens of meters across).
/// Prints one full pulse and the busiest tick after EffectLifetimeSystem's first-tick spread.
/// </summary>
[TestFixture]
public sealed class PylonAuraQueryBenchmarkTests
{
    private const int MapExtentCm = 1_600_000;
    private const int CellSizeCm = 100;
    private const int ChunkSizeCells = 64;
    private const int AuraCount = 320;
    private const int AuraColumns = 20;
    private const int AuraRows = 16;
    private const int RadiusCm = 650;
    private const int PeriodTicks = 30;
    private const int BackgroundCount = 2_000;
    private const int StackedCrowd = 200;
    private const int WarmupTicks = 8;
    private const int MeasuredTicks = 40;
    private const int TemplateId = 1;
    private const int CityCount = 320;
    private const int CityRadiusCm = 4_000;
    private const int CityMinSeparationCm = 40_000;
    private const int CityPlacementSeed = 3202409;

    private static readonly (int X, int Y)[] LocalOffsets =
    {
        (200, 0),
        (0, 200),
        (-200, 0),
        (0, -200),
        (300, 300),
        (-300, 200),
        (200, -400),
        (-400, -100),
    };

    private static readonly (int X, int Y)[] CityOccupantOffsets =
    {
        (1_200, 0),
        (-1_200, 0),
        (0, 1_200),
        (0, -1_200),
        (2_200, 800),
        (-2_200, 800),
        (2_200, -800),
        (-2_200, -800),
        (800, 2_800),
        (-800, 2_800),
        (800, -2_800),
        (-800, -2_800),
        (3_000, 1_500),
        (-3_000, 1_500),
        (1_500, -3_000),
    };

    [Test]
    public void SixteenKmMap_320PylonAuras_PrintsRadiusQueryCost()
    {
        using World world = World.Create();
        var spec = new WorldSizeSpec(new WorldAabbCm(0, 0, MapExtentCm, MapExtentCm), CellSizeCm);
        var partition = new ChunkedGridSpatialPartitionWorld(ChunkSizeCells);
        var service = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));
        service.SetPositionProvider(entity => world.Get<WorldPositionCm>(entity).Value.ToWorldCmInt2());

        Aura[] auras = PlaceSpreadAuras(world, partition);
        int background = PlaceBackground(world, partition, auras, RadiusCm);
        var buffer = new Entity[256];
        var centers = new WorldCmInt2[auras.Length];
        for (int i = 0; i < auras.Length; i++)
        {
            centers[i] = auras[i].Center;
            SpatialQueryResult result = service.QueryRadius(centers[i], RadiusCm, buffer);
            Assert.That(result.Count, Is.EqualTo(1 + LocalOffsets.Length), $"aura {i} hit count");
            Assert.That(result.Dropped, Is.EqualTo(0), $"aura {i} dropped");
        }

        int[] tickCounts = new int[PeriodTicks + 1];
        for (int i = 0; i < auras.Length; i++)
        {
            tickCounts[FirstPeriodTick(auras[i].Pylon)]++;
        }

        int busiestTick = 1;
        int busiestCount = tickCounts[1];
        for (int tick = 2; tick <= PeriodTicks; tick++)
        {
            if (tickCounts[tick] > busiestCount)
            {
                busiestCount = tickCounts[tick];
                busiestTick = tick;
            }
        }

        var busiestCenters = new WorldCmInt2[busiestCount];
        int filled = 0;
        for (int i = 0; i < auras.Length; i++)
        {
            if (FirstPeriodTick(auras[i].Pylon) != busiestTick)
            {
                continue;
            }

            busiestCenters[filled++] = auras[i].Center;
        }

        Assert.That(filled, Is.EqualTo(busiestCount));

        Measure pulse = MeasureQueries(service, centers, buffer, RadiusCm);
        Measure busiest = MeasureQueries(service, busiestCenters, buffer, RadiusCm);
        Measure stacked = MeasureStackedCrowd(buffer);

        TestContext.Out.WriteLine(
            $"16km {AuraCount} auras radius={RadiusCm}cm cell={CellSizeCm}cm chunk={ChunkSizeCells} locals={LocalOffsets.Length} background={background}");
        TestContext.Out.WriteLine(
            $"full pulse {AuraCount} queries: median={pulse.MedianMs:F4}ms p95={pulse.P95Ms:F4}ms alloc/iter={pulse.AllocatedBytesPerIteration}");
        TestContext.Out.WriteLine(
            $"busiest tick {busiestCount}/{AuraCount} (period {PeriodTicks}, tick {busiestTick}): median={busiest.MedianMs:F4}ms p95={busiest.P95Ms:F4}ms alloc/iter={busiest.AllocatedBytesPerIteration}");
        TestContext.Out.WriteLine(
            $"stacked {AuraCount} queries over {StackedCrowd} entities: median={stacked.MedianMs:F4}ms p95={stacked.P95Ms:F4}ms alloc/iter={stacked.AllocatedBytesPerIteration}");

        Assert.That(pulse.HitSum, Is.EqualTo((1 + LocalOffsets.Length) * AuraCount * MeasuredTicks));
        Assert.That(busiest.HitSum, Is.EqualTo((1 + LocalOffsets.Length) * busiestCount * MeasuredTicks));
        Assert.That(stacked.HitSum, Is.EqualTo(StackedCrowd * AuraCount * MeasuredTicks));
    }

    [Test]
    public void SixteenKmMap_320PoissonCities_PrintsRadiusQueryCost()
    {
        using World world = World.Create();
        var spec = new WorldSizeSpec(new WorldAabbCm(0, 0, MapExtentCm, MapExtentCm), CellSizeCm);
        var partition = new ChunkedGridSpatialPartitionWorld(ChunkSizeCells);
        var service = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));
        service.SetPositionProvider(entity => world.Get<WorldPositionCm>(entity).Value.ToWorldCmInt2());

        Aura[] cities = PlacePoissonCities(world, partition, out int placementAttempts);
        int nearestMinCm = NearestNeighborMinCm(cities);
        int nearestMedianCm = NearestNeighborMedianCm(cities);
        Assert.That(nearestMinCm, Is.GreaterThanOrEqualTo(CityMinSeparationCm));
        Assert.That(nearestMinCm, Is.GreaterThan(CityRadiusCm * 2));

        int background = PlaceBackground(world, partition, cities, CityRadiusCm);
        var buffer = new Entity[256];
        var centers = new WorldCmInt2[cities.Length];
        int occupants = 1 + CityOccupantOffsets.Length;
        for (int i = 0; i < cities.Length; i++)
        {
            centers[i] = cities[i].Center;
            SpatialQueryResult result = service.QueryRadius(centers[i], CityRadiusCm, buffer);
            Assert.That(result.Count, Is.EqualTo(occupants), $"city {i} hit count");
            Assert.That(result.Dropped, Is.EqualTo(0), $"city {i} dropped");
        }

        (int busiestTick, WorldCmInt2[] busiestCenters) = BusiestTick(cities);
        Measure pulse = MeasureQueries(service, centers, buffer, CityRadiusCm);
        Measure busiest = MeasureQueries(service, busiestCenters, buffer, CityRadiusCm);

        TestContext.Out.WriteLine(
            $"16km hard-core Poisson {CityCount} cities radius={CityRadiusCm}cm minSeparation={CityMinSeparationCm}cm nearestMin={nearestMinCm}cm nearestMedian={nearestMedianCm}cm occupants={occupants} background={background} placementAttempts={placementAttempts}");
        TestContext.Out.WriteLine(
            $"full pulse {CityCount} queries: median={pulse.MedianMs:F4}ms p95={pulse.P95Ms:F4}ms alloc/iter={pulse.AllocatedBytesPerIteration}");
        TestContext.Out.WriteLine(
            $"busiest tick {busiestCenters.Length}/{CityCount} (period {PeriodTicks}, tick {busiestTick}): median={busiest.MedianMs:F4}ms p95={busiest.P95Ms:F4}ms alloc/iter={busiest.AllocatedBytesPerIteration}");

        Assert.That(pulse.HitSum, Is.EqualTo(occupants * CityCount * MeasuredTicks));
        Assert.That(busiest.HitSum, Is.EqualTo(occupants * busiestCenters.Length * MeasuredTicks));
    }

    private static Aura[] PlaceSpreadAuras(World world, ChunkedGridSpatialPartitionWorld partition)
    {
        int stepX = MapExtentCm / AuraColumns;
        int stepY = MapExtentCm / AuraRows;
        var auras = new Aura[AuraCount];
        int index = 0;
        for (int row = 0; row < AuraRows; row++)
        {
            for (int column = 0; column < AuraColumns; column++)
            {
                int x = column * stepX + stepX / 2;
                int y = row * stepY + stepY / 2;
                Entity pylon = AddEntity(world, partition, x, y);
                for (int local = 0; local < LocalOffsets.Length; local++)
                {
                    AddEntity(world, partition, x + LocalOffsets[local].X, y + LocalOffsets[local].Y);
                }

                auras[index++] = new Aura(new WorldCmInt2(x, y), pylon);
            }
        }

        return auras;
    }

    private static Measure MeasureStackedCrowd(Entity[] buffer)
    {
        using World world = World.Create();
        var spec = new WorldSizeSpec(new WorldAabbCm(0, 0, MapExtentCm, MapExtentCm), CellSizeCm);
        var partition = new ChunkedGridSpatialPartitionWorld(ChunkSizeCells);
        var service = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));
        service.SetPositionProvider(entity => world.Get<WorldPositionCm>(entity).Value.ToWorldCmInt2());

        const int origin = MapExtentCm / 2;
        for (int i = 0; i < StackedCrowd; i++)
        {
            int x = origin + (i % 14) * 40 - 280;
            int y = origin + (i / 14) * 40 - 280;
            AddEntity(world, partition, x, y);
        }

        var center = new WorldCmInt2(origin, origin);
        SpatialQueryResult probe = service.QueryRadius(center, RadiusCm, buffer);
        Assert.That(probe.Count, Is.EqualTo(StackedCrowd));
        Assert.That(probe.Dropped, Is.EqualTo(0));

        var centers = new WorldCmInt2[AuraCount];
        Array.Fill(centers, center);
        return MeasureQueries(service, centers, buffer, RadiusCm);
    }

    private static Measure MeasureQueries(SpatialQueryService service, WorldCmInt2[] centers, Entity[] buffer, int radiusCm)
    {
        for (int tick = 0; tick < WarmupTicks; tick++)
        {
            QueryAll(service, centers, buffer, radiusCm);
        }

        var samples = new double[MeasuredTicks];
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        int hitSum = 0;
        for (int tick = 0; tick < MeasuredTicks; tick++)
        {
            long start = Stopwatch.GetTimestamp();
            hitSum += QueryAll(service, centers, buffer, radiusCm);
            samples[tick] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Array.Sort(samples);
        return new Measure(
            samples[MeasuredTicks / 2],
            samples[(int)(MeasuredTicks * 0.95)],
            allocated / MeasuredTicks,
            hitSum);
    }

    private static int QueryAll(SpatialQueryService service, WorldCmInt2[] centers, Entity[] buffer, int radiusCm)
    {
        int hits = 0;
        for (int i = 0; i < centers.Length; i++)
        {
            SpatialQueryResult result = service.QueryRadius(centers[i], radiusCm, buffer);
            if (result.Dropped != 0)
            {
                throw new InvalidOperationException(
                    $"Aura query dropped {result.Dropped} entities at ({centers[i].X},{centers[i].Y}).");
            }

            hits += result.Count;
        }

        return hits;
    }

    private static int PlaceBackground(World world, ChunkedGridSpatialPartitionWorld partition, Aura[] auras, int radiusCm)
    {
        int placed = 0;
        int columns = 50;
        int step = MapExtentCm / columns;
        for (int i = 0; i < BackgroundCount; i++)
        {
            int x = (i % columns) * step + step / 4;
            int y = (i / columns) * step + step / 4;
            if (x < 0 || y < 0 || x >= MapExtentCm || y >= MapExtentCm)
            {
                continue;
            }

            if (NearAnyAura(auras, x, y, radiusCm))
            {
                continue;
            }

            AddEntity(world, partition, x, y);
            placed++;
        }

        return placed;
    }

    private static Aura[] PlacePoissonCities(World world, ChunkedGridSpatialPartitionWorld partition, out int placementAttempts)
    {
        var rng = new Random(CityPlacementSeed);
        var accepted = new WorldCmInt2[CityCount];
        long minSeparationSq = (long)CityMinSeparationCm * CityMinSeparationCm;
        int count = 0;
        placementAttempts = 0;
        while (count < CityCount)
        {
            placementAttempts++;
            if (placementAttempts > 200_000)
            {
                throw new InvalidOperationException(
                    $"Hard-core Poisson placement stopped at {count}/{CityCount} cities after {placementAttempts} attempts.");
            }

            int x = rng.Next(CityRadiusCm, MapExtentCm - CityRadiusCm);
            int y = rng.Next(CityRadiusCm, MapExtentCm - CityRadiusCm);
            bool separated = true;
            for (int i = 0; i < count; i++)
            {
                long dx = x - accepted[i].X;
                long dy = y - accepted[i].Y;
                if (dx * dx + dy * dy < minSeparationSq)
                {
                    separated = false;
                    break;
                }
            }

            if (!separated)
            {
                continue;
            }

            accepted[count++] = new WorldCmInt2(x, y);
        }

        var cities = new Aura[CityCount];
        for (int i = 0; i < CityCount; i++)
        {
            int x = accepted[i].X;
            int y = accepted[i].Y;
            Entity city = AddEntity(world, partition, x, y);
            for (int occupant = 0; occupant < CityOccupantOffsets.Length; occupant++)
            {
                AddEntity(
                    world,
                    partition,
                    x + CityOccupantOffsets[occupant].X,
                    y + CityOccupantOffsets[occupant].Y);
            }

            cities[i] = new Aura(accepted[i], city);
        }

        return cities;
    }

    private static (int Tick, WorldCmInt2[] Centers) BusiestTick(Aura[] auras)
    {
        int[] tickCounts = new int[PeriodTicks + 1];
        for (int i = 0; i < auras.Length; i++)
        {
            tickCounts[FirstPeriodTick(auras[i].Pylon)]++;
        }

        int busiestTick = 1;
        int busiestCount = tickCounts[1];
        for (int tick = 2; tick <= PeriodTicks; tick++)
        {
            if (tickCounts[tick] > busiestCount)
            {
                busiestCount = tickCounts[tick];
                busiestTick = tick;
            }
        }

        var centers = new WorldCmInt2[busiestCount];
        int filled = 0;
        for (int i = 0; i < auras.Length; i++)
        {
            if (FirstPeriodTick(auras[i].Pylon) != busiestTick)
            {
                continue;
            }

            centers[filled++] = auras[i].Center;
        }

        if (filled != busiestCount)
        {
            throw new InvalidOperationException($"Busiest tick filled {filled}, expected {busiestCount}.");
        }

        return (busiestTick, centers);
    }

    private static int NearestNeighborMinCm(Aura[] cities)
    {
        int min = int.MaxValue;
        for (int i = 0; i < cities.Length; i++)
        {
            int nearest = NearestOtherCm(cities, i);
            if (nearest < min)
            {
                min = nearest;
            }
        }

        return min;
    }

    private static int NearestNeighborMedianCm(Aura[] cities)
    {
        var nearest = new int[cities.Length];
        for (int i = 0; i < cities.Length; i++)
        {
            nearest[i] = NearestOtherCm(cities, i);
        }

        Array.Sort(nearest);
        return nearest[cities.Length / 2];
    }

    private static int NearestOtherCm(Aura[] cities, int index)
    {
        long bestSq = long.MaxValue;
        for (int other = 0; other < cities.Length; other++)
        {
            if (other == index)
            {
                continue;
            }

            long dx = cities[index].Center.X - cities[other].Center.X;
            long dy = cities[index].Center.Y - cities[other].Center.Y;
            long distSq = dx * dx + dy * dy;
            if (distSq < bestSq)
            {
                bestSq = distSq;
            }
        }

        return IntegerSqrt(bestSq);
    }

    private static int IntegerSqrt(long value)
    {
        int root = (int)Math.Sqrt(value);
        while ((long)(root + 1) * (root + 1) <= value)
        {
            root++;
        }

        while ((long)root * root > value)
        {
            root--;
        }

        return root;
    }

    private static bool NearAnyAura(Aura[] auras, int x, int y, int radiusCm)
    {
        long limit = (long)radiusCm * radiusCm;
        for (int i = 0; i < auras.Length; i++)
        {
            long dx = x - auras[i].Center.X;
            long dy = y - auras[i].Center.Y;
            if (dx * dx + dy * dy <= limit)
            {
                return true;
            }
        }

        return false;
    }

    private static Entity AddEntity(World world, ChunkedGridSpatialPartitionWorld partition, int xCm, int yCm)
    {
        Entity entity = world.Create(new WorldPositionCm { Value = new Fix64Vec2(xCm, yCm) });
        partition.Add(entity, MathUtil.FloorDiv(xCm, CellSizeCm), MathUtil.FloorDiv(yCm, CellSizeCm));
        return entity;
    }

    // Same mix as EffectLifetimeSystem.LifetimeTickJob.ResolveInitialPeriodOffset.
    private static int FirstPeriodTick(Entity pylon)
    {
        var context = new EffectContext
        {
            RootId = pylon.Id,
            Source = pylon,
            Target = pylon,
        };
        uint hash = 2166136261u;
        hash = Mix(hash, context.RootId);
        hash = Mix(hash, TemplateId);
        hash = Mix(hash, PeriodTicks);
        hash = Mix(hash, context.Source.Id);
        hash = Mix(hash, context.Source.Version);
        hash = Mix(hash, context.Target.Id);
        hash = Mix(hash, context.Target.Version);
        hash = Mix(hash, context.TargetContext.Id);
        hash = Mix(hash, context.TargetContext.Version);
        hash ^= hash >> 16;
        return 1 + (int)(hash % (uint)PeriodTicks);
    }

    private static uint Mix(uint hash, int value)
    {
        unchecked
        {
            hash ^= (uint)value;
            return hash * 16777619u;
        }
    }

    private readonly struct Aura
    {
        public Aura(WorldCmInt2 center, Entity pylon)
        {
            Center = center;
            Pylon = pylon;
        }

        public WorldCmInt2 Center { get; }
        public Entity Pylon { get; }
    }

    private readonly struct Measure
    {
        public Measure(double medianMs, double p95Ms, long allocatedBytesPerIteration, int hitSum)
        {
            MedianMs = medianMs;
            P95Ms = p95Ms;
            AllocatedBytesPerIteration = allocatedBytesPerIteration;
            HitSum = hitSum;
        }

        public double MedianMs { get; }
        public double P95Ms { get; }
        public long AllocatedBytesPerIteration { get; }
        public int HitSum { get; }
    }
}
