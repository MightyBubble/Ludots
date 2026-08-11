using Ludots.Core.Config;

namespace CapabilityStandardStaticPresenter30kMod.Runtime
{
    internal static class CapabilityStandardStaticPresenter30kComponentAuthoring
    {
        public static void Register(string modId)
        {
            ComponentRegistry.Register<DynamicWorkerCrowdTag>("DynamicWorkerCrowdTag", modId);
            ComponentRegistry.Register<MinimapMarkerBallMovementTag>("MinimapMarkerBallMovementTag", modId);
        }
    }
}
