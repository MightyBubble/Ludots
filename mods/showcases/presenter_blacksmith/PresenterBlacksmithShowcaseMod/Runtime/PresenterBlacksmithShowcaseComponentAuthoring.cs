using Ludots.Core.Config;

namespace PresenterBlacksmithShowcaseMod.Runtime
{
    internal static class PresenterBlacksmithShowcaseComponentAuthoring
    {
        public static void Register(string modId)
        {
            ComponentRegistry.Register<DynamicWorkerCrowdTag>("DynamicWorkerCrowdTag", modId);
            ComponentRegistry.Register<MinimapMarkerBallMovementTag>("MinimapMarkerBallMovementTag", modId);
        }
    }
}
