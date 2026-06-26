using System;
using System.Numerics;

namespace FormationCapabilityShowcaseMod.Runtime;

internal enum FormationCapabilityShowcaseFormationOutlineShape : byte
{
    Rectangle = 1,
    Circle = 2,
}

internal enum FormationCapabilityShowcaseFormationSlotLayout : byte
{
    Grid = 1,
    Disc = 2,
}

internal static class FormationCapabilityShowcaseFormationOutlineShapeNames
{
    public const string Rectangle = nameof(Rectangle);
    public const string Circle = nameof(Circle);
}

internal static class FormationCapabilityShowcaseFormationSlotLayoutNames
{
    public const string Grid = nameof(Grid);
    public const string Disc = nameof(Disc);
}

internal static class FormationCapabilityShowcaseFormationOutlineSegments
{
    private const int RectangleEdgeSegmentCount = 4;
    private const int CircleRingSegmentCount = 1;
    private const int FrontIndicatorSegmentCount = 1;

    public static int CountSplineSegments(
        FormationCapabilityShowcaseFormationOutlineShape shape,
        bool hasFrontIndicator,
        int curveSampleCount)
    {
        if (curveSampleCount <= 0)
        {
            throw new InvalidOperationException("Formation Capability formation outline requires configured curveSampleCount > 0.");
        }

        int segmentCount = shape switch
        {
            FormationCapabilityShowcaseFormationOutlineShape.Rectangle => RectangleEdgeSegmentCount,
            FormationCapabilityShowcaseFormationOutlineShape.Circle => CircleRingSegmentCount,
            _ => throw new InvalidOperationException($"Formation Capability formation outline has unsupported shape '{shape}'."),
        };

        if (hasFrontIndicator)
        {
            segmentCount += FrontIndicatorSegmentCount;
        }

        return checked(segmentCount * curveSampleCount);
    }

    public static int CountSplineSegments(in FormationCapabilityShowcaseFormationOutline outline)
    {
        return CountSplineSegments(
            outline.Shape,
            outline.FrontIndicatorLengthCm > 0f,
            outline.CurveSampleCount);
    }
}

internal struct FormationCapabilityShowcaseFormationSoldier
{
    public int FormationIndex;
    public int SlotIndex;
}

internal struct FormationCapabilityShowcaseFormationAgent
{
    public int FormationIndex;
}

internal struct FormationCapabilityShowcaseFormationState
{
    public int SoldierCount;
    public int AliveSoldierCount;
    public float CenterXCm;
    public float CenterYCm;
    public float FacingRad;
}

internal struct FormationCapabilityShowcaseFormationOutline
{
    public FormationCapabilityShowcaseFormationOutlineShape Shape;
    public float WidthCm;
    public float DepthCm;
    public float RadiusCm;
    public float HeightOffsetM;
    public int CurveSampleCount;
    public float EmissionPositionEpsilonM;
    public float EmissionFacingEpsilonRadians;
    public float EdgeLineWidthCm;
    public float CircleRingWidthCm;
    public float FrontIndicatorLengthCm;
    public float FrontIndicatorLineWidthCm;
    public Vector4 FillColor;
    public Vector4 BorderColor;
}

internal struct FormationCapabilityShowcaseObstacleOverlay
{
    public float RadiusCm;
    public float HeightOffsetM;
    public float BorderWidthCm;
    public Vector4 FillColor;
    public Vector4 BorderColor;
}
