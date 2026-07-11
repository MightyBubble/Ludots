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

    [JsonRequired] public string Mode { get; set; } = string.Empty;
    [JsonRequired] public MassNavigationFlowOrcaAvoidanceConfig Orca { get; set; } = new();
    [JsonRequired] public MassNavigationFlowSonarAvoidanceConfig Sonar { get; set; } = new();
    [JsonRequired] public float DominantMassRatio { get; set; }
    [JsonRequired] public float FriendlyResponseScale { get; set; }
    [JsonRequired] public float FriendlyResponseMin { get; set; }
    [JsonRequired] public float FriendlyResponseMax { get; set; }
    [JsonRequired] public float NonFriendlyResponseScale { get; set; }
    [JsonRequired] public float NonFriendlyResponseMin { get; set; }
    [JsonRequired] public float NonFriendlyResponseMax { get; set; }
    [JsonRequired] public float DominantPushResponseScale { get; set; }
    [JsonRequired] public float DominantPushResponseMin { get; set; }
    [JsonRequired] public float DominantPushResponseMax { get; set; }
    [JsonRequired] public float FriendlyCorrectionShareMin { get; set; }
    [JsonRequired] public float FriendlyCorrectionShareMax { get; set; }
    [JsonRequired] public float DominantCorrectionOtherMassWeight { get; set; }
    [JsonRequired] public float DominantCorrectionShareMin { get; set; }
    [JsonRequired] public float DominantCorrectionShareMax { get; set; }
    [JsonRequired] public float NonFriendlyCorrectionOtherMassWeight { get; set; }
    [JsonRequired] public float NonFriendlyCorrectionShareMin { get; set; }
    [JsonRequired] public float NonFriendlyCorrectionShareMax { get; set; }

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
    [JsonRequired] public float TimeHorizonSeconds { get; set; }
    [JsonRequired] public int MaxNeighbors { get; set; }

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
    [JsonRequired] public int MaxSteerAngleDeg { get; set; }
    [JsonRequired] public int BackwardPenaltyAngleDeg { get; set; }
    [JsonRequired] public float PredictionTimeScale { get; set; }
    [JsonRequired] public bool IgnoreBehindMovingAgents { get; set; }
    [JsonRequired] public bool BlockedStop { get; set; }
    [JsonRequired] public bool UsePreferredVelocityWhenBlocked { get; set; }
    [JsonRequired] public float TimeHorizonSeconds { get; set; }
    [JsonRequired] public int MaxNeighbors { get; set; }

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
