using System;
using System.Numerics;
using Ludots.Core.Input.Runtime;

namespace Ludots.Core.Gameplay.Camera
{
    public sealed class Orbit3CCameraController : ICameraController
    {
        private readonly Orbit3CCameraConfig _config;
        private readonly PlayerInputHandler _input;
        private bool _isRotating;
        private Vector2 _lastPointerPos;

        public Orbit3CCameraController(Orbit3CCameraConfig config, PlayerInputHandler input)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _input = input ?? throw new ArgumentNullException(nameof(input));
        }

        public void Update(CameraState state, float dt)
        {
            if (state == null) return;

            float zoom = _input.ReadAction<float>(_config.ZoomActionId);
            if (MathF.Abs(zoom) > 0.0001f)
            {
                state.DistanceCm -= zoom * _config.ZoomCmPerWheel;
                state.DistanceCm = Math.Clamp(state.DistanceCm, _config.MinDistanceCm, _config.MaxDistanceCm);
            }

            bool rotateHold = _input.ReadAction<bool>(_config.RotateHoldActionId);
            Vector2 pointerPos = _input.ReadAction<Vector2>(_config.PointerPosActionId);

            if (rotateHold)
            {
                if (!_isRotating)
                {
                    _isRotating = true;
                    _lastPointerPos = pointerPos;
                }
                else
                {
                    Vector2 delta = pointerPos - _lastPointerPos;
                    _lastPointerPos = pointerPos;

                    state.Yaw += delta.X * _config.RotateDegPerPixel;
                    state.Pitch += delta.Y * _config.RotateDegPerPixel;
                    state.Pitch = Math.Clamp(state.Pitch, _config.MinPitchDeg, _config.MaxPitchDeg);
                    state.Yaw = Wrap360(state.Yaw);
                }
            }
            else
            {
                _isRotating = false;
            }

            if (dt > 0f)
            {
                bool rotateLeft = _input.ReadAction<bool>(_config.RotateLeftActionId);
                bool rotateRight = _input.ReadAction<bool>(_config.RotateRightActionId);
                float dir = (rotateRight ? 1f : 0f) - (rotateLeft ? 1f : 0f);
                if (dir != 0f)
                {
                    state.Yaw += dir * (_config.RotateDegPerSecond * dt);
                    state.Yaw = Wrap360(state.Yaw);
                }
            }

            if (_config.EnablePan && dt > 0f)
            {
                Vector2 move = _input.ReadAction<Vector2>(_config.MoveActionId);
                if (move.LengthSquared() > 0.0001f)
                {
                    Vector2 dir = OrbitCameraDirectionUtil.MoveInputToDirection(state.Yaw, move);
                    if (dir.LengthSquared() > 0.0001f)
                    {
                        state.TargetCm += dir * (_config.PanCmPerSecond * dt);
                    }
                }
            }
        }

        private static float Wrap360(float degrees)
        {
            degrees %= 360f;
            if (degrees < 0f) degrees += 360f;
            return degrees;
        }
    }
}

