using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using System.Numerics;
using System;

namespace PerformanceVisualizationMod.Systems
{
    public class BenchmarkCameraController : ICameraController
    {
        private readonly PlayerInputHandler _input;
        private const float MoveSpeed = 20000.0f;
        private const float RotateSpeed = 90.0f; // Degrees per second
        private const float ZoomSpeed = 20000.0f;

        public BenchmarkCameraController(PlayerInputHandler input)
        {
            _input = input;
        }

        public void Update(CameraState state, float dt)
        {
            var move = _input.ReadAction<Vector2>("Move");

            if (move.LengthSquared() > 0)
            {
                var yawRad = ToRadians(state.Yaw);
                var sin = (float)Math.Sin(yawRad);
                var cos = (float)Math.Cos(yawRad);

                var forward = new Vector2(sin, cos);
                var right = new Vector2(cos, -sin);

                var moveDir = right * -move.X + forward * move.Y;
                
                if (moveDir.LengthSquared() > 0)
                    moveDir = Vector2.Normalize(moveDir);

                float moveStep = MoveSpeed * dt;
                var delta = new Vector2(moveDir.X * moveStep, moveDir.Y * moveStep);
                state.TargetCm = state.TargetCm + delta;
            }

            var rotateLeft = _input.ReadAction<float>("RotateLeft");
            var rotateRight = _input.ReadAction<float>("RotateRight");
            var rotate = rotateRight - rotateLeft;
            
            if (Math.Abs(rotate) > 0.01f)
            {
                state.Yaw += rotate * RotateSpeed * dt;
                if (state.Yaw >= 360.0f) state.Yaw -= 360.0f;
                if (state.Yaw < 0.0f) state.Yaw += 360.0f;
            }
            
            var zoom = _input.ReadAction<float>("Zoom");
            if (Math.Abs(zoom) > 0.01f)
            {
                state.DistanceCm -= zoom * ZoomSpeed * dt;
                state.DistanceCm = Math.Clamp(state.DistanceCm, 5000.0f, 300000.0f);
            }
        }

        private static float ToRadians(float degrees)
        {
            return degrees * (float)(Math.PI / 180.0);
        }
    }
}
