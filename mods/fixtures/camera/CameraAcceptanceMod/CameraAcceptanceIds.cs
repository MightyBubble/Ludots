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
        public const string ProjectionSpawnCountDecreaseActionId = "CameraAcceptanceProjectionSpawnCountDecrease";
        public const string ProjectionSpawnCountIncreaseActionId = "CameraAcceptanceProjectionSpawnCountIncrease";
        public const string TogglePanelActionId = "CameraAcceptanceTogglePanel";
        public const string ToggleHudActionId = "CameraAcceptanceToggleHud";
        public const string ToggleTextActionId = "CameraAcceptanceToggleText";
        public const string ToggleHotpathBarsActionId = "CameraAcceptanceToggleHotpathBars";
        public const string ToggleHotpathHudTextActionId = "CameraAcceptanceToggleHotpathHudText";
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
                ProjectionMapId => "Projection and raycast acceptance. Left click empty ground to spawn a random-scatter batch and a transient performer marker.",
                HotpathMapId => "Presentation hotpath harness. Drive a 10k+ deterministic crowd with manual camera movement, inspect the live visible-entity panel, and toggle panel/diagnostic HUD/selection/HUD bars/HUD text/primitives/culling load in one reproducible scene.",
                RtsMapId => "RTS/MOBA behavior composition. Validate middle-drag, edge scroll, WASD pan, and wheel zoom.",
                TpsMapId => "TPS behavior composition. Hold right mouse to aim/look, then use wheel zoom.",
                BlendMapId => "Blend acceptance. Pick a curve, then left click ground to move the camera there smoothly.",
                FollowMapId => "Follow acceptance. Click an entity to select it; when the target is lost, the camera must stay in place.",
                StackMapId => "Virtual camera stack acceptance. Base follow camera, reveal shot, nested alert shot, then clear back down.",
                _ => "Focused camera acceptance slices."
            };
        }

        public static string DescribeControls(string? mapId)
        {
            return mapId switch
            {
                ProjectionMapId => "Use the panel to move between scenarios. On this map, press Q/E to decrease/increase the left-click spawn batch by 100 with a floor of 0, then left click empty ground and verify a random-scatter batch appears around the raycast point while the cue marker still appears then expires.",
                HotpathMapId => "Move the camera manually in RTS mode across the 10k+ crowd and watch the panel print the currently visible entities. Use F6 panel, F7 diagnostics HUD, F8 selection labels, F9 HUD bars, F10 HUD text, F12 primitives, and C to isolate culling load.",
                RtsMapId => "Keyboard: WASD pan. Mouse: move to screen edge for edge-scroll, hold middle mouse to drag-pan, wheel to zoom.",
                TpsMapId => "Hold right mouse and drag to rotate. Wheel zooms. This map stays on the follow target while you aim.",
                BlendMapId => "Pick Cut / Linear / Smooth in the panel, then left click a ground point to trigger the blend.",
                FollowMapId => "Click Hero or Captain in world to select, click empty ground to clear selection, move Captain deterministically, and switch Follow Close/Wide to verify no fallback.",
                StackMapId => "Use panel buttons: Reveal -> Alert -> Clear -> Clear, and verify the stack walks back to the base follow camera.",
                _ => "Use the panel to switch acceptance scenarios."
            };
        }
    }
}
