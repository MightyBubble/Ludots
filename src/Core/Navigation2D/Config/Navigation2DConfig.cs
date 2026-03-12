using System;
using System.Collections.Generic;

namespace Ludots.Core.Navigation2D.Config
{
    public enum Navigation2DAvoidanceMode
    {
        Orca = 0,
        Sonar = 1,
        Hybrid = 2,
    }

    public enum Navigation2DSpatialUpdateMode
    {
        Incremental = 0,
        RebuildOnAnyCellMigration = 1,
        Adaptive = 2,
    }

    public enum Navigation2DPlaygroundScenarioKind
    {
        PassThrough = 0,
        OrthogonalCross = 1,
        Bottleneck = 2,
        LaneMerge = 3,
        CircleSwap = 4,
        GoalQueue = 5,
    }

    public sealed class Navigation2DQueryBudgetConfig
    {
        public int MaxNeighborsPerAgent { get; set; } = 8;
        public int MaxCandidateChecksPerAgent { get; set; } = 32;
    }

    public sealed class Navigation2DOrcaConfig
    {
        public bool Enabled { get; set; } = true;
        public bool FallbackToPreferredVelocity { get; set; } = true;
    }

    public sealed class Navigation2DSonarConfig
    {
        public bool Enabled { get; set; } = true;
        public int MaxSteerAngleDeg { get; set; } = 280;
        public int BackwardPenaltyAngleDeg { get; set; } = 230;
        public bool IgnoreBehindMovingAgents { get; set; } = true;
        public bool BlockedStop { get; set; } = false;
        public float PredictionTimeScale { get; set; } = 0.9f;
    }

    public sealed class Navigation2DHybridAvoidanceConfig
    {
        public bool Enabled { get; set; } = true;
        public int DenseNeighborThreshold { get; set; } = 6;
        public int MinSpeedForOrcaCmPerSec { get; set; } = 120;
        public int MinOpposingNeighborsForOrca { get; set; } = 1;
        public float OpposingVelocityDotThreshold { get; set; } = -0.25f;
    }

    public sealed class Navigation2DSmartStopConfig
    {
        public bool Enabled { get; set; } = true;
        public int QueryRadiusCm { get; set; } = 100;
        public int MaxNeighbors { get; set; } = 8;
        public int SelfGoalDistanceLimitCm { get; set; } = 160;
        public int GoalToleranceCm { get; set; } = 80;
        public int ArrivedSlackCm { get; set; } = 20;
        public int StoppedSpeedThresholdCmPerSec { get; set; } = 5;
    }

    public sealed class Navigation2DSeparationConfig
    {
        public bool Enabled { get; set; } = true;
        public int RadiusCm { get; set; } = 120;
        public float Weight { get; set; } = 0.75f;
    }

    public sealed class Navigation2DSteeringTemporalCoherenceConfig
    {
        public bool Enabled { get; set; } = false;
        public bool RequireSteadyStateWorld { get; set; } = true;
        public int MaxReuseTicks { get; set; } = 12;
        public int PositionToleranceCm { get; set; } = 40;
        public int VelocityToleranceCmPerSec { get; set; } = 320;
        public int PreferredVelocityToleranceCmPerSec { get; set; } = 80;
        public int NeighborPositionQuantizationCm { get; set; } = 40;
        public int NeighborVelocityQuantizationCmPerSec { get; set; } = 320;
    }

    public sealed class Navigation2DSpatialPartitionConfig
    {
        public Navigation2DSpatialUpdateMode UpdateMode { get; set; } = Navigation2DSpatialUpdateMode.Adaptive;
        public int RebuildCellMigrationsThreshold { get; set; } = 128;
        public int RebuildAccumulatedCellMigrationsThreshold { get; set; } = 1024;
    }

    public sealed class Navigation2DFlowStreamingConfig
    {
        public bool Enabled { get; set; } = true;
        public int ActivationRadiusTiles { get; set; } = 2;
        public int MaxActiveTilesPerFlow { get; set; } = 256;
        public int UnloadGraceTicks { get; set; } = 8;
        public float MaxPotentialCells { get; set; } = 300f;
        public int MaxActivationWindowWidthTiles { get; set; } = 0;
        public int MaxActivationWindowHeightTiles { get; set; } = 0;
        public bool WorldBoundsEnabled { get; set; } = false;
        public int WorldMinTileX { get; set; } = -512;
        public int WorldMinTileY { get; set; } = -512;
        public int WorldMaxTileX { get; set; } = 511;
        public int WorldMaxTileY { get; set; } = 511;
    }

