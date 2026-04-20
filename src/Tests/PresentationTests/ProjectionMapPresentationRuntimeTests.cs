using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using CameraAcceptanceMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
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
            "SharedThreeCProfilesMod",
            "CameraAcceptanceMod"
        };

        [Test]
        public void ProjectionMap_BootstrapsPerformerInstances_AndEmitsWorldPrimitives()
        {
            using var engine = CreateEngine(ProjectionMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            var performers = engine.GetService(CoreServiceKeys.PerformerInstanceBuffer)
                ?? throw new InvalidOperationException("PerformerInstanceBuffer missing.");
            var definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
                ?? throw new InvalidOperationException("PerformerDefinitionRegistry missing.");
            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
                ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer missing.");
            var snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer)
                ?? throw new InvalidOperationException("PresentationVisualSnapshotBuffer missing.");

            var activeDefinitions = CollectActiveDefinitionKeys(performers, definitions);
            Assert.That(activeDefinitions.Count, Is.GreaterThanOrEqualTo(2), "Projection fixture should bootstrap skinned + static performer definitions.");
            Assert.That(FindPerformerOwners(engine.World, performers).Count, Is.EqualTo(3), "Projection fixture should bootstrap one performer-backed actor per map entity.");

            Assert.That(primitives.Count, Is.EqualTo(3), "Projection fixture should emit one visible primitive per visible performer-backed actor.");
            Assert.That(snapshot.Count, Is.EqualTo(3), "Adapter-facing snapshot should expose all visible projection fixture outputs.");

            int skinnedCount = 0;
            int staticCount = 0;
            var stableIds = new HashSet<int>();
            foreach (ref readonly PrimitiveDrawItem item in primitives.GetSpan())
            {
                Assert.That(item.StableId, Is.GreaterThan(0), "Performer-emitted primitives must expose stable ids.");
                Assert.That(item.TemplateId, Is.GreaterThan(0), "Performer-emitted primitives must expose definition-backed template ids.");
                stableIds.Add(item.StableId);

                if (item.RenderPath == VisualRenderPath.SkinnedMesh)
                {
                    skinnedCount++;
                    Assert.That(item.Animator.GetControllerId(), Is.GreaterThan(0), "Skinned performer output must carry animator payload.");
                }

                if (item.RenderPath == VisualRenderPath.StaticMesh)
                {
                    staticCount++;
                    Assert.That(item.Animator.GetControllerId(), Is.EqualTo(0), "Static performer output must stay free of animator payload.");
                }
            }

            foreach (ref readonly PrimitiveDrawItem item in snapshot.GetSpan())
            {
                Assert.That(item.StableId, Is.GreaterThan(0));
                Assert.That(item.Visibility, Is.EqualTo(VisualVisibility.Visible));
            }

            Assert.That(stableIds.Count, Is.EqualTo(3), "Each visible performer-backed fixture should keep a unique stable id.");
            Assert.That(skinnedCount, Is.EqualTo(1), "Projection hero fixture should emit exactly one skinned performer output.");
            Assert.That(staticCount, Is.EqualTo(2), "Projection dummy fixtures should stay on the static performer lane.");
        }

        [Test]
        public void ProjectionMap_PerformerSnapshot_RebuildsPerFrameWithoutRetainingDestroyedOwners()
        {
            using var engine = CreateEngine(ProjectionMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            var performers = engine.GetService(CoreServiceKeys.PerformerInstanceBuffer)
                ?? throw new InvalidOperationException("PerformerInstanceBuffer missing.");
            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
                ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer missing.");
            var snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer)
                ?? throw new InvalidOperationException("PresentationVisualSnapshotBuffer missing.");

            Assert.That(performers.ActiveCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(primitives.Count, Is.EqualTo(3));
            Assert.That(snapshot.Count, Is.EqualTo(3));

            var owners = FindPerformerOwners(engine.World, performers);
            Assert.That(owners.Count, Is.EqualTo(3), "Projection fixture should have three live performer owners before teardown.");
            for (int i = 0; i < owners.Count; i++)
            {
                engine.World.Destroy(owners[i]);
            }

            Tick(engine, 1);

            Assert.That(performers.ActiveCount, Is.EqualTo(0), "Dead entity anchors should release their performer subtree on the next runtime tick.");
            Assert.That(primitives.Count, Is.EqualTo(0), "Visible draw buffer must be rebuilt after performer owners are destroyed.");
            Assert.That(snapshot.Count, Is.EqualTo(0), "Snapshot buffer must not retain visuals from released performer instances.");
        }

        [Test]
        public void ProjectionMap_CameraFixture_DisablesEntityHudPerformers()
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
                }

                if (item.Kind == WorldHudItemKind.Text)
                {
                    textCount++;
                }
            }

            Assert.That(barCount, Is.EqualTo(0), "Projection camera fixture overrides entity HUD performers off at config level.");
            Assert.That(textCount, Is.EqualTo(0), "Projection camera fixture overrides entity HUD performers off at config level.");
        }

        [Test]
        public void ProjectionMap_WritesPerformerLaneAcceptanceArtifacts()
        {
            using var engine = CreateEngine(ProjectionMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
                ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer missing.");

            int skinnedCount = 0;
            int staticCount = 0;
            int heroStableId = 0;
            int heroControllerId = 0;
            var staticStableIds = new List<int>();
            var traceLines = new List<string>();
            int eventId = 1;

            foreach (ref readonly var item in primitives.GetSpan())
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
                    source = "performer_emit",
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

        private static HashSet<string> CollectActiveDefinitionKeys(PerformerInstanceBuffer performers, PerformerDefinitionRegistry definitions)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int handle = 0; handle < performers.Capacity; handle++)
            {
                if (!performers.IsActive(handle))
                {
                    continue;
                }

                keys.Add(definitions.GetName(performers.Get(handle).DefId));
            }

            return keys;
        }

        private static List<Entity> FindPerformerOwners(World world, PerformerInstanceBuffer performers)
        {
            var owners = new List<Entity>();
            var seen = new HashSet<int>();
            for (int handle = 0; handle < performers.Capacity; handle++)
            {
                if (!performers.IsActive(handle))
                {
                    continue;
                }

                PerformerInstance instance = performers.Get(handle);
                if (instance.AnchorKind != PresentationAnchorKind.Entity || !world.IsAlive(instance.Owner))
                {
                    continue;
                }

                if (seen.Add(instance.Owner.Id))
                {
                    owners.Add(instance.Owner);
                }
            }

            return owners;
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
            sb.AppendLine("- scenario name: projection_map performer skinned vs static lane contract");
            sb.AppendLine("- build/version: local PresentationTests");
            sb.AppendLine("- seed/map/clock: deterministic fixture / camera_acceptance_projection / 5 ticks @ 60 Hz");
            sb.AppendLine($"- execution timestamp: {DateTime.UtcNow:O}");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.AppendLine($"- [T+005] Hero#{heroStableId}.Emit -> lane SkinnedMesh | Animator controller {heroControllerId} bound | result = performer skinned contract valid");
            for (int i = 0; i < staticStableIds.Count; i++)
            {
                sb.AppendLine($"- [T+005] Dummy#{staticStableIds[i]}.Emit -> lane StaticMesh | Animator none | result = static performer lane stays separate");
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
                    A[start: load projection fixture] --> B[presentation: bootstrap performer instances]
                    B --> C{render path}
                    C -->|SkinnedMesh| D[animator contract: emit packed animator payload]
                    C -->|StaticMesh| E[static lane: forbid animator payload]
                    D --> F[outcome: emit skinned performer snapshot]
                    E --> G[outcome: emit static performer snapshot]
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
