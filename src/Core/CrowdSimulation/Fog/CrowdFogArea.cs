using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Fog;

/// <summary>恰一种形状：rect、circle 或 poly。</summary>
public sealed record CrowdFogShape
{
    /// <summary>rect [x0, y0, x1, y1](厘米)。</summary>
    public double[]? Rect { get; init; }
    /// <summary>circle [cx, cy, r](厘米,r &gt; 0)。</summary>
    public double[]? Circle { get; init; }
    /// <summary>poly ≥ 3 个 [x, y] 顶点(厘米)。</summary>
    public double[][]? Poly { get; init; }

    private static readonly string[] ShapeNames = { "rect", "circle", "poly" };

    /// <summary>恰一形状门:area 多给少给都拒——与 Parse 同口径同报错,入队校验与解析共用,
    /// 形状名集合只有这一份。</summary>
    public static void RequireExactlyOne(string type, JsonNode cmd)
    {
        int given = 0;
        foreach (var n in ShapeNames)
        {
            if (cmd[n] != null) given++;
        }

        if (given != 1)
        {
            throw new InvalidOperationException($"{type}: 需且仅需给出 rect / circle / poly 之一");
        }
    }

    /// <summary>从指令 JSON 解析恰一形状。</summary>
    public static CrowdFogShape Parse(string type, JsonNode cmd)
    {
        RequireExactlyOne(type, cmd);
        bool Fin(JsonNode? v) => v is JsonValue jv && jv.TryGetValue<double>(out _);

        if (cmd["rect"] is { } r)
        {
            if (r is not JsonArray ra || ra.Count != 4 || !AllFin(ra))
            {
                throw new InvalidOperationException($"{type}: rect 需为 [x0, y0, x1, y1](厘米)");
            }

            return new CrowdFogShape { Rect = Numbers(ra) };
        }

        if (cmd["circle"] is { } c)
        {
            if (c is not JsonArray ca || ca.Count != 3 || !AllFin(ca) || ca[2]!.GetValue<double>() <= 0)
            {
                throw new InvalidOperationException($"{type}: circle 需为 [cx, cy, r](厘米,r > 0)");
            }

            return new CrowdFogShape { Circle = Numbers(ca) };
        }

        var p = cmd["poly"]!;
        if (p is not JsonArray pa || pa.Count < 3)
        {
            throw new InvalidOperationException($"{type}: poly 需为 ≥3 个 [x, y] 顶点(厘米)");
        }

        var pts = new double[pa.Count][];
        for (int i = 0; i < pa.Count; i++)
        {
            if (pa[i] is not JsonArray v || v.Count != 2 || !Fin(v[0]) || !Fin(v[1]))
            {
                throw new InvalidOperationException($"{type}: poly 需为 ≥3 个 [x, y] 顶点(厘米)");
            }

            pts[i] = Numbers(v);
        }

        return new CrowdFogShape { Poly = pts };

        bool AllFin(JsonArray a)
        {
            foreach (var v in a)
            {
                if (!Fin(v)) return false;
            }

            return true;
        }
    }

    private static double[] Numbers(JsonArray a)
    {
        var r = new double[a.Count];
        for (int i = 0; i < a.Count; i++) r[i] = a[i]!.GetValue<double>();
        return r;
    }
}

/// <summary>形状 → fog 格。rect 覆盖触到的每一格；circle / poly 用格心。
/// 空结果回退锚点格（circle 心 / poly 首顶点）。米坐标 FromDouble 后除以格长。
/// 乘用 MulExact，除用精确商，开方用 SqrtPrecise；poly 扫描线交点 128 位先乘后除。circle / poly 超出 ±46340 m 抛错；
/// rect 不设这个守卫。命令在 tick 内执行。</summary>
public static class CrowdFogArea
{
    /// <summary>米域输入守卫:circle 半径/圆心与 poly 顶点超出即拒——Q31.32 平方安全界,
    /// 回绕不许静默发生(rect 无平方不设守卫,越图仍走 clamp)。
    /// 与 <see cref="Ludots.Core.CrowdSimulation.Config.CrowdSimulationConfigLoader.MinFogCellSizeCm"/> 成对:格长 ≥ 1 米时,
    /// 本守卫内的格坐标(圆扫描线再加半格)平方仍小于 2^31。</summary>
    private const double MaxShapeMeters = 46340.0;

    private static void RequireShapeMeters(double[] values)
    {
        foreach (double v in values)
        {
            if (!(Math.Abs(CrowdReferenceMeters.CmToM(v)) <= MaxShapeMeters)) // 载荷是厘米,守卫以米计
            {
                throw new InvalidOperationException(
                    $"迷雾区域形状坐标超出 ±{MaxShapeMeters} 米平方安全界——拒绝静默回绕。");
            }
        }
    }

