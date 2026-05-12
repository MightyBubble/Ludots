using Ludots.Core.Config;
using Ludots.Core.Layers;

namespace MassNavigationTotalWarEntryMod.Runtime;

internal static class TotalWarShowcaseComponentAuthoring
{
    public static void Register()
    {
        LayerRegistry.Register(TotalWarShowcaseLayerNames.FormationAgent);
        LayerRegistry.Register(TotalWarShowcaseLayerNames.SoldierAgent);
        ComponentRegistry.Register<TotalWarFormationSoldier>("TotalWarFormationSoldier");
        ComponentRegistry.Register<TotalWarFormationAgent>("TotalWarFormationAgent");
        ComponentRegistry.Register<TotalWarFormationState>("TotalWarFormationState");
        ComponentRegistry.Register<TotalWarFormationOutline>("TotalWarFormationOutline");
        ComponentRegistry.Register<TotalWarObstacleOverlay>("TotalWarObstacleOverlay");
    }
}
