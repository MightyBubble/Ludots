namespace Ludots.Core.CrowdSimulation;

/// <summary>
/// CrowdSimulation 单位身份组件（LU-06）：指向 Navigation/agent_profiles.json 中的
/// 代理体型（移动类型 × 半径级）。与 MassNavigationAgent 互斥——同一模板只能带其中一个。
/// </summary>
public struct CrowdSimulationAgent
{
    public string ProfileId;
}
