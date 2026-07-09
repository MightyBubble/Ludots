using System;

namespace CapabilityStandardVirtualCameraShowcaseMod;

public static class CapabilityStandardVirtualCameraShowcaseIds
{
    public const string MapId = "capability_standard_virtual_camera";
    public const string TacticalCameraId = "CapabilityStandard.VirtualCamera.Profile.Tactical";
    public const string BehaviorOrbitCameraId = "CapabilityStandard.VirtualCamera.Profile.BehaviorOrbit";
    public const string HeightmapOrbitCameraId = "CapabilityStandard.VirtualCamera.Profile.HeightmapOrbit";
    public const string TpsCameraId = "CapabilityStandard.VirtualCamera.Profile.TPS";
    public const string FpsCameraId = "CapabilityStandard.VirtualCamera.Profile.FPS";
    public const string RevealShotCameraId = "CapabilityStandard.VirtualCamera.Shot.Reveal";
    public const string BehaviorOrbitModeId = "CapabilityStandard.VirtualCamera.Mode.BehaviorOrbit";
    public const string HeightmapOrbitModeId = "CapabilityStandard.VirtualCamera.Mode.HeightmapOrbit";
    public const string TpsModeId = "CapabilityStandard.VirtualCamera.Mode.TPS";
    public const string FpsModeId = "CapabilityStandard.VirtualCamera.Mode.FPS";
    public const string BehaviorOrbitModeActionId = "CapabilityStandardVirtualCameraMode";
    public const string HeightmapOrbitModeActionId = "CapabilityStandardVirtualCameraHeightmapMode";
    public const string TpsModeActionId = "CapabilityStandardVirtualCameraTpsMode";
    public const string FpsModeActionId = "CapabilityStandardVirtualCameraFpsMode";
    public const string MoveActionId = "CapabilityStandard.VirtualCamera.Move";
    public const string PointerPositionActionId = "CapabilityStandard.VirtualCamera.PointerPosition";
    public const string PointerDeltaActionId = "CapabilityStandard.VirtualCamera.PointerDelta";
    public const string GrabDragHoldActionId = "CapabilityStandard.VirtualCamera.GrabDragHold";
    public const string RotateHoldActionId = "CapabilityStandard.VirtualCamera.RotateHold";
    public const string LookActionId = "CapabilityStandard.VirtualCamera.Look";
    public const string AvatarMoveActionId = "CapabilityStandard.VirtualCamera.AvatarMove";
    public const string RotateLeftActionId = "CapabilityStandard.VirtualCamera.RotateLeft";
    public const string RotateRightActionId = "CapabilityStandard.VirtualCamera.RotateRight";
    public const string ZoomActionId = "CapabilityStandard.VirtualCamera.Zoom";
    public const string AvatarMoveXAttribute = "CapabilityStandard.Avatar.MoveX";
    public const string AvatarMoveYAttribute = "CapabilityStandard.Avatar.MoveY";
    public const string RuntimeStateKey = "CapabilityStandardVirtualCameraShowcase.State";

    public static bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, MapId, StringComparison.OrdinalIgnoreCase);
    }
}
