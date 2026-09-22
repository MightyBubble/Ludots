namespace Ludots.Core.MassNavigation.Runtime;

using Arch.Core;

/// <summary>
/// One agent entering the flow solver. The integer domain id is an opaque
/// relationship-domain key: authored agents derive it from their control-domain or
/// member-of relationship (see MassNavigationAuthoredAgentBindingSystem.ResolveDomain),
/// scenario spawn derives it from the scenario's domain table. The solver never reads
/// gameplay team state — domain pairs only feed the injected relationship projection
/// that scales avoidance.
/// </summary>
public readonly struct MassNavigationAgentSeed
{
    public MassNavigationAgentSeed(
        int relationshipDomainId,
        float localPositionXCm,
        float localPositionYCm,
        bool heavy,
        float navMass,
        float visualScale,
        float bodyRadiusCm,
        float speedCmPerSecond,
        MassNavigationAgentLayer layer)
    {
        RelationshipDomainId = relationshipDomainId;
        DomainRep = Entity.Null;
        LocalPositionXCm = localPositionXCm;
        LocalPositionYCm = localPositionYCm;
        Heavy = heavy;
        NavMass = navMass;
        VisualScale = visualScale;
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
        float visualScale,
        float bodyRadiusCm,
        float speedCmPerSecond,
        MassNavigationAgentLayer layer)
        : this(
            RequireDomainId(domainRep),
            localPositionXCm,
            localPositionYCm,
            heavy,
            navMass,
            visualScale,
            bodyRadiusCm,
            speedCmPerSecond,
            layer)
    {
        DomainRep = domainRep;
    }

    public int RelationshipDomainId { get; }
    public Entity DomainRep { get; }
    public float LocalPositionXCm { get; }
    public float LocalPositionYCm { get; }
    public bool Heavy { get; }
    public float NavMass { get; }
    public float VisualScale { get; }
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
