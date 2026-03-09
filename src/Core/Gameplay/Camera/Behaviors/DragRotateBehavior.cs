using System;
using System.Numerics;

namespace Ludots.Core.Gameplay.Camera.Behaviors
{
    internal sealed class DragRotateBehavior : ICameraBehavior
    {
        private readonly string _holdActionId;
        private readonly string _pointerPosActionId;
        private readonly float _degPerPixel;
        private readonly float _minPitchDeg;
        private readonly float _maxPitchDeg;
        private bool _isRotating;
        private Vector2 _lastPointerPos;

        public DragRotateBehavior(
            string holdActionId, string pointerPosActionId,
            float degPerPixel, float minPitchDeg, float maxPitchDeg)
        {
            _holdActionId = holdActionId ?? "OrbitRotateHold";
            _pointerPosActionId = pointerPosActionId ?? "PointerPos";
            _degPerPixel = degPerPixel;
            _minPitchDeg = minPitchDeg;
            _maxPitchDeg = maxPitchDeg;
        }

        public void Update(CameraState state, CameraBehaviorContext ctx, float dt)
        {
            bool hold = ctx.Input.ReadAction<bool>(_holdActionId);
            Vector2 pointerPos = ctx.Input.ReadAction<Vector2>(_pointerPosActionId);

            if (hold)
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

                    state.Yaw += delta.X * _degPerPixel;
                    state.Pitch += delta.Y * _degPerPixel;
                    state.Pitch = Math.Clamp(state.Pitch, _minPitchDeg, _maxPitchDeg);
                    state.Yaw = Wrap360(state.Yaw);
                }
            }
            else
            {
                _isRotating = false;
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
