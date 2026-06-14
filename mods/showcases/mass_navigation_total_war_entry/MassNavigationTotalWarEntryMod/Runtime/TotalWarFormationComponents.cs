using System;
using System.Numerics;

namespace MassNavigationTotalWarEntryMod.Runtime;

internal enum TotalWarFormationOutlineShape : byte
{
    Rectangle = 1,
    Circle = 2,
}

internal enum TotalWarFormationSlotLayout : byte
{
    Grid = 1,
    Disc = 2,
}

internal static class TotalWarFormationOutlineShapeNames
{
    public const string Rectangle = nameof(Rectangle);
    public const string Circle = nameof(Circle);
}

internal static class TotalWarFormationSlotLayoutNames
{
    public const string Grid = nameof(Grid);
    public const string Disc = nameof(Disc);
}

internal static class TotalWarFormationOutlineSegments
{
    private const int RectangleEdgeSegmentCount = 4;
    private const int CircleRingSegmentCount = 1;
    private const int FrontIndicatorSegmentCount = 1;

    public static int CountSplineSegments(
        TotalWarFormationOutlineShape shape,
        bool hasFrontIndicator,
        int curveSampleCount)
    {
        if (curveSampleCount <= 0)
        {
            throw new InvalidOperationException("Total War formation outline requires configured curveSampleCount > 0.");
        }

        int segmentCount = shape switch
        {
            TotalWarFormationOutlineShape.Rectangle => RectangleEdgeSegmentCount,
            TotalWarFormationOutlineShape.Circle => CircleRingSegmentCount,
            _ => throw new InvalidOperationException($"Total War formation outline has unsupported shape '{shape}'."),
        };

        if (hasFrontIndicator)
        {
            segmentCount += FrontIndicatorSegmentCount;
        }

        return checked(segmentCount * curveSampleCount);
    }

    public static int CountSplineSegments(in TotalWarFormationOutline outline)
    {
        return CountSplineSegments(
            outline.Shape,
            outline.FrontIndicatorLengthCm > 0f,
            outline.CurveSampleCount);
    }
}

internal struct TotalWarFormationSoldier
{
    public int FormationIndex;
    public int SlotIndex;
}

internal struct TotalWarFormationAgent
{
    public int FormationIndex;
}

internal struct TotalWarFormationState
{
    public int SoldierCount;
    public int AliveSoldierCount;
    public float CenterXCm;
    public float CenterYCm;
    public float FacingRad;
}

internal struct TotalWarFormationOutline
{
    public TotalWarFormationOutlineShape Shape;
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

internal struct TotalWarObstacleOverlay
{
    public float RadiusCm;
    public float HeightOffsetM;
    public float BorderWidthCm;
    public Vector4 FillColor;
    public Vector4 BorderColor;
}
