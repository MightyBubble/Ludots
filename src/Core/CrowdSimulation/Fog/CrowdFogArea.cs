using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Fog;

/// <summary>面命令的区域形状(fog/area.js shapeOf 移植):恰一形状,字段校验。</summary>
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

    /// <summary>从指令 JSON 解析恰一形状(与参考端 shapeOf 同报错口径)。</summary>
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

/// <summary>形状 → fog 格(fog/area.js areaCells 移植):rect 保守覆盖(触到的每一格);
/// circle / poly 按格心在内(行中心线上的半宽 / 偶奇扫描线);比一格还小的形状仍覆盖锚点格
/// (circle 心 / poly 首顶点)——点击大小的形状不是静默空操作。格升序去重 + 格包围盒。
/// 求值树与参考端 __L31_FIX64_LOS__ 补丁同一棵、逐 op 对齐:坐标以米入网(FromDouble
/// 向零截断)后先除以格长归一到格单位(CrowdFix 先归一同手法)——平方与跨度积都落在
/// ≤ 格数的量级,乘法走 MulExact(精确积)、除法精确商、开方 SqrtPrecise,两端逐位可复现。
/// 厘米/米域直乘的溢出界只有 ~463 m(L51):千米级圆/多边形在旧树里静默回绕,现以
/// ±46340 m 输入守卫显式抛错(与参考端同文案)。rect 无乘法:米入网后除以格长再 floor。命令在 tick 内执行,
/// tick 内一律定点。</summary>
public static class CrowdFogArea
{
    /// <summary>米域输入守卫:circle 半径/圆心与 poly 顶点超出即拒——Q31.32 平方安全界,
    /// 回绕不许静默发生(rect 无平方不设守卫,越图仍走 clamp)。</summary>
    private const double MaxShapeMeters = 46340.0;

    private static void RequireShapeMeters(double[] values)
    {
        foreach (double v in values)
        {
            if (!(Math.Abs(v) / 100.0 <= MaxShapeMeters)) // 载荷是厘米,守卫以米计
            {
                throw new InvalidOperationException(
                    $"迷雾区域形状坐标超出 ±{MaxShapeMeters} 米平方安全界——拒绝静默回绕。");
            }
        }
    }

    public static (int[] Cells, (int X0, int Y0, int X1, int Y1) Box) AreaCells(int f, int fcsCm, CrowdFogShape shape)
    {
        Fix64 cs = Fix64.FromDouble(fcsCm / 100.0);
        int ClampCell(Fix64 v) => Math.Max(0, Math.Min(f - 1, Fix64.Floor(v).ToInt()));
        Fix64 Cell(double cm) => Fix64.FromDouble(cm / 100.0) / cs;
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

        var xs = new Fix64[64];
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
                        // 先除后乘(归一):t ∈ (0,1),跨度积回到小值域
                        Fix64 t = (py - yi) / (yj - yi);
                        xs[nx++] = pts[i][0] + Fix64Math.MulExact(t, pts[j][0] - pts[i][0]);
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
