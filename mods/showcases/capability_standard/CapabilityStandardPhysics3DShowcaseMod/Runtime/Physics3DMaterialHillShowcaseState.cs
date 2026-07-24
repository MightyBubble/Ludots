namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal readonly record struct Physics3DMaterialHillShowcaseState(
    Physics3DShowcaseChallengeStatus Status,
    int ElapsedTicks,
    int TicksRemaining,
    int StableTicks,
    int RequiredStableTicks,
    int FirstPlaceLaneIndex,
    int SecondPlaceLaneIndex,
    int ThirdPlaceLaneIndex,
    float FirstPlaceTravelCm,
    float SecondPlaceTravelCm,
    float ThirdPlaceTravelCm)
{
    public static Physics3DMaterialHillShowcaseState Empty { get; } = new(
        Status: Physics3DShowcaseChallengeStatus.Ready,
        ElapsedTicks: 0,
        TicksRemaining: 0,
        StableTicks: 0,
        RequiredStableTicks: 0,
        FirstPlaceLaneIndex: -1,
        SecondPlaceLaneIndex: -1,
        ThirdPlaceLaneIndex: -1,
        FirstPlaceTravelCm: 0f,
        SecondPlaceTravelCm: 0f,
        ThirdPlaceTravelCm: 0f);

    public float WinningMarginCm => FirstPlaceTravelCm - SecondPlaceTravelCm;
}
