using System;
using System.Text.Json.Serialization;
using Ludots.Core.Spatial;

namespace Ludots.Core.MassNavigation.Runtime;

internal enum MassNavigationFlowPairAvoidancePolicy : byte
{
    FriendlyCooperativeYield = 0,
    NonFriendlyBlocker = 1,
    DominantPush = 2,
}

public enum MassNavigationFlowAvoidanceMode : byte
{
    Separation = 0,
    Orca = 1,
    Sonar = 2,
}

public sealed class MassNavigationFlowAvoidanceTuning
{
    public const int MaxKernelNeighbors = SpatialScaleDefaults.TerrainChunkCells;

    private MassNavigationFlowAvoidanceMode _parsedMode;

    public string Mode { get; set; } = string.Empty;
    public MassNavigationFlowOrcaAvoidanceConfig Orca { get; set; } = new();
    public MassNavigationFlowSonarAvoidanceConfig Sonar { get; set; } = new();
    public float DominantMassRatio { get; set; }
    public float FriendlyResponseScale { get; set; }
    public float FriendlyResponseMin { get; set; }
    public float FriendlyResponseMax { get; set; }
    public float NonFriendlyResponseScale { get; set; }
    public float NonFriendlyResponseMin { get; set; }
    public float NonFriendlyResponseMax { get; set; }
    public float DominantPushResponseScale { get; set; }
    public float DominantPushResponseMin { get; set; }
    public float DominantPushResponseMax { get; set; }
    public float FriendlyCorrectionShareMin { get; set; }
    public float FriendlyCorrectionShareMax { get; set; }
    public float DominantCorrectionOtherMassWeight { get; set; }
    public float DominantCorrectionShareMin { get; set; }
    public float DominantCorrectionShareMax { get; set; }
    public float NonFriendlyCorrectionOtherMassWeight { get; set; }
    public float NonFriendlyCorrectionShareMin { get; set; }
    public float NonFriendlyCorrectionShareMax { get; set; }

    [JsonIgnore]
    public MassNavigationFlowAvoidanceMode ParsedMode => _parsedMode;

    public void CopyFrom(MassNavigationFlowAvoidanceTuning source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Mode = source.Mode;
        _parsedMode = source.ParsedMode;
        Orca.CopyFrom(source.Orca);
        Sonar.CopyFrom(source.Sonar);
        DominantMassRatio = source.DominantMassRatio;
        FriendlyResponseScale = source.FriendlyResponseScale;
        FriendlyResponseMin = source.FriendlyResponseMin;
        FriendlyResponseMax = source.FriendlyResponseMax;
        NonFriendlyResponseScale = source.NonFriendlyResponseScale;
        NonFriendlyResponseMin = source.NonFriendlyResponseMin;
        NonFriendlyResponseMax = source.NonFriendlyResponseMax;
        DominantPushResponseScale = source.DominantPushResponseScale;
        DominantPushResponseMin = source.DominantPushResponseMin;
        DominantPushResponseMax = source.DominantPushResponseMax;
        FriendlyCorrectionShareMin = source.FriendlyCorrectionShareMin;
        FriendlyCorrectionShareMax = source.FriendlyCorrectionShareMax;
        DominantCorrectionOtherMassWeight = source.DominantCorrectionOtherMassWeight;
        DominantCorrectionShareMin = source.DominantCorrectionShareMin;
        DominantCorrectionShareMax = source.DominantCorrectionShareMax;
        NonFriendlyCorrectionOtherMassWeight = source.NonFriendlyCorrectionOtherMassWeight;
        NonFriendlyCorrectionShareMin = source.NonFriendlyCorrectionShareMin;
        NonFriendlyCorrectionShareMax = source.NonFriendlyCorrectionShareMax;
    }

    public void Validate()
    {
        _parsedMode = Mode switch
        {
            "Separation" => MassNavigationFlowAvoidanceMode.Separation,
            "Orca" => MassNavigationFlowAvoidanceMode.Orca,
            "Sonar" => MassNavigationFlowAvoidanceMode.Sonar,
            "" => throw new InvalidOperationException("MassNavigation avoidance.mode must be explicit."),
            _ => throw new InvalidOperationException($"MassNavigation avoidance.mode '{Mode}' is not configured.")
        };
        if (Orca == null)
        {
            throw new InvalidOperationException("MassNavigation avoidance.orca must be explicitly configured.");
        }

        if (Sonar == null)
        {
            throw new InvalidOperationException("MassNavigation avoidance.sonar must be explicitly configured.");
        }

        Orca.Validate();
        Sonar.Validate();
        RequirePositive(DominantMassRatio, nameof(DominantMassRatio));
        RequirePositive(FriendlyResponseScale, nameof(FriendlyResponseScale));
        RequirePositive(NonFriendlyResponseScale, nameof(NonFriendlyResponseScale));
        RequirePositive(DominantPushResponseScale, nameof(DominantPushResponseScale));
        RequirePositive(DominantCorrectionOtherMassWeight, nameof(DominantCorrectionOtherMassWeight));
        RequirePositive(NonFriendlyCorrectionOtherMassWeight, nameof(NonFriendlyCorrectionOtherMassWeight));
        RequireOrderedClamp("FriendlyResponse", FriendlyResponseMin, FriendlyResponseMax);
        RequireOrderedClamp("NonFriendlyResponse", NonFriendlyResponseMin, NonFriendlyResponseMax);
        RequireOrderedClamp("DominantPushResponse", DominantPushResponseMin, DominantPushResponseMax);
        RequireShareClamp("FriendlyCorrectionShare", FriendlyCorrectionShareMin, FriendlyCorrectionShareMax);
        RequireShareClamp("DominantCorrectionShare", DominantCorrectionShareMin, DominantCorrectionShareMax);
        RequireShareClamp("NonFriendlyCorrectionShare", NonFriendlyCorrectionShareMin, NonFriendlyCorrectionShareMax);
    }

