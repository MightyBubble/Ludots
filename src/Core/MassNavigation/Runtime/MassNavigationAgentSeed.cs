namespace Ludots.Core.MassNavigation.Runtime;

using Arch.Core;

public readonly struct MassNavigationAgentSeed
{
    public MassNavigationAgentSeed(
        int teamId,
        float localPositionXCm,
        float localPositionYCm,
        bool heavy,
        float navMass,
        float bodyRadiusCm,
        float speedCmPerSecond,
        MassNavigationAgentLayer layer)
    {
        TeamId = teamId;
        DomainRep = Entity.Null;
        LocalPositionXCm = localPositionXCm;
        LocalPositionYCm = localPositionYCm;
        Heavy = heavy;
        NavMass = navMass;
        BodyRadiusCm = bodyRadiusCm;
        SpeedCmPerSecond = speedCmPerSecond;
        Layer = layer;
    }

    public MassNavigationAgentSeed(
        Entity domainRep,
        float localPositionXCm,
        float localPositionYCm,
        bool heavy,
        float navMass,
        float bodyRadiusCm,
        float speedCmPerSecond,
        MassNavigationAgentLayer layer)
        : this(
            RequireDomainId(domainRep),
            localPositionXCm,
            localPositionYCm,
            heavy,
            navMass,
            bodyRadiusCm,
            speedCmPerSecond,
            layer)
    {
        DomainRep = domainRep;
    }

    public int TeamId { get; }
    public Entity DomainRep { get; }
    public float LocalPositionXCm { get; }
    public float LocalPositionYCm { get; }
    public bool Heavy { get; }
    public float NavMass { get; }
    public float BodyRadiusCm { get; }
    public float SpeedCmPerSecond { get; }
    public MassNavigationAgentLayer Layer { get; }

    private static int RequireDomainId(Entity domainRep)
    {
        if (domainRep == Entity.Null)
        {
            throw new InvalidOperationException("MassNavigation agent seed requires a non-null relationship domain representative.");
        }

        return domainRep.Id;
    }
}
