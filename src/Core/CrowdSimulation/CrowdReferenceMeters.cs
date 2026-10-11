using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Spatial;

namespace Ludots.Core.CrowdSimulation;

/// <summary>
/// 参考端米制求值树的换算口。迷雾面、视线和桥面烘焙的计算留在原处，
/// 这里只固定厘米进米的那一步。几种除法不能互换：Fix64/Fix64 是精确除，
/// Fix64/int 是原始值整数除，double 与先截断的 int 各走各的舍入。
/// </summary>
public static class CrowdReferenceMeters
{
    public const int CentimetersPerMeter = SpatialScaleDefaults.CentimetersPerMeter;

    /// <summary>Fix64 厘米 ÷ Fix64 每米厘米数（精确除）。视线眼高与格高。</summary>
    public static Fix64 CmToMPrecise(int centimeters)
        => Fix64.FromInt(centimeters) / Fix64.FromInt(CentimetersPerMeter);

    /// <summary>Fix64 厘米 ÷ Fix64 每米厘米数（精确除）。</summary>
    public static Fix64 CmToMPrecise(Fix64 centimeters)
        => centimeters / Fix64.FromInt(CentimetersPerMeter);

    /// <summary>Fix64.FromInt 之后按 int 除原始值。桥面端点与格心。</summary>
    public static Fix64 CmToMRaw(int centimeters)
        => Fix64.FromInt(centimeters) / CentimetersPerMeter;

    /// <summary>半宽。除数是每米厘米数的两倍，一次 Fix64/int 除尽，不拆成先除再除。</summary>
    public static Fix64 HalfWidthM(int widthCm)
        => Fix64.FromInt(widthCm) / (CentimetersPerMeter * 2);

    /// <summary>先按 int 截断到米，再装进 Fix64。桥头可达距离。</summary>
    public static Fix64 TruncatedCmToM(int centimeters)
        => Fix64.FromInt(centimeters / CentimetersPerMeter);

    /// <summary>double 厘米 / 每米厘米数。迷雾形状入网前的米值。</summary>
    public static double CmToM(double centimeters)
        => centimeters / CentimetersPerMeter;

    /// <summary>double 厘米换米后再 FromDouble。迷雾形状的格坐标。</summary>
    public static Fix64 CmToMFix(double centimeters)
        => Fix64.FromDouble(centimeters / CentimetersPerMeter);

    /// <summary>
    /// 点名生成的散布半径（导航格）。两个 /100 都是 Fix64/int，约掉会改原始值。
    /// </summary>
    public static Fix64 SpawnSpreadNavCells(int count, Fix64 personalRadiusCm, Fix64 groupSpacingScale, Fix64 navCellSizeCm)
        => Fix64Math.Sqrt(Fix64.FromInt(count)) * personalRadiusCm / CentimetersPerMeter * groupSpacingScale / (navCellSizeCm / CentimetersPerMeter);
}
