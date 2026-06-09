namespace MassNavigationMod.Runtime;

public sealed class MassNavigationCrowdSemantics
{
    public MassNavigationObstacleSemantics Obstacle { get; set; } = new();
    public MassNavigationTargetProjectionSemantics TargetProjection { get; set; } = new();
    public MassNavigationGroupSemantics Group { get; set; } = new();
    public MassNavigationSteeringSemantics Steering { get; set; } = new();

    public void Validate()
    {
        if (Obstacle == null)
        {
            throw new InvalidOperationException("Mass-nav semantics requires an explicit obstacle section.");
        }

        if (TargetProjection == null)
        {
            throw new InvalidOperationException("Mass-nav semantics requires an explicit targetProjection section.");
        }

        if (Group == null)
        {
            throw new InvalidOperationException("Mass-nav semantics requires an explicit group section.");
        }

        if (Steering == null)
        {
            throw new InvalidOperationException("Mass-nav semantics requires an explicit steering section.");
        }

        Group.Validate();
        Steering.Validate();
    }
}

public sealed class MassNavigationObstacleSemantics
{
    public float AgentBodyRadiusCm { get; set; } = 20f;
    public float HardResolveCandidateDistanceCm { get; set; } = 100f;
    public float SoftPushPaddingCm { get; set; } = 350f;
    public float SoftPushForceScale { get; set; } = 8f;

    public float AgentBodyDiameterCm => AgentBodyRadiusCm * 2f;
    public float AgentBodyDiameterSq => AgentBodyDiameterCm * AgentBodyDiameterCm;
    public float ResolveHardBlockRadiusCm(float obstacleRadiusCm) => obstacleRadiusCm + AgentBodyRadiusCm;
    public float ResolveSoftPushRadiusCm(float obstacleRadiusCm) => obstacleRadiusCm + SoftPushPaddingCm;
}

public sealed class MassNavigationTargetProjectionSemantics
{
    public float TeamTargetClearanceCm { get; set; } = 60f;
    public float GroupCenterClearanceCm { get; set; } = 60f;
    public float TeamSlotClearanceCm { get; set; } = 45f;
    public float GroupSlotClearanceCm { get; set; } = 50f;
    public float LooseTargetClearanceCm { get; set; } = 50f;
}

public sealed class MassNavigationGroupSemantics
{
    public float SpawnSpacingCm { get; set; } = 46f;
    public float TeamSlotSpacingCm { get; set; } = 90f;
    public int TargetRefreshBudgetPerUpdate { get; set; } = 192;
    public float PullDeadZoneCm { get; set; } = 50f;
    public float PullClampCm { get; set; } = 2_000f;
    public float ArrivedRadiusCm { get; set; } = 150f;
    public float FormationArriveThresholdCm { get; set; } = 200f;
    public float LooseArriveThresholdCm { get; set; } = 300f;
    public float UnitTargetStopThresholdCm { get; set; } = 50f;
    public float FormationFlowSlowRadiusCm { get; set; } = 400f;
    public float NearSlotBlend { get; set; } = 0.82f;
    public float FarSlotBlend { get; set; } = 0.38f;
    public float NearSlotBlendDistanceSq { get; set; } = 4_000_000f;

    public void Validate()
    {
        if (TargetRefreshBudgetPerUpdate <= 0)
        {
            throw new InvalidOperationException("Mass-nav group semantics requires TargetRefreshBudgetPerUpdate > 0.");
        }
    }
}

public sealed class MassNavigationSteeringSemantics
{
    public float SpeedCmPerSecond { get; set; } = 800f;
    public float SeparationRadiusCm { get; set; } = 200f;
    public int MaxSeparationNeighborsPerUnit { get; set; } = 24;
    public float GoalArrivalRadiusCm { get; set; } = 1_200f;
    public float FlowObstacleAvoidanceScale { get; set; } = 1.2f;
    public float FormationSeparationScale { get; set; } = 2f;
    public float LooseSeparationScale { get; set; } = 4f;
    public float VelocityBlendPerSecond { get; set; } = 5f;

    public void Validate()
    {
        if (MaxSeparationNeighborsPerUnit <= 0)
        {
            throw new InvalidOperationException("Mass-nav steering semantics requires MaxSeparationNeighborsPerUnit > 0.");
        }
    }
}

