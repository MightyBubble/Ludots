using Arch.Core;

namespace Ludots.Core.MassCrowd.Runtime;

public struct MassCrowdAgent
{
    public int ProfileId;
}

public struct MassCrowdAgentIndex
{
    public int Value;
}

public struct MassCrowdAgentProfile
{
    public int ProfileId;
    public bool Heavy;
    public float VisualScale;
    public float SpeedCmPerSecond;
}

public struct MassCrowdBlocker
{
    public float RadiusCm;
}

public struct MassCrowdBlockerProfile
{
    public float RadiusCm;
}

public struct MassCrowdHotspotMarker
{
}

public struct SimulationAuthority
{
}

public struct SimulationResidencyPolicy
{
    public SimulationResidencyKind Kind;
}

public enum SimulationResidencyKind : byte
{
    AlwaysResident = 1,
    BudgetedResident = 2,
    Streamable = 3,
}

public struct CollisionParticipation
{
    public CollisionParticipationKind Kind;
}

public enum CollisionParticipationKind : byte
{
    CrowdOnly = 1,
    Physics2D = 2,
    Physics2DAndCrowd = 3,
}

public struct AvoidanceLane
{
    public AvoidanceLaneKind Kind;
}

public enum AvoidanceLaneKind : byte
{
    FormationPhysics = 1,
    MassCrowd = 2,
}

public struct MassCrowdFormationAnchor
{
    public int FormationId;
    public int SlotCount;
}

public struct MassCrowdFormationFollower
{
    public int FormationId;
    public Entity Anchor;
    public int SlotIndex;
    public float LocalOffsetXCm;
    public float LocalOffsetYCm;
}

public struct MassCrowdFollowerLocomotion
{
    public float TargetChangeEpsilonCm;
    public float FacingChangeEpsilonRadians;
}
