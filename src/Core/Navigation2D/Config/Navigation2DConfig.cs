namespace Ludots.Core.Navigation2D.Config
{
    public enum Navigation2DAvoidanceMode
    {
        Orca = 0,
        Sonar = 1,
        Hybrid = 2,
    }

    public sealed class Navigation2DConfig
    {
        public bool Enabled { get; set; } = false;
        public int MaxAgents { get; set; } = 50000;
        public int FlowIterationsPerTick { get; set; } = 4096;
        public Navigation2DSteeringConfig Steering { get; set; } = new();
    }

    public sealed class Navigation2DSteeringConfig
    {
        public Navigation2DAvoidanceMode Mode { get; set; } = Navigation2DAvoidanceMode.Orca;
        public Navigation2DQueryBudgetConfig QueryBudget { get; set; } = new();
        public Navigation2DOrcaConfig Orca { get; set; } = new();
        public Navigation2DSonarConfig Sonar { get; set; } = new();
        public Navigation2DHybridConfig Hybrid { get; set; } = new();
        public Navigation2DSmartStopConfig SmartStop { get; set; } = new();
    }

    public sealed class Navigation2DQueryBudgetConfig
    {
        public int MaxNeighborsPerAgent { get; set; } = 16;
        public int MaxCandidateChecksPerAgent { get; set; } = 64;
    }

    public sealed class Navigation2DOrcaConfig
    {
        public bool Enabled { get; set; } = true;
        public bool FallbackToPreferredVelocity { get; set; } = true;
    }

    public sealed class Navigation2DSonarConfig
    {
        public bool Enabled { get; set; } = false;
        public float PredictionTimeScale { get; set; } = 1f;
        public bool BlockedStop { get; set; } = true;
        public bool IgnoreBehindMovingAgents { get; set; } = true;
        public float MaxSteerAngleDeg { get; set; } = 360f;
        public float BackwardPenaltyAngleDeg { get; set; } = 180f;
    }

    public sealed class Navigation2DHybridConfig
    {
        public bool Enabled { get; set; } = false;
        public int DenseNeighborThreshold { get; set; } = 6;
        public int MinOpposingNeighborsForOrca { get; set; } = 2;
    }

    public sealed class Navigation2DSmartStopConfig
    {
        public bool Enabled { get; set; } = false;
        public int MaxNeighbors { get; set; } = 6;
        public int GoalToleranceCm { get; set; } = 100;
    }
}
