namespace Ludots.Core.Gameplay.Camera
{
    public interface ICameraFollowTarget
    {
        bool TryGetTransform(out CameraTargetTransformSnapshot transform);
    }
}
