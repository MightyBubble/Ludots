using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// 单位运动学字段(S5):速度 / 槽位偏移 / 槽位-流场混合度 / 停滞计时 / 休息锚点 / 跳跃状态。
/// 校验码按 vx、vy、slotX、slotY、blend、stall 的顺序读这些字段。全部是 Fix64。
/// </summary>
public struct CrowdSimulationKinematics
{
    /// <summary>当前速度(厘米/秒,世界系)。</summary>
    public Fix64Vec2 Velocity;
    /// <summary>阵型槽位在领队局部系中的偏移(厘米:横向,后向)。</summary>
    public Fix64Vec2 SlotOffsetCm;
    /// <summary>槽位与流场的混合度，0..1。意图步按 BStep 加减。跳跃落地和重新规划会写成 0。</summary>
    public Fix64 Blend;
    /// <summary>停滞计时(秒):近目标且无进展时累计,超 settleTime 判到达。</summary>
    public Fix64 StallSeconds;
    /// <summary>休息锚点(厘米):ARRIVED 时的驻点,被推离超过唤醒距离则重新进入 MOVING。</summary>
    public Fix64Vec2 RestCm;
    /// <summary>跳跃落点，厘米。起点是 RestCm。</summary>
    public Fix64Vec2 JumpToCm;
    /// <summary>跳跃:已行进时间(秒)/ 全长(厘米)。</summary>
    public Fix64 JumpT;
    public Fix64 JumpLengthCm;
}
