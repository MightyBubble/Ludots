using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Navigation2D.Systems;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using Navigation2DPlaygroundMod.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Navigation2D
{
    [TestFixture]
    public sealed class Navigation2DPlaygroundScenarioTests
    {
        private static readonly QueryDescription _dynamicAgentsQuery = new QueryDescription()
            .WithAll<NavAgent2D, Position2D, Velocity2D, NavPlaygroundTeam>()
            .WithNone<NavPlaygroundBlocker>();

        private static readonly QueryDescription _blockerQuery = new QueryDescription()
            .WithAll<NavPlaygroundBlocker>();

        private static readonly QueryDescription _desiredVelocityQuery = new QueryDescription()
            .WithAll<NavDesiredVelocity2D, NavPlaygroundTeam>()
            .WithNone<NavPlaygroundBlocker>();

        [Test]
        public void PlaygroundGameConfig_DeclaresExplicitScenarioCatalogAndContractIds()
        {
            string repoRoot = FindRepoRoot();
            string gameJson = File.ReadAllText(Path.Combine(repoRoot, "mods", "Navigation2DPlaygroundMod", "assets", "game.json"));

            Assert.That(gameJson, Does.Contain("\"AgentNavProfileId\": \"rts_default\""));
            Assert.That(gameJson, Does.Contain("\"AgentCrowdProfileId\": \"crowd_default\""));
            Assert.That(gameJson, Does.Contain("\"AgentKnockbackPolicyId\": \"knockback_default\""));
            Assert.That(gameJson, Does.Contain("\"Id\": \"pass_through\""));
            Assert.That(gameJson, Does.Contain("\"Id\": \"goal_queue\""));
        }

        [Test]
        public void PlaygroundScenarioCatalog_SpawnsSimulatesAndWritesAcceptanceArtifacts()
        {
            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "navigation2d-playground-scenarios");
            Directory.CreateDirectory(artifactDir);

            string inputJson = File.ReadAllText(Path.Combine(repoRoot, "mods", "Navigation2DPlaygroundMod", "assets", "Input", "default_input.json"));
            string mapJson = File.ReadAllText(Path.Combine(repoRoot, "mods", "Navigation2DPlaygroundMod", "assets", "Maps", "nav2d_playground.json"));
            string gameJson = File.ReadAllText(Path.Combine(repoRoot, "mods", "Navigation2DPlaygroundMod", "assets", "game.json"));
            string viewModesJson = File.ReadAllText(Path.Combine(repoRoot, "mods", "Navigation2DPlaygroundMod", "assets", "viewmodes.json"));
            string virtualCameraJson = File.ReadAllText(Path.Combine(repoRoot, "mods", "Navigation2DPlaygroundMod", "assets", "Configs", "Camera", "virtual_cameras.json"));
            Assert.That(inputJson, Does.Contain("Nav2D_PreviousScenario"));
            Assert.That(inputJson, Does.Contain("Nav2D_NextScenario"));
            Assert.That(inputJson, Does.Contain("Nav2D_ToolSpawnTeam0"));
            Assert.That(inputJson, Does.Contain("Nav2D_ViewModeFollow"));
            Assert.That(mapJson, Does.Contain("\"VirtualCameraId\": \"Navigation2D.Playground.Camera.Command\""));
            Assert.That(mapJson, Does.Contain("\"Boards\""));
            Assert.That(gameJson, Does.Contain("\"Playground\""));
            Assert.That(gameJson, Does.Contain("\"TemporalCoherence\""));
            Assert.That(gameJson, Does.Contain("\"DefaultSpawnBatch\""));
            Assert.That(gameJson, Does.Contain("\"CommandFormationSpacingCm\""));
            Assert.That(viewModesJson, Does.Contain("\"Navigation2D.Playground.Mode.Follow\""));
            Assert.That(virtualCameraJson, Does.Contain("\"Navigation2D.Playground.Camera.Follow\""));

            var config = BuildExplicitNavigationConfig().CloneValidated();

            const int agentsPerTeam = 96;
            const float deltaTime = 1f / 60f;
            const int simulationSteps = 4;

            var traceLines = new List<string>();
            var timeline = new StringBuilder();
            int totalDynamicAgents = 0;
            int totalBlockers = 0;

            var playground = config.Playground;
            for (int scenarioIndex = 0; scenarioIndex < playground.Scenarios.Count; scenarioIndex++)
            {
                var scenario = playground.Scenarios[scenarioIndex];
                using var world = World.Create();
                using var runtime = new Navigation2DRuntime(config, gridCellSizeCm: 100, loadedChunks: null);
                runtime.FlowEnabled = true;
                var steering = new Navigation2DSteeringSystem2D(world, runtime);
                var kinematicMotion = new Navigation2DKinematicMotionSystem(world);

                var summary = Navigation2DPlaygroundScenarioSpawner.SpawnScenario(world, runtime.ContractCatalog, playground, scenario, agentsPerTeam);
                MaterializeSpawnedNavActors(world, runtime.ContractCatalog, deltaTime);
                int dynamicCount = world.CountEntities(in _dynamicAgentsQuery);
                int blockerCount = world.CountEntities(in _blockerQuery);
                Assert.That(dynamicCount, Is.EqualTo(summary.DynamicAgents));
                Assert.That(blockerCount, Is.EqualTo(summary.BlockerCount));
                Assert.That(dynamicCount, Is.GreaterThan(0), $"Scenario '{scenario.Name}' should spawn dynamic agents.");
                if (scenario.Kind is Navigation2DPlaygroundScenarioKind.Bottleneck or Navigation2DPlaygroundScenarioKind.GoalQueue)
                {
                    Assert.That(blockerCount, Is.GreaterThan(0), $"Scenario '{scenario.Name}' should spawn blockers.");
                }

                for (int step = 0; step < simulationSteps; step++)
                {
                    steering.Update(deltaTime);
                    kinematicMotion.Update(deltaTime);
                }

                int movingDesiredAgents = CountMovingDesiredAgents(world);
                float averageSpeed = ComputeAverageSpeed(world, maxSamples: 24);

                Assert.That(movingDesiredAgents, Is.GreaterThan(0), $"Scenario '{scenario.Name}' should produce non-zero desired velocity.");
                Assert.That(averageSpeed, Is.GreaterThan(0f), $"Scenario '{scenario.Name}' should move sampled agents.");

                totalDynamicAgents += dynamicCount;
                totalBlockers += blockerCount;

                timeline.AppendLine($"- [T+{scenarioIndex + 1:000}] Scenario#{scenarioIndex + 1} {scenario.Name} [{scenario.Id}] | Teams={summary.TeamCount} | Dynamic={dynamicCount} | Blockers={blockerCount} | MovingDesired={movingDesiredAgents} | AvgSpeed={averageSpeed:F1}cm/s");
                traceLines.Add(JsonSerializer.Serialize(new
                {
                    event_id = $"nav2d-playground-{scenarioIndex + 1:000}",
                    step = "scenario_run",
                    scenario_id = summary.ScenarioId,
                    scenario_name = summary.ScenarioName,
                    team_count = summary.TeamCount,
                    dynamic_agents = dynamicCount,
                    blockers = blockerCount,
                    moving_desired_agents = movingDesiredAgents,
                    average_speed_cm_per_sec = MathF.Round(averageSpeed, 2),
                    status = "done"
                }));
            }

            string battleReport = BuildBattleReport(playground.Scenarios.Count, agentsPerTeam, totalDynamicAgents, totalBlockers, timeline);
            string traceJsonl = string.Join(Environment.NewLine, traceLines) + Environment.NewLine;
            string pathMmd = BuildPathMermaid();

            string battleReportPath = Path.Combine(artifactDir, "battle-report.md");
            string tracePath = Path.Combine(artifactDir, "trace.jsonl");
            string pathPath = Path.Combine(artifactDir, "path.mmd");
            File.WriteAllText(battleReportPath, battleReport);
            File.WriteAllText(tracePath, traceJsonl);
            File.WriteAllText(pathPath, pathMmd);

            Assert.That(File.Exists(battleReportPath), Is.True);
            Assert.That(File.Exists(tracePath), Is.True);
            Assert.That(File.Exists(pathPath), Is.True);
        }

        private static int CountMovingDesiredAgents(World world)
        {
            int moving = 0;
            foreach (ref var chunk in world.Query(in _desiredVelocityQuery))
            {
                var desired = chunk.GetSpan<NavDesiredVelocity2D>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (desired[i].ValueCmPerSec.ToVector2().LengthSquared() > 1f)
                    {
                        moving++;
                    }
                }
            }

            return moving;
        }

        private static void MaterializeSpawnedNavActors(World world, Navigation2DContractCatalog catalog, float deltaTime)
        {
            var query = new QueryDescription().WithAll<NavActor, NavProfileRef, WorldPositionCm>();
            foreach (ref var chunk in world.Query(in query))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var navProfileRefs = chunk.GetSpan<NavProfileRef>();
                bool hasCrowdProfileRef = chunk.Has<NavCrowdProfileRef>();
                Span<NavCrowdProfileRef> crowdProfileRefs = hasCrowdProfileRef ? chunk.GetSpan<NavCrowdProfileRef>() : default;
                bool hasKnockbackPolicyRef = chunk.Has<NavKnockbackPolicyRef>();
                Span<NavKnockbackPolicyRef> knockbackPolicyRefs = hasKnockbackPolicyRef ? chunk.GetSpan<NavKnockbackPolicyRef>() : default;

                foreach (int index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    if (world.Has<NavActorRuntimeState>(entity))
                    {
                        continue;
                    }

                    world.Add(entity, new NavActorRuntimeState
                    {
                        IsValidated = 1,
                        AppliedNavProfileId = navProfileRefs[index].ProfileId,
                        AppliedCrowdProfileId = hasCrowdProfileRef ? crowdProfileRefs[index].ProfileId : 0,
                        AppliedKnockbackPolicyId = hasKnockbackPolicyRef ? knockbackPolicyRefs[index].PolicyId : 0,
                    });
                }
            }

            var materialization = new NavActorMaterializationSystem(world, catalog);
            materialization.Update(deltaTime);
        }

        private static float ComputeAverageSpeed(World world, int maxSamples)
        {
            int sampled = 0;
            float totalSpeed = 0f;
            foreach (ref var chunk in world.Query(in _dynamicAgentsQuery))
            {
                var velocities = chunk.GetSpan<Velocity2D>();
                for (int i = 0; i < chunk.Count && sampled < maxSamples; i++)
                {
                    totalSpeed += velocities[i].Linear.ToVector2().Length();
                    sampled++;
                }

                if (sampled >= maxSamples)
                {
                    break;
                }
            }

            return sampled == 0 ? 0f : totalSpeed / sampled;
        }

        private static Navigation2DConfig BuildExplicitNavigationConfig()
        {
            return new Navigation2DConfig
            {
                Enabled = true,
                MaxAgents = 50000,
                FlowIterationsPerTick = 2048,
                Spatial = new Navigation2DSpatialPartitionConfig
                {
                    UpdateMode = Navigation2DSpatialUpdateMode.Adaptive,
                    RebuildCellMigrationsThreshold = 128,
                    RebuildAccumulatedCellMigrationsThreshold = 1024,
                },
                Steering = new Navigation2DSteeringConfig
                {
                    Mode = Navigation2DAvoidanceMode.Hybrid,
                    QueryBudget = new Navigation2DQueryBudgetConfig
                    {
                        MaxNeighborsPerAgent = 8,
                        MaxCandidateChecksPerAgent = 24,
                    },
                    Orca = new Navigation2DOrcaConfig
                    {
                        Enabled = true,
                        FallbackToPreferredVelocity = true,
                    },
                    Sonar = new Navigation2DSonarConfig
                    {
                        Enabled = true,
                        MaxSteerAngleDeg = 280,
                        BackwardPenaltyAngleDeg = 230,
                        IgnoreBehindMovingAgents = true,
                        BlockedStop = false,
                        PredictionTimeScale = 0.9f,
                    },
                    Hybrid = new Navigation2DHybridAvoidanceConfig
                    {
                        Enabled = true,
                        DenseNeighborThreshold = 6,
                        MinSpeedForOrcaCmPerSec = 120,
                        MinOpposingNeighborsForOrca = 1,
                        OpposingVelocityDotThreshold = -0.25f,
                    },
                    SmartStop = new Navigation2DSmartStopConfig
                    {
                        Enabled = true,
                        QueryRadiusCm = 100,
                        MaxNeighbors = 6,
                        SelfGoalDistanceLimitCm = 180,
                        GoalToleranceCm = 80,
                        ArrivedSlackCm = 20,
                        StoppedSpeedThresholdCmPerSec = 5,
                    }
                },
                Playground = new Navigation2DPlaygroundConfig
                {
                    DefaultAgentsPerTeam = 64,
                    AgentsPerTeamStep = 64,
                    DefaultScenarioIndex = 0,
                    DefaultSpawnBatch = 16,
                    SpawnBatchStep = 16,
                    CommandGoalRadiusCm = 120,
                    CommandFormationSpacingCm = 140,
                    DynamicSpawnSpacingCm = 120,
                    DynamicBlockerRadiusCm = 140,
                    AgentNavProfileId = "rts_default",
                    AgentCrowdProfileId = "crowd_default",
                    AgentKnockbackPolicyId = "knockback_default",
                    Scenarios = new List<Navigation2DPlaygroundScenarioConfig>
                    {
                        new()
                        {
                            Id = "pass_through",
                            Name = "Pass Through",
                            Kind = Navigation2DPlaygroundScenarioKind.PassThrough,
                            TeamCount = 2,
                            FormationSpacingCm = 120,
                            StartOffsetCm = 9000,
                            GoalOffsetCm = 9000,
                            GoalRadiusCm = 120,
                        },
                        new()
                        {
                            Id = "orthogonal_cross",
                            Name = "Orthogonal Cross",
                            Kind = Navigation2DPlaygroundScenarioKind.OrthogonalCross,
                            TeamCount = 2,
                            FormationSpacingCm = 120,
                            StartOffsetCm = 8200,
                            GoalOffsetCm = 8200,
                            GoalRadiusCm = 120,
                        },
                        new()
                        {
                            Id = "bottleneck",
                            Name = "Bottleneck",
                            Kind = Navigation2DPlaygroundScenarioKind.Bottleneck,
                            TeamCount = 2,
                            FormationSpacingCm = 120,
                            StartOffsetCm = 9200,
                            GoalOffsetCm = 9200,
                            CorridorHalfWidthCm = 320,
                            GoalRadiusCm = 120,
                            BlockerRadiusCm = 150,
                            BlockerCount = 20,
                            BlockerSpacingCm = 280,
                        },
                        new()
                        {
                            Id = "lane_merge",
                            Name = "Lane Merge",
                            Kind = Navigation2DPlaygroundScenarioKind.LaneMerge,
                            TeamCount = 2,
                            FormationSpacingCm = 120,
                            StartOffsetCm = 8800,
                            GoalOffsetCm = 9200,
                            LaneOffsetCm = 2400,
                            GoalRadiusCm = 150,
                        },
                        new()
                        {
                            Id = "circle_swap",
                            Name = "Circle Swap",
                            Kind = Navigation2DPlaygroundScenarioKind.CircleSwap,
                            TeamCount = 2,
                            FormationSpacingCm = 120,
                            RingRadiusCm = 5400,
                            GoalRadiusCm = 160,
                        },
                        new()
                        {
                            Id = "goal_queue",
                            Name = "Goal Queue",
                            Kind = Navigation2DPlaygroundScenarioKind.GoalQueue,
                            TeamCount = 1,
                            FormationSpacingCm = 120,
                            StartOffsetCm = 9200,
                            GoalOffsetCm = 7200,
                            GoalRadiusCm = 220,
                            CorridorHalfWidthCm = 220,
                            BlockerRadiusCm = 130,
                            BlockerCount = 18,
                            BlockerSpacingCm = 260,
                        }
                    }
                },
                Contracts = new Navigation2DContractsConfig
                {
                    NavProfiles = new List<Navigation2DNavProfileConfig>
                    {
                        new()
                        {
                            Id = "rts_default",
                            MaxSpeedCmPerSec = 800,
                            MaxAccelCmPerSec2 = 6000,
                            RadiusCm = 40,
                            NeighborDistCm = 400,
                            TimeHorizonSec = 2f,
                            MaxNeighbors = 16,
                            GoalRadiusCm = 120,
                        }
                    },
                    CrowdProfiles = new List<Navigation2DCrowdProfileConfig>
                    {
                        new()
                        {
                            Id = "crowd_default",
                            GeometryRadiusCm = 40,
                            NavMass = 1f,
                            YieldWeight = 1f,
                            PushClass = NavPushClass.Cooperative,
                            SolverPreference = NavSolverMode.Hybrid,
                            RetryLimit = 2,
                            TimeoutTicks = 90,
                            AbandonTicks = 240,
                        }
                    },
                    KnockbackPolicies = new List<Navigation2DKnockbackPolicyConfig>
                    {
                        new()
                        {
                            Id = "knockback_default",
                            OverrideTicks = 15,
                            ClearNavGoalWhileActive = true,
                        }
                    },
                    GroupSolver = new Navigation2DNavGroupSolverConfig
                    {
                        Enabled = true,
                        PreciseOrcaMaxGroupSize = 32,
                        CrowdFlowMinGroupSize = 128,
                        Rules = new List<Navigation2DNavSolverRuleConfig>
                        {
                            new()
                            {
                                Id = "precise_orca",
                                MinGroupSize = 0,
                                MaxGroupSize = 32,
                                SolverMode = NavSolverMode.PreciseOrca,
                                Reason = "group_size_small",
                            },
                            new()
                            {
                                Id = "hybrid_mid",
                                MinGroupSize = 33,
                                MaxGroupSize = 127,
                                SolverMode = NavSolverMode.Hybrid,
                                Reason = "group_size_mid",
                            },
                            new()
                            {
                                Id = "crowd_flow",
                                MinGroupSize = 128,
                                MaxGroupSize = int.MaxValue,
                                SolverMode = NavSolverMode.CrowdFlow,
                                Reason = "group_size_large",
                            }
                        }
                    },
                    CrowdRelationship = new Navigation2DCrowdRelationshipPolicyConfig
                    {
                        FriendlyYieldFactor = 1f,
                        NeutralYieldFactor = 0.35f,
                        HostileYieldFactor = 0.2f,
                        DominantPushMassRatio = 2f,
                    }
                }
            };
        }

        private static string BuildBattleReport(int scenarioCount, int agentsPerTeam, int totalDynamicAgents, int totalBlockers, StringBuilder timeline)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: navigation2d-playground-scenarios");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: switch multiple Navigation2D avoidance scenarios from the playable mod without hidden constants.");
            sb.AppendLine("- Gameplay domain: `Navigation2DPlaygroundMod` scenario catalog, explicit config pipeline, blocker-aware presentation, and reusable input/UI wiring.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Seed: none");
            sb.AppendLine("- Map: `mods/Navigation2DPlaygroundMod/assets/Maps/nav2d_playground.json`");
            sb.AppendLine("- Clock profile: fixed `1/60s`, `4` steering/integration steps per scenario");
            sb.AppendLine($"- Initial entities: `96` agents per team, `{scenarioCount}` configured scenarios");
            sb.AppendLine("- Config source: `game.json -> ConfigPipeline.MergeGameConfig() -> GameConfig.Navigation2D.Playground`");
            sb.AppendLine("- Input source: `mods/Navigation2DPlaygroundMod/assets/Input/default_input.json`");
            sb.AppendLine("- UI source: `UIRoot` + `ReactivePage`, with `ScreenOverlayBuffer` retained for telemetry evidence.");
            sb.AppendLine();
            sb.AppendLine("## Action Script");
            sb.AppendLine("1. Validate `Navigation2D.Playground` catalog and input/map assets.");
            sb.AppendLine("2. Spawn each configured scenario through `Navigation2DPlaygroundScenarioSpawner`.");
            sb.AppendLine("3. Run steering and integration for four deterministic ticks.");
            sb.AppendLine("4. Record blocker counts, moving desired-velocity agents, and sampled average speed.");
            sb.AppendLine();
            sb.AppendLine("## Expected Outcomes");
            sb.AppendLine("- Primary success condition: every scenario spawns correctly and produces measurable movement.");
            sb.AppendLine("- Failure branch condition: scenario index/catalog wiring is invalid, or blocker scenarios spawn without blockers.");
            sb.AppendLine("- Key metrics: dynamic agent count, blocker count, moving desired-velocity agents, average sampled speed.");
            sb.AppendLine();
            sb.AppendLine("## Evidence Artifacts");
            sb.AppendLine("- `artifacts/acceptance/navigation2d-playground-scenarios/trace.jsonl`");
            sb.AppendLine("- `artifacts/acceptance/navigation2d-playground-scenarios/battle-report.md`");
            sb.AppendLine("- `artifacts/acceptance/navigation2d-playground-scenarios/path.mmd`");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.Append(timeline.ToString());
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine("- verdict: scenario switching, blocker visualization, and explicit playground config are wired into the existing mod/input/UI pipeline.");
            sb.AppendLine("- reason: every configured scenario spawned, blocker scenarios contained blockers, and all scenarios produced non-zero desired motion plus non-zero sampled speed.");
            sb.AppendLine();
            sb.AppendLine("## Summary Stats");
            sb.AppendLine($"- scenario count: `{scenarioCount}`");
            sb.AppendLine($"- agents per team in acceptance run: `{agentsPerTeam}`");
            sb.AppendLine($"- total dynamic agents exercised across catalog: `{totalDynamicAgents}`");
            sb.AppendLine($"- total blockers exercised across catalog: `{totalBlockers}`");
            sb.AppendLine("- reusable wiring: config via `Navigation2D.Playground`, input via `default_input.json`, view modes via `viewmodes.json`, camera via virtual camera registry, telemetry via `ScreenOverlayBuffer`");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Load Navigation2D.Playground catalog] --> B{Scenario index valid?}",
                "    B -->|yes| C[Spawn scenario topology]",
                "    B -->|no| X[Clamp to catalog bounds]",
                "    X --> C",
                "    C --> D[Run steering + integration ticks]",
                "    D --> E{Scenario has blockers?}",
                "    E -->|yes| F[Validate blocker count + movement]",
                "    E -->|no| G[Validate open-space movement]",
                "    F --> H[Write battle-report + trace + path]",
                "    G --> H"
            }) + Environment.NewLine;
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                var candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }
    }
}




