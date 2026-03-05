using System;

namespace Ludots.Core.Gameplay.Camera
{
    public sealed class CompositeCameraController : ICameraController
    {
        private readonly ICameraBehavior[] _behaviors;
        private readonly CameraBehaviorContext _ctx;

        public CompositeCameraController(ICameraBehavior[] behaviors, CameraBehaviorContext ctx)
        {
            _behaviors = behaviors ?? throw new ArgumentNullException(nameof(behaviors));
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public void Update(CameraState state, float dt)
        {
            if (state == null) return;
            for (int i = 0; i < _behaviors.Length; i++)
                _behaviors[i].Update(state, _ctx, dt);
        }
    }
}
