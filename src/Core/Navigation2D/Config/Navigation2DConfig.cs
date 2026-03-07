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



    public sealed class Navigation2DSpatialPartitionConfig

    {

        public Navigation2DSpatialUpdateMode UpdateMode { get; set; } = Navigation2DSpatialUpdateMode.Adaptive;

        public int RebuildCellMigrationsThreshold { get; set; } = 128;

        public int RebuildAccumulatedCellMigrationsThreshold { get; set; } = 1024;

    }



    public sealed class Navigation2DSteeringConfig

    {

        public Navigation2DAvoidanceMode Mode { get; set; } = Navigation2DAvoidanceMode.Hybrid;

        public Navigation2DQueryBudgetConfig QueryBudget { get; set; } = new();

        public Navigation2DOrcaConfig Orca { get; set; } = new();

        public Navigation2DSonarConfig Sonar { get; set; } = new();

        public Navigation2DHybridAvoidanceConfig Hybrid { get; set; } = new();

        public Navigation2DSmartStopConfig SmartStop { get; set; } = new();

    }



    public sealed class Navigation2DConfig

    {

        public bool Enabled { get; set; } = false;

        public int MaxAgents { get; set; } = 50000;

        public int FlowIterationsPerTick { get; set; } = 4096;

        public Navigation2DSteeringConfig Steering { get; set; } = new();

        public Navigation2DSpatialPartitionConfig Spatial { get; set; } = new();



        public Navigation2DConfig CloneValidated()

        {

            var steering = Steering;

            var spatial = Spatial;



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

                },

                Spatial = new Navigation2DSpatialPartitionConfig

                {

                    UpdateMode = spatial?.UpdateMode ?? Navigation2DSpatialUpdateMode.Adaptive,

                    RebuildCellMigrationsThreshold = ClampAtLeast(spatial?.RebuildCellMigrationsThreshold ?? 128, 0),

                    RebuildAccumulatedCellMigrationsThreshold = ClampAtLeast(spatial?.RebuildAccumulatedCellMigrationsThreshold ?? 1024, 0),

                }

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
