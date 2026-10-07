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
/// 谁持有流场谁发布(演示 Mod / 未来的规划器);null = 不呈现。
/// </summary>
public sealed class CrowdFlowFieldVisualSource
{
    public FlowField? Flow { get; set; }
    public int CellCount { get; set; }
    public int CellSizeCm { get; set; }
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

    public CrowdFlowFieldVisualProjector(CrowdFlowFieldVisualSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public void Project(GlobalFieldVisualBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var flow = _source.Flow;
        int n = _source.CellCount;
        if (flow == null || n <= 0 || flow.Reached <= 0) return;
        int n2 = n * n;
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
            int dx = wc % n - c % n, dy = wc / n - c / n;
            if (dx == 0 && dy == 0) continue;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            float taut = (float)flow.Len[u].ToDouble();
            // 强度:近终点最亮,远终点也有一个可读下限(方向图要看得见整片走廊)
            float strength = Math.Max(0.35f, 1f / (1f + taut * 0.04f));
            _cells[count++] = new GlobalFieldVisualCell(
                new Ludots.Core.Fields.FieldCell2D(c % n, c / n),
                new Vector4(dx / len, dy / len, strength, 0f));
        }

        if (count == 0) return;
        _dirty[0] = new IntRect(0, 0, n, n);
        var descriptor = new GlobalFieldVisualDescriptor(
            new GlobalFieldVisualId(GlobalFieldVisualKind.Flow, scopeKeyId: 1, layerKeyId: 0, surfaceKeyId: 0),
            _source.CellSizeCm,
            new WorldCmInt2(0, 0),
            new IntRect(0, 0, n, n),
            GlobalFieldVisualValueKind.Vector4);
        buffer.Upsert(in descriptor, _cells.AsSpan(0, count), _dirty);
    }
}
