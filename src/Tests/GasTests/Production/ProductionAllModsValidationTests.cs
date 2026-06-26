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
                    new[] { "LudotsCoreMod", "CoreInputMod", "MobaDemoMod" },
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
                    "CapabilityStandardPhysics2DShowcaseMod",
                    new[] { "LudotsCoreMod", "CoreInputMod", "CameraProfilesMod", "CapabilityStandardPhysics2DShowcaseMod" },
                    true))
                .SetName("ProdModSmoke_CapabilityStandardPhysics2DShowcaseMod");

            yield return new TestCaseData(new ModCase(
                    "TerrainBenchmarkMod",
                    new[] { "LudotsCoreMod", "TerrainBenchmarkMod" },
                    true))
                .SetName("ProdModSmoke_TerrainBenchmarkMod");

            yield return new TestCaseData(new ModCase(
                    "CameraBootstrapMod",
                    new[] { "LudotsCoreMod", "CameraBootstrapMod" },
                    true))
                .SetName("ProdModSmoke_CameraBootstrapMod");

            yield return new TestCaseData(new ModCase(
                    "SharedThreeCProfilesMod",
                    new[] { "LudotsCoreMod", "SharedThreeCProfilesMod" },
                    true))
                .SetName("ProdModSmoke_SharedThreeCProfilesMod");

            yield return new TestCaseData(new ModCase(
                    "CameraProfilesMod",
                    new[] { "LudotsCoreMod", "CoreInputMod", "CameraProfilesMod" },
                    true))
                .SetName("ProdModSmoke_CameraProfilesMod");

            yield return new TestCaseData(new ModCase(
                    "VirtualCameraShotsMod",
                    new[] { "LudotsCoreMod", "VirtualCameraShotsMod" },
                    true))
                .SetName("ProdModSmoke_VirtualCameraShotsMod");

            yield return new TestCaseData(new ModCase(
                    "CameraAcceptanceMod",
                    new[] { "LudotsCoreMod", "CoreInputMod", "SharedThreeCProfilesMod", "CameraAcceptanceMod" },
                    true))
                .SetName("ProdModSmoke_CameraAcceptanceMod");

            yield return new TestCaseData(new ModCase(
                    "CameraAcceptanceHotpathEntryMod",
                    new[] { "LudotsCoreMod", "CoreInputMod", "SharedThreeCProfilesMod", "CameraAcceptanceMod", "CameraAcceptanceHotpathEntryMod" },
                    true))
                .SetName("ProdModSmoke_CameraAcceptanceHotpathEntryMod");

            yield return new TestCaseData(new ModCase(
                    "InteractionShowcaseMod",
                    new[] { "LudotsCoreMod", "CoreInputMod", "CameraProfilesMod", "EntityInfoPanelsMod", "InteractionShowcaseMod" },
                    true))
                .SetName("ProdModSmoke_InteractionShowcaseMod");

            yield return new TestCaseData(new ModCase(
                    "ChampionSkillSandboxMod",
                    new[] { "LudotsCoreMod", "CoreInputMod", "CameraProfilesMod", "EntityCommandPanelMod", "DiagnosticsOverlayMod", "ChampionSkillSandboxMod" },
                    true))
                .SetName("ProdModSmoke_ChampionSkillSandboxMod");

            yield return new TestCaseData(new ModCase(
                    "NarrativeFrontendMod",
                    new[] { "LudotsCoreMod", "NarrativeFrontendMod" },
                    true))
                .SetName("ProdModSmoke_NarrativeFrontendMod");

            yield return new TestCaseData(new ModCase(
                    "NarrativeShowcaseMod",
                    new[] { "LudotsCoreMod", "CoreInputMod", "CameraProfilesMod", "NarrativeFrontendMod", "EntityInfoPanelsMod", "InteractionShowcaseMod", "NarrativeShowcaseMod" },
                    true))
                .SetName("ProdModSmoke_NarrativeShowcaseMod");

            yield return new TestCaseData(new ModCase(
                    "RelationshipShowcaseMod",
                    new[] { "LudotsCoreMod", "CoreInputMod", "CameraProfilesMod", "NarrativeFrontendMod", "RelationshipShowcaseMod" },
                    true))
                .SetName("ProdModSmoke_RelationshipShowcaseMod");

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
                    "AIDemoMod",
                    new[] { "LudotsCoreMod", "AIDemoMod" },
                    true))
                .SetName("ProdModSmoke_AIDemoMod");

            yield return new TestCaseData(new ModCase(
                    "AIInspectorMod",
                    new[] { "LudotsCoreMod", "AIDemoMod", "AIInspectorMod" },
                    true))
                .SetName("ProdModSmoke_AIInspectorMod");

            yield return new TestCaseData(new ModCase(
                    "CombatStanceBehaviorMod",
                    new[] { "LudotsCoreMod", "CombatStanceBehaviorMod" },
                    true))
                .SetName("ProdModSmoke_CombatStanceBehaviorMod");

            yield return new TestCaseData(new ModCase(
                    "UtilityAutocastShowcaseMod",
                    new[] { "LudotsCoreMod", "AIInspectorMod", "UtilityAutocastShowcaseMod" },
                    true))
                .SetName("ProdModSmoke_UtilityAutocastShowcaseMod");

            yield return new TestCaseData(new ModCase(
                    "CombatStanceShowcaseMod",
                    new[] { "LudotsCoreMod", "CombatStanceBehaviorMod", "CombatStanceShowcaseMod" },
                    true))
                .SetName("ProdModSmoke_CombatStanceShowcaseMod");

            yield return new TestCaseData(new ModCase(
                    "DepConsumerMod",
                    new[] { "LudotsCoreMod", "DepApiMod", "DepConsumerMod" },
                    true))
                .SetName("ProdModSmoke_DepConsumerMod_With_DepApiMod");

            yield return new TestCaseData(new ModCase(
                    "BrokenBuildMod",
                    new[] { "LudotsCoreMod", "BrokenBuildMod" },
                    false,
                    typeof(FileNotFoundException),
                    "declares main assembly but the DLL was not found"))
                .SetName("ProdModLoadFail_BrokenBuildMod_MissingDeclaredMain");

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
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, modCase.Mods);

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
            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
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
