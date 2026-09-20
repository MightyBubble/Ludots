using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.Tests;
using Ludots.UI;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using NUnit.Framework;
using UiPlayerAggregateGraphMvpShowcaseMod;
using UiPlayerAggregateGraphMvpShowcaseMod.Runtime;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
[Category("ci-gate")]
[Category("acceptance")]
public sealed class UiPlayerAggregateGraphMvpShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string BindingName = "ui_player_aggregate_graph_mvp";
    private const string PresetId = "ui_player_aggregate_graph_mvp_raylib";
    private const string ShowcaseModId = "UiPlayerAggregateGraphMvpShowcaseMod";
    private const string TestInputBackendKey = "Tests.UiPlayerAggregateGraphMvp.InputBackend";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        ShowcaseModId,
    };

    [Test]
    public void UiPlayerAggregateGraphMvp_GraphOutputDrivesPanelAndUpdatesAfterShutDown()
    {
        string repoRoot = FindRepoRoot();
        AssertLauncherBinding(repoRoot);
        AssertLauncherPreset(repoRoot);
        AssertNoPresentationEntitySum(repoRoot);
        AssertConfigHygieneArtifacts(repoRoot);

        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "ui-player-aggregate-graph-mvp");
        string screensDir = Path.Combine(artifactDir, "screens");
        AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

        var frameTimesMs = new List<double>(64);
        var evidence = new List<UiAcceptanceEvidenceFrame>(3);
        using GameEngine engine = CreateEngine(repoRoot);
        engine.Start();
        engine.LoadMap(UiPlayerAggregateGraphMvpIds.MapId);
        Tick(engine, 8, frameTimesMs);

        UIRoot uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        UiPlayerAggregateGraphMvpRuntime runtime = ResolveRuntime(engine);
        UiPlayerAggregateGraphMvpConfig config = runtime.RequireConfig(engine);

        float expectedOre = SumSeed(config.Buildings, static building => building.Ore);
        float expectedCrystal = SumSeed(config.Buildings, static building => building.Crystal);

        AssertConfigIsRuntimeAuthority(engine, config);

        UiPlayerAggregateGraphMvpSnapshot initial = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Assert.That(initial.OreTotal, Is.EqualTo(expectedOre).Within(0.001f));
            Assert.That(initial.CrystalTotal, Is.EqualTo(expectedCrystal).Within(0.001f));
            Assert.That(initial.BuildingShutDown, Is.False);
            Assert.That(initial.OreSummaryKey, Is.EqualTo(config.OreBinding.GraphOutputKey));
            Assert.That(initial.CrystalSummaryKey, Is.EqualTo(config.CrystalBinding.GraphOutputKey));
            IReadOnlyList<string> uiText = AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot);
            Assert.That(uiText, Does.Contain(config.Presentation.Title));
            Assert.That(string.Join('\n', uiText), Does.Contain("GraphOutputValueStore"));
            Assert.That(string.Join('\n', uiText), Does.Contain(config.GraphId));
            Assert.That(string.Join('\n', uiText), Does.Contain(config.OreBinding.Label));
            Assert.That(string.Join('\n', uiText), Does.Contain(config.CrystalBinding.Label));
            Assert.That(
                uiRoot.Scene!.FindByElementId(UiPlayerAggregateGraphMvpIds.PanelRootElementId),
                Is.Not.Null);
        });

        AssertGraphOutputMatchesSnapshot(engine, config, initial.OreTotal, initial.CrystalTotal);
        evidence.Add(Capture(uiRoot, screensDir, evidence.Count + 1, "initial_totals"));

        runtime.ShutDownBuilding(engine);
        Tick(engine, 4, frameTimesMs);

        float expectedOreAfter = SumSeedExcept(
            config.Buildings,
            config.ShutDownBuildingName,
            static building => building.Ore);
        float expectedCrystalAfter = SumSeedExcept(
            config.Buildings,
            config.ShutDownBuildingName,
            static building => building.Crystal);

        UiPlayerAggregateGraphMvpSnapshot afterShutDown = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(afterShutDown.BuildingShutDown, Is.True);
            Assert.That(afterShutDown.OreTotal, Is.EqualTo(expectedOreAfter).Within(0.001f));
            Assert.That(afterShutDown.CrystalTotal, Is.EqualTo(expectedCrystalAfter).Within(0.001f));
            Assert.That(afterShutDown.OreTotal, Is.LessThan(initial.OreTotal));
            Assert.That(afterShutDown.CrystalTotal, Is.LessThan(initial.CrystalTotal));
        });
        AssertGraphOutputMatchesSnapshot(engine, config, afterShutDown.OreTotal, afterShutDown.CrystalTotal);
        evidence.Add(Capture(uiRoot, screensDir, evidence.Count + 1, "after_shutdown"));

        ClickElement(uiRoot, UiPlayerAggregateGraphMvpIds.ShutDownButtonElementId);
        Tick(engine, 2, frameTimesMs);
        Assert.That(runtime.Snapshot.BuildingShutDown, Is.True);

        WriteTrace(artifactDir, config, initial, afterShutDown, frameTimesMs);
        AcceptanceUiEvidenceWriter.WriteTimelineSheet(
            evidence,
            screensDir,
            Path.Combine(artifactDir, "timeline.png"),
            "Player Aggregate Graph MVP");
        AcceptanceUiEvidenceWriter.WriteFiveWOneHMarkdown(
            "ui-player-aggregate-graph-mvp",
            evidence,
            Path.Combine(artifactDir, "5w1h.md"));
    }

    private static void AssertGraphOutputMatchesSnapshot(
        GameEngine engine,
        UiPlayerAggregateGraphMvpConfig config,
        float oreTotal,
        float crystalTotal)
    {
        GraphOutputValueStore values = engine.GetService(CoreServiceKeys.GraphOutputValueStore)
            ?? throw new InvalidOperationException("GraphOutputValueStore missing.");
        Entity owner = FindEntityByName(engine.World, config.FactionOwnerName);
        Assert.That(RequireSummaryFloat(values, owner, config.SummaryKeys.OreTotal), Is.EqualTo(oreTotal).Within(0.001f));
        Assert.That(RequireSummaryFloat(values, owner, config.SummaryKeys.CrystalTotal), Is.EqualTo(crystalTotal).Within(0.001f));
    }

    private static float RequireSummaryFloat(GraphOutputValueStore values, Entity owner, string key)
    {
        if (!values.TryGet(owner, key, out GraphOutputValueHandle handle) ||
            !values.TryGetView(handle, out GraphOutputValueView view))
        {
            throw new InvalidOperationException($"Missing GraphOutputValueStore summary '{key}'.");
        }

        Assert.That(view.Kind, Is.EqualTo(GraphOutputValueKind.Float));
        return view.FloatValue;
    }

    private static float SumSeed(
        IReadOnlyList<UiPlayerAggregateBuildingSeed> buildings,
        Func<UiPlayerAggregateBuildingSeed, float> selector)
    {
        // Test oracle only: expected totals come from authored seed config, not presentation entity iteration.
        float total = 0f;
        for (int i = 0; i < buildings.Count; i++)
        {
            total += selector(buildings[i]);
        }

        return total;
    }

    private static float SumSeedExcept(
        IReadOnlyList<UiPlayerAggregateBuildingSeed> buildings,
        string shutDownName,
        Func<UiPlayerAggregateBuildingSeed, float> selector)
    {
        float total = 0f;
        for (int i = 0; i < buildings.Count; i++)
        {
            if (string.Equals(buildings[i].Name, shutDownName, StringComparison.Ordinal))
            {
                continue;
            }

            total += selector(buildings[i]);
        }

        return total;
    }

    private static void AssertNoPresentationEntitySum(string repoRoot)
    {
        string modDir = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "ui_player_aggregate_graph_mvp",
            ShowcaseModId);
        string[] presentationPaths =
        {
            Path.Combine(modDir, "UI", "UiPlayerAggregateGraphMvpPanelController.cs"),
            Path.Combine(modDir, "Systems", "UiPlayerAggregateGraphMvpPresentationSystem.cs"),
        };

        foreach (string path in presentationPaths)
        {
            string text = File.ReadAllText(path);
            Assert.That(text, Does.Not.Contain("QueryAllMapEntities"));
            Assert.That(text, Does.Not.Contain("SumAttribute"));
            Assert.That(text, Does.Not.Contain("GetCurrent("));
            Assert.That(text, Does.Not.Contain("AttributeBuffer"));
            Assert.That(text, Does.Not.Contain("world.Query"));
        }
    }

    private static void AssertConfigHygieneArtifacts(string repoRoot)
    {
        string modDir = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "ui_player_aggregate_graph_mvp",
            ShowcaseModId);
        string mapPath = Path.Combine(modDir, "assets", "Maps", "ui_player_aggregate_graph_mvp.json");
        string graphsPath = Path.Combine(modDir, "assets", "GAS", "graphs.json");
        string configPath = Path.Combine(modDir, "assets", "UiPlayerAggregateGraphMvpShowcaseConfig.json");
        string modEntryPath = Path.Combine(modDir, "UiPlayerAggregateGraphMvpShowcaseModEntry.cs");

        using JsonDocument mapDocument = JsonDocument.Parse(File.ReadAllText(mapPath));
        foreach (JsonElement entity in mapDocument.RootElement.GetProperty("Entities").EnumerateArray())
        {
            if (!entity.TryGetProperty("Overrides", out JsonElement overrides))
            {
                continue;
            }

            Assert.That(
                overrides.TryGetProperty("AttributeBuffer", out _),
                Is.False,
                "Map Overrides must not author AttributeBuffer stock; buildings[] in showcase config is the SSOT.");
        }

        using JsonDocument configDocument = JsonDocument.Parse(File.ReadAllText(configPath));
        int playerTeamId = configDocument.RootElement.GetProperty("playerTeamId").GetInt32();
        using JsonDocument graphsDocument = JsonDocument.Parse(File.ReadAllText(graphsPath));
        int authoredGraphTeamId = RequireGraphTeamId(graphsDocument.RootElement);
        Assert.That(
            authoredGraphTeamId,
            Is.Not.EqualTo(playerTeamId),
            "graphs.json QueryFilterTeam.teamId must stay a non-authoritative compile placeholder; runtime Imm is injected from playerTeamId.");

        string modEntry = File.ReadAllText(modEntryPath);
        Assert.That(modEntry, Does.Not.Contain("AttributeRegistry.Register(\"Showcase.Resource."));
        Assert.That(modEntry, Does.Contain("bootstrapConfig.Attributes"));
    }

    private static void AssertConfigIsRuntimeAuthority(GameEngine engine, UiPlayerAggregateGraphMvpConfig config)
    {
        int graphId = GraphIdRegistry.GetId(config.GraphId);
        Assert.That(graphId, Is.GreaterThan(0));
        GraphProgramRegistry programs = engine.GetService(CoreServiceKeys.GraphProgramRegistry)
            ?? throw new InvalidOperationException("GraphProgramRegistry missing.");
        Assert.That(programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program), Is.True);
        Assert.That(TryReadInjectedTeamId(program, out int injectedTeamId), Is.True);
        Assert.That(injectedTeamId, Is.EqualTo(config.PlayerTeamId));

        int oreAttributeId = AttributeRegistry.GetId(config.Attributes.Ore);
        int crystalAttributeId = AttributeRegistry.GetId(config.Attributes.Crystal);
        Assert.That(oreAttributeId, Is.Not.EqualTo(AttributeRegistry.InvalidId));
        Assert.That(crystalAttributeId, Is.Not.EqualTo(AttributeRegistry.InvalidId));

        for (int i = 0; i < config.Buildings.Length; i++)
        {
            UiPlayerAggregateBuildingSeed seed = config.Buildings[i];
            Entity entity = FindEntityByName(engine.World, seed.Name);
            Assert.That(engine.World.Get<Team>(entity).Id, Is.EqualTo(config.PlayerTeamId), seed.Name);
            ref AttributeBuffer attributes = ref engine.World.Get<AttributeBuffer>(entity);
            Assert.That(attributes.GetCurrent(oreAttributeId), Is.EqualTo(seed.Ore).Within(0.001f), seed.Name);
            Assert.That(attributes.GetCurrent(crystalAttributeId), Is.EqualTo(seed.Crystal).Within(0.001f), seed.Name);
        }

        Entity owner = FindEntityByName(engine.World, config.FactionOwnerName);
        Assert.That(engine.World.Get<Team>(owner).Id, Is.EqualTo(config.PlayerTeamId));
    }

    private static int RequireGraphTeamId(JsonElement graphsRoot)
    {
        foreach (JsonElement graph in graphsRoot.EnumerateArray())
        {
            if (!string.Equals(graph.GetProperty("id").GetString(), "ui.panel.player.resource.aggregate", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (JsonElement node in graph.GetProperty("nodes").EnumerateArray())
            {
                if (!string.Equals(node.GetProperty("op").GetString(), "QueryFilterTeam", StringComparison.Ordinal))
                {
                    continue;
                }

                return node.GetProperty("teamId").GetInt32();
            }
        }

        throw new InvalidOperationException("Aggregate graph QueryFilterTeam teamId is missing.");
    }

    private static bool TryReadInjectedTeamId(ReadOnlySpan<GraphInstruction> program, out int teamId)
    {
        teamId = 0;
        int found = 0;
        for (int i = 0; i < program.Length; i++)
        {
            if ((GraphNodeOp)program[i].Op != GraphNodeOp.QueryFilterTeam || program[i].Flags != 0)
            {
                continue;
            }

            teamId = program[i].Imm;
            found++;
        }

        return found == 1;
    }

    private static void AssertLauncherBinding(string repoRoot)
    {
        string launcherConfig = Path.Combine(repoRoot, "launcher.config.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(launcherConfig));
        foreach (JsonElement binding in document.RootElement.GetProperty("bindings").EnumerateArray())
        {
            if (!string.Equals(binding.GetProperty("name").GetString(), BindingName, StringComparison.Ordinal))
            {
                continue;
            }

            JsonElement target = binding.GetProperty("target");
            Assert.That(target.GetProperty("type").GetString(), Is.EqualTo("path"));
            Assert.That(
                target.GetProperty("value").GetString(),
                Is.EqualTo("mods/showcases/ui_player_aggregate_graph_mvp/UiPlayerAggregateGraphMvpShowcaseMod"));
            Assert.That(target.GetProperty("projectPath").GetString(), Is.EqualTo("UiPlayerAggregateGraphMvpShowcaseMod.csproj"));
            return;
        }

        Assert.Fail($"Launcher binding '{BindingName}' is missing.");
    }

    private static void AssertLauncherPreset(string repoRoot)
    {
        string launcherPresets = Path.Combine(repoRoot, "launcher.presets.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(launcherPresets));
        foreach (JsonElement preset in document.RootElement.GetProperty("presets").EnumerateArray())
        {
            if (!string.Equals(preset.GetProperty("id").GetString(), PresetId, StringComparison.Ordinal))
            {
                continue;
            }

            Assert.That(preset.GetProperty("adapterId").GetString(), Is.EqualTo("raylib"));
            JsonElement selectors = preset.GetProperty("selectors");
            Assert.That(selectors.GetArrayLength(), Is.EqualTo(1));
            Assert.That(selectors[0].GetString(), Is.EqualTo($"${BindingName}"));
            return;
        }

        Assert.Fail($"Launcher preset '{PresetId}' is missing.");
    }

    private static GameEngine CreateEngine(string repoRoot)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods),
            Path.Combine(repoRoot, "assets"));
        InstallInput(engine);
        AcceptanceUiHostInstaller.Install(engine);
        return engine;
    }

    private static void InstallInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var backend = new TestInputBackend();
        var inputHandler = new PlayerInputHandler(backend, inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        engine.GlobalContext[TestInputBackendKey] = backend;
    }

    private static UiPlayerAggregateGraphMvpRuntime ResolveRuntime(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(UiPlayerAggregateGraphMvpIds.RuntimeServiceKey, out object? runtimeObj) &&
               runtimeObj is UiPlayerAggregateGraphMvpRuntime runtime
            ? runtime
            : throw new InvalidOperationException("UiPlayerAggregateGraphMvpRuntime missing.");
    }

    private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
    {
        for (int i = 0; i < frames; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
            frameTimesMs.Add((Stopwatch.GetTimestamp() - t0) * 1000d / Stopwatch.Frequency);
        }
    }

    private static void ClickElement(UIRoot root, string elementId)
    {
        UiScene scene = root.Scene ?? throw new InvalidOperationException("UI scene is not mounted.");
        scene.Layout(root.Width, root.Height);
        UiNode node = scene.FindByElementId(elementId)
            ?? throw new InvalidOperationException($"UI element '{elementId}' was not found.");
        Assert.That(node.ActionHandles.Count, Is.GreaterThan(0), $"UI element '{elementId}' must be clickable.");

        float x = node.LayoutRect.X + (node.LayoutRect.Width * 0.5f);
        float y = node.LayoutRect.Y + (node.LayoutRect.Height * 0.5f);
        bool downHandled = root.HandleInput(new PointerEvent
        {
            PointerId = 0,
            Action = PointerAction.Down,
            Button = PointerButton.Left,
            X = x,
            Y = y
        });
        bool upHandled = root.HandleInput(new PointerEvent
        {
            PointerId = 0,
            Action = PointerAction.Up,
            Button = PointerButton.Left,
            X = x,
            Y = y
        });

        Assert.That(downHandled || upHandled, Is.True, $"UI element '{elementId}' did not handle pointer click.");
    }

    private static Entity FindEntityByName(World world, string name)
    {
        Entity found = Entity.Null;
        var query = new QueryDescription().WithAll<Ludots.Core.Components.Name>();
        world.Query(in query, (Entity entity, ref Ludots.Core.Components.Name entityName) =>
        {
            if (found == Entity.Null &&
                string.Equals(entityName.Value, name, StringComparison.Ordinal))
            {
                found = entity;
            }
        });

        if (found == Entity.Null)
        {
            throw new InvalidOperationException($"Entity '{name}' was not found.");
        }

        return found;
    }

    private static UiAcceptanceEvidenceFrame Capture(UIRoot uiRoot, string screensDir, int order, string step)
    {
        return AcceptanceUiEvidenceWriter.CaptureFrame(
            uiRoot,
            screensDir,
            order,
            step,
            when: $"T+{order:000}",
            who: "Player reading the resource overview strip",
            what: "Confirm Ore/Crystal totals track GraphOutputValueStore after producer shut-down.",
            where: UiPlayerAggregateGraphMvpIds.MapId,
            why: "Prove Query graph aggregate projections drive the panel without presentation hand-summing.",
            how: "Boot the real showcase mod, read GraphOutputValueStore, shut down one building, re-assert totals.");
    }

    private static void WriteTrace(
        string artifactDir,
        UiPlayerAggregateGraphMvpConfig config,
        UiPlayerAggregateGraphMvpSnapshot initial,
        UiPlayerAggregateGraphMvpSnapshot afterShutDown,
        IReadOnlyList<double> frameTimesMs)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        File.WriteAllLines(
            Path.Combine(artifactDir, "trace.jsonl"),
            new[]
            {
                JsonSerializer.Serialize(new
                {
                    step = "initial",
                    graphId = config.GraphId,
                    oreKey = config.SummaryKeys.OreTotal,
                    crystalKey = config.SummaryKeys.CrystalTotal,
                    snapshot = initial
                }, options),
                JsonSerializer.Serialize(new { step = "after_shutdown", snapshot = afterShutDown }, options),
            });

        File.WriteAllLines(
            Path.Combine(artifactDir, "battle-report.md"),
            new[]
            {
                "# Player Aggregate Graph MVP Acceptance",
                string.Empty,
                "- Presentation path reads GraphOutputValueStore only; no entity-iteration sum in UI/Presentation systems.",
                $"- graph: `{config.GraphId}`",
                $"- summary keys: `{config.SummaryKeys.OreTotal}`, `{config.SummaryKeys.CrystalTotal}`",
                $"- initial ore/crystal: {initial.OreTotal:0}/{initial.CrystalTotal:0}",
                $"- after shut-down ore/crystal: {afterShutDown.OreTotal:0}/{afterShutDown.CrystalTotal:0}",
                $"- sampled frames: {frameTimesMs.Count:N0}",
            });
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "mods")) &&
                File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root.");
    }

    private sealed class TestInputBackend : IInputBackend
    {
        private readonly HashSet<string> _buttons = new(StringComparer.Ordinal);
        private Vector2 _mousePosition = new(-1f, -1f);

        public float GetAxis(string devicePath) => 0f;

        public bool GetButton(string devicePath) => _buttons.Contains(devicePath);

        public Vector2 GetMousePosition() => _mousePosition;

        public float GetMouseWheel() => 0f;

        public void SetButton(string path, bool down)
        {
            if (down)
            {
                _buttons.Add(path);
            }
            else
            {
                _buttons.Remove(path);
            }
        }

        public void EnableIME(bool enable)
        {
        }

        public void SetIMECandidatePosition(int x, int y)
        {
        }

        public string GetCharBuffer() => string.Empty;
    }
}
