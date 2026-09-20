using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Components
{
    /// <summary>
    /// 局部位姿（attachment 绑定的子实体侧声明）。与 <see cref="ChildOf"/>（GAS 组件边）成对出现：
    /// ChildOf 决定"挂在谁下面"，本组件决定"相对父的位姿如何派生"。
    /// 世界坐标仍是唯一 sim SSOT——本组件不存储派生结果，派生结果每固定步由
    /// AttachmentPositionSyncSystem 写入子实体 WorldPositionCm/PreviousWorldPositionCm。
    /// </summary>
    public struct AttachedLocalPose
    {
        /// <summary>相对父锚点的偏移（厘米）。旋转语义由 <see cref="OffsetRotation"/> 决定。</summary>
        public Fix64Vec2 OffsetCm;

        /// <summary>
        /// 局部朝向（弧度）。继承父朝向时是相对父朝向的增量；否则是独立的初始朝向声明，
        /// 之后实体朝向完全自治（例如独立瞄准的炮塔）。
        /// </summary>
        public Fix64 LocalFacingRad;

        /// <summary>offset 的旋转源：不旋转 / 父朝向 / 自身朝向。</summary>
        public AttachedOffsetRotation OffsetRotation;

        /// <summary>朝向是否跟随父：子朝向 = 父朝向 + <see cref="LocalFacingRad"/>。</summary>
        public byte InheritParentFacing;
    }

    public enum AttachedOffsetRotation : byte
    {
        /// <summary>offset 是固定世界方向偏移（静态父样例）。</summary>
        None = 0,

        /// <summary>offset 随父朝向旋转（炮塔随底盘朝向平移的安装位）。</summary>
        ParentFacing = 1,

        /// <summary>offset 随子自身朝向旋转（环绕锚点，manifestation 前向偏移先例）。</summary>
        OwnFacing = 2,
    }
}
