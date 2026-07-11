using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Layers;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MassNavigation;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.Avoidance;
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
            JsonObject runtime = config["runtime"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.runtime must be authored.");
            JsonObject solver = runtime["solver"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.solver must be authored.");
            solver.Remove("parallelWorkerCount");

            JsonException missing = Assert.Throws<JsonException>(() => MassNavigationConfig.Load(runtime))!;
            Assert.That(missing.Message, Does.Contain("parallelWorkerCount"));

            config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            runtime = config["runtime"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.runtime must be authored.");
            solver = runtime["solver"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.solver must be authored.");
            solver["parallelWorkerCount"] = 0;

            InvalidOperationException invalid = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(runtime))!;
            Assert.That(invalid.Message, Does.Contain("ParallelWorkerCount"));
        }

        [Test]
        public void MassNavigationConfig_UsesCadenceHzAsTheOnlyFlowSchedule()
        {
            JsonObject config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject runtime = config["runtime"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.runtime must be authored.");
            JsonObject flow = runtime["flow"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.flow must be authored.");
            flow.Remove("stepIntervalTicks");
            flow.Remove("crowdStampIntervalTicks");
            flow.Remove("obstacleStampIntervalTicks");

            Assert.That(() => MassNavigationConfig.Load(runtime), Throws.Nothing);

            flow["stepIntervalTicks"] = 1;
            JsonException legacy = Assert.Throws<JsonException>(() => MassNavigationConfig.Load(runtime))!;
            Assert.That(legacy.Message, Does.Contain("stepIntervalTicks"));
        }

        [Test]
        public void MassNavigationConfig_NamesCrowdCostBudgetByItsActualBehavior()
        {
            JsonObject config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject runtime = config["runtime"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.runtime must be authored.");
            JsonObject flow = runtime["flow"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.flow must be authored.");
            flow.Remove("enabled");
            flow.Remove("iterationsPerStep");
            flow.Remove("maxIterationsPerStep");
            flow["crowdCostEnabled"] = false;
            flow["crowdStampBudgetAgentsPerRefresh"] = 4_096;

            MassNavigationConfig loaded = MassNavigationConfig.Load(runtime);

            Assert.That(loaded.Flow.CrowdCostEnabled, Is.False);
            Assert.That(loaded.Flow.CrowdStampBudgetAgentsPerRefresh, Is.EqualTo(4_096));

            flow["enabled"] = false;
            JsonException legacy = Assert.Throws<JsonException>(() => MassNavigationConfig.Load(runtime))!;
            Assert.That(legacy.Message, Does.Contain("enabled"));
        }

        [Test]
        public void MassNavigationConfig_UsesSolverAndStreamingAsSpatialOwners()
        {
            JsonObject config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject runtime = config["runtime"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.runtime must be authored.");
            JsonObject world = runtime["world"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.world must be authored.");
            world.Remove("solverWindowWidthCm");
            world.Remove("solverWindowHeightCm");
            world.Remove("streamingRadiusCm");

            Assert.That(() => MassNavigationConfig.Load(runtime), Throws.Nothing);

            world["solverWindowWidthCm"] = 10_000;
            JsonException legacy = Assert.Throws<JsonException>(() => MassNavigationConfig.Load(runtime))!;
            Assert.That(legacy.Message, Does.Contain("solverWindowWidthCm"));
        }

        [Test]
        public void MassNavigationConfig_RejectsCadenceThatSolverWouldSilentlyClamp()
        {
            JsonObject config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject runtime = config["runtime"]!.AsObject();
            runtime["cadence"]!["simulationHz"] = 15;
            runtime["semantics"]!["solver"]!["maxStepDtSeconds"] = 0.05f;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(runtime))!;
            Assert.That(ex.Message, Does.Contain("simulationHz"));
            Assert.That(ex.Message, Does.Contain("maxStepDtSeconds"));
        }

        [Test]
        public void AutoSpawnLayout_RequiresExplicitRandomSeed()
        {
            JsonObject config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject scenario = config["sceneAuthoring"]?["scenario"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.scenario must be authored.");
            JsonObject spawnLayout = scenario["spawnLayout"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.scenario.spawnLayout must be authored.");
            spawnLayout.Remove("randomSeed");

            JsonException missing = Assert.Throws<JsonException>(() => MassNavigationCapabilityProfile.Load(config))!;
            Assert.That(missing.Message, Does.Contain("randomSeed"));
        }

        [Test]
        public void MassNavigationConfig_CommandActorCapacityMustCoverAuthoredScenarioAgents()
        {
            JsonObject initialScratchConfig = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            int authoredAgentCount = ResolveAuthoredAgentCount(initialScratchConfig);
            JsonObject runtimeCapacity = initialScratchConfig["runtime"]?["capacity"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.runtime.capacity must be authored.");
            runtimeCapacity["initialCommandActorScratchCapacity"] = authoredAgentCount - 1;

            InvalidOperationException initialScratch = Assert.Throws<InvalidOperationException>(() => MassNavigationCapabilityProfile.Load(initialScratchConfig))!;
            Assert.That(initialScratch.Message, Does.Contain("runtime.capacity.initialCommandActorScratchCapacity"));
            Assert.That(initialScratch.Message, Does.Contain("authored scene agent count"));

            JsonObject runtimeScratchConfig = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            authoredAgentCount = ResolveAuthoredAgentCount(runtimeScratchConfig);
            MassNavigationCapabilityProfile runtimeProfile = MassNavigationCapabilityProfile.Load(runtimeScratchConfig);
            runtimeProfile.Runtime.Capacity.CommandActorScratchCapacity = authoredAgentCount - 1;
            MassNavigationScenarioConfig scenario = runtimeProfile.SceneAuthoring.Scenario!;

            InvalidOperationException runtimeScratch = Assert.Throws<InvalidOperationException>(
                () => runtimeProfile.Runtime.Capacity.ValidateForScenario(
                    scenario.Teams.Length,
                    scenario.AgentsPerTeam))!;
            Assert.That(runtimeScratch.Message, Does.Contain("runtime.capacity.commandActorScratchCapacity"));
            Assert.That(runtimeScratch.Message, Does.Contain("authored scene agent count"));
        }

        [Test]
        public void MassNavigationConfig_RejectsLegacyWorldObstacles()
        {
            JsonObject config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject runtime = config["runtime"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.runtime must be authored.");
            JsonObject world = runtime["world"]?.AsObject()
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

            JsonException ex = Assert.Throws<JsonException>(() => MassNavigationConfig.Load(runtime))!;
            Assert.That(ex.Message, Does.Contain("obstacles"));
        }

        [Test]
        public void MassNavigationConfig_RequiresExplicitStrictCaseAvoidanceMode()
        {
            JsonObject missingModeConfig = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject missingRuntime = missingModeConfig["runtime"]!.AsObject();
            JsonObject missingAvoidance = missingRuntime["avoidance"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.avoidance must be authored.");
            missingAvoidance.Remove("mode");

            JsonException missing = Assert.Throws<JsonException>(() => MassNavigationConfig.Load(missingRuntime))!;
            Assert.That(missing.Message, Does.Contain("mode"));

            JsonObject wrongCaseConfig = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject wrongCaseRuntime = wrongCaseConfig["runtime"]!.AsObject();
            JsonObject wrongCaseAvoidance = wrongCaseRuntime["avoidance"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.avoidance must be authored.");
            wrongCaseAvoidance["mode"] = "orca";

            InvalidOperationException wrongCase = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(wrongCaseRuntime))!;
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
                    CreateProfilePlanSet(),
                    layer,
                    CreateSpawnLayout(randomSeed: 1234));
                TeamManager.LoadConfig(new TeamConfig
                {
                    DefaultRelationship = "Friendly",
                    Relationships = new List<RelationshipEntry>(),
                });

                using var world = World.Create();
                var navGroups = new MassNavigationGroupRuntime(
                    new MassNavigationFormationRuntime(LoadBaseMassNavigationConfig().Semantics.Group),
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
                new MassNavigationFormationRuntime(LoadBaseMassNavigationConfig().Semantics.Group),
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
        public void SimulationRuntime_PropagatesFormationGroupSemanticsToMassNavigationFlow()
        {
            MassNavigationConfig config = LoadBaseMassNavigationConfig();
            MassNavigationGroupSemantics group = config.Semantics.Group;
            group.FormationLineSpacingCm = 111f;
            group.FormationSquareSpacingCm = 222f;
            group.FormationCircleSpacingCm = 333f;
            group.FormationCircleMinRadiusCm = 444f;
            group.FormationWedgeSpacingCm = 555f;
            group.FormationRotationEpsilonRadians = 0.0123f;

            var runtime = new MassNavigationSimulationRuntime(new Ludots.Core.Map.MapId("test"), config);
            MassNavigationGroupSemantics massFlowGroup = runtime.GetRuntimeGroupSemantics();

            Assert.That(massFlowGroup.FormationLineSpacingCm, Is.EqualTo(group.FormationLineSpacingCm));
            Assert.That(massFlowGroup.FormationSquareSpacingCm, Is.EqualTo(group.FormationSquareSpacingCm));
            Assert.That(massFlowGroup.FormationCircleSpacingCm, Is.EqualTo(group.FormationCircleSpacingCm));
            Assert.That(massFlowGroup.FormationCircleMinRadiusCm, Is.EqualTo(group.FormationCircleMinRadiusCm));
            Assert.That(massFlowGroup.FormationWedgeSpacingCm, Is.EqualTo(group.FormationWedgeSpacingCm));
            Assert.That(massFlowGroup.FormationRotationEpsilonRadians, Is.EqualTo(group.FormationRotationEpsilonRadians));
        }

        [Test]
        public void SimulationRuntime_PropagatesAllMappedConfigFieldsToMassNavigationFlow()
        {
            MassNavigationConfig config = LoadBaseMassNavigationConfig();
            int seed = 1;
            MutateWritableLeaves(config.Arrival, ref seed);
            MutateWritableLeaves(config.Avoidance, ref seed);
            MutateWritableLeaves(config.Semantics, ref seed);

            var runtime = new MassNavigationSimulationRuntime(new Ludots.Core.Map.MapId("test"), config);
            MassNavigationFlowSolverState flow = runtime.GetFlowSolverForTests();

            AssertWritableLeavesEqual(config.Arrival, flow.ArrivalTuning, "arrival");
            AssertWritableLeavesEqual(config.Avoidance, flow.AvoidanceTuning, "avoidance");
            AssertWritableLeavesEqual(config.Semantics, flow.Semantics, "semantics");
        }

        [Test]
        public void SimulationRuntime_CompilesImmutableExecutionPlanFromAuthoringConfig()
        {
            MassNavigationConfig config = LoadBaseMassNavigationConfig();
            var runtime = new MassNavigationSimulationRuntime(new Ludots.Core.Map.MapId("test"), config);
            int cadenceHz = runtime.Cadence.FlowStepHz;
            int crowdBudget = runtime.FlowConfig.CrowdStampBudgetAgentsPerRefresh;
            int streamingRadius = runtime.Streaming.RadiusCm;
            float formationRotationEpsilon = runtime.FormationRuntime.RotationEpsilonRadians;

            config.Cadence.FlowStepHz = cadenceHz + 1;
            config.Flow.CrowdStampBudgetAgentsPerRefresh = crowdBudget + 1;
            config.Streaming.RadiusCm = streamingRadius + config.World!.StreamingChunkSizeCm;
            config.Semantics.Group.FormationRotationEpsilonRadians = formationRotationEpsilon + 1f;

            Assert.Multiple(() =>
            {
                Assert.That(runtime.Cadence.FlowStepHz, Is.EqualTo(cadenceHz));
                Assert.That(runtime.FlowConfig.CrowdStampBudgetAgentsPerRefresh, Is.EqualTo(crowdBudget));
                Assert.That(runtime.Streaming.RadiusCm, Is.EqualTo(streamingRadius));
                Assert.That(runtime.FormationRuntime.RotationEpsilonRadians, Is.EqualTo(formationRotationEpsilon));
            });
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
            config.Capacity.InitialCommandActorSnapshotCapacity = 4;
            config.Capacity.InitialCommandActorScratchCapacity = 4;
            config.Capacity.GroupMembershipAgentCapacity = 4;
            config.Capacity.CommandActorScratchCapacity = 4;
            config.Capacity.GroupMemberCapacity = 4;
            config.Capacity.OrderIngestionMemberCapacity = 4;
            var runtime = new MassNavigationSimulationRuntime(new Ludots.Core.Map.MapId("test"), config);
            runtime.BindBoardWorld(
                new WorldSizeSpec(new WorldAabbCm(-5_000, -5_000, 10_000, 10_000), 100),
                new Ludots.Core.Navigation.GraphWorld.WorldGridLoadedChunks(config.World!.StreamingChunkSizeCm));

            MassNavigationAgentLayer layer = CreateAgentLayer();
            Entity light = CreateAuthoredAgentEntity(world, localX: 1000f, localY: 1200f, layer);
            Entity heavy = CreateAuthoredAgentEntity(world, localX: 1400f, localY: 1200f, layer);
            MassNavigationAgentSeed[] seeds =
            {
                CreateAvoidanceSeed(teamId: 1, localX: 1000f, localY: 1200f, heavy: false, layer),
                CreateAvoidanceSeed(teamId: 2, localX: 1400f, localY: 1200f, heavy: true, layer),
            };
            runtime.RebuildFromAuthoredAgents(world, new[] { light, heavy }, seeds, new[] { true, true });
            runtime.SetCommandActorSnapshot(new[] { light }, revision: 1);
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

            MassNavigationAvoidanceAgentSnapshot commandActorAgent = agents.Single(agent => agent.AgentIndex == 0);
            MassNavigationAvoidanceAgentSnapshot heavyAgent = agents.Single(agent => agent.AgentIndex == 1);
            Assert.Multiple(() =>
            {
                Assert.That(commandActorAgent.LocalXCm, Is.EqualTo(1000f).Within(0.001f));
                Assert.That(commandActorAgent.LocalYCm, Is.EqualTo(1200f).Within(0.001f));
                Assert.That(commandActorAgent.WorldXCm, Is.EqualTo(-4000f).Within(0.001f));
                Assert.That(commandActorAgent.WorldYCm, Is.EqualTo(-3800f).Within(0.001f));
                Assert.That(commandActorAgent.CommandActor, Is.True);
                Assert.That(commandActorAgent.InsidePlayArea, Is.True);
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

        [Test]
        public void SeparationCapacity_DoesNotAllocateUnusedOrcaOrSonarScratchPerAgent()
        {
            const int preparedCapacity = 1_000;
            MassNavigationFlowSolverState flow = CreateConfiguredFlow(parallelWorkerCount: 4);

            flow.PreallocateAgentCapacity(preparedCapacity);

            Assert.Multiple(() =>
            {
                Assert.That(GetPrivateArrayLength(flow, "_avoidanceNeighborScratch"), Is.Zero);
                Assert.That(GetPrivateArrayLength(flow, "_orcaLineScratch"), Is.Zero);
                Assert.That(GetPrivateArrayLength(flow, "_orcaProjectionLineScratch"), Is.Zero);
                Assert.That(GetPrivateArrayLength(flow, "_sonarIntervalScratch"), Is.Zero);
            });
        }

        [TestCase("Orca")]
        [TestCase("Sonar")]
        public void HighQualityAvoidanceCapacity_AllocatesScratchPerWorkerNotPerAgent(string mode)
        {
            const int preparedCapacity = 10_000;
            const int workerCount = 4;
            MassNavigationFlowSolverState flow = CreateConfiguredFlow(workerCount, mode);

            flow.PreallocateAgentCapacity(preparedCapacity);

            int neighborLimit = mode == "Orca"
                ? flow.AvoidanceTuning.Orca.MaxNeighbors
                : flow.AvoidanceTuning.Sonar.MaxNeighbors;
            Assert.Multiple(() =>
            {
                Assert.That(GetPrivateArrayLength(flow, "_avoidanceNeighborScratch"), Is.EqualTo(workerCount * neighborLimit));
                Assert.That(GetPrivateArrayLength(flow, "_orcaLineScratch"), Is.EqualTo(mode == "Orca" ? workerCount * neighborLimit : 0));
                Assert.That(GetPrivateArrayLength(flow, "_orcaProjectionLineScratch"), Is.EqualTo(mode == "Orca" ? workerCount * OrcaSolver2D.MaxProjectionLines : 0));
                Assert.That(GetPrivateArrayLength(flow, "_sonarIntervalScratch"), Is.EqualTo(mode == "Sonar" ? workerCount * SonarSolver2D.MaxIntervals : 0));
            });
        }

        [TestCase("Orca")]
        [TestCase("Sonar")]
        public void ParallelHighQualityAvoidance_MatchesSingleWorkerTrajectory(string mode)
        {
            World.SharedJobScheduler ??= new JobScheduler(new JobScheduler.Config
            {
                ThreadPrefixName = "MassNavAvoidanceTests",
                ThreadCount = 0,
                MaxExpectedConcurrentJobs = 64,
                StrictAllocationMode = false
            });

            const int agentCount = 32;
            MassNavigationAgentLayer layer = CreateAgentLayer();
            var seeds = new MassNavigationAgentSeed[agentCount];
            for (int i = 0; i < agentCount; i++)
            {
                seeds[i] = CreateAvoidanceSeed(
                    teamId: 1,
                    localX: 4_800f + ((i % 8) * 55f),
                    localY: 4_800f + ((i / 8) * 55f),
                    heavy: false,
                    layer);
            }

            MassNavigationFlowSolverState singleWorker = CreateConfiguredFlow(parallelWorkerCount: 1, mode);
            MassNavigationFlowSolverState parallel = CreateConfiguredFlow(parallelWorkerCount: 4, mode);
            singleWorker.Semantics.Solver.ParallelStepMinAgents = 2;
            parallel.Semantics.Solver.ParallelStepMinAgents = 2;
            singleWorker.ResetAuthoredAgents(seeds);
            parallel.ResetAuthoredAgents(seeds);
            for (int i = 0; i < agentCount; i++)
            {
                float targetX = 9_000f - seeds[i].LocalPositionXCm;
                float targetY = 9_000f - seeds[i].LocalPositionYCm;
                singleWorker.SetUnitTarget(i, targetX, targetY, resetRecovery: true);
                parallel.SetUnitTarget(i, targetX, targetY, resetRecovery: true);
            }

            TeamManager.LoadConfig(new TeamConfig
            {
                DefaultRelationship = "Friendly",
                Relationships = new List<RelationshipEntry>(),
            });
            using var singleWorld = World.Create();
            using var parallelWorld = World.Create();
            MassNavigationGroupRuntime singleGroups = CreateNavGroupRuntime(agentCount);
            MassNavigationGroupRuntime parallelGroups = CreateNavGroupRuntime(agentCount);

            for (int step = 0; step < 20; step++)
            {
                singleWorker.Step(0.016f, singleWorld, singleGroups, runHardResolve: false, hardResolveCandidateThresholdAgents: agentCount + 1);
                parallel.Step(0.016f, parallelWorld, parallelGroups, runHardResolve: false, hardResolveCandidateThresholdAgents: agentCount + 1);
            }

            for (int i = 0; i < agentCount; i++)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(parallel.GetPositionX(i), Is.EqualTo(singleWorker.GetPositionX(i)).Within(0.0001f), $"agent {i} x");
                    Assert.That(parallel.GetPositionY(i), Is.EqualTo(singleWorker.GetPositionY(i)).Within(0.0001f), $"agent {i} y");
                    Assert.That(parallel.GetVelocityCmPerSecond(i).X, Is.EqualTo(singleWorker.GetVelocityCmPerSecond(i).X).Within(0.0001f), $"agent {i} vx");
                    Assert.That(parallel.GetVelocityCmPerSecond(i).Y, Is.EqualTo(singleWorker.GetVelocityCmPerSecond(i).Y).Within(0.0001f), $"agent {i} vy");
                });
            }
        }

        [Test]
        public void PreparedAgentCapacity_RejectsAppendInsteadOfResizingDuringRuntime()
        {
            MassNavigationFlowSolverState flow = CreateConfiguredFlow(parallelWorkerCount: 1);
            MassNavigationAgentLayer layer = CreateAgentLayer();
            MassNavigationAgentSeed first = CreateAvoidanceSeed(1, 1_000.25f, 1_200.5f, false, layer);
            MassNavigationAgentSeed second = CreateAvoidanceSeed(1, 1_400f, 1_200f, false, layer);
            flow.PreallocateAgentCapacity(1);
            flow.ResetAuthoredAgents(new[] { first });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => flow.AppendAuthoredAgents(new[] { second }))!;

            Assert.That(ex.Message, Does.Contain("prepared agent capacity 1"));
            Assert.That(flow.UnitCount, Is.EqualTo(1));
        }

        [Test]
        public void PreparedAgentCapacity_RecordsOneColdAllocationAndNoRuntimeGrowth()
        {
            MassNavigationFlowSolverState flow = CreateConfiguredFlow(parallelWorkerCount: 1);
            MassNavigationAgentLayer layer = CreateAgentLayer();
            MassNavigationAgentSeed first = CreateAvoidanceSeed(1, 1_000f, 1_200f, false, layer);
            MassNavigationAgentSeed second = CreateAvoidanceSeed(1, 1_400f, 1_200f, false, layer);

            flow.PreallocateAgentCapacity(2);
            int coldAllocationCount = flow.AgentStorageAllocationCount;
            flow.ResetAuthoredAgents(new[] { first });
            flow.AppendAuthoredAgents(new[] { second });

            Assert.That(flow.PreparedAgentCapacity, Is.EqualTo(2));
            Assert.That(coldAllocationCount, Is.EqualTo(1));
            Assert.That(flow.AgentStorageAllocationCount, Is.EqualTo(coldAllocationCount));
        }

        [Test]
        public void EntitySync_PreservesSubCentimeterWorldPositionPrecision()
        {
            using var world = World.Create();
            MassNavigationAgentLayer layer = CreateAgentLayer();
            MassNavigationAgentSeed seed = CreateAvoidanceSeed(1, 1_000.25f, 1_200.5f, false, layer);
            MassNavigationFlowSolverState flow = CreateConfiguredFlow(parallelWorkerCount: 1);
            flow.PreallocateAgentCapacity(1);
            flow.ResetAuthoredAgents(new[] { seed });
            Entity entity = CreateAuthoredAgentEntity(world, 0f, 0f, layer);
            var agentState = new MassNavigationAgentState();
            agentState.RegisterAgentAtIndex(entity, agentIndex: 0, controllable: true);

            flow.SyncEntities(world, agentState);

            WorldPositionCm position = world.Get<WorldPositionCm>(entity);
            Assert.That(position.Value.X.ToFloat(), Is.EqualTo(1_000.25f).Within(0.0001f));
            Assert.That(position.Value.Y.ToFloat(), Is.EqualTo(1_200.5f).Within(0.0001f));
        }

        private static MassNavigationFlowSolverState CreateSpawnedFlow(int randomSeed)
        {
            var flow = CreateConfiguredFlow(parallelWorkerCount: 1);
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            flow.Reset(
                new[] { 1 },
                unitsPerTeam: 4,
                CreateProfilePlanSet(),
                layer,
                CreateSpawnLayout(randomSeed));
            return flow;
        }

        private static MassNavigationFlowSolverState CreateConfiguredFlow(int parallelWorkerCount, string? avoidanceMode = null)
        {
            MassNavigationConfig config = LoadBaseMassNavigationConfig();
            if (avoidanceMode != null)
            {
                config.Avoidance.Mode = avoidanceMode;
                config.Avoidance.Validate();
            }

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
                new MassNavigationFormationRuntime(config.Semantics.Group),
                CreateRuntimeCapacity(agentCapacity: agentCapacity, groupMemberCapacity: agentCapacity));
        }

        private static MassNavigationConfig LoadBaseMassNavigationConfig()
        {
            MassNavigationConfig config = MassNavigationCapabilityProfile.Load(
                ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"))).Runtime;
            config.AgentProfiles.BindAgentProfiles(CreateAgentProfiles());
            return config;
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

        private static MassNavigationCapacityConfig CreateRuntimeCapacity(
            int agentCapacity = 16,
            int groupMemberCapacity = 16)
        {
            return new MassNavigationCapacityConfig
            {
                NavigationGroupCapacity = 8,
                GroupMembershipAgentCapacity = agentCapacity,
                CommandActorScratchCapacity = groupMemberCapacity,
                GroupMemberCapacity = groupMemberCapacity,
                OrderIngestionTokenCapacity = 8,
                OrderIngestionMemberCapacity = groupMemberCapacity,
                LoadedChunkCapacity = 16,
                MetadataTeamCapacity = 4,
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

        private static MassNavigationAgentProfilePlanSet CreateProfilePlanSet()
        {
            MassNavigationConfig config = LoadBaseMassNavigationConfig();
            config.AgentProfiles = CreateProfileSet();
            return MassNavigationRuntimePlan.Compile(config).AgentProfiles;
        }

        private static AgentProfileRegistry CreateAgentProfiles()
        {
            return new AgentProfileRegistry(new[]
            {
                new AgentProfileConfig
                {
                    Id = "heavy",
                    RadiusCm = 30,
                    HeightCm = 180,
                    ClearanceCm = 40,
                    Mass = 2,
                    Layer = 0
                },
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
            JsonNode node = JsonNode.Parse(File.ReadAllText(path))
                ?? throw new InvalidOperationException($"Expected JSON at '{path}'.");
            if (node is JsonObject obj)
            {
                return obj;
            }

            if (node is JsonArray profiles && profiles.Count == 1 && profiles[0] is JsonObject profile)
            {
                JsonObject resolved = (JsonObject)profile.DeepClone();
                resolved.Remove("id");
                resolved.Remove("extends");
                return resolved;
            }

            throw new InvalidOperationException($"Expected JSON object or one MassNavigation profile at '{path}'.");
        }

        private static int GetPrivateArrayLength(MassNavigationFlowSolverState flow, string fieldName)
        {
            FieldInfo field = typeof(MassNavigationFlowSolverState).GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Missing MassNavigationFlowSolverState field '{fieldName}'.");
            return ((Array?)field.GetValue(flow))?.Length
                ?? throw new InvalidOperationException($"MassNavigationFlowSolverState field '{fieldName}' is not an array.");
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
            JsonObject scenario = config["sceneAuthoring"]?["scenario"]?.AsObject()
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
