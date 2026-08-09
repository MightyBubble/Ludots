using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Components
{
    /// <summary>
    /// 移动意图模式。意图不是位置真相；执行器只读消费。
    /// </summary>
    public enum MoveIntentMode : byte
    {
        None = 0,
        Direction = 1,
        TargetPoint = 2,
    }

    /// <summary>
    /// 移动意图（想怎么移动）。由控制/订单/AI 写入；Nav / Motor / Physics 驱动只读。
    /// </summary>
    public struct MoveIntent
    {
        public MoveIntentMode Mode;

        /// <summary>Direction 模式：逻辑平面偏航（弧度，0 = +X）。</summary>
        public float DirectionRad;

        /// <summary>TargetPoint 模式：世界厘米目标点。</summary>
        public Fix64Vec2 TargetWorldCm;

        /// <summary>期望速率（cm/s）。None 模式必须为 0；其余模式必须 &gt; 0。</summary>
        public float DesiredSpeedCmPerSec;
    }

    /// <summary>
    /// 面朝意图模式。与移动意图正交：可侧移同时面朝另一方向。
    /// </summary>
    public enum FacingIntentMode : byte
    {
        None = 0,
        ExplicitYaw = 1,
        FollowMoveDirection = 2,
    }

    /// <summary>
    /// 身体面朝意图（想面朝哪）。不是 <see cref="FacingDirection"/> 客观朝向。
    /// </summary>
    public struct FacingIntent
    {
        public FacingIntentMode Mode;

        /// <summary>ExplicitYaw 模式：目标身体偏航（弧度，0 = +X）。</summary>
        public float YawRad;
    }

    /// <summary>
    /// 意图组件合同校验（fail-fast，无静默纠正）。
    /// </summary>
    public static class MovementIntentRules
    {
        public static void Validate(in MoveIntent intent)
        {
            switch (intent.Mode)
            {
                case MoveIntentMode.None:
                    if (intent.DesiredSpeedCmPerSec != 0f)
                    {
                        throw new System.InvalidOperationException(
                            "MoveIntent mode None requires DesiredSpeedCmPerSec == 0.");
                    }

                    break;
                case MoveIntentMode.Direction:
                    RequireFinite(intent.DirectionRad, "MoveIntent.DirectionRad");
                    RequirePositiveSpeed(intent.DesiredSpeedCmPerSec);
                    break;
                case MoveIntentMode.TargetPoint:
                    RequireFinite(intent.TargetWorldCm.X.ToFloat(), "MoveIntent.TargetWorldCm.X");
                    RequireFinite(intent.TargetWorldCm.Y.ToFloat(), "MoveIntent.TargetWorldCm.Y");
                    RequirePositiveSpeed(intent.DesiredSpeedCmPerSec);
                    break;
                default:
                    throw new System.InvalidOperationException(
                        $"MoveIntent mode {(byte)intent.Mode} is not configured.");
            }
        }

        public static void Validate(in FacingIntent intent)
        {
            switch (intent.Mode)
            {
                case FacingIntentMode.None:
                case FacingIntentMode.FollowMoveDirection:
                    break;
                case FacingIntentMode.ExplicitYaw:
                    RequireFinite(intent.YawRad, "FacingIntent.YawRad");
                    break;
                default:
                    throw new System.InvalidOperationException(
                        $"FacingIntent mode {(byte)intent.Mode} is not configured.");
            }
        }

        private static void RequirePositiveSpeed(float speedCmPerSec)
        {
            if (!(speedCmPerSec > 0f) || !float.IsFinite(speedCmPerSec))
            {
                throw new System.InvalidOperationException(
                    "MoveIntent DesiredSpeedCmPerSec must be finite and > 0 for Direction/TargetPoint modes.");
            }
        }

        private static void RequireFinite(float value, string name)
        {
            if (!float.IsFinite(value))
            {
                throw new System.InvalidOperationException($"{name} must be finite.");
            }
        }
    }
}
