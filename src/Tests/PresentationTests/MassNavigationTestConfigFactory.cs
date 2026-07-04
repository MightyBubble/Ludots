using System;
using System.IO;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Navigation.AgentProfiles;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

internal static class MassNavigationTestConfigFactory
{
    internal const int LocalTeamId = 1;
    internal const int EnemyTeamId = 2;

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
                StreamingRadiusCm = 1000,
                CommandFocusHoldTicks = 3,
                WorkAreaPaddingCm = 100,
                WorkAreaMaxWidthCm = solver.FieldWidthCm,
                WorkAreaMaxHeightCm = solver.FieldHeightCm,
                ActiveHotZoneId = "center",
                HotZones = new[]
                {
                    new MassNavigationHotZoneConfig
                    {
                        Id = "center",
                        Label = "Center",
                        CenterXCm = 5000,
                        CenterYCm = 5000,
                        WidthCm = 1000,
                        HeightCm = 1000,
                    },
                },
            },
            Streaming = new MassNavigationStreamingConfig
            {
                RetainSeconds = 6f,
                RadiusCm = 1000,
            },
            Scenario = new MassNavigationScenarioConfig
            {
                AgentsPerTeam = 1,
                InitialSelectedTeamId = LocalTeamId,
                Teams = new[]
                {
                    new MassNavigationScenarioTeamConfig { Id = LocalTeamId, Name = "Team 1" },
                    new MassNavigationScenarioTeamConfig { Id = EnemyTeamId, Name = "Team 2" },
                },
                SpawnLayout = new MassNavigationScenarioSpawnLayoutConfig
                {
                    Kind = "OrbitOpposedTargets",
                    OrbitRadiusCm = 3000f,
                },
            },
            ScenarioRuntime = new MassNavigationScenarioRuntimeConfig
            {
                AutoSpawnConfiguredScenario = true,
                InitialSelectionScratchCapacity = 8,
                InitialSelectedEntityCapacity = 8,
                RuntimeCapacity = new MassNavigationRuntimeCapacityConfig
                {
                    NavigationGroupCapacity = 8,
                    GroupMembershipAgentCapacity = 16,
                    SelectionMemberScratchCapacity = 8,
                    GroupMemberCapacity = 8,
                    OrderIngestionTokenCapacity = 8,
                    OrderIngestionMemberCapacity = 8,
                    LoadedChunkCapacity = 32,
                    MetadataTeamCapacity = 2,
                },
            },
            AgentProfiles = new MassNavigationAgentProfileSetConfig
            {
                DefaultProfileId = "light",
                Profiles = new[]
                {
                    new MassNavigationAgentProfileConfig
                    {
                        Id = "light",
                        Heavy = false,
                        VisualScale = 0.22f,
                        SpeedCmPerSecond = 800f,
                        EveryNth = 0,
                        NthOffset = 0,
                    },
                },
            },
            Cadence = baseConfig.Cadence,
            Presentation = baseConfig.Presentation,
            TeamRelationships = baseConfig.TeamRelationships,
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
        return new AgentProfileRegistry(new[]
        {
            new AgentProfileConfig
            {
                Id = "light",
                RadiusCm = 20,
                HeightCm = 180,
                ClearanceCm = 40,
                Mass = 1,
                Layer = 0
            }
        });
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
            if (Directory.Exists(Path.Combine(current, "mods")) &&
                File.Exists(Path.Combine(current, "AGENTS.md")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current)!;
        }

        throw new DirectoryNotFoundException("Repository root not found from test work directory.");
    }
}
