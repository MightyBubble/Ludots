namespace Ludots.Core.CrowdSimulation;

/// <summary>
/// 导航区域覆盖（地图实体数据）：把足迹覆盖格的导航区域改为 area，优先级高者胜、同级后放者胜。
/// layered = true 的实体（桥）不改写地面层,而是生成独立的桥面层。
/// </summary>
public struct CrowdSimulationNavArea
{
    public string Area;
    public int Priority;
    public bool Layered;
}
