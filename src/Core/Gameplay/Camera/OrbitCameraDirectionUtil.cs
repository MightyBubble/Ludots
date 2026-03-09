using System;
using System.Numerics;

namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// 与 CameraPresenter 一致的 Orbit 相机方向约定。
    /// TargetCm: X=worldX, Y=worldZ。Yaw 0 = 朝向 +Z。
    /// 供 Core 相机行为管线、CameraPresenter 等复用。
    /// </summary>
    public static class OrbitCameraDirectionUtil
    {
        /// <summary>
        /// 视线方向（TargetCm 空间）。与 CameraPresenter 的 view dir = (-sin, 0, cos) 对应。
        /// </summary>
        public static Vector2 ForwardFromYawDegrees(float yawDeg)
        {
            float rad = yawDeg * (MathF.PI / 180f);
            return new Vector2(-MathF.Sin(rad), MathF.Cos(rad));
        }

        /// <summary>
        /// 屏幕右侧方向（TargetCm 空间）。right = forward × up，与 CameraPresenter 一致。
        /// </summary>
        public static Vector2 RightFromYawDegrees(float yawDeg)
        {
            float rad = yawDeg * (MathF.PI / 180f);
            return new Vector2(-MathF.Cos(rad), -MathF.Sin(rad));
        }

        /// <summary>
        /// 将 WASD 输入 (move.X=右-左, move.Y=上-下) 转为 TargetCm 空间平移方向。
        /// </summary>
        public static Vector2 MoveInputToDirection(float yawDeg, Vector2 move)
        {
            Vector2 fwd = ForwardFromYawDegrees(yawDeg);
            Vector2 right = RightFromYawDegrees(yawDeg);
            Vector2 dir = fwd * move.Y + right * move.X;
            return dir.LengthSquared() > 0.0001f ? Vector2.Normalize(dir) : Vector2.Zero;
        }
    }
}
