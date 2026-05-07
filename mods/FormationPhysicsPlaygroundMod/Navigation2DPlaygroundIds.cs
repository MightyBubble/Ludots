namespace Navigation2DPlaygroundMod
{
    internal static class Navigation2DPlaygroundIds
    {
        public const string MapId = "formation_physics_playground";

        public const string CommandModeId = "FormationPhysics.Playground.Mode.Command";
        public const string FollowModeId = "FormationPhysics.Playground.Mode.Follow";

        public const string CommandCameraId = "FormationPhysics.Playground.Camera.Command";
        public const string FollowCameraId = "FormationPhysics.Playground.Camera.Follow";

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
