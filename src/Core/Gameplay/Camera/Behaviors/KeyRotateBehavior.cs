namespace Ludots.Core.Gameplay.Camera.Behaviors
{
    internal sealed class KeyRotateBehavior : ICameraBehavior
    {
        private readonly string _rotateLeftActionId;
        private readonly string _rotateRightActionId;
        private readonly float _degPerSecond;

        public KeyRotateBehavior(string rotateLeftActionId, string rotateRightActionId, float degPerSecond)
        {
            _rotateLeftActionId = rotateLeftActionId ?? "RotateLeft";
            _rotateRightActionId = rotateRightActionId ?? "RotateRight";
            _degPerSecond = degPerSecond;
        }

        public void Update(CameraState state, CameraBehaviorContext ctx, float dt)
        {
            if (dt <= 0f) return;

            bool left = ctx.Input.ReadAction<bool>(_rotateLeftActionId);
            bool right = ctx.Input.ReadAction<bool>(_rotateRightActionId);
            float dir = (right ? 1f : 0f) - (left ? 1f : 0f);
            if (dir == 0f) return;

            state.Yaw += dir * (_degPerSecond * dt);
            state.Yaw %= 360f;
            if (state.Yaw < 0f) state.Yaw += 360f;
        }
    }
}
