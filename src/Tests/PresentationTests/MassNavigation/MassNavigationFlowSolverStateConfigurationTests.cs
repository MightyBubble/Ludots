using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Layers;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MassNavigation;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using NUnit.Framework;
using Schedulers;
using Ludots.Core.Gameplay.Teams;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassNavigationFlowSolverStateConfigurationTests
    {
        [Test]
        public void MassNavigationConfig_RequiresExplicitParallelWorkerCount()
        {
            JsonObject config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject solver = config["solver"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.solver must be authored.");
            solver.Remove("parallelWorkerCount");

            InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(config))!;
            Assert.That(missing.Message, Does.Contain("parallelWorkerCount"));

            config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            solver = config["solver"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.solver must be authored.");
            solver["parallelWorkerCount"] = 0;

            InvalidOperationException invalid = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(config))!;
            Assert.That(invalid.Message, Does.Contain("ParallelWorkerCount"));
        }

        [Test]
        public void AutoSpawnLayout_RequiresExplicitRandomSeed()
        {
            JsonObject config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject scenario = config["scenario"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.scenario must be authored.");
            JsonObject spawnLayout = scenario["spawnLayout"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.scenario.spawnLayout must be authored.");
            spawnLayout.Remove("randomSeed");

            InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(config))!;
            Assert.That(missing.Message, Does.Contain("randomSeed"));
        }

        [Test]
        public void MassNavigationConfig_RejectsLegacyWorldObstacles()
        {
            JsonObject config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject world = config["world"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.world must be authored.");
            world["obstacles"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "legacy_obstacle",
                    ["localXCm"] = 1000f,
                    ["localYCm"] = 1000f,
                    ["radiusCm"] = 100f,
                },
            };

            JsonException ex = Assert.Throws<JsonException>(() => MassNavigationConfig.Load(config))!;
            Assert.That(ex.Message, Does.Contain("obstacles"));
        }

        [Test]
        public void MassNavigationConfig_RequiresExplicitStrictCaseAvoidanceMode()
        {
            JsonObject missingModeConfig = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject missingAvoidance = missingModeConfig["avoidance"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.avoidance must be authored.");
            missingAvoidance.Remove("mode");

            InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(missingModeConfig))!;
            Assert.That(missing.Message, Does.Contain("mode"));

            JsonObject wrongCaseConfig = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject wrongCaseAvoidance = wrongCaseConfig["avoidance"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.avoidance must be authored.");
            wrongCaseAvoidance["mode"] = "orca";

            InvalidOperationException wrongCase = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(wrongCaseConfig))!;
            Assert.That(wrongCase.Message, Does.Contain("avoidance.mode"));
            Assert.That(wrongCase.Message, Does.Contain("orca"));
        }

        [Test]
        public void MassNavigationConfig_SeparationModeAvoidanceToleratesMissingOrcaAndSonarSections()
        {
            JsonObject config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject avoidance = config["avoidance"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.avoidance must be authored.");
            Assert.That(avoidance["mode"]?.GetValue<string>(), Is.EqualTo("Separation"));
            Assert.That(avoidance.ContainsKey("orca"), Is.False);
            Assert.That(avoidance.ContainsKey("sonar"), Is.False);

            MassNavigationConfig loaded = MassNavigationConfig.Load(config);
            Assert.That(loaded.Avoidance.ParsedMode, Is.EqualTo(MassNavigationFlowAvoidanceMode.Separation));
        }

        [Test]
        public void MassNavigationConfig_OrcaModeRequiresOrcaSectionButNotSonar()
        {
            JsonObject config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject avoidance = config["avoidance"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.avoidance must be authored.");
            avoidance["mode"] = "Orca";
            avoidance["orca"] = new JsonObject
            {
                ["timeHorizonSeconds"] = 0.85,
                ["maxNeighbors"] = 16,
            };

            MassNavigationConfig loaded = MassNavigationConfig.Load(config);
            Assert.That(loaded.Avoidance.ParsedMode, Is.EqualTo(MassNavigationFlowAvoidanceMode.Orca));

            JsonObject missingOrca = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject missingOrcaAvoidance = missingOrca["avoidance"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.avoidance must be authored.");
            missingOrcaAvoidance["mode"] = "Orca";
            missingOrcaAvoidance["orca"] = new JsonObject
            {
                ["maxNeighbors"] = 16,
            };

            InvalidOperationException missingHorizon = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(missingOrca))!;
            Assert.That(missingHorizon.Message, Does.Contain("timeHorizonSeconds"));
        }

        [Test]
        public void MassNavigationConfig_SonarModeRequiresSonarSection()
        {
            JsonObject config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject avoidance = config["avoidance"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.avoidance must be authored.");
            avoidance["mode"] = "Sonar";
            avoidance.Remove("sonar");
            avoidance["sonar"] = new JsonObject
            {
                ["maxSteerAngleDeg"] = 280,
                ["backwardPenaltyAngleDeg"] = 230,
                ["predictionTimeScale"] = 0.9,
                ["ignoreBehindMovingAgents"] = true,
                ["blockedStop"] = false,
                ["usePreferredVelocityWhenBlocked"] = true,
                ["timeHorizonSeconds"] = 0.85,
                ["maxNeighbors"] = 16,
            };

            MassNavigationConfig loaded = MassNavigationConfig.Load(config);
            Assert.That(loaded.Avoidance.ParsedMode, Is.EqualTo(MassNavigationFlowAvoidanceMode.Sonar));

            avoidance["sonar"] = new JsonObject
            {
                ["maxSteerAngleDeg"] = 280,
                ["backwardPenaltyAngleDeg"] = 230,
                ["predictionTimeScale"] = 0.9,
                ["ignoreBehindMovingAgents"] = true,
                ["blockedStop"] = false,
                ["usePreferredVelocityWhenBlocked"] = true,
                ["timeHorizonSeconds"] = 0.85,
            };

            InvalidOperationException missingNeighbors = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(config))!;
            Assert.That(missingNeighbors.Message, Does.Contain("maxNeighbors"));
        }

        [Test]
        public void MassNavigationConfig_HotZonesAreOptionalDebugSection()
        {
            JsonObject config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject world = config["world"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.world must be authored.");
            world.Remove("hotZones");
            world.Remove("activeHotZoneId");

            MassNavigationConfig loaded = MassNavigationConfig.Load(config);
            Assert.That(loaded.World!.HotZones, Is.Empty);
            Assert.That(loaded.World.ActiveHotZoneId, Is.Empty);

            JsonObject danglingActiveZone = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            (danglingActiveZone["world"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.world must be authored.")).Remove("hotZones");

            InvalidOperationException dangling = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(danglingActiveZone))!;
            Assert.That(dangling.Message, Does.Contain("ActiveHotZoneId"));

            JsonObject zonesWithoutActive = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            (zonesWithoutActive["world"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.world must be authored.")).Remove("activeHotZoneId");

            InvalidOperationException missingActive = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(zonesWithoutActive))!;
            Assert.That(missingActive.Message, Does.Contain("activeHotZoneId"));
        }

        [Test]
        public void MassNavigationConfig_RuntimeCapacityAppliesEngineDefaultsAndKeepsOverrides()
        {
            JsonObject config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            MassNavigationConfig loaded = MassNavigationConfig.Load(config);
            Assert.That(loaded.RuntimeCapacity.GroupMembershipAgentCapacity, Is.EqualTo(160_000),
                "An explicit override larger than the engine default must survive defaulting.");
            Assert.That(loaded.RuntimeCapacity.LoadedChunkCapacity, Is.EqualTo(256),
                "An explicit loadedChunkCapacity override must survive defaulting.");
            Assert.That(loaded.RuntimeCapacity.GroupMemberCapacity, Is.EqualTo(MassNavigationEngineDefaults.GroupMembershipAgentCapacity),
                "Omitted member capacities default to the engine agent-capacity default.");
            Assert.That(loaded.RuntimeCapacity.RelationshipDomainCapacity, Is.EqualTo(MassNavigationEngineDefaults.RelationshipDomainCapacity));

            JsonObject minimal = new JsonObject
            {
                ["mapId"] = "minimal_map",
            };
            MassNavigationConfig defaults = MassNavigationConfig.Load(minimal);
            Assert.That(defaults.RuntimeCapacity.GroupMembershipAgentCapacity, Is.EqualTo(MassNavigationEngineDefaults.GroupMembershipAgentCapacity));
            Assert.That(defaults.World!.StreamingChunkSizeCm, Is.Zero,
                "streamingChunkSizeCm stays 0 (board-derived) until BindBoardWorld.");
            Assert.That(defaults.RuntimeCapacity.LoadedChunkCapacity, Is.Zero,
                "loadedChunkCapacity stays 0 (board-derived) until BindBoardWorld.");
            defaults.RuntimeCapacity.ApplyBoardDerivedChunkCapacity(6400, 16000);
            Assert.That(defaults.RuntimeCapacity.LoadedChunkCapacity, Is.EqualTo(9),
                "A 16000cm radius window over 6400cm chunks needs a 3x3 chunk span.");
        }

        [Test]
        public void MassNavigationConfig_RejectsDeadScenarioAndProfileSections()
        {
            foreach (string deadKey in new[] { "scenario", "scenarioRuntime", "agentProfiles", "presentation", "teamRelationships" })
            {
                JsonObject config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
                config[deadKey] = new JsonObject();
                JsonException rejected = Assert.Throws<JsonException>(() => MassNavigationConfig.Load(config))!;
                Assert.That(rejected.Message, Does.Contain(deadKey),
                    $"Dead config section '{deadKey}' must be rejected on write.");
            }
        }

        public void SimulationRuntime_CapturesReadOnlyAvoidanceSnapshot()
        {
            using var world = World.Create();
            MassNavigationConfig config = LoadBaseMassNavigationConfig();
            config.Solver.FieldWidthCm = 10_000;
            config.Solver.FieldHeightCm = 10_000;
            config.Solver.PlayAreaMinXCm = 50f;
            config.Solver.PlayAreaMaxXCm = 9_950f;
            config.Solver.PlayAreaMinYCm = 50f;
            config.Solver.PlayAreaMaxYCm = 9_950f;
            config.Solver.MaxObstacleCount = 8;
            config.RuntimeCapacity.GroupMembershipAgentCapacity = 4;
            config.RuntimeCapacity.GroupMemberCapacity = 4;
            config.RuntimeCapacity.MovePlanExecutionMemberCapacity = 4;
            var runtime = new MassNavigationSimulationRuntime(config);
            runtime.BindBoardWorld(
                new WorldSizeSpec(new WorldAabbCm(-5_000, -5_000, 10_000, 10_000), 100),
                MassNavigationOrderChainTests.CreateLoadedChunksForTests(runtime));

            MassNavigationAgentLayer layer = CreateAgentLayer();
            Entity light = CreateAuthoredAgentEntity(world, localX: 1000f, localY: 1200f, layer);
            Entity heavy = CreateAuthoredAgentEntity(world, localX: 1400f, localY: 1200f, layer);
            MassNavigationAgentSeed[] seeds =
            {
                CreateAvoidanceSeed(teamId: 1, localX: 1000f, localY: 1200f, heavy: false, layer),
                CreateAvoidanceSeed(teamId: 2, localX: 1400f, localY: 1200f, heavy: true, layer),
            };
            runtime.RebuildFromAuthoredAgents(world, new[] { light, heavy }, seeds, new[] { true, true });
            runtime.RebuildRuntimeObstacles(new[]
            {
                new MassNavigationObstacleSnapshot(worldXCm: 2200f, worldYCm: 2300f, radiusCm: 150f),
            });

            var agents = new MassNavigationAvoidanceAgentSnapshot[runtime.NavigationAgentCount];
            var obstacles = new MassNavigationObstacleSnapshot[runtime.NavigationObstacleCount];
            MassNavigationAvoidanceSnapshot snapshot = runtime.CaptureAvoidanceSnapshot(agents, obstacles);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.UnitCount, Is.EqualTo(2));
                Assert.That(snapshot.ObstacleCount, Is.EqualTo(1));
                Assert.That(snapshot.PlayAreaMinXCm, Is.EqualTo(50f));
                Assert.That(snapshot.PlayAreaMaxXCm, Is.EqualTo(9_950f));
                Assert.That(agents, Has.Length.EqualTo(2));
                Assert.That(obstacles, Has.Length.EqualTo(1));
            });

            MassNavigationAvoidanceAgentSnapshot lightAgent = agents.Single(agent => agent.AgentIndex == 0);
            MassNavigationAvoidanceAgentSnapshot heavyAgent = agents.Single(agent => agent.AgentIndex == 1);
            Assert.Multiple(() =>
            {
                Assert.That(lightAgent.LocalXCm, Is.EqualTo(1000f).Within(0.001f));
                Assert.That(lightAgent.LocalYCm, Is.EqualTo(1200f).Within(0.001f));
                Assert.That(lightAgent.WorldXCm, Is.EqualTo(-4000f).Within(0.001f));
                Assert.That(lightAgent.WorldYCm, Is.EqualTo(-3800f).Within(0.001f));
                Assert.That(lightAgent.InsidePlayArea, Is.True);
                Assert.That(heavyAgent.TeamId, Is.EqualTo(2));
                Assert.That(heavyAgent.HeavyProfile, Is.True);
                Assert.That(heavyAgent.BodyRadiusCm, Is.EqualTo(20f));
                Assert.That(obstacles[0].WorldXCm, Is.EqualTo(2200f).Within(0.001f));
                Assert.That(obstacles[0].RadiusCm, Is.EqualTo(150f));
            });

            Assert.That(
                () => runtime.CaptureAvoidanceSnapshot(agents.AsSpan(0, 1), obstacles),
                Throws.InvalidOperationException.With.Message.Contains("agent slots"));
        }

        private static MassNavigationFlowSolverState CreateSpawnedFlow(int randomSeed)
        {
            var flow = CreateConfiguredFlow(parallelWorkerCount: 1);
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            float spread = 400f + (randomSeed % 97);
            var seeds = new MassNavigationAgentSeed[4];
            for (int i = 0; i < seeds.Length; i++)
            {
                seeds[i] = new MassNavigationAgentSeed(
                    teamId: 1,
                    localPositionXCm: 2000f + (i * spread * 0.25f),
                    localPositionYCm: 2000f + (randomSeed % 13) + (i * 37f),
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: 20f,
                    speedCmPerSecond: 800f,
                    layer);
            }

            flow.ResetAuthoredAgents(seeds);
            return flow;
        }

        private static MassNavigationAgentSeed[] CreateSeededUnits(int teamCount, int unitsPerTeam, MassNavigationAgentLayer layer)
        {
            var seeds = new MassNavigationAgentSeed[teamCount * unitsPerTeam];
            int index = 0;
            for (int teamIndex = 0; teamIndex < teamCount; teamIndex++)
            {
                for (int localIndex = 0; localIndex < unitsPerTeam; localIndex++, index++)
                {
                    seeds[index] = new MassNavigationAgentSeed(
                        teamId: teamIndex + 1,
                        localPositionXCm: 3000f + (teamIndex * 800f) + (localIndex * 120f),
                        localPositionYCm: 3000f + (localIndex * 90f),
                        heavy: false,
                        navMass: 1f,
                        visualScale: 1f,
                        bodyRadiusCm: 20f,
                        speedCmPerSecond: 800f,
                        layer);
                }
            }

            return seeds;
        }

        /// <summary>QuadrantSpread 布局的 authored 种子等价物：2x2 象限、每队 cols x rows 网格满铺。</summary>
        private static MassNavigationAgentSeed[] CreateQuadrantSpreadSeeds(int teamCount, int unitsPerTeam, MassNavigationAgentLayer layer)
        {
            const float fieldWidthCm = 10_000f;
            const float fieldHeightCm = 10_000f;
            const float spawnSpacingCm = 46f;
            int colsTeams = System.Math.Max(1, (int)System.MathF.Ceiling(System.MathF.Sqrt(teamCount)));
            int rowsTeams = System.Math.Max(1, (int)System.MathF.Ceiling(teamCount / (float)colsTeams));
            float cellWidthCm = fieldWidthCm / colsTeams;
            float cellHeightCm = fieldHeightCm / rowsTeams;
            int cols = System.Math.Max(1, (int)System.MathF.Ceiling(System.MathF.Sqrt(unitsPerTeam)));
            int rows = System.Math.Max(1, (int)System.MathF.Ceiling(unitsPerTeam / (float)cols));
            float spacing = System.MathF.Max(spawnSpacingCm, System.MathF.Min(cellWidthCm / cols, cellHeightCm / rows));

            var seeds = new MassNavigationAgentSeed[teamCount * unitsPerTeam];
            int index = 0;
            for (int teamIndex = 0; teamIndex < teamCount; teamIndex++)
            {
                int quadrantX = teamIndex % colsTeams;
                int quadrantY = teamIndex / colsTeams;
                float centerX = (fieldWidthCm * 0.5f) + ((quadrantX - ((colsTeams - 1) * 0.5f)) * cellWidthCm);
                float centerY = (fieldHeightCm * 0.5f) + ((quadrantY - ((rowsTeams - 1) * 0.5f)) * cellHeightCm);
                for (int localIndex = 0; localIndex < unitsPerTeam; localIndex++, index++)
                {
                    int row = localIndex / cols;
                    int col = localIndex % cols;
                    float lateral = (col - ((cols - 1) * 0.5f)) * spacing;
                    float depth = (row - ((rows - 1) * 0.5f)) * spacing;
                    seeds[index] = new MassNavigationAgentSeed(
                        teamId: teamIndex + 1,
                        localPositionXCm: centerX + lateral,
                        localPositionYCm: centerY + depth,
                        heavy: false,
                        navMass: 1f,
                        visualScale: 1f,
                        bodyRadiusCm: 20f,
                        speedCmPerSecond: 800f,
                        layer);
                }
            }

            return seeds;
        }

        private static MassNavigationFlowSolverState CreateConfiguredFlow(int parallelWorkerCount)
        {
            MassNavigationConfig config = LoadBaseMassNavigationConfig();
            var flow = new MassNavigationFlowSolverState(CreateSolverConfig(parallelWorkerCount));
            flow.ArrivalTuning.CopyFrom(config.Arrival);
            flow.AvoidanceTuning.CopyFrom(config.Avoidance);
            flow.Semantics.CopyFrom(config.Semantics);
            return flow;
        }

        private static MassNavigationFlowSolverState CreateSparseConfiguredFlow()
        {
            MassNavigationConfig config = LoadBaseMassNavigationConfig();
            var flow = new MassNavigationFlowSolverState(new MassNavigationFlowSolverConfig
            {
                FieldWidthCm = 20_000,
                FieldHeightCm = 20_000,
                FlowCellSizeCm = 100,
                MaxObstacleCount = 1,
                ParallelWorkerCount = 1,
                SeparationHashCellSizeCm = 100,
                SeparationHashMinSearchRadiusCells = 2,
                HardResolveHashCellSizeCm = 50,
                HardResolveHashMinSearchRadiusCells = 1,
                PlayAreaMinXCm = 50f,
                PlayAreaMaxXCm = 19_950f,
                PlayAreaMinYCm = 50f,
                PlayAreaMaxYCm = 19_950f,
            });
            flow.ArrivalTuning.CopyFrom(config.Arrival);
            flow.AvoidanceTuning.CopyFrom(config.Avoidance);
            flow.Semantics.CopyFrom(config.Semantics);
            return flow;
        }

        private static MassNavigationAgentSeed CreateSeed(
            float localX,
            float localY,
            MassNavigationAgentLayer layer)
        {
            return new MassNavigationAgentSeed(
                teamId: 1,
                localPositionXCm: localX,
                localPositionYCm: localY,
                heavy: false,
                navMass: 1f,
                visualScale: 1f,
                bodyRadiusCm: 20f,
                speedCmPerSecond: 800f,
                layer);
        }

        private static float StepUnitTargetAndMeasureXDelta(float dt)
        {
            using var world = World.Create();
            var flow = CreateUnitTargetFlow(unitCount: 1);
            Assert.That(flow.SetUnitTarget(0, 9_000f, 5_000f, resetRecovery: true), Is.True);
            float before = flow.GetPositionX(0);
            flow.Step(
                dt,
                world,
                CreateNavGroupRuntime(agentCapacity: flow.UnitCount),
                runHardResolve: false,
                hardResolveCandidateThresholdAgents: flow.UnitCount + 1);
            return flow.GetPositionX(0) - before;
        }

        private static MassNavigationFlowSolverState CreateUnitTargetFlow(int unitCount)
        {
            var flow = CreateConfiguredFlow(parallelWorkerCount: 1);
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            var seeds = new MassNavigationAgentSeed[unitCount];
            for (int i = 0; i < seeds.Length; i++)
            {
                seeds[i] = new MassNavigationAgentSeed(
                    teamId: 1,
                    localPositionXCm: 1_000f,
                    localPositionYCm: 5_000f + (i * 500f),
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: 20f,
                    speedCmPerSecond: 800f,
                    layer);
            }

            flow.ResetAuthoredAgents(seeds);
            TeamManager.LoadConfig(new TeamConfig
            {
                DefaultRelationship = "Friendly",
                Relationships = new List<RelationshipEntry>(),
            });
            return flow;
        }

        private static MassNavigationGroupRuntime CreateNavGroupRuntime(int agentCapacity)
        {
            MassNavigationConfig config = LoadBaseMassNavigationConfig();
            return new MassNavigationGroupRuntime(
                config.Semantics.Group,
                CreateRuntimeCapacity(agentCapacity: agentCapacity, groupMemberCapacity: agentCapacity));
        }

        private static MassNavigationConfig LoadBaseMassNavigationConfig()
        {
            return MassNavigationConfig.Load(
                ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json")));
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

        private static MassNavigationRuntimeCapacityConfig CreateRuntimeCapacity(
            int agentCapacity = 16,
            int groupMemberCapacity = 16)
        {
            return new MassNavigationRuntimeCapacityConfig
            {
                NavigationGroupCapacity = 8,
                GroupMembershipAgentCapacity = agentCapacity,
                GroupMemberCapacity = groupMemberCapacity,
                MovePlanExecutionGroupCapacity = 8,
                MovePlanExecutionMemberCapacity = groupMemberCapacity,
                RouteStateCapacity = 8,
                RouteMaxExpandedPerRequest = 128,
                RouteWaypointCapacityPerAgent = 64,
                LoadedChunkCapacity = 16,
                RelationshipDomainCapacity = 4,
                DisplacedAgentCapacity = 4,
            };
        }

        private static MassNavigationAgentLayer CreateAgentLayer()
        {
            int layerIndex = LayerRegistry.Register(MassNavigationLayerNames.Agent);
            uint mask = 1u << layerIndex;
            return new MassNavigationAgentLayer(mask, mask);
        }

        private static MassNavigationAgentSeed CreateAvoidanceSeed(
            int teamId,
            float localX,
            float localY,
            bool heavy,
            MassNavigationAgentLayer layer)
        {
            return new MassNavigationAgentSeed(
                teamId,
                localX,
                localY,
                heavy,
                navMass: heavy ? 4f : 1f,
                visualScale: heavy ? 1.5f : 1f,
                bodyRadiusCm: 20f,
                speedCmPerSecond: 800f,
                layer);
        }

        private static Entity CreateAuthoredAgentEntity(World world, float localX, float localY, MassNavigationAgentLayer layer)
        {
            int profileId = MassNavigationProfileRegistry.Register("light");
            return world.Create(
                new MassNavigationAgent { ProfileId = profileId },
                new Team { Id = 1 },
                WorldPositionCm.FromCmFloat(localX, localY),
                new EntityLayer(layer.CategoryMask, layer.InteractionMask),
                new FacingDirection { AngleRad = 0f },
                OrderBuffer.CreateEmpty());
        }

        [Test]
        public void CadenceAgentSlicing_SpreadsSolverRoundsAcrossFixedTicks()
        {
            var config = new MassNavigationCadenceConfig
            {
                SimulationHz = 15,
                TargetUpdateHz = 15,
                FlowStepHz = 5,
                FlowCrowdStampHz = 5,
                FlowObstacleStampHz = 2,
                HardResolveHz = 10,
                EntitySyncHz = 15,
                MaxStepsPerFixedTick = 1,
                HardResolveCandidateThresholdAgents = 1,
                AgentSliceCount = 3,
            };
            var scheduler = new MassNavigationCadenceScheduler(config);
            const float fixedDt = 1f / 45f;
            const int fixedTicks = 90;
            int sliceSteps = 0;
            var sliceVisits = new int[3];
            int roundStarts = 0;
            int hardResolveRounds = 0;
            for (int tick = 0; tick < fixedTicks; tick++)
            {
                int stepsToRun = scheduler.BeginFixedTick(fixedDt);
                Assert.That(stepsToRun, Is.LessThanOrEqualTo(1), "Sliced cadence must keep one slice step per fixed tick.");
                for (int step = 0; step < stepsToRun; step++)
                {
                    MassNavigationCadenceStep cadenceStep = scheduler.NextSimulationStep();
                    Assert.That(cadenceStep.SimulationDt, Is.EqualTo(1f / 15f).Within(0.00001f),
                        "Each agent must keep stepping with the full simulationHz delta.");
                    Assert.That(cadenceStep.AgentSliceCount, Is.EqualTo(3));
                    sliceVisits[cadenceStep.AgentSliceIndex]++;
                    if (cadenceStep.AgentSliceRoundStart)
                    {
                        roundStarts++;
                        if (cadenceStep.RunHardResolve)
                        {
                            hardResolveRounds++;
                        }
                    }

                    sliceSteps++;
                }
            }

            Assert.That(sliceSteps, Is.EqualTo(90), "45 slice steps per second must amortize across every fixed tick.");
            Assert.That(roundStarts, Is.EqualTo(30), "Rounds must complete at simulationHz (15 per second).");
            foreach (int visits in sliceVisits)
            {
                Assert.That(visits, Is.EqualTo(30), "Every agent slice must be visited exactly once per round.");
            }

            Assert.That(hardResolveRounds, Is.EqualTo(20), "Hard resolve must keep its 10Hz round cadence.");
        }

        [Test]
        public void CadenceAgentSlicing_DefaultSingleSliceKeepsLegacyStepRhythm()
        {
            var config = new MassNavigationCadenceConfig
            {
                SimulationHz = 15,
                TargetUpdateHz = 15,
                FlowStepHz = 5,
                FlowCrowdStampHz = 5,
                FlowObstacleStampHz = 2,
                HardResolveHz = 10,
                EntitySyncHz = 15,
                MaxStepsPerFixedTick = 1,
                HardResolveCandidateThresholdAgents = 1,
                AgentSliceCount = 1,
            };
            var scheduler = new MassNavigationCadenceScheduler(config);
            int steps = 0;
            int hardResolveSteps = 0;
            for (int tick = 0; tick < 90; tick++)
            {
                int stepsToRun = scheduler.BeginFixedTick(1f / 45f);
                for (int step = 0; step < stepsToRun; step++)
                {
                    MassNavigationCadenceStep cadenceStep = scheduler.NextSimulationStep();
                    Assert.That(cadenceStep.AgentSliceIndex, Is.EqualTo(0));
                    Assert.That(cadenceStep.AgentSliceCount, Is.EqualTo(1));
                    Assert.That(cadenceStep.AgentSliceRoundStart, Is.True);
                    if (cadenceStep.RunHardResolve)
                    {
                        hardResolveSteps++;
                    }

                    steps++;
                }
            }

            Assert.That(steps, Is.EqualTo(30), "Default cadence keeps the legacy 15Hz simulation rhythm at fixed 45Hz.");
            Assert.That(hardResolveSteps, Is.EqualTo(20));
        }

        [Test]
        public void CadenceAgentSlicing_RejectsUnsustainableSliceRate()
        {
            var config = new MassNavigationCadenceConfig
            {
                SimulationHz = 15,
                TargetUpdateHz = 15,
                FlowStepHz = 5,
                FlowCrowdStampHz = 5,
                FlowObstacleStampHz = 2,
                HardResolveHz = 10,
                EntitySyncHz = 15,
                MaxStepsPerFixedTick = 1,
                HardResolveCandidateThresholdAgents = 1,
                AgentSliceCount = 4,
            };
            var scheduler = new MassNavigationCadenceScheduler(config);
            InvalidOperationException rejected = Assert.Throws<InvalidOperationException>(
                () => scheduler.BeginFixedTick(1f / 45f))!;
            Assert.That(rejected.Message, Does.Contain("agentSliceCount"));
        }

        private static JsonObject ReadObject(string path)
        {
            return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                ?? throw new InvalidOperationException($"Expected JSON object at '{path}'.");
        }

        private static void MutateWritableLeaves(object target, ref int seed)
        {
            foreach (PropertyInfo property in target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!CanMapProperty(property))
                {
                    continue;
                }

                Type propertyType = property.PropertyType;
                if (propertyType == typeof(bool))
                {
                    property.SetValue(target, seed++ % 2 == 0);
                }
                else if (propertyType == typeof(int))
                {
                    property.SetValue(target, 10 + seed++);
                }
                else if (propertyType == typeof(float))
                {
                    property.SetValue(target, 0.25f + (seed++ * 3.5f));
                }
                else if (propertyType == typeof(string))
                {
                    property.SetValue(target, property.Name == "Mode" ? "Sonar" : $"mapped-{seed++}");
                }
                else
                {
                    object nested = property.GetValue(target)
                        ?? throw new InvalidOperationException($"Expected explicit nested config section {property.Name}.");
                    MutateWritableLeaves(nested, ref seed);
                }
            }
        }

        private static void AssertWritableLeavesEqual(object expected, object actual, string path)
        {
            foreach (PropertyInfo property in expected.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!CanMapProperty(property))
                {
                    continue;
                }

                object? expectedValue = property.GetValue(expected);
                object? actualValue = property.GetValue(actual);
                Type propertyType = property.PropertyType;
                string propertyPath = $"{path}.{property.Name}";
                if (propertyType == typeof(bool) ||
                    propertyType == typeof(int) ||
                    propertyType == typeof(float) ||
                    propertyType == typeof(string))
                {
                    Assert.That(actualValue, Is.EqualTo(expectedValue), propertyPath);
                    continue;
                }

                Assert.That(actualValue, Is.Not.Null, propertyPath);
                AssertWritableLeavesEqual(expectedValue!, actualValue!, propertyPath);
            }
        }

        private static bool CanMapProperty(PropertyInfo property)
        {
            return property.CanRead &&
                property.CanWrite &&
                property.GetIndexParameters().Length == 0;
        }

        private static int ResolveAuthoredAgentCount(JsonObject config)
        {
            JsonObject scenario = config["scenario"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.scenario must be authored.");
            JsonArray teams = scenario["teams"]?.AsArray()
                ?? throw new InvalidOperationException("MassNavigationConfig.scenario.teams must be authored.");
            int agentsPerTeam = scenario["agentsPerTeam"]?.GetValue<int>()
                ?? throw new InvalidOperationException("MassNavigationConfig.scenario.agentsPerTeam must be authored.");
            return checked(teams.Count * agentsPerTeam);
        }

        private static string MassNavigationModRoot()
        {
            return Path.Combine(FindRepoRoot(), "mods", "capabilities", "navigation", "MassNavigationMod");
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
}
