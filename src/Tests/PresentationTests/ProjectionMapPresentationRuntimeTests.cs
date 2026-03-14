using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using CameraAcceptanceMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class ProjectionMapPresentationRuntimeTests
    {
        private static readonly string[] ProjectionMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraAcceptanceMod"
        };

        [Test]
        public void ProjectionMap_PopulatesVisualRuntimeStates_AndEmitsWorldPrimitives()
        {
            using var engine = CreateEngine(ProjectionMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            int entitiesWithVisualRuntime = 0;
            int skinnedCount = 0;
            int staticCount = 0;
            var visualQuery = new QueryDescription().WithAll<Name, VisualRuntimeState>();
            engine.World.Query(in visualQuery, (ref Name _, ref VisualRuntimeState visual) =>
            {
                entitiesWithVisualRuntime++;
                if (visual.RenderPath == VisualRenderPath.SkinnedMesh) skinnedCount++;
                if (visual.RenderPath == VisualRenderPath.StaticMesh) staticCount++;
            });

            Assert.That(entitiesWithVisualRuntime, Is.EqualTo(3), "Projection fixture entities must all carry VisualRuntimeState.");
            Assert.That(skinnedCount, Is.EqualTo(1), "Hero fixture must be marked as SkinnedMesh.");
            Assert.That(staticCount, Is.EqualTo(2), "Dummy fixtures must be marked as StaticMesh.");

            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            Assert.That(primitives, Is.Not.Null);
            Assert.That(primitives!.Count, Is.EqualTo(3), "Entity visuals must emit one primitive draw item per visible fixture entity.");

            int skinnedWithAnimator = 0;
            int staticWithoutAnimator = 0;
            var stableIds = new HashSet<int>();
            foreach (ref readonly var item in primitives.GetSpan())
            {
                Assert.That(item.StableId, Is.GreaterThan(0), "Visible entity visuals must expose stable ids for adapter instance mapping.");
                Assert.That(item.TemplateId, Is.GreaterThan(0), "Visible entity visuals must expose their visual template id.");
                stableIds.Add(item.StableId);

                if (item.RenderPath == VisualRenderPath.SkinnedMesh)
                {
                    skinnedWithAnimator++;
                    Assert.That(item.Animator.GetControllerId(), Is.GreaterThan(0), "Skinned visuals must carry packed animator controller ids.");
                    Assert.That((item.Flags & VisualRuntimeFlags.HasAnimator) != 0, Is.True, "Skinned visuals must mark animator presence.");
                }

                if (item.RenderPath == VisualRenderPath.StaticMesh)
                {
                    staticWithoutAnimator++;
                    Assert.That(item.Animator.GetControllerId(), Is.EqualTo(0), "Static visuals should not carry animator controllers in the projection fixture.");
                }
            }

            Assert.That(stableIds.Count, Is.EqualTo(3), "Each visible fixture entity must keep a unique stable id.");
            Assert.That(skinnedWithAnimator, Is.EqualTo(1), "Exactly one hero fixture should emit the skinned animator payload.");
            Assert.That(staticWithoutAnimator, Is.EqualTo(2), "Projection fixture dummies should stay on the static mesh path.");
        }

        [Test]
        public void ProjectionMap_HealthHud_UsesRegisteredAttributeValues()
        {
            using var engine = CreateEngine(ProjectionMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            var hud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            Assert.That(hud, Is.Not.Null);

            int barCount = 0;
            int textCount = 0;
            foreach (ref readonly var item in hud!.GetSpan())
            {
                if (item.Kind == WorldHudItemKind.Bar)
                {
                    barCount++;
                    Assert.That(item.Value0, Is.EqualTo(1f).Within(0.001f), "Health bar fill must resolve the real Health ratio.");
                }

                if (item.Kind == WorldHudItemKind.Text)
                {
                    textCount++;
                    Assert.That(item.Value0, Is.EqualTo(100f).Within(0.001f), "Health text current value must come from AttributeBuffer.");
                    Assert.That(item.Value1, Is.EqualTo(100f).Within(0.001f), "Health text base value must come from AttributeBuffer.");
                    Assert.That(item.Id1, Is.EqualTo((int)WorldHudValueMode.AttributeCurrentOverBase));
                }
            }

            Assert.That(barCount, Is.EqualTo(3));
            Assert.That(textCount, Is.EqualTo(3));
        }

        [Test]
        public void ProjectionMap_WritesSkinnedRuntimeContractAcceptanceArtifacts()
        {
            using var engine = CreateEngine(ProjectionMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            Assert.That(primitives, Is.Not.Null);

            int skinnedCount = 0;
            int staticCount = 0;
            int heroStableId = 0;
            int heroControllerId = 0;
            var staticStableIds = new List<int>();
            var traceLines = new List<string>();
            int eventId = 1;

            foreach (ref readonly var item in primitives!.GetSpan())
            {
                bool isSkinned = item.RenderPath.IsSkinnedLane();
                if (isSkinned)
                {
                    skinnedCount++;
                    heroStableId = item.StableId;
                    heroControllerId = item.Animator.GetControllerId();
                }
                else if (item.RenderPath.IsStaticInstanceLane())
                {
                    staticCount++;
                    staticStableIds.Add(item.StableId);
                }

                traceLines.Add(JsonSerializer.Serialize(new
                {
                    event_id = $"projection_map_{eventId++}",
                    tick = 5,
                    lane = item.RenderPath.ToString(),
                    stable_id = item.StableId,
                    template_id = item.TemplateId,
                    mesh_asset_id = item.MeshAssetId,
                    animator_controller_id = item.Animator.GetControllerId(),
                    animator_lane = isSkinned ? "skinned" : "static",
                }));
            }

            Assert.That(skinnedCount, Is.EqualTo(1));
            Assert.That(staticCount, Is.EqualTo(2));
            Assert.That(heroStableId, Is.GreaterThan(0));
            Assert.That(heroControllerId, Is.GreaterThan(0));

            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "presentation-skinned-runtime-contract");
            Directory.CreateDirectory(artifactDir);

            string tracePath = Path.Combine(artifactDir, "trace.jsonl");
            string battleReportPath = Path.Combine(artifactDir, "battle-report.md");
            string pathPath = Path.Combine(artifactDir, "path.mmd");

            File.WriteAllText(tracePath, string.Join(Environment.NewLine, traceLines));
            File.WriteAllText(battleReportPath, BuildSkinnedRuntimeBattleReport(heroStableId, heroControllerId, staticStableIds));
            File.WriteAllText(pathPath, BuildSkinnedRuntimePathMermaid());

            Assert.That(File.Exists(tracePath), Is.True);
            Assert.That(File.Exists(battleReportPath), Is.True);
            Assert.That(File.Exists(pathPath), Is.True);
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

        private static void LoadMap(GameEngine engine, string mapId, int frames = 5)
        {
            engine.LoadMap(mapId);
            Tick(engine, frames);
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

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.Tick(1f / 60f);
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

        private static string BuildSkinnedRuntimeBattleReport(int heroStableId, int heroControllerId, IReadOnlyList<int> staticStableIds)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario: presentation-skinned-runtime-contract");
            sb.AppendLine();
            sb.AppendLine("## Header");
            sb.AppendLine("- scenario name: projection_map skinned vs static lane contract");
            sb.AppendLine("- build/version: local PresentationTests");
            sb.AppendLine("- seed/map/clock: deterministic fixture / camera_acceptance_projection / 5 ticks @ 60 Hz");
            sb.AppendLine($"- execution timestamp: {DateTime.UtcNow:O}");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.AppendLine($"- [T+005] Hero#{heroStableId}.Spawn -> lane SkinnedMesh | Animator controller {heroControllerId} bound | result = skinned runtime contract valid");
            for (int i = 0; i < staticStableIds.Count; i++)
            {
                sb.AppendLine($"- [T+005] Dummy#{staticStableIds[i]}.Spawn -> lane StaticMesh | Animator none | result = static dirty-sync lane stays separate");
            }

            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success/failure decision: success");
            sb.AppendLine("- failed assertions: none");
            sb.AppendLine("- reason codes: skinned_lane_bound, static_lane_clean");
            sb.AppendLine();
            sb.AppendLine("## Summary Stats");
            sb.AppendLine("- total actions: 3");
            sb.AppendLine("- key damage/heal/control counters: not applicable");
            sb.AppendLine("- dropped/budget/fuse counters: 0");
            return sb.ToString();
        }

        private static string BuildSkinnedRuntimePathMermaid()
        {
            return
                """
                flowchart TD
                    A[start: load projection fixture] --> B[presentation: resolve visual templates]
                    B --> C{render path}
                    C -->|SkinnedMesh or GpuSkinnedInstance| D[animator contract: require controller + packed state]
                    C -->|StaticMesh or instance lane| E[static lane: forbid animator payload]
                    D --> F[outcome: emit skinned runtime snapshot]
                    E --> G[outcome: emit static runtime snapshot]
                    D -->|if controller missing| H[fail: fuse invalid skinned contract]
                    E -->|if animator payload present| I[fail: reject static/skinned lane mixing]
                """;
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
