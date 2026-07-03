namespace Ludots.Core.MassNavigation.Runtime;

public readonly struct MassNavigationAgentSeed
{
    public MassNavigationAgentSeed(
        int teamId,
        float localPositionXCm,
        float localPositionYCm,
        bool heavy,
        float navMass,
        float visualScale,
        float bodyRadiusCm,
        float speedCmPerSecond,
        MassNavigationAgentLayer layer)
    {
        TeamId = teamId;
        LocalPositionXCm = localPositionXCm;
        LocalPositionYCm = localPositionYCm;
        Heavy = heavy;
        NavMass = navMass;
        VisualScale = visualScale;
        BodyRadiusCm = bodyRadiusCm;
        SpeedCmPerSecond = speedCmPerSecond;
        Layer = layer;
    }

    public int TeamId { get; }
    public float LocalPositionXCm { get; }
    public float LocalPositionYCm { get; }
    public bool Heavy { get; }
    public float NavMass { get; }
    public float VisualScale { get; }
    public float BodyRadiusCm { get; }
    public float SpeedCmPerSecond { get; }
    public MassNavigationAgentLayer Layer { get; }
}
