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

/// <summary>形状 → fog 格(fog/area.js areaCells 移植,Fix64):rect 保守覆盖(触到的每一格);
/// circle / poly 按格心在内(行中心线上的半宽 / 偶奇扫描线);比一格还小的形状仍覆盖锚点格
/// (circle 心 / poly 首顶点)——点击大小的形状不是静默空操作。格升序去重 + 格包围盒。
/// 逐 op 与参考端 __L31_FIX64_LOS__ 补丁同一棵求值树:形状数字经 FromDouble(向零截断)
/// 入网格,除法精确商向零截断,乘法精确积向下取整,圆半宽开方走 SqrtPrecise(整数位法,
/// 可被参考端 BigInt 逐位复现)。命令在 tick 内执行,tick 内一律定点。</summary>
public static class CrowdFogArea
{
    public static (int[] Cells, (int X0, int Y0, int X1, int Y1) Box) AreaCells(int f, int fcsCm, CrowdFogShape shape)
    {
        Fix64 cs = Fix64.FromInt(fcsCm);
        int ClampC(Fix64 v) => Math.Max(0, Math.Min(f - 1, Fix64.Floor(v / cs).ToInt()));
        var cells = new List<int>();
        (int X0, int Y0, int X1, int Y1) box;

        if (shape.Rect is { } rect)
        {
            Fix64 ax = Fix64.Min(Fix64.FromDouble(rect[0]), Fix64.FromDouble(rect[2]));
            Fix64 bx = Fix64.Max(Fix64.FromDouble(rect[0]), Fix64.FromDouble(rect[2]));
            Fix64 ay = Fix64.Min(Fix64.FromDouble(rect[1]), Fix64.FromDouble(rect[3]));
            Fix64 by = Fix64.Max(Fix64.FromDouble(rect[1]), Fix64.FromDouble(rect[3]));
            box = (ClampC(ax), ClampC(ay), ClampC(bx), ClampC(by));
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
        if (shape.Circle is { } c)
        {
            Fix64 c0 = Fix64.FromDouble(c[0]), c1 = Fix64.FromDouble(c[1]), r = Fix64.FromDouble(c[2]);
            anchor = new[] { c0, c1 };
            bx0 = c0 - r; by0 = c1 - r; bx1 = c0 + r; by1 = c1 + r;
        }
        else
        {
            pts = new Fix64[shape.Poly!.Length][];
            for (int i = 0; i < pts.Length; i++)
            {
                pts[i] = new[] { Fix64.FromDouble(shape.Poly[i][0]), Fix64.FromDouble(shape.Poly[i][1]) };
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

        int fy0 = ClampC(by0), fy1 = ClampC(by1);
        for (int fy = fy0; fy <= fy1; fy++)
        {
            Fix64 py = (Fix64.FromInt(fy) + Fix64.HalfValue) * cs;
            int nx = 0;
            if (pts == null)
            {
                Fix64 c0 = Fix64.FromDouble(shape.Circle![0]), c1 = Fix64.FromDouble(shape.Circle[1]);
                Fix64 r = Fix64.FromDouble(shape.Circle[2]);
                Fix64 d = r * r - (py - c1) * (py - c1);
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
                        xs[nx++] = pts[i][0] + (py - yi) * (pts[j][0] - pts[i][0]) / (yj - yi);
                    }
                }

                Array.Sort(xs, 0, nx);
            }

            for (int k = 0; k + 1 < nx; k += 2)
            {
                int a = Math.Max(0, Fix64.Ceiling(xs[k] / cs - Fix64.HalfValue).ToInt());
                int b = Math.Min(f - 1, Fix64.Floor(xs[k + 1] / cs - Fix64.HalfValue).ToInt());
                for (int fx = a; fx <= b; fx++) cells.Add(fy * f + fx);
            }
        }

        if (cells.Count == 0) cells.Add(ClampC(anchor[1]) * f + ClampC(anchor[0]));
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
