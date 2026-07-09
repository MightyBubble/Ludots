using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.MassNavigation.Runtime;

namespace CapabilityStandardTimeFlowShowcaseMod.Runtime;

internal sealed class CapabilityStandardTimeFlowShowcaseConfig
{
    public string MapId { get; set; } = string.Empty;
    public TimeFlowScaleRequestConfig[] SimulationScaleRequests { get; set; } = Array.Empty<TimeFlowScaleRequestConfig>();
    public TimeFlowScaleRequestConfig[] GasScaleRequests { get; set; } = Array.Empty<TimeFlowScaleRequestConfig>();
    public TimeFlowNavigationProbeConfig NavigationProbe { get; set; } = new();
    public MassNavigationFlowSolverConfig NavigationSolver { get; set; } = new();
    public MassNavigationRuntimeCapacityConfig NavigationRuntimeCapacity { get; set; } = new();
    public MassNavigationFlowArrivalTuning NavigationArrival { get; set; } = new();
    public MassNavigationFlowAvoidanceTuning NavigationAvoidance { get; set; } = new();
    public TimeFlowNavigationSemanticsConfig NavigationSemantics { get; set; } = new();
    public TimeFlowPhysicsProbeConfig PhysicsProbe { get; set; } = new();

    public static CapabilityStandardTimeFlowShowcaseConfig Load(JsonObject configObject)
    {
        using var document = JsonDocument.Parse(configObject.ToJsonString());
        ValidateRequiredProperties(document.RootElement);
        var options = StrictJsonOptions.CreateCamelCase();
        CapabilityStandardTimeFlowShowcaseConfig? config = document.RootElement.Deserialize<CapabilityStandardTimeFlowShowcaseConfig>(options);
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize capability-standard TimeFlow showcase config.");
        }

        config.Validate();
        return config;
    }

    private static void ValidateRequiredProperties(JsonElement root)
    {
        RequireProperty(root, "mapId");
        RequireProperty(root, "simulationScaleRequests");
        RequireProperty(root, "gasScaleRequests");
        RequireProperty(root, "navigationProbe");
        RequireProperty(root, "navigationSolver");
        RequireProperty(root, "navigationRuntimeCapacity");
        RequireProperty(root, "navigationArrival");
        RequireProperty(root, "navigationAvoidance");
        RequireProperty(root, "navigationSemantics");
        RequireProperty(root, "physicsProbe");
    }

    private void Validate()
    {
        RequireNonEmpty(MapId, nameof(MapId));
        ValidateScaleRequests(SimulationScaleRequests, nameof(SimulationScaleRequests));
        ValidateScaleRequests(GasScaleRequests, nameof(GasScaleRequests));
        NavigationProbe.Validate(NavigationSolver);
        NavigationSolver.Validate();
        ValidateRuntimeCapacity(NavigationRuntimeCapacity);
        NavigationArrival.Validate();
        NavigationAvoidance.Validate();
        NavigationSemantics.Validate();
        PhysicsProbe.Validate();
    }

    private static void ValidateScaleRequests(TimeFlowScaleRequestConfig[] requests, string fieldName)
    {
        if (requests == null || requests.Length <= 0)
        {
            throw new InvalidOperationException($"TimeFlow showcase requires at least one {fieldName} entry.");
        }

        for (int i = 0; i < requests.Length; i++)
        {
            requests[i].Validate($"{fieldName}[{i}]");
        }
    }

    private static void ValidateRuntimeCapacity(MassNavigationRuntimeCapacityConfig capacity)
    {
        if (capacity == null)
        {
            throw new InvalidOperationException("TimeFlow showcase requires navigationRuntimeCapacity.");
        }

        RequirePositive(capacity.NavigationGroupCapacity, "navigationRuntimeCapacity.navigationGroupCapacity");
        RequirePositive(capacity.GroupMembershipAgentCapacity, "navigationRuntimeCapacity.groupMembershipAgentCapacity");
        RequirePositive(capacity.SelectionMemberScratchCapacity, "navigationRuntimeCapacity.selectionMemberScratchCapacity");
        RequirePositive(capacity.GroupMemberCapacity, "navigationRuntimeCapacity.groupMemberCapacity");
    }

    private static JsonElement RequireProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new InvalidOperationException($"Capability-standard TimeFlow showcase config requires explicit '{propertyName}' property.");
        }

        return value;
    }

    private static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Capability-standard TimeFlow showcase config requires non-empty {fieldName}.");
        }
    }

    private static void RequirePositive(int value, string fieldName)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"Capability-standard TimeFlow showcase config requires {fieldName} > 0.");
        }
    }
}

