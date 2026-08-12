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
        public const string AfterP1RelativePath = "docs/rfcs/gas-loadtime-capacity/benchmark-after-p1.json";
        public const string AfterP2RelativePath = "docs/rfcs/gas-loadtime-capacity/benchmark-after-p2.json";

        private World _world = null!;
        private readonly TagOps _tagOps = new TagOps(
            new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
            new TagRuleRegistry());

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            _tagOps.ClearRuleRegistry();
            GasLoadTimeCapacitySession.ClearForTests();
            GasLoadTimeCapacitySession.EnsureLegacyPlanAndStoreForTests(entityRowCapacity: EntityCount * 3);
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
            GasLoadTimeCapacitySession.ClearForTests();
            GasLoadTimeCapacitySession.EnsureLegacyPlanAndStoreForTests();
        }

        [Test]
        public void Capture_LegacyEmbedded_BaselineReport()
        {
            var report = CaptureWorldStoreReport(phase: "baseline");
            string outPath = ResolveOutputPath("gas-capacity-benchmark-captured.json");
            report.WriteToFile(outPath);
            TestContext.Out.WriteLine($"Wrote capacity benchmark capture: {outPath}");
            TestContext.Out.WriteLine(report.ToJson());

            That(report.Metrics.Count, Is.GreaterThanOrEqualTo(6));
            That(report.AttributeSlotCount, Is.EqualTo(AttributeBuffer.MAX_ATTRS));
            That(report.TagIdSpace, Is.EqualTo(GasLoadTimeCapacityPlan.CreateLegacyEmbeddedBaseline().TagIdSpace));
        }

        [Test]
        public void Capture_AfterP1_WorldStoreReport()
        {
            var report = CaptureWorldStoreReport(phase: "after-p1");
            string outPath = ResolveOutputPath("gas-capacity-benchmark-after-p1.json");
            report.WriteToFile(outPath);

            string repoOut = ResolveRepoPath(AfterP1RelativePath);
            report.WriteToFile(repoOut);
            TestContext.Out.WriteLine($"Wrote capacity benchmark after-p1: {repoOut}");
            TestContext.Out.WriteLine(report.ToJson());

            That(report.StorageKind, Is.EqualTo("world-column-store"));
            That(report.Phase, Is.EqualTo("after-p1"));
        }


        [Test]
        public void Capture_AfterP2_WorldStoreReport()
        {
            var report = CaptureWorldStoreReport(phase: "after-p2");
            string outPath = ResolveOutputPath("gas-capacity-benchmark-after-p2.json");
            report.WriteToFile(outPath);

            string repoOut = ResolveRepoPath(AfterP2RelativePath);
            report.WriteToFile(repoOut);
            TestContext.Out.WriteLine($"Wrote capacity benchmark after-p2: {repoOut}");
            TestContext.Out.WriteLine(report.ToJson());

            That(report.StorageKind, Is.EqualTo("world-column-store"));
            That(report.Phase, Is.EqualTo("after-p2"));
        }

        [Test]
        public void Compare_AfterP2AgainstCommittedBaseline_WhenBaselineExists()
        {
            string baselinePath = ResolveRepoPath(BaselineRelativePath);
            if (!File.Exists(baselinePath))
            {
                Assert.Ignore($"Baseline not committed yet at {baselinePath}. Run capture and commit P0 baseline first.");
            }

            var baseline = GasCapacityBenchmarkReport.FromJsonFile(baselinePath);
            var after = CaptureWorldStoreReport(phase: "after-p2");
            string result = GasCapacityBenchmarkReport.Compare(baseline, after);

            TestContext.Out.WriteLine(result);
            string notesPath = ResolveRepoPath("docs/rfcs/gas-loadtime-capacity/benchmark-regression-notes.md");
            That(File.Exists(notesPath), Is.True, "P2 must document benchmark overrides in benchmark-regression-notes.md");
            AssertNoUndocumentedHotPathAllocGrowth(result);
            AssertAttrSetGetOpsWithinOverride(result, maxOpsRegression: 0.40);
            AssertTagAddHasOpsWithinOverride(result, maxOpsRegression: 0.50);
        }

        [Test]
        public void Compare_AfterP1AgainstCommittedBaseline_WhenBaselineExists()
        {
            string baselinePath = ResolveRepoPath(BaselineRelativePath);
            if (!File.Exists(baselinePath))
            {
                Assert.Ignore($"Baseline not committed yet at {baselinePath}. Run capture and commit P0 baseline first.");
            }

            var baseline = GasCapacityBenchmarkReport.FromJsonFile(baselinePath);
            var after = CaptureWorldStoreReport(phase: "after-p1");
            string result = GasCapacityBenchmarkReport.Compare(baseline, after);

            TestContext.Out.WriteLine(result);
            string notesPath = ResolveRepoPath("docs/rfcs/gas-loadtime-capacity/benchmark-regression-notes.md");
            That(File.Exists(notesPath), Is.True, "P1 must document benchmark overrides in benchmark-regression-notes.md");
            AssertNoUndocumentedHotPathAllocGrowth(result);
            AssertAttrSetGetOpsWithinOverride(result, maxOpsRegression: 0.40);
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
            var after = CaptureWorldStoreReport(phase: "after-selfcheck");
            string result = GasCapacityBenchmarkReport.Compare(baseline, after);

            TestContext.Out.WriteLine(result);
            AssertNoUndocumentedHotPathAllocGrowth(result);
        }

        private static void AssertNoUndocumentedHotPathAllocGrowth(string compareResult)
        {
            if (compareResult == "OK" || !compareResult.Contains("allocated bytes grew", StringComparison.Ordinal))
            {
                return;
            }

            // tag.dirty.collect alloc bump is documented (DirtyFlags absolute-max words).
            // Attribute hot-path alloc growth remains a hard fail.
            foreach (string line in compareResult.Split('\n'))
            {
                if (!line.Contains("allocated bytes grew", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.Contains("tag.dirty.collect", StringComparison.Ordinal))
                {
                    continue;
                }

                Fail(compareResult);
            }
        }

        private static void AssertAttrSetGetOpsWithinOverride(string compareResult, double maxOpsRegression)
        {
            AssertHotOpsWithinOverride(compareResult, "attr.setw.get.hot", maxOpsRegression, "P1");
        }

        private static void AssertTagAddHasOpsWithinOverride(string compareResult, double maxOpsRegression)
        {
            AssertHotOpsWithinOverride(compareResult, "tag.add.has.hot", maxOpsRegression, "P2");
        }

        private static void AssertHotOpsWithinOverride(
            string compareResult,
            string metricId,
            double maxOpsRegression,
            string phaseLabel)
        {
            string marker = $"{metricId}: ops regressed ";
            int idx = compareResult.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
            {
                return;
            }

            string rest = compareResult.Substring(idx + marker.Length);
            string[] parts = rest.Split(new[] { " -> ", " (" }, StringSplitOptions.None);
            That(parts.Length, Is.GreaterThanOrEqualTo(2));
            double before = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            double after = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            double floor = before * (1.0 - maxOpsRegression);
            That(after, Is.GreaterThanOrEqualTo(floor),
                $"{metricId} ops {after:F0} below documented {phaseLabel} override floor {floor:F0} ({maxOpsRegression:P0}). Full: {compareResult}");
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

        private GasCapacityBenchmarkReport CaptureWorldStoreReport(string phase)
        {
            var plan = GasLoadTimeCapacityPlan.CreateLegacyEmbeddedBaseline();
            GasLoadTimeCapacitySession.ClearForTests();
            GasLoadTimeCapacitySession.Freeze(plan);
            GasLoadTimeCapacitySession.EnsureStore(plan, entityRowCapacity: EntityCount * 3);

            var report = new GasCapacityBenchmarkReport
            {
                Phase = phase,
                StorageKind = "world-column-store",
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
            // Store columns are session-scoped; measure entity component delta after store already exists.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetTotalMemory(true);

            var entities = new Entity[EntityCount];
            for (int i = 0; i < EntityCount; i++)
            {
                var e = _world.Create(
                    AttributeBuffer.CreateAttached(),
                    new DirtyFlags(),
                    new AttributeLastSnapshot());
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

            DestroyAttributeEntities(entities);
        }

        private void MeasureAttributeSetGetHot(GasCapacityBenchmarkReport report)
        {
            var entities = new Entity[EntityCount];
            for (int i = 0; i < EntityCount; i++)
            {
                var e = _world.Create(AttributeBuffer.CreateAttached());
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

            DestroyAttributeEntities(entities);
        }

        private void MeasureAttributeAggregateTick(GasCapacityBenchmarkReport report)
        {
            var entities = new Entity[Math.Min(EntityCount, 2000)];
            for (int i = 0; i < entities.Length; i++)
            {
                var e = _world.Create(
                    AttributeBuffer.CreateAttached(),
                    new ActiveEffectContainer(),
                    new AttributeAggregateDirty(),
                    new DirtyFlags());
                entities[i] = e;
                ref var attrs = ref _world.Get<AttributeBuffer>(e);
                attrs.SetBase(0, 100f);
                attrs.SetCurrent(0, 100f);
            }

            var agg = new AttributeAggregatorSystem(_world, tagOps: _tagOps);
            // warmup (clears AttributeAggregateDirty; measured loop matches baseline empty-query cost)
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

            DestroyAttributeEntities(entities);
            agg.Dispose();
        }

        private void MeasureTagFootprint(GasCapacityBenchmarkReport report)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetTotalMemory(true);

            var entities = new Entity[EntityCount];
            for (int i = 0; i < EntityCount; i++)
            {
                entities[i] = _world.Create(
                    GameplayTagContainer.CreateAttached(),
                    new TagCountContainer(),
                    new DirtyFlags(),
                    new GameplayTagEffectiveCache());
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
                entities[i] = _world.Create(GameplayTagContainer.CreateAttached(), new TagCountContainer());
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
                var tags = GameplayTagContainer.CreateAttached();
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

        private void DestroyAttributeEntities(Entity[] entities)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                GasAttributeRows.ReleaseIfPresent(_world, entities[i]);
                _world.Destroy(entities[i]);
            }
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
