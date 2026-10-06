namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Whether the camera arm adopts the followed target's facing yaw. None is the unset state and
    /// fails fast at load: a rig that silently guesses facing semantics is authoring debt, not a
    /// default. Placement needs no rig kind — distance 0 is at-pivot by geometry.
    /// </summary>
    public enum CameraFacingMode
    {
        None,
        FollowTarget
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
        SolePossessedRep,
        EntityCollectionPrimary,
        EntityCollectionGroup
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

    public enum VirtualCameraControlMode
    {
        BuiltIn,
        PlatformManaged
    }

    public enum VirtualCameraTargetHeightMode
    {
        Flat,
        ContinuousHeightmap
    }
}
