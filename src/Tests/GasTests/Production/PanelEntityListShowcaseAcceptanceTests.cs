using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
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
public sealed class PanelEntityListShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "entity_list_arena";
    private const string PanelTemplateId = "panel.entity.list";
    private const string ListScrollHostId = "panel-list-panel.entity.list-units";

    [Test]
    public void PanelEntityList_FiltersSortsAndShowsStunBadge()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "panel_entity_list");
        string screensDir = Path.Combine(artifactDir, "screens");
        AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

        using GameEngine engine = CreateEngine(repoRoot);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 8);

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            string.Join(" | ", engine.TriggerManager.Errors));

        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("PanelHost missing.");
        Assert.That(panelHost.Count, Is.EqualTo(1));

        Entity commander = FindEntity(engine.World, "指挥官");
        PanelInstanceHandle panel = FindPanel(panelHost, commander);
        Assert.That(panelHost.TryGetValues(panel, out PanelVariableSet values), Is.True);
        Assert.That(values.Get("rowCount"), Is.EqualTo(4f).Within(0.001f),
            "Graph alive+team filter must make rowCount match the projected list length.");

        Assert.That(panelHost.TryGetListProjections(panel, out IReadOnlyList<PanelListProjection> lists), Is.True);
        Assert.That(lists.Count, Is.EqualTo(1));
        Assert.That(lists[0].TotalCount, Is.EqualTo(4), "Graph already dropped the fallen scout.");

        Assert.That(
            panelHost.TryProjectListWindow(panel, "units", PanelListViewWindow.All, out PanelListProjection units),
            Is.True);
        Assert.That(units.Items.Count, Is.EqualTo(4));

        string[] expectedOrder = { "指挥官", "医师", "晕眩卫士", "弓手" };
        float[] expectedHealth = { 100f, 97f, 80f, 64f };
        for (int i = 0; i < expectedOrder.Length; i++)
        {
            Assert.That(units.Items[i].Strings["displayName"], Is.EqualTo(expectedOrder[i]));
            Assert.That(units.Items[i].Floats["health"], Is.EqualTo(expectedHealth[i]).Within(0.001f));
        }

        Assert.That(units.Items[2].Bools["stunned"], Is.True, "晕眩卫士 must carry Status.Stunned.");
        Assert.That(units.Items[0].Bools["stunned"], Is.False);
        Assert.That(units.Items[1].Bools["stunned"], Is.False);
        Assert.That(units.Items[3].Bools["stunned"], Is.False);

        UIRoot root = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        Assert.That(root.Scene, Is.Not.Null);
        root.Scene!.Layout(root.Width, root.Height);

        Assert.That(FindNodeByClass(root.Scene.Root!, "panel-entity-list"), Is.Not.Null);
        Assert.That(FindNodeByClass(root.Scene.Root!, "roster-list") != null
            || FindNodeByClass(root.Scene.Root!, "control-list-scroll") != null, Is.True);
        Assert.That(root.Scene.TryGetVirtualWindow(ListScrollHostId, out UiVirtualWindow windowBefore), Is.True,
            "Virtualized roster list must register a scroll virtual window.");
        Assert.That(windowBefore.TotalCount, Is.EqualTo(4));
        Assert.That(FindNodeByClass(root.Scene.Root!, "unit-stunned"), Is.Not.Null,
            "Declared stun badge must mount for the stunned row.");

        IReadOnlyList<string> texts = AcceptanceUiEvidenceWriter.ExtractUiText(root);
        Assert.That(texts, Does.Contain("指挥官"));
        Assert.That(texts, Does.Contain("医师"));
        Assert.That(texts, Does.Contain("晕眩卫士"));
        Assert.That(texts, Does.Contain("弓手"));
        Assert.That(texts, Does.Not.Contain("阵亡斥候"));
        Assert.That(texts, Does.Contain("晕眩"));
        Assert.That(texts, Does.Not.Contain("敌方哨兵"));

        AcceptanceUiEvidenceWriter.CaptureFrame(
            root,
            screensDir,
            order: 1,
            step: "roster-sorted",
            when: "地图加载后名册已投影",
            who: "关卡作者 / 玩家",
            what: "左侧名册按血量降序列出存活单位，晕眩卫士带徽标",
            where: "screen.topLeft panel.entity.list",
            why: "验证元素 subject+graph 透传与 list 编排",
            how: "容器图 EntityCollection；panel.unit.roster subject=Entity 自解；list 只引用 template");

        UiNode scrollHost = root.Scene.FindByElementId(ListScrollHostId)
            ?? throw new InvalidOperationException($"Scroll host '{ListScrollHostId}' missing.");
        Assert.That(scrollHost.MaxScrollY, Is.GreaterThan(1f),
            "Short viewport must make the roster scrollable.");

        float scrolled = ScrollVertical(root, ListScrollHostId, scrollHost.MaxScrollY);
        Tick(engine, 2);
        root.Scene.Layout(root.Width, root.Height);

        Assert.That(root.Scene.TryGetVirtualWindow(ListScrollHostId, out UiVirtualWindow windowAfter), Is.True);
        Assert.That(windowAfter.ScrollOffset, Is.EqualTo(scrolled).Within(0.5f));
        Assert.That(windowAfter.TotalCount, Is.EqualTo(4), "Scroll must not drop the collection total.");
        Assert.That(FindNodeByClass(root.Scene.Root!, "unit-stunned"), Is.Not.Null,
            "Stun badge must remain available while the virtual window follows scroll.");

        AcceptanceUiEvidenceWriter.CaptureFrame(
            root,
            screensDir,
            order: 2,
            step: "roster-scrolled",
            when: "玩家向下滚动名册视口",
            who: "玩家",
            what: "短视口可滚，虚拟窗口跟着滚动偏移更新",
            where: "screen.topLeft panel.entity.list list scroll",
            why: "验收 list 滚动与 virtualize 窗口合同",
            how: "viewportHeight+virtualize；ScrollView host 改 ScrollOffset 后重投影");
    }

    private static GameEngine CreateEngine(string repoRoot)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "PanelEntityListShowcaseMod" }),
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

    private static float ScrollVertical(UIRoot root, string elementId, float deltaY)
    {
        UiScene scene = root.Scene ?? throw new InvalidOperationException("UIRoot scene missing.");
        scene.Layout(root.Width, root.Height);
        UiNode node = scene.FindByElementId(elementId)
            ?? throw new InvalidOperationException($"Scroll host '{elementId}' missing.");

        MethodInfo setScrollOffset = typeof(UiNode).GetMethod(
                "SetScrollOffset",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(UiNode).FullName, "SetScrollOffset");
        MethodInfo refreshReactiveRuntime = typeof(UiScene).GetMethod(
                "TryRefreshReactiveRuntimeDependencies",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(UiScene).FullName, "TryRefreshReactiveRuntimeDependencies");

        float nextOffsetY = MathF.Min(node.MaxScrollY, node.ScrollOffsetY + deltaY);
        bool changed = (bool)(setScrollOffset.Invoke(node, new object[] { node.ScrollOffsetX, nextOffsetY }) ?? false);
        Assert.That(changed, Is.True, $"Scroll host '{elementId}' must accept a larger offset.");

        _ = refreshReactiveRuntime.Invoke(scene, Array.Empty<object>());
        scene.Layout(root.Width, root.Height);
        return nextOffsetY;
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
            throw new InvalidOperationException($"Entity named '{name}' not found.");
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
