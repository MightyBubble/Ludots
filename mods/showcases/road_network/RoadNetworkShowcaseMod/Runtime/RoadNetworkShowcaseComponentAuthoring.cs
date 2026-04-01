using Ludots.Core.Config;

namespace RoadNetworkShowcaseMod.Runtime
{
    internal static class RoadNetworkShowcaseComponentAuthoring
    {
        public static void Register()
        {
            ComponentRegistry.Register<RoadColumnTag>("RoadColumnTag");
            ComponentRegistry.Register<RoadFortTag>("RoadFortTag");
            ComponentRegistry.Register<RoadAiControlledTag>("RoadAiControlledTag");
            ComponentRegistry.Register<RoadMoveProfileRef>("RoadMoveProfileRef");
            ComponentRegistry.Register<RoadFortControlState>("RoadFortControlState");
        }
    }
}
