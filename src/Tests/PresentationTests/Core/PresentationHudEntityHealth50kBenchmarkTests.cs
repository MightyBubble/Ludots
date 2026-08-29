using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Hud;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    [Category("benchmark")]
    public sealed class PresentationHudEntityHealth50kBenchmarkTests
    {
        private const int EntityCount = 50_000;
        private const int HudItemCount = EntityCount * 2;
        private const int MaxHealth = 1000;
        private const int WarmupFrames = 4;
        private const int MeasuredFrames = 30;
        private const double TargetFrameBudgetMs = 1000d / 60d;
        private const int RandomSeed = 0x5EED_1000;

        [Test]
        public void Benchmark_EntityHealth50k_SyncsHudBarsAndText_WritesReport()
        {
            using var world = World.Create();
            int healthAttributeId = AttributeRegistry.Register("Health");
            Entity[] entities = CreateEntities(world, healthAttributeId);

            var screenHud = new ScreenHudBatchBuffer(HudItemCount + 1024);
            var builder = new PresentationOverlaySceneBuilder(screenHud, null, null, null, screenOverlay: null);
            var scene = new PresentationOverlayScene(HudItemCount + 1024);
            uint randomState = unchecked((uint)RandomSeed);

            Warmup(world, entities, screenHud, builder, scene, healthAttributeId, ref randomState);

            BenchmarkResult result = RunMeasured(world, entities, screenHud, builder, scene, healthAttributeId, ref randomState);
            HudSyncValidation validation = ValidateFinalHud(world, entities, screenHud, healthAttributeId);

            string artifactDir = Path.Combine(
                FindRepoRoot(),
                "artifacts",
                "benchmarks",
                "presentation-entity-health-hud-50k");
            Directory.CreateDirectory(artifactDir);
            string reportPath = Path.Combine(artifactDir, "benchmark-report.md");
            string tracePath = Path.Combine(artifactDir, "trace.jsonl");

            File.WriteAllText(reportPath, BuildReport(result, validation));
            File.WriteAllText(tracePath, BuildTrace(result));

            TestContext.Out.WriteLine(File.ReadAllText(reportPath));

            Assert.That(File.Exists(reportPath), Is.True);
            Assert.That(File.Exists(tracePath), Is.True);
            Assert.That(result.BarCount, Is.EqualTo(EntityCount));
            Assert.That(result.TextCount, Is.EqualTo(EntityCount));
            Assert.That(result.SceneCount, Is.EqualTo(HudItemCount));
            Assert.That(result.ScreenHudDroppedTotal, Is.EqualTo(0));
            Assert.That(result.SceneDroppedTotal, Is.EqualTo(0));
            Assert.That(validation.MismatchCount, Is.EqualTo(0));
            Assert.That(validation.ValidatedEntities, Is.EqualTo(EntityCount));
            Assert.That(result.AverageChangedEntities, Is.EqualTo(EntityCount));
        }

        private static Entity[] CreateEntities(World world, int healthAttributeId)
        {
            var entities = new Entity[EntityCount];
            for (int i = 0; i < EntityCount; i++)
            {
                var attributes = default(AttributeBuffer);
                attributes.SetBase(healthAttributeId, MaxHealth);
                attributes.SetCurrent(healthAttributeId, 250f + (i % 501));
                entities[i] = world.Create(attributes);
            }

            return entities;
        }

        private static void Warmup(
            World world,
            Entity[] entities,
            ScreenHudBatchBuffer screenHud,
            PresentationOverlaySceneBuilder builder,
            PresentationOverlayScene scene,
            int healthAttributeId,
            ref uint randomState)
        {
            for (int frame = 0; frame < WarmupFrames; frame++)
            {
                ExecuteFrame(world, entities, screenHud, builder, scene, healthAttributeId, ref randomState);
            }
        }

        private static BenchmarkResult RunMeasured(
            World world,
            Entity[] entities,
            ScreenHudBatchBuffer screenHud,
            PresentationOverlaySceneBuilder builder,
            PresentationOverlayScene scene,
            int healthAttributeId,
            ref uint randomState)
        {
            double[] frameTotals = new double[MeasuredFrames];
            double[] syncTimes = new double[MeasuredFrames];
            double[] buildTimes = new double[MeasuredFrames];
            int[] changedEntities = new int[MeasuredFrames];
            int[] dirtyLanes = new int[MeasuredFrames];
            int[] retainedItems = new int[MeasuredFrames];
            int[] mutatedItems = new int[MeasuredFrames];
            long[] healthChecksums = new long[MeasuredFrames];

            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            int barCount = 0;
            int textCount = 0;
            int sceneCount = 0;

            for (int frame = 0; frame < MeasuredFrames; frame++)
            {
                FrameMetrics metrics = ExecuteFrame(world, entities, screenHud, builder, scene, healthAttributeId, ref randomState);
                frameTotals[frame] = metrics.TotalMs;
                syncTimes[frame] = metrics.SyncMs;
                buildTimes[frame] = metrics.BuildMs;
                changedEntities[frame] = metrics.ChangedEntities;
                dirtyLanes[frame] = metrics.DirtyLanes;
                retainedItems[frame] = metrics.RetainedItems;
                mutatedItems[frame] = metrics.MutatedItems;
                healthChecksums[frame] = metrics.HealthChecksum;
                barCount = metrics.BarCount;
                textCount = metrics.TextCount;
                sceneCount = metrics.SceneCount;
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - startAlloc;
            return new BenchmarkResult(
                frameTotals,
                syncTimes,
                buildTimes,
                changedEntities,
                dirtyLanes,
                retainedItems,
                mutatedItems,
                healthChecksums,
                allocatedBytes,
                barCount,
                textCount,
                sceneCount,
                screenHud.DroppedTotal,
                scene.DroppedTotal);
        }

        private static FrameMetrics ExecuteFrame(
            World world,
            Entity[] entities,
            ScreenHudBatchBuffer screenHud,
            PresentationOverlaySceneBuilder builder,
            PresentationOverlayScene scene,
            int healthAttributeId,
            ref uint randomState)
        {
            long totalStart = Stopwatch.GetTimestamp();

            long syncStart = Stopwatch.GetTimestamp();
            int changedEntities = FillHudFromEntityHealth(world, entities, screenHud, healthAttributeId, ref randomState, out long healthChecksum);
            double syncMs = ElapsedMs(syncStart);

            long buildStart = Stopwatch.GetTimestamp();
            builder.Build(scene);
            double buildMs = ElapsedMs(buildStart);
            double totalMs = (Stopwatch.GetTimestamp() - totalStart) * 1000d / Stopwatch.Frequency;

            return new FrameMetrics(
                totalMs,
                syncMs,
                buildMs,
                changedEntities,
                screenHud.BarCount,
                screenHud.TextCount,
                scene.Count,
                scene.DirtyLaneCount,
                scene.RetainedItemCountLastBuild,
                scene.MutatedItemCountLastBuild,
                healthChecksum);
        }

        private static int FillHudFromEntityHealth(
            World world,
            Entity[] entities,
            ScreenHudBatchBuffer screenHud,
            int healthAttributeId,
            ref uint randomState,
            out long healthChecksum)
        {
            screenHud.Clear();

            const int columns = 400;
            const float baseX = 4f;
            const float baseY = 6f;
            const float colSpacing = 4.1f;
            const float rowSpacing = 3.6f;
            const float barWidth = 3f;
            const float barHeight = 2f;
            const int fontSize = 8;
            Vector4 barBackground = new(0.10f, 0.12f, 0.16f, 0.86f);
            Vector4 barForeground = new(0.16f, 0.82f, 0.36f, 0.96f);
            Vector4 textColor = new(0.94f, 0.96f, 0.88f, 1f);

            int changedEntities = 0;
            long checksum = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(entities[i]);
                int previousHealth = (int)attributes.GetCurrent(healthAttributeId);
                int nextHealth = NextHealth(previousHealth, ref randomState);
                attributes.SetCurrent(healthAttributeId, nextHealth);
                int currentHealth = (int)attributes.GetCurrent(healthAttributeId);
                int baseHealth = (int)attributes.GetBase(healthAttributeId);
                if (baseHealth <= 0)
                {
                    throw new InvalidOperationException("Health base value must be positive for HUD synchronization.");
                }

                if (currentHealth != previousHealth)
                {
                    changedEntities++;
                }

                checksum += currentHealth;
                int row = i / columns;
                int column = i % columns;
                float x = baseX + (column * colSpacing);
                float y = baseY + (row * rowSpacing);
                float fill = currentHealth / (float)baseHealth;

                screenHud.TryAddBar(new ScreenHudBarItem
                {
                    StableId = HudItemIdentity.ComposeStableId(i + 1, WorldHudItemKind.Bar, discriminator: healthAttributeId),
                    DirtySerial = HudItemIdentity.ComposeBarDirtySerial(barWidth, barHeight, fill, barBackground, barForeground),
                    ScreenX = x,
                    ScreenY = y,
                    Width = barWidth,
                    Height = barHeight,
                    Value0 = fill,
                    Color0 = barBackground,
                    Color1 = barForeground,
                });

                screenHud.TryAddText(new ScreenHudTextItem
                {
                    StableId = HudItemIdentity.ComposeStableId(i + 1, WorldHudItemKind.Text, discriminator: healthAttributeId),
                    DirtySerial = HudItemIdentity.ComposeTextDirtySerial(
                        fontSize,
                        0,
                        (int)WorldHudValueMode.AttributeCurrentOverBase,
                        currentHealth,
                        baseHealth,
                        textColor,
                        default),
                    ScreenX = x,
                    ScreenY = y - 7f,
                    FontSize = fontSize,
                    Color0 = textColor,
                    Value0 = currentHealth,
                    Value1 = baseHealth,
                    Id1 = (int)WorldHudValueMode.AttributeCurrentOverBase,
                });
            }

            healthChecksum = checksum;
            return changedEntities;
        }

        private static int NextHealth(int previousHealth, ref uint randomState)
        {
            int delta = (int)(NextRandom(ref randomState) % 41u) - 20;
            if (delta == 0)
            {
                delta = 1;
            }

            int next = previousHealth + delta;
            if (next < 0)
            {
                next = -next;
            }

            if (next > MaxHealth)
            {
                next = MaxHealth - (next - MaxHealth);
            }

            if (next < 0)
            {
                next = 0;
            }

            if (next > MaxHealth)
            {
                next = MaxHealth;
            }

            if (next == previousHealth)
            {
                next = previousHealth < MaxHealth ? previousHealth + 1 : previousHealth - 1;
            }

            return next;
        }

        private static uint NextRandom(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        private static HudSyncValidation ValidateFinalHud(
            World world,
            Entity[] entities,
            ScreenHudBatchBuffer screenHud,
            int healthAttributeId)
        {
            ReadOnlySpan<ScreenHudBarItem> bars = screenHud.GetBarSpan();
            ReadOnlySpan<ScreenHudTextItem> texts = screenHud.GetTextSpan();
            int mismatchCount = 0;
            long healthChecksum = 0;
            long textChecksum = 0;

            if (bars.Length != entities.Length || texts.Length != entities.Length)
            {
                mismatchCount += Math.Abs(bars.Length - entities.Length);
                mismatchCount += Math.Abs(texts.Length - entities.Length);
            }

            int count = Math.Min(entities.Length, Math.Min(bars.Length, texts.Length));
            for (int i = 0; i < count; i++)
            {
                ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(entities[i]);
                int currentHealth = (int)attributes.GetCurrent(healthAttributeId);
                int baseHealth = (int)attributes.GetBase(healthAttributeId);
                float expectedFill = currentHealth / (float)baseHealth;
                int expectedBarStableId = HudItemIdentity.ComposeStableId(i + 1, WorldHudItemKind.Bar, discriminator: healthAttributeId);
                int expectedTextStableId = HudItemIdentity.ComposeStableId(i + 1, WorldHudItemKind.Text, discriminator: healthAttributeId);

                if (bars[i].StableId != expectedBarStableId ||
                    texts[i].StableId != expectedTextStableId ||
                    Math.Abs(bars[i].Value0 - expectedFill) > 0.0001f ||
                    (int)texts[i].Value0 != currentHealth ||
                    (int)texts[i].Value1 != baseHealth ||
                    texts[i].Id1 != (int)WorldHudValueMode.AttributeCurrentOverBase)
                {
                    mismatchCount++;
                }

                healthChecksum += currentHealth;
                textChecksum += (int)texts[i].Value0;
            }

            return new HudSyncValidation(count, mismatchCount, healthChecksum, textChecksum);
        }

        private static string BuildReport(BenchmarkResult result, HudSyncValidation validation)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Presentation Entity Health HUD 50k Benchmark");
            sb.AppendLine();
            sb.AppendLine($"- workload: `{EntityCount}` Arch ECS entities with `AttributeBuffer.Health`");
            sb.AppendLine($"- HUD output: `{EntityCount}` bars + `{EntityCount}` text items");
            sb.AppendLine("- health churn: every measured frame mutates every entity to a deterministic random HP value");
            sb.AppendLine($"- measured frames: `{MeasuredFrames}` after `{WarmupFrames}` warmup frames");
            sb.AppendLine($"- target frame budget: `{TargetFrameBudgetMs:F2} ms` at 60 Hz");
            sb.AppendLine();
            sb.AppendLine("## Correctness");
            sb.AppendLine();
            sb.AppendLine($"- validated entities: `{validation.ValidatedEntities}`");
            sb.AppendLine($"- bar/text mismatches: `{validation.MismatchCount}`");
            sb.AppendLine($"- final HP checksum: `{validation.HealthChecksum}`");
            sb.AppendLine($"- final text checksum: `{validation.TextChecksum}`");
            sb.AppendLine($"- screen HUD drops: `{result.ScreenHudDroppedTotal}`");
            sb.AppendLine($"- overlay scene drops: `{result.SceneDroppedTotal}`");
            sb.AppendLine();
            sb.AppendLine("## Throughput");
            sb.AppendLine();
            sb.AppendLine($"- avg total: `{result.AverageTotalMs:F3} ms`");
            sb.AppendLine($"- p95 total: `{result.P95TotalMs:F3} ms`");
            sb.AppendLine($"- max total: `{result.MaxTotalMs:F3} ms`");
            sb.AppendLine($"- avg HP->HUD sync: `{result.AverageSyncMs:F3} ms`");
            sb.AppendLine($"- avg HUD->overlay build: `{result.AverageBuildMs:F3} ms`");
            sb.AppendLine($"- avg fps equivalent: `{result.AverageFps:F1}`");
            sb.AppendLine($"- alloc per frame: `{result.AllocatedBytesPerFrame:F1} B`");
            sb.AppendLine($"- avg changed entities: `{result.AverageChangedEntities:F0}`");
            sb.AppendLine($"- avg dirty lanes: `{result.AverageDirtyLanes:F2}`");
            sb.AppendLine($"- avg retained overlay items: `{result.AverageRetainedItems:F0}`");
            sb.AppendLine($"- avg mutated overlay items: `{result.AverageMutatedItems:F0}`");
            sb.AppendLine($"- 60 Hz pass: `{(result.P95TotalMs <= TargetFrameBudgetMs ? "yes" : "no")}`");
            sb.AppendLine();
            sb.AppendLine("## Final Counts");
            sb.AppendLine();
            sb.AppendLine($"- bars: `{result.BarCount}`");
            sb.AppendLine($"- text: `{result.TextCount}`");
            sb.AppendLine($"- overlay scene items: `{result.SceneCount}`");
            return sb.ToString();
        }

        private static string BuildTrace(BenchmarkResult result)
        {
            var sb = new StringBuilder();
            for (int frame = 0; frame < result.FrameTotals.Length; frame++)
            {
                sb.Append("{");
                sb.Append("\"scenario\":\"entity_health_hud_50k\",");
                sb.Append("\"frame\":").Append(frame).Append(',');
                sb.Append("\"total_ms\":").Append(result.FrameTotals[frame].ToString("F4", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"sync_ms\":").Append(result.SyncTimes[frame].ToString("F4", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"build_ms\":").Append(result.BuildTimes[frame].ToString("F4", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"changed_entities\":").Append(result.ChangedEntities[frame]).Append(',');
                sb.Append("\"dirty_lanes\":").Append(result.DirtyLanes[frame]).Append(',');
                sb.Append("\"retained_items\":").Append(result.RetainedItems[frame]).Append(',');
                sb.Append("\"mutated_items\":").Append(result.MutatedItems[frame]).Append(',');
                sb.Append("\"health_checksum\":").Append(result.HealthChecksums[frame]);
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.WorkDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "AGENTS.md")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current)!;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }

        private static double ElapsedMs(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
        }

        private readonly record struct FrameMetrics(
            double TotalMs,
            double SyncMs,
            double BuildMs,
            int ChangedEntities,
            int BarCount,
            int TextCount,
            int SceneCount,
            int DirtyLanes,
            int RetainedItems,
            int MutatedItems,
            long HealthChecksum);

        private readonly record struct HudSyncValidation(
            int ValidatedEntities,
            int MismatchCount,
            long HealthChecksum,
            long TextChecksum);

        private sealed class BenchmarkResult
        {
            public BenchmarkResult(
                double[] frameTotals,
                double[] syncTimes,
                double[] buildTimes,
                int[] changedEntities,
                int[] dirtyLanes,
                int[] retainedItems,
                int[] mutatedItems,
                long[] healthChecksums,
                long allocatedBytes,
                int barCount,
                int textCount,
                int sceneCount,
                int screenHudDroppedTotal,
                int sceneDroppedTotal)
            {
                FrameTotals = frameTotals;
                SyncTimes = syncTimes;
                BuildTimes = buildTimes;
                ChangedEntities = changedEntities;
                DirtyLanes = dirtyLanes;
                RetainedItems = retainedItems;
                MutatedItems = mutatedItems;
                HealthChecksums = healthChecksums;
                AllocatedBytes = allocatedBytes;
                BarCount = barCount;
                TextCount = textCount;
                SceneCount = sceneCount;
                ScreenHudDroppedTotal = screenHudDroppedTotal;
                SceneDroppedTotal = sceneDroppedTotal;
            }

            public double[] FrameTotals { get; }
            public double[] SyncTimes { get; }
            public double[] BuildTimes { get; }
            public int[] ChangedEntities { get; }
            public int[] DirtyLanes { get; }
            public int[] RetainedItems { get; }
            public int[] MutatedItems { get; }
            public long[] HealthChecksums { get; }
            public long AllocatedBytes { get; }
            public int BarCount { get; }
            public int TextCount { get; }
            public int SceneCount { get; }
            public int ScreenHudDroppedTotal { get; }
            public int SceneDroppedTotal { get; }

            public double AverageTotalMs => Average(FrameTotals);
            public double P95TotalMs => Percentile(FrameTotals, 0.95);
            public double MaxTotalMs => Max(FrameTotals);
            public double AverageSyncMs => Average(SyncTimes);
            public double AverageBuildMs => Average(BuildTimes);
            public double AverageFps => AverageTotalMs <= 0d ? 0d : 1000d / AverageTotalMs;
            public double AllocatedBytesPerFrame => FrameTotals.Length == 0 ? 0d : AllocatedBytes / (double)FrameTotals.Length;
            public double AverageChangedEntities => Average(ChangedEntities);
            public double AverageDirtyLanes => Average(DirtyLanes);
            public double AverageRetainedItems => Average(RetainedItems);
            public double AverageMutatedItems => Average(MutatedItems);

            private static double Average(double[] values)
            {
                if (values.Length == 0)
                {
                    return 0d;
                }

                double sum = 0d;
                for (int i = 0; i < values.Length; i++)
                {
                    sum += values[i];
                }

                return sum / values.Length;
            }

            private static double Average(int[] values)
            {
                if (values.Length == 0)
                {
                    return 0d;
                }

                double sum = 0d;
                for (int i = 0; i < values.Length; i++)
                {
                    sum += values[i];
                }

                return sum / values.Length;
            }

            private static double Max(double[] values)
            {
                double max = 0d;
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] > max)
                    {
                        max = values[i];
                    }
                }

                return max;
            }

            private static double Percentile(double[] values, double percentile)
            {
                if (values.Length == 0)
                {
                    return 0d;
                }

                double[] copy = new double[values.Length];
                Array.Copy(values, copy, values.Length);
                Array.Sort(copy);
                int index = (int)Math.Ceiling((copy.Length - 1) * percentile);
                return copy[index];
            }
        }
    }
}
