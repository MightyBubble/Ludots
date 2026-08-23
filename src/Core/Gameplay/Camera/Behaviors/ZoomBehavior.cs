using System;

namespace Ludots.Core.Gameplay.Camera.Behaviors
{
    internal sealed class ZoomBehavior : ICameraBehavior
    {
        private readonly float _cmPerWheel;
        private readonly float _minDistanceCm;
        private readonly float _maxDistanceCm;
        private readonly float _factorPerWheel;

        public ZoomBehavior(float cmPerWheel, float minDistanceCm, float maxDistanceCm, float factorPerWheel = 0f)
        {
            _cmPerWheel = cmPerWheel;
            _minDistanceCm = minDistanceCm;
            _maxDistanceCm = maxDistanceCm;
            _factorPerWheel = factorPerWheel;
        }

        public void Update(CameraState state, CameraBehaviorContext ctx, float dt)
        {
            float zoom = ctx.BehaviorInput.Zoom;
            if (MathF.Abs(zoom) < 0.0001f) return;

            if (_factorPerWheel > 0f)
            {
                // Proportional zoom: each wheel notch scales distance by a constant factor.
                // This keeps zoom responsive across a huge distance range (continent maps)
                // where a fixed cm step is either glacial when far out or jumpy when close in.
                state.DistanceCm *= MathF.Pow(_factorPerWheel, -zoom);
            }
            else
            {
                state.DistanceCm -= zoom * _cmPerWheel;
            }

            state.DistanceCm = Math.Clamp(state.DistanceCm, _minDistanceCm, _maxDistanceCm);
        }
    }
}
