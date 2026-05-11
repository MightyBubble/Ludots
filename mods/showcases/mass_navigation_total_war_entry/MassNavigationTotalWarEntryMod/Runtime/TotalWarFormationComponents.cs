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

internal struct TotalWarFormationSoldier
{
    public int FormationIndex;
    public int SlotIndex;
}

internal struct TotalWarFormationAnchor
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
    public float EdgeLineWidthCm;
    public float CircleRingWidthCm;
    public float FrontIndicatorLengthCm;
    public float FrontIndicatorLineWidthCm;
    public Vector4 FillColor;
    public Vector4 BorderColor;
}
