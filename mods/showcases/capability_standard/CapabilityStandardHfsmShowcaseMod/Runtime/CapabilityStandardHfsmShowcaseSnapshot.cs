namespace CapabilityStandardHfsmShowcaseMod.Runtime;

public sealed record CapabilityStandardHfsmShowcaseSnapshot(
    bool IsActive,
    string StateId,
    string StateLabel,
    string StatePath,
    string PlayerStory,
    string LastEvent,
    int Health,
    int Water,
    int LapCount,
    int TransitionCount,
    int HeroXCm,
    int HeroYCm,
    bool AnyStateArmed,
    bool Dead)
{
    public static CapabilityStandardHfsmShowcaseSnapshot Inactive { get; } = new(
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        0,
        0,
        0,
        0,
        0,
        0,
        false,
        false);
}
