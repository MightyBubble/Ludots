using System;
using System.Collections.Generic;
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
        public void ParallelStep_RequiresSchedulerWhenConfiguredParallel()
        {
            JobScheduler? previousScheduler = World.SharedJobScheduler;
            World.SharedJobScheduler = null;

            try
            {
                var flow = CreateConfiguredFlow(parallelWorkerCount: 2);
                var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
                flow.Semantics.Solver.ParallelStepMinAgents = 2;
                flow.Reset(
                    new[] { 1 },
                    unitsPerTeam: 2,
                    CreateProfileSet(),
                    layer,
                    CreateSpawnLayout(randomSeed: 1234));
                TeamManager.LoadConfig(new TeamConfig
                {
                    DefaultRelationship = "Friendly",
                    Relationships = new List<RelationshipEntry>(),
                });

                using var world = World.Create();
                var navGroups = new MassNavigationGroupRuntime(
                    LoadBaseMassNavigationConfig().Semantics.Group,
                    CreateRuntimeCapacity(agentCapacity: 2, groupMemberCapacity: 2));
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                    flow.Step(
                        dt: 0.016f,
                        world,
                        navGroups,
                        runHardResolve: false,
                        hardResolveCandidateThresholdAgents: 1))!;

                Assert.That(ex.Message, Does.Contain("World.SharedJobScheduler"));
                Assert.That(ex.Message, Does.Contain("parallelWorkerCount"));
            }
            finally
            {
                World.SharedJobScheduler = previousScheduler;
            }
        }

        [Test]
        public void ParallelStep_EmitsExactlyOneArrivalEventPerSettledAgent()
        {
            World.SharedJobScheduler ??= new JobScheduler(new JobScheduler.Config
            {
                ThreadPrefixName = "MassNavArrivalTests",
                ThreadCount = 0,
                MaxExpectedConcurrentJobs = 64,
                StrictAllocationMode = false
            });

            const int agentCount = 16;
            var flow = CreateConfiguredFlow(parallelWorkerCount: 4);
            flow.Semantics.Solver.ParallelStepMinAgents = 2;
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            var seeds = new MassNavigationAgentSeed[agentCount];
            for (int i = 0; i < agentCount; i++)
            {
                seeds[i] = new MassNavigationAgentSeed(
                    teamId: 1,
                    localPositionXCm: 1_000f + ((i % 4) * 800f),
                    localPositionYCm: 1_000f + ((i / 4) * 800f),
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: 20f,
                    speedCmPerSecond: 800f,
                    layer);
            }

            TeamManager.LoadConfig(new TeamConfig
            {
                DefaultRelationship = "Friendly",
                Relationships = new List<RelationshipEntry>(),
            });
            flow.ResetAuthoredAgents(seeds);
            for (int i = 0; i < agentCount; i++)
            {
                flow.SetUnitTarget(i, flow.GetPositionX(i), flow.GetPositionY(i), resetRecovery: true);
            }

            using var world = World.Create();
            var navGroups = new MassNavigationGroupRuntime(
                LoadBaseMassNavigationConfig().Semantics.Group,
                CreateRuntimeCapacity(agentCapacity: agentCount, groupMemberCapacity: agentCount));

            flow.Step(dt: 0.016f, world, navGroups, runHardResolve: false, hardResolveCandidateThresholdAgents: 1);
            Assert.That(flow.SettledUnitCount, Is.EqualTo(agentCount));
            Assert.That(flow.PendingArrivalEventCount, Is.EqualTo(agentCount));

            flow.Step(dt: 0.016f, world, navGroups, runHardResolve: false, hardResolveCandidateThresholdAgents: 1);
            Assert.That(flow.PendingArrivalEventCount, Is.EqualTo(agentCount));
        }

        [Test]
        public void SpawnJitter_UsesConfiguredSeedDeterministically()
        {
            var first = CreateSpawnedFlow(randomSeed: 1234);
            var second = CreateSpawnedFlow(randomSeed: 1234);
            var different = CreateSpawnedFlow(randomSeed: 5678);

            Assert.That(second.GetPositionX(0), Is.EqualTo(first.GetPositionX(0)));
            Assert.That(second.GetPositionY(0), Is.EqualTo(first.GetPositionY(0)));
            Assert.That(
                MathF.Abs(different.GetPositionX(0) - first.GetPositionX(0)) +
                MathF.Abs(different.GetPositionY(0) - first.GetPositionY(0)),
                Is.GreaterThan(0.001f));
        }

        [Test]
        public void Step_UsesCallerDeltaTimeAsSingleSimulationClock()
        {
            float pausedDistance = StepUnitTargetAndMeasureXDelta(0f);
            float smallStepDistance = StepUnitTargetAndMeasureXDelta(0.0125f);
            float mediumStepDistance = StepUnitTargetAndMeasureXDelta(0.025f);
            float largeStepDistance = StepUnitTargetAndMeasureXDelta(0.05f);

            Assert.That(pausedDistance, Is.EqualTo(0f).Within(0.001f),
                "A zero simulation dt must stop MassNavigation movement.");
            Assert.That(smallStepDistance, Is.GreaterThan(pausedDistance),
                "A positive simulation dt must move MassNavigation agents.");
            Assert.That(smallStepDistance, Is.LessThan(mediumStepDistance),
                "MassNavigation must consume the caller-provided simulation dt as its single time source.");
            Assert.That(largeStepDistance, Is.GreaterThan(mediumStepDistance),
                "A larger simulation dt within the configured solver cap must move farther.");
        }

        [Test]
        public void Step_ZeroDeltaDoesNotAdvanceArrivalTimeout()
        {
            using var world = World.Create();
            var flow = CreateUnitTargetFlow(unitCount: 1);
            flow.ArrivalTuning.Enabled = true;
            flow.ArrivalTuning.TimeoutMs = 250;
            flow.ArrivalTuning.ProgressDistanceCm = 10_000;
            Assert.That(flow.SetUnitTarget(0, 9_000f, 5_000f, resetRecovery: true), Is.True);

            flow.Step(
                dt: 0f,
                world,
                CreateNavGroupRuntime(agentCapacity: flow.UnitCount),
                runHardResolve: false,
                hardResolveCandidateThresholdAgents: flow.UnitCount + 1);

            Assert.That(flow.IsUnitSettled(0), Is.False,
                "Pause must freeze MassNavigation arrival recovery timers instead of timing out a stopped unit.");
        }

        [Test]
        public void SimulationRuntime_PropagatesGroupedAgentSemanticsToMassNavigationFlow()
        {
            MassNavigationConfig config = MassNavigationConfig.Load(
                ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json")));
            MassNavigationGroupSemantics group = config.Semantics.Group;
            group.GroupedAgentArriveThresholdCm = 222f;
            group.GroupedAgentFlowSlowRadiusCm = 444f;

            var runtime = new MassNavigationSimulationRuntime(config);
            MassNavigationGroupSemantics massFlowGroup = runtime.GetRuntimeGroupSemantics();

            Assert.That(massFlowGroup.GroupedAgentArriveThresholdCm, Is.EqualTo(group.GroupedAgentArriveThresholdCm));
            Assert.That(massFlowGroup.GroupedAgentFlowSlowRadiusCm, Is.EqualTo(group.GroupedAgentFlowSlowRadiusCm));
        }

        [Test]
        public void SimulationRuntime_PropagatesAllMappedConfigFieldsToMassNavigationFlow()
        {
            MassNavigationConfig config = MassNavigationConfig.Load(
                ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json")));
            int seed = 1;
            MutateWritableLeaves(config.Arrival, ref seed);
            MutateWritableLeaves(config.Avoidance, ref seed);
            MutateWritableLeaves(config.Semantics, ref seed);

            var runtime = new MassNavigationSimulationRuntime(config);
            MassNavigationFlowSolverState flow = runtime.GetFlowSolverForTests();

            AssertWritableLeavesEqual(config.Arrival, flow.ArrivalTuning, "arrival");
            AssertWritableLeavesEqual(config.Avoidance, flow.AvoidanceTuning, "avoidance");
            AssertWritableLeavesEqual(config.Semantics, flow.Semantics, "semantics");
        }

        [Test]
        public void SimulationRuntime_PreallocatesRelationshipMatrixFromRuntimeCapacity()
        {
            MassNavigationConfig config = LoadBaseMassNavigationConfig();
            config.ScenarioRuntime.RuntimeCapacity.RelationshipDomainCapacity = 5;

            var runtime = new MassNavigationSimulationRuntime(config);
            MassNavigationFlowSolverState flow = runtime.GetFlowSolverForTests();

            Assert.That(
                flow.DomainRelationshipMatrixCapacity,
                Is.EqualTo(25),
                "Relationship-domain cooperative matrix must be prepared from runtime capacity before fixed-step; it must not grow on demand while stepping.");
        }

        [Test]
        public void SimulationRuntime_AuthoredRebuildKeepsPreallocatedRelationshipMatrixCapacity()
        {
            using var world = World.Create();
            MassNavigationConfig config = LoadBaseMassNavigationConfig();
            config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity = 4;
            config.ScenarioRuntime.RuntimeCapacity.RelationshipDomainCapacity = 4;
            var runtime = new MassNavigationSimulationRuntime(config);
            MassNavigationFlowSolverState flow = runtime.GetFlowSolverForTests();
            MassNavigationAgentLayer layer = CreateAgentLayer();
            Entity agent = CreateAuthoredAgentEntity(world, localX: 1000f, localY: 1200f, layer);
            MassNavigationAgentSeed[] seeds =
            {
                CreateAvoidanceSeed(teamId: 1, localX: 1000f, localY: 1200f, heavy: false, layer),
            };

            runtime.RebuildFromAuthoredAgents(world, new[] { agent }, seeds, new[] { true });

            Assert.That(
                flow.DomainRelationshipMatrixCapacity,
                Is.EqualTo(16),
                "Authored map binding must keep the cold-preallocated relationship matrix capacity; shrinking it would make later domain append fail or allocate during fixed-step.");
        }

        [Test]
        public void MassNavigationAuthoringContract_DoesNotRequirePresentationServicesWhenScenarioIsExternallyAuthored()
        {
            using var engine = new GameEngine();
            MassNavigationConfig config = LoadBaseMassNavigationConfig();
            config.ScenarioRuntime.AutoSpawnConfiguredScenario = false;
            engine.RemoveService(CoreServiceKeys.PerformerDefinitionRegistry);
            engine.RemoveService(CoreServiceKeys.PresentationMeshAssetRegistry);
            engine.RemoveService(CoreServiceKeys.VisualHeightmap);

            Assert.That(
                () => MassNavigationAuthoringContract.Require(engine, config),
                Throws.Nothing,
                "Externally-authored MassNavigation maps must be able to prepare execution without Presentation performer, mesh, or VisualHeightmap services.");
        }

        [Test]
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
            config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity = 4;
            config.ScenarioRuntime.RuntimeCapacity.GroupMemberCapacity = 4;
            config.ScenarioRuntime.RuntimeCapacity.MovePlanExecutionMemberCapacity = 4;
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
            flow.Reset(
                new[] { 1 },
                unitsPerTeam: 4,
                CreateProfileSet(),
                layer,
                CreateSpawnLayout(randomSeed));
            return flow;
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
            };
        }

        private static MassNavigationScenarioSpawnLayoutConfig CreateSpawnLayout(int randomSeed)
        {
            var spawnLayout = new MassNavigationScenarioSpawnLayoutConfig
            {
                Kind = "OrbitOpposedTargets",
                OrbitRadiusCm = 3_650f,
                RandomSeed = randomSeed,
            };
            spawnLayout.Validate();
            return spawnLayout;
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
            profileSet.BindAgentProfiles(CreateAgentProfiles());
            return profileSet;
        }

        private static AgentProfileRegistry CreateAgentProfiles()
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
