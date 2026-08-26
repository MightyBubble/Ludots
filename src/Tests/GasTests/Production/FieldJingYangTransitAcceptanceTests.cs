using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Fields;
using Ludots.Core.Gameplay.FieldRegions;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelHosting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// 荆扬过境验收：归属层物化、FieldRegion 进出通知、区内名单投影。
/// 地图明文 key 为 jing/yang；引擎侧无业务词。
/// </summary>
[NonParallelizable]
[TestFixture]
[Category("acceptance")]
[Category("ci-gate")]
public sealed class FieldJingYangTransitAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "field_jing_yang_transit";
    private const string LayerKey = "ownership.transit";
    private const string CollectionKey = "collection.field.ownership.transit.members";
    private const string ProgressPanelId = "panel.field_jing_yang.progress";

    private static readonly string[] Mods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "FieldJingYangTransitMod",
    };

    [Test]
    public void JingYangTransit_Materialize_EnterExit_Roster_Panel()
    {
        using GameEngine engine = CreateEngine(Mods);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 2);

        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("map session missing");
        MapVariableStore variables = session.Variables
            ?? throw new InvalidOperationException("map variables missing");
        Assert.That(session.Fields, Is.Not.Null);
        Assert.That(session.RegionIndex, Is.Not.Null);
        Assert.That(session.Fields!.TryGetByKey(LayerKey, out FieldLayerData layerData), Is.True);
        Assert.That(layerData, Is.InstanceOf<DiscreteIdFieldLayerData>());
        var layer = (DiscreteIdFieldLayerData)layerData;

        Assert.That(session.RegionIndex!.Count, Is.EqualTo(2), "jing and yang must materialize");
        Assert.That(layer.Regions.GetName(1), Is.EqualTo("jing"));
        Assert.That(layer.Regions.GetName(2), Is.EqualTo("yang"));

        Assert.That(variables.ReadInt("stage"), Is.EqualTo(1), "MapLoaded writes stage=1");

        // First membership tick: hero starts in yang → FieldRegionEntered(yang).
        TickUntil(engine, () => variables.ReadInt("stage") == 2, maxFrames: 12,
            () => $"Expected FieldRegionEntered(yang) to write stage=2 (got stage={variables.ReadInt("stage")}, region={variables.ReadInt("region_code")}).");
        Assert.That(variables.ReadInt("region_code"), Is.EqualTo(2));
        Assert.That(variables.ReadInt("last_enter_code"), Is.EqualTo(2));
        Assert.That(variables.ReadInt("enter_count"), Is.GreaterThanOrEqualTo(1));
        // Only dirty regions project into EntityCollectionStore; jing has never been entered yet.
        AssertRosterCount(engine, session, layer.LayerId, regionId: 2, expected: 1);

        Entity hero = FindHero(engine.World);
        engine.World.Set(hero, new WorldPositionCm { Value = Fix64Vec2.FromInt(150, 150) }); // jing cell (1,1)

        TickUntil(engine, () => variables.ReadInt("stage") == 3, maxFrames: 12,
            () => $"Expected FieldRegionEntered(jing) to write stage=3 (got stage={variables.ReadInt("stage")}, last_enter={variables.ReadInt("last_enter_code")}, last_exit={variables.ReadInt("last_exit_code")}).");
        Assert.That(variables.ReadInt("region_code"), Is.EqualTo(1));
        Assert.That(variables.ReadInt("last_enter_code"), Is.EqualTo(1));
        Assert.That(variables.ReadInt("last_exit_code"), Is.EqualTo(2));
        Assert.That(variables.ReadInt("exit_count"), Is.GreaterThanOrEqualTo(1));
        AssertRosterCount(engine, session, layer.LayerId, regionId: 1, expected: 1);
        AssertRosterCount(engine, session, layer.LayerId, regionId: 2, expected: 0);

        // Return to yang.
        engine.World.Set(hero, new WorldPositionCm { Value = Fix64Vec2.FromInt(750, 150) });
        TickUntil(engine, () => variables.ReadInt("region_code") == 2 && variables.ReadInt("last_exit_code") == 1, maxFrames: 12,
            () => "Returning to yang must exit jing and enter yang.");
        AssertRosterCount(engine, session, layer.LayerId, regionId: 2, expected: 1);
        AssertRosterCount(engine, session, layer.LayerId, regionId: 1, expected: 0);

        var panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("PanelHost missing.");
        int progressPanels = 0;
        foreach (PanelHostInstanceInfo info in panelHost.SnapshotInstances())
        {
            if (string.Equals(info.TemplateId, ProgressPanelId, StringComparison.Ordinal))
            {
                progressPanels++;
            }
        }

        Assert.That(progressPanels, Is.EqualTo(1), "MapLoaded must create the progress panel.");
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            string.Join("; ", engine.TriggerManager.Errors));
    }

    [Test]
    public void JingYangTransitMod_IsDataOnly()
    {
        string modRoot = Path.Combine(FindRepoRoot(), "mods", "showcases", "field_jing_yang_transit", "FieldJingYangTransitMod");
        Assert.That(File.Exists(Path.Combine(modRoot, "mod.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(modRoot, "FieldJingYangTransitMod.csproj")), Is.False,
            "Transit showcase must stay code-free: Fields + map + graphs + panels only.");
    }

    private static void AssertRosterCount(
        GameEngine engine,
        MapSession session,
        FieldLayerId layerId,
        int regionId,
        int expected)
    {
        Assert.That(session.RegionIndex!.TryResolve(layerId, regionId, out Entity region), Is.True);
        EntityCollectionStore collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("EntityCollectionStore missing.");
        Assert.That(collections.TryGetView(region, CollectionKey, out EntityCollectionView view), Is.True,
            $"collection missing for region {regionId}");
        Assert.That(view.Count, Is.EqualTo(expected),
            $"region {regionId} roster expected {expected}, got {view.Count}");
    }

    private static Entity FindHero(World world)
    {
        Entity found = Entity.Null;
        world.Query(
            new QueryDescription().WithAll<Name, FieldTrackedCm, WorldPositionCm>(),
            (Entity entity, ref Name name) =>
            {
                if (string.Equals(name.Value, "TransitHero", StringComparison.Ordinal))
                {
                    found = entity;
                }
            });
        Assert.That(found, Is.Not.EqualTo(Entity.Null), "TransitHero missing");
        return found;
    }

    private static GameEngine CreateEngine(string[] mods)
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, mods),
            Path.Combine(repoRoot, "assets"));
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        return engine;
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
        }
    }

    private static void TickUntil(GameEngine engine, Func<bool> condition, int maxFrames, Func<string> describeFailure)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            Tick(engine, 1);
            if (condition())
            {
                return;
            }
        }

        Assert.Fail(describeFailure());
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "mods")) &&
                Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root.");
    }
}
