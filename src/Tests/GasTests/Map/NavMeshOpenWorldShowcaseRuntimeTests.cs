using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

/// <summary>
/// NavMeshOpenWorldShowcaseMod 的真实引擎验收：地图能加载、三层 navmesh 通道可查、
/// 四类通行 profile 分离、地图摆放的单位能被绑定进求解器并正常行走。
///
/// 本测试驱动真实 GameEngine，不用替身；showcase 本身零 C#。
/// </summary>
[TestFixture]
[Category("acceptance")]
public sealed class NavMeshOpenWorldShowcaseRuntimeTests
{
    private const string MapId = "navmesh_openworld_strait";

    private static readonly string[] ShowcaseMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "SelectionInteractionMod",
        "MassNavigationMod",
        "NavMeshOpenWorldShowcaseMod"
    };

    [Test]
    public void Showcase_LoadsMapAndPublishesThreeLayerNavigation()
    {
        using GameEngine engine = CreateEngine();
        engine.Start();
        engine.LoadStartupMap();

        Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo(MapId));

        var navRegistry = RequireService<NavQueryServiceRegistry>(engine, CoreServiceKeys.NavQueryServices);
        var agentProfiles = RequireService<AgentProfileRegistry>(engine, CoreServiceKeys.AgentProfiles);
        NavMeshBakeConfigContext nav = NavMeshBakeConfigLoader.LoadContextFromRepoRoot(
            FindRepoRoot(),
            "NavMeshOpenWorldShowcaseMod");

        Assert.That(
            nav.Config.Layers.Select(layer => layer.Layer).OrderBy(v => v),
            Is.EqualTo(new[] { 0, 1, 2 }),
            "Ground, Mountain and Water must all be declared as navigation layers.");

        // 每 layer × 每 profile 都必须能查到 store，否则该通行能力在该层上是死的。
        for (int li = 0; li < nav.Config.Layers.Count; li++)
        {
            for (int pi = 0; pi < nav.Config.Profiles.Count; pi++)
            {
                string profileId = nav.Config.Profiles[pi].Id;
                Assert.That(
                    navRegistry.TryGetStore(nav.Config.Layers[li].Layer, pi, out NavTileStore store),
                    Is.True,
                    $"layer {nav.Config.Layers[li].Id} profile {profileId} must have a registered NavTileStore.");
                Assert.That(store, Is.Not.Null);
            }
        }

        Assert.That(agentProfiles.Count, Is.GreaterThanOrEqualTo(5));
        Assert.That(agentProfiles.Require("land_infantry", "test").Layer, Is.EqualTo(0));
        Assert.That(agentProfiles.Require("mountain_corps", "test").Layer, Is.EqualTo(1));
        Assert.That(agentProfiles.Require("naval_deep", "test").Layer, Is.EqualTo(2));
    }

    [Test]
    public void Showcase_BakedTilesActuallyAnswerPathQueries()
    {
        using GameEngine engine = CreateEngine();
        engine.Start();
        engine.LoadStartupMap();

        var pathService = RequireService<IPathService>(engine, CoreServiceKeys.PathService);

        // 烘焙瓦片是流式加载的；先推孨几个 tick 让目标区域驻留，再查询。
        PathResult result = default;
        bool solved = false;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var request = new PathRequest(
                requestId: 1,
                actor: Arch.Core.Entity.Null,
                domain: PathDomain.NavMesh,
                agentTypeId: "LandInfantry",
                start: PathEndpoint.FromWorldCm(42_400, 62_400),
                goal: PathEndpoint.FromWorldCm(44_400, 64_400),
                budget: new PathBudget(maxExpanded: 0, maxPoints: 512));

            solved = pathService.TrySolve(in request, out result);
            if (solved && result.Status == PathStatus.Found)
            {
                break;
            }

            engine.Tick(1f / 30f);
        }

        Assert.That(solved, Is.True, "the openworld map must answer land path queries.");
        Assert.That(
            result.Status,
            Is.EqualTo(PathStatus.Found),
            $"land path across the west plain must be found; got {result.Status} (error {result.ErrorCode}).");
        Assert.That(result.Handle.IsValid, Is.True);
    }

    [Test]
    public void Showcase_MapPlacedUnitsJoinTheSolverAndMove()
    {
        using GameEngine engine = CreateEngine();
        engine.Start();
        engine.LoadStartupMap();
        WaitForNavigationRuntimeReady(engine);

        MassNavigationSimulationRuntime simulation =
            RequireService<MassNavigationRuntimeBinding>(engine, MassNavigationKeys.RuntimeBinding).RequireCurrent();

        Assert.That(
            simulation.Config.ScenarioRuntime.AutoSpawnConfiguredScenario,
            Is.False,
            "the openworld theatre places its units in the map, so scenario auto-spawn must stay off.");

        // 等待 authored binding 把地图摆放的单位绑进求解器。
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (simulation.MassNavigationFlow.UnitCount == 0 && DateTime.UtcNow < deadline)
        {
            engine.Tick(1f / 30f);
        }

        int unitCount = simulation.MassNavigationFlow.UnitCount;
        Assert.That(unitCount, Is.GreaterThan(0), "map-placed MassNavigationAgent units must be bound into the solver.");

        var before = new System.Numerics.Vector2[unitCount];
        for (int i = 0; i < unitCount; i++)
        {
            before[i] = new System.Numerics.Vector2(
                simulation.MassNavigationFlow.GetPositionX(i),
                simulation.MassNavigationFlow.GetPositionY(i));
        }

        // 走向东岸平原。
        for (int i = 0; i < unitCount; i++)
        {
            simulation.MassNavigationFlow.SetUnitTarget(i, 142_400f, 62_400f, resetRecovery: true);
        }

        for (int tick = 0; tick < 120; tick++)
        {
            engine.Tick(1f / 30f);
        }

        int moved = 0;
        for (int i = 0; i < unitCount; i++)
        {
            var now = new System.Numerics.Vector2(
                simulation.MassNavigationFlow.GetPositionX(i),
                simulation.MassNavigationFlow.GetPositionY(i));
            if (System.Numerics.Vector2.Distance(before[i], now) > 100f)
            {
                moved++;
            }
        }

        Assert.That(moved, Is.GreaterThan(0), "at least one map-placed unit must actually walk toward its target.");
    }

    private static void WaitForNavigationRuntimeReady(GameEngine engine)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var binding = engine.GetService(MassNavigationKeys.RuntimeBinding);
            if (binding != null)
            {
                // 准备完成前 RequireCurrent 会抛错；轮询直到两趟 binding 都跑完。
                try
                {
                    binding.RequireCurrent();
                    return;
                }
                catch (InvalidOperationException)
                {
                }
            }

            engine.Tick(1f / 30f);
        }

        Assert.Fail("MassNavigation runtime never reached a prepared current-map state for the openworld showcase.");
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, ShowcaseMods),
            Path.Combine(repoRoot, "assets"));
        return engine;
    }

    private static TService RequireService<TService>(GameEngine engine, ServiceKey<TService> key)
    {
        return engine.GetService(key)
            ?? throw new InvalidOperationException($"Service '{key}' is required by the openworld showcase.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "showcase.registry.json")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
