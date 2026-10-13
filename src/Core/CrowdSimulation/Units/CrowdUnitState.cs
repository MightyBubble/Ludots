namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>单位移动状态机。枚举序稳定，进校验码。</summary>
public enum CrowdUnitState : byte
{
    Idle = 0,
    Moving = 1,
    Arrived = 2,
    Unreachable = 3,
    Jump = 4,
}
