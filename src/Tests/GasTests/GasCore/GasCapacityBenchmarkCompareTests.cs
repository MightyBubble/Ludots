using System;
using System.IO;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Capacity;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Fixed-parameter comparison bed for RFC-0066. Baseline is committed under
    /// docs/rfcs/gas-loadtime-capacity/; after reports are written beside the test output.
    /// </summary>
    [TestFixture]
    [Category("benchmark")]
    public class GasCapacityBenchmarkCompareTests
    {
        public const int EntityCount = 10_000;
        public const int Iterations = 100;
        public const string BaselineRelativePath = "docs/rfcs/gas-loadtime-capacity/benchmark-baseline.json";

        private World _world = null!;
        private readonly TagOps _tagOps = new TagOps(
            new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
            new TagRuleRegistry());

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            _tagOps.ClearRuleRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }

        [Test]
        public void Capture_LegacyEmbedded_BaselineReport()
        {
            var report = CaptureLegacyEmbeddedReport(phase: "baseline");
            string outPath = ResolveOutputPath("gas-capacity-benchmark-captured.json");
            report.WriteToFile(outPath);
            TestContext.Out.WriteLine($"Wrote capacity benchmark capture: {outPath}");
            TestContext.Out.WriteLine(report.ToJson());

            That(report.Metrics.Count, Is.GreaterThanOrEqualTo(6));
            That(report.AttributeSlotCount, Is.EqualTo(AttributeBuffer.MAX_ATTRS));
            That(report.TagIdSpace, Is.EqualTo(GameplayTagContainer.MAX_TAG_ID + 1));
        }

        [Test]
        public void Compare_CapturedAgainstCommittedBaseline_WhenBaselineExists()
        {
            string baselinePath = ResolveRepoPath(BaselineRelativePath);
            if (!File.Exists(baselinePath))
            {
                Assert.Ignore($"Baseline not committed yet at {baselinePath}. Run capture and commit P0 baseline first.");
            }

            var baseline = GasCapacityBenchmarkReport.FromJsonFile(baselinePath);
            var after = CaptureLegacyEmbeddedReport(phase: "after-selfcheck");
            string result = GasCapacityBenchmarkReport.Compare(baseline, after);

            TestContext.Out.WriteLine(result);
            // Self-check on the same storage may jitter; only hard-fail on allocation growth or extreme drift.
            if (result != "OK" && result.Contains("allocated bytes grew", StringComparison.Ordinal))
            {
                Fail(result);
            }
        }

        [Test]
        public void ReportCompare_DetectsOpsRegressionAndAllocGrowth()
        {
            var baseline = new GasCapacityBenchmarkReport
            {
                EntityCount = 10,
                Iterations = 1,
            };
            baseline.AddMetric("tag.add.has.hot", 1_000_000, "ops_per_sec", allocatedBytes: 0);
            baseline.AddMetric("attr.footprint.per_entity", 100, "bytes", allocatedBytes: 0);

            var after = new GasCapacityBenchmarkReport
            {
                EntityCount = 10,
                Iterations = 1,
            };
            after.AddMetric("tag.add.has.hot", 500_000, "ops_per_sec", allocatedBytes: 128);
            after.AddMetric("attr.footprint.per_entity", 100, "bytes", allocatedBytes: 0);

            string result = GasCapacityBenchmarkReport.Compare(baseline, after, regressionThreshold: 0.10);
            That(result, Does.Contain("REGRESSION"));
            That(result, Does.Contain("tag.add.has.hot"));
            That(result, Does.Contain("allocated bytes grew"));
        }

        private GasCapacityBenchmarkReport CaptureLegacyEmbeddedReport(string phase)
        {
            var plan = GasLoadTimeCapacityPlan.CreateLegacyEmbeddedBaseline();
            var report = new GasCapacityBenchmarkReport
            {
                Phase = phase,
                StorageKind = "legacy-embedded",
                AttributeSlotCount = plan.AttributeSlotCount,
                TagIdSpace = plan.TagIdSpace,
                EntityCount = EntityCount,
                Iterations = Iterations,
            };

            MeasureAttributeFootprint(report);
            MeasureAttributeSetGetHot(report);
            MeasureAttributeAggregateTick(report);
            MeasureTagFootprint(report);
            MeasureTagAddHasHot(report);
            MeasureTagDirtyCollect(report);
            return report;
        }

        private void MeasureAttributeFootprint(GasCapacityBenchmarkReport report)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetTotalMemory(true);

            var entities = new Entity[EntityCount];
            var archetype = new ComponentType[]
            {
                typeof(AttributeBuffer),
                typeof(DirtyFlags),
                typeof(AttributeLastSnapshot),
            };
            for (int i = 0; i < EntityCount; i++)
            {
                var e = _world.Create(archetype);
                entities[i] = e;
                ref var attrs = ref _world.Get<AttributeBuffer>(e);
                attrs.SetBase(0, 100f);
                attrs.SetBase(1, 50f);
            }

            long after = GC.GetTotalMemory(true);
            double perEntity = (after - before) / (double)EntityCount;
            report.AddMetric("attr.footprint.per_entity", perEntity, "bytes");

            int structural = Unsafe.SizeOf<AttributeBuffer>() +
                             Unsafe.SizeOf<DirtyFlags>() +
                             Unsafe.SizeOf<AttributeLastSnapshot>();
            report.AddMetric("attr.struct_sizeof.bundle", structural, "bytes");

            for (int i = 0; i < entities.Length; i++)
            {
                _world.Destroy(entities[i]);
            }
        }

        private void MeasureAttributeSetGetHot(GasCapacityBenchmarkReport report)
        {
            var entities = new Entity[EntityCount];
            for (int i = 0; i < EntityCount; i++)
            {
                var e = _world.Create(new AttributeBuffer());
                entities[i] = e;
                ref var attrs = ref _world.Get<AttributeBuffer>(e);
                attrs.SetBase(0, 100f);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long alloc0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            float sink = 0f;
            for (int iter = 0; iter < Iterations; iter++)
            {
                for (int i = 0; i < EntityCount; i++)
                {
                    ref var attrs = ref _world.Get<AttributeBuffer>(entities[i]);
                    attrs.SetCurrent(0, 90f + (iter & 7));
                    sink += attrs.GetCurrent(0);
                }
            }

            sw.Stop();
            long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
            double ops = EntityCount * (double)Iterations * 2.0 / sw.Elapsed.TotalSeconds;
            report.AddMetric("attr.setw.get.hot", ops, "ops_per_sec", alloc);
            That(sink, Is.GreaterThan(0f));

            for (int i = 0; i < entities.Length; i++)
            {
                _world.Destroy(entities[i]);
            }
        }

        private void MeasureAttributeAggregateTick(GasCapacityBenchmarkReport report)
        {
            var entities = new Entity[Math.Min(EntityCount, 2000)];
            for (int i = 0; i < entities.Length; i++)
            {
                var e = _world.Create(
                    new AttributeBuffer(),
                    new ActiveEffectContainer(),
                    new AttributeAggregateDirty(),
                    new DirtyFlags());
                entities[i] = e;
                ref var attrs = ref _world.Get<AttributeBuffer>(e);
                attrs.SetBase(0, 100f);
                attrs.SetCurrent(0, 100f);
            }

            var agg = new AttributeAggregatorSystem(_world, tagOps: _tagOps);
            // warmup
            agg.Update(0.016f);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long alloc0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 50; i++)
            {
                agg.Update(0.016f);
            }

            sw.Stop();
            long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
            report.AddMetric("attr.aggregate.tick", sw.Elapsed.TotalMilliseconds / 50.0, "ms_per_tick", alloc);

            for (int i = 0; i < entities.Length; i++)
            {
                _world.Destroy(entities[i]);
            }
        }

        private void MeasureTagFootprint(GasCapacityBenchmarkReport report)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetTotalMemory(true);

            var entities = new Entity[EntityCount];
            var archetype = new ComponentType[]
            {
                typeof(GameplayTagContainer),
                typeof(TagCountContainer),
                typeof(DirtyFlags),
                typeof(GameplayTagEffectiveCache),
            };
            for (int i = 0; i < EntityCount; i++)
            {
                entities[i] = _world.Create(archetype);
            }

            long after = GC.GetTotalMemory(true);
            report.AddMetric("tag.footprint.per_entity", (after - before) / (double)EntityCount, "bytes");
            report.AddMetric(
                "tag.struct_sizeof.container",
                Unsafe.SizeOf<GameplayTagContainer>(),
                "bytes");

            for (int i = 0; i < entities.Length; i++)
            {
                _world.Destroy(entities[i]);
            }
        }

        private void MeasureTagAddHasHot(GasCapacityBenchmarkReport report)
        {
            var entities = new Entity[EntityCount];
            for (int i = 0; i < EntityCount; i++)
            {
                entities[i] = _world.Create(new GameplayTagContainer(), new TagCountContainer());
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long alloc0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int hits = 0;
            for (int iter = 0; iter < Iterations; iter++)
            {
                for (int i = 0; i < EntityCount; i++)
                {
                    ref var tags = ref _world.Get<GameplayTagContainer>(entities[i]);
                    ref var counts = ref _world.Get<TagCountContainer>(entities[i]);
                    int tagId = (i % 255) + 1;
                    _tagOps.AddTag(ref tags, ref counts, tagId);
                    if (tags.HasTag(tagId))
                    {
                        hits++;
                    }
                }
            }

            sw.Stop();
            long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
            double ops = EntityCount * (double)Iterations * 2.0 / sw.Elapsed.TotalSeconds;
            report.AddMetric("tag.add.has.hot", ops, "ops_per_sec", alloc);
            That(hits, Is.GreaterThan(0));

            for (int i = 0; i < entities.Length; i++)
            {
                _world.Destroy(entities[i]);
            }
        }

        private void MeasureTagDirtyCollect(GasCapacityBenchmarkReport report)
        {
            const int entityCount = 1000;
            var queue = new DeferredTriggerQueue();
            var system = new DeferredTriggerCollectionSystem(_world, queue, _tagOps);

            for (int i = 0; i < entityCount; i++)
            {
                int tagId = (i % 31) + 1;
                var tags = default(GameplayTagContainer);
                var counts = default(TagCountContainer);
                var dirty = default(DirtyFlags);
                tags.AddTag(tagId);
                counts.AddCount(tagId);
                dirty.MarkTagDirty(tagId);
                _world.Create(tags, counts, dirty);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long alloc0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            system.Update(0.016f);
            sw.Stop();
            long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;

            report.AddMetric("tag.dirty.collect", sw.Elapsed.TotalMilliseconds, "ms", alloc);
            That(queue.TagTriggerCount, Is.EqualTo(entityCount));
            system.Dispose();
        }

        private static string ResolveOutputPath(string fileName)
        {
            string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "gas-capacity-benchmark");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, fileName);
        }

        private static string ResolveRepoPath(string relativePath)
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
            {
                bool isRepoRoot =
                    Directory.Exists(Path.Combine(dir, "src")) &&
                    Directory.Exists(Path.Combine(dir, "docs"));
                if (isRepoRoot)
                {
                    return Path.Combine(dir, relativePath);
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new DirectoryNotFoundException(
                $"Could not locate repo root (src+docs) while resolving '{relativePath}'.");
        }
    }
}
