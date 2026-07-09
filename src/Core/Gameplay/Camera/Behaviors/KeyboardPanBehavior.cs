using System.Numerics;

namespace Ludots.Core.Gameplay.Camera.Behaviors
{
    internal sealed class KeyboardPanBehavior : ICameraBehavior
    {
        private readonly float _panCmPerSecond;

        public KeyboardPanBehavior(float panCmPerSecond)
        {
            _panCmPerSecond = panCmPerSecond;
        }

        public void Update(CameraState state, CameraBehaviorContext ctx, float dt)
        {
            if (state.IsFollowing || dt <= 0f) return;

            Vector2 move = ctx.BehaviorInput.Move;
            if (move.LengthSquared() < 0.0001f) return;

            Vector2 dir = OrbitCameraDirectionUtil.MoveInputToDirection(state.Yaw, move);
            if (dir.LengthSquared() > 0.0001f)
                state.TargetCm += dir * (_panCmPerSecond * dt);
        }
    }
}
