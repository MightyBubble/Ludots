using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using NUnit.Framework;
using PresenterBlacksmithShowcaseMod;
using Ludots.Tests;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// 30k Blacksmith scatter 场景的分阶段内存观测。场景合同与
    /// PresenterBlacksmithShowcaseScatterBenchmarkTests 的 scatter_30000_tight 完全一致：
    /// 30000 建筑 × 9 类 presenter 树、同种子、同半径、同 warmup/measured 帧数。
    /// 只观测不改容量；数据写到基准产物目录（默认 NUnit 工作目录）。
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("benchmark")]
    public sealed class PresenterBlacksmithScatterMemoryPhaseTests
    {
        private const int TotalBuildings = 30_000;
        private const int ScatterSeed = 31_415_926;
        private const float ScatterMinRadiusCm = 750f;
        private const float ScatterMaxRadiusCm = 2400f;
        private const int WarmupFrames = 12;
        private const int MeasuredFrames = 120;
        private const int MaxInitializationFrames = 240;
        private const int PresenterTreeKindsPerBuilding = 9;

        [Test]
        public void MemoryPhases_ScatterBlacksmith30k_WritesPhaseReport()
        {
            using var recorder = new PresentationMemoryPhaseRecorder("BLACKSMITH-SCATTER-30K");
            recorder.CapturePhase("pre-construct");

            // 引擎生命周期收在局部函数栈内：Debug 下方法局部会钉住 GC 根，
            // 出栈后再采 post-dispose 才是真实释放后活集；post-dispose 用 LOH 压缩回收实测可达对象。
            LifecycleOutcome outcome = RunEngineLifecycle(recorder);
            recorder.CapturePhase("post-dispose", compacting: true);

            string artifactDir = recorder.WriteArtifacts("artifacts/benchmarks/presenter-blacksmith-scatter-memory-phases");
            string reportPath = Path.Combine(artifactDir, "memory-phase-report.md");
            TestContext.Out.WriteLine($"runID: {recorder.RunId}");
            TestContext.Out.WriteLine(File.ReadAllText(reportPath));

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Queued, Is.EqualTo(TotalBuildings - 1), "scatter 队列数必须是 30000-1（地图自带 1 栋）。");
                Assert.That(outcome.Tree.BlacksmithEntities, Is.EqualTo(TotalBuildings));
                Assert.That(outcome.Tree.RootCount, Is.EqualTo(TotalBuildings));
                Assert.That(outcome.Tree.WorkshopLeftCount, Is.EqualTo(TotalBuildings));
                Assert.That(outcome.Tree.WorkshopRightCount, Is.EqualTo(TotalBuildings));
                Assert.That(outcome.Tree.ChimneyCount, Is.EqualTo(TotalBuildings));
                Assert.That(outcome.Tree.RouteSplineCount, Is.EqualTo(TotalBuildings));
                Assert.That(outcome.Tree.DecalCount, Is.EqualTo(TotalBuildings));
                Assert.That(outcome.Tree.WorkerCount, Is.EqualTo(TotalBuildings));
                Assert.That(outcome.Tree.BarCount, Is.EqualTo(TotalBuildings));
                Assert.That(outcome.Tree.TextCount, Is.EqualTo(TotalBuildings));
                Assert.That(outcome.EventDrops, Is.EqualTo(0), "观测期间 presentation 事件不得丢弃。");
                Assert.That(outcome.CommandDrops, Is.EqualTo(0), "观测期间 presenter 命令不得丢弃。");
                Assert.That(outcome.PrimitiveDrops, Is.EqualTo(0), "观测期间 primitive 缓冲不得丢弃。");
                Assert.That(outcome.WorldHudDrops, Is.EqualTo(0), "观测期间 world HUD 不得丢弃。");
                Assert.That(outcome.ScreenHudDrops, Is.EqualTo(0), "观测期间 screen HUD 不得丢弃。");
                Assert.That(outcome.SkinnedDrops, Is.EqualTo(0), "观测期间 skinned 缓冲不得丢弃。");
                Assert.That(recorder.AllocationSamplingActive, Is.True, "GCAllocationTick 事件源必须实际启用，否则热点证据降级。");
                Assert.That(recorder.AllocationSamplingErrors, Is.EqualTo(0), "采样回调不得有吞掉的异常。");
                Assert.That(File.Exists(reportPath), Is.True);
                Assert.That(File.Exists(Path.Combine(artifactDir, "memory-phase-samples.jsonl")), Is.True);
            });
        }

        private readonly record struct LifecycleOutcome(
            int Queued,
            BlacksmithTreeCounts Tree,
            long EventDrops,
            long CommandDrops,
            long PrimitiveDrops,
            long WorldHudDrops,
            long ScreenHudDrops,
            long SkinnedDrops);

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
                var hudProjection = PresenterBlacksmithShowcaseTestHarness.CreateHeadlessHudProjection(engine);
                PresenterBlacksmithShowcaseTestHarness.LoadMap(
                    engine,
                    PresenterBlacksmithShowcaseIds.ShowcaseMapId,
                    frames: 8);
                recorder.CapturePhase("post-map-load", engine);

                int queued = PresenterBlacksmithShowcaseTestHarness.EnqueueScatter(
                    engine,
                    TotalBuildings,
                    ScatterSeed,
                    ScatterMinRadiusCm,
                    ScatterMaxRadiusCm);
                recorder.CapturePhase("scatter-enqueued", engine);

                var spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                    ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");
                var presenters = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)
                    ?? throw new InvalidOperationException("PresenterEntityRuntime missing.");

                recorder.BeginAllocationPhase("init-burst");
                int initFrames;
                for (initFrames = 0; initFrames < MaxInitializationFrames; initFrames++)
                {
                    PresenterBlacksmithShowcaseTestHarness.TickWithHudProjection(engine, hudProjection, 1);
                    recorder.CaptureFrame("init-burst", initFrames, engine);
                    if (spawnQueue.Count == 0 &&
                        presenters.ActiveCount >= PresenterTreeKindsPerBuilding * TotalBuildings)
                    {
                        break;
                    }
                }

                Assert.That(initFrames, Is.LessThan(MaxInitializationFrames), "30k scatter 初始化在帧预算内没有收敛。");
                recorder.CapturePhase("post-init", engine);
                recorder.CaptureSubPhasePoint("init-loop-break", engine);

                var tree = CountBlacksmithTree(engine);

                recorder.BeginAllocationPhase("steady-warmup");
                for (int frame = 0; frame < WarmupFrames; frame++)
                {
                    PresenterBlacksmithShowcaseTestHarness.TickWithHudProjection(engine, hudProjection, 1);
                    recorder.CaptureFrame("steady-warmup", frame, engine);
                }

                recorder.BeginAllocationPhase("steady");
                for (int frame = 0; frame < MeasuredFrames; frame++)
                {
                    PresenterBlacksmithShowcaseTestHarness.TickWithHudProjection(engine, hudProjection, 1);
                    recorder.CaptureFrame("steady", frame, engine);
                }

                recorder.CapturePhase("post-steady", engine);

                var events = engine.GetService(CoreServiceKeys.PresentationEventStream)!;
                var commands = engine.GetService(CoreServiceKeys.PresenterCommandBuffer)!;
                var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)!;
                var worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)!;
                var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)!;
                var skinned = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer)!;

                return new LifecycleOutcome(
                    queued,
                    tree,
                    events.DroppedTotal,
                    commands.DroppedTotal,
                    primitives.DroppedTotal,
                    worldHud.DroppedTotal,
                    screenHud.DroppedTotal,
                    skinned.DroppedTotal);
            }
            finally
            {
                engine.Dispose();
            }
        }

        private readonly record struct BlacksmithTreeCounts(
            int BlacksmithEntities,
            int RootCount,
            int WorkshopLeftCount,
            int WorkshopRightCount,
            int ChimneyCount,
            int RouteSplineCount,
            int DecalCount,
            int WorkerCount,
            int BarCount,
            int TextCount);

        private static BlacksmithTreeCounts CountBlacksmithTree(GameEngine engine)
        {
            var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
                ?? throw new InvalidOperationException("PresenterDefinitionRegistry missing.");
            int rootId = definitions.GetId(PresenterBlacksmithShowcaseIds.RootDefinitionId);
            int workshopLeftId = definitions.GetId(PresenterBlacksmithShowcaseIds.WorkshopLeftDefinitionId);
            int workshopRightId = definitions.GetId(PresenterBlacksmithShowcaseIds.WorkshopRightDefinitionId);
            int chimneyId = definitions.GetId(PresenterBlacksmithShowcaseIds.ChimneyDefinitionId);
            int routeSplineId = definitions.GetId(PresenterBlacksmithShowcaseIds.RouteSplineDefinitionId);
            int decalId = definitions.GetId(PresenterBlacksmithShowcaseIds.DecalDefinitionId);
            int workerId = definitions.GetId(PresenterBlacksmithShowcaseIds.WorkerDefinitionId);
            int barId = definitions.GetId(PresenterBlacksmithShowcaseIds.DurabilityBarDefinitionId);
            int textId = definitions.GetId(PresenterBlacksmithShowcaseIds.DurabilityTextDefinitionId);

            int blacksmithEntities = 0;
            var nameQuery = new QueryDescription().WithAll<Name>();
            engine.World.Query(in nameQuery, (ref Name name) =>
            {
                if (string.Equals(name.Value, PresenterBlacksmithShowcaseIds.EntityName, StringComparison.Ordinal))
                {
                    blacksmithEntities++;
                }
            });

            int rootCount = 0;
            int workshopLeftCount = 0;
            int workshopRightCount = 0;
            int chimneyCount = 0;
            int routeSplineCount = 0;
            int decalCount = 0;
            int workerCount = 0;
            int barCount = 0;
            int textCount = 0;
            var presenterQuery = new QueryDescription().WithAll<PresenterState>();
            engine.World.Query(in presenterQuery, (ref PresenterState state) =>
            {
                CountIf(state.DefId, rootId, ref rootCount);
                CountIf(state.DefId, workshopLeftId, ref workshopLeftCount);
                CountIf(state.DefId, workshopRightId, ref workshopRightCount);
                CountIf(state.DefId, chimneyId, ref chimneyCount);
                CountIf(state.DefId, routeSplineId, ref routeSplineCount);
                CountIf(state.DefId, decalId, ref decalCount);
                CountIf(state.DefId, workerId, ref workerCount);
                CountIf(state.DefId, barId, ref barCount);
                CountIf(state.DefId, textId, ref textCount);
            });

            return new BlacksmithTreeCounts(
                blacksmithEntities,
                rootCount,
                workshopLeftCount,
                workshopRightCount,
                chimneyCount,
                routeSplineCount,
                decalCount,
                workerCount,
                barCount,
                textCount);
        }

        private static void CountIf(int actualId, int expectedId, ref int counter)
        {
            if (actualId == expectedId)
            {
                counter++;
            }
        }
    }
}
