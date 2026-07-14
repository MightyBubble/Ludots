using System.Numerics;

namespace Ludots.Core.MassNavigation.Formation;

public static class FormationOrderKeys
{
    public const string Move = "formationMove";
    public const string Rotate = "formationRotate";
}

public enum FormationSlotLayout : byte
{
    Grid = 1,
    Disc = 2,
}

public readonly record struct FormationPose(
    Vector2 CenterWorldCm,
    float FacingRadians);

public readonly record struct FormationMember(
    int FormationIndex,
    int SlotIndex,
    Vector2 LocalOffsetCm);

public readonly record struct FormationSlotPlan(
    FormationSlotLayout Layout,
    int SlotCount,
    int Columns,
    int Rows,
    float SpacingXCm,
    float SpacingYCm,
    float RingSpacingCm);

public struct FormationCommandState
{
    public float TargetCenterXCm;
    public float TargetCenterYCm;
    public float TargetFacingRad;
    public byte HasMoveTarget;
}

public struct FormationAnchorState
{
    public int FormationIndex;
    public int SlotCount;
    public float TargetChangeEpsilonCm;
    public float FacingChangeEpsilonRadians;
}

public struct FormationMemberState
{
    public int FormationIndex;
    public int SlotIndex;
    public float LocalOffsetXCm;
    public float LocalOffsetYCm;
}

public struct FormationRuntimeState
{
    public int MemberCount;
    public int AliveMemberCount;
    public float CenterXCm;
    public float CenterYCm;
    public float FacingRad;
}

public readonly record struct FormationTargetPlan(
    Vector2 TargetWorldCm,
    Vector2 ProjectionHintWorldCm);
