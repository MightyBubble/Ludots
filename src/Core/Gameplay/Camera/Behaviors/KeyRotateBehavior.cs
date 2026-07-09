namespace Ludots.Core.Gameplay.Camera.Behaviors
{
    internal sealed class KeyRotateBehavior : ICameraBehavior
    {
        private readonly float _degPerSecond;

        public KeyRotateBehavior(float degPerSecond)
        {
            _degPerSecond = degPerSecond;
        }

        public void Update(CameraState state, CameraBehaviorContext ctx, float dt)
        {
            if (dt <= 0f) return;

            bool left = ctx.BehaviorInput.RotateLeft;
            bool right = ctx.BehaviorInput.RotateRight;
            float dir = (right ? 1f : 0f) - (left ? 1f : 0f);
            if (dir == 0f) return;

            state.Yaw += dir * (_degPerSecond * dt);
            state.Yaw %= 360f;
            if (state.Yaw < 0f) state.Yaw += 360f;
        }
    }
}