internal sealed class TimeFlowScaleRequestConfig
{
    public string Label { get; set; } = string.Empty;
    public int ScalePermille { get; set; }

    public void Validate(string context)
    {
        if (string.IsNullOrWhiteSpace(Label))
        {
            throw new InvalidOperationException($"TimeFlow showcase {context}.label must not be empty.");
        }

        if (ScalePermille <= 0 || ScalePermille > TimeFlowService.MaxScalePermille)
        {
            throw new InvalidOperationException(
                $"TimeFlow showcase {context}.scalePermille must be inside [1, {TimeFlowService.MaxScalePermille}].");
        }
    }
}

internal sealed class TimeFlowNavigationProbeConfig
{
    public int TeamId { get; set; }
    public float StartXCm { get; set; }
    public float StartYCm { get; set; }
    public float TargetXCm { get; set; }
    public float TargetYCm { get; set; }
    public float BodyRadiusCm { get; set; }
    public float SpeedCmPerSecond { get; set; }
    public float NavMass { get; set; }
    public float VisualScale { get; set; }

    public void Validate(MassNavigationFlowSolverConfig solver)
    {
        if (TeamId <= 0)
        {
            throw new InvalidOperationException("TimeFlow showcase navigationProbe.teamId must be positive.");
        }

        RequirePositive(BodyRadiusCm, "navigationProbe.bodyRadiusCm");
        RequirePositive(SpeedCmPerSecond, "navigationProbe.speedCmPerSecond");
        RequirePositive(NavMass, "navigationProbe.navMass");
        RequirePositive(VisualScale, "navigationProbe.visualScale");
        RequireInside(StartXCm, solver.PlayAreaMinXCm, solver.PlayAreaMaxXCm, "navigationProbe.startXCm");
        RequireInside(StartYCm, solver.PlayAreaMinYCm, solver.PlayAreaMaxYCm, "navigationProbe.startYCm");
        RequireInside(TargetXCm, solver.PlayAreaMinXCm, solver.PlayAreaMaxXCm, "navigationProbe.targetXCm");
        RequireInside(TargetYCm, solver.PlayAreaMinYCm, solver.PlayAreaMaxYCm, "navigationProbe.targetYCm");
    }

    private static void RequirePositive(float value, string fieldName)
    {
        if (!(value > 0f))
        {
            throw new InvalidOperationException($"TimeFlow showcase requires {fieldName} > 0.");
        }
    }

    private static void RequireInside(float value, float min, float max, string fieldName)
    {
        if (!(value >= min) || !(value <= max))
        {
            throw new InvalidOperationException($"TimeFlow showcase requires {fieldName} inside navigation solver play area.");
        }
    }
}

internal sealed class TimeFlowPhysicsProbeConfig
{
    public int StartXCm { get; set; }
    public int StartYCm { get; set; }
    public float VelocityXCmPerSecond { get; set; }
    public float VelocityYCmPerSecond { get; set; }
    public int RadiusCm { get; set; }
    public float InverseMass { get; set; }
    public float InverseInertia { get; set; }
    public float Friction { get; set; }
    public float Restitution { get; set; }
    public float BaseDamping { get; set; }

    public void Validate()
    {
        if (RadiusCm <= 0)
        {
            throw new InvalidOperationException("TimeFlow showcase physicsProbe.radiusCm must be positive.");
        }

        if (!(InverseMass > 0f) || !(InverseInertia > 0f))
        {
            throw new InvalidOperationException("TimeFlow showcase physicsProbe inverse mass and inertia must be positive.");
        }

        if (Friction < 0f || Restitution < 0f || BaseDamping < 0f)
        {
            throw new InvalidOperationException("TimeFlow showcase physicsProbe material values must be non-negative.");
        }
    }
}

