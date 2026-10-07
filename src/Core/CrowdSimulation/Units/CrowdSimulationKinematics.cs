using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// 单位运动学字段(S5):速度 / 槽位偏移 / 槽位-流场混合度 / 停滞计时 / 休息锚点 / 跳跃状态。
/// 校验码的 vx/vy/slotX/slotY/blend/stall 槽位在 S4 已按参考实现字段序预留,
/// 本阶段起写入真值。全部 Fix64(甲方体系),参考端的 f64 只是导出真值时的换算输入。
/// </summary>
public struct CrowdSimulationKinematics
{
    /// <summary>当前速度(厘米/秒,世界系)。</summary>
    public Fix64Vec2 Velocity;
    /// <summary>阵型槽位在领队局部系中的偏移(厘米:横向,后向)。</summary>
    public Fix64Vec2 SlotOffsetCm;
    /// <summary>槽位↔流场的连续混合度 0..1(防抖动:永不硬切换)。</summary>
    public Fix64 Blend;
    /// <summary>停滞计时(秒):近目标且无进展时累计,超 settleTime 判到达。</summary>
    public Fix64 StallSeconds;
    /// <summary>休息锚点(厘米):ARRIVED 时的驻点,被推离超过唤醒距离则重新进入 MOVING。</summary>
    public Fix64Vec2 RestCm;
    /// <summary>跳跃:落点(厘米;起点即 RestCm,参考实现同)。</summary>
    public Fix64Vec2 JumpToCm;
    /// <summary>跳跃:已行进时间(秒)/ 全长(厘米)。</summary>
    public Fix64 JumpT;
    public Fix64 JumpLengthCm;
}