    public sealed class Navigation2DFlowCrowdDensityConfig
    {
        public float Min { get; set; } = 0.32f;
        public float Max { get; set; } = 1.6f;
        public float Exponent { get; set; } = 0.3f;
    }

    public sealed class Navigation2DFlowCrowdSpeedConfig
    {
        public float MinFactor { get; set; } = 0.2f;
        public float MaxFactor { get; set; } = 1f;
        public float FlowVelocityScaleCmPerSec { get; set; } = 900f;
    }

    public sealed class Navigation2DFlowCrowdCostConfig
    {
        public float DistanceWeight { get; set; } = 0.2f;
        public float TimeWeight { get; set; } = 0.8f;
        public float DiscomfortWeight { get; set; } = 1.25f;
        public bool NormalizeDistanceAndTimeWeights { get; set; } = true;
    }

    public sealed class Navigation2DFlowCrowdDiscomfortConfig
    {
        public bool Enabled { get; set; } = true;
        public int ObstacleHaloRadiusCm { get; set; } = 240;
        public float ObstacleHaloValue { get; set; } = 1.25f;
        public float ObstacleHaloEdgeValue { get; set; } = 0.15f;
    }

    public sealed class Navigation2DFlowCrowdConfig
    {
        public bool Enabled { get; set; } = true;
        public Navigation2DFlowCrowdDensityConfig Density { get; set; } = new();
        public Navigation2DFlowCrowdSpeedConfig Speed { get; set; } = new();
        public Navigation2DFlowCrowdCostConfig Cost { get; set; } = new();
        public Navigation2DFlowCrowdDiscomfortConfig Discomfort { get; set; } = new();
    }

