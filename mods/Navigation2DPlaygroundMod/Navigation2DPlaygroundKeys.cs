using Ludots.Core.Scripting;

namespace Navigation2DPlaygroundMod
{
    public static class Navigation2DPlaygroundKeys
    {
        public static readonly ServiceKey<int> AgentsPerTeam = new("Navigation2DPlayground_AgentsPerTeam");
        public static readonly ServiceKey<int> LiveAgentsTotal = new("Navigation2DPlayground_LiveAgentsTotal");
        public static readonly ServiceKey<int> FlowDebugLines = new("Navigation2DPlayground_FlowDebugLines");
    }
}
