using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// AttributeAggregatorSystem 稳态预算守卫：
    /// 零脏实体帧聚合成本必须近零（dirty-gate 合同）；
    /// massnav 式错峰到期（每 tick 1/60 实体脏）与全量到期帧给出耗时分布作回归基线。
    /// 计时断言放宽防 CI 抖动，预算本身由分布报告监督。
    /// </summary>
    [TestFixture]
    public class AttributeAggregatorSteadyStateBudgetTests
    {
        private const int EntityCount = 10_000;
        private const int SpreadFiringPerTick = 167;
        private const int ObservationTicks = 120;

        [Test]
        public void SteadyTick_WithoutDirtyEntities_AggregatesNothingNearZeroCost()
        {
            (World world, AttributeAggregatorSystem aggregator, _) = BuildPopulation();
            using (world)
            {
                for (int i = 0; i < 8; i++)
                {
                    aggregator.Update(0.0166f);
                }

                var samples = new double[ObservationTicks];
                for (int tick = 0; tick < ObservationTicks; tick++)
                {
                    long start = Stopwatch.GetTimestamp();
                    aggregator.Update(0.0166f);
                    samples[tick] = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                }

                Array.Sort(samples);
                TestContext.Out.WriteLine(
                    $"Aggregator steady zero-dirty: entities={EntityCount} median={samples[ObservationTicks / 2]:F4}ms p95={samples[(int)(ObservationTicks * 0.95)]:F4}ms max={samples[^1]:F4}ms");
                Assert.That(samples[^1], Is.LessThan(0.3d),
                    $"Zero-dirty steady ticks must aggregate nothing; worst observed {samples[^1]:F4}ms exceeds the 0.3ms budget.");
                Assert.That(CountDirtyTags(world), Is.Zero);
            }
        }

        [Test]
        public void SpreadFiringTicks_ConsumeOnlyMarkedEntities()
        {
            (World world, AttributeAggregatorSystem aggregator, Entity[] entities) = BuildPopulation();
            using (world)
            {
                for (int i = 0; i < 8; i++)
                {
                    aggregator.Update(0.0166f);
                }

                var samples = new double[ObservationTicks];
                int cursor = 0;
                for (int tick = 0; tick < ObservationTicks; tick++)
                {
                    for (int m = 0; m < SpreadFiringPerTick; m++)
                    {
                        world.Add(entities[cursor], new AttributeAggregateDirty());
                        cursor = (cursor + 1) % entities.Length;
                    }

                    long start = Stopwatch.GetTimestamp();
                    aggregator.Update(0.0166f);
                    samples[tick] = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                    Assert.That(CountDirtyTags(world), Is.Zero,
                        $"Tick {tick}: aggregator must consume every AttributeAggregateDirty entity it processed.");
                }

                Array.Sort(samples);
                TestContext.Out.WriteLine(
                    $"Aggregator spread-firing ({SpreadFiringPerTick}/tick over {EntityCount}): median={samples[ObservationTicks / 2]:F4}ms p95={samples[(int)(ObservationTicks * 0.95)]:F4}ms max={samples[^1]:F4}ms");
                Assert.That(samples[ObservationTicks / 2], Is.LessThan(3d),
                    "Spread-firing median regressed beyond the 3ms regression bound.");
            }
        }

        [Test]
        public void BurstFiringTick_ConsumesAllDirtyEntities()
        {
            (World world, AttributeAggregatorSystem aggregator, Entity[] entities) = BuildPopulation();
            using (world)
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    world.Add(entities[i], new AttributeAggregateDirty());
                }

                for (int warmup = 0; warmup < 64; warmup++)
                {
                    world.Add(entities[warmup], new AttributeAggregateDirty());
                    aggregator.Update(0.0166f);
                }

                for (int i = 0; i < entities.Length; i++)
                {
                    world.Add(entities[i], new AttributeAggregateDirty());
                }

                long start = Stopwatch.GetTimestamp();
                aggregator.Update(0.0166f);
                double elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                TestContext.Out.WriteLine($"Aggregator burst-firing ({EntityCount} dirty in one tick): {elapsedMs:F3}ms");
                Assert.That(CountDirtyTags(world), Is.Zero);
            }
        }

        private static (World world, AttributeAggregatorSystem aggregator, Entity[] entities) BuildPopulation()
        {
            var world = World.Create();
            var tagOps = new TagOps(new DirtyEntityQueue(EntityCount + 8), new TagRuleRegistry());
            var aggregator = new AttributeAggregatorSystem(world, tagOps: tagOps);
            var entities = new Entity[EntityCount];
            for (int i = 0; i < EntityCount; i++)
            {
                entities[i] = world.Create(
                    new AttributeBuffer(),
                    new ActiveEffectContainer(),
                    new DirtyFlags());
            }

            return (world, aggregator, entities);
        }

        private static int CountDirtyTags(World world)
        {
            var query = new QueryDescription().WithAll<AttributeAggregateDirty>();
            return world.CountEntities(in query);
        }
    }
}
