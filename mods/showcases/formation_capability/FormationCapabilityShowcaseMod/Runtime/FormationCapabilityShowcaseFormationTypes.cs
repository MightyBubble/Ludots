using System.Numerics;

namespace FormationCapabilityShowcaseMod.Runtime;

internal enum FormationSlotLayout : byte
{
    Grid = 1,
    Disc = 2,
}

internal static class FormationNumericEncoding
{
    public const int RadiansScale = 1_000_000;

    private const float Int32InclusiveLowerBound = -2_147_483_648f;
    private const float Int32ExclusiveUpperBound = 2_147_483_648f;

    public static bool TryRoundCm(float value, out int encoded)
    {
        return TryRoundToInt32(value, out encoded);
    }

    public static int RoundCm(float value, string context)
    {
        if (!TryRoundCm(value, out int encoded))
        {
            throw new InvalidOperationException($"{context} requires a finite centimeter value within Int32 range.");
        }

        return encoded;
    }

    public static int RoundCm(float value)
    {
        if (!TryRoundCm(value, out int encoded))
        {
            throw new InvalidOperationException("Formation centimeter value must be finite and within Int32 range.");
        }

        return encoded;
    }

    public static bool TryEncodeRadians(float value, out int encoded)
    {
        if (!float.IsFinite(value))
        {
            encoded = default;
            return false;
        }

        return TryRoundToInt32(value * RadiansScale, out encoded);
    }

    public static int EncodeRadians(float value, string context)
    {
        if (!TryEncodeRadians(value, out int encoded))
        {
            throw new InvalidOperationException($"{context} requires finite radians within encoded Int32 range.");
        }

        return encoded;
    }

    public static int EncodeRadians(float value)
    {
        if (!TryEncodeRadians(value, out int encoded))
        {
            throw new InvalidOperationException("Formation radians value must be finite and within encoded Int32 range.");
        }

        return encoded;
    }

    public static float DecodeRadians(int encodedRadians)
    {
        return encodedRadians / (float)RadiansScale;
    }

    private static bool TryRoundToInt32(float value, out int encoded)
    {
        if (!float.IsFinite(value))
        {
            encoded = default;
            return false;
        }

        float rounded = MathF.Round(value);
        if (rounded < Int32InclusiveLowerBound || rounded >= Int32ExclusiveUpperBound)
        {
            encoded = default;
            return false;
        }

        encoded = (int)rounded;
        return true;
    }
}

internal readonly record struct FormationPose(
    Vector2 CenterWorldCm,
    float FacingRadians);

internal readonly record struct FormationMember(
    int FormationIndex,
    int SlotIndex,
    Vector2 LocalOffsetCm);

internal readonly record struct FormationSlotPlan(
    FormationSlotLayout Layout,
    int SlotCount,
    int Columns,
    int Rows,
    float SpacingXCm,
    float SpacingYCm,
    float RingSpacingCm);

internal struct FormationAnchorState
{
    public int FormationIndex;
    public int SlotCount;
}

internal struct FormationMemberState
{
    public int FormationIndex;
    public int SlotIndex;
    public int LocalOffsetXCm;
    public int LocalOffsetYCm;
}

internal struct FormationRuntimeState
{
    public int MemberCount;
    public int AliveMemberCount;
    public int CenterXCm;
    public int CenterYCm;
    public int FacingMicroRad;
}

internal readonly record struct FormationTargetPlan(
    Vector2 TargetWorldCm,
    Vector2 ProjectionHintWorldCm);
