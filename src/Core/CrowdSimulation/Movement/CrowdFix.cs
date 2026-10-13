using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>
/// 厘米域 hypot:先按 max(|x|,|y|) 归一再开方。
/// Q31.32 平方约在 463 m 溢出,直接平方会炸。
/// </summary>
public static class CrowdFix
{
    public static Fix64 Hypot(Fix64 x, Fix64 y)
    {
        Fix64 ax = Fix64.Abs(x), ay = Fix64.Abs(y);
        Fix64 m = ax > ay ? ax : ay;
        if (m == Fix64.Zero) return Fix64.Zero;
        Fix64 rx = x / m, ry = y / m;
        return m * Fix64Math.Sqrt(rx * rx + ry * ry);
    }
}
