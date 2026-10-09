using System;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Structures;

public enum CrowdStructureShape
{
    Rect,
    Disc,
    Path,
}

/// <summary>
/// 结构足迹组件(参考 structures/footprint.js 移植;厘米域 Fix64):
/// rect 轴对齐盒(半边长)、disc 圆盘、path 沿线段的条带(宽 w,无圆角端)。
/// 纯几何,无语义:格归属判定由使用方决定(区域栅格用格心 covers,阻挡用逐格覆盖份额)。
/// </summary>
public readonly struct CrowdStructureFootprint
{
    public required CrowdStructureShape Shape { get; init; }
    public Fix64 X { get; init; }
    public Fix64 Y { get; init; }
    public Fix64 Hx { get; init; }
    public Fix64 Hy { get; init; }
    public Fix64 R { get; init; }
    public Fix64 X0 { get; init; }
    public Fix64 Y0 { get; init; }
    public Fix64 X1 { get; init; }
    public Fix64 Y1 { get; init; }
    public Fix64 W { get; init; }

    public static CrowdStructureFootprint Make(RuntimeStructureTemplate tpl, Fix64 xCm, Fix64 yCm, Fix64 sizeCm, Fix64 toXCm, Fix64 toYCm) =>
        tpl.Footprint switch
        {
            "rect" => new CrowdStructureFootprint { Shape = CrowdStructureShape.Rect, X = xCm, Y = yCm, Hx = sizeCm / 2, Hy = sizeCm / 2 },
            "disc" => new CrowdStructureFootprint { Shape = CrowdStructureShape.Disc, X = xCm, Y = yCm, R = sizeCm },
            "path" => new CrowdStructureFootprint { Shape = CrowdStructureShape.Path, X0 = xCm, Y0 = yCm, X1 = toXCm, Y1 = toYCm, W = sizeCm },
            _ => throw new InvalidOperationException($"结构模板 {tpl.Id} 的足迹形状 \"{tpl.Footprint}\" 需为 rect / disc / path。"),
        };

    /// <summary>包围盒 (minX, minY, maxX, maxY),厘米。</summary>
    public (Fix64 MinX, Fix64 MinY, Fix64 MaxX, Fix64 MaxY) Bbox() => Shape switch
    {
        CrowdStructureShape.Rect => (X - Hx, Y - Hy, X + Hx, Y + Hy),
        CrowdStructureShape.Disc => (X - R, Y - R, X + R, Y + R),
        _ => (Fix64.Min(X0, X1) - W / 2, Fix64.Min(Y0, Y1) - W / 2, Fix64.Max(X0, X1) + W / 2, Fix64.Max(Y0, Y1) + W / 2),
    };

    /// <summary>地图桥实体的仓内足迹(F02):span 即导出端从参考仓读出的 path 足迹
    /// (两岸各延长 portalCells-1 格后取整厘米),直接落形不做二次几何。</summary>
    public static CrowdStructureFootprint Bridge(CrowdSimulationBridgeSpan span) => new()
    {
        Shape = CrowdStructureShape.Path,
        X0 = Fix64.FromInt(span.X0Cm),
        Y0 = Fix64.FromInt(span.Y0Cm),
        X1 = Fix64.FromInt(span.X1Cm),
        Y1 = Fix64.FromInt(span.Y1Cm),
        W = Fix64.FromInt(span.WidthCm),
    };

    /// <summary>点是否在足迹内(path 的端上点带 ε 容差,抗浮点骑线)。</summary>
    public bool Covers(Fix64 px, Fix64 py)
    {
        switch (Shape)
        {
            case CrowdStructureShape.Rect:
                return Fix64.Abs(px - X) <= Hx && Fix64.Abs(py - Y) <= Hy;
            case CrowdStructureShape.Disc:
                Fix64 dx = px - X, dy = py - Y;
                return dx * dx + dy * dy <= R * R;
            default:
                Fix64 ax = X1 - X0, ay = Y1 - Y0;
                Fix64 length = CrowdFix.Hypot(ax, ay);
                // 参考端退化阈值 L > 1e-9 米 = 1e-7 厘米;Fix64 raw = 1e-7 × 2^32 ≈ 429.5,有依据取整
                Fix64 degenerate = Fix64.FromRaw(430);
                Fix64 ux = length > degenerate ? ax / length : Fix64.OneValue;
                Fix64 uy = length > degenerate ? ay / length : Fix64.Zero;
                Fix64 vx = px - X0, vy = py - Y0;
                Fix64 t = vx * ux + vy * uy;
                // 参考端 EPS = 1e-6 米 = 1e-4 厘米;raw = 1e-4 × 2^32 ≈ 429496.7,有依据取整
                Fix64 eps = Fix64.FromRaw(429497);
                return t >= -eps && t <= length + eps && Fix64.Abs(vx * uy - vy * ux) < W / 2;
        }
    }

    /// <summary>path 足迹:点到两端沿线段距离的较小者(厘米);portal 判定用。</summary>
    public Fix64 EndDistance(Fix64 px, Fix64 py)
    {
        Fix64 ax = X1 - X0, ay = Y1 - Y0;
        Fix64 length = CrowdFix.Hypot(ax, ay);
        // 同 Covers 的退化阈值(1e-9 米)
        if (length <= Fix64.FromRaw(430)) return Fix64.Zero;
        Fix64 t = ((px - X0) * ax + (py - Y0) * ay) / length;
        return Fix64.Min(t, length - t);
    }

    /// <summary>足迹包围盒外扩 ext 厘米所触的格矩形 [x0, y0, x1, y1),夹到网格内。</summary>
    public (int X0, int Y0, int X1, int Y1) CellRectOf(int cellSizeCm, int n, int extCm = 0)
    {
        var (a, b, c, d) = Bbox();
        Fix64 ext = Fix64.FromInt(extCm);
        Fix64 cs = Fix64.FromInt(cellSizeCm);
        return (
            Math.Max(0, (int)Fix64.Floor((a - ext) / cs).ToLong()),
            Math.Max(0, (int)Fix64.Floor((b - ext) / cs).ToLong()),
            Math.Min(n, (int)Fix64.Floor((c + ext) / cs).ToLong() + 1),
            Math.Min(n, (int)Fix64.Floor((d + ext) / cs).ToLong() + 1));
    }
}