internal sealed class TimeFlowNavigationSemanticsConfig
{
    public float ObstacleHardResolveCandidateDistanceCm { get; set; }
    public float ObstacleSoftPushPaddingCm { get; set; }
    public float ObstacleSoftPushForceScale { get; set; }
    public float TargetProjectionClearanceCm { get; set; }
    public float UnitTargetStopThresholdCm { get; set; }
    public float SeparationRadiusCm { get; set; }
    public float GoalArrivalRadiusCm { get; set; }
    public float FlowObstacleAvoidanceScale { get; set; }
    public float LooseSeparationScale { get; set; }
    public float VelocityBlendPerSecond { get; set; }
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

    public void Validate()
    {
        RequirePositive(ObstacleHardResolveCandidateDistanceCm, nameof(ObstacleHardResolveCandidateDistanceCm));
        RequirePositive(ObstacleSoftPushPaddingCm, nameof(ObstacleSoftPushPaddingCm));
        RequirePositive(ObstacleSoftPushForceScale, nameof(ObstacleSoftPushForceScale));
        RequireNonNegative(TargetProjectionClearanceCm, nameof(TargetProjectionClearanceCm));
        RequirePositive(UnitTargetStopThresholdCm, nameof(UnitTargetStopThresholdCm));
        RequirePositive(SeparationRadiusCm, nameof(SeparationRadiusCm));
        RequirePositive(GoalArrivalRadiusCm, nameof(GoalArrivalRadiusCm));
        RequireNonNegative(FlowObstacleAvoidanceScale, nameof(FlowObstacleAvoidanceScale));
        RequireNonNegative(LooseSeparationScale, nameof(LooseSeparationScale));
        RequireNonNegative(VelocityBlendPerSecond, nameof(VelocityBlendPerSecond));
        RequirePositive(MaxStepDtSeconds, nameof(MaxStepDtSeconds));
        RequirePositive(ParallelStepMinAgents, nameof(ParallelStepMinAgents));
        RequirePositive(DirectionEpsilonSq, nameof(DirectionEpsilonSq));
        RequirePositive(NormalizationEpsilonSq, nameof(NormalizationEpsilonSq));
        RequirePositive(InverseSqrtMinValue, nameof(InverseSqrtMinValue));
        RequireNonNegative(EntitySyncPositionEpsilonSq, nameof(EntitySyncPositionEpsilonSq));
        RequireNonNegative(EntitySyncVelocityEpsilonSq, nameof(EntitySyncVelocityEpsilonSq));
        RequireNonNegative(FacingVelocityEpsilonSq, nameof(FacingVelocityEpsilonSq));
        RequirePositive(FlowBlockedCellCost, nameof(FlowBlockedCellCost));
        RequirePositive(FlowBlockedCellThreshold, nameof(FlowBlockedCellThreshold));
        RequirePositive(FlowTargetStopDistanceSq, nameof(FlowTargetStopDistanceSq));
        RequireNonNegative(FlowObstacleNeighborRadiusCells, nameof(FlowObstacleNeighborRadiusCells));
        RequireNonNegative(FlowObstacleNeighborWeight, nameof(FlowObstacleNeighborWeight));
        RequireNonNegative(FlowObstacleAvoidanceWeight, nameof(FlowObstacleAvoidanceWeight));
        RequirePositive(CoincidentPairHashBucketCount, nameof(CoincidentPairHashBucketCount));
        RequirePositive(CoincidentPairHashPrimeA, nameof(CoincidentPairHashPrimeA));
        RequirePositive(CoincidentPairHashPrimeB, nameof(CoincidentPairHashPrimeB));
    }

