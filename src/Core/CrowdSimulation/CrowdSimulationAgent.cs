using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation;

/// <summary>
/// 群体单位的运行时体型。速度、个人半径、推挤优先级和 special 在模板装载时写成定点数；
/// 档案下标和移动类型下标在生成时写入。profileId / agentTypeId 只留在模板和配置里。
/// 与 MassNavigationAgent 互斥。
/// </summary>
public struct CrowdSimulationAgent
{
    public int ProfileIndex;
    public int AgentTypeIndex;
    public Fix64 SpeedCmPerSecond;
    public Fix64 PersonalRadiusCm;
    public Fix64 PushPriority;
    /// <summary>true 不参与批量部署。为 true 的模板只能由生成工具点名。</summary>
    public bool Special;
}
