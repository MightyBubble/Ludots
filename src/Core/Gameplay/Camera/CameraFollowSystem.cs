using Ludots.Core.Input.Runtime;

namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Updates CameraState.TargetCm from CameraManager.FollowTargetPositionCm when follow is active.
    /// No ECS dependency — the follow target position is set externally by ECS systems or triggers.
    /// Runs in the render frame BEFORE CameraManager.Update().
    /// </summary>
    public sealed class CameraFollowSystem
    {
        private readonly CameraManager _camera;
        private readonly PlayerInputHandler _input;
        private readonly string _followActionId;

        public CameraFollowSystem(CameraManager camera, PlayerInputHandler input, string followActionId = null)
        {
            _camera = camera;
            _input = input;
            _followActionId = followActionId ?? "CameraLock";
        }

        public void Update(float dt)
        {
            var mode = _camera.FollowMode;
            if (mode == CameraFollowMode.None)
            {
                _camera.State.IsFollowing = false;
                return;
            }

            bool shouldFollow;
            if (mode == CameraFollowMode.AlwaysFollow)
            {
                shouldFollow = true;
            }
            else
            {
                shouldFollow = _input.ReadAction<bool>(_followActionId);
            }

            if (!shouldFollow || !_camera.FollowTargetPositionCm.HasValue)
            {
                _camera.State.IsFollowing = false;
                return;
            }

            _camera.State.TargetCm = _camera.FollowTargetPositionCm.Value;
            _camera.State.IsFollowing = true;
        }
    }
}
