using Ludots.Core.Config;
using Ludots.Core.Layers;

namespace CapabilityStandardTotalWarLikeMod.Runtime;

internal static class CapabilityStandardTotalWarLikeComponentAuthoring
{
    public static void Register(string modId)
    {
        LayerRegistry.Register(CapabilityStandardTotalWarLikeLayerNames.FormationAgent);
        LayerRegistry.Register(CapabilityStandardTotalWarLikeLayerNames.SoldierAgent);
        ComponentRegistry.Register<CapabilityStandardTotalWarLikeFormationSoldier>("CapabilityStandardTotalWarLikeFormationSoldier", modId);
        ComponentRegistry.Register<CapabilityStandardTotalWarLikeFormationAgent>("CapabilityStandardTotalWarLikeFormationAgent", modId);
        ComponentRegistry.Register<CapabilityStandardTotalWarLikeFormationState>("CapabilityStandardTotalWarLikeFormationState", modId);
        ComponentRegistry.Register<CapabilityStandardTotalWarLikeFormationOutline>("CapabilityStandardTotalWarLikeFormationOutline", modId);
        ComponentRegistry.Register<CapabilityStandardTotalWarLikeObstacleOverlay>("CapabilityStandardTotalWarLikeObstacleOverlay", modId);
    }
}
