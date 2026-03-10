using System;
using System.Numerics;

namespace Ludots.Core.Gameplay.Camera.Behaviors
{
    public sealed class DragRotateBehavior : ICameraBehavior
    {
        private readonly string _holdActionId;
        private readonly string _lookActionId;
        private readonly float _degPerPixel;
        private readonly float _minPitchDeg;
        private readonly float _maxPitchDeg;

        public DragRotateBehavior(
            string holdActionId, string lookActionId,
            float degPerPixel, float minPitchDeg, float maxPitchDeg)
        {
            _holdActionId = holdActionId ?? "OrbitRotateHold";
            _lookActionId = lookActionId ?? "Look";
            _degPerPixel = degPerPixel;
            _minPitchDeg = minPitchDeg;
            _maxPitchDeg = maxPitchDeg;
        }

        public void Update(CameraState state, CameraBehaviorContext ctx, float dt)
        {
            bool hold = ctx.Input.ReadAction<bool>(_holdActionId);
            if (!hold)
            {
                return;
            }

            Vector2 look = ctx.Input.ReadAction<Vector2>(_lookActionId);
            if (MathF.Abs(look.X) < 0.01f && MathF.Abs(look.Y) < 0.01f)
            {
                return;
            }

            state.Yaw += look.X * _degPerPixel;
            state.Pitch += look.Y * _degPerPixel;
            state.Pitch = Math.Clamp(state.Pitch, _minPitchDeg, _maxPitchDeg);
            state.Yaw = Wrap360(state.Yaw);
        }

        private static float Wrap360(float degrees)
        {
            degrees %= 360f;
            if (degrees < 0f) degrees += 360f;
            return degrees;
        }
    }
}