    public static (int[] Cells, (int X0, int Y0, int X1, int Y1) Box) AreaCells(int f, int fogCellSizeCm, CrowdFogShape shape)
    {
        Fix64 fogCellSizeM = CrowdReferenceMeters.CmToMFix(fogCellSizeCm);
        int ClampCell(Fix64 v) => Math.Max(0, Math.Min(f - 1, Fix64.Floor(v).ToInt()));
        Fix64 Cell(double cm) => CrowdReferenceMeters.CmToMFix(cm) / fogCellSizeM;
        var cells = new List<int>();
        (int X0, int Y0, int X1, int Y1) box;

        if (shape.Rect is { } rect)
        {
            Fix64 ax = Fix64.Min(Cell(rect[0]), Cell(rect[2]));
            Fix64 bx = Fix64.Max(Cell(rect[0]), Cell(rect[2]));
            Fix64 ay = Fix64.Min(Cell(rect[1]), Cell(rect[3]));
            Fix64 by = Fix64.Max(Cell(rect[1]), Cell(rect[3]));
            box = (ClampCell(ax), ClampCell(ay), ClampCell(bx), ClampCell(by));
            for (int y = box.Y0; y <= box.Y1; y++)
            {
                for (int x = box.X0; x <= box.X1; x++) cells.Add(y * f + x);
            }

            return (cells.ToArray(), box);
        }

        var xs = new Fix64[CrowdSimulationLimits.FogPolygonOrdinateScratch];
        Fix64[] anchor;
        Fix64 bx0, by0, bx1, by1;
        Fix64[][]? pts = null;
        Fix64 c0 = Fix64.Zero, c1 = Fix64.Zero, r = Fix64.Zero;
        if (shape.Circle is { } c)
        {
            RequireShapeMeters(c);
            c0 = Cell(c[0]);
            c1 = Cell(c[1]);
            r = Cell(c[2]);
            anchor = new[] { c0, c1 };
            bx0 = c0 - r; by0 = c1 - r; bx1 = c0 + r; by1 = c1 + r;
        }
        else
        {
            var poly = shape.Poly!;
            foreach (double[] p in poly) RequireShapeMeters(p);
            pts = new Fix64[poly.Length][];
            for (int i = 0; i < pts.Length; i++)
            {
                pts[i] = new[] { Cell(poly[i][0]), Cell(poly[i][1]) };
            }

            anchor = pts[0];
            bx0 = by0 = Fix64.MaxValue;
            bx1 = by1 = Fix64.MinValue;
            foreach (var p in pts)
            {
                bx0 = Fix64.Min(bx0, p[0]); by0 = Fix64.Min(by0, p[1]);
                bx1 = Fix64.Max(bx1, p[0]); by1 = Fix64.Max(by1, p[1]);
            }
        }

        int fy0 = ClampCell(by0), fy1 = ClampCell(by1);
        for (int fy = fy0; fy <= fy1; fy++)
        {
            Fix64 py = Fix64.FromInt(fy) + Fix64.HalfValue;
            int nx = 0;
            if (pts == null)
            {
                Fix64 d = Fix64Math.MulExact(r, r) - Fix64Math.MulExact(py - c1, py - c1);
                if (d >= Fix64.Zero)
                {
                    Fix64 h = Fix64Math.SqrtPrecise(d);
                    if (nx + 2 > xs.Length) Array.Resize(ref xs, nx + 2);
                    xs[nx++] = c0 - h;
                    xs[nx++] = c0 + h;
                }
            }
            else
            {
                for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                {
                    Fix64 yi = pts[i][1], yj = pts[j][1];
                    if ((yi > py) != (yj > py))
                    {
                        if (nx == xs.Length) Array.Resize(ref xs, nx * 2);
                        // 128 位先乘后除、只截断一次：先除的截断商会把恰在斜边上的格心推到边外一个最小单位，
                        // 闭合边界漏格。|py−yi| < |yj−yi|，商不超过 |xj−xi|，不会溢出。
                        Int128 num = (Int128)(py - yi).RawValue * (pts[j][0] - pts[i][0]).RawValue;
                        xs[nx++] = pts[i][0] + Fix64.FromRaw((long)(num / (yj - yi).RawValue));
                    }
                }

                Array.Sort(xs, 0, nx);
            }

            for (int k = 0; k + 1 < nx; k += 2)
            {
                int a = Math.Max(0, Fix64.Ceiling(xs[k] - Fix64.HalfValue).ToInt());
                int b = Math.Min(f - 1, Fix64.Floor(xs[k + 1] - Fix64.HalfValue).ToInt());
                for (int fx = a; fx <= b; fx++) cells.Add(fy * f + fx);
            }
        }

        if (cells.Count == 0) cells.Add(ClampCell(anchor[1]) * f + ClampCell(anchor[0]));
        cells.Sort();
        int x0 = f, y0 = f, x1 = -1, y1 = -1;
        int prev = -1;
        var uniq = new List<int>(cells.Count);
        foreach (int cell in cells)
        {
            if (cell == prev) continue;
            prev = cell;
            uniq.Add(cell);
            int x = cell % f, y = cell / f;
            if (x < x0) x0 = x;
            if (x > x1) x1 = x;
            if (y < y0) y0 = y;
            if (y > y1) y1 = y;
        }

        return (uniq.ToArray(), (x0, y0, x1, y1));
    }
}
