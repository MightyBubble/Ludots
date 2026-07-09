using System;
using System.Numerics;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Gameplay.Camera.Behaviors
{
    internal sealed class DragRotateBehavior : ICameraBehavior
    {
        private readonly float _degPerPixel;
        private readonly float _minPitchDeg;
        private readonly float _maxPitchDeg;
        private readonly bool _requiresHold;

        public DragRotateBehavior(
            float degPerPixel, float minPitchDeg, float maxPitchDeg, bool requiresHold)
        {
            _degPerPixel = degPerPixel;
            _minPitchDeg = minPitchDeg;
            _maxPitchDeg = maxPitchDeg;
            _requiresHold = requiresHold;
        }

        public void Update(CameraState state, CameraBehaviorContext ctx, float dt)
        {
            if (_requiresHold && !ctx.BehaviorInput.RotateHold)
            {
                return;
            }

            Vector2 look = ctx.BehaviorInput.Look;
            if (MathF.Abs(look.X) < 0.01f && MathF.Abs(look.Y) < 0.01f)
            {
                return;
            }

            state.Yaw += look.X * _degPerPixel;
            state.Pitch += look.Y * _degPerPixel;
            state.Pitch = Math.Clamp(state.Pitch, _minPitchDeg, _maxPitchDeg);
            state.Yaw = WorldPlane2D.NormalizeDegreesPositive(state.Yaw);
        }
    }
}
