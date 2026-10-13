using System;
using System.Numerics;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Fields;
using Ludots.Core.Presentation.Rendering;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 群体导航流场的呈现源(服务):当前要可视化的流场与它的几何参数。
/// 谁持有流场谁发布。null = 不呈现。
/// </summary>
public sealed class CrowdFlowFieldVisualSource
{
    public FlowField? Flow { get; set; }
    public int NavCellsPerSide { get; set; }
    public int NavCellSizeCm { get; set; }
}

/// <summary>
/// 流场 → 全场视觉缓冲的投影器(方向图):逐到达格写 Vector4(dirX, dirY, strength, 0),
/// 方向 = 格心指向路点格心的单位向量,强度 = 绷紧长度衰减(越近终点越亮);
/// 颜色映射与贴地绘制是渲染端契约(RaylibFieldRenderPresenter 的 Flow 分支)。
/// </summary>
public sealed class CrowdFlowFieldVisualProjector : IGlobalFieldVisualProjector
{
    private readonly CrowdFlowFieldVisualSource _source;
    private GlobalFieldVisualCell[] _cells = new GlobalFieldVisualCell[4096];
    private IntRect[] _dirty = new IntRect[1];

    public int LayerKeyId { get; set; }
    public bool LimitToRect { get; set; }
    public int MinCellX { get; set; }
    public int MinCellY { get; set; }
    public int MaxCellX { get; set; }
    public int MaxCellY { get; set; }
    public int CellStep { get; set; } = 1;
    public int MaxCells { get; set; } = int.MaxValue;

    public CrowdFlowFieldVisualProjector(CrowdFlowFieldVisualSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public void Project(GlobalFieldVisualBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (LimitToRect)
        {
            ProjectRect(buffer);
            return;
        }

        var flow = _source.Flow;
        int navCellsPerSide = _source.NavCellsPerSide;
        if (flow == null || navCellsPerSide <= 0 || flow.Reached <= 0) return;
        int n2 = navCellsPerSide * navCellsPerSide;
        var inf = Fix64.MaxValue / 4;
        int count = 0;
        if (_cells.Length < flow.Reached) _cells = new GlobalFieldVisualCell[flow.Reached];
        // 流场缓冲本身不携带弹出序,按格号扫一遍(呈现路径,每帧一次)
        for (int u = 0; u < flow.Integ.Length; u++)
        {
            if (flow.Integ[u] >= inf) continue;
            int w = flow.Wp[u];
            if (w < 0) continue;
            int lv = u >= n2 ? 1 : 0, c = u - lv * n2, wc = w % n2;
            int dx = wc % navCellsPerSide - c % navCellsPerSide, dy = wc / navCellsPerSide - c / navCellsPerSide;
            if (dx == 0 && dy == 0) continue;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            float taut = (float)flow.Len[u].ToDouble();
            // 强度:近终点最亮,远终点也有一个可读下限(方向图要看得见整片走廊)
            float strength = Math.Max(0.35f, 1f / (1f + taut * 0.04f));
            _cells[count++] = new GlobalFieldVisualCell(
                new Ludots.Core.Fields.FieldCell2D(c % navCellsPerSide, c / navCellsPerSide),
                new Vector4(dx / len, dy / len, strength, 0f));
        }

        if (count == 0) return;
        _dirty[0] = new IntRect(0, 0, navCellsPerSide, navCellsPerSide);
        var descriptor = new GlobalFieldVisualDescriptor(
            new GlobalFieldVisualId(GlobalFieldVisualKind.Flow, scopeKeyId: 1, layerKeyId: LayerKeyId, surfaceKeyId: 0),
            _source.NavCellSizeCm,
            new WorldCmInt2(0, 0),
            new IntRect(0, 0, navCellsPerSide, navCellsPerSide),
            GlobalFieldVisualValueKind.Vector4);
        buffer.Upsert(in descriptor, _cells.AsSpan(0, count), _dirty);
    }

    private void ProjectRect(GlobalFieldVisualBuffer buffer)
    {
        var flow = _source.Flow;
        int navCellsPerSide = _source.NavCellsPerSide;
        int step = CellStep;
        if (flow == null || navCellsPerSide <= 0 || flow.Reached <= 0 || step < 1) return;
        int x0 = MinCellX < 0 ? 0 : MinCellX;
        int y0 = MinCellY < 0 ? 0 : MinCellY;
        int x1 = MaxCellX > navCellsPerSide ? navCellsPerSide : MaxCellX;
        int y1 = MaxCellY > navCellsPerSide ? navCellsPerSide : MaxCellY;
        if (x1 <= x0 || y1 <= y0) return;
        int spanX = x1 - x0, spanY = y1 - y0;
        int bw = (spanX + step - 1) / step, bh = (spanY + step - 1) / step;
        int cap = MaxCells < bw * bh ? MaxCells : bw * bh;
        if (cap < 1) return;
        if (_cells.Length < cap) _cells = new GlobalFieldVisualCell[cap];
        int n2 = navCellsPerSide * navCellsPerSide;
        var inf = Fix64.MaxValue / 4;
        int count = 0;
        for (int y = y0; y < y1 && count < cap; y += step)
        {
            for (int x = x0; x < x1 && count < cap; x += step)
            {
                int c = y * navCellsPerSide + x;
                bool hit = TryArrow(flow, c, navCellsPerSide, n2, inf, out float dx, out float dy, out float strength);
                if (!hit)
                    hit = TryArrow(flow, c + n2, navCellsPerSide, n2, inf, out dx, out dy, out strength);
                if (!hit) continue;
                _cells[count++] = new GlobalFieldVisualCell(
                    new Ludots.Core.Fields.FieldCell2D((x - x0) / step, (y - y0) / step),
                    new Vector4(dx, dy, strength, 0f));
            }
        }

        if (count == 0) return;
        _dirty[0] = new IntRect(0, 0, bw, bh);
        var descriptor = new GlobalFieldVisualDescriptor(
            new GlobalFieldVisualId(GlobalFieldVisualKind.Flow, scopeKeyId: 1, layerKeyId: LayerKeyId, surfaceKeyId: 0),
            _source.NavCellSizeCm * step,
            new WorldCmInt2(x0 * _source.NavCellSizeCm, y0 * _source.NavCellSizeCm),
            new IntRect(0, 0, bw, bh),
            GlobalFieldVisualValueKind.Vector4);
        buffer.Upsert(in descriptor, _cells.AsSpan(0, count), _dirty);
    }

    private static bool TryArrow(FlowField flow, int u, int navCellsPerSide, int n2, Fix64 inf, out float dx, out float dy, out float strength)
    {
        dx = dy = strength = 0f;
        if ((uint)u >= (uint)flow.Integ.Length || flow.Integ[u] >= inf) return false;
        int w = flow.Wp[u];
        if (w < 0) return false;
        int c = u >= n2 ? u - n2 : u;
        int wc = w % n2;
        int ddx = wc % navCellsPerSide - c % navCellsPerSide;
        int ddy = wc / navCellsPerSide - c / navCellsPerSide;
        if (ddx == 0 && ddy == 0) return false;
        float len = MathF.Sqrt(ddx * ddx + ddy * ddy);
        float taut = (float)flow.Len[u].ToDouble();
        dx = ddx / len;
        dy = ddy / len;
        strength = Math.Max(0.35f, 1f / (1f + taut * 0.04f));
        return true;
    }
}