    public sealed class Navigation2DPlaygroundScenarioConfig
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public Navigation2DPlaygroundScenarioKind Kind { get; set; } = Navigation2DPlaygroundScenarioKind.PassThrough;
        public int TeamCount { get; set; } = 2;
        public int FormationSpacingCm { get; set; } = 120;
        public int StartOffsetCm { get; set; } = 9000;
        public int GoalOffsetCm { get; set; } = 9000;
        public int LaneOffsetCm { get; set; } = 2200;
        public int CorridorHalfWidthCm { get; set; } = 450;
        public int GoalRadiusCm { get; set; } = 120;
        public int BlockerRadiusCm { get; set; } = 140;
        public int BlockerCount { get; set; } = 0;
        public int BlockerSpacingCm { get; set; } = 260;
        public int RingRadiusCm { get; set; } = 5200;
    }

    public sealed class Navigation2DPlaygroundConfig
    {
        public int DefaultAgentsPerTeam { get; set; } = 5000;
        public int AgentsPerTeamStep { get; set; } = 500;
        public int DefaultScenarioIndex { get; set; } = 0;
        public int DefaultSpawnBatch { get; set; } = 128;
        public int SpawnBatchStep { get; set; } = 64;
        public int CommandGoalRadiusCm { get; set; } = 120;
        public int CommandFormationSpacingCm { get; set; } = 140;
        public int DynamicSpawnSpacingCm { get; set; } = 120;
        public int DynamicBlockerRadiusCm { get; set; } = 140;
        public List<Navigation2DPlaygroundScenarioConfig> Scenarios { get; set; } = new();
    }

    public sealed class Navigation2DSteeringConfig
    {
        public Navigation2DAvoidanceMode Mode { get; set; } = Navigation2DAvoidanceMode.Hybrid;
        public Navigation2DQueryBudgetConfig QueryBudget { get; set; } = new();
        public Navigation2DOrcaConfig Orca { get; set; } = new();
        public Navigation2DSonarConfig Sonar { get; set; } = new();
        public Navigation2DHybridAvoidanceConfig Hybrid { get; set; } = new();
        public Navigation2DSmartStopConfig SmartStop { get; set; } = new();
        public Navigation2DSeparationConfig Separation { get; set; } = new();
        public Navigation2DSteeringTemporalCoherenceConfig TemporalCoherence { get; set; } = new();
    }

    public sealed class Navigation2DConfig
    {
        public bool Enabled { get; set; } = false;
        public int MaxAgents { get; set; } = 50000;
        public int FlowIterationsPerTick { get; set; } = 4096;
        public Navigation2DSteeringConfig Steering { get; set; } = new();
        public Navigation2DSpatialPartitionConfig Spatial { get; set; } = new();
        public Navigation2DPlaygroundConfig Playground { get; set; } = new();
        public Navigation2DFlowStreamingConfig FlowStreaming { get; set; } = new();
        public Navigation2DFlowCrowdConfig FlowCrowd { get; set; } = new();

        public Navigation2DConfig CloneValidated()
        {
            var steering = Steering;
            var spatial = Spatial;
            var playground = Playground;
            var flowStreaming = FlowStreaming;
            var flowCrowd = FlowCrowd;

            return new Navigation2DConfig
            {
                Enabled = Enabled,
                MaxAgents = MaxAgents < 1 ? 1 : MaxAgents,
                FlowIterationsPerTick = FlowIterationsPerTick < 0 ? 0 : FlowIterationsPerTick,
                Steering = new Navigation2DSteeringConfig
                {
                    Mode = steering?.Mode ?? Navigation2DAvoidanceMode.Hybrid,
                    QueryBudget = new Navigation2DQueryBudgetConfig
                    {
                        MaxNeighborsPerAgent = ClampAtLeast(steering?.QueryBudget?.MaxNeighborsPerAgent ?? 8, 0),
                        MaxCandidateChecksPerAgent = ClampAtLeast(steering?.QueryBudget?.MaxCandidateChecksPerAgent ?? 32, 0),
                    },
                    Orca = new Navigation2DOrcaConfig
                    {
                        Enabled = steering?.Orca?.Enabled ?? true,
                        FallbackToPreferredVelocity = steering?.Orca?.FallbackToPreferredVelocity ?? true,
                    },
                    Sonar = new Navigation2DSonarConfig
                    {
                        Enabled = steering?.Sonar?.Enabled ?? true,
                        MaxSteerAngleDeg = ClampRange(steering?.Sonar?.MaxSteerAngleDeg ?? 280, 1, 360),
                        BackwardPenaltyAngleDeg = ClampRange(steering?.Sonar?.BackwardPenaltyAngleDeg ?? 230, 0, 360),
                        IgnoreBehindMovingAgents = steering?.Sonar?.IgnoreBehindMovingAgents ?? true,
                        BlockedStop = steering?.Sonar?.BlockedStop ?? false,
                        PredictionTimeScale = ClampAtLeast(steering?.Sonar?.PredictionTimeScale ?? 0.9f, 0f),
                    },
                    Hybrid = new Navigation2DHybridAvoidanceConfig
                    {
                        Enabled = steering?.Hybrid?.Enabled ?? true,
                        DenseNeighborThreshold = ClampAtLeast(steering?.Hybrid?.DenseNeighborThreshold ?? 6, 1),
                        MinSpeedForOrcaCmPerSec = ClampAtLeast(steering?.Hybrid?.MinSpeedForOrcaCmPerSec ?? 120, 0),
                        MinOpposingNeighborsForOrca = ClampAtLeast(steering?.Hybrid?.MinOpposingNeighborsForOrca ?? 1, 1),
                        OpposingVelocityDotThreshold = ClampRange(steering?.Hybrid?.OpposingVelocityDotThreshold ?? -0.25f, -1f, 1f),
                    },
                    SmartStop = new Navigation2DSmartStopConfig
                    {
                        Enabled = steering?.SmartStop?.Enabled ?? true,
                        QueryRadiusCm = ClampAtLeast(steering?.SmartStop?.QueryRadiusCm ?? 100, 0),
                        MaxNeighbors = ClampAtLeast(steering?.SmartStop?.MaxNeighbors ?? 8, 0),
                        SelfGoalDistanceLimitCm = ClampAtLeast(steering?.SmartStop?.SelfGoalDistanceLimitCm ?? 160, 0),
                        GoalToleranceCm = ClampAtLeast(steering?.SmartStop?.GoalToleranceCm ?? 80, 0),
                        ArrivedSlackCm = ClampAtLeast(steering?.SmartStop?.ArrivedSlackCm ?? 20, 0),
                        StoppedSpeedThresholdCmPerSec = ClampAtLeast(steering?.SmartStop?.StoppedSpeedThresholdCmPerSec ?? 5, 0),
                    },
                    Separation = new Navigation2DSeparationConfig
                    {
                        Enabled = steering?.Separation?.Enabled ?? true,
                        RadiusCm = ClampAtLeast(steering?.Separation?.RadiusCm ?? 120, 0),
                        Weight = ClampAtLeast(steering?.Separation?.Weight ?? 0.75f, 0f),
                    },
                    TemporalCoherence = new Navigation2DSteeringTemporalCoherenceConfig
                    {
                        Enabled = steering?.TemporalCoherence?.Enabled ?? false,
                        RequireSteadyStateWorld = steering?.TemporalCoherence?.RequireSteadyStateWorld ?? true,
                        MaxReuseTicks = ClampAtLeast(steering?.TemporalCoherence?.MaxReuseTicks ?? 12, 1),
                        PositionToleranceCm = ClampAtLeast(steering?.TemporalCoherence?.PositionToleranceCm ?? 40, 0),
                        VelocityToleranceCmPerSec = ClampAtLeast(steering?.TemporalCoherence?.VelocityToleranceCmPerSec ?? 320, 0),
                        PreferredVelocityToleranceCmPerSec = ClampAtLeast(steering?.TemporalCoherence?.PreferredVelocityToleranceCmPerSec ?? 80, 0),
                        NeighborPositionQuantizationCm = ClampAtLeast(steering?.TemporalCoherence?.NeighborPositionQuantizationCm ?? 40, 1),
                        NeighborVelocityQuantizationCmPerSec = ClampAtLeast(steering?.TemporalCoherence?.NeighborVelocityQuantizationCmPerSec ?? 320, 1),
                    },
                },
                Spatial = new Navigation2DSpatialPartitionConfig
                {
                    UpdateMode = spatial?.UpdateMode ?? Navigation2DSpatialUpdateMode.Adaptive,
                    RebuildCellMigrationsThreshold = ClampAtLeast(spatial?.RebuildCellMigrationsThreshold ?? 128, 0),
                    RebuildAccumulatedCellMigrationsThreshold = ClampAtLeast(spatial?.RebuildAccumulatedCellMigrationsThreshold ?? 1024, 0),
                },
                Playground = ClonePlaygroundValidated(playground),
                FlowStreaming = new Navigation2DFlowStreamingConfig
                {
                    Enabled = flowStreaming?.Enabled ?? true,
                    ActivationRadiusTiles = ClampAtLeast(flowStreaming?.ActivationRadiusTiles ?? 2, 0),
                    MaxActiveTilesPerFlow = ClampAtLeast(flowStreaming?.MaxActiveTilesPerFlow ?? 256, 1),
                    UnloadGraceTicks = ClampAtLeast(flowStreaming?.UnloadGraceTicks ?? 8, 0),
                    MaxPotentialCells = ClampAtLeast(flowStreaming?.MaxPotentialCells ?? 300f, 1f),
                    MaxActivationWindowWidthTiles = ClampAtLeast(flowStreaming?.MaxActivationWindowWidthTiles ?? 0, 0),
                    MaxActivationWindowHeightTiles = ClampAtLeast(flowStreaming?.MaxActivationWindowHeightTiles ?? 0, 0),
                    WorldBoundsEnabled = flowStreaming?.WorldBoundsEnabled ?? false,
                    WorldMinTileX = flowStreaming?.WorldMinTileX ?? -512,
                    WorldMinTileY = flowStreaming?.WorldMinTileY ?? -512,
                    WorldMaxTileX = Math.Max(flowStreaming?.WorldMinTileX ?? -512, flowStreaming?.WorldMaxTileX ?? 511),
                    WorldMaxTileY = Math.Max(flowStreaming?.WorldMinTileY ?? -512, flowStreaming?.WorldMaxTileY ?? 511),
                },
                FlowCrowd = new Navigation2DFlowCrowdConfig
                {
                    Enabled = flowCrowd?.Enabled ?? true,
                    Density = new Navigation2DFlowCrowdDensityConfig
                    {
                        Min = ClampAtLeast(flowCrowd?.Density?.Min ?? 0.32f, 0f),
                        Max = Math.Max(ClampAtLeast(flowCrowd?.Density?.Min ?? 0.32f, 0f), flowCrowd?.Density?.Max ?? 1.6f),
                        Exponent = ClampRange(flowCrowd?.Density?.Exponent ?? 0.3f, 0f, 1f),
                    },
                    Speed = new Navigation2DFlowCrowdSpeedConfig
                    {
                        MinFactor = ClampAtLeast(flowCrowd?.Speed?.MinFactor ?? 0.2f, 0.01f),
                        MaxFactor = Math.Max(ClampAtLeast(flowCrowd?.Speed?.MinFactor ?? 0.2f, 0.01f), flowCrowd?.Speed?.MaxFactor ?? 1f),
                        FlowVelocityScaleCmPerSec = ClampAtLeast(flowCrowd?.Speed?.FlowVelocityScaleCmPerSec ?? 900f, 1f),
                    },
                    Cost = new Navigation2DFlowCrowdCostConfig
                    {
                        DistanceWeight = ClampAtLeast(flowCrowd?.Cost?.DistanceWeight ?? 0.2f, 0f),
                        TimeWeight = ClampAtLeast(flowCrowd?.Cost?.TimeWeight ?? 0.8f, 0f),
                        DiscomfortWeight = ClampAtLeast(flowCrowd?.Cost?.DiscomfortWeight ?? 1.25f, 0f),
                        NormalizeDistanceAndTimeWeights = flowCrowd?.Cost?.NormalizeDistanceAndTimeWeights ?? true,
                    },
                    Discomfort = new Navigation2DFlowCrowdDiscomfortConfig
                    {
                        Enabled = flowCrowd?.Discomfort?.Enabled ?? true,
                        ObstacleHaloRadiusCm = ClampAtLeast(flowCrowd?.Discomfort?.ObstacleHaloRadiusCm ?? 240, 0),
                        ObstacleHaloValue = ClampAtLeast(flowCrowd?.Discomfort?.ObstacleHaloValue ?? 1.25f, 0f),
                        ObstacleHaloEdgeValue = ClampAtLeast(flowCrowd?.Discomfort?.ObstacleHaloEdgeValue ?? 0.15f, 0f),
                    },
                },
            };
        }
        private static Navigation2DPlaygroundConfig ClonePlaygroundValidated(Navigation2DPlaygroundConfig? playground)
        {
            int defaultAgentsPerTeam = ClampAtLeast(playground?.DefaultAgentsPerTeam ?? 5000, 0);
            int agentsPerTeamStep = ClampAtLeast(playground?.AgentsPerTeamStep ?? 500, 1);
            int defaultSpawnBatch = ClampAtLeast(playground?.DefaultSpawnBatch ?? 128, 1);
            int spawnBatchStep = ClampAtLeast(playground?.SpawnBatchStep ?? 64, 1);
            int commandGoalRadiusCm = ClampAtLeast(playground?.CommandGoalRadiusCm ?? 120, 0);
            int commandFormationSpacingCm = ClampAtLeast(playground?.CommandFormationSpacingCm ?? 140, 1);
            int dynamicSpawnSpacingCm = ClampAtLeast(playground?.DynamicSpawnSpacingCm ?? 120, 1);
            int dynamicBlockerRadiusCm = ClampAtLeast(playground?.DynamicBlockerRadiusCm ?? 140, 1);

            var scenarios = new List<Navigation2DPlaygroundScenarioConfig>();
            var sourceScenarios = playground?.Scenarios;
            if (sourceScenarios != null)
            {
                for (int i = 0; i < sourceScenarios.Count; i++)
                {
                    scenarios.Add(CloneScenarioValidated(sourceScenarios[i], i));
                }
            }

            if (scenarios.Count == 0)
            {
                scenarios.AddRange(CreateDefaultScenarioCatalog());
            }

            int defaultScenarioIndex = playground?.DefaultScenarioIndex ?? 0;
            if (defaultScenarioIndex < 0)
            {
                defaultScenarioIndex = 0;
            }
            if (defaultScenarioIndex >= scenarios.Count)
            {
                defaultScenarioIndex = scenarios.Count - 1;
            }

            return new Navigation2DPlaygroundConfig
            {
                DefaultAgentsPerTeam = defaultAgentsPerTeam,
                AgentsPerTeamStep = agentsPerTeamStep,
                DefaultScenarioIndex = defaultScenarioIndex,
                DefaultSpawnBatch = Math.Min(defaultSpawnBatch, Math.Max(1, defaultAgentsPerTeam)),
                SpawnBatchStep = spawnBatchStep,
                CommandGoalRadiusCm = commandGoalRadiusCm,
                CommandFormationSpacingCm = commandFormationSpacingCm,
                DynamicSpawnSpacingCm = dynamicSpawnSpacingCm,
                DynamicBlockerRadiusCm = dynamicBlockerRadiusCm,
                Scenarios = scenarios,
            };
        }

        private static Navigation2DPlaygroundScenarioConfig CloneScenarioValidated(Navigation2DPlaygroundScenarioConfig? scenario, int fallbackIndex)
        {
            var kind = scenario?.Kind ?? Navigation2DPlaygroundScenarioKind.PassThrough;
            return new Navigation2DPlaygroundScenarioConfig
            {
                Id = string.IsNullOrWhiteSpace(scenario?.Id) ? GetDefaultScenarioId(kind, fallbackIndex) : scenario.Id.Trim(),
                Name = string.IsNullOrWhiteSpace(scenario?.Name) ? kind.ToString() : scenario.Name.Trim(),
                Kind = kind,
                TeamCount = ClampAtLeast(scenario?.TeamCount ?? GetDefaultTeamCount(kind), 1),
                FormationSpacingCm = ClampAtLeast(scenario?.FormationSpacingCm ?? 120, 1),
                StartOffsetCm = ClampAtLeast(scenario?.StartOffsetCm ?? 9000, 0),
                GoalOffsetCm = ClampAtLeast(scenario?.GoalOffsetCm ?? 9000, 0),
                LaneOffsetCm = ClampAtLeast(scenario?.LaneOffsetCm ?? 2200, 0),
                CorridorHalfWidthCm = ClampAtLeast(scenario?.CorridorHalfWidthCm ?? 450, 0),
                GoalRadiusCm = ClampAtLeast(scenario?.GoalRadiusCm ?? 120, 0),
                BlockerRadiusCm = ClampAtLeast(scenario?.BlockerRadiusCm ?? 140, 0),
                BlockerCount = ClampAtLeast(scenario?.BlockerCount ?? 0, 0),
                BlockerSpacingCm = ClampAtLeast(scenario?.BlockerSpacingCm ?? 260, 1),
                RingRadiusCm = ClampAtLeast(scenario?.RingRadiusCm ?? 5200, 1),
            };
        }

        private static List<Navigation2DPlaygroundScenarioConfig> CreateDefaultScenarioCatalog()
        {
            return new List<Navigation2DPlaygroundScenarioConfig>
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
                },
            };
        }

        private static int GetDefaultTeamCount(Navigation2DPlaygroundScenarioKind kind)
        {
            return kind == Navigation2DPlaygroundScenarioKind.GoalQueue ? 1 : 2;
        }

        private static string GetDefaultScenarioId(Navigation2DPlaygroundScenarioKind kind, int fallbackIndex)
        {
            return kind switch
            {
                Navigation2DPlaygroundScenarioKind.PassThrough => "pass_through",
                Navigation2DPlaygroundScenarioKind.OrthogonalCross => "orthogonal_cross",
                Navigation2DPlaygroundScenarioKind.Bottleneck => "bottleneck",
                Navigation2DPlaygroundScenarioKind.LaneMerge => "lane_merge",
                Navigation2DPlaygroundScenarioKind.CircleSwap => "circle_swap",
                Navigation2DPlaygroundScenarioKind.GoalQueue => "goal_queue",
                _ => $"scenario_{fallbackIndex}"
            };
        }

        private static int ClampAtLeast(int value, int minValue)
        {
            return value < minValue ? minValue : value;
        }

        private static int ClampRange(int value, int minValue, int maxValue)
        {
            if (value < minValue) return minValue;
            if (value > maxValue) return maxValue;
            return value;
        }

        private static float ClampAtLeast(float value, float minValue)
        {
            return value < minValue ? minValue : value;
        }

        private static float ClampRange(float value, float minValue, float maxValue)
        {
            if (value < minValue) return minValue;
            if (value > maxValue) return maxValue;
            return value;
        }
    }
}


