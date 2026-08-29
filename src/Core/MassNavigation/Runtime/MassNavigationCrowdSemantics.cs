namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationCrowdSemantics
{
    public MassNavigationObstacleSemantics Obstacle { get; set; } = new();
    public MassNavigationTargetProjectionSemantics TargetProjection { get; set; } = new();
    public MassNavigationGroupSemantics Group { get; set; } = new();
    public MassNavigationRouteSemantics Route { get; set; } = new();
    public MassNavigationSteeringSemantics Steering { get; set; } = new();
    public MassNavigationSolverSemantics Solver { get; set; } = new();

    public void CopyFrom(MassNavigationCrowdSemantics source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        Obstacle.CopyFrom(source.Obstacle);
        TargetProjection.CopyFrom(source.TargetProjection);
        Group.CopyFrom(source.Group);
        Route.CopyFrom(source.Route);
        Steering.CopyFrom(source.Steering);
        Solver.CopyFrom(source.Solver);
    }

    public void Validate()
    {
        if (Obstacle == null ||
            TargetProjection == null ||
            Group == null ||
            Route == null ||
            Steering == null ||
            Solver == null)
        {
            throw new System.InvalidOperationException("MassNavigation semantics requires obstacle, targetProjection, group, route, steering, and solver sections.");
        }

        Obstacle.Validate();
        TargetProjection.Validate();
        Group.Validate();
        Route.Validate();
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
    public float PullDeadZoneCm { get; set; }
    public float PullClampCm { get; set; }
    public float ArrivedRadiusCm { get; set; }
    public float GroupedAgentArriveThresholdCm { get; set; }
    public float LooseArriveThresholdCm { get; set; }
    public float UnitTargetStopThresholdCm { get; set; }
    public float GroupedAgentFlowSlowRadiusCm { get; set; }
    public float NearSlotBlend { get; set; }
    public float FarSlotBlend { get; set; }
    public float NearSlotBlendDistanceSq { get; set; }

    public void CopyFrom(MassNavigationGroupSemantics source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        SpawnSpacingCm = source.SpawnSpacingCm;
        SpawnJitterCm = source.SpawnJitterCm;
        TeamSlotSpacingCm = source.TeamSlotSpacingCm;
        PullDeadZoneCm = source.PullDeadZoneCm;
        PullClampCm = source.PullClampCm;
        ArrivedRadiusCm = source.ArrivedRadiusCm;
        GroupedAgentArriveThresholdCm = source.GroupedAgentArriveThresholdCm;
        LooseArriveThresholdCm = source.LooseArriveThresholdCm;
        UnitTargetStopThresholdCm = source.UnitTargetStopThresholdCm;
        GroupedAgentFlowSlowRadiusCm = source.GroupedAgentFlowSlowRadiusCm;
        NearSlotBlend = source.NearSlotBlend;
        FarSlotBlend = source.FarSlotBlend;
        NearSlotBlendDistanceSq = source.NearSlotBlendDistanceSq;
    }

    public void Validate()
    {
        RequirePositive(SpawnSpacingCm, nameof(SpawnSpacingCm));
        RequireNonNegative(SpawnJitterCm, nameof(SpawnJitterCm));
        RequirePositive(TeamSlotSpacingCm, nameof(TeamSlotSpacingCm));
        RequireNonNegative(PullDeadZoneCm, nameof(PullDeadZoneCm));
        RequirePositive(PullClampCm, nameof(PullClampCm));
        RequirePositive(ArrivedRadiusCm, nameof(ArrivedRadiusCm));
        RequirePositive(GroupedAgentArriveThresholdCm, nameof(GroupedAgentArriveThresholdCm));
        RequirePositive(LooseArriveThresholdCm, nameof(LooseArriveThresholdCm));
        RequirePositive(UnitTargetStopThresholdCm, nameof(UnitTargetStopThresholdCm));
        RequirePositive(GroupedAgentFlowSlowRadiusCm, nameof(GroupedAgentFlowSlowRadiusCm));
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

public sealed class MassNavigationRouteSemantics
{
    public float WaypointAdvanceStopThresholdScale { get; set; }
    public float WaypointAdvanceBodyRadiusScale { get; set; }

    public void CopyFrom(MassNavigationRouteSemantics source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        WaypointAdvanceStopThresholdScale = source.WaypointAdvanceStopThresholdScale;
        WaypointAdvanceBodyRadiusScale = source.WaypointAdvanceBodyRadiusScale;
    }

    public void Validate()
    {
        if (WaypointAdvanceStopThresholdScale < 1f)
        {
            throw new System.InvalidOperationException(
                "MassNavigation route semantics requires waypointAdvanceStopThresholdScale >= 1 so the advance circle contains the solver's unit stop circle.");
        }

        RequirePositive(WaypointAdvanceStopThresholdScale, nameof(WaypointAdvanceStopThresholdScale));
        RequirePositive(WaypointAdvanceBodyRadiusScale, nameof(WaypointAdvanceBodyRadiusScale));
    }

    private static void RequirePositive(float value, string name)
    {
        if (!(value > 0f))
        {
            throw new System.InvalidOperationException($"MassNavigation route semantics requires {name} > 0.");
        }
    }
}

public sealed class MassNavigationSteeringSemantics
{
    public float SeparationRadiusCm { get; set; }
    public float GoalArrivalRadiusCm { get; set; }
    public float FlowObstacleAvoidanceScale { get; set; }
    public float GroupedAgentSeparationScale { get; set; }
    public float LooseSeparationScale { get; set; }
    public float VelocityBlendPerSecond { get; set; }

    public void CopyFrom(MassNavigationSteeringSemantics source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        SeparationRadiusCm = source.SeparationRadiusCm;
        GoalArrivalRadiusCm = source.GoalArrivalRadiusCm;
        FlowObstacleAvoidanceScale = source.FlowObstacleAvoidanceScale;
        GroupedAgentSeparationScale = source.GroupedAgentSeparationScale;
        LooseSeparationScale = source.LooseSeparationScale;
        VelocityBlendPerSecond = source.VelocityBlendPerSecond;
    }

    public void Validate()
    {
        RequirePositive(SeparationRadiusCm, nameof(SeparationRadiusCm));
        RequirePositive(GoalArrivalRadiusCm, nameof(GoalArrivalRadiusCm));
        RequireNonNegative(FlowObstacleAvoidanceScale, nameof(FlowObstacleAvoidanceScale));
        RequireNonNegative(GroupedAgentSeparationScale, nameof(GroupedAgentSeparationScale));
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
    public float CrowdStampCenterCost { get; set; }
    public float CrowdStampNeighborCost { get; set; }
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
