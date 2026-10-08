using System;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation;

/// <summary>
/// CrowdSimulation 单位身份组件（LU-06）：指向 Navigation/agent_profiles.json 中的
/// 代理体型（移动类型 × 半径级），并携带模板写真的仿真参数。与 MassNavigationAgent
/// 互斥——同一模板只能带其中一个。
/// 模板可以只写部分字段，缺省字段在单位生成时按 config 默认值补全
/// （配置分层：unitTypes/agentTypes 是默认值层，不是兼容路径）；补全后仿真只读本组件。
/// </summary>
public struct CrowdSimulationAgent
{
    public string ProfileId;
    /// <summary>移动类型 id（模板声明；null = 未声明，按 unitTypes[].agentType 补全）。</summary>
    public string? AgentType;
    /// <summary>半径级（厘米口径；null = 未声明，按 profile 的 radiusCm 补全）。</summary>
    public int? RadiusClassCm;
    public Fix64? SpeedCmPerSecond;
    public Fix64? RadiusCm;
    public Fix64? PersonalRadiusCm;

    /// <summary>补全后的仿真速度；生成钩子未补全即生成路径有 bug，fail-fast。</summary>
    public Fix64 ResolvedSpeed => SpeedCmPerSecond
        ?? throw new InvalidOperationException($"单位 {ProfileId} 的仿真参数不完整：缺 speedCmPerSecond（生成钩子未补全）。");

    /// <summary>补全后的个人（避让/碰撞）半径；生成钩子未补全即生成路径有 bug，fail-fast。</summary>
    public Fix64 ResolvedPersonalRadiusCm => PersonalRadiusCm
        ?? throw new InvalidOperationException($"单位 {ProfileId} 的仿真参数不完整：缺 personalRadiusCm（生成钩子未补全）。");
}
