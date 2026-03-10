namespace CameraShowcaseMod
{
    public static class CameraShowcaseIds
    {
        public const string HubMapId = "camera_showcase_hub";
        public const string StackMapId = "camera_showcase_stack";
        public const string SelectionMapId = "camera_showcase_selection";
        public const string BootstrapMapId = "camera_showcase_bootstrap";

        public const string TacticalProfileId = "Camera.Profile.Tactical";
        public const string FollowProfileId = "Camera.Profile.Follow";
        public const string InspectProfileId = "Camera.Profile.Inspect";
        public const string TrackProfileId = "Camera.Showcase.Profile.TrackFocus";

        public const string TacticalModeId = "Camera.Mode.Tactical";
        public const string FollowModeId = "Camera.Mode.Follow";
        public const string InspectModeId = "Camera.Mode.Inspect";

        public const string RevealShotId = "Camera.Showcase.Shot.CommandReveal";
        public const string FocusLockShotId = "Camera.Shot.SelectionLock";
        public const string InspectSweepShotId = "Camera.Shot.InspectSweep";

        public const string TrackModeId = "Camera.Mode.TrackFocus";
        public const string TrackModeActionId = "CameraModeTrackFocus";

        public const string HeroName = "CameraShowcaseHero";
        public const string ScoutName = "CameraShowcaseScout";
        public const string CaptainName = "CameraShowcaseCaptain";
        public const string BootstrapNorthWestName = "CameraShowcaseNorthWest";
        public const string BootstrapSouthEastName = "CameraShowcaseSouthEast";

        public static bool IsShowcaseMap(string? mapId)
        {
            return string.Equals(mapId, HubMapId, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(mapId, StackMapId, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(mapId, SelectionMapId, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(mapId, BootstrapMapId, System.StringComparison.OrdinalIgnoreCase);
        }

        public static string DescribeMap(string? mapId)
        {
            return mapId switch
            {
                HubMapId => "Baseline shared profiles plus an upper-layer selection-to-follow composition sample.",
                StackMapId => "Tagged reveal shot wins over map-default follow, then falls back.",
                SelectionMapId => "Selection chooses the tracked subject; the camera follows through an explicit bridge with no fallback.",
                BootstrapMapId => "Shared bootstrap centers wide bounds before free camera control.",
                _ => "Production sample for the unified virtual camera stack."
            };
        }
    }
}
