namespace Ludots.Core.CrowdSimulation;

/// <summary>
/// 地图实体上的条带足迹：两端点连线 + 宽度（厘米）。条带不改写底层地形，
/// 它在哪一层、铺什么区域由 <see cref="CrowdSimulationStructure"/> 点名的结构模板决定。
/// </summary>
public struct CrowdSimulationPathSpan
{
    public int FromXCm;
    public int FromYCm;
    public int ToXCm;
    public int ToYCm;
    public int WidthCm;
}
