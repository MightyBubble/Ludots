using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation;

/// <summary>
/// CrowdSimulation 单位身份组件（LU-06）。速度、个人半径和推挤优先级写在单位模板上；
/// 半径厘米只在 Navigation/agent_profiles.json。与 MassNavigationAgent 互斥。
/// </summary>
public struct CrowdSimulationAgent
{
    public string ProfileId;
    public string AgentTypeId;
    public Fix64 SpeedCmPerSecond;
    public Fix64 PersonalRadiusCm;
    public Fix64 PushPriority;
    /// <summary>不参与批量部署，只能由生成工具点名。同一移动类型的模板必须一致。</summary>
    public bool Special;
}
