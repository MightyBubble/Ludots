using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Movement;

/// <summary>
/// 厘米域 hypot(detHypot 的甲方等价):先按 max(|x|,|y|) 归一再开方——
/// Fix64(Q32.32)平方的溢出界约 463 m,地图距离以千米计,直接平方必然炸。
/// 语义与参考实现 detHypot 一致(精确值,不近似),只是中间量换了表达。
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

    /// <summary>平方的替代:距离比较直接对 max(|x|,|y|) 与限值比(需要平方时才用 Hypot)。</summary>
    public static Fix64 DistSq(Fix64 x, Fix64 y)
    {
        Fix64 h = Hypot(x, y);
        return h * h;
    }
}
