using Ludots.Core.Spatial;

namespace MassNavigationMod.Runtime;

public sealed class MassNavigationCrowdSemantics
{
    public MassNavigationObstacleSemantics Obstacle { get; set; } = new();
    public MassNavigationTargetProjectionSemantics TargetProjection { get; set; } = new();
    public MassNavigationGroupSemantics Group { get; set; } = new();
    public MassNavigationSteeringSemantics Steering { get; set; } = new();
    public MassNavigationSolverSemantics Solver { get; set; } = new();

    public void Validate()
    {
        if (Obstacle == null ||
            TargetProjection == null ||
            Group == null ||
            Steering == null ||
            Solver == null)
        {
            throw new System.InvalidOperationException("Mass-nav semantics requires obstacle, targetProjection, group, steering, and solver sections.");
        }

        Obstacle.Validate();
        TargetProjection.Validate();
        Group.Validate();
        Steering.Validate();
        Solver.Validate();
    }
}

public sealed class MassNavigationObstacleSemantics
{
    public float HardResolveCandidateDistanceCm { get; set; } = SpatialScaleDefaults.CellCm;
    public float SoftPushPaddingCm { get; set; } = 350f;
    public float SoftPushForceScale { get; set; } = 8f;

    public float ResolveSoftPushRadiusCm(float obstacleRadiusCm) => obstacleRadiusCm + SoftPushPaddingCm;

    public void Validate()
    {
        RequirePositive(HardResolveCandidateDistanceCm, nameof(HardResolveCandidateDistanceCm));
        RequirePositive(SoftPushPaddingCm, nameof(SoftPushPaddingCm));
        RequirePositive(SoftPushForceScale, nameof(SoftPushForceScale));
    }

    private static void RequirePositive(float value, string name)
    {
        if (!(value > 0f))
        {
            throw new System.InvalidOperationException($"Mass-nav obstacle semantics requires {name} > 0.");
        }
    }
}

public sealed class MassNavigationTargetProjectionSemantics
{
    public float TeamTargetClearanceCm { get; set; } = 60f;
    public float GroupCenterClearanceCm { get; set; } = 60f;
    public float TeamSlotClearanceCm { get; set; } = 45f;
    public float GroupSlotClearanceCm { get; set; } = 50f;
    public float LooseTargetClearanceCm { get; set; } = 50f;

    public void Validate()
    {
        RequireNonNegative(TeamTargetClearanceCm, nameof(TeamTargetClearanceCm));
        RequireNonNegative(GroupCenterClearanceCm, nameof(GroupCenterClearanceCm));
        RequireNonNegative(TeamSlotClearanceCm, nameof(TeamSlotClearanceCm));
        RequireNonNegative(GroupSlotClearanceCm, nameof(GroupSlotClearanceCm));
        RequireNonNegative(LooseTargetClearanceCm, nameof(LooseTargetClearanceCm));
    }

    private static void RequireNonNegative(float value, string name)
    {
        if (!(value >= 0f))
        {
            throw new System.InvalidOperationException($"Mass-nav target projection semantics requires {name} >= 0.");
        }
    }
}

