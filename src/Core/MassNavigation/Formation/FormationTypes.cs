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

public static class FormationNumericEncoding
{
    public const int RadiansScale = 1_000_000;

    public static int RoundCm(float value, string context)
    {
        if (!float.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
        {
            throw new InvalidOperationException($"{context} requires a finite centimeter value within Int32 range.");
        }

        return checked((int)MathF.Round(value));
    }

    public static int RoundCm(float value)
    {
        if (!float.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
        {
            throw new InvalidOperationException("Formation centimeter value must be finite and within Int32 range.");
        }

        return checked((int)MathF.Round(value));
    }

    public static int EncodeRadians(float value, string context)
    {
        if (!float.IsFinite(value) ||
            value < int.MinValue / (float)RadiansScale ||
            value > int.MaxValue / (float)RadiansScale)
        {
            throw new InvalidOperationException($"{context} requires finite radians within encoded Int32 range.");
        }

        return checked((int)MathF.Round(value * RadiansScale));
    }

    public static int EncodeRadians(float value)
    {
        if (!float.IsFinite(value) ||
            value < int.MinValue / (float)RadiansScale ||
            value > int.MaxValue / (float)RadiansScale)
        {
            throw new InvalidOperationException("Formation radians value must be finite and within encoded Int32 range.");
        }

        return checked((int)MathF.Round(value * RadiansScale));
    }

    public static float DecodeRadians(int encodedRadians)
    {
        return encodedRadians / (float)RadiansScale;
    }
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
    public int TargetCenterXCm;
    public int TargetCenterYCm;
    public int TargetFacingMicroRad;
    public byte HasMoveTarget;
}

public struct FormationAnchorState
{
    public int FormationIndex;
    public int SlotCount;
    public int TargetChangeEpsilonCm;
    public int FacingChangeEpsilonMicroRad;
}

public struct FormationMemberState
{
    public int FormationIndex;
    public int SlotIndex;
    public int LocalOffsetXCm;
    public int LocalOffsetYCm;
}

public struct FormationRuntimeState
{
    public int MemberCount;
    public int AliveMemberCount;
    public int CenterXCm;
    public int CenterYCm;
    public int FacingMicroRad;
}

public readonly record struct FormationTargetPlan(
    Vector2 TargetWorldCm,
    Vector2 ProjectionHintWorldCm);
