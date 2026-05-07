namespace Navigation2DPlaygroundMod.Systems
{
    public enum Navigation2DPlaygroundToolMode : byte
    {
        Move = 0,
        SpawnTeam0 = 1,
        SpawnTeam1 = 2,
        SpawnBlocker = 3,
    }

    public static class Navigation2DPlaygroundState
    {
        public static bool Enabled;
        public static int AgentsPerTeam = 300;
        public static int CurrentScenarioIndex;
        public static int SpawnBatch = 128;
        public static Navigation2DPlaygroundToolMode ToolMode = Navigation2DPlaygroundToolMode.Move;
    }
}
