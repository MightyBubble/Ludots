using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Fields;
using Ludots.Core.Gameplay.FieldRegions;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// Field hierarchy query acceptance: load map with Fields/hierarchies.json,
/// resolve chain for zone.a1 via RegionHierarchyBuilder.TryResolveChain.
/// </summary>
[NonParallelizable]
[TestFixture]
[Category("acceptance")]
[Category("ci-gate")]
public sealed class FieldHierarchyQueryAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "field_hierarchy_query";
    private const string LayerKey = "ownership.zones";

    private static readonly string[] Mods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "FieldHierarchyQueryMod",
    };

    [Test]
    public void HierarchyQuery_ResolveChain_ContainsZoneAndParentGroup()
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
        Assert.That(session.RegionGroups, Is.Not.Null, "hierarchies.json must wire RegionGroups at map load");
        Assert.That(session.RegionGroups!.GroupByKey.ContainsKey("group.mid"), Is.True);
        Assert.That(session.RegionGroups.GroupByKey.ContainsKey("group.top"), Is.True);

        Assert.That(session.Fields!.TryGetByKey(LayerKey, out FieldLayerData layerData), Is.True);
        var layer = (DiscreteIdFieldLayerData)layerData;
        Assert.That(session.RegionIndex!.Count, Is.EqualTo(3));
        Assert.That(layer.Regions.GetName(1), Is.EqualTo("zone.a1"));

        Assert.That(variables.ReadInt("stage"), Is.EqualTo(1), "MapLoaded writes stage=1");

        Assert.That(session.RegionIndex.TryResolve(layer.LayerId, regionId: 1, out Entity zoneA1), Is.True);
        var chain = new List<string>();
        Assert.That(RegionHierarchyBuilder.TryResolveChain(engine.World, zoneA1, chain), Is.True);
        Assert.That(chain, Does.Contain("zone.a1"));
        Assert.That(chain, Does.Contain("group.mid"));
        Assert.That(chain, Does.Contain("group.top"));
        Assert.That(chain[0], Is.EqualTo("zone.a1"), "finest region first");
        Assert.That(chain, Is.EqualTo(new[] { "zone.a1", "group.mid", "group.top" }));

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            string.Join("; ", engine.TriggerManager.Errors));
    }

    [Test]
    public void HierarchyQueryMod_IsDataOnly()
    {
        string modRoot = Path.Combine(FindRepoRoot(), "mods", "showcases", "field_hierarchy_query", "FieldHierarchyQueryMod");
        Assert.That(File.Exists(Path.Combine(modRoot, "mod.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(modRoot, "FieldHierarchyQueryMod.csproj")), Is.False,
            "Hierarchy showcase must stay code-free.");
        Assert.That(File.Exists(Path.Combine(modRoot, "assets", "Fields", "hierarchies.json")), Is.True);
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
