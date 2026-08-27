using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
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
        Assert.That(effects.Items[0].Floats["remaining"], Is.EqualTo(80f).Within(0.001f));
        Assert.That(effects.Items[0].Floats["total"], Is.EqualTo(100f).Within(0.001f));
        Assert.That(effects.Items[0].Floats["stacks"], Is.EqualTo(3f).Within(0.001f));

        Assert.That(effects.Items[2].Strings["displayName"], Is.EqualTo("护盾"));
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