public sealed class MassNavigationGroupSemantics
{
    public float SpawnSpacingCm { get; set; } = 46f;
    public float SpawnJitterCm { get; set; } = 12f;
    public float TeamSlotSpacingCm { get; set; } = 90f;
    public float FormationLineSpacingCm { get; set; } = 180f;
    public float FormationSquareSpacingCm { get; set; } = 80f;
    public float FormationCircleSpacingCm { get; set; } = 180f;
    public float FormationCircleMinRadiusCm { get; set; } = 200f;
    public float FormationWedgeSpacingCm { get; set; } = 180f;
    public float FormationRotationEpsilonRadians { get; set; } = 0.00001f;
    public float FormationRotationSpeedRadiansPerSecond { get; set; } = 2.5f;
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
        RequirePositive(SpawnSpacingCm, nameof(SpawnSpacingCm));
        RequireNonNegative(SpawnJitterCm, nameof(SpawnJitterCm));
        RequirePositive(TeamSlotSpacingCm, nameof(TeamSlotSpacingCm));
        RequirePositive(FormationLineSpacingCm, nameof(FormationLineSpacingCm));
        RequirePositive(FormationSquareSpacingCm, nameof(FormationSquareSpacingCm));
        RequirePositive(FormationCircleSpacingCm, nameof(FormationCircleSpacingCm));
        RequirePositive(FormationCircleMinRadiusCm, nameof(FormationCircleMinRadiusCm));
        RequirePositive(FormationWedgeSpacingCm, nameof(FormationWedgeSpacingCm));
        RequireNonNegative(FormationRotationEpsilonRadians, nameof(FormationRotationEpsilonRadians));
        RequirePositive(FormationRotationSpeedRadiansPerSecond, nameof(FormationRotationSpeedRadiansPerSecond));
        RequireNonNegative(PullDeadZoneCm, nameof(PullDeadZoneCm));
        RequirePositive(PullClampCm, nameof(PullClampCm));
        RequirePositive(ArrivedRadiusCm, nameof(ArrivedRadiusCm));
        RequirePositive(FormationArriveThresholdCm, nameof(FormationArriveThresholdCm));
        RequirePositive(LooseArriveThresholdCm, nameof(LooseArriveThresholdCm));
        RequirePositive(UnitTargetStopThresholdCm, nameof(UnitTargetStopThresholdCm));
        RequirePositive(FormationFlowSlowRadiusCm, nameof(FormationFlowSlowRadiusCm));
        RequireBlend(NearSlotBlend, nameof(NearSlotBlend));
        RequireBlend(FarSlotBlend, nameof(FarSlotBlend));
        RequirePositive(NearSlotBlendDistanceSq, nameof(NearSlotBlendDistanceSq));
    }

    private static void RequirePositive(float value, string name)
    {
        if (!(value > 0f))
        {
            throw new System.InvalidOperationException($"Mass-nav group semantics requires {name} > 0.");
        }
    }

    private static void RequireNonNegative(float value, string name)
    {
        if (!(value >= 0f))
        {
            throw new System.InvalidOperationException($"Mass-nav group semantics requires {name} >= 0.");
        }
    }

    private static void RequireBlend(float value, string name)
    {
        if (!(value >= 0f) || !(value <= 1f))
        {
            throw new System.InvalidOperationException($"Mass-nav group semantics requires {name} inside [0, 1].");
        }
    }
}

public sealed class MassNavigationSteeringSemantics
{
    public float SeparationRadiusCm { get; set; } = 200f;
    public float GoalArrivalRadiusCm { get; set; } = 1_200f;
    public float FlowObstacleAvoidanceScale { get; set; } = 1.2f;
    public float FormationSeparationScale { get; set; } = 2f;
    public float LooseSeparationScale { get; set; } = 4f;
    public float VelocityBlendPerSecond { get; set; } = 5f;

    public void Validate()
    {
        RequirePositive(SeparationRadiusCm, nameof(SeparationRadiusCm));
        RequirePositive(GoalArrivalRadiusCm, nameof(GoalArrivalRadiusCm));
        RequireNonNegative(FlowObstacleAvoidanceScale, nameof(FlowObstacleAvoidanceScale));
        RequireNonNegative(FormationSeparationScale, nameof(FormationSeparationScale));
        RequireNonNegative(LooseSeparationScale, nameof(LooseSeparationScale));
        RequireNonNegative(VelocityBlendPerSecond, nameof(VelocityBlendPerSecond));
    }

    private static void RequirePositive(float value, string name)
    {
        if (!(value > 0f))
        {
            throw new System.InvalidOperationException($"Mass-nav steering semantics requires {name} > 0.");
        }
    }

    private static void RequireNonNegative(float value, string name)
    {
        if (!(value >= 0f))
        {
            throw new System.InvalidOperationException($"Mass-nav steering semantics requires {name} >= 0.");
        }
    }
}

