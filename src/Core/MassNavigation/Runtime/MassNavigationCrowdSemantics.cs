namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationCrowdSemantics
{
    public MassNavigationObstacleSemantics Obstacle { get; set; } = new();
    public MassNavigationTargetProjectionSemantics TargetProjection { get; set; } = new();
    public MassNavigationGroupSemantics Group { get; set; } = new();
    public MassNavigationSteeringSemantics Steering { get; set; } = new();
    public MassNavigationSolverSemantics Solver { get; set; } = new();

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
    public float HardResolveCandidateDistanceCm { get; set; }
    public float SoftPushPaddingCm { get; set; }
    public float SoftPushForceScale { get; set; }

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
    public float TeamTargetClearanceCm { get; set; }
    public float GroupCenterClearanceCm { get; set; }
    public float TeamSlotClearanceCm { get; set; }
    public float GroupSlotClearanceCm { get; set; }
    public float LooseTargetClearanceCm { get; set; }

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
    public float SpawnSpacingCm { get; set; }
    public float SpawnJitterCm { get; set; }
    public float TeamSlotSpacingCm { get; set; }
    public float FormationLineSpacingCm { get; set; }
    public float FormationSquareSpacingCm { get; set; }
    public float FormationCircleSpacingCm { get; set; }
    public float FormationCircleMinRadiusCm { get; set; }
    public float FormationWedgeSpacingCm { get; set; }
    public float FormationRotationEpsilonRadians { get; set; }
    public float FormationRotationSpeedRadiansPerSecond { get; set; }
    public float PullDeadZoneCm { get; set; }
    public float PullClampCm { get; set; }
    public float ArrivedRadiusCm { get; set; }
    public float FormationArriveThresholdCm { get; set; }
    public float LooseArriveThresholdCm { get; set; }
    public float UnitTargetStopThresholdCm { get; set; }
    public float FormationFlowSlowRadiusCm { get; set; }
    public float NearSlotBlend { get; set; }
    public float FarSlotBlend { get; set; }
    public float NearSlotBlendDistanceSq { get; set; }

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
        FormationRotationSpeedRadiansPerSecond = source.FormationRotationSpeedRadiansPerSecond;
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
    public float SeparationRadiusCm { get; set; }
    public float GoalArrivalRadiusCm { get; set; }
    public float FlowObstacleAvoidanceScale { get; set; }
    public float FormationSeparationScale { get; set; }
    public float LooseSeparationScale { get; set; }
    public float VelocityBlendPerSecond { get; set; }

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
    public float MinNavMass { get; set; }
    public float MinVisualScale { get; set; }
    public float MaxStepDtSeconds { get; set; }
    public int ParallelStepMinAgents { get; set; }
    public float DirectionEpsilonSq { get; set; }
    public float NormalizationEpsilonSq { get; set; }
    public float InverseSqrtMinValue { get; set; }
    public float EntitySyncPositionEpsilonSq { get; set; }
    public float EntitySyncVelocityEpsilonSq { get; set; }
    public float FacingVelocityEpsilonSq { get; set; }
    public float FlowBlockedCellCost { get; set; }
    public float FlowBlockedCellThreshold { get; set; }
    public float FlowTargetStopDistanceSq { get; set; }
    public int FlowObstacleNeighborRadiusCells { get; set; }
    public float FlowObstacleNeighborWeight { get; set; }
    public float FlowObstacleAvoidanceWeight { get; set; }
    public int CoincidentPairHashBucketCount { get; set; }
    public int CoincidentPairHashPrimeA { get; set; }
    public int CoincidentPairHashPrimeB { get; set; }

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
