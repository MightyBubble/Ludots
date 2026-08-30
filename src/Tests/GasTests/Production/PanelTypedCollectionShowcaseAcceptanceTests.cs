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
public sealed class PanelTypedCollectionShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;

    public sealed record Case(
        string EntryMod,
        string MapId,
        string PanelId,
        string Collection,
        string Title,
        string ArtifactSlug,
        string[] Names,
        int? TotalCount = null,
        string[]? NestedNames = null);

    private static readonly Case[] Cases =
    [
        new("PanelEffectTemplatesEntryMod", "collection_bags_effect_templates",
            "panel.collection.effects", "templates", "效果图鉴", "effect_templates",
            ["祝福", "迅捷", "护盾"]),
        new("PanelRosterNestedEntryMod", "collection_bags_roster_nested",
            "panel.collection.roster", "units", "编队档案", "roster_nested",
            ["名册守望者", "名册学徒"]),
        new("PanelPresentTagsEntryMod", "collection_bags_present_tags",
            "panel.collection.tags", "tags", "身上的印记", "present_tags",
            ["勇气印记", "洞察印记", "守望印记"]),
        new("PanelInventoryAggregateEntryMod", "collection_bags_inventory_aggregate",
            "panel.collection.inventory", "items", "背包堆叠", "inventory_aggregate",
            ["试炼药剂"], TotalCount: 3),
        new("PanelItemDefinitionsEntryMod", "collection_bags_item_definitions",
            "panel.collection.itemDefinitions", "definitions", "物品图鉴", "item_definitions",
            ["试炼药剂", "干粮"]),
        new("PanelActiveTasksEntryMod", "collection_bags_active_tasks",
            "panel.collection.tasks", "tasks", "进行中的差事", "active_tasks",
            ["巡夜差事"]),
        new("PanelActiveActivitiesEntryMod", "collection_bags_active_activities",
            "panel.collection.activities", "activities", "进行中的活动", "active_activities",
            ["名册集会"]),
        new("PanelAbilityHoldersEntryMod", "collection_bags_ability_holders",
            "panel.collection.holders", "slots", "谁会火球", "ability_holders",
            [], NestedNames: ["名册守望者", "名册学徒"]),
        new("PanelProgressionNodesEntryMod", "collection_bags_progression_nodes",
            "panel.collection.progression", "progress", "修行进度", "progression_nodes",
            ["名册修行"]),
    ];

    [TestCaseSource(nameof(Cases))]
    public void TypedCollectionBag_ShowsSinglePanelMembers(Case testCase)
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", $"panel_{testCase.ArtifactSlug}");
        string screensDir = Path.Combine(artifactDir, "screens");
        AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

        using GameEngine engine = CreateEngine(repoRoot, testCase.EntryMod);
        engine.Start();
        engine.LoadMap(testCase.MapId);
        Tick(engine, 20);

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            string.Join(" | ", engine.TriggerManager.Errors));

        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("PanelHost missing.");
        Assert.That(panelHost.Count, Is.EqualTo(1), "one panel per showcase");

        Entity hero = FindEntity(engine.World, "名册守望者");
        PanelListProjection projection = ProjectAll(
            panelHost,
            FindPanel(panelHost, hero, testCase.PanelId),
            testCase.Collection);

        if (testCase.TotalCount is int total)
        {
            Assert.That(projection.TotalCount, Is.EqualTo(total));
        }

        if (testCase.Names.Length > 0)
        {
            AssertNames(projection, testCase.Names);
        }

        if (testCase.NestedNames is { Length: > 0 } nested)
        {
            Assert.That(projection.Items, Is.Not.Empty);
            Assert.That(projection.Items[0].NestedLists, Is.Not.Empty);
            AssertNames(projection.Items[0].NestedLists[0], nested);
        }

        UIRoot root = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        Assert.That(root.Scene, Is.Not.Null);
        root.Scene!.Layout(root.Width, root.Height);
        IReadOnlyList<string> texts = AcceptanceUiEvidenceWriter.ExtractUiText(root);
        Assert.That(texts, Does.Contain(testCase.Title));

        AcceptanceUiEvidenceWriter.CaptureFrame(
            root,
            screensDir,
            order: 1,
            step: testCase.ArtifactSlug,
            when: "地图加载后单面板已投影",
            who: "玩家",
            what: $"{testCase.Title}列出真实成员",
            where: "screen.topLeft",
            why: "一袋一面板验收",
            how: "typed collection bag showcase entry");
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

    private static GameEngine CreateEngine(string repoRoot, string entryMod)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(
                repoRoot,
                new[] { "LudotsCoreMod", "PanelCollectionBagsShowcaseMod", entryMod }),
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
