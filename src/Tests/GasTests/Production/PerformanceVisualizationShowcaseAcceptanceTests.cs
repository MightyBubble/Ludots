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

namespace Ludots.Tests.GAS.Production
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PerformanceVisualizationShowcaseAcceptanceTests
    {
        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "PerformanceVisualizationMod",
        };

        [Test]
        public void VisualBenchmarkShowcase_RunClearAndRerun_MaintainsSingleScenarioTruth()
        {
            using var engine = CreateEngine();
            LoadMap(engine, VisualBenchmarkIds.MapId, frames: 10);

            object runtime = ResolveRuntime(engine);
            Assert.That(ReadBoolProperty(runtime, "IsActive"), Is.True);

            Assert.That(InvokeBool(runtime, "TryRunScenario", engine, "small"), Is.True);
            Tick(engine, 8);

            int smallCount = CountScenarioEntities(engine);
            var worldHud = RequireWorldHud(engine);
            int smallBars = CountWorldHudItems(worldHud, WorldHudItemKind.Bar);

            Assert.That(smallCount, Is.EqualTo(2048));
            Assert.That(smallBars, Is.GreaterThan(0), "Performer-driven health bars should emit world HUD bars.");
            Assert.That(worldHud.DroppedSinceClear, Is.EqualTo(0), "2K showcase lane should stay inside the world HUD budget.");

            Assert.That(InvokeBool(runtime, "TryRunScenario", engine, "medium"), Is.True);
            Tick(engine, 8);

            int mediumCount = CountScenarioEntities(engine);
            int mediumBars = CountWorldHudItems(worldHud, WorldHudItemKind.Bar);
            Assert.That(mediumCount, Is.EqualTo(8192),
                "Re-running the benchmark should replace the previous map-scoped scenario instead of accumulating a second truth.");
            Assert.That(mediumBars, Is.GreaterThan(0));
            Assert.That(worldHud.DroppedSinceClear, Is.EqualTo(0), "8K showcase lane should remain cull-gated enough to avoid HUD overflow.");

            Assert.That(InvokeBool(runtime, "TryClearScenario", engine), Is.True);
            Tick(engine, 8);
            Assert.That(CountScenarioEntities(engine), Is.EqualTo(0), "Clear should destroy all benchmark entities scoped to the current map.");
        }

        [Test]
        public void VisualBenchmarkShowcase_Run32K_PreservesPureVisualStressSemantics()
        {
            using var engine = CreateEngine();
            LoadMap(engine, VisualBenchmarkIds.MapId, frames: 10);

            object runtime = ResolveRuntime(engine);
            Assert.That(ReadBoolProperty(runtime, "IsActive"), Is.True);

            Assert.That(InvokeBool(runtime, "TryRunScenario", engine, "large"), Is.True);
            Tick(engine, 40);

            int largeCount = CountScenarioEntities(engine);
            var worldHud = RequireWorldHud(engine);
            int barCount = CountWorldHudItems(worldHud, WorldHudItemKind.Bar);

            Assert.That(largeCount, Is.EqualTo(32768), "32K showcase lane should fully realize the requested visual entity count.");
            Assert.That(barCount, Is.EqualTo(0), "32K showcase lane is pure visual stress and must not spawn performer HUD bars.");
            Assert.That(worldHud.DroppedSinceClear, Is.EqualTo(0), "Pure visual lane should not overflow the world HUD buffer.");
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

        private static bool ReadBoolProperty(object instance, string propertyName)
        {
            object? value = instance.GetType().GetProperty(propertyName)?.GetValue(instance);
            return value is bool flag && flag;
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

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            List<string> modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);

            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, new Ludots.UI.Skia.SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, new Ludots.UI.Skia.SkiaImageSizeProvider());

            engine.Start();
            return engine;
        }

        private static void LoadMap(GameEngine engine, string mapId, int frames)
        {
            engine.LoadMap(mapId);
            Tick(engine, frames);
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(1f / 60f);
            }
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.AuthoritativeInput, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
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
                var candidate = Path.Combine(dir, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("Could not locate repo root.");
        }
    }
}
