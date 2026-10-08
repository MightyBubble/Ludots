using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>
/// 流场连续采样(flowSample.js 移植,Fix64 厘米域):
/// routeLength = 从精确位置到终点的绷紧路线长(格)——所有到达阈值共用的长度单位,
/// 量化量与连续量不混用;不可达 = Fix64.MaxValue(参考实现 Infinity 的哨兵约定,S3-c 沿用)。
/// 流场数组分层:下标 = 层 × N² + 格(0 地面 / 1 桥面);路点可指另一层(桥头跨层)。
/// </summary>
public static class CrowdFlowSample
{
    public static readonly Fix64 Unreachable = Fix64.MaxValue;

    /// <summary>从 (pxCm,pyCm) 所在格到终点的连续路线长(格):路点绷紧长 + 到路点的直线距离。</summary>
    public static Fix64 RouteLength(FlowField flow, int n, int cellSizeCm, Fix64 pxCm, Fix64 pyCm, int cell, int lv)
    {
        int n2 = n * n;
        Fix64 l = flow.Len[lv * n2 + cell];
        if (l >= Unreachable / 4) return Unreachable;
        int t = flow.Wp[lv * n2 + cell];
        if (t < 0) return l;
        int tc = t % n2;
        Fix64 cs = Fix64.FromInt(cellSizeCm);
        Fix64 dx = pxCm / cs - (Fix64.FromInt(tc % n) + Fix64.HalfValue);
        Fix64 dy = pyCm / cs - (Fix64.FromInt(tc / n) + Fix64.HalfValue);
        return flow.Len[t] + Fix64Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// 连续流场方向:按单位在 4 个格心间的位置双线性混合各格指向其路点的方向,
    /// 跨界不打折。写出单位方向;返回路线长(MaxValue = 场外)。
    /// </summary>
    public static Fix64 SampleFlow(
        FlowField flow, byte[] pass, int n, int cellSizeCm,
        Fix64 pxCm, Fix64 pyCm, int cell, out Fix64 dirX, out Fix64 dirY, int lv = 0)
    {
        int n2 = n * n, o = lv * n2;
        dirX = Fix64.Zero;
        dirY = Fix64.Zero;
        if (flow.Len[o + cell] >= Unreachable / 4) return Unreachable;
        Fix64 cs = Fix64.FromInt(cellSizeCm);
        Fix64 fx = pxCm / cs - Fix64.HalfValue, fy = pyCm / cs - Fix64.HalfValue;
        int x0 = (int)Fix64.Floor(fx).ToLong(), y0 = (int)Fix64.Floor(fy).ToLong();
        Fix64 tx = fx - Fix64.FromInt(x0), ty = fy - Fix64.FromInt(y0);
        Fix64 sx = Fix64.Zero, sy = Fix64.Zero;
        for (int k = 0; k < 4; k++)
        {
            int cx = x0 + (k & 1), cy = y0 + (k >> 1);
            if (cx < 0 || cy < 0 || cx >= n || cy >= n) continue;
            int c = cy * n + cx;
            if (pass[c] == 0 || flow.Integ[o + c] >= Unreachable / 4) continue;
            int t = flow.Wp[o + c];
            if (t < 0) continue;
            int tc = t % n2;
            Fix64 wx = (Fix64.FromInt(tc % n) + Fix64.HalfValue) * cs - pxCm;
            Fix64 wy = (Fix64.FromInt(tc / n) + Fix64.HalfValue) * cs - pyCm;
            // 拉直路点可能远在数十格外,厘米平方必炸(Q32.32 界约 463m)——归一化 hypot,见 CrowdFix。
            Fix64 l = CrowdFix.Hypot(wx, wy);
            if (l < Fix64.FromFloat(1e-3f)) continue;
            Fix64 w = ((k & 1) != 0 ? tx : Fix64.OneValue - tx) * ((k >> 1) != 0 ? ty : Fix64.OneValue - ty);
            sx += (wx / l) * w;
            sy += (wy / l) * w;
        }

        Fix64 m = Fix64Math.Sqrt(sx * sx + sy * sy);
        if (m > Fix64.FromFloat(1e-6f))
        {
            dirX = sx / m;
            dirY = sy / m;
        }

        return RouteLength(flow, n, cellSizeCm, pxCm, pyCm, cell, lv);
    }
}
