using Ludots.Core.Map;

namespace SanguoGrandStrategyMod;

public static class SanguoGrandStrategyIds
{
    public const string InstalledKey = "SanguoGrandStrategyMod.Installed";
    public const string RuntimeServiceKey = "SanguoGrandStrategyMod.Runtime";
    public const string ShowcaseMapId = "sanguo_grand_strategy_china";
    public const string TopicName = "ludots.showcase.sanguo.world";

    public static readonly MapId ShowcaseMap = new(ShowcaseMapId);

    public static bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, ShowcaseMapId, StringComparison.OrdinalIgnoreCase);
    }
}
