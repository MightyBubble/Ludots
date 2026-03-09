using System;

namespace Ludots.Core.Gameplay.Camera.Behaviors
{
    internal sealed class ZoomBehavior : ICameraBehavior
    {
        private readonly string _zoomActionId;
        private readonly float _cmPerWheel;
        private readonly float _minDistanceCm;
        private readonly float _maxDistanceCm;

        public ZoomBehavior(string zoomActionId, float cmPerWheel, float minDistanceCm, float maxDistanceCm)
        {
            _zoomActionId = zoomActionId ?? "Zoom";
            _cmPerWheel = cmPerWheel;
            _minDistanceCm = minDistanceCm;
            _maxDistanceCm = maxDistanceCm;
        }

        public void Update(CameraState state, CameraBehaviorContext ctx, float dt)
        {
            float zoom = ctx.Input.ReadAction<float>(_zoomActionId);
            if (MathF.Abs(zoom) < 0.0001f) return;

            state.DistanceCm -= zoom * _cmPerWheel;
            state.DistanceCm = Math.Clamp(state.DistanceCm, _minDistanceCm, _maxDistanceCm);
        }
    }
}
