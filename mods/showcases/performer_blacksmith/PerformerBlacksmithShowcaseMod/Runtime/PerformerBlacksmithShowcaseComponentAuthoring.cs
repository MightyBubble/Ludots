using Ludots.Core.Config;

namespace PerformerBlacksmithShowcaseMod.Runtime
{
    internal static class PerformerBlacksmithShowcaseComponentAuthoring
    {
        public static void Register(string modId)
        {
            ComponentRegistry.Register<DynamicWorkerCrowdTag>("DynamicWorkerCrowdTag", modId);
            ComponentRegistry.Register<MinimapMarkerBallMovementTag>("MinimapMarkerBallMovementTag", modId);
        }
    }
}
