using System.Text.Json.Serialization;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationCrowdSemantics
{
    [JsonRequired] public MassNavigationObstacleSemantics Obstacle { get; set; } = new();
    [JsonRequired] public MassNavigationTargetProjectionSemantics TargetProjection { get; set; } = new();
    [JsonRequired] public MassNavigationGroupSemantics Group { get; set; } = new();
    [JsonRequired] public MassNavigationSteeringSemantics Steering { get; set; } = new();
    [JsonRequired] public MassNavigationSolverSemantics Solver { get; set; } = new();

    public void CopyFrom(MassNavigationCrowdSemantics source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        Obstacle.CopyFrom(source.Obstacle);
        TargetProjection.CopyFrom(source.TargetProjection);
        Group.CopyFrom(source.Group);
        Steering.CopyFrom(source.Steering);
        Solver.CopyFrom(source.Solver);
    }

    public void Validate()
    {
        if (Obstacle == null ||
            TargetProjection == null ||
            Group == null ||
            Steering == null ||
            Solver == null)
        {
            throw new System.InvalidOperationException("MassNavigation semantics requires obstacle, targetProjection, group, steering, and solver sections.");
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
    [JsonRequired] public float HardResolveCandidateDistanceCm { get; set; }
    [JsonRequired] public float SoftPushPaddingCm { get; set; }
    [JsonRequired] public float SoftPushForceScale { get; set; }

    public float ResolveSoftPushRadiusCm(float obstacleRadiusCm) => obstacleRadiusCm + SoftPushPaddingCm;

    public void CopyFrom(MassNavigationObstacleSemantics source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        HardResolveCandidateDistanceCm = source.HardResolveCandidateDistanceCm;
        SoftPushPaddingCm = source.SoftPushPaddingCm;
        SoftPushForceScale = source.SoftPushForceScale;
    }

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
            throw new System.InvalidOperationException($"MassNavigation obstacle semantics requires {name} > 0.");
        }
    }
}

public sealed class MassNavigationTargetProjectionSemantics
{
    [JsonRequired] public float TeamTargetClearanceCm { get; set; }
    [JsonRequired] public float GroupCenterClearanceCm { get; set; }
    [JsonRequired] public float TeamSlotClearanceCm { get; set; }
    [JsonRequired] public float GroupSlotClearanceCm { get; set; }
    [JsonRequired] public float LooseTargetClearanceCm { get; set; }

    public void CopyFrom(MassNavigationTargetProjectionSemantics source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        TeamTargetClearanceCm = source.TeamTargetClearanceCm;
        GroupCenterClearanceCm = source.GroupCenterClearanceCm;
        TeamSlotClearanceCm = source.TeamSlotClearanceCm;
        GroupSlotClearanceCm = source.GroupSlotClearanceCm;
        LooseTargetClearanceCm = source.LooseTargetClearanceCm;
    }

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
            throw new System.InvalidOperationException($"MassNavigation target projection semantics requires {name} >= 0.");
        }
    }
}

public sealed class MassNavigationGroupSemantics
{
    [JsonRequired] public float SpawnSpacingCm { get; set; }
    [JsonRequired] public float SpawnJitterCm { get; set; }
    [JsonRequired] public float TeamSlotSpacingCm { get; set; }
    [JsonRequired] public float FormationLineSpacingCm { get; set; }
    [JsonRequired] public float FormationSquareSpacingCm { get; set; }
    [JsonRequired] public float FormationCircleSpacingCm { get; set; }
    [JsonRequired] public float FormationCircleMinRadiusCm { get; set; }
    [JsonRequired] public float FormationWedgeSpacingCm { get; set; }
    [JsonRequired] public float FormationRotationEpsilonRadians { get; set; }
    [JsonRequired] public float PullDeadZoneCm { get; set; }
    [JsonRequired] public float PullClampCm { get; set; }
    [JsonRequired] public float ArrivedRadiusCm { get; set; }
    [JsonRequired] public float FormationArriveThresholdCm { get; set; }
    [JsonRequired] public float LooseArriveThresholdCm { get; set; }
    [JsonRequired] public float UnitTargetStopThresholdCm { get; set; }
    [JsonRequired] public float FormationFlowSlowRadiusCm { get; set; }
    [JsonRequired] public float NearSlotBlend { get; set; }
    [JsonRequired] public float FarSlotBlend { get; set; }
    [JsonRequired] public float NearSlotBlendDistanceSq { get; set; }

