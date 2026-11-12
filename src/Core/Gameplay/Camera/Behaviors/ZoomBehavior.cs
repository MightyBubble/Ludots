using System;
using System.Numerics;

namespace Ludots.Core.Gameplay.Camera.Behaviors
{
    internal sealed class ZoomBehavior : ICameraBehavior
    {
        private readonly float _cmPerWheel;
        private readonly float _minDistanceCm;
        private readonly float _maxDistanceCm;
        private readonly float _factorPerWheel;
        private readonly bool _towardsPointer;

        public ZoomBehavior(
            float cmPerWheel,
            float minDistanceCm,
            float maxDistanceCm,
            float factorPerWheel = 0f,
            bool towardsPointer = false)
        {
            _cmPerWheel = cmPerWheel;
            _minDistanceCm = minDistanceCm;
            _maxDistanceCm = maxDistanceCm;
            _factorPerWheel = factorPerWheel;
            _towardsPointer = towardsPointer;
        }

        public void Update(CameraState state, CameraBehaviorContext ctx, float dt)
        {
            float zoom = ctx.BehaviorInput.Zoom;
            if (MathF.Abs(zoom) < 0.0001f) return;

            // 先钉指针下的地面点，改距离后再把目标平移回去。跟随中只改距离。
            bool shift = false;
            Vector2 anchor = default;
            Vector2 pointer = ctx.BehaviorInput.PointerPosition;
            Vector2 resolution = ctx.Viewport.Resolution;
            float aspect = ctx.Viewport.AspectRatio;
            if (_towardsPointer && !state.IsFollowing && ctx.BehaviorInput.PointerActive)
            {
                CameraStateSnapshot before = CameraStateSnapshot.FromState(state);
                shift = CameraViewportUtil.TryGroundCmUnderPointer(in before, pointer, resolution, aspect, out anchor);
            }

            if (_factorPerWheel > 0f)
            {
                state.DistanceCm *= MathF.Pow(_factorPerWheel, -zoom);
            }
            else
            {
                state.DistanceCm -= zoom * _cmPerWheel;
            }

            state.DistanceCm = Math.Clamp(state.DistanceCm, _minDistanceCm, _maxDistanceCm);
            if (!shift) return;

            CameraStateSnapshot after = CameraStateSnapshot.FromState(state);
            if (CameraViewportUtil.TryGroundCmUnderPointer(in after, pointer, resolution, aspect, out Vector2 shifted))
            {
                state.TargetCm += anchor - shifted;
            }
        }
    }
}
