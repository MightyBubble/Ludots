using Arch.Core;
using Ludots.Core.Gameplay.GAS;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Effect生命周期状态
    /// </summary>
    public enum EffectState : byte
    {
        Created = 0,    // 已创建
        Pending = 1,    // 等待响应链
        Trigger = 2,    // 响应链处理中
        Calculate = 3,  // 计算最终值
        Apply = 4,      // 应用中
        Committed = 5   // 已提交
    }

    public struct GameplayEffect
    {
        public EffectLifetimeKind LifetimeKind;
        public GasClockId ClockId;
        public int TotalTicks;
        public int RemainingTicks;
        public int PeriodTicks;
        public int NextTickAtTick;
        public int ExpiresAtTick;
        public GasConditionHandle ExpireCondition;

        public byte Flags;

        /// <summary>
        /// Effect状态（使用Flags的bit 1-3，共3位，支持0-7）
        /// </summary>
        public EffectState State
        {
            readonly get => (EffectState)((Flags >> 1) & 0x07);
            set => Flags = (byte)((Flags & 0xF1) | ((byte)value << 1));
        }
    }
}
