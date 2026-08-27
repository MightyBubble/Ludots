using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Fields;
using Ludots.Core.Gameplay.FieldRegions;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[NonParallelizable]
[TestFixture]
[Category("acceptance")]
[Category("ci-gate")]
public sealed class EastAsiaBordersLandSeaAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "east_asia_visual_heightmap";
    private const string LayerKey = "ownership.east_asia.country";

    private static readonly string[] BorderMods =
    {
        "LudotsCoreMod",
        "EastAsiaPlayableTerrainMod",
        "EastAsiaVisualHeightmapRuntimeEntryMod",
        "FieldEastAsiaCountryMod",
        "EastAsiaNavMeshDebugMod",
        "CoreInputMod",
        "CameraProfilesMod",
        "MassNavigationMod",
        "EastAsiaBordersLandSeaDemoMod",
    };

    [Test]
    public void BordersLandSea_PathingHasFootLandMeshAndShipWaterMesh()
    {
        using GameEngine engine = CreateEngine(BorderMods);
        engine.Start();

        var pathing = new PathingConfigLoader(engine.ConfigPipeline)
            .Load(engine.ConfigCatalog, engine.ConfigConflictReport);
        PathingAgentTypeConfig? foot = null;
        PathingAgentTypeConfig? ship = null;
        for (int i = 0; i < pathing.AgentTypes.Count; i++)
        {
            PathingAgentTypeConfig agent = pathing.AgentTypes[i];
            if (string.Equals(agent.ProfileId, "Small", StringComparison.Ordinal))
            {
                foot = agent;
            }

            if (string.Equals(agent.ProfileId, "Medium", StringComparison.Ordinal))
            {
                ship = agent;
            }
        }

        Assert.That(foot, Is.Not.Null);
        Assert.That(foot!.Selection.Mode, Is.EqualTo(PathSelectionMode.PreferMesh));
        Assert.That(ship, Is.Not.Null);
        Assert.That(ship!.Selection.Mode, Is.EqualTo(PathSelectionMode.PreferMesh));

        var agentProfiles = engine.GetService(CoreServiceKeys.AgentProfiles)
            ?? throw new InvalidOperationException("AgentProfiles missing");
        Assert.That(agentProfiles.Require("Small", "land-sea foot").Layer, Is.EqualTo(0));
        Assert.That(agentProfiles.Require("Medium", "land-sea ship").Layer, Is.EqualTo(1));
    }

    [Test]
    public void BordersLandSea_FootStaysOnLandMeshAndShipStaysOnWaterMesh()
    {
        using GameEngine engine = CreateEngine(BorderMods);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 2);

        IPathService pathService = engine.GetService(CoreServiceKeys.PathService)
            ?? throw new InvalidOperationException("PathService missing");
        Assert.That(pathService, Is.TypeOf<AutoPathService>());

        Assert.That(
            TrySolve(pathService, "StrategyFoot", 200_000, 100_000, 250_000, 150_000, out PathResult foot),
            Is.True);
        Assert.That(foot.Status, Is.EqualTo(PathStatus.Found), "army must walk the land mesh");
        Assert.That(foot.ResolvedDomain, Is.EqualTo(PathDomain.NavMesh));

        Assert.That(
            TrySolve(pathService, "StrategyShip", 855_397, 58_235, 1_055_397, 58_235, out PathResult shipSea),
            Is.True);
        Assert.That(shipSea.Status, Is.EqualTo(PathStatus.Found), "ship must walk the water mesh");
        Assert.That(shipSea.ResolvedDomain, Is.EqualTo(PathDomain.NavMesh));

        Assert.That(
            TrySolve(pathService, "StrategyShip", 855_397, 58_235, 200_000, 100_000, out PathResult shipInland),
            Is.True);
        Assert.That(
            shipInland.Status,
            Is.AnyOf(PathStatus.NoPath, PathStatus.NotReady),
            "ship must not cut across land");
    }

    [Test]
    public void BordersLandSea_CrossingCountriesUpdatesBorderPanelVars()
    {
        using GameEngine engine = CreateEngine(BorderMods);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 4);

        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("map session missing");
        Assert.That(session.Fields!.TryGetByKey(LayerKey, out _), Is.True);
        Assert.That(session.MapConfig!.Tags, Does.Contain("Raylib.FieldOverlays:Off"));
        Assert.That(FindNamed(engine, "EastAsia.CountryDecal"), Is.Not.EqualTo(Entity.Null));

        Entity army = FindNamed(engine, "EastAsia.Army");
        Entity ship = FindNamed(engine, "EastAsia.Ship");
        // Detach MassNav binding so continental teleports are not clamped back into the
        // local solver window (FieldRegion membership is what this case accepts).
        DetachMassNavigation(engine, army);
        DetachMassNavigation(engine, ship);

        var variables = session.Variables
            ?? throw new InvalidOperationException("map variables missing");

        engine.World.Set(army, new WorldPositionCm { Value = Fix64Vec2.FromInt(1_038_777, 295_848) });
        TickUntil(
            engine,
            () => variables.ReadInt("last_enter_code") == 3,
            maxFrames: 12,
            () => $"expected South Korea code 3, got {variables.ReadInt("last_enter_code")} stage={variables.ReadInt("stage")}");
        Assert.That(variables.ReadInt("enter_count"), Is.GreaterThanOrEqualTo(1));
        Assert.That(
            FieldRegionQueries.TryIsInFieldRegion(engine.World, session, army, LayerKey, "country.south_korea", out bool inKorea)
            && inKorea,
            Is.True);

        engine.World.Set(army, new WorldPositionCm { Value = Fix64Vec2.FromInt(1_842_944, 332_291) });
        TickUntil(
            engine,
            () => variables.ReadInt("last_enter_code") == 2,
            maxFrames: 12,
            () => $"expected Japan code 2, got {variables.ReadInt("last_enter_code")} region={variables.ReadInt("region_code")} enter={variables.ReadInt("enter_count")}");
        Assert.That(variables.ReadInt("region_code"), Is.EqualTo(2));
        Assert.That(variables.ReadInt("enter_count"), Is.GreaterThanOrEqualTo(2));
        Assert.That(
            FieldRegionQueries.TryIsInFieldRegion(engine.World, session, army, LayerKey, "country.japan", out bool inJapan)
            && inJapan,
            Is.True);
    }

    private static bool TrySolve(
        IPathService pathService,
        string agentTypeId,
        int startXcm,
        int startYcm,
        int goalXcm,
        int goalYcm,
        out PathResult result)
    {
        var request = new PathRequest(
            requestId: 1,
            actor: default,
            PathDomain.Auto,
            agentTypeId,
            PathEndpoint.FromWorldCm(startXcm, startYcm),
            PathEndpoint.FromWorldCm(goalXcm, goalYcm),
            new PathBudget(maxExpanded: 0, maxPoints: 128));
        return pathService.TrySolve(in request, out result);
    }

    private static void DetachMassNavigation(GameEngine engine, Entity entity)
    {
        if (engine.World.Has<MassNavigationAgentIndex>(entity))
        {
            engine.World.Remove<MassNavigationAgentIndex>(entity);
        }

        if (engine.World.Has<MassNavigationAgent>(entity))
        {
            engine.World.Remove<MassNavigationAgent>(entity);
        }
    }

    private static Entity FindNamed(GameEngine engine, string name)
    {
        Entity found = Entity.Null;
        var query = new QueryDescription().WithAll<Name, WorldPositionCm>();
        engine.World.Query(in query, (Entity entity, ref Name named) =>
        {
            if (named.Value == name)
            {
                found = entity;
            }
        });
        Assert.That(found, Is.Not.EqualTo(Entity.Null), $"missing entity '{name}'");
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

    private static void TickUntil(GameEngine engine, Func<bool> condition, int maxFrames, Func<string> failure)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (condition())
            {
                return;
            }

            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
        }

        Assert.Fail(failure());
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
