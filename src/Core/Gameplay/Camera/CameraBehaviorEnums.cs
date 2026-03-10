namespace Ludots.Core.Gameplay.Camera
{
    public enum CameraRigKind
    {
        Orbit,
        TopDown,
        ThirdPerson,
        FirstPerson
    }

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

    public enum CameraFollowTargetKind
    {
        None,
        LocalPlayer,
        SelectedEntity,
        SelectedOrLocalPlayer
    }

    public enum CameraBlendCurve
    {
        Cut,
        Linear,
        SmoothStep
    }

    public enum VirtualCameraTargetSource
    {
        CurrentState,
        Fixed,
        FollowTarget
    }
}
