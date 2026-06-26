using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.MassCrowd.Runtime;
using Ludots.Core.Navigation.AgentProfiles;
using NUnit.Framework;
using Schedulers;
using Ludots.Core.Gameplay.Teams;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassFlowSimulationStateConfigurationTests
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
                var flow = new MassFlowSimulationState(CreateSolverConfig(parallelWorkerCount: 2));
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
                    new MassNavigationFormationRuntime(new MassNavigationGroupSemantics()),
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
        public void SimulationRuntime_PropagatesFormationGroupSemanticsToMassFlow()
        {
            MassNavigationConfig config = MassNavigationConfig.Load(
                ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json")));
            MassNavigationGroupSemantics group = config.Semantics.Group;
            group.FormationLineSpacingCm = 111f;
            group.FormationSquareSpacingCm = 222f;
            group.FormationCircleSpacingCm = 333f;
            group.FormationCircleMinRadiusCm = 444f;
            group.FormationWedgeSpacingCm = 555f;
            group.FormationRotationEpsilonRadians = 0.0123f;
            group.FormationRotationSpeedRadiansPerSecond = 6.75f;

            var runtime = new MassNavigationSimulationRuntime(config);
            MassNavigationGroupSemantics massFlowGroup = runtime.GetRuntimeGroupSemantics();

            Assert.That(massFlowGroup.FormationLineSpacingCm, Is.EqualTo(group.FormationLineSpacingCm));
            Assert.That(massFlowGroup.FormationSquareSpacingCm, Is.EqualTo(group.FormationSquareSpacingCm));
            Assert.That(massFlowGroup.FormationCircleSpacingCm, Is.EqualTo(group.FormationCircleSpacingCm));
            Assert.That(massFlowGroup.FormationCircleMinRadiusCm, Is.EqualTo(group.FormationCircleMinRadiusCm));
            Assert.That(massFlowGroup.FormationWedgeSpacingCm, Is.EqualTo(group.FormationWedgeSpacingCm));
            Assert.That(massFlowGroup.FormationRotationEpsilonRadians, Is.EqualTo(group.FormationRotationEpsilonRadians));
            Assert.That(massFlowGroup.FormationRotationSpeedRadiansPerSecond, Is.EqualTo(group.FormationRotationSpeedRadiansPerSecond));
        }

        private static MassFlowSimulationState CreateSpawnedFlow(int randomSeed)
        {
            var flow = new MassFlowSimulationState(CreateSolverConfig(parallelWorkerCount: 1));
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            flow.Reset(
                new[] { 1 },
                unitsPerTeam: 4,
                CreateProfileSet(),
                layer,
                CreateSpawnLayout(randomSeed));
            return flow;
        }

        private static MassFlowSolverConfig CreateSolverConfig(int parallelWorkerCount)
        {
            return new MassFlowSolverConfig
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
                SelectionMemberScratchCapacity = groupMemberCapacity,
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

        private static JsonObject ReadObject(string path)
        {
            return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                ?? throw new InvalidOperationException($"Expected JSON object at '{path}'.");
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
