namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// CrowdSimulation 单位的仿真身份组件:S4 校验码的整型字段载体。
/// 句柄 = 代(14 位) &lt;&lt; 18 | 槽位(18 位),与参考实现 units.id 同构;
/// state / mode / level / order 本阶段恒为生成初值,随 S5+ 接入运动语义。
/// </summary>
public struct CrowdSimulationUnitState
{
    /// <summary>句柄槽位(0..capacity-1;句柄里还含代,代由管理层的代数组持有)。</summary>
    public int Slot;
    /// <summary>所属导航组(-1 = 未分组)。</summary>
    public int GroupId;
    public byte State;
    public byte Mode;
    public byte Level;
    public uint Order;
    /// <summary>选中标记(交互态,不进校验)。</summary>
    public byte Selected;
}
