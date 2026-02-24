using System;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;

namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Stateless logic for updating camera state based on input.
    /// This runs in the gameplay loop and is platform-agnostic.
    /// </summary>
    public static class CameraLogic
    {
        // Sensitivity constants - could be moved to a config later
        public const float ROTATION_SPEED = 2.0f;
        public const float ZOOM_SPEED = 50.0f;
        public const int MOVE_THRESHOLD = 10;

        /// <summary>
        /// Updates the camera state based on input frame.
        /// </summary>
        /// <param name="state">The camera state to update.</param>
        /// <param name="input">The input frame for the current tick.</param>
        public static void Update(CameraState state, PlayerInputFrame input, in WorldSizeSpec world)
        {
            if (state == null) return;

            // 1. Movement (Axes 0/1)
            // We assume Axes[0] is X (Horizontal) and Axes[1] is Y (Vertical)
            // Input values are typically raw integers (e.g., -1000 to 1000 or -127 to 127)
            unsafe
            {
                int moveX = input.Axes[0];
                int moveY = input.Axes[1];

                var target = state.TargetCm;
                bool changed = false;

                if (System.Math.Abs(moveX) > MOVE_THRESHOLD)
                {
                    target.X += System.Math.Sign(moveX) * world.GridCellSizeCm;
                    changed = true;
                }

                if (System.Math.Abs(moveY) > MOVE_THRESHOLD)
                {
                    target.Y += System.Math.Sign(moveY) * world.GridCellSizeCm;
                    changed = true;
                }

                if (changed)
                {
                    target.X = System.Math.Clamp(target.X, world.Bounds.Left, world.Bounds.Right);
                    target.Y = System.Math.Clamp(target.Y, world.Bounds.Top, world.Bounds.Bottom);
                    state.TargetCm = target;
                }
            }

            // 2. Rotation (Buttons)
            // Mapping: Bit 0 (Left), Bit 1 (Right)
            if ((input.Buttons & 1) != 0) state.Yaw -= ROTATION_SPEED;
            if ((input.Buttons & 2) != 0) state.Yaw += ROTATION_SPEED;

            // Normalize Yaw
            state.Yaw %= 360.0f;
            if (state.Yaw < 0) state.Yaw += 360.0f;

            // 3. Zoom (Buttons)
            // Mapping: Bit 2 (In), Bit 3 (Out)
            if ((input.Buttons & 4) != 0) state.DistanceCm -= ZOOM_SPEED;
            if ((input.Buttons & 8) != 0) state.DistanceCm += ZOOM_SPEED;

            state.DistanceCm = System.Math.Clamp(state.DistanceCm, 500.0f, 10000.0f);
        }
    }
}
