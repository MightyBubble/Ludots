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
    public void PanelCollectionBags_ShowsTypedEffectAbilityAndTagBags()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "panel_collection_bags");
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
        Assert.That(panelHost.Count, Is.EqualTo(3));

        Entity hero = FindEntity(engine.World, "名册守望者");
        PanelListProjection effects = ProjectAll(
            panelHost,
            FindPanel(panelHost, hero, "panel.collection.effects"),
            "templates");
        Assert.That(effects.Items, Has.Count.GreaterThanOrEqualTo(3));
        AssertNames(effects, "祝福", "迅捷", "护盾");

        PanelListProjection abilities = ProjectAll(
            panelHost,
            FindPanel(panelHost, hero, "panel.collection.abilities"),
            "slots");
        Assert.That(abilities.Items, Is.Not.Empty);
        AssertNames(abilities, "火球术", "闪现", "守护姿态");

        PanelListProjection tags = ProjectAll(
            panelHost,
            FindPanel(panelHost, hero, "panel.collection.tags"),
            "tags");
        Assert.That(tags.Items, Is.Not.Empty);
        AssertNames(tags, "勇气印记", "洞察印记", "守望印记");

        UIRoot root = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        Assert.That(root.Scene, Is.Not.Null);
        root.Scene!.Layout(root.Width, root.Height);

        IReadOnlyList<string> texts = AcceptanceUiEvidenceWriter.ExtractUiText(root);
        Assert.That(texts, Does.Contain("效果图鉴"));
        Assert.That(texts, Does.Contain("技能格"));
        Assert.That(texts, Does.Contain("身上的印记"));
        Assert.That(texts, Does.Contain("祝福"));
        Assert.That(texts, Does.Contain("迅捷"));
        Assert.That(texts, Does.Contain("护盾"));
        Assert.That(texts, Does.Contain("火球术"));
        Assert.That(texts, Does.Contain("勇气印记"));

        AcceptanceUiEvidenceWriter.CaptureFrame(
            root,
            screensDir,
            order: 1,
            step: "typed-collection-bags",
            when: "地图加载后名册墙已投影",
            who: "玩家",
            what: "效果图鉴、技能格、身上的印记同时列出真实成员",
            where: "screen.topLeft / screen.topCenter / screen.topRight",
            why: "验证三类 typed collection bags 的玩家可见竖切",
            how: "QueryCollectEffectTemplates、QueryCollectAbilitySlots、QueryCollectPresentTags");
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
