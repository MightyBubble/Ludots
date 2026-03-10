namespace CameraAcceptanceMod
{
    public static class CameraAcceptanceIds
    {
        public const string InputContextId = "CameraAcceptance.Controls";

        public const string ProjectionMapId = "camera_acceptance_projection";
        public const string RtsMapId = "camera_acceptance_rts";
        public const string TpsMapId = "camera_acceptance_tps";
        public const string BlendMapId = "camera_acceptance_blend";
        public const string FollowMapId = "camera_acceptance_follow";
        public const string StackMapId = "camera_acceptance_stack";

        public const string RtsCameraId = "Camera.Acceptance.Profile.RtsMoba";
        public const string TpsCameraId = "Camera.Acceptance.Profile.TpsAim";
        public const string BlendBaseCameraId = "Camera.Acceptance.Profile.BlendBase";
        public const string FollowCloseCameraId = "Camera.Acceptance.Profile.FollowClose";
        public const string FollowWideCameraId = "Camera.Acceptance.Profile.FollowWide";
        public const string BlendCutCameraId = "Camera.Acceptance.Blend.Cut";
        public const string BlendLinearCameraId = "Camera.Acceptance.Blend.Linear";
        public const string BlendSmoothCameraId = "Camera.Acceptance.Blend.Smooth";
        public const string StackRevealShotId = "Camera.Acceptance.Shot.CommandReveal";
        public const string StackAlertShotId = "Camera.Acceptance.Shot.AlertSweep";

        public const string RtsModeId = "Camera.Acceptance.Mode.Rts";
        public const string TpsModeId = "Camera.Acceptance.Mode.Tps";
        public const string FollowCloseModeId = "Camera.Acceptance.Mode.FollowClose";
        public const string FollowWideModeId = "Camera.Acceptance.Mode.FollowWide";

        public const string RtsModeActionId = "CameraAcceptanceModeRts";
        public const string TpsModeActionId = "CameraAcceptanceModeTps";
        public const string FollowCloseModeActionId = "CameraAcceptanceModeFollowClose";
        public const string FollowWideModeActionId = "CameraAcceptanceModeFollowWide";

        public const string BlendCutActionId = "CameraAcceptanceBlendCut";
        public const string BlendLinearActionId = "CameraAcceptanceBlendLinear";
        public const string BlendSmoothActionId = "CameraAcceptanceBlendSmooth";
        public const string ActiveBlendCameraIdKey = "CameraAcceptance.ActiveBlendCameraId";
        public const string TpsAimHoldActionId = "CameraAcceptanceTpsAimHold";
        public const string StackRevealActionId = "CameraAcceptanceStackReveal";
        public const string StackAlertActionId = "CameraAcceptanceStackAlert";
        public const string StackClearActionId = "CameraAcceptanceStackClear";
        public const string SpawnModifierActionId = "CameraAcceptanceSpawnModifier";
        public const string SelectionProfileId = "Selection.Profile.PointerBox";
        public const string FixtureTemplateId = "moba_dummy";
        public const string FixtureNamePrefix = "CameraFixture";

        public const string HeroName = "CameraAcceptanceHero";
        public const string ScoutName = "CameraAcceptanceScout";
        public const string CaptainName = "CameraAcceptanceCaptain";
        public const string FocusDummyName = "CameraAcceptanceDummy";
        public const string AlarmDummyName = "CameraAcceptanceAlarmDummy";

        public static bool IsAcceptanceMap(string? mapId)
        {
            return string.Equals(mapId, ProjectionMapId, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(mapId, RtsMapId, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(mapId, TpsMapId, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(mapId, BlendMapId, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(mapId, FollowMapId, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(mapId, StackMapId, System.StringComparison.OrdinalIgnoreCase);
        }

        public static string DescribeMap(string? mapId)
        {
            return mapId switch
            {
                ProjectionMapId => "Projection, raycast, and selection acceptance. Left click blank ground for the cue marker, hold Q + left click to spawn fixtures, then click or drag-box to select them.",
                RtsMapId => "RTS/MOBA behavior composition. Validate middle-drag, edge scroll, WASD pan, and wheel zoom.",
                TpsMapId => "TPS behavior composition. Hold right mouse to aim/look, then use wheel zoom.",
                BlendMapId => "Blend acceptance. Pick a curve, then left click ground to move the camera there smoothly.",
                FollowMapId => "Follow acceptance. Selection owns the follow target; right click moves the selected entity and blank-ground clicks detach without fallback.",
                StackMapId => "Virtual camera stack acceptance. Base follow camera, reveal shot, nested alert shot, then clear back down.",
                _ => "Focused camera acceptance slices."
            };
        }

        public static string DescribeControls(string? mapId)
        {
            return mapId switch
            {
                ProjectionMapId => "Hold Q + left click to spawn a fixture at the ray-hit point with its entity id over its head. Left click selects, drag creates box selection, clicking blank ground clears, and blank-ground clicks also emit the transient cue marker. The panel should still reflect viewport-visible entities from core culling.",
                RtsMapId => "Keyboard: WASD pan. Mouse: move to screen edge for edge-scroll, hold middle mouse to drag-pan, wheel to zoom.",
                TpsMapId => "Hold right mouse and drag to rotate. Wheel zooms. This map stays on the follow target while you aim.",
                BlendMapId => "Pick Cut / Linear / Smooth in the panel, then left click a ground point to trigger the blend.",
                FollowMapId => "Left click selects, blank-ground left click detaches, right click ground moves the selected entity, and Follow Close/Wide lets you compare follow rig parameters without coupling selection into the camera config.",
                StackMapId => "Use panel buttons: Reveal -> Alert -> Clear -> Clear, and verify the stack walks back to the base follow camera.",
                _ => "Use the panel to switch acceptance scenarios."
            };
        }
    }
}
