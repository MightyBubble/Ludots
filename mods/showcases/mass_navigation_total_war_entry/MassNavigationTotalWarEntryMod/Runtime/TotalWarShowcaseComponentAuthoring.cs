using Ludots.Core.Config;
using Ludots.Core.Layers;

namespace MassNavigationTotalWarEntryMod.Runtime;

internal static class TotalWarShowcaseComponentAuthoring
{
    public static void Register(string modId)
    {
        LayerRegistry.Register(TotalWarShowcaseLayerNames.FormationAgent);
        LayerRegistry.Register(TotalWarShowcaseLayerNames.SoldierAgent);
        ComponentRegistry.Register<TotalWarFormationSoldier>("TotalWarFormationSoldier", modId);
        ComponentRegistry.Register<TotalWarFormationAgent>("TotalWarFormationAgent", modId);
        ComponentRegistry.Register<TotalWarFormationState>("TotalWarFormationState", modId);
        ComponentRegistry.Register<TotalWarFormationOutline>("TotalWarFormationOutline", modId);
        ComponentRegistry.Register<TotalWarObstacleOverlay>("TotalWarObstacleOverlay", modId);
    }
}