    public void ApplyTo(MassNavigationCrowdSemantics semantics)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        semantics.Obstacle.HardResolveCandidateDistanceCm = ObstacleHardResolveCandidateDistanceCm;
        semantics.Obstacle.SoftPushPaddingCm = ObstacleSoftPushPaddingCm;
        semantics.Obstacle.SoftPushForceScale = ObstacleSoftPushForceScale;
        semantics.TargetProjection.TeamTargetClearanceCm = TargetProjectionClearanceCm;
        semantics.TargetProjection.GroupCenterClearanceCm = TargetProjectionClearanceCm;
        semantics.TargetProjection.TeamSlotClearanceCm = TargetProjectionClearanceCm;
        semantics.TargetProjection.GroupSlotClearanceCm = TargetProjectionClearanceCm;
        semantics.TargetProjection.LooseTargetClearanceCm = TargetProjectionClearanceCm;
        semantics.Group.UnitTargetStopThresholdCm = UnitTargetStopThresholdCm;
        semantics.Group.LooseArriveThresholdCm = GoalArrivalRadiusCm;
        semantics.Steering.SeparationRadiusCm = SeparationRadiusCm;
        semantics.Steering.GoalArrivalRadiusCm = GoalArrivalRadiusCm;
        semantics.Steering.FlowObstacleAvoidanceScale = FlowObstacleAvoidanceScale;
        semantics.Steering.LooseSeparationScale = LooseSeparationScale;
        semantics.Steering.VelocityBlendPerSecond = VelocityBlendPerSecond;
        semantics.Solver.MaxStepDtSeconds = MaxStepDtSeconds;
        semantics.Solver.ParallelStepMinAgents = ParallelStepMinAgents;
        semantics.Solver.DirectionEpsilonSq = DirectionEpsilonSq;
        semantics.Solver.NormalizationEpsilonSq = NormalizationEpsilonSq;
        semantics.Solver.InverseSqrtMinValue = InverseSqrtMinValue;
        semantics.Solver.EntitySyncPositionEpsilonSq = EntitySyncPositionEpsilonSq;
        semantics.Solver.EntitySyncVelocityEpsilonSq = EntitySyncVelocityEpsilonSq;
        semantics.Solver.FacingVelocityEpsilonSq = FacingVelocityEpsilonSq;
        semantics.Solver.FlowBlockedCellCost = FlowBlockedCellCost;
        semantics.Solver.FlowBlockedCellThreshold = FlowBlockedCellThreshold;
        semantics.Solver.FlowTargetStopDistanceSq = FlowTargetStopDistanceSq;
        semantics.Solver.FlowObstacleNeighborRadiusCells = FlowObstacleNeighborRadiusCells;
        semantics.Solver.FlowObstacleNeighborWeight = FlowObstacleNeighborWeight;
        semantics.Solver.FlowObstacleAvoidanceWeight = FlowObstacleAvoidanceWeight;
        semantics.Solver.CoincidentPairHashBucketCount = CoincidentPairHashBucketCount;
        semantics.Solver.CoincidentPairHashPrimeA = CoincidentPairHashPrimeA;
        semantics.Solver.CoincidentPairHashPrimeB = CoincidentPairHashPrimeB;
    }

    private static void RequirePositive(float value, string fieldName)
    {
        if (!(value > 0f))
        {
            throw new InvalidOperationException($"TimeFlow showcase navigationSemantics.{fieldName} must be > 0.");
        }
    }

    private static void RequirePositive(int value, string fieldName)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"TimeFlow showcase navigationSemantics.{fieldName} must be > 0.");
        }
    }

    private static void RequireNonNegative(float value, string fieldName)
    {
        if (!(value >= 0f))
        {
            throw new InvalidOperationException($"TimeFlow showcase navigationSemantics.{fieldName} must be >= 0.");
        }
    }

    private static void RequireNonNegative(int value, string fieldName)
    {
        if (value < 0)
        {
            throw new InvalidOperationException($"TimeFlow showcase navigationSemantics.{fieldName} must be >= 0.");
        }
    }
}

internal sealed class CapabilityStandardTimeFlowShowcaseConfigLoader
{
    public const string RelativePath = "CapabilityStandardTimeFlowShowcaseConfig.json";

    private readonly ConfigPipeline _pipeline;

    public CapabilityStandardTimeFlowShowcaseConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public CapabilityStandardTimeFlowShowcaseConfig Load(ConfigCatalog catalog, ConfigConflictReport report)
    {
        if (!catalog.TryGet(RelativePath, out ConfigCatalogEntry entry))
        {
            throw new InvalidOperationException($"Capability-standard TimeFlow showcase config '{RelativePath}' must be registered in config_catalog.json.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.Replace)
        {
            throw new InvalidOperationException($"Capability-standard TimeFlow showcase config '{RelativePath}' must use Replace merge policy.");
        }

        JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
        if (merged == null)
        {
            throw new InvalidOperationException($"Capability-standard TimeFlow showcase requires config '{RelativePath}' through ConfigPipeline.");
        }

        return CapabilityStandardTimeFlowShowcaseConfig.Load(merged);
    }
}
