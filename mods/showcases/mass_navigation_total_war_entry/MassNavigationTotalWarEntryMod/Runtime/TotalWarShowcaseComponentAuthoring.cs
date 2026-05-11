using Ludots.Core.Config;

namespace MassNavigationTotalWarEntryMod.Runtime;

internal static class TotalWarShowcaseComponentAuthoring
{
    public static void Register()
    {
        ComponentRegistry.Register<TotalWarFormationSoldier>("TotalWarFormationSoldier");
        ComponentRegistry.Register<TotalWarFormationAnchor>("TotalWarFormationAnchor");
        ComponentRegistry.Register<TotalWarFormationState>("TotalWarFormationState");
        ComponentRegistry.Register<TotalWarFormationOutline>("TotalWarFormationOutline");
    }
}
