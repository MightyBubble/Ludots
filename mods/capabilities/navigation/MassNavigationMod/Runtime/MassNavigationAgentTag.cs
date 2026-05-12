namespace MassNavigationMod.Runtime;

public struct MassNavigationAgentTag
{
}

public struct MassNavigationControllable
{
}

public struct MassNavigationBlocker
{
}

public struct MassNavigationHotspotMarker
{
}

public struct MassNavigationAgentIndex
{
    public int Value;
}

public struct MassNavigationAgentProfile
{
    public bool Heavy;
    public float NavMass;
    public float VisualScale;
    public float BodyRadiusCm;
    public float SpeedCmPerSecond;
}

public struct MassNavigationBlockerProfile
{
    public float RadiusCm;
}

