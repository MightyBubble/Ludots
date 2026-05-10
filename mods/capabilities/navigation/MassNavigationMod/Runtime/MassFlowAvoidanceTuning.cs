namespace MassNavigationMod.Runtime;

internal enum MassFlowPairAvoidancePolicy : byte
{
    FriendlyCooperativeYield = 0,
    NonFriendlyBlocker = 1,
    DominantPush = 2,
}

public sealed class MassFlowAvoidanceTuning
{
    public float LightNavMass { get; set; } = 1f;
    public float HeavyNavMass { get; set; } = 4f;
    public float LightVisualScale { get; set; } = 0.22f;
    public float HeavyVisualScale { get; set; } = 0.34f;
    public float DominantMassRatio { get; set; } = 2.25f;
    public float FriendlyResponseScale { get; set; } = 1.1f;
    public float NonFriendlyResponseScale { get; set; } = 1.25f;
    public float DominantPushResponseScale { get; set; } = 1.6f;
}

