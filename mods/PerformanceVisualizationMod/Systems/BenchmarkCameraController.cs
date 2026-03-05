using System;
using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;

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
                var moveDir = OrbitCameraDirectionUtil.MoveInputToDirection(state.Yaw, move);
                if (moveDir.LengthSquared() > 0)
                {
                    float moveStep = MoveSpeed * dt;
                    state.TargetCm += moveDir * moveStep;
                }
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

    }
}
