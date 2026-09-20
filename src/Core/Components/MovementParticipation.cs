namespace Ludots.Core.Components
{
    /// <summary>
    /// 物理存在轴（参与模型轴二，authoring 声明，模板级数据）。
    /// 描述物理引擎如何看待该实体，与"谁写位置"（<see cref="PoseAuthority"/>）正交。
    /// </summary>
    public enum PhysicsPresenceKind : byte
    {
        /// <summary>物理宽相里没有该实体。</summary>
        None = 1,

        /// <summary>物理里有碰撞体：产生碰撞/触发事件、推得动 dynamic 体、自己不被物理推动。</summary>
        Kinematic = 2,

        /// <summary>完整刚体，参与积分与求解。</summary>
        Dynamic = 3,
    }

    /// <summary>
    /// 移动参与 authoring 明文（参与模型）。模板级声明，缺字段 fail-fast。
    /// 初始位姿写权由 <see cref="PhysicsPresence"/> 推导（None/Kinematic → Nav；Dynamic → Physics），
    /// 见 <see cref="MovementParticipationRules.DeriveInitialPoseAuthority"/>。
    /// </summary>
    public struct MovementParticipation
    {
        public PhysicsPresenceKind PhysicsPresence;

        /// <summary>该实体是否允许进入 GAS 位移写权窗口。</summary>
        public bool DisplacementAllowed;

        /// <summary>位移速度低于该阈值时窗口结束并交还写权（cm/s，必须 &gt; 0）。</summary>
        public float DisplacementHandbackSpeedThresholdCmPerSec;

        /// <summary>位移写权窗口的兜底上限（毫秒，必须 &gt; 0）；超时 fail-fast 抛异常。</summary>
        public int DisplacementMaxDurationMs;
    }

    /// <summary>
    /// 位姿写权轴（参与模型轴一，运行时状态，不是 authoring 字段）。
    /// 每个固定步只有一个写权持有者产出该实体的最终位姿。
    /// </summary>
    public enum PoseAuthorityKind : byte
    {
        /// <summary>MassNavigation 求解器产出位姿结果。</summary>
        Nav = 1,

        /// <summary>GAS 位移窗口驱动位姿（瞬态，窗口结束必须交还）。</summary>
        Displacement = 2,

        /// <summary>Physics2D 积分产出位姿结果。</summary>
        Physics = 3,

        /// <summary>Attachment 绑定持有位姿（attach 授予 / detach 归还，无窗口时限）。
        /// 子实体位姿由 AttachmentPositionSyncSystem 从父位姿派生。</summary>
        Attached = 4,
    }

    /// <summary>
    /// 运行时位姿写权状态。只随 <see cref="MovementParticipation"/> 一起出现；
    /// 切换只允许在固定步边界经 CommandBuffer 结算（PoseAuthorityCommitSystem）。
    /// </summary>
    public struct PoseAuthority
    {
        public PoseAuthorityKind Value;
    }

    /// <summary>
    /// 参与模型的推导规则单点（SSOT），供 authoring 解析与运行时绑定共同使用。
    /// </summary>
    public static class MovementParticipationRules
    {
        public static PoseAuthorityKind DeriveInitialPoseAuthority(PhysicsPresenceKind presence)
        {
            return presence switch
            {
                PhysicsPresenceKind.None => PoseAuthorityKind.Nav,
                PhysicsPresenceKind.Kinematic => PoseAuthorityKind.Nav,
                PhysicsPresenceKind.Dynamic => PoseAuthorityKind.Physics,
                _ => throw new System.InvalidOperationException(
                    $"MovementParticipation physicsPresence value {(byte)presence} has no configured initial pose authority."),
            };
        }
    }
}
