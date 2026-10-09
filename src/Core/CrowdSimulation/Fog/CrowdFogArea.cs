using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ludots.Core.CrowdSimulation.Fog;

/// <summary>D50 面命令的区域形状(fog/area.js shapeOf 移植):恰一形状,字段校验。</summary>
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
/// 双精度运算与参考端同一 IEEE 序列(厘米域与米域只差统一线性缩放,商与取整逐位同)。</summary>
public static class CrowdFogArea
{
    public static (int[] Cells, (int X0, int Y0, int X1, int Y1) Box) AreaCells(int f, int fcsCm, CrowdFogShape shape)
    {
        int ClampC(double v) => Math.Max(0, Math.Min(f - 1, (int)Math.Floor(v / fcsCm)));
        var cells = new List<int>();
        (int X0, int Y0, int X1, int Y1) box;

        if (shape.Rect is { } rect)
        {
            double ax = Math.Min(rect[0], rect[2]), bx = Math.Max(rect[0], rect[2]);
            double ay = Math.Min(rect[1], rect[3]), by = Math.Max(rect[1], rect[3]);
            box = (ClampC(ax), ClampC(ay), ClampC(bx), ClampC(by));
            for (int y = box.Y0; y <= box.Y1; y++)
            {
                for (int x = box.X0; x <= box.X1; x++) cells.Add(y * f + x);
            }

            return (cells.ToArray(), box);
        }

        double[] xs = new double[64];
        double[] anchor;
        double bx0, by0, bx1, by1;
        double[][]? pts = null;
        if (shape.Circle is { } c)
        {
            anchor = new[] { c[0], c[1] };
            bx0 = c[0] - c[2]; by0 = c[1] - c[2]; bx1 = c[0] + c[2]; by1 = c[1] + c[2];
        }
        else
        {
            pts = shape.Poly!;
            anchor = pts[0];
            bx0 = by0 = double.MaxValue;
            bx1 = by1 = double.MinValue;
            foreach (var p in pts)
            {
                bx0 = Math.Min(bx0, p[0]); by0 = Math.Min(by0, p[1]);
                bx1 = Math.Max(bx1, p[0]); by1 = Math.Max(by1, p[1]);
            }
        }

        int fy0 = ClampC(by0), fy1 = ClampC(by1);
        for (int fy = fy0; fy <= fy1; fy++)
        {
            double py = (fy + 0.5) * fcsCm;
            int nx = 0;
            if (pts == null)
            {
                double r = shape.Circle![2];
                double d = r * r - (py - shape.Circle[1]) * (py - shape.Circle[1]);
                if (d >= 0)
                {
                    double h = Math.Sqrt(d);
                    if (nx + 2 > xs.Length) Array.Resize(ref xs, nx + 2);
                    xs[nx++] = shape.Circle[0] - h;
                    xs[nx++] = shape.Circle[0] + h;
                }
            }
            else
            {
                for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                {
                    double yi = pts[i][1], yj = pts[j][1];
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
                int a = Math.Max(0, (int)Math.Ceiling(xs[k] / fcsCm - 0.5));
                int b = Math.Min(f - 1, (int)Math.Floor(xs[k + 1] / fcsCm - 0.5));
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
