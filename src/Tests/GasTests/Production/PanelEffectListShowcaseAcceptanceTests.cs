using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI;
using Ludots.UI.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class PanelEffectListShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "effect_list_arena";
    private const string PanelTemplateId = "panel.effect.list";
    private const string ListScrollHostId = "panel-list-panel.effect.list-effects";

    [Test]
    public void PanelEffectList_ProjectsActiveEffectsWithTiming()
    {
        string repoRoot = FindRepoRoot();
        AssertResourceOnlyComposition(repoRoot);
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "panel_effect_list");
        string screensDir = Path.Combine(artifactDir, "screens");
        AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

        using GameEngine engine = CreateEngine(repoRoot);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 12);

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            string.Join(" | ", engine.TriggerManager.Errors));

        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("PanelHost missing.");
        Assert.That(panelHost.Count, Is.EqualTo(1));

        Entity hero = FindEntity(engine.World, "试炼者");
        AssertRealActiveEffects(engine.World, hero, 3);
        PanelInstanceHandle panel = FindPanel(panelHost, hero);
        Assert.That(panelHost.TryGetValues(panel, out PanelVariableSet values), Is.True);
        Assert.That(values.Get("rowCount"), Is.EqualTo(3f).Within(0.001f));

        Assert.That(
            panelHost.TryProjectListWindow(panel, "effects", PanelListViewWindow.All, out PanelListProjection effects),
            Is.True);
        Assert.That(effects.Items.Count, Is.EqualTo(3));

        string[] expectedNames = { "祝福", "迅捷", "护盾" };
        for (int i = 0; i < expectedNames.Length; i++)
        {
            Assert.That(effects.Items[i].Strings["displayName"], Is.EqualTo(expectedNames[i]));
            float remaining = effects.Items[i].Floats["remaining"];
            float total = effects.Items[i].Floats["total"];
            Assert.That(remaining, Is.GreaterThan(0f));
            Assert.That(remaining, Is.LessThan(total));
        }

        UIRoot root = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        Assert.That(root.Scene, Is.Not.Null);
        root.Scene!.Layout(root.Width, root.Height);

        Assert.That(FindNodeByClass(root.Scene.Root!, "effect-list") != null
            || FindNodeByClass(root.Scene.Root!, "control-list-scroll") != null, Is.True);
        Assert.That(root.Scene.TryGetVirtualWindow(ListScrollHostId, out UiVirtualWindow window), Is.True);
        Assert.That(window.TotalCount, Is.EqualTo(3));

        IReadOnlyList<string> texts = AcceptanceUiEvidenceWriter.ExtractUiText(root);
        Assert.That(texts, Does.Contain("祝福"));
        Assert.That(texts, Does.Contain("迅捷"));
        Assert.That(texts, Does.Contain("护盾"));

        AcceptanceUiEvidenceWriter.CaptureFrame(
            root,
            screensDir,
            order: 1,
            step: "effect-strip",
            when: "地图加载后效果条已投影",
            who: "关卡作者 / 玩家",
            what: "左侧列出试炼者身上三条效果，各自带剩余时间进度",
            where: "screen.topLeft panel.effect.list",
            why: "验证 QueryCollectActiveEffects + EffectInstance subject 竖切",
            how: "容器图 EntityCollection；panel.effect.chip subject=EffectInstance；LoadEffectTiming");
    }

    private static void AssertResourceOnlyComposition(string repoRoot)
    {
        string modPath = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "panel_effect_list",
            "PanelEffectListShowcaseMod");
        Assert.That(Directory.EnumerateFiles(modPath, "*.cs", SearchOption.TopDirectoryOnly), Is.Empty);
        Assert.That(Directory.EnumerateFiles(modPath, "*.csproj", SearchOption.TopDirectoryOnly), Is.Empty);

        using JsonDocument manifest = ParseStrict(Path.Combine(modPath, "mod.json"));
        Assert.That(manifest.RootElement.TryGetProperty("main", out _), Is.False);

        using JsonDocument templates = ParseStrict(Path.Combine(modPath, "assets", "Entities", "templates.json"));
        Assert.That(
            templates.RootElement[0].GetProperty("onSpawnEffect").GetString(),
            Is.EqualTo("Effect.Showcase.EffectList.Seed"));

        using JsonDocument effects = ParseStrict(Path.Combine(modPath, "assets", "GAS", "effects.json"));
        JsonElement runner = effects.RootElement.EnumerateArray()
            .Single(effect => effect.GetProperty("id").GetString() == "Effect.Showcase.EffectList.Seed");
        Assert.That(runner.GetProperty("lifetime").GetString(), Is.EqualTo("Instant"));
        Assert.That(
            runner.GetProperty("phaseGraphs").GetProperty("OnApply").GetProperty("main").GetString(),
            Is.EqualTo("Graph.EffectList.Effects.Seed"));
        foreach (JsonElement effect in effects.RootElement.EnumerateArray().Where(
                     effect => effect.GetProperty("id").GetString() != "Effect.Showcase.EffectList.Seed"))
        {
            Assert.That(effect.GetProperty("lifetime").GetString(), Is.EqualTo("After"));
            Assert.That(effect.GetProperty("duration").GetProperty("durationTicks").GetInt32(), Is.GreaterThan(0));
        }

        using JsonDocument graphs = ParseStrict(Path.Combine(modPath, "assets", "GAS", "graphs.json"));
        JsonElement seedGraph = graphs.RootElement.EnumerateArray()
            .Single(graph => graph.GetProperty("id").GetString() == "Graph.EffectList.Effects.Seed");
        Assert.That(
            seedGraph.GetProperty("nodes").EnumerateArray()
                .Count(node => node.GetProperty("op").GetString() == "ApplyEffectTemplate"),
            Is.EqualTo(3));
        JsonElement panelGraph = graphs.RootElement.EnumerateArray()
            .Single(graph => graph.GetProperty("id").GetString() == "Graph.EffectList.Panel.Open");
        string[] panelOps = panelGraph.GetProperty("nodes").EnumerateArray()
            .Select(node => node.GetProperty("op").GetString()!)
            .ToArray();
        string[] allowedPanelOps =
        {
            "LoadExplicitTarget",
            "CreatePanel",
            "ShowPanel",
            "ConstInt",
            "HaltReturnInt",
        };
        Assert.That(panelOps.All(allowedPanelOps.Contains), Is.True);
    }

    private static void AssertRealActiveEffects(World world, Entity hero, int expectedCount)
    {
        Assert.That(world.Has<ActiveEffectContainer>(hero), Is.True);
        ActiveEffectContainer container = world.Get<ActiveEffectContainer>(hero);
        Assert.That(container.Count, Is.EqualTo(expectedCount));
        for (int i = 0; i < container.Count; i++)
        {
            Entity effect = container.GetEntity(i);
            Assert.That(world.IsAlive(effect), Is.True);
            Assert.That(world.Has<EffectTemplateRef>(effect), Is.True);
            Assert.That(world.Has<GameplayEffect>(effect), Is.True);
            GameplayEffect timing = world.Get<GameplayEffect>(effect);
            Assert.That(timing.LifetimeKind, Is.EqualTo(EffectLifetimeKind.After));
            Assert.That(timing.RemainingTicks, Is.GreaterThan(0));
            Assert.That(timing.RemainingTicks, Is.LessThan(timing.TotalTicks));
        }
    }

    private static JsonDocument ParseStrict(string path)
    {
        return JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
    }

    private static GameEngine CreateEngine(string repoRoot)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "PanelEffectListShowcaseMod" }),
            Path.Combine(repoRoot, "assets"));
        AcceptanceUiHostInstaller.Install(engine);
        return engine;
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.Tick(DeltaTime);
        }
    }

    private static PanelInstanceHandle FindPanel(PanelHost host, Entity scope)
    {
        foreach (PanelHostInstanceInfo info in host.SnapshotInstances())
        {
            if (info.TemplateId == PanelTemplateId && info.Scope == scope)
            {
                return info.Handle;
            }
        }

        throw new InvalidOperationException($"No panel '{PanelTemplateId}' for scope {scope}.");
    }

    private static Entity FindEntity(World world, string name)
    {
        Entity found = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name component) =>
        {
            if (string.Equals(component.Value, name, StringComparison.Ordinal))
            {
                found = entity;
            }
        });

        if (found == Entity.Null)
        {
            throw new InvalidOperationException($"Entity '{name}' not found.");
        }

        return found;
    }

    private static UiNode? FindNodeByClass(UiNode node, string className)
    {
        foreach (string token in node.ClassNames)
        {
            if (string.Equals(token, className, StringComparison.Ordinal))
            {
                return node;
            }
        }

        foreach (UiNode child in node.Children)
        {
            if (FindNodeByClass(child, className) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Ludots.sln")) ||
                File.Exists(Path.Combine(dir, "showcase.registry.json")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Cannot locate repository root.");
    }
}
