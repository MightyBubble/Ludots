namespace MassNavigationMod.Runtime;

public readonly struct MassNavigationAgentSeed
{
    public MassNavigationAgentSeed(
        int teamId,
        float localPositionXCm,
        float localPositionYCm,
        bool heavy,
        float navMass,
        float visualScale)
    {
        TeamId = teamId;
        LocalPositionXCm = localPositionXCm;
        LocalPositionYCm = localPositionYCm;
        Heavy = heavy;
        NavMass = navMass;
        VisualScale = visualScale;
    }

    public int TeamId { get; }
    public float LocalPositionXCm { get; }
    public float LocalPositionYCm { get; }
    public bool Heavy { get; }
    public float NavMass { get; }
    public float VisualScale { get; }
}
