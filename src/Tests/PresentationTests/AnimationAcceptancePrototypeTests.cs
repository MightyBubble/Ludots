using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using AnimationAcceptanceMod;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class AnimationAcceptancePrototypeTests
    {
        private static readonly string[] PrototypeMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "AnimationAcceptanceMod"
        };

        [Test]
        public void AnimationAcceptanceMap_EmitsLayeredSkinnedSnapshot_ForTankAndHumanoid()
        {
            using var engine = CreateEngine(PrototypeMods);
            LoadMap(engine, AnimationAcceptanceIds.StartupMapId, frames: 12);

            PrimitiveDrawBuffer? snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer);
            Assert.That(snapshot, Is.Not.Null);

            int tankCount = 0;
            int humanoidCount = 0;
            int staticCount = 0;
            var traceLines = new List<string>();

            foreach (ref readonly var item in snapshot!.GetSpan())
            {
                if (item.RenderPath.IsSkinnedLane())
                {
                    Assert.That(item.Animator.GetControllerId(), Is.GreaterThan(0));
                    Assert.That(item.StableId, Is.GreaterThan(0));
                    Assert.That(item.Visibility, Is.EqualTo(VisualVisibility.Visible));

                    if (item.AnimatorAux.LayerMode == AnimatorAuxLayerMode.TankTurret)
                    {
                        tankCount++;
                        Assert.That(item.AnimatorAux.OverlayWeight01, Is.EqualTo(1f).Within(0.001f));
                        Assert.That(MathF.Abs(item.AnimatorAux.AimYawRad), Is.GreaterThan(0.01f));
                    }
                    else if (item.AnimatorAux.LayerMode == AnimatorAuxLayerMode.HumanoidUpperBody)
                    {
                        humanoidCount++;
                        Assert.That(item.AnimatorAux.OverlayWeight01, Is.GreaterThan(0.1f));
                        Assert.That(MathF.Abs(item.AnimatorAux.AimYawRad), Is.GreaterThan(0.01f));
                    }

                    traceLines.Add(JsonSerializer.Serialize(new
                    {
                        stable_id = item.StableId,
                        template_id = item.TemplateId,
                        lane = item.RenderPath.ToString(),
                        controller_id = item.Animator.GetControllerId(),
                        primary_state = item.Animator.GetPrimaryStateIndex(),
                        overlay_state = item.AnimatorAux.OverlayStateIndex,
                        overlay_weight = item.AnimatorAux.OverlayWeight01,
                        overlay_time = item.AnimatorAux.OverlayNormalizedTime01,
                        aim_yaw = item.AnimatorAux.AimYawRad,
                        layer_mode = item.AnimatorAux.LayerMode.ToString(),
                    }));
                }
                else if (item.RenderPath.IsStaticInstanceLane())
                {
                    staticCount++;
                }
            }

            Assert.That(tankCount, Is.EqualTo(1), "Exactly one tank prototype should be visible in the acceptance map.");
            Assert.That(humanoidCount, Is.EqualTo(1), "Exactly one humanoid upper-body prototype should be visible in the acceptance map.");
            Assert.That(staticCount, Is.EqualTo(1), "Acceptance map should keep one static baseline entity for lane separation.");

            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "animation-layered-prototypes");
            Directory.CreateDirectory(artifactDir);

            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), string.Join(Environment.NewLine, traceLines));
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport());
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathArtifact());
        }

        private static GameEngine CreateEngine(params string[] modIds)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, modIds);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);
            engine.Start();
            return engine;
        }

        private static void LoadMap(GameEngine engine, string mapId, int frames)
        {
            engine.LoadMap(mapId);
            for (int i = 0; i < frames; i++)
            {
                engine.Tick(1f / 60f);
            }
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static string BuildBattleReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario: animation-layered-prototypes");
            sb.AppendLine();
            sb.AppendLine("## Header");
            sb.AppendLine("- scenario name: raylib layered tank + humanoid prototype acceptance");
            sb.AppendLine("- build/version: local PresentationTests");
            sb.AppendLine("- seed/map/clock: deterministic fixture / animation_acceptance_entry / 12 ticks @ 60 Hz");
            sb.AppendLine($"- execution timestamp: {DateTime.UtcNow:O}");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.AppendLine("- [T+012] Tank prototype reports moving lower body with independent turret aim.");
            sb.AppendLine("- [T+012] Humanoid prototype reports locomotion lower body with upper-body overlay aim/fire.");
            sb.AppendLine("- [T+012] Static baseline entity remains on static lane.");
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success/failure decision: success");
            sb.AppendLine("- failed assertions: none");
            sb.AppendLine("- reason codes: layered_tank_visible, layered_humanoid_visible, static_lane_separate");
            return sb.ToString();
        }

        private static string BuildPathArtifact()
        {
            return
                """
                flowchart TD
                    A[start animation acceptance map] --> B[tick prototype driver system]
                    B --> C[tank base locomotion updates packed state]
                    B --> D[humanoid base locomotion updates packed state]
                    C --> E[tank aux state adds turret aim + recoil overlay]
                    D --> F[humanoid aux state adds upper-body aim/fire overlay]
                    E --> G[snapshot emits skinned tank prototype item]
                    F --> H[snapshot emits skinned humanoid prototype item]
                    A --> I[static baseline remains on static lane]
                """;
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
    }
}
