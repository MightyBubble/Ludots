using Ludots.Core.Config;

namespace CapabilityStandardStaticPerformer30kMod.Runtime
{
    internal static class CapabilityStandardStaticPerformer30kComponentAuthoring
    {
        public static void Register(string modId)
        {
            ComponentRegistry.Register<DynamicWorkerCrowdTag>("DynamicWorkerCrowdTag", modId);
            ComponentRegistry.Register<MinimapMarkerBallMovementTag>("MinimapMarkerBallMovementTag", modId);
        }
    }
}
