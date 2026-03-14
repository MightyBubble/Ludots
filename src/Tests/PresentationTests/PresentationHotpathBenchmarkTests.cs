using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Ludots.Core.Presentation.Hud;
using Ludots.Presentation.Skia;
using NUnit.Framework;
using SkiaSharp;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresentationHotpathBenchmarkTests
    {
        private const int VisibleEntityCount = 10240;
        private const int ItemCount = VisibleEntityCount * 2;
        private const int WarmupFrames = 12;
        private const int MeasuredFrames = 120;
        private const double TargetFrameBudgetMs = 1000d / 120d;

        [Test]
        public void Benchmark_SkiaOverlay_10kHudAndText_Writes120HzReport()
        {
            var screenHud = new ScreenHudBatchBuffer(ItemCount + 64);
            var builder = new PresentationOverlaySceneBuilder(screenHud, null, null, null, screenOverlay: null);
            var scene = new PresentationOverlayScene(ItemCount + 128);
            using var renderer = new SkiaOverlayRenderer();
            using var surface = SKSurface.Create(new SKImageInfo(1280, 720));

            BenchmarkScenarioResult steadyState = RunScenario(
                "steady_same_view",
                screenHud,
                builder,
                scene,
                renderer,
                surface,
                frameIndex => CreateFrameConfig(positionOffsetX: 0f, positionOffsetY: 0f, valueOffset: 0, pulseFill: false));

            BenchmarkScenarioResult cameraPan = RunScenario(
                "camera_pan",
                screenHud,
                builder,
                scene,
                renderer,
                surface,
                frameIndex => CreateFrameConfig(positionOffsetX: frameIndex * 1.25f, positionOffsetY: (frameIndex & 7) * 0.25f, valueOffset: 0, pulseFill: false));

            BenchmarkScenarioResult valueChurn = RunScenario(
                "value_churn",
                screenHud,
                builder,
                scene,
                renderer,
                surface,
                frameIndex => CreateFrameConfig(positionOffsetX: 0f, positionOffsetY: 0f, valueOffset: frameIndex, pulseFill: true));

            BenchmarkScenarioResult cameraPanBarsOnly = RunScenario(
                "camera_pan_bars_only",
                screenHud,
                builder,
                scene,
                renderer,
                surface,
                frameIndex => CreateFrameConfig(positionOffsetX: frameIndex * 1.25f, positionOffsetY: (frameIndex & 7) * 0.25f, valueOffset: 0, pulseFill: false, emitBars: true, emitText: false));

            BenchmarkScenarioResult cameraPanTextOnly = RunScenario(
                "camera_pan_text_only",
                screenHud,
                builder,
                scene,
                renderer,
                surface,
                frameIndex => CreateFrameConfig(positionOffsetX: frameIndex * 1.25f, positionOffsetY: (frameIndex & 7) * 0.25f, valueOffset: 0, pulseFill: false, emitBars: false, emitText: true));

            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "benchmarks", "presentation-skia-hotpath");
            Directory.CreateDirectory(artifactDir);
            string reportPath = Path.Combine(artifactDir, "benchmark-report.md");
            string tracePath = Path.Combine(artifactDir, "trace.jsonl");

            File.WriteAllText(reportPath, BuildBenchmarkReport(steadyState, cameraPan, valueChurn, cameraPanBarsOnly, cameraPanTextOnly));
            File.WriteAllText(tracePath, BuildTraceJsonl(steadyState, cameraPan, valueChurn, cameraPanBarsOnly, cameraPanTextOnly));

            TestContext.Out.WriteLine(File.ReadAllText(reportPath));

            Assert.That(File.Exists(reportPath), Is.True);
            Assert.That(File.Exists(tracePath), Is.True);
        }

        private static BenchmarkScenarioResult RunScenario(
            string scenarioName,
            ScreenHudBatchBuffer screenHud,
            PresentationOverlaySceneBuilder builder,
            PresentationOverlayScene scene,
            SkiaOverlayRenderer renderer,
            SKSurface surface,
            Func<int, FrameConfig> configFactory)
        {
            var harness = new UnderUiHostHarness();
            Warmup(screenHud, builder, scene, renderer, surface, harness, configFactory);

            double[] frameTotals = new double[MeasuredFrames];
            double[] buildTimes = new double[MeasuredFrames];
            double[] renderTimes = new double[MeasuredFrames];
            int[] dirtyLanes = new int[MeasuredFrames];
            int[] rebuiltLanes = new int[MeasuredFrames];

            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < MeasuredFrames; frame++)
            {
                FrameConfig config = configFactory(frame);
                FrameMetrics metrics = ExecuteFrame(screenHud, builder, scene, renderer, surface, harness, config);
                frameTotals[frame] = metrics.TotalMs;
                buildTimes[frame] = metrics.BuildMs;
                renderTimes[frame] = metrics.RenderMs;
                dirtyLanes[frame] = metrics.DirtyLanes;
                rebuiltLanes[frame] = metrics.RebuiltLanes;
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - startAlloc;
            return new BenchmarkScenarioResult(
                scenarioName,
                frameTotals,
                buildTimes,
                renderTimes,
                dirtyLanes,
                rebuiltLanes,
                allocatedBytes);
        }

        private static void Warmup(
            ScreenHudBatchBuffer screenHud,
            PresentationOverlaySceneBuilder builder,
            PresentationOverlayScene scene,
            SkiaOverlayRenderer renderer,
            SKSurface surface,
            UnderUiHostHarness harness,
            Func<int, FrameConfig> configFactory)
        {
            for (int frame = 0; frame < WarmupFrames; frame++)
            {
                ExecuteFrame(screenHud, builder, scene, renderer, surface, harness, configFactory(frame));
            }
        }

        private static FrameMetrics ExecuteFrame(
            ScreenHudBatchBuffer screenHud,
            PresentationOverlaySceneBuilder builder,
            PresentationOverlayScene scene,
            SkiaOverlayRenderer renderer,
            SKSurface surface,
            UnderUiHostHarness harness,
            FrameConfig config)
        {
            long totalStart = Stopwatch.GetTimestamp();
            FillScreenHud(screenHud, config);

            long buildStart = Stopwatch.GetTimestamp();
            builder.Build(scene);
            double buildMs = ElapsedMs(buildStart);

            int dirtyLaneCount = scene.DirtyLaneCount;
            double renderMs = harness.Render(scene, renderer, surface.Canvas, out int rebuiltLaneCount);
            double totalMs = (Stopwatch.GetTimestamp() - totalStart) * 1000d / Stopwatch.Frequency;

            return new FrameMetrics(totalMs, buildMs, renderMs, dirtyLaneCount, rebuiltLaneCount);
        }

        private static void FillScreenHud(ScreenHudBatchBuffer screenHud, FrameConfig config)
        {
            screenHud.Clear();

            const int columns = 64;
            const float baseBarX = 14f;
            const float baseBarY = 12f;
            const float colSpacing = 18.5f;
            const float rowSpacing = 4.25f;
            const float barWidth = 16f;
            const float barHeight = 3f;
            const int fontSize = 11;
            Vector4 barBackground = new(0.10f, 0.12f, 0.16f, 0.88f);
            Vector4 barForeground = new(0.15f, 0.82f, 0.46f, 0.96f);
            Vector4 textColor = new(0.96f, 0.96f, 0.90f, 1f);

            for (int i = 0; i < VisibleEntityCount; i++)
            {
                int row = i / columns;
                int column = i % columns;
                float x = baseBarX + (column * colSpacing) + config.PositionOffsetX;
                float y = baseBarY + (row * rowSpacing) + config.PositionOffsetY;

                float fill = config.PulseFill
                    ? 0.18f + (((i + config.ValueOffset) % 12) * 0.06f)
                    : 0.22f + ((i % 9) * 0.07f);
                if (fill > 0.98f)
                {
                    fill = 0.98f;
                }

                int numericValue = 100 + ((i + config.ValueOffset) % 900);

                if (config.EmitBars)
                {
                    screenHud.TryAdd(new ScreenHudItem
                    {
                        StableId = HudItemIdentity.ComposeStableId(i + 1, WorldHudItemKind.Bar, discriminator: 1),
                        DirtySerial = HudItemIdentity.ComposeBarDirtySerial(barWidth, barHeight, fill, barBackground, barForeground),
                        Kind = WorldHudItemKind.Bar,
                        ScreenX = x,
                        ScreenY = y,
                        Width = barWidth,
                        Height = barHeight,
                        Value0 = fill,
                        Color0 = barBackground,
                        Color1 = barForeground,
                    });
                }

                if (config.EmitText)
                {
                    screenHud.TryAdd(new ScreenHudItem
                    {
                        StableId = HudItemIdentity.ComposeStableId(i + 1, WorldHudItemKind.Text, discriminator: 2),
                        DirtySerial = HudItemIdentity.ComposeTextDirtySerial(
                            fontSize,
                            legacyStringId: 0,
                            legacyModeId: (int)WorldHudValueMode.AttributeCurrent,
                            value0: numericValue,
                            value1: 0f,
                            color: textColor,
                            packet: default),
                        Kind = WorldHudItemKind.Text,
                        ScreenX = x + 1f,
                        ScreenY = y - 9f,
                        FontSize = fontSize,
                        Color0 = textColor,
                        Value0 = numericValue,
                        Id1 = (int)WorldHudValueMode.AttributeCurrent,
                    });
                }
            }
        }

        private static FrameConfig CreateFrameConfig(float positionOffsetX, float positionOffsetY, int valueOffset, bool pulseFill, bool emitBars = true, bool emitText = true)
        {
            return new FrameConfig(positionOffsetX, positionOffsetY, valueOffset, pulseFill, emitBars, emitText);
        }

        private static string BuildBenchmarkReport(
            BenchmarkScenarioResult steadyState,
            BenchmarkScenarioResult cameraPan,
            BenchmarkScenarioResult valueChurn,
            BenchmarkScenarioResult cameraPanBarsOnly,
            BenchmarkScenarioResult cameraPanTextOnly)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Presentation Skia Hotpath Benchmark");
            sb.AppendLine();
            sb.AppendLine("- target: `120 Hz`");
            sb.AppendLine($"- frame budget: `{TargetFrameBudgetMs:F2} ms`");
            sb.AppendLine($"- workload: `{VisibleEntityCount}` bars + `{VisibleEntityCount}` text");
            sb.AppendLine("- viewport: `1280x720`");
            sb.AppendLine("- measured frames: `120` after warmup");
            sb.AppendLine();
            AppendScenario(sb, steadyState);
            AppendScenario(sb, cameraPan);
            AppendScenario(sb, valueChurn);
            AppendScenario(sb, cameraPanBarsOnly);
            AppendScenario(sb, cameraPanTextOnly);
            return sb.ToString();
        }

        private static void AppendScenario(StringBuilder sb, BenchmarkScenarioResult scenario)
        {
            sb.AppendLine($"## {scenario.Name}");
            sb.AppendLine();
            sb.AppendLine($"- avg total: `{scenario.AverageTotalMs:F3} ms`");
            sb.AppendLine($"- p95 total: `{scenario.P95TotalMs:F3} ms`");
            sb.AppendLine($"- max total: `{scenario.MaxTotalMs:F3} ms`");
            sb.AppendLine($"- avg build: `{scenario.AverageBuildMs:F3} ms`");
            sb.AppendLine($"- avg render: `{scenario.AverageRenderMs:F3} ms`");
            sb.AppendLine($"- avg fps: `{scenario.AverageFps:F1}`");
            sb.AppendLine($"- alloc per frame: `{scenario.AllocatedBytesPerFrame:F1} B`");
            sb.AppendLine($"- avg dirty lanes: `{scenario.AverageDirtyLanes:F2}`");
            sb.AppendLine($"- avg rebuilt lanes: `{scenario.AverageRebuiltLanes:F2}`");
            sb.AppendLine($"- 120 Hz pass: `{(scenario.P95TotalMs <= TargetFrameBudgetMs ? "yes" : "no")}`");
            sb.AppendLine();
        }

        private static string BuildTraceJsonl(
            BenchmarkScenarioResult steadyState,
            BenchmarkScenarioResult cameraPan,
            BenchmarkScenarioResult valueChurn,
            BenchmarkScenarioResult cameraPanBarsOnly,
            BenchmarkScenarioResult cameraPanTextOnly)
        {
            var sb = new StringBuilder();
            AppendTrace(sb, steadyState);
            AppendTrace(sb, cameraPan);
            AppendTrace(sb, valueChurn);
            AppendTrace(sb, cameraPanBarsOnly);
            AppendTrace(sb, cameraPanTextOnly);
            return sb.ToString();
        }

        private static void AppendTrace(StringBuilder sb, BenchmarkScenarioResult scenario)
        {
            for (int i = 0; i < scenario.FrameTotals.Length; i++)
            {
                sb.Append("{");
                sb.Append("\"scenario\":\"").Append(scenario.Name).Append("\",");
                sb.Append("\"frame\":").Append(i).Append(',');
                sb.Append("\"total_ms\":").Append(scenario.FrameTotals[i].ToString("F4", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"build_ms\":").Append(scenario.BuildTimes[i].ToString("F4", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"render_ms\":").Append(scenario.RenderTimes[i].ToString("F4", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"dirty_lanes\":").Append(scenario.DirtyLanes[i]).Append(',');
                sb.Append("\"rebuilt_lanes\":").Append(scenario.RebuiltLanes[i]);
                sb.AppendLine("}");
            }
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

        private readonly record struct FrameConfig(
            float PositionOffsetX,
            float PositionOffsetY,
            int ValueOffset,
            bool PulseFill,
            bool EmitBars,
            bool EmitText);

        private readonly record struct FrameMetrics(
            double TotalMs,
            double BuildMs,
            double RenderMs,
            int DirtyLanes,
            int RebuiltLanes);

        private sealed class UnderUiHostHarness
        {
            private int _lastLayerVersion = -1;
            private bool _hadContent;

            public double Render(
                PresentationOverlayScene scene,
                SkiaOverlayRenderer renderer,
                SKCanvas canvas,
                out int rebuiltLaneCount)
            {
                renderer.ResetFrameStats();

                bool hasUnderlay = scene.ContainsLayer(PresentationOverlayLayer.UnderUi);
                int layerVersion = scene.GetLayerVersion(PresentationOverlayLayer.UnderUi);
                bool refresh = (hasUnderlay || _hadContent) &&
                    (layerVersion != _lastLayerVersion || hasUnderlay != _hadContent);

                if (!refresh)
                {
                    rebuiltLaneCount = 0;
                    return 0d;
                }

                long renderStart = Stopwatch.GetTimestamp();
                canvas.Clear(SKColors.Transparent);
                if (hasUnderlay)
                {
                    renderer.Render(scene, canvas, PresentationOverlayLayer.UnderUi);
                }

                _hadContent = hasUnderlay;
                _lastLayerVersion = layerVersion;
                rebuiltLaneCount = renderer.RebuiltLaneCountLastFrame;
                return ElapsedMs(renderStart);
            }
        }

        private sealed class BenchmarkScenarioResult
        {
            public BenchmarkScenarioResult(
                string name,
                double[] frameTotals,
                double[] buildTimes,
                double[] renderTimes,
                int[] dirtyLanes,
                int[] rebuiltLanes,
                long allocatedBytes)
            {
                Name = name;
                FrameTotals = frameTotals;
                BuildTimes = buildTimes;
                RenderTimes = renderTimes;
                DirtyLanes = dirtyLanes;
                RebuiltLanes = rebuiltLanes;
                AllocatedBytes = allocatedBytes;
            }

            public string Name { get; }
            public double[] FrameTotals { get; }
            public double[] BuildTimes { get; }
            public double[] RenderTimes { get; }
            public int[] DirtyLanes { get; }
            public int[] RebuiltLanes { get; }
            public long AllocatedBytes { get; }

            public double AverageTotalMs => Average(FrameTotals);
            public double P95TotalMs => Percentile(FrameTotals, 0.95);
            public double MaxTotalMs => Max(FrameTotals);
            public double AverageBuildMs => Average(BuildTimes);
            public double AverageRenderMs => Average(RenderTimes);
            public double AverageFps => AverageTotalMs <= 0d ? 0d : 1000d / AverageTotalMs;
            public double AllocatedBytesPerFrame => FrameTotals.Length == 0 ? 0d : AllocatedBytes / (double)FrameTotals.Length;
            public double AverageDirtyLanes => Average(DirtyLanes);
            public double AverageRebuiltLanes => Average(RebuiltLanes);

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
