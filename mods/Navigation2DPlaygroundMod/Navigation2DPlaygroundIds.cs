namespace Navigation2DPlaygroundMod
{
    internal static class Navigation2DPlaygroundIds
    {
        public const string MapId = "nav2d_playground";

        public const string CommandModeId = "Navigation2D.Playground.Mode.Command";
        public const string FollowModeId = "Navigation2D.Playground.Mode.Follow";

        public const string CommandCameraId = "Navigation2D.Playground.Camera.Command";
        public const string FollowCameraId = "Navigation2D.Playground.Camera.Follow";

        public static bool IsPlaygroundMap(string? mapId)
        {
            return string.Equals(mapId, MapId, System.StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsOwnedViewMode(string? modeId)
        {
            return string.Equals(modeId, CommandModeId, System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(modeId, FollowModeId, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