public sealed class MassNavigationSolverSemantics
{
    public float MinNavMass { get; set; } = 0.001f;
    public float MinVisualScale { get; set; } = 0.01f;
    public float MaxStepDtSeconds { get; set; } = 0.05f;
    public int ParallelStepMinAgents { get; set; } = 2048;
    public float DirectionEpsilonSq { get; set; } = 0.0001f;
    public float NormalizationEpsilonSq { get; set; } = 0.000001f;
    public float InverseSqrtMinValue { get; set; } = 0.00000001f;
    public float EntitySyncPositionEpsilonSq { get; set; } = 0.25f;
    public float EntitySyncVelocityEpsilonSq { get; set; } = 0.01f;
    public float FacingVelocityEpsilonSq { get; set; } = 0.01f;
    public float FlowBlockedCellCost { get; set; } = 99_999f;
    public float FlowBlockedCellThreshold { get; set; } = 9_999f;
    public float FlowTargetStopDistanceSq { get; set; } = 1f;
    public int FlowObstacleNeighborRadiusCells { get; set; } = 4;
    public float FlowObstacleNeighborWeight { get; set; } = 5f;
    public float FlowObstacleAvoidanceWeight { get; set; } = 1.5f;
    public int CoincidentPairHashBucketCount { get; set; } = 1024;
    public int CoincidentPairHashPrimeA { get; set; } = 73_856_093;
    public int CoincidentPairHashPrimeB { get; set; } = 19_349_663;

    public void Validate()
    {
        RequirePositive(MinNavMass, nameof(MinNavMass));
        RequirePositive(MinVisualScale, nameof(MinVisualScale));
        RequirePositive(MaxStepDtSeconds, nameof(MaxStepDtSeconds));
        RequirePositive(ParallelStepMinAgents, nameof(ParallelStepMinAgents));
        RequirePositive(DirectionEpsilonSq, nameof(DirectionEpsilonSq));
        RequirePositive(NormalizationEpsilonSq, nameof(NormalizationEpsilonSq));
        RequirePositive(InverseSqrtMinValue, nameof(InverseSqrtMinValue));
        RequirePositive(EntitySyncPositionEpsilonSq, nameof(EntitySyncPositionEpsilonSq));
        RequirePositive(EntitySyncVelocityEpsilonSq, nameof(EntitySyncVelocityEpsilonSq));
        RequirePositive(FacingVelocityEpsilonSq, nameof(FacingVelocityEpsilonSq));
        RequirePositive(FlowBlockedCellCost, nameof(FlowBlockedCellCost));
        RequirePositive(FlowBlockedCellThreshold, nameof(FlowBlockedCellThreshold));
        if (FlowBlockedCellCost <= FlowBlockedCellThreshold)
        {
            throw new System.InvalidOperationException("Mass-nav solver semantics requires FlowBlockedCellCost > FlowBlockedCellThreshold.");
        }

        RequirePositive(FlowTargetStopDistanceSq, nameof(FlowTargetStopDistanceSq));
        RequireNonNegative(FlowObstacleNeighborRadiusCells, nameof(FlowObstacleNeighborRadiusCells));
        RequireNonNegative(FlowObstacleNeighborWeight, nameof(FlowObstacleNeighborWeight));
        RequireNonNegative(FlowObstacleAvoidanceWeight, nameof(FlowObstacleAvoidanceWeight));
        RequirePowerOfTwo(CoincidentPairHashBucketCount, nameof(CoincidentPairHashBucketCount));
        RequirePositive(CoincidentPairHashPrimeA, nameof(CoincidentPairHashPrimeA));
        RequirePositive(CoincidentPairHashPrimeB, nameof(CoincidentPairHashPrimeB));
    }

    private static void RequirePositive(float value, string name)
    {
        if (!(value > 0f))
        {
            throw new System.InvalidOperationException($"Mass-nav solver semantics requires {name} > 0.");
        }
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new System.InvalidOperationException($"Mass-nav solver semantics requires {name} > 0.");
        }
    }

    private static void RequireNonNegative(float value, string name)
    {
        if (!(value >= 0f))
        {
            throw new System.InvalidOperationException($"Mass-nav solver semantics requires {name} >= 0.");
        }
    }

    private static void RequireNonNegative(int value, string name)
    {
        if (value < 0)
        {
            throw new System.InvalidOperationException($"Mass-nav solver semantics requires {name} >= 0.");
        }
    }

    private static void RequirePowerOfTwo(int value, string name)
    {
        if (value <= 0 || (value & (value - 1)) != 0)
        {
            throw new System.InvalidOperationException($"Mass-nav solver semantics requires {name} to be a positive power of two.");
        }
    }
}

