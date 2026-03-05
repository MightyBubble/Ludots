namespace Ludots.Core.Gameplay.Camera
{
    public enum CameraPanMode
    {
        None,
        Keyboard,
        EdgePan,
        KeyboardAndEdge
    }

    public enum CameraRotateMode
    {
        None,
        DragRotate,
        KeyRotate,
        Both
    }

    public enum CameraFollowMode
    {
        None,
        HoldToLock,
        AlwaysFollow
    }
}
