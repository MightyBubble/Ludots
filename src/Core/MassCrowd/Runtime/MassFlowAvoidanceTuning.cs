using System;
using System.Text.Json.Serialization;
using Ludots.Core.Spatial;

namespace Ludots.Core.MassCrowd.Runtime;

internal enum MassFlowPairAvoidancePolicy : byte
{
    FriendlyCooperativeYield = 0,
    NonFriendlyBlocker = 1,
    DominantPush = 2,
}

public enum MassFlowAvoidanceMode : byte
{
    Separation = 0,
    Orca = 1,
    Sonar = 2,
}

public sealed class MassFlowAvoidanceTuning
{
    public const int MaxKernelNeighbors = SpatialScaleDefaults.TerrainChunkCells;

    private MassFlowAvoidanceMode _parsedMode = MassFlowAvoidanceMode.Separation;

    public string Mode { get; set; } = "Separation";
    public MassFlowOrcaAvoidanceConfig Orca { get; set; } = new();
    public MassFlowSonarAvoidanceConfig Sonar { get; set; } = new();
    public float DominantMassRatio { get; set; } = 2.25f;
    public float FriendlyResponseScale { get; set; } = 1.1f;
    public float FriendlyResponseMin { get; set; } = 0.35f;
    public float FriendlyResponseMax { get; set; } = 2.75f;
    public float NonFriendlyResponseScale { get; set; } = 1.25f;
    public float NonFriendlyResponseMin { get; set; } = 0.25f;
    public float NonFriendlyResponseMax { get; set; } = 3.25f;
    public float DominantPushResponseScale { get; set; } = 1.6f;
    public float DominantPushResponseMin { get; set; } = 0.15f;
    public float DominantPushResponseMax { get; set; } = 4.5f;
    public float FriendlyCorrectionShareMin { get; set; } = 0.18f;
    public float FriendlyCorrectionShareMax { get; set; } = 0.82f;
    public float DominantCorrectionOtherMassWeight { get; set; } = 1.8f;
    public float DominantCorrectionShareMin { get; set; } = 0.05f;
    public float DominantCorrectionShareMax { get; set; } = 0.95f;
    public float NonFriendlyCorrectionOtherMassWeight { get; set; } = 1.2f;
    public float NonFriendlyCorrectionShareMin { get; set; } = 0.08f;
    public float NonFriendlyCorrectionShareMax { get; set; } = 0.92f;

    [JsonIgnore]
    public MassFlowAvoidanceMode ParsedMode => _parsedMode;

    public void CopyFrom(MassFlowAvoidanceTuning source)
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
            "Separation" => MassFlowAvoidanceMode.Separation,
            "Orca" => MassFlowAvoidanceMode.Orca,
            "Sonar" => MassFlowAvoidanceMode.Sonar,
            "" => throw new InvalidOperationException("Mass-nav avoidance.mode must be explicit."),
            _ => throw new InvalidOperationException($"Mass-nav avoidance.mode '{Mode}' is not configured.")
        };
        if (Orca == null)
        {
            throw new InvalidOperationException("Mass-nav avoidance.orca must be explicitly configured.");
        }

        if (Sonar == null)
        {
            throw new InvalidOperationException("Mass-nav avoidance.sonar must be explicitly configured.");
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
            throw new InvalidOperationException($"Mass-nav avoidance requires {name} > 0.");
        }
    }

    private static void RequireOrderedClamp(string name, float min, float max)
    {
        if (!(min > 0f) || !(max >= min))
        {
            throw new InvalidOperationException($"Mass-nav avoidance requires ordered positive {name} min/max.");
        }
    }

    private static void RequireShareClamp(string name, float min, float max)
    {
        if (!(min >= 0f) || !(max <= 1f) || !(max >= min))
        {
            throw new InvalidOperationException($"Mass-nav avoidance requires {name} min/max inside [0, 1].");
        }
    }
}

public sealed class MassFlowOrcaAvoidanceConfig
{
    public float TimeHorizonSeconds { get; set; } = 0.85f;
    public int MaxNeighbors { get; set; } = 16;

    public void CopyFrom(MassFlowOrcaAvoidanceConfig source)
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
            throw new InvalidOperationException($"Mass-nav avoidance.orca requires {name} > 0.");
        }
    }

    private static void RequireNeighborLimit(int value, string name)
    {
        if (value <= 0 || value > MassFlowAvoidanceTuning.MaxKernelNeighbors)
        {
            throw new InvalidOperationException(
                $"Mass-nav avoidance.orca requires {name} inside [1, {MassFlowAvoidanceTuning.MaxKernelNeighbors}].");
        }
    }
}

public sealed class MassFlowSonarAvoidanceConfig
{
    public int MaxSteerAngleDeg { get; set; } = 280;
    public int BackwardPenaltyAngleDeg { get; set; } = 230;
    public float PredictionTimeScale { get; set; } = 0.9f;
    public bool IgnoreBehindMovingAgents { get; set; } = true;
    public bool BlockedStop { get; set; }
    public bool UsePreferredVelocityWhenBlocked { get; set; } = true;
    public float TimeHorizonSeconds { get; set; } = 0.85f;
    public int MaxNeighbors { get; set; } = 16;

    public void CopyFrom(MassFlowSonarAvoidanceConfig source)
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
            throw new InvalidOperationException($"Mass-nav avoidance.sonar requires {name} inside [1, 360].");
        }
    }

    private static void RequirePositive(float value, string name)
    {
        if (!(value > 0f))
        {
            throw new InvalidOperationException($"Mass-nav avoidance.sonar requires {name} > 0.");
        }
    }

    private static void RequireNonNegative(float value, string name)
    {
        if (!(value >= 0f))
        {
            throw new InvalidOperationException($"Mass-nav avoidance.sonar requires {name} >= 0.");
        }
    }

    private static void RequireNeighborLimit(int value, string name)
    {
        if (value <= 0 || value > MassFlowAvoidanceTuning.MaxKernelNeighbors)
        {
            throw new InvalidOperationException(
                $"Mass-nav avoidance.sonar requires {name} inside [1, {MassFlowAvoidanceTuning.MaxKernelNeighbors}].");
        }
    }
}
