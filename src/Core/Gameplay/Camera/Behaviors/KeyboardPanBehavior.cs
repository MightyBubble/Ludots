using System.Numerics;

namespace Ludots.Core.Gameplay.Camera.Behaviors
{
    public sealed class KeyboardPanBehavior : ICameraBehavior
    {
        private readonly string _moveActionId;
        private readonly float _panCmPerSecond;

        public KeyboardPanBehavior(string moveActionId, float panCmPerSecond)
        {
            _moveActionId = moveActionId ?? "Move";
            _panCmPerSecond = panCmPerSecond;
        }

        public void Update(CameraState state, CameraBehaviorContext ctx, float dt)
        {
            if (state.IsFollowing || dt <= 0f) return;

            Vector2 move = ctx.Input.ReadAction<Vector2>(_moveActionId);
            if (move.LengthSquared() < 0.0001f) return;

            Vector2 dir = OrbitCameraDirectionUtil.MoveInputToDirection(state.Yaw, move);
            if (dir.LengthSquared() > 0.0001f)
                state.TargetCm += dir * (_panCmPerSecond * dt);
        }
    }
}