    private static void RequirePositive(float value, string name)
    {
        if (!(value > 0f))
        {
            throw new InvalidOperationException($"MassNavigation avoidance requires {name} > 0.");
        }
    }

    private static void RequireOrderedClamp(string name, float min, float max)
    {
        if (!(min > 0f) || !(max >= min))
        {
            throw new InvalidOperationException($"MassNavigation avoidance requires ordered positive {name} min/max.");
        }
    }

    private static void RequireShareClamp(string name, float min, float max)
    {
        if (!(min >= 0f) || !(max <= 1f) || !(max >= min))
        {
            throw new InvalidOperationException($"MassNavigation avoidance requires {name} min/max inside [0, 1].");
        }
    }
}

public sealed class MassNavigationFlowOrcaAvoidanceConfig
{
    public float TimeHorizonSeconds { get; set; }
    public int MaxNeighbors { get; set; }

    public void CopyFrom(MassNavigationFlowOrcaAvoidanceConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);
        TimeHorizonSeconds = source.TimeHorizonSeconds;
        MaxNeighbors = source.MaxNeighbors;
    }

    public void Validate()
    {
        RequirePositive(TimeHorizonSeconds, nameof(TimeHorizonSeconds));
        RequireNeighborLimit(MaxNeighbors, nameof(MaxNeighbors));
    }

    private static void RequirePositive(float value, string name)
    {
        if (!(value > 0f))
        {
            throw new InvalidOperationException($"MassNavigation avoidance.orca requires {name} > 0.");
        }
    }

    private static void RequireNeighborLimit(int value, string name)
    {
        if (value <= 0 || value > MassNavigationFlowAvoidanceTuning.MaxKernelNeighbors)
        {
            throw new InvalidOperationException(
                $"MassNavigation avoidance.orca requires {name} inside [1, {MassNavigationFlowAvoidanceTuning.MaxKernelNeighbors}].");
        }
    }
}

public sealed class MassNavigationFlowSonarAvoidanceConfig
{
    public int MaxSteerAngleDeg { get; set; }
    public int BackwardPenaltyAngleDeg { get; set; }
    public float PredictionTimeScale { get; set; }
    public bool IgnoreBehindMovingAgents { get; set; }
    public bool BlockedStop { get; set; }
    public bool UsePreferredVelocityWhenBlocked { get; set; }
    public float TimeHorizonSeconds { get; set; }
    public int MaxNeighbors { get; set; }

    public void CopyFrom(MassNavigationFlowSonarAvoidanceConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);
        MaxSteerAngleDeg = source.MaxSteerAngleDeg;
        BackwardPenaltyAngleDeg = source.BackwardPenaltyAngleDeg;
        PredictionTimeScale = source.PredictionTimeScale;
        IgnoreBehindMovingAgents = source.IgnoreBehindMovingAgents;
        BlockedStop = source.BlockedStop;
        UsePreferredVelocityWhenBlocked = source.UsePreferredVelocityWhenBlocked;
        TimeHorizonSeconds = source.TimeHorizonSeconds;
        MaxNeighbors = source.MaxNeighbors;
    }

    public void Validate()
    {
        RequireAngle(MaxSteerAngleDeg, nameof(MaxSteerAngleDeg));
        RequireAngle(BackwardPenaltyAngleDeg, nameof(BackwardPenaltyAngleDeg));
        RequireNonNegative(PredictionTimeScale, nameof(PredictionTimeScale));
        RequirePositive(TimeHorizonSeconds, nameof(TimeHorizonSeconds));
        RequireNeighborLimit(MaxNeighbors, nameof(MaxNeighbors));
    }

    private static void RequireAngle(int value, string name)
    {
        if (value <= 0 || value > 360)
        {
            throw new InvalidOperationException($"MassNavigation avoidance.sonar requires {name} inside [1, 360].");
        }
    }

    private static void RequirePositive(float value, string name)
    {
        if (!(value > 0f))
        {
            throw new InvalidOperationException($"MassNavigation avoidance.sonar requires {name} > 0.");
        }
    }

    private static void RequireNonNegative(float value, string name)
    {
        if (!(value >= 0f))
        {
            throw new InvalidOperationException($"MassNavigation avoidance.sonar requires {name} >= 0.");
        }
    }

    private static void RequireNeighborLimit(int value, string name)
    {
        if (value <= 0 || value > MassNavigationFlowAvoidanceTuning.MaxKernelNeighbors)
        {
            throw new InvalidOperationException(
                $"MassNavigation avoidance.sonar requires {name} inside [1, {MassNavigationFlowAvoidanceTuning.MaxKernelNeighbors}].");
        }
    }
}
