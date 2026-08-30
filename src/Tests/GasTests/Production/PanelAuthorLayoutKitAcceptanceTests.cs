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
using Ludots.Core.Gameplay.GAS.Registry;
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
public sealed class PanelAuthorLayoutKitAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "author_layout_kit_classroom";

    [Test]
    public void AuthorLayoutKit_ShowsListGridColumnWithTimingAndStacks()
    {
        string repoRoot = FindRepoRoot();
        AssertResourceOnlyComposition(repoRoot);
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "panel_author_layout_kit");
        string screensDir = Path.Combine(artifactDir, "screens");
        AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

        using GameEngine engine = CreateEngine(repoRoot);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 20);

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            string.Join(" | ", engine.TriggerManager.Errors));

        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("PanelHost missing.");
        Assert.That(panelHost.Count, Is.EqualTo(3), "classroom shows list + grid + column");

        Entity hero = FindEntity(engine.World, "试炼者");
        AssertRealActiveEffects(engine.World, hero);
        AssertBag(panelHost, hero, "panel.kit.effect.list", PanelPresentMode.List);
        AssertBag(panelHost, hero, "panel.kit.effect.grid", PanelPresentMode.Grid);
        AssertBag(panelHost, hero, "panel.kit.effect.column", PanelPresentMode.Column);

        UIRoot root = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        Assert.That(root.Scene, Is.Not.Null);
        root.Scene!.Layout(root.Width, root.Height);

        IReadOnlyList<string> texts = AcceptanceUiEvidenceWriter.ExtractUiText(root);
        Assert.That(texts, Does.Contain("竖列 · list"));
        Assert.That(texts, Does.Contain("网格 · grid"));
        Assert.That(texts, Does.Contain("横栏 · column"));
        Assert.That(texts, Does.Contain("祝福"));
        Assert.That(texts, Does.Contain("×3"));

        Assert.That(
            root.Scene!.EnumerateVisualNodes().Any(node => node.Kind == UiNodeKind.Image),
            Is.True,
            "classroom chips must render image controls");
        UiNode[] progressTracks = root.Scene.EnumerateVisualNodes()
            .Where(node => HasClass(node, "progress-track"))
            .ToArray();
        Assert.That(progressTracks, Is.Not.Empty, "default panel stylesheet must render progress tracks");
        Assert.That(progressTracks, Is.All.Matches<UiNode>(node =>
            node.Style.Height == UiLength.Px(10f) &&
            node.Style.BackgroundColor.Alpha > 0));

        AssertColumnChipsStayInsidePanelFrame(root);

        AcceptanceUiEvidenceWriter.CaptureFrame(
            root,
            screensDir,
            order: 1,
            step: "classroom",
            when: "三面板教室已投影",
            who: "作者",
            what: "同一芯片 list/grid/column + 剩余时间与层数",
            where: "screen",
            why: "开箱布局套件验收",
            how: "present 封闭集 + LoadEffectTiming/LoadEffectStack");
    }

    private static void AssertBag(
        PanelHost panelHost,
        Entity hero,
        string panelId,
        PanelPresentMode expectedPresent)
    {
        PanelInstanceHandle panel = FindPanel(panelHost, hero, panelId);
        Assert.That(
            panelHost.TryProjectListWindow(panel, "effects", PanelListViewWindow.All, out PanelListProjection effects),
            Is.True);
        Assert.That(effects.TotalCount, Is.EqualTo(4));
        Assert.That(effects.Items.Count, Is.EqualTo(4));

        Assert.That(effects.Items[0].Strings["displayName"], Is.EqualTo("祝福"));
        Assert.That(effects.Items[0].Strings["imageId"], Is.EqualTo("effect.icon.祝福"));
        AssertTiming(effects.Items[0]);
        Assert.That(effects.Items[0].Floats["stacks"], Is.EqualTo(3f).Within(0.001f));

        Assert.That(effects.Items[2].Strings["displayName"], Is.EqualTo("护盾"));
        AssertTiming(effects.Items[2]);
        Assert.That(effects.Items[2].Floats["stacks"], Is.EqualTo(2f).Within(0.001f));

        // Present mode is config-side; projection content is shared. Spot-check template id.
        Assert.That(panelId, Does.Contain(expectedPresent switch
        {
            PanelPresentMode.List => "list",
            PanelPresentMode.Grid => "grid",
            PanelPresentMode.Column => "column",
            _ => "?",
        }));
    }

    private static void AssertTiming(PanelListItemProjection item)
    {
        float remaining = item.Floats["remaining"];
        float total = item.Floats["total"];
        Assert.That(remaining, Is.GreaterThan(0f));
        Assert.That(remaining, Is.LessThan(total));
    }

    private static void AssertResourceOnlyComposition(string repoRoot)
    {
        string modPath = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "panel_author_layout_kit",
            "PanelAuthorLayoutKitShowcaseMod");
        Assert.That(Directory.EnumerateFiles(modPath, "*.cs", SearchOption.TopDirectoryOnly), Is.Empty);
        Assert.That(Directory.EnumerateFiles(modPath, "*.csproj", SearchOption.TopDirectoryOnly), Is.Empty);

        using JsonDocument manifest = ParseStrict(Path.Combine(modPath, "mod.json"));
        Assert.That(manifest.RootElement.TryGetProperty("main", out _), Is.False);

        using JsonDocument templates = ParseStrict(Path.Combine(modPath, "assets", "Entities", "templates.json"));
        Assert.That(
            templates.RootElement[0].GetProperty("onSpawnEffect").GetString(),
            Is.EqualTo("Effect.Showcase.AuthorLayoutKit.Seed"));

        using JsonDocument effects = ParseStrict(Path.Combine(modPath, "assets", "GAS", "effects.json"));
        JsonElement runner = effects.RootElement.EnumerateArray()
            .Single(effect => effect.GetProperty("id").GetString() == "Effect.Showcase.AuthorLayoutKit.Seed");
        Assert.That(runner.GetProperty("lifetime").GetString(), Is.EqualTo("Instant"));
        Assert.That(
            runner.GetProperty("phaseGraphs").GetProperty("OnApply").GetProperty("main").GetString(),
            Is.EqualTo("Graph.AuthorLayoutKit.Effects.Seed"));
        foreach (JsonElement effect in effects.RootElement.EnumerateArray().Where(
                     effect => !effect.GetProperty("id").GetString()!
                         .StartsWith("Effect.Showcase.AuthorLayoutKit.Seed", StringComparison.Ordinal)))
        {
            Assert.That(effect.GetProperty("lifetime").GetString(), Is.EqualTo("After"));
            Assert.That(effect.GetProperty("duration").GetProperty("durationTicks").GetInt32(), Is.GreaterThan(0));
        }

        AssertStackConfig(effects, "祝福");
        AssertStackConfig(effects, "护盾");

        using JsonDocument graphs = ParseStrict(Path.Combine(modPath, "assets", "GAS", "graphs.json"));
        string[] applications = graphs.RootElement.EnumerateArray()
            .Where(graph => graph.GetProperty("id").GetString()!
                .StartsWith("Graph.AuthorLayoutKit.Effects.Seed", StringComparison.Ordinal))
            .SelectMany(graph => graph.GetProperty("nodes").EnumerateArray())
            .Where(node => node.GetProperty("op").GetString() == "ApplyEffectTemplate")
            .Select(node => node.GetProperty("effectTemplate").GetString()!)
            .Where(id => id is "祝福" or "迅捷" or "护盾" or "洞察")
            .ToArray();
        Assert.That(applications.Count(id => id == "祝福"), Is.EqualTo(3));
        Assert.That(applications.Count(id => id == "护盾"), Is.EqualTo(2));
        Assert.That(applications.Count(id => id == "迅捷"), Is.EqualTo(1));
        Assert.That(applications.Count(id => id == "洞察"), Is.EqualTo(1));

        JsonElement panelGraph = graphs.RootElement.EnumerateArray()
            .Single(graph => graph.GetProperty("id").GetString() == "Graph.AuthorLayoutKit.Panels.Open");
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

    private static void AssertStackConfig(JsonDocument effects, string effectId)
    {
        JsonElement effect = effects.RootElement.EnumerateArray()
            .Single(candidate => candidate.GetProperty("id").GetString() == effectId);
        JsonElement stack = effect.GetProperty("stack");
        Assert.That(stack.GetProperty("limit").GetInt32(), Is.GreaterThan(1));
        Assert.That(stack.GetProperty("policy").GetString(), Is.EqualTo("RefreshDuration"));
        Assert.That(stack.GetProperty("overflowPolicy").GetString(), Is.EqualTo("RejectNew"));
    }

    private static void AssertRealActiveEffects(World world, Entity hero)
    {
        Assert.That(world.Has<ActiveEffectContainer>(hero), Is.True);
        ActiveEffectContainer container = world.Get<ActiveEffectContainer>(hero);
        Assert.That(container.Count, Is.EqualTo(4));
        var stacksByTemplate = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < container.Count; i++)
        {
            Entity effect = container.GetEntity(i);
            Assert.That(world.IsAlive(effect), Is.True);
            Assert.That(world.Has<EffectTemplateRef>(effect), Is.True);
            Assert.That(world.Has<GameplayEffect>(effect), Is.True);

            EffectTemplateRef templateRef = world.Get<EffectTemplateRef>(effect);
            string templateName = EffectTemplateIdRegistry.GetName(templateRef.TemplateId);
            GameplayEffect timing = world.Get<GameplayEffect>(effect);
            Assert.That(timing.LifetimeKind, Is.EqualTo(EffectLifetimeKind.After));
            Assert.That(timing.RemainingTicks, Is.GreaterThan(0));
            Assert.That(timing.RemainingTicks, Is.LessThan(timing.TotalTicks));
            stacksByTemplate[templateName] = world.Has<EffectStack>(effect)
                ? world.Get<EffectStack>(effect).Count
                : 1;
        }

        Assert.That(stacksByTemplate, Is.EquivalentTo(new Dictionary<string, int>
        {
            ["祝福"] = 3,
            ["迅捷"] = 1,
            ["护盾"] = 2,
            ["洞察"] = 1,
        }));
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
            RepoModPaths.ResolveExplicit(
                repoRoot,
                new[] { "LudotsCoreMod", "PanelAuthorLayoutKitShowcaseMod" }),
            Path.Combine(repoRoot, "assets"));
        AcceptanceUiHostInstaller.Install(engine);
        return engine;
    }

    private static void AssertColumnChipsStayInsidePanelFrame(UIRoot root)
    {
        UiScene scene = root.Scene
            ?? throw new InvalidOperationException("UIRoot.Scene missing after layout.");

        UiNode? panel = null;
        foreach (UiNode node in scene.EnumerateVisualNodes())
        {
            if (HasClass(node, "panel-kit-effect-column"))
            {
                panel = node;
                break;
            }
        }

        Assert.That(panel, Is.Not.Null, "column panel node missing");
        UiRect frame = panel!.LayoutRect;

        var chips = new List<UiNode>();
        foreach (UiNode node in scene.EnumerateVisualNodes())
        {
            if (HasClass(node, "list-item-column"))
            {
                chips.Add(node);
            }
        }

        Assert.That(chips.Count, Is.EqualTo(4), "column present must keep all four chips in the layout tree");
        // Panel chrome: border 2 + padding 12 — chips must stay in the content box, not flush the outer edge.
        const float inset = 10f;
        const float epsilon = 1.5f;
        float contentLeft = frame.X + inset;
        float contentRight = frame.X + frame.Width - inset;
        float contentTop = frame.Y + inset;
        float contentBottom = frame.Y + frame.Height - inset;
        foreach (UiNode chip in chips)
        {
            UiRect box = chip.LayoutRect;
            Assert.That(box.Width, Is.GreaterThan(8f), "column chip collapsed");
            Assert.That(box.X, Is.GreaterThanOrEqualTo(contentLeft - epsilon),
                $"column chip left {box.X} escapes content left {contentLeft}");
            Assert.That(box.X + box.Width, Is.LessThanOrEqualTo(contentRight + epsilon),
                $"column chip right {box.X + box.Width} escapes content right {contentRight}");
            Assert.That(box.Y, Is.GreaterThanOrEqualTo(contentTop - epsilon),
                $"column chip top {box.Y} escapes content top {contentTop}");
            Assert.That(box.Y + box.Height, Is.LessThanOrEqualTo(contentBottom + epsilon),
                $"column chip bottom {box.Y + box.Height} escapes content bottom {contentBottom}");
        }
    }

    private static bool HasClass(UiNode node, string className)
    {
        for (int i = 0; i < node.ClassNames.Count; i++)
        {
            if (string.Equals(node.ClassNames[i], className, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.Tick(DeltaTime);
        }
    }

    private static PanelInstanceHandle FindPanel(PanelHost host, Entity scope, string templateId)
    {
        foreach (PanelHostInstanceInfo info in host.SnapshotInstances())
        {
            if (info.TemplateId == templateId && info.Scope == scope)
            {
                return info.Handle;
            }
        }

        throw new InvalidOperationException($"No panel '{templateId}' for scope {scope}.");
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
