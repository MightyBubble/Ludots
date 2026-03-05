namespace Ludots.Core.Gameplay.Camera
{
    public interface ICameraBehavior
    {
        void Update(CameraState state, CameraBehaviorContext ctx, float dt);
    }
}
