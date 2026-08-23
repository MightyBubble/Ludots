using Arch.Core;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Runtime state for an active displacement effect.
    /// Created by <see cref="BuiltinHandlers.HandleApplyDisplacement"/> and consumed by
    /// <see cref="Systems.DisplacementRuntimeSystem"/>.
    /// </summary>
    public struct DisplacementState
    {
        public Entity TargetEntity;
        public Entity SourceEntity;
        public Entity DirectionTargetEntity;
        public DisplacementDirectionMode DirectionMode;
        /// <summary>Fixed direction in radians (Fix64). Only used when DirectionMode=Fixed.</summary>
        public Fix64 FixedDirectionRad;
        /// <summary>Resolved target point for ToTarget displacement.</summary>
        public Fix64Vec2 TargetPointCm;
        public bool HasTargetPoint;
        /// <summary>Total distance to travel in centimeters.</summary>
        public int TotalDistanceCm;
        /// <summary>Remaining distance in centimeters (Fix64 for sub-tick precision).</summary>
        public Fix64 RemainingDistanceCm;
        /// <summary>Total duration in ticks.</summary>
        public int TotalDurationTicks;
        /// <summary>Remaining ticks.</summary>
        public int RemainingTicks;
        /// <summary>Whether to override navigation input during displacement.</summary>
        public bool OverrideNavigation;
        public bool MovementSuppressionApplied;
        /// <summary>
        /// 目标是 massnav agent 时置位：已向 PoseAuthorityArbiter 申请位移写权窗口
        /// 。窗口在下一个固定步边界生效，生效前不施加位移。
        /// </summary>
        public bool PoseWindowRequested;
        /// <summary>
        /// 叠加位移触发的替换：新位移段已就位，待位移系统在写权确认后刷新窗口时钟。
        /// </summary>
        public bool WindowRefreshRequested;
    }
}
