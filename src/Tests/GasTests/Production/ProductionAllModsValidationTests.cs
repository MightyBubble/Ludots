using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [TestFixture]
    public sealed class ProductionAllModsValidationTests
    {
        public sealed record ModCase(
            string Name,
            string[] Mods,
            bool ShouldSucceed,
            Type ExpectedExceptionType = null,
            string ExpectedMessageContains = null);

        public static IEnumerable<TestCaseData> Cases()
        {
            yield return new TestCaseData(new ModCase(
                    "LudotsCoreMod",
                    new[] { "LudotsCoreMod" },
                    true))
                .SetName("ProdModSmoke_LudotsCoreMod");

            yield return new TestCaseData(new ModCase(
                    "MobaDemoMod",
                    new[] { "LudotsCoreMod", "MobaDemoMod" },
                    true))
                .SetName("ProdModSmoke_MobaDemoMod");

            yield return new TestCaseData(new ModCase(
                    "GasBenchmarkMod",
                    new[] { "LudotsCoreMod", "GasBenchmarkMod" },
                    true))
                .SetName("ProdModSmoke_GasBenchmarkMod");

            yield return new TestCaseData(new ModCase(
                    "PerformanceMod",
                    new[] { "LudotsCoreMod", "PerformanceMod" },
                    true))
                .SetName("ProdModSmoke_PerformanceMod");

            yield return new TestCaseData(new ModCase(
                    "PerformanceVisualizationMod",
                    new[] { "LudotsCoreMod", "PerformanceVisualizationMod" },
                    true))
                .SetName("ProdModSmoke_PerformanceVisualizationMod");

            yield return new TestCaseData(new ModCase(
                    "Physics2DPlaygroundMod",
                    new[] { "LudotsCoreMod", "Physics2DPlaygroundMod" },
                    true))
                .SetName("ProdModSmoke_Physics2DPlaygroundMod");

            yield return new TestCaseData(new ModCase(
                    "TerrainBenchmarkMod",
                    new[] { "LudotsCoreMod", "TerrainBenchmarkMod" },
                    true))
                .SetName("ProdModSmoke_TerrainBenchmarkMod");

            yield return new TestCaseData(new ModCase(
                    "Universal3CCameraMod",
                    new[] { "LudotsCoreMod", "Universal3CCameraMod" },
                    true))
                .SetName("ProdModSmoke_Universal3CCameraMod");

            yield return new TestCaseData(new ModCase(
                    "UiTestMod",
                    new[] { "LudotsCoreMod", "UiTestMod" },
                    true))
                .SetName("ProdModSmoke_UiTestMod");

            yield return new TestCaseData(new ModCase(
                    "FourXDemoMod",
                    new[] { "LudotsCoreMod", "FourXDemoMod" },
                    true))
                .SetName("ProdModSmoke_FourXDemoMod");

            yield return new TestCaseData(new ModCase(
                    "TcgDemoMod",
                    new[] { "LudotsCoreMod", "TcgDemoMod" },
                    true))
                .SetName("ProdModSmoke_TcgDemoMod");

            yield return new TestCaseData(new ModCase(
                    "ArpgDemoMod",
                    new[] { "LudotsCoreMod", "ArpgDemoMod" },
                    true))
                .SetName("ProdModSmoke_ArpgDemoMod");

            yield return new TestCaseData(new ModCase(
                    "HtmlTestMod",
                    new[] { "LudotsCoreMod", "HtmlTestMod" },
                    true))
                .SetName("ProdModSmoke_HtmlTestMod");

            yield return new TestCaseData(new ModCase(
                    "ReactiveTestMod",
                    new[] { "LudotsCoreMod", "ReactiveTestMod" },
                    true))
                .SetName("ProdModSmoke_ReactiveTestMod");

            yield return new TestCaseData(new ModCase(
                    "DepConsumerMod",
                    new[] { "LudotsCoreMod", "DepApiMod", "DepConsumerMod" },
                    true))
                .SetName("ProdModSmoke_DepConsumerMod_With_DepApiMod");

            yield return new TestCaseData(new ModCase(
                    "BrokenBuildMod",
                    new[] { "LudotsCoreMod", "BrokenBuildMod" },
                    true))
                .SetName("ProdModSmoke_BrokenBuildMod_AssetOnlyOk");

            yield return new TestCaseData(new ModCase(
                    "MissingDepMod",
                    new[] { "LudotsCoreMod", "MissingDepMod" },
                    false,
                    typeof(Exception),
                    "Missing dependency:"))
                .SetName("ProdModLoadFail_MissingDepMod");

            yield return new TestCaseData(new ModCase(
                    "CycleMods",
                    new[] { "LudotsCoreMod", "CycleAMod", "CycleBMod" },
                    false,
                    typeof(Exception),
                    "Circular dependency detected!"))
                .SetName("ProdModLoadFail_CycleAMod_CycleBMod");

            yield return new TestCaseData(new ModCase(
                    "VersionMismatchMod",
                    new[] { "LudotsCoreMod", "DepApiMod", "VersionMismatchMod" },
                    false,
                    typeof(Exception),
                    "Version mismatch:"))
                .SetName("ProdModLoadFail_VersionMismatchMod");
        }

        [TestCaseSource(nameof(Cases))]
        public void ValidateMods(ModCase modCase)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string modsRoot = Path.Combine(repoRoot, "src", "Mods");

            var modPaths = new List<string>(modCase.Mods.Length);
            for (int i = 0; i < modCase.Mods.Length; i++)
            {
                modPaths.Add(Path.Combine(modsRoot, modCase.Mods[i]));
            }

            var engine = new GameEngine();
            try
            {
                if (!modCase.ShouldSucceed)
                {
                    var ex = Assert.Throws(modCase.ExpectedExceptionType ?? typeof(Exception), () =>
                    {
                        engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
                    });
                    if (!string.IsNullOrWhiteSpace(modCase.ExpectedMessageContains))
                    {
                        Assert.That(ex.Message, Does.Contain(modCase.ExpectedMessageContains));
                    }
                    return;
                }

                engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
                InstallDummyInput(engine);

                engine.Start();
                engine.LoadMap(engine.MergedConfig.StartupMapId);

                for (int i = 0; i < 10; i++)
                {
                    engine.Tick(1f / 60f);
                }

                Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            }
            finally
            {
                engine.Dispose();
            }
        }

        private static void InstallDummyInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            engine.GlobalContext[ContextKeys.InputHandler] = inputHandler;
            engine.GlobalContext[ContextKeys.UiCaptured] = false;
        }

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
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
                if (File.Exists(candidate)) return dir;
                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("Could not locate repo root.");
        }
    }
}
