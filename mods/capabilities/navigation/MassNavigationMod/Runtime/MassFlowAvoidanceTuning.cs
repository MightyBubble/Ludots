namespace MassNavigationMod.Runtime;

internal enum MassFlowPairAvoidancePolicy : byte
{
    FriendlyCooperativeYield = 0,
    NonFriendlyBlocker = 1,
    DominantPush = 2,
}

public sealed class MassFlowAvoidanceTuning
{
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

    public void Validate()
    {
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
            throw new System.InvalidOperationException($"Mass-nav avoidance requires {name} > 0.");
        }
    }

    private static void RequireOrderedClamp(string name, float min, float max)
    {
        if (!(min > 0f) || !(max >= min))
        {
            throw new System.InvalidOperationException($"Mass-nav avoidance requires ordered positive {name} min/max.");
        }
    }

    private static void RequireShareClamp(string name, float min, float max)
    {
        if (!(min >= 0f) || !(max <= 1f) || !(max >= min))
        {
            throw new System.InvalidOperationException($"Mass-nav avoidance requires {name} min/max inside [0, 1].");
        }
    }
}