    public void CopyFrom(MassNavigationGroupSemantics source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        SpawnSpacingCm = source.SpawnSpacingCm;
        SpawnJitterCm = source.SpawnJitterCm;
        TeamSlotSpacingCm = source.TeamSlotSpacingCm;
        FormationLineSpacingCm = source.FormationLineSpacingCm;
        FormationSquareSpacingCm = source.FormationSquareSpacingCm;
        FormationCircleSpacingCm = source.FormationCircleSpacingCm;
        FormationCircleMinRadiusCm = source.FormationCircleMinRadiusCm;
        FormationWedgeSpacingCm = source.FormationWedgeSpacingCm;
        FormationRotationEpsilonRadians = source.FormationRotationEpsilonRadians;
        PullDeadZoneCm = source.PullDeadZoneCm;
        PullClampCm = source.PullClampCm;
        ArrivedRadiusCm = source.ArrivedRadiusCm;
        FormationArriveThresholdCm = source.FormationArriveThresholdCm;
        LooseArriveThresholdCm = source.LooseArriveThresholdCm;
        UnitTargetStopThresholdCm = source.UnitTargetStopThresholdCm;
        FormationFlowSlowRadiusCm = source.FormationFlowSlowRadiusCm;
        NearSlotBlend = source.NearSlotBlend;
        FarSlotBlend = source.FarSlotBlend;
        NearSlotBlendDistanceSq = source.NearSlotBlendDistanceSq;
    }

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
            throw new System.InvalidOperationException($"MassNavigation group semantics requires {name} > 0.");
        }
    }

    private static void RequireNonNegative(float value, string name)
    {
        if (!(value >= 0f))
        {
            throw new System.InvalidOperationException($"MassNavigation group semantics requires {name} >= 0.");
        }
    }

    private static void RequireBlend(float value, string name)
    {
        if (!(value >= 0f) || !(value <= 1f))
        {
            throw new System.InvalidOperationException($"MassNavigation group semantics requires {name} inside [0, 1].");
        }
    }
}

public sealed class MassNavigationSteeringSemantics
{
    [JsonRequired] public float SeparationRadiusCm { get; set; }
    [JsonRequired] public float GoalArrivalRadiusCm { get; set; }
    [JsonRequired] public float FlowObstacleAvoidanceScale { get; set; }
    [JsonRequired] public float FormationSeparationScale { get; set; }
    [JsonRequired] public float LooseSeparationScale { get; set; }
    [JsonRequired] public float VelocityBlendPerSecond { get; set; }

    public void CopyFrom(MassNavigationSteeringSemantics source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        SeparationRadiusCm = source.SeparationRadiusCm;
        GoalArrivalRadiusCm = source.GoalArrivalRadiusCm;
        FlowObstacleAvoidanceScale = source.FlowObstacleAvoidanceScale;
        FormationSeparationScale = source.FormationSeparationScale;
        LooseSeparationScale = source.LooseSeparationScale;
        VelocityBlendPerSecond = source.VelocityBlendPerSecond;
    }

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
            throw new System.InvalidOperationException($"MassNavigation steering semantics requires {name} > 0.");
        }
    }

    private static void RequireNonNegative(float value, string name)
    {
        if (!(value >= 0f))
        {
            throw new System.InvalidOperationException($"MassNavigation steering semantics requires {name} >= 0.");
        }
    }
}

public sealed class MassNavigationSolverSemantics
{
    [JsonRequired] public float MinNavMass { get; set; }
    [JsonRequired] public float MinVisualScale { get; set; }
    [JsonRequired] public float MaxStepDtSeconds { get; set; }
    [JsonRequired] public int ParallelStepMinAgents { get; set; }
    [JsonRequired] public float DirectionEpsilonSq { get; set; }
    [JsonRequired] public float NormalizationEpsilonSq { get; set; }
    [JsonRequired] public float InverseSqrtMinValue { get; set; }
    [JsonRequired] public float EntitySyncPositionEpsilonSq { get; set; }
    [JsonRequired] public float EntitySyncVelocityEpsilonSq { get; set; }
    [JsonRequired] public float FacingVelocityEpsilonSq { get; set; }
    [JsonRequired] public float FlowBlockedCellCost { get; set; }
    [JsonRequired] public float FlowBlockedCellThreshold { get; set; }
    [JsonRequired] public float FlowTargetStopDistanceSq { get; set; }
    [JsonRequired] public int FlowObstacleNeighborRadiusCells { get; set; }
    [JsonRequired] public float FlowObstacleNeighborWeight { get; set; }
    [JsonRequired] public float FlowObstacleAvoidanceWeight { get; set; }
    [JsonRequired] public float CrowdStampCenterCost { get; set; }
    [JsonRequired] public float CrowdStampNeighborCost { get; set; }
    [JsonRequired] public int CoincidentPairHashBucketCount { get; set; }
    [JsonRequired] public int CoincidentPairHashPrimeA { get; set; }
    [JsonRequired] public int CoincidentPairHashPrimeB { get; set; }

