using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class PanelCollectionBagsShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "collection_bags_arena";

    [Test]
    public void PanelCollectionBags_ShowsTypedBagsAcrossSixPanels()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "panel_collection_bags");
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
        Assert.That(panelHost.Count, Is.EqualTo(6));

        Entity hero = FindEntity(engine.World, "名册守望者");

        PanelListProjection effects = ProjectAll(
            panelHost, FindPanel(panelHost, hero, "panel.collection.effects"), "templates");
        Assert.That(effects.Items, Has.Count.GreaterThanOrEqualTo(3));
        AssertNames(effects, "祝福", "迅捷", "护盾");

        PanelListProjection roster = ProjectAll(
            panelHost, FindPanel(panelHost, hero, "panel.collection.roster"), "units");
        Assert.That(roster.Items, Has.Count.EqualTo(2));
        AssertNames(roster, "名册守望者", "名册学徒");
        Assert.That(roster.Items[0].NestedLists, Is.Not.Empty);
        Assert.That(roster.Items[0].NestedLists[0].Items, Is.Not.Empty);
        AssertNames(roster.Items[0].NestedLists[0], "火球术");

        PanelInstanceHandle tagsPanel = FindPanel(panelHost, hero, "panel.collection.tags");
        AssertNames(ProjectAll(panelHost, tagsPanel, "tags"), "勇气印记", "洞察印记", "守望印记");
        AssertNames(ProjectAll(panelHost, tagsPanel, "activities"), "名册集会");

        PanelInstanceHandle supply = FindPanel(panelHost, hero, "panel.collection.supply");
        PanelListProjection items = ProjectAll(panelHost, supply, "items");
        Assert.That(items.TotalCount, Is.EqualTo(3));
        AssertNames(items, "试炼药剂");
        PanelListProjection definitions = ProjectAll(panelHost, supply, "definitions");
        AssertNames(definitions, "试炼药剂", "干粮");

        PanelInstanceHandle quest = FindPanel(panelHost, hero, "panel.collection.questboard");
        AssertNames(ProjectAll(panelHost, quest, "tasks"), "巡夜差事");
        AssertNames(ProjectAll(panelHost, quest, "progress"), "名册修行");

        PanelInstanceHandle holders = FindPanel(panelHost, hero, "panel.collection.holders");
        PanelListProjection slots = ProjectAll(panelHost, holders, "slots");
        Assert.That(slots.Items, Is.Not.Empty);
        Assert.That(slots.Items[0].NestedLists, Is.Not.Empty);
        AssertNames(slots.Items[0].NestedLists[0], "名册守望者", "名册学徒");

        UIRoot root = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        Assert.That(root.Scene, Is.Not.Null);
        root.Scene!.Layout(root.Width, root.Height);

        IReadOnlyList<string> texts = AcceptanceUiEvidenceWriter.ExtractUiText(root);
        Assert.That(texts, Does.Contain("效果图鉴"));
        Assert.That(texts, Does.Contain("编队档案"));
        Assert.That(texts, Does.Contain("身上的印记"));
        Assert.That(texts, Does.Contain("物资柜"));
        Assert.That(texts, Does.Contain("差事板"));
        Assert.That(texts, Does.Contain("谁会火球"));
        Assert.That(texts, Does.Contain("祝福"));
        Assert.That(texts, Does.Contain("火球术"));
        Assert.That(texts, Does.Contain("试炼药剂"));
        Assert.That(texts, Does.Contain("巡夜差事"));
        Assert.That(texts, Does.Contain("名册修行"));
        Assert.That(texts, Does.Contain("名册集会"));
        Assert.That(texts, Has.Some.Matches("×\\s*3"));

        AcceptanceUiEvidenceWriter.CaptureFrame(
            root,
            screensDir,
            order: 1,
            step: "typed-collection-bags",
            when: "地图加载后名册墙已投影",
            who: "玩家",
            what: "六块面板同时展示模板、嵌套技能、印记、聚合背包、差事活动、反查与进度",
            where: "screen 六锚点",
            why: "验收查询图集合输出合同 §3.8 项 2–7 玩家可见竖切",
            how: "typed Collect* + IntId/Entity bags + nested/source=input/aggregate");
    }

    private static PanelListProjection ProjectAll(
        PanelHost host,
        PanelInstanceHandle panel,
        string collectionName)
    {
        Assert.That(
            host.TryProjectListWindow(panel, collectionName, PanelListViewWindow.All, out PanelListProjection projection),
            Is.True);
        return projection;
    }

    private static void AssertNames(PanelListProjection projection, params string[] expected)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < projection.Items.Count; i++)
        {
            names.Add(projection.Items[i].Strings["displayName"]);
        }

        Assert.That(names, Is.SupersetOf(expected));
    }

    private static GameEngine CreateEngine(string repoRoot)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "PanelCollectionBagsShowcaseMod" }),
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
