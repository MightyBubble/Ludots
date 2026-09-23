using Ludots.Core.Config;
using ThreeKingdomsScenarioMod.Gameplay;

namespace ThreeKingdomsScenarioMod.Runtime;

internal static class ThreeKingdomsScenarioComponentAuthoring
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        ComponentRegistry.Register<ThreeKingdomsAttachmentHost>("ThreeKingdomsAttachmentHost");
        ComponentRegistry.Register<ThreeKingdomsWallFeature>("ThreeKingdomsWallFeature");
        ComponentRegistry.Register<ThreeKingdomsGateFeature>("ThreeKingdomsGateFeature");
        ComponentRegistry.Register<ThreeKingdomsLadderFeature>("ThreeKingdomsLadderFeature");
        ComponentRegistry.Register<ThreeKingdomsTunnelFeature>("ThreeKingdomsTunnelFeature");
        ComponentRegistry.Register<ThreeKingdomsTrenchFeature>("ThreeKingdomsTrenchFeature");
    }
}
