using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;
using PerformanceVisualizationMod;
using PerformanceVisualizationMod.Runtime;
using Ludots.Tests;

namespace Ludots.Tests.GAS.Production
{
    /// <summary>
    /// PerformanceVisualization medium（8K Presenter HUD）场景的分阶段内存观测。
    /// 场景合同与 PerformanceVisualizationShowcaseAcceptanceTests 的 medium 分支一致：
    /// 8192 视觉 actor、64×128 阵列、间距 210cm、健康属性 + presenter 驱动 HUD bar。
    /// 只观测不改容量；含 clear→dispose 释放路径证据。
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("benchmark")]
    public sealed class PerformanceVisualizationMemoryPhaseTests
    {
        private const string MediumScenarioKey = "medium";
        private const int MediumEntityCount = 8_192;
        private const int SpawnSettleFrameBudget = 60;
        private const int ClearSettleFrameBudget = 120;
        private const int SteadyFrames = 90;

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "PerformanceVisualizationMod",
        };

        [Test]
        public void MemoryPhases_VisualBenchmarkMedium8k_WritesPhaseReport()
        {
            using var recorder = new PresentationMemoryPhaseRecorder("PV-VISUAL-MEDIUM-8K");
            recorder.CapturePhase("pre-construct");

            // 引擎生命周期收在局部函数栈内：Debug 下方法局部会钉住 GC 根，
            // 出栈后再采 post-dispose 才是真实释放后活集。
            (int settledMediumCount, int mediumBars) = RunEngineLifecycle(recorder);
            recorder.CapturePhase("post-dispose");

            string artifactDir = recorder.WriteArtifacts("artifacts/benchmarks/visual-benchmark-medium-memory-phases");
            string reportPath = Path.Combine(artifactDir, "memory-phase-report.md");
            TestContext.Out.WriteLine($"runID: {recorder.RunId}");
            TestContext.Out.WriteLine(File.ReadAllText(reportPath));

            Assert.Multiple(() =>
            {
                Assert.That(settledMediumCount, Is.EqualTo(MediumEntityCount));
                Assert.That(mediumBars, Is.GreaterThan(0), "medium 是 Presenter HUD 负载，必须产出 world HUD bar。");
                Assert.That(File.Exists(reportPath), Is.True);
                Assert.That(File.Exists(Path.Combine(artifactDir, "memory-phase-samples.jsonl")), Is.True);
            });
        }

        private static (int SettledMediumCount, int MediumBars) RunEngineLifecycle(PresentationMemoryPhaseRecorder recorder)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            List<string> modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

            var engine = new GameEngine();
            recorder.CapturePhase("post-construct", engine);
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);
            InstallHeadlessUi(engine);
            recorder.CapturePhase("post-config", engine);
            TestContext.Out.WriteLine(
                $"merged presentation config: {PresentationMemoryPhaseRecorder.DescribePresentationConfig(engine)}");
            engine.Start();
            recorder.CapturePhase("post-start", engine);

            try
            {
                engine.LoadMap(VisualBenchmarkIds.MapId);
                TickPlain(engine, 10);
                recorder.CapturePhase("post-map-load", engine);

                object runtime = ResolveRuntime(engine);
                Assert.That(InvokeBool(runtime, "TryRunScenario", engine, MediumScenarioKey), Is.True);
                recorder.CapturePhase("scenario-queued", engine);

                recorder.BeginAllocationPhase("spawn-burst");
                int spawnFrames;
                int mediumCount = 0;
                for (spawnFrames = 0; spawnFrames < SpawnSettleFrameBudget; spawnFrames++)
                {
                    TickPlain(engine, 1);
                    mediumCount = CountScenarioEntities(engine);
                    recorder.CaptureFrame("spawn-burst", spawnFrames, engine);
                    if (mediumCount == MediumEntityCount)
                    {
                        break;
                    }
                }

                Assert.That(mediumCount, Is.EqualTo(MediumEntityCount), "medium 场景必须完整落地 8192 个视觉 actor。");
                recorder.CapturePhase("post-spawn", engine);

                var worldHud = RequireWorldHud(engine);
                int mediumBars = CountWorldHudItems(worldHud, WorldHudItemKind.Bar);
                TestContext.Out.WriteLine($"medium settled: entities={mediumCount}, worldHud bars={mediumBars}, frames={spawnFrames}");

                recorder.BeginAllocationPhase("steady");
                for (int frame = 0; frame < SteadyFrames; frame++)
                {
                    TickPlain(engine, 1);
                    recorder.CaptureFrame("steady", frame, engine);
                }

                recorder.CapturePhase("post-steady", engine);

                Assert.That(InvokeBool(runtime, "TryClearScenario", engine), Is.True);
                recorder.BeginAllocationPhase("clear-burst");
                int clearFrames;
                int clearedCount = mediumCount;
                for (clearFrames = 0; clearFrames < ClearSettleFrameBudget; clearFrames++)
                {
                    TickPlain(engine, 1);
                    clearedCount = CountScenarioEntities(engine);
                    recorder.CaptureFrame("clear-burst", clearFrames, engine);
                    if (clearedCount == 0)
                    {
                        break;
                    }
                }

                Assert.That(clearedCount, Is.EqualTo(0), "clear 必须把 map-scoped 视觉 actor 全部销毁。");
                recorder.CapturePhase("post-clear", engine);
                TestContext.Out.WriteLine($"clear settled: frames={clearFrames}");

                return (mediumCount, mediumBars);
            }
            finally
            {
                engine.Dispose();
            }
        }

        private static void TickPlain(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(1f / 60f);
            }
        }

        private static object ResolveRuntime(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(VisualBenchmarkIds.RuntimeServiceKey, out object? runtimeObj) ||
                runtimeObj == null)
            {
                throw new InvalidOperationException("Visual benchmark runtime service is missing.");
            }

            return runtimeObj;
        }

        private static bool InvokeBool(object instance, string methodName, params object[] args)
        {
            object? result = instance.GetType().GetMethod(methodName)?.Invoke(instance, args);
            return result is bool flag && flag;
        }

        private static WorldHudBatchBuffer RequireWorldHud(GameEngine engine)
        {
            return engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)
                ?? throw new InvalidOperationException("WorldHudBatchBuffer missing.");
        }

        private static int CountScenarioEntities(GameEngine engine)
        {
            int count = 0;
            MapId currentMapId = engine.CurrentMapSession?.MapId ?? default;
            var query = new QueryDescription().WithAll<MapEntity, Name>();
            engine.World.Query(in query, (ref MapEntity mapEntity, ref Name name) =>
            {
                if (mapEntity.MapId == currentMapId &&
                    name.Value != null &&
                    name.Value.StartsWith(VisualBenchmarkIds.ScenarioLabel, StringComparison.Ordinal))
                {
                    count++;
                }
            });

            return count;
        }

        private static int CountWorldHudItems(WorldHudBatchBuffer worldHud, WorldHudItemKind kind)
        {
            int count = 0;
            foreach (ref readonly var item in worldHud.GetSpan())
            {
                if (item.Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.AuthoritativeInput, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static void InstallHeadlessUi(GameEngine engine)
        {
            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, new SkiaImageSizeProvider());
        }

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public System.Numerics.Vector2 GetMousePosition() => System.Numerics.Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private static string FindRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                string candidate = Path.Combine(dir, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("Repository root not found.");
            }

            throw new InvalidOperationException("Repository root not found.");
        }
    }
}
