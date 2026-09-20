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
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

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
        public void ProjectionMap_BootstrapsPresenterInstances_AndEmitsWorldPrimitives()
        {
            using var engine = CreateEngine(ProjectionMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            var presenters = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)
                ?? throw new InvalidOperationException("PresenterEntityRuntime missing.");
            var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
                ?? throw new InvalidOperationException("PresenterDefinitionRegistry missing.");
            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
                ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer missing.");
            var snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer)
                ?? throw new InvalidOperationException("PresentationVisualSnapshotBuffer missing.");
            var skinnedBatch = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer)
                ?? throw new InvalidOperationException("PresentationSkinnedVisualBatchBuffer missing.");

            var activeDefinitions = CollectActiveDefinitionKeys(engine.World, presenters, definitions);
            Assert.That(activeDefinitions.Count, Is.GreaterThanOrEqualTo(2), "Projection fixture should bootstrap skinned + static presenter definitions.");
            Assert.That(FindPresenterOwners(engine.World, presenters).Count, Is.EqualTo(4), "Projection fixture should bootstrap one presenter-backed actor per map entity, including the off-camera team representative.");

            Assert.That(primitives.Count, Is.EqualTo(2), "Projection fixture should emit visible static presenters on the primitive lane.");
            Assert.That(snapshot.Count, Is.EqualTo(2), "Adapter-facing primitive snapshot should expose the visible static projection outputs.");
            Assert.That(skinnedBatch.Count, Is.EqualTo(1), "Projection hero fixture should emit its skinned presenter on the skinned batch lane.");

            int skinnedCount = 0;
            int staticCount = 0;
            var stableIds = new HashSet<int>();
            foreach (ref readonly PrimitiveDrawItem item in primitives.GetSpan())
            {
                Assert.That(item.StableId, Is.GreaterThan(0), "Presenter-emitted primitives must expose stable ids.");
                Assert.That(item.TemplateId, Is.GreaterThan(0), "Presenter-emitted primitives must expose definition-backed template ids.");
                stableIds.Add(item.StableId);

                if (item.RenderPath == VisualRenderPath.StaticMesh)
                {
                    staticCount++;
                    Assert.That(item.Animator.GetControllerId(), Is.EqualTo(0), "Static presenter output must stay free of animator payload.");
                }
            }

            foreach (ref readonly PrimitiveDrawItem item in snapshot.GetSpan())
            {
                Assert.That(item.StableId, Is.GreaterThan(0));
                Assert.That(item.Visibility, Is.EqualTo(VisualVisibility.Visible));
            }

            foreach (ref readonly SkinnedVisualBatchItem item in skinnedBatch.GetSpan())
            {
                Assert.That(item.StableId, Is.GreaterThan(0), "Skinned presenter output must expose a stable id.");
                Assert.That(item.TemplateId, Is.GreaterThan(0), "Skinned presenter output must expose a definition-backed template id.");
                Assert.That(item.RenderPath, Is.EqualTo(VisualRenderPath.SkinnedMesh));
                Assert.That(item.Animator.GetControllerId(), Is.GreaterThan(0), "Skinned presenter output must carry animator payload.");
                Assert.That(item.Visibility, Is.EqualTo(VisualVisibility.Visible));
                stableIds.Add(item.StableId);
                skinnedCount++;
            }

            Assert.That(stableIds.Count, Is.EqualTo(3), "Each visible presenter-backed fixture should keep a unique stable id.");
            Assert.That(skinnedCount, Is.EqualTo(1), "Projection hero fixture should emit exactly one skinned presenter output.");
            Assert.That(staticCount, Is.EqualTo(2), "Projection dummy fixtures should stay on the static presenter lane.");
        }

        [Test]
        public void ProjectionMap_PresenterSnapshot_RebuildsPerFrameWithoutRetainingDestroyedOwners()
        {
            using var engine = CreateEngine(ProjectionMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            var presenters = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)
                ?? throw new InvalidOperationException("PresenterEntityRuntime missing.");
            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
                ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer missing.");
            var snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer)
                ?? throw new InvalidOperationException("PresentationVisualSnapshotBuffer missing.");
            var skinnedBatch = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer)
                ?? throw new InvalidOperationException("PresentationSkinnedVisualBatchBuffer missing.");

            Assert.That(presenters.ActiveCount, Is.GreaterThanOrEqualTo(4));
            Assert.That(primitives.Count, Is.EqualTo(2));
            Assert.That(snapshot.Count, Is.EqualTo(2));
            Assert.That(skinnedBatch.Count, Is.EqualTo(1));

            var owners = FindPresenterOwners(engine.World, presenters);
            Assert.That(owners.Count, Is.EqualTo(4), "Projection fixture should have four live presenter owners before teardown.");
            for (int i = 0; i < owners.Count; i++)
            {
                engine.World.Destroy(owners[i]);
            }

            Tick(engine, 1);

            Assert.That(presenters.ActiveCount, Is.EqualTo(0), "Dead entity anchors should release their presenter subtree on the next runtime tick.");
            Assert.That(primitives.Count, Is.EqualTo(0), "Visible draw buffer must be rebuilt after presenter owners are destroyed.");
            Assert.That(snapshot.Count, Is.EqualTo(0), "Snapshot buffer must not retain visuals from released presenter instances.");
            Assert.That(skinnedBatch.Count, Is.EqualTo(0), "Skinned batch buffer must not retain visuals from released presenter instances.");
        }

        [Test]
        public void ProjectionMap_CameraFixture_DisablesEntityHudPresenters()
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

            Assert.That(barCount, Is.EqualTo(0), "Projection camera fixture overrides entity HUD presenters off at config level.");
            Assert.That(textCount, Is.EqualTo(0), "Projection camera fixture overrides entity HUD presenters off at config level.");
        }

        [Test]
        public void ProjectionMap_WritesPresenterLaneAcceptanceArtifacts()
        {
            using var engine = CreateEngine(ProjectionMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
                ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer missing.");
            var skinnedBatch = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer)
                ?? throw new InvalidOperationException("PresentationSkinnedVisualBatchBuffer missing.");

            int skinnedCount = 0;
            int staticCount = 0;
            int heroStableId = 0;
            int heroControllerId = 0;
            var staticStableIds = new List<int>();
            var traceLines = new List<string>();
            int eventId = 1;

            foreach (ref readonly var item in primitives.GetSpan())
            {
                if (item.RenderPath.IsStaticInstanceLane())
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
                    source = "presenter_emit",
                }));
            }

            foreach (ref readonly var item in skinnedBatch.GetSpan())
            {
                skinnedCount++;
                heroStableId = item.StableId;
                heroControllerId = item.Animator.GetControllerId();
                traceLines.Add(JsonSerializer.Serialize(new
                {
                    event_id = $"projection_map_{eventId++}",
                    tick = 5,
                    lane = item.RenderPath.ToString(),
                    stable_id = item.StableId,
                    template_id = item.TemplateId,
                    mesh_asset_id = item.MeshAssetId,
                    animator_controller_id = item.Animator.GetControllerId(),
                    source = "presenter_emit",
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

        private static HashSet<string> CollectActiveDefinitionKeys(World world, PresenterEntityRuntime presenters, PresenterDefinitionRegistry definitions)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var query = new QueryDescription().WithAll<PresenterState>();
            world.Query(in query, (Entity entity, ref PresenterState state) =>
            {
                keys.Add(definitions.GetName(state.DefId));
            });

            return keys;
        }

        private static List<Entity> FindPresenterOwners(World world, PresenterEntityRuntime presenters)
        {
            var owners = new List<Entity>();
            var seen = new HashSet<int>();
            var query = new QueryDescription().WithAll<PresenterState>();
            world.Query(in query, (Entity entity, ref PresenterState state) =>
            {
                if (state.AnchorKind != PresentationAnchorKind.Entity || !world.IsAlive(state.OwnerEntity))
                {
                    return;
                }

                if (seen.Add(state.OwnerEntity.Id))
                {
                    owners.Add(state.OwnerEntity);
                }
            });

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
            HeadlessPresentationTestHost.Install(engine);
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
                HeadlessPresentationTestHost.UpdateCamera(engine);
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
            sb.AppendLine("- scenario name: projection_map presenter skinned vs static lane contract");
            sb.AppendLine("- build/version: local PresentationTests");
            sb.AppendLine("- seed/map/clock: deterministic fixture / camera_acceptance_projection / 5 ticks @ 60 Hz");
            sb.AppendLine($"- execution timestamp: {DateTime.UtcNow:O}");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.AppendLine($"- [T+005] Hero#{heroStableId}.Emit -> lane SkinnedMesh | Animator controller {heroControllerId} bound | result = presenter skinned contract valid");
            for (int i = 0; i < staticStableIds.Count; i++)
            {
                sb.AppendLine($"- [T+005] Dummy#{staticStableIds[i]}.Emit -> lane StaticMesh | Animator none | result = static presenter lane stays separate");
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
                    A[start: load projection fixture] --> B[presentation: bootstrap presenter instances]
                    B --> C{render path}
                    C -->|SkinnedMesh| D[animator contract: emit packed animator payload]
                    C -->|StaticMesh| E[static lane: forbid animator payload]
                    D --> F[outcome: emit skinned presenter snapshot]
                    E --> G[outcome: emit static presenter snapshot]
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
