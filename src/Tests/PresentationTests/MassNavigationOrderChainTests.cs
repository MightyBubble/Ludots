using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Layers;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class MassNavigationOrderChainTests
{
    internal const int LocalTeamId = 1;
    internal const int EnemyTeamId = 2;

    [Test]
    public void BindBoardWorld_RejectsActiveHotZoneOutsideBoardCenterRange()
    {
        MassNavigationConfig config = CreateConfigForTests();
        config.World!.HotZones[0].CenterXCm = 1_000;
        var simulation = new MassNavigationSimulationRuntime(config);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => simulation.BindBoardWorld(
                new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100),
                new Ludots.Core.Navigation.GraphWorld.WorldGridLoadedChunks(simulation.WorldConfig.StreamingChunkSizeCm)))!;

        Assert.That(ex.Message, Does.Contain("active hot zone"));
        Assert.That(ex.Message, Does.Contain("center x"));
        Assert.That(ex.Message, Does.Contain("center range"));
    }

    [Test]
    public void StreamingCapacityFailure_DoesNotMoveSolverOrCommitLoadedChunkContribution()
    {
        MassNavigationConfig config = CreateConfigForTests();
        var simulation = new MassNavigationSimulationRuntime(config);
        var loadedChunks = new Ludots.Core.Navigation.GraphWorld.WorldGridLoadedChunks(
            config.World!.StreamingChunkSizeCm);
        simulation.BindBoardWorld(
            new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100),
            loadedChunks);
        using var world = World.Create();
        simulation.ResetRuntimeState(
            world,
            new[]
            {
                new MassNavigationAgentSeed(
                    teamId: LocalTeamId,
                    localPositionXCm: 1200f,
                    localPositionYCm: 1300f,
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: 20f,
                    speedCmPerSecond: 800f,
                    new MassNavigationAgentLayer(1u, 1u)),
            });

        float solverX = simulation.SolverWindowCenterXCm;
        float solverY = simulation.SolverWindowCenterYCm;
        float workAreaX = simulation.FlowWorkAreaCenterXCm;
        float workAreaY = simulation.FlowWorkAreaCenterYCm;
        float originX = simulation.MassNavigationFlow.WorldOriginXCm;
        float originY = simulation.MassNavigationFlow.WorldOriginYCm;
        Vector2 localPosition = simulation.GetAgentLocalPositionCm(0);
        Vector2 worldPosition = simulation.GetAgentWorldPositionCm(0);
        long[] loadedBefore = simulation.LoadedChunks.ActiveChunkKeys.OrderBy(key => key).ToArray();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            simulation.FocusSimulationWindow(new Vector2(20_000f, 20_000f)))!;

        Assert.That(ex.Message, Does.Contain("loadedChunkCapacity"));
        Assert.That(simulation.SolverWindowCenterXCm, Is.EqualTo(solverX));
        Assert.That(simulation.SolverWindowCenterYCm, Is.EqualTo(solverY));
        Assert.That(simulation.FlowWorkAreaCenterXCm, Is.EqualTo(workAreaX));
        Assert.That(simulation.FlowWorkAreaCenterYCm, Is.EqualTo(workAreaY));
        Assert.That(simulation.MassNavigationFlow.WorldOriginXCm, Is.EqualTo(originX));
        Assert.That(simulation.MassNavigationFlow.WorldOriginYCm, Is.EqualTo(originY));
        Assert.That(simulation.GetAgentLocalPositionCm(0), Is.EqualTo(localPosition));
        Assert.That(simulation.GetAgentWorldPositionCm(0), Is.EqualTo(worldPosition));
        Assert.That(simulation.LoadedChunks.ActiveChunkKeys.OrderBy(key => key).ToArray(), Is.EqualTo(loadedBefore));
    }

    [Test]
    public void StationaryStreamingWindow_RemainsLoadedBeyondRetentionPeriod()
    {
        MassNavigationConfig config = CreateConfigForTests();
        var simulation = new MassNavigationSimulationRuntime(config);
        var loadedChunks = new Ludots.Core.Navigation.GraphWorld.WorldGridLoadedChunks(
            config.World!.StreamingChunkSizeCm);
        simulation.BindBoardWorld(
            new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100),
            loadedChunks);

        long[] expected = loadedChunks.ActiveChunkKeys.OrderBy(key => key).ToArray();
        Assert.That(expected, Is.Not.Empty);

        simulation.BeginFrame(config.Streaming.RetainSeconds + 1f);
        simulation.UpdateStreamingWindow(new Vector2(5_000f, 5_000f));

        Assert.That(loadedChunks.ActiveChunkKeys.OrderBy(key => key).ToArray(), Is.EqualTo(expected));
    }

    internal static MassNavigationConfig CreateConfigForTests()
    {
        MassNavigationConfig baseConfig = LoadBaseMassNavigationConfig();
        MassNavigationFlowSolverConfig solver = CreateTestSolverConfig();
        var config = new MassNavigationConfig
        {
            MapId = "mass_navigation",
            Solver = solver,
            World = new MassNavigationWorldConfig
            {
                SolverWindowWidthCm = solver.FieldWidthCm,
                SolverWindowHeightCm = solver.FieldHeightCm,
                StreamingChunkSizeCm = 500,
                CommandFocusHoldTicks = 3,
                WorkAreaPaddingCm = 100,
                WorkAreaMaxWidthCm = solver.FieldWidthCm,
                WorkAreaMaxHeightCm = solver.FieldHeightCm,
                ActiveHotZoneId = "center",
                HotZones =
                [
                    new MassNavigationHotZoneConfig
                    {
                        Id = "center",
                        Label = "Center",
                        CenterXCm = 5000,
                        CenterYCm = 5000,
                        WidthCm = 1000,
                        HeightCm = 1000,
                    },
                ],
            },
            Streaming = new MassNavigationStreamingConfig { RetainSeconds = 6f, RadiusCm = 1000 },
            Scenario = new MassNavigationScenarioConfig
            {
                AgentsPerTeam = 1,
                Teams =
                [
                    new MassNavigationScenarioTeamConfig { Id = LocalTeamId, Name = "Team 1" },
                    new MassNavigationScenarioTeamConfig { Id = EnemyTeamId, Name = "Team 2" },
                ],
                SpawnLayout = new MassNavigationScenarioSpawnLayoutConfig
                {
                    Kind = "OrbitOpposedTargets",
                    OrbitRadiusCm = 3000f,
                },
            },
            ScenarioRuntime = new MassNavigationScenarioRuntimeConfig
            {
                AutoSpawnConfiguredScenario = true,
                RuntimeCapacity = new MassNavigationRuntimeCapacityConfig
                {
                    NavigationGroupCapacity = 8,
                    GroupMembershipAgentCapacity = 16,
                    GroupMemberCapacity = 8,
                    MovePlanExecutionGroupCapacity = 8,
                    MovePlanExecutionMemberCapacity = 8,
                    RouteStateCapacity = 16,
                    RouteMaxExpandedPerRequest = 128,
                    RouteWaypointCapacityPerAgent = 64,
                    LoadedChunkCapacity = 32,
                    RelationshipDomainCapacity = 2,
                },
            },
            AgentProfiles = new MassNavigationAgentProfileSetConfig
            {
                DefaultProfileId = "light",
                Profiles =
                [
                    new MassNavigationAgentProfileConfig
                    {
                        Id = "light",
                        Heavy = false,
                        VisualScale = 0.22f,
                        SpeedCmPerSecond = 800f,
                        EveryNth = 0,
                        NthOffset = 0,
                    },
                ],
            },
            Cadence = baseConfig.Cadence,
            Presentation = baseConfig.Presentation,
            TeamRelationships = baseConfig.TeamRelationships,
            RelationshipPolicy = baseConfig.RelationshipPolicy,
            Flow = baseConfig.Flow,
            Arrival = baseConfig.Arrival,
            Avoidance = baseConfig.Avoidance,
            Semantics = baseConfig.Semantics,
        };
        config.Solver.Validate();
        config.World.Validate(config.Solver);
        config.Streaming.Validate();
        config.ScenarioRuntime.Validate();
        config.Scenario.Validate(config.ScenarioRuntime);
        config.AgentProfiles.Validate();
        config.AgentProfiles.BindAgentProfiles(CreateAgentProfilesForTests());
        return config;
    }

    private static MassNavigationConfig LoadBaseMassNavigationConfig()
    {
        string path = Path.Combine(
            FindRepoRoot(),
            "mods",
            "capabilities",
            "navigation",
            "MassNavigationMod",
            "assets",
            "MassNavigationConfig.json");
        using FileStream stream = File.OpenRead(path);
        return MassNavigationConfig.Load(stream);
    }

    private static AgentProfileRegistry CreateAgentProfilesForTests()
    {
        return new AgentProfileRegistry(
        [
            new AgentProfileConfig
            {
                Id = "light",
                RadiusCm = 20,
                HeightCm = 180,
                ClearanceCm = 40,
                Mass = 1,
                Layer = 0,
            },
        ]);
    }

    private static MassNavigationFlowSolverConfig CreateTestSolverConfig()
    {
        return new MassNavigationFlowSolverConfig
        {
            FieldWidthCm = 10_000,
            FieldHeightCm = 10_000,
            FlowCellSizeCm = 100,
            MaxObstacleCount = 64,
            ParallelWorkerCount = 1,
            SeparationHashCellSizeCm = 100,
            SeparationHashMinSearchRadiusCells = 2,
            HardResolveHashCellSizeCm = 50,
            HardResolveHashMinSearchRadiusCells = 1,
            PlayAreaMinXCm = 50f,
            PlayAreaMaxXCm = 9_950f,
            PlayAreaMinYCm = 50f,
            PlayAreaMaxYCm = 9_950f,
        };
    }

    private static string FindRepoRoot()
    {
        string current = TestContext.CurrentContext.WorkDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "mods")) && File.Exists(Path.Combine(current, "AGENTS.md")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current)!;
        }

        throw new DirectoryNotFoundException("Repository root not found from test work directory.");
    }
}
