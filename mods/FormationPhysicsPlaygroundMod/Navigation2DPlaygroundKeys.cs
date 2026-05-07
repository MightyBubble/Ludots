using Ludots.Core.Scripting;

namespace Navigation2DPlaygroundMod
{
    public static class Navigation2DPlaygroundKeys
    {
        public static readonly ServiceKey<int> AgentsPerTeam = new("FormationPhysicsPlayground_AgentsPerTeam");
        public static readonly ServiceKey<int> LiveAgentsTotal = new("FormationPhysicsPlayground_LiveAgentsTotal");
        public static readonly ServiceKey<int> FlowDebugLines = new("FormationPhysicsPlayground_FlowDebugLines");
        public static readonly ServiceKey<int> BlockerCount = new("FormationPhysicsPlayground_BlockerCount");
        public static readonly ServiceKey<int> ScenarioIndex = new("FormationPhysicsPlayground_ScenarioIndex");
        public static readonly ServiceKey<int> ScenarioCount = new("FormationPhysicsPlayground_ScenarioCount");
        public static readonly ServiceKey<int> ScenarioTeamCount = new("FormationPhysicsPlayground_ScenarioTeamCount");
        public static readonly ServiceKey<string> ScenarioId = new("FormationPhysicsPlayground_ScenarioId");
        public static readonly ServiceKey<string> ScenarioName = new("FormationPhysicsPlayground_ScenarioName");
        public static readonly ServiceKey<int> SpawnBatch = new("FormationPhysicsPlayground_SpawnBatch");
    }
}
