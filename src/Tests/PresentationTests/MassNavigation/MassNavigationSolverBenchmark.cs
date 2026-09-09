using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using NUnit.Framework;
using Schedulers;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// Scratch 10K solver benchmark used while tuning the MassNavigation hot paths.
    /// Not an assertion gate: it prints phase timings so a change can be judged by numbers.
    /// </summary>
    [TestFixture]
    [Explicit("Manual 10K solver benchmark; prints phase timings instead of asserting.")]
    public sealed class MassNavigationSolverBenchmark
    {
        [TestCase(10_000)]
        [TestCase(20_000)]
        public void Step_10kDenseGrid_PrintsPhaseTimings(int agentCount)
        {
            MassNavigationConfig config = LoadConfig();
            var flow = new MassNavigationFlowSolverState(CreateSolverConfig(parallelWorkerCount: 8));
            flow.ArrivalTuning.CopyFrom(config.Arrival);
            flow.AvoidanceTuning.CopyFrom(config.Avoidance);
            flow.Semantics.CopyFrom(config.Semantics);

            MassNavigationAgentLayer layer = CreateAgentLayer();
            MassNavigationAgentProfileSetConfig profileSet = CreateProfileSet();
            var spawnLayout = new MassNavigationScenarioSpawnLayoutConfig
            {
                Kind = "OrbitOpposedTargets",
                OrbitRadiusCm = 3_650f,
                RandomSeed = 12_648_430,
            };
            spawnLayout.Validate();

            const int teamCount = 4;
            int unitsPerTeam = agentCount / teamCount;
            int actualAgents = unitsPerTeam * teamCount;
            flow.Reset(new[] { 1, 2, 3, 4 }, unitsPerTeam, profileSet, layer, spawnLayout);
            flow.ResetAuthoredAgents(BuildOrbitSeeds(config, actualAgents, teamCount, layer));
            TeamManager.LoadConfig(new TeamConfig
            {
                DefaultRelationship = "Friendly",
                Relationships = new List<RelationshipEntry>(),
            });

            using var world = World.Create();
            if (World.SharedJobScheduler == null)
            {
                World.SharedJobScheduler = new JobScheduler(new JobScheduler.Config
                {
                    ThreadPrefixName = "MassNavBench",
                    ThreadCount = 0,
                    MaxExpectedConcurrentJobs = 64,
                    StrictAllocationMode = false,
                });
            }

            var groups = new MassNavigationGroupRuntime(
                config.Semantics.Group,
                new MassNavigationRuntimeCapacityConfig
                {
                    NavigationGroupCapacity = 8,
                    GroupMembershipAgentCapacity = actualAgents,
                    GroupMemberCapacity = actualAgents,
                    MovePlanExecutionGroupCapacity = 8,
                    MovePlanExecutionMemberCapacity = actualAgents,
                    RouteStateCapacity = 8,
                    RouteMaxExpandedPerRequest = 128,
                    RouteWaypointCapacityPerAgent = 64,
                    LoadedChunkCapacity = 16,
                    RelationshipDomainCapacity = 4,
                    DisplacedAgentCapacity = 4,
                });

            for (int i = 0; i < actualAgents; i++)
            {
                flow.SetUnitTarget(i, flow.GetPositionX(i), flow.GetPositionY(i), resetRecovery: true);
            }

            RunAndPrint(flow, world, groups, actualAgents, "dense-orbit");
        }

        [TestCase(10_000)]
        public void Step_10kOpposedMarch_PrintsPhaseTimings(int agentCount)
        {
            MassNavigationConfig config = LoadConfig();
            var flow = new MassNavigationFlowSolverState(CreateSolverConfig(parallelWorkerCount: 8));
            flow.ArrivalTuning.CopyFrom(config.Arrival);
            flow.AvoidanceTuning.CopyFrom(config.Avoidance);
            flow.Semantics.CopyFrom(config.Semantics);

            MassNavigationAgentLayer layer = CreateAgentLayer();
            const int teamCount = 4;
            int unitsPerTeam = agentCount / teamCount;
            int actualAgents = unitsPerTeam * teamCount;
            flow.ResetAuthoredAgents(BuildOrbitSeeds(config, actualAgents, teamCount, layer));
            TeamManager.LoadConfig(new TeamConfig
            {
                DefaultRelationship = "Friendly",
                Relationships = new List<RelationshipEntry>(),
            });

            using var world = World.Create();
            if (World.SharedJobScheduler == null)
            {
                World.SharedJobScheduler = new JobScheduler(new JobScheduler.Config
                {
                    ThreadPrefixName = "MassNavBench",
                    ThreadCount = 0,
                    MaxExpectedConcurrentJobs = 64,
                    StrictAllocationMode = false,
                });
            }

            var groups = new MassNavigationGroupRuntime(
                config.Semantics.Group,
                new MassNavigationRuntimeCapacityConfig
                {
                    NavigationGroupCapacity = 8,
                    GroupMembershipAgentCapacity = actualAgents,
                    GroupMemberCapacity = actualAgents,
                    MovePlanExecutionGroupCapacity = 8,
                    MovePlanExecutionMemberCapacity = actualAgents,
                    RouteStateCapacity = 8,
                    RouteMaxExpandedPerRequest = 128,
                    RouteWaypointCapacityPerAgent = 64,
                    LoadedChunkCapacity = 16,
                    RelationshipDomainCapacity = 4,
                    DisplacedAgentCapacity = 4,
                });

            for (int i = 0; i < actualAgents; i++)
            {
                float targetX = i < actualAgents / 2 ? 9_200f : 800f;
                float targetY = 5_000f;
                flow.SetUnitTarget(i, targetX, targetY, resetRecovery: true);
            }

            RunAndPrint(flow, world, groups, actualAgents, "opposed-march");
        }

        private static void RunAndPrint(
            MassNavigationFlowSolverState flow,
            World world,
            MassNavigationGroupRuntime groups,
            int agentCount,
            string label)
        {
            double prep = 0, steer = 0, hard = 0;
            Action<double> onPrep = v => prep = v;
            Action<double> onSteer = v => steer = v;
            Action<double> onHard = v => hard = v;

            for (int warm = 0; warm < 10; warm++)
            {
                flow.Step(1f / 15f, world, groups, runHardResolve: true, hardResolveCandidateThresholdAgents: 1, onPrep, onSteer, onHard);
            }

            const int samples = 120;
            double best = double.MaxValue;
            double bestSteer = 0, bestHard = 0;
            double[] totals = new double[samples];
            for (int s = 0; s < samples; s++)
            {
                long stepStart = Stopwatch.GetTimestamp();
                flow.Step(1f / 15f, world, groups, runHardResolve: true, hardResolveCandidateThresholdAgents: 1, onPrep, onSteer, onHard);
                double ms = Stopwatch.GetElapsedTime(stepStart).TotalMilliseconds;
                totals[s] = ms;
                if (ms < best)
                {
                    best = ms;
                    bestSteer = steer;
                    bestHard = hard;
                }
            }

            Array.Sort(totals);
            double p50 = totals[samples / 2];
            TestContext.WriteLine(
                $"layout={label} agents={agentCount} best={best:F3}ms p50={p50:F3}ms prep={prep:F3} steer={bestSteer:F3} hard={bestHard:F3} " +
                $"candidates={flow.LastHardResolveCandidateAgentCount} pairs={flow.LastHardResolvePairCheckCount} " +
                $"penetrating={flow.LastHardResolvePenetratingPairCount}");
        }

        private static MassNavigationAgentSeed[] BuildOrbitSeeds(
            MassNavigationConfig config,
            int agentCount,
            int teamCount,
            MassNavigationAgentLayer layer)
        {
            var seeds = new MassNavigationAgentSeed[agentCount];
            int unitsPerTeam = agentCount / teamCount;
            int columns = (int)MathF.Ceiling(MathF.Sqrt(unitsPerTeam));
            const float spacing = 55f;
            for (int i = 0; i < agentCount; i++)
            {
                int teamIndex = i / unitsPerTeam;
                int local = i % unitsPerTeam;
                float orbitAngle = teamIndex * (MathF.PI * 2f / teamCount);
                float slotsPerRow = MathF.Min(columns, unitsPerTeam - ((local / columns) * columns));
                float col = local % columns;
                float row = local / columns;
                float localX = (col - (slotsPerRow - 1) * 0.5f) * spacing;
                float localY = -row * spacing;
                float cos = MathF.Cos(orbitAngle);
                float sin = MathF.Sin(orbitAngle);
                float x = 5_000f + (localX * cos) - (localY * sin);
                float y = 5_000f + (localX * sin) + (localY * cos);
                seeds[i] = new MassNavigationAgentSeed(
                    teamId: teamIndex + 1,
                    localPositionXCm: x,
                    localPositionYCm: y,
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: 20f,
                    speedCmPerSecond: 800f,
                    layer);
            }

            return seeds;
        }

        private static MassNavigationFlowSolverConfig CreateSolverConfig(int parallelWorkerCount)
        {
            return new MassNavigationFlowSolverConfig
            {
                FieldWidthCm = 10_000,
                FieldHeightCm = 10_000,
                FlowCellSizeCm = 100,
                MaxObstacleCount = 64,
                ParallelWorkerCount = parallelWorkerCount,
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

        private static MassNavigationAgentProfileSetConfig CreateProfileSet()
        {
            var profileSet = new MassNavigationAgentProfileSetConfig
            {
                DefaultProfileId = "light",
                Profiles = new[]
                {
                    new MassNavigationAgentProfileConfig
                    {
                        Id = "light",
                        Heavy = false,
                        VisualScale = 1f,
                        SpeedCmPerSecond = 800f,
                        EveryNth = 0,
                        NthOffset = 0,
                    },
                },
            };
            profileSet.Validate();
            profileSet.BindAgentProfiles(new Ludots.Core.Navigation.AgentProfiles.AgentProfileRegistry(new[]
            {
                new Ludots.Core.Navigation.AgentProfiles.AgentProfileConfig
                {
                    Id = "light",
                    RadiusCm = 20,
                    HeightCm = 180,
                    ClearanceCm = 40,
                    Mass = 1,
                    Layer = 0,
                },
            }));
            return profileSet;
        }

        private static MassNavigationAgentLayer CreateAgentLayer()
        {
            int layerIndex = Ludots.Core.Layers.LayerRegistry.Register(MassNavigationLayerNames.Agent);
            uint mask = 1u << layerIndex;
            return new MassNavigationAgentLayer(mask, mask);
        }


        private static MassNavigationConfig LoadConfig()
        {
            string root = MassNavigationModRoot();
            string text = File.ReadAllText(Path.Combine(root, "assets", "MassNavigationConfig.json"));
            JsonObject json = JsonNode.Parse(text)!.AsObject();
            return MassNavigationConfig.Load(json);
        }

        private static string MassNavigationModRoot()
        {
            string current = AppContext.BaseDirectory;
            for (int i = 0; i < 12 && current != null; i++)
            {
                string candidate = Path.Combine(current, "mods", "capabilities", "navigation", "MassNavigationMod");
                if (File.Exists(Path.Combine(candidate, "assets", "MassNavigationConfig.json")))
                {
                    return candidate;
                }

                current = Path.GetDirectoryName(current);
            }

            throw new InvalidOperationException("MassNavigationMod root not found above the test binary.");
        }
    }
}
