using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Arch.Core;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    [Category("benchmark")]
    public sealed class PresenterTimerBenchmarkTests
    {
        private const int PresenterInstances = 30_000;
        private const int MeasuredFrames = 120;
        private const float FrameDt = 0.016f;

        [Test]
        public void Benchmark_PresenterTimerTick_30kInstances_WritesReport()
        {
            using World world = World.Create();
            var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity * 16);

            TimerBenchmarkResult single = RunScenario(world, events, timersPerInstance: 1, label: "30k x 1");
            TimerBenchmarkResult triple = RunScenario(world, events, timersPerInstance: 3, label: "30k x 3");

            WriteReport(single, triple);

            Assert.That(single.AllocatedBytes, Is.EqualTo(0), "steady-state tick must be 0-alloc.");
            Assert.That(triple.AllocatedBytes, Is.EqualTo(0), "steady-state tick must be 0-alloc.");
            Assert.That(single.P95TickMs, Is.LessThan(8.0), $"30k timer tick p95 sanity bound; actual {single.P95TickMs:F4} ms.");
            Assert.That(triple.P95TickMs, Is.LessThan(24.0), $"90k timer tick p95 sanity bound; actual {triple.P95TickMs:F4} ms.");
        }

        private static TimerBenchmarkResult RunScenario(World world, PresentationEventStream events, int timersPerInstance, string label)
        {
            int timerCount = PresenterInstances * timersPerInstance;
            var table = new PresenterTimerTable(capacity: timerCount * 2, randomSeed: 20260823u);
            var system = new PresenterTimerSystem(world, table, events);
            Entity sharedOwner = world.Create();

            int[] nameIds = new int[timersPerInstance];
            for (int t = 0; t < timersPerInstance; t++)
            {
                nameIds[t] = PresenterTimerNameRegistry.Register($"bench.{label}.phase{t}");
            }

            // 30k 假实例：stable id 唯一即可，timer 表不校验实体活性
            for (int i = 0; i < PresenterInstances; i++)
            {
                for (int t = 0; t < timersPerInstance; t++)
                {
                    // 错峰：每帧约 1/50 到期，模拟分段演出链
                    float duration = 0.32f + (i % 50) * FrameDt;
                    table.Set(10_000 + i, default, sharedOwner, nameIds[t], duration + t * 0.5f, 0f);
                }
            }

            // 预热
            for (int frame = 0; frame < 10; frame++)
            {
                system.Update(FrameDt);
                RechainExpired(table, nameIds, sharedOwner);
                events.Clear();
            }

            double[] frameMs = new double[MeasuredFrames];
            int[] expiredCounts = new int[MeasuredFrames];
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            int gc0Before = GC.CollectionCount(0);
            for (int frame = 0; frame < MeasuredFrames; frame++)
            {
                long frameStart = Stopwatch.GetTimestamp();
                system.Update(FrameDt);
                RechainExpired(table, nameIds, sharedOwner);
                events.Clear();
                frameMs[frame] = (Stopwatch.GetTimestamp() - frameStart) * 1000d / Stopwatch.Frequency;
                expiredCounts[frame] = table.ExpiredCount;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;

            Assert.That(GC.CollectionCount(0), Is.EqualTo(gc0Before), $"[{label}] steady-state tick must not trigger gen0 GC.");
            Assert.That(table.Count, Is.EqualTo(timerCount), $"[{label}] same-name re-set must keep the timer population steady.");

            return new TimerBenchmarkResult(
                Label: label,
                TimerCount: timerCount,
                FrameMs: frameMs,
                ExpiredCounts: expiredCounts,
                AllocatedBytes: allocated);
        }

        // 到期即同名重挂（保持 population 恒定的稳态形态；换成下一个名字会顶替实例上仍存活的同名 timer）
        private static void RechainExpired(PresenterTimerTable table, int[] nameIds, Entity sharedOwner)
        {
            for (int i = 0; i < table.ExpiredCount; i++)
            {
                int stableId = table.GetExpiredStableId(i);
                int nameId = table.GetExpiredNameId(i);
                int phase = 0;
                for (int t = 0; t < nameIds.Length; t++)
                {
                    if (nameIds[t] == nameId)
                    {
                        phase = t;
                        break;
                    }
                }

                int instance = stableId - 10_000;
                table.Set(stableId, default, sharedOwner, nameId, 0.32f + (instance % 50) * FrameDt + phase * 0.5f, 0f);
            }
        }

        private static void WriteReport(in TimerBenchmarkResult single, in TimerBenchmarkResult triple)
        {
            string artifactDir = Path.Combine(
                PresenterBlacksmithShowcaseTestHarness.FindRepoRoot(),
                "artifacts",
                "benchmarks",
                "presenter-timer");
            Directory.CreateDirectory(artifactDir);
            string reportPath = Path.Combine(artifactDir, "benchmark-report.md");
            var sb = new StringBuilder();
            sb.AppendLine("# Presenter Named Timer Benchmark");
            sb.AppendLine();
            sb.AppendLine("- scenario: 30k presenter instances x 1 / 3 active named timers, staggered expiry, expired timers re-chained every frame");
            sb.AppendLine("- production path: `PresenterTimerSystem.Tick` -> `PresentationEventStream(TimerExpired)` -> rule consumption (stream cleared per frame)");
            sb.AppendLine("- steady-state requirement: 0 alloc, no gen0 GC, constant timer population");
            sb.AppendLine();
            sb.AppendLine("| Scenario | Timers | Avg Tick | P95 Tick | Max Tick | Avg Expired/Frame | Alloc Bytes |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            AppendRow(sb, in single);
            AppendRow(sb, in triple);
            File.WriteAllText(reportPath, sb.ToString());
            TestContext.Out.WriteLine(sb.ToString());
        }

        private static void AppendRow(StringBuilder sb, in TimerBenchmarkResult result)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {result.Label} | {result.TimerCount} | {Average(result.FrameMs):F4} ms | {Percentile(result.FrameMs, 0.95):F4} ms | {MaxMs(result.FrameMs):F4} ms | {Average(result.ExpiredCounts):F1} | {result.AllocatedBytes} |");
        }

        private static double Average(double[] values)
        {
            double sum = 0d;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }

            return values.Length == 0 ? 0d : sum / values.Length;
        }

        private static double Average(int[] values)
        {
            double sum = 0d;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }

            return values.Length == 0 ? 0d : sum / values.Length;
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

        private static double MaxMs(double[] values)
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

        private readonly record struct TimerBenchmarkResult(
            string Label,
            int TimerCount,
            double[] FrameMs,
            int[] ExpiredCounts,
            long AllocatedBytes)
        {
            public double P95TickMs => Percentile(FrameMs, 0.95);
        }
    }
}
