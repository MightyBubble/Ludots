namespace Ludots.Core.CrowdSimulation;

/// <summary>
/// 桥面跨度（地图实体数据）：两点连线 + 宽度。桥实体不改写地面地形，
/// 桥面层（deck）的导航语义由分层烘焙接管；此处只是数据与呈现输入。
/// </summary>
public struct CrowdSimulationBridgeSpan
{
    public int X0Cm;
    public int Y0Cm;
    public int X1Cm;
    public int Y1Cm;
    public int WidthCm;
}