    public void CopyFrom(MassNavigationSolverSemantics source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        MinNavMass = source.MinNavMass;
        MinVisualScale = source.MinVisualScale;
        MaxStepDtSeconds = source.MaxStepDtSeconds;
        ParallelStepMinAgents = source.ParallelStepMinAgents;
        DirectionEpsilonSq = source.DirectionEpsilonSq;
        NormalizationEpsilonSq = source.NormalizationEpsilonSq;
        InverseSqrtMinValue = source.InverseSqrtMinValue;
        EntitySyncPositionEpsilonSq = source.EntitySyncPositionEpsilonSq;
        EntitySyncVelocityEpsilonSq = source.EntitySyncVelocityEpsilonSq;
        FacingVelocityEpsilonSq = source.FacingVelocityEpsilonSq;
        FlowBlockedCellCost = source.FlowBlockedCellCost;
        FlowBlockedCellThreshold = source.FlowBlockedCellThreshold;
        FlowTargetStopDistanceSq = source.FlowTargetStopDistanceSq;
        FlowObstacleNeighborRadiusCells = source.FlowObstacleNeighborRadiusCells;
        FlowObstacleNeighborWeight = source.FlowObstacleNeighborWeight;
        FlowObstacleAvoidanceWeight = source.FlowObstacleAvoidanceWeight;
        CrowdStampCenterCost = source.CrowdStampCenterCost;
        CrowdStampNeighborCost = source.CrowdStampNeighborCost;
        CoincidentPairHashBucketCount = source.CoincidentPairHashBucketCount;
        CoincidentPairHashPrimeA = source.CoincidentPairHashPrimeA;
        CoincidentPairHashPrimeB = source.CoincidentPairHashPrimeB;
    }

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
            throw new System.InvalidOperationException("MassNavigation solver semantics requires FlowBlockedCellCost > FlowBlockedCellThreshold.");
        }

        RequirePositive(FlowTargetStopDistanceSq, nameof(FlowTargetStopDistanceSq));
        RequireNonNegative(FlowObstacleNeighborRadiusCells, nameof(FlowObstacleNeighborRadiusCells));
        RequireNonNegative(FlowObstacleNeighborWeight, nameof(FlowObstacleNeighborWeight));
        RequireNonNegative(FlowObstacleAvoidanceWeight, nameof(FlowObstacleAvoidanceWeight));
        RequirePositive(CrowdStampCenterCost, nameof(CrowdStampCenterCost));
        RequirePositive(CrowdStampNeighborCost, nameof(CrowdStampNeighborCost));
        RequirePowerOfTwo(CoincidentPairHashBucketCount, nameof(CoincidentPairHashBucketCount));
        RequirePositive(CoincidentPairHashPrimeA, nameof(CoincidentPairHashPrimeA));
        RequirePositive(CoincidentPairHashPrimeB, nameof(CoincidentPairHashPrimeB));
    }

    private static void RequirePositive(float value, string name)
    {
        if (!(value > 0f))
        {
            throw new System.InvalidOperationException($"MassNavigation solver semantics requires {name} > 0.");
        }
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new System.InvalidOperationException($"MassNavigation solver semantics requires {name} > 0.");
        }
    }

    private static void RequireNonNegative(float value, string name)
    {
        if (!(value >= 0f))
        {
            throw new System.InvalidOperationException($"MassNavigation solver semantics requires {name} >= 0.");
        }
    }

    private static void RequireNonNegative(int value, string name)
    {
        if (value < 0)
        {
            throw new System.InvalidOperationException($"MassNavigation solver semantics requires {name} >= 0.");
        }
    }

    private static void RequirePowerOfTwo(int value, string name)
    {
        if (value <= 0 || (value & (value - 1)) != 0)
        {
            throw new System.InvalidOperationException($"MassNavigation solver semantics requires {name} to be a positive power of two.");
        }
    }
}
