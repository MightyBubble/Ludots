namespace Ludots.Core.CrowdSimulation;

/// <summary>
/// 地图抽取只读桥跨度实体的 area 字符串。priority / layered 写入组件后，地表路径不读。
/// 桥面格谁覆盖谁由桥列表顺序决定。
/// </summary>
public struct CrowdSimulationNavArea
{
    public string Area;
    public int Priority;
    public bool Layered;
}
