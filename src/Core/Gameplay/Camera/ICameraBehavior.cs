namespace Ludots.Core.Gameplay.Camera
{
    internal interface ICameraBehavior
    {
        void Update(CameraState state, CameraBehaviorContext ctx, float dt);
    }
}
