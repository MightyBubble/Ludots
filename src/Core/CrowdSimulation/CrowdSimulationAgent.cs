using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation;

/// <summary>
/// 群体单位的运行时体型。速度、个人半径、推挤优先级在模板装载时写成定点数；
/// autoDeploy 为 true 时参与批量部署和随机测试。
/// 档案下标和移动类型下标在生成时写入。profileId 只留在模板里；移动类型取体型的 navProfileId。
/// 与 MassNavigationAgent 互斥。
/// </summary>
public struct CrowdSimulationAgent
{
    public int ProfileIndex;
    public int AgentTypeIndex;
    public Fix64 SpeedCmPerSec;
    public Fix64 PersonalRadiusCm;
    public Fix64 PushPriority;
    /// <summary>true 参与批量部署和随机测试。为 false 的模板只能由生成工具点名。</summary>
    public bool AutoDeploy;
}
