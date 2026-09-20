namespace CameraAcceptanceMod
{
    public static class CameraAcceptanceIds
    {
        public const string InputContextId = "CameraAcceptance.Controls";

        public const string ProjectionMapId = "camera_acceptance_projection";
        public const string HotpathMapId = "camera_acceptance_hotpath";
        public const string RtsMapId = "camera_acceptance_rts";
        public const string TpsMapId = "camera_acceptance_tps";
        public const string BlendMapId = "camera_acceptance_blend";
        public const string FollowMapId = "camera_acceptance_follow";
        public const string StackMapId = "camera_acceptance_stack";

        public const string RtsCameraId = "Shared3C.Profile.RtsMoba";
        public const string ProjectionCameraId = "Camera.Acceptance.Profile.ProjectionFixed";
        public const string HotpathCameraId = "Camera.Acceptance.Profile.HotpathOverview";
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
        public const string LocalAvatarMoveActionId = "CameraAcceptanceLocalAvatarMove";

        public const string BlendCutActionId = "CameraAcceptanceBlendCut";
        public const string BlendLinearActionId = "CameraAcceptanceBlendLinear";
        public const string BlendSmoothActionId = "CameraAcceptanceBlendSmooth";
        public const string ActiveBlendCameraIdKey = "CameraAcceptance.ActiveBlendCameraId";
        public const string TpsAimHoldActionId = "CameraAcceptanceTpsAimHold";
        public const string StackRevealActionId = "CameraAcceptanceStackReveal";
        public const string StackAlertActionId = "CameraAcceptanceStackAlert";
        public const string StackClearActionId = "CameraAcceptanceStackClear";
        public const string ProjectionSpawnCountDecreaseActionId = "CameraAcceptanceProjectionSpawnCountDecrease";
        public const string ProjectionSpawnCountIncreaseActionId = "CameraAcceptanceProjectionSpawnCountIncrease";
        public const string TogglePanelActionId = "CameraAcceptanceTogglePanel";
        public const string ToggleHudActionId = "CameraAcceptanceToggleHud";
        public const string ToggleTextActionId = "CameraAcceptanceToggleText";
        public const string ToggleHotpathBarsActionId = "CameraAcceptanceToggleHotpathBars";
        public const string ToggleHotpathHudTextActionId = "CameraAcceptanceToggleHotpathHudText";
        public const string ToggleTerrainActionId = "CameraAcceptanceToggleTerrain";
        public const string ToggleGuidesActionId = "CameraAcceptanceToggleGuides";
        public const string TogglePrimitiveActionId = "CameraAcceptanceTogglePrimitives";
        public const string ToggleHotpathCullCrowdActionId = "CameraAcceptanceToggleHotpathCullCrowd";
        public const string ProjectionSpawnCountKey = "CameraAcceptance.ProjectionSpawnCount";
        public const int ProjectionSpawnCountDefault = 100;
        public const int ProjectionSpawnCountStep = 100;
        public const int HotpathCrowdTargetCount = 10240;
        public const int HotpathSelectionLabelLimit = 16;
        public const string HotpathCrowdTemplateId = "moba_dummy";
        public const int HotpathSweepTravelFrames = 180;
        public const int HotpathSweepHoldFrames = 30;
        public const int HotpathSweepLeftX = 7200;
        public const int HotpathSweepRightX = 30000;
        public const int HotpathSweepCenterY = 7200;
        public const int HotpathSweepAmplitudeY = 2200;

        public const string HeroName = "CameraAcceptanceHero";
        public const string ScoutName = "CameraAcceptanceScout";
        public const string CaptainName = "CameraAcceptanceCaptain";
        public const string FocusDummyName = "CameraAcceptanceDummy";
        public const string AlarmDummyName = "CameraAcceptanceAlarmDummy";
        public const string ProjectionSpawnTemplateId = "moba_dummy";
        public const string ProjectionCueFixturePresenterId = "camera_acceptance_projection_cue_fixture";
        public const string ProjectionCueDecalPresenterId = "camera_acceptance_projection_cue_decal";
        public const string ProjectionCueVfxPresenterId = "camera_acceptance_projection_cue_vfx";
        public const string ProjectionCueSurfacePresenterId = "camera_acceptance_projection_cue_surface";

        public static bool IsAcceptanceMap(string? mapId)
        {
            return string.Equals(mapId, ProjectionMapId, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(mapId, HotpathMapId, System.StringComparison.OrdinalIgnoreCase)
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
                ProjectionMapId => "Projection and raycast acceptance. Use the primary pointer action on empty ground to spawn a random-scatter batch and a transient presenter marker.",
                HotpathMapId => "Presentation hotpath harness. Drive the local avatar through a 10k+ deterministic crowd while the virtual camera follows, inspect the live visible-entity panel, and toggle panel/diagnostic HUD/selection/HUD bars/HUD text/terrain/reference guides/primitives/culling load in one reproducible scene.",
                RtsMapId => "RTS/MOBA shared profile acceptance. Validate the shared follow camera stays bound to the local player while WASD moves the avatar entity.",
                TpsMapId => "TPS behavior composition. Use WASD to move the local avatar, hold right mouse to aim/look, then use wheel zoom.",
                BlendMapId => "Blend acceptance. Pick a curve, then use the primary pointer action on ground to move the camera there smoothly.",
                FollowMapId => "Follow acceptance. Click an entity to select it; when the target is lost, the camera must stay in place.",
                StackMapId => "Virtual camera stack acceptance. Base follow camera, reveal shot, nested alert shot, then clear back down.",
                _ => "Focused camera acceptance slices."
            };
        }

        public static string DescribeControls(string? mapId)
        {
            return mapId switch
            {
                ProjectionMapId => "Use the panel to move between scenarios. On this map, press Q/E to decrease/increase the primary-action spawn batch by 100 with a floor of 0, then use the primary pointer action on empty ground and verify a random-scatter batch appears around the raycast point while the cue marker still appears then expires.",
                HotpathMapId => "Middle-drag to pan the overview camera. Use WASD to move the local avatar in RTS/TPS modes and watch the virtual camera follow while the panel prints the currently visible entities. Use F6 panel, F7 diagnostics HUD, F8 selection labels, F9 HUD bars, F10 HUD text, F11 terrain, G guides, F12 primitives, and C to isolate culling load.",
                RtsMapId => "Use WASD to move the local avatar and verify the shared follow camera stays locked to that entity.",
                TpsMapId => "Use WASD to move the local avatar, hold right mouse and drag to rotate, and use the wheel to zoom. The camera should stay bound to the follow target.",
                BlendMapId => "Pick Cut / Linear / Smooth in the panel, then use the primary pointer action on a ground point to trigger the blend.",
                FollowMapId => "Click Hero or Captain in world to select, click empty ground to clear selection, move Captain deterministically, and switch Follow Close/Wide to verify no fallback.",
                StackMapId => "Use panel buttons: Reveal -> Alert -> Clear -> Clear, and verify the stack walks back to the base follow camera.",
                _ => "Use the panel to switch acceptance scenarios."
            };
        }
    }
}
