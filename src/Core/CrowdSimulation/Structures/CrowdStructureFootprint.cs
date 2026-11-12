using System;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Structures;

public enum CrowdStructureShape
{
    Square,
    Disc,
    Path,
}

/// <summary>
/// 厘米、Fix64。square 是半边长轴对齐盒，disc 是圆盘，path 是无圆角的条带。
/// 区域重标用格心 Covers，阻挡用格覆盖份额。
/// </summary>
public readonly struct CrowdStructureFootprint
{
    public required CrowdStructureShape Shape { get; init; }
    public Fix64 CenterXCm { get; init; }
    public Fix64 CenterYCm { get; init; }
    public Fix64 Hx { get; init; }
    public Fix64 Hy { get; init; }
    public Fix64 RadiusCm { get; init; }
    public Fix64 X0 { get; init; }
    public Fix64 Y0 { get; init; }
    public Fix64 X1 { get; init; }
    public Fix64 Y1 { get; init; }
    public Fix64 WidthCm { get; init; }

    public static CrowdStructureFootprint Make(RuntimeStructureTemplate tpl, Fix64 xCm, Fix64 yCm, Fix64 sizeCm, Fix64 toXCm, Fix64 toYCm) =>
        tpl.Footprint switch
        {
            CrowdStructureShape.Square => new CrowdStructureFootprint { Shape = CrowdStructureShape.Square, CenterXCm = xCm, CenterYCm = yCm, Hx = sizeCm / 2, Hy = sizeCm / 2 },
            CrowdStructureShape.Disc => new CrowdStructureFootprint { Shape = CrowdStructureShape.Disc, CenterXCm = xCm, CenterYCm = yCm, RadiusCm = sizeCm },
            _ => new CrowdStructureFootprint { Shape = CrowdStructureShape.Path, X0 = xCm, Y0 = yCm, X1 = toXCm, Y1 = toYCm, WidthCm = sizeCm },
        };

    /// <summary>包围盒 (minX, minY, maxX, maxY),厘米。</summary>
    public (Fix64 MinX, Fix64 MinY, Fix64 MaxX, Fix64 MaxY) Bbox() => Shape switch
    {
        CrowdStructureShape.Square => (CenterXCm - Hx, CenterYCm - Hy, CenterXCm + Hx, CenterYCm + Hy),
        CrowdStructureShape.Disc => (CenterXCm - RadiusCm, CenterYCm - RadiusCm, CenterXCm + RadiusCm, CenterYCm + RadiusCm),
        _ => (Fix64.Min(X0, X1) - WidthCm / 2, Fix64.Min(Y0, Y1) - WidthCm / 2, Fix64.Max(X0, X1) + WidthCm / 2, Fix64.Max(Y0, Y1) + WidthCm / 2),
    };

    /// <summary>按 span 的厘米端点原样落成 path。本仓库不延长。</summary>
    public static CrowdStructureFootprint FromPathSpan(CrowdSimulationPathSpan span) => new()
    {
        Shape = CrowdStructureShape.Path,
        X0 = Fix64.FromInt(span.FromXCm),
        Y0 = Fix64.FromInt(span.FromYCm),
        X1 = Fix64.FromInt(span.ToXCm),
        Y1 = Fix64.FromInt(span.ToYCm),
        WidthCm = Fix64.FromInt(span.WidthCm),
    };

    /// <summary>点是否在足迹内(path 的端上点带 ε 容差,抗浮点骑线)。</summary>
    public bool Covers(Fix64 px, Fix64 py)
    {
        switch (Shape)
        {
            case CrowdStructureShape.Square:
                return Fix64.Abs(px - CenterXCm) <= Hx && Fix64.Abs(py - CenterYCm) <= Hy;
            case CrowdStructureShape.Disc:
                // Q31.32 的平方大约在 46340 厘米溢出，斜向远处会比成圆内。
                Int128 ddx = (Int128)px.RawValue - CenterXCm.RawValue;
                Int128 ddy = (Int128)py.RawValue - CenterYCm.RawValue;
                Int128 radiusRaw = RadiusCm.RawValue;
                return ddx * ddx + ddy * ddy <= radiusRaw * radiusRaw;
            default:
                Fix64 ax = X1 - X0, ay = Y1 - Y0;
                Fix64 length = CrowdFix.Hypot(ax, ay);
                // 退化阈值 L > 1e-9 米 = 1e-7 厘米;Fix64 raw = 1e-7 × 2^32 ≈ 429.5,有依据取整
                Fix64 degenerate = Fix64.FromRaw(430);
                Fix64 ux = length > degenerate ? ax / length : Fix64.OneValue;
                Fix64 uy = length > degenerate ? ay / length : Fix64.Zero;
                Fix64 vx = px - X0, vy = py - Y0;
                Fix64 t = vx * ux + vy * uy;
                // EPS = 1e-6 米 = 1e-4 厘米;raw = 1e-4 × 2^32 ≈ 429496.7,有依据取整
                Fix64 eps = Fix64.FromRaw(429497);
                return t >= -eps && t <= length + eps && Fix64.Abs(vx * uy - vy * ux) < WidthCm / 2;
        }
    }

    /// <summary>足迹包围盒外扩 ext 厘米所触的格矩形 [x0, y0, x1, y1),夹到网格内。</summary>
    public (int X0, int Y0, int X1, int Y1) CellRectOf(int navCellSizeCm, int navCellsPerSide, int extCm = 0)
    {
        var (a, b, c, d) = Bbox();
        Fix64 ext = Fix64.FromInt(extCm);
        return (
            Math.Max(0, CrowdSimulationSpace.NavCellCoord(a - ext, navCellSizeCm)),
            Math.Max(0, CrowdSimulationSpace.NavCellCoord(b - ext, navCellSizeCm)),
            Math.Min(navCellsPerSide, CrowdSimulationSpace.NavCellCoord(c + ext, navCellSizeCm) + 1),
            Math.Min(navCellsPerSide, CrowdSimulationSpace.NavCellCoord(d + ext, navCellSizeCm) + 1));
    }
}
