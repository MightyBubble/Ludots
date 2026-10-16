using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>
/// 接触法线单位化。厘米定点下 d² 截断了低位，Sqrt(dx²) 回不到 |dx|，贴轴接触会少推一点；
/// 盒内最浅轴退出按 ex &lt; ey 判定，这点偏差会把平局翻到另一轴。贴轴时法线精确取 ±1。
/// </summary>
internal static class CrowdContactNormal
{
    public static void Normalize(ref Fix64 dx, ref Fix64 dy, Fix64 d2)
    {
        if (dy == Fix64.Zero)
        {
            dx = dx > Fix64.Zero ? Fix64.OneValue : -Fix64.OneValue;
            return;
        }

        if (dx == Fix64.Zero)
        {
            dy = dy > Fix64.Zero ? Fix64.OneValue : -Fix64.OneValue;
            return;
        }

        Fix64 d = Fix64Math.Sqrt(d2);
        dx /= d;
        dy /= d;
    }
}
