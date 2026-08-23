using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Arch.Core;
using NUnit.Framework;
using PerformanceVisualizationMod.Runtime;
using Ludots.Core.Map;

namespace Ludots.Tests.GAS.Production
{
    [TestFixture]
    public sealed class BenchDiagScratchTests
    {
        [Test]
        public void DiagnoseMediumOverflow()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (Directory.Exists(Path.Combine(dir, "assets")) &&
                    Directory.Exists(Path.Combine(dir, "mods")))
                {
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }
            string repoRoot = dir!;
            var engine = new GameEngine();
            var modPaths = new List<string>
            {
                Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                Path.Combine(repoRoot, "mods", "PerformanceVisualizationMod"),
            };
            engine.InitializeWithConfigPipeline(modPaths, Path.Combine(repoRoot, "assets"));
            var uiRoot = new Ludots.UI.UIRoot(new Ludots.UI.Skia.SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, new Ludots.UI.Skia.SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, new Ludots.UI.Skia.SkiaImageSizeProvider());
            engine.Start();
            engine.LoadMap("visual_benchmark");
            for (int i = 0; i < 10; i++) { engine.Tick(1f / 60f); }

            object? runtimeObj = null;
            foreach (var kvp in engine.GlobalContext)
            {
                if (kvp.Value is VisualBenchmarkRuntime r) { runtimeObj = r; break; }
            }
            Assert.That(runtimeObj, Is.Not.Null, "runtime service missing");
            var runtime = (VisualBenchmarkRuntime)runtimeObj!;
            Assert.That(TryInvoke(engine, runtime, "small"), Is.True);
            for (int f = 0; f < 8; f++) { engine.Tick(1f / 60f); TestContext.Out.WriteLine($"small frame {f}: count={Count(engine)}"); }

            Assert.That(TryInvoke(engine, runtime, "medium"), Is.True);
            for (int f = 0; f < 8; f++)
            {
                try
                {
                    engine.Tick(1f / 60f);
                    TestContext.Out.WriteLine($"medium frame {f}: count={Count(engine)}");
                }
                catch (Exception ex)
                {
                    TestContext.Out.WriteLine($"medium frame {f}: THREW {ex.Message}");
                    throw;
                }
            }
        }

        private static bool TryInvoke(GameEngine engine, VisualBenchmarkRuntime runtime, string key)
        {
            var mi = typeof(VisualBenchmarkRuntime).GetMethod("TryRunScenario");
            return (bool)mi!.Invoke(runtime, new object[] { engine, key })!;
        }

        private static int Count(GameEngine engine)
        {
            int count = 0;
            MapId currentMapId = engine.CurrentMapSession?.MapId ?? default;
            var query = new QueryDescription().WithAll<MapEntity, Name>();
            engine.World.Query(in query, (ref MapEntity mapEntity, ref Name name) =>
            {
                if (mapEntity.MapId == currentMapId &&
                    name.Value != null &&
                    name.Value.StartsWith("VisualBenchmark.Subject", StringComparison.Ordinal))
                {
                    count++;
                }
            });
            return count;
        }
    }
}
