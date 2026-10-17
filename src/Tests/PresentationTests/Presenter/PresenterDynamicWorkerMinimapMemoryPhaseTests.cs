using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Scripting;
using Ludots.Presentation.Skia;
using NUnit.Framework;
using PresenterBlacksmithShowcaseMod;
using SkiaSharp;
using Ludots.Tests;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// Minimap marker large world 30k 场景的分阶段内存观测。场景合同与
    /// PresenterDynamicWorkerBenchmarkTests.Benchmark_MinimapMarkerLargeWorldShowcase_ThirtyThousandHotPath
    /// 一致：同一地图、同一 settle 判定、RTS 全图预设、同 warmup/measured 帧数与离屏 Skia 渲染路径。
    /// 独立 runID 取证（阶段表+容量高水位），不与 Blacksmith scatter 案例互代。只观测不改容量。
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("benchmark")]
    public sealed class PresenterDynamicWorkerMinimapMemoryPhaseTests
    {
        private const int SettleMaxFrames = 240;
        private const int WarmupFrames = 12;
        private const int MeasuredFrames = 90;
        private const string MetadataSectionKey = "presenterBlacksmith";
        private const string MinimapMarkerShowcaseTotalMetadataKey = "minimapMarkerShowcaseTotal";

        [Test]
        public void MemoryPhases_MinimapMarkerLargeWorld30k_WritesPhaseReport()
        {
            using var recorder = new PresentationMemoryPhaseRecorder("MINIMAP-MARKER-LARGEWORLD-30K");
            recorder.CapturePhase("pre-construct");

            // 引擎生命周期收在局部函数栈内：Debug 下方法局部会钉住 GC 根，
            // 出栈后再采 post-dispose 才是真实释放后活集；post-dispose 用 LOH 压缩回收实测可达对象。
            LifecycleOutcome outcome = RunEngineLifecycle(recorder);
            recorder.CapturePhase("post-dispose", compacting: true);

            string artifactDir = recorder.WriteArtifacts("artifacts/benchmarks/minimap-marker-large-world-memory-phases");
            string reportPath = Path.Combine(artifactDir, "memory-phase-report.md");
            TestContext.Out.WriteLine($"runID: {recorder.RunId}");
            TestContext.Out.WriteLine(File.ReadAllText(reportPath));

            Assert.Multiple(() =>
            {
                Assert.That(outcome.ExpectedMarkers, Is.EqualTo(30_000), "本观测合同就是 30k marker 案例本身。");
                Assert.That(outcome.EntityCount, Is.EqualTo(outcome.ExpectedMarkers));
                Assert.That(outcome.MarkerCount, Is.EqualTo(outcome.ExpectedMarkers));
                Assert.That(outcome.ScreenMarkerCount, Is.EqualTo(outcome.ExpectedMarkers));
                Assert.That(outcome.MaxMarkerCount, Is.EqualTo(outcome.ExpectedMarkers), "稳态每帧 marker 数必须维持满量。");
                Assert.That(outcome.MaxScreenMarkerCount, Is.EqualTo(outcome.ExpectedMarkers), "稳态每帧屏幕 marker 数必须维持满量。");
                Assert.That(outcome.MarkerDrops, Is.EqualTo(0), "观测期间 minimap marker 不得丢弃。");
                Assert.That(outcome.ScreenMarkerDrops, Is.EqualTo(0), "观测期间屏幕 marker 不得丢弃。");
                Assert.That(recorder.AllocationSamplingActive, Is.True, "GCAllocationTick 事件源必须实际启用，否则热点证据降级。");
                Assert.That(recorder.AllocationSamplingErrors, Is.EqualTo(0), "采样回调不得有吞掉的异常。");
                Assert.That(File.Exists(reportPath), Is.True);
                Assert.That(File.Exists(Path.Combine(artifactDir, "memory-phase-samples.jsonl")), Is.True);
            });
        }

        private readonly record struct LifecycleOutcome(
            int ExpectedMarkers,
            int EntityCount,
            int MarkerCount,
            int ScreenMarkerCount,
            int BucketCount,
            long MarkerDrops,
            long ScreenMarkerDrops,
            int MaxMarkerCount,
            int MaxScreenMarkerCount);

        private static LifecycleOutcome RunEngineLifecycle(PresentationMemoryPhaseRecorder recorder)
        {
            GameEngine engine = PresenterBlacksmithShowcaseTestHarness.CreateEngineStaged(
                Array.Empty<string>(),
                afterConstruct: staged => recorder.CapturePhase("post-construct", staged),
                afterConfig: staged =>
                {
                    recorder.CapturePhase("post-config", staged);
                    TestContext.Out.WriteLine(
                        $"merged presentation config: {PresentationMemoryPhaseRecorder.DescribePresentationConfig(staged)}");
                    TestContext.Out.WriteLine(
                        $"merged gas runtime capacity: {PresentationMemoryPhaseRecorder.DescribeGasRuntimeCapacityConfig(staged)}");
                },
                afterStart: staged => recorder.CapturePhase("post-start", staged));

            try
            {
                PresenterBlacksmithShowcaseTestHarness.LoadMap(
                    engine,
                    PresenterBlacksmithShowcaseIds.MinimapMarkerLargeWorldShowcaseMapId,
                    frames: 0);
                recorder.CapturePhase("post-map-load", engine);
                int expectedMarkers = engine.CurrentMapSession!.MapConfig.Metadata[MetadataSectionKey]![
                    MinimapMarkerShowcaseTotalMetadataKey]!.GetValue<int>();

                var minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
                    ?? throw new InvalidOperationException("Core MinimapRuntime missing.");
                var markerBuffer = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
                    ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
                var screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
                    ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");
                var spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                    ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");

                minimap.Visible = true;
                minimap.UseRtsFullMapPreset();

                recorder.BeginAllocationPhase("settle-wait");
                int settleFrames;
                int entityCount = 0;
                int markerCount = 0;
                for (settleFrames = 0; settleFrames < SettleMaxFrames; settleFrames++)
                {
                    PresenterBlacksmithShowcaseTestHarness.Tick(engine, 1);
                    entityCount = CountMinimapMarkerBallEntities(engine);
                    markerCount = markerBuffer.Count;
                    recorder.CaptureFrame("settle-wait", settleFrames, engine);
                    if (entityCount == expectedMarkers && markerCount == expectedMarkers)
                    {
                        break;
                    }
                }

                Assert.That(entityCount, Is.EqualTo(expectedMarkers), "30k marker ball 必须在帧预算内全部落地。");
                Assert.That(markerCount, Is.EqualTo(expectedMarkers), "marker 缓冲必须达到满量。");
                recorder.CapturePhase("post-settle", engine);
                recorder.CaptureSubPhasePoint("markers-settled", engine);
                TestContext.Out.WriteLine($"settle: frames={settleFrames}, entities={entityCount}, markers={markerCount}, spawnQueue={spawnQueue.Count}");
                Assert.That(spawnQueue.Count, Is.EqualTo(0), "稳态观测开始前 spawn 队列必须排空。");

                int width = Math.Max(1, engine.MergedConfig?.WindowWidth ?? 1280);
                int height = Math.Max(1, engine.MergedConfig?.WindowHeight ?? 720);
                using var renderer = new SkiaOverlayRenderer();
                using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
                var scene = new PresentationOverlayScene(64);

                recorder.BeginAllocationPhase("render-warmup");
                for (int frame = 0; frame < WarmupFrames; frame++)
                {
                    PresenterBlacksmithShowcaseTestHarness.Tick(engine, 1);
                    RenderMarkersOffscreen(scene, renderer, surface, screenMarkers);
                    recorder.CaptureFrame("render-warmup", frame, engine);
                }

                recorder.BeginAllocationPhase("render-steady");
                int maxMarkerCount = 0;
                int maxScreenMarkerCount = 0;
                for (int frame = 0; frame < MeasuredFrames; frame++)
                {
                    PresenterBlacksmithShowcaseTestHarness.Tick(engine, 1);
                    RenderMarkersOffscreen(scene, renderer, surface, screenMarkers);
                    recorder.CaptureFrame("render-steady", frame, engine);
                    maxMarkerCount = Math.Max(maxMarkerCount, markerBuffer.Count);
                    maxScreenMarkerCount = Math.Max(maxScreenMarkerCount, screenMarkers.Count);
                }

                recorder.CapturePhase("post-steady", engine);

                return new LifecycleOutcome(
                    expectedMarkers,
                    entityCount,
                    markerBuffer.Count,
                    screenMarkers.Count,
                    screenMarkers.BucketCount,
                    markerBuffer.DroppedTotal,
                    screenMarkers.DroppedTotal,
                    maxMarkerCount,
                    maxScreenMarkerCount);
            }
            finally
            {
                engine.Dispose();
            }
        }

        private static void RenderMarkersOffscreen(
            PresentationOverlayScene scene,
            SkiaOverlayRenderer renderer,
            SKSurface surface,
            MinimapScreenMarkerBuffer screenMarkers)
        {
            scene.BeginBuild();
            scene.SetTopMostMinimapMarkers(screenMarkers);
            scene.EndBuild();
            surface.Canvas.Clear(SKColors.Transparent);
            renderer.ResetFrameStats();
            renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.TopMost);
        }

        private static int CountMinimapMarkerBallEntities(GameEngine engine)
        {
            int total = 0;
            var query = new QueryDescription().WithAll<Name>();
            engine.World.Query(in query, (ref Name name) =>
            {
                if (string.Equals(name.Value, PresenterBlacksmithShowcaseIds.MinimapMarkerBallEntityName, StringComparison.Ordinal))
                {
                    total++;
                }
            });

            return total;
        }
    }
}
