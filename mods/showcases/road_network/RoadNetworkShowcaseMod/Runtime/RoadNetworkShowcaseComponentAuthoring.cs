using Ludots.Core.Config;

namespace RoadNetworkShowcaseMod.Runtime
{
    internal static class RoadNetworkShowcaseComponentAuthoring
    {
        public static void Register(string modId)
        {
            ComponentRegistry.Register<RoadColumnTag>("RoadColumnTag", modId);
            ComponentRegistry.Register<RoadFortTag>("RoadFortTag", modId);
            ComponentRegistry.Register<RoadAiControlledTag>("RoadAiControlledTag", modId);
            ComponentRegistry.Register<RoadMoveProfileRef>("RoadMoveProfileRef", modId);
            ComponentRegistry.Register<RoadFortControlState>("RoadFortControlState", modId);
        }
    }
}
