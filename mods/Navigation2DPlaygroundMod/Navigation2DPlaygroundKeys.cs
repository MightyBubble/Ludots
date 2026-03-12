using Ludots.Core.Scripting;

namespace Navigation2DPlaygroundMod
{
    public static class Navigation2DPlaygroundKeys
    {
        public static readonly ServiceKey<int> AgentsPerTeam = new("Navigation2DPlayground_AgentsPerTeam");
        public static readonly ServiceKey<int> LiveAgentsTotal = new("Navigation2DPlayground_LiveAgentsTotal");
        public static readonly ServiceKey<int> FlowDebugLines = new("Navigation2DPlayground_FlowDebugLines");
        public static readonly ServiceKey<int> BlockerCount = new("Navigation2DPlayground_BlockerCount");
        public static readonly ServiceKey<int> ScenarioIndex = new("Navigation2DPlayground_ScenarioIndex");
        public static readonly ServiceKey<int> ScenarioCount = new("Navigation2DPlayground_ScenarioCount");
        public static readonly ServiceKey<int> ScenarioTeamCount = new("Navigation2DPlayground_ScenarioTeamCount");
        public static readonly ServiceKey<string> ScenarioId = new("Navigation2DPlayground_ScenarioId");
        public static readonly ServiceKey<string> ScenarioName = new("Navigation2DPlayground_ScenarioName");
        public static readonly ServiceKey<int> SpawnBatch = new("Navigation2DPlayground_SpawnBatch");
    }
}
