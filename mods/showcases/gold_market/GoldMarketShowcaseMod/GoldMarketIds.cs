namespace GoldMarketShowcaseMod;

public static class GoldMarketIds
{
    public const string ShowcaseMapId = "gold_market_showcase";
    public const string InstalledKey = "GoldMarketShowcase.Installed";
    public const string RuntimeServiceKey = "GoldMarketShowcase.Runtime";
    public const string BuyActionId = "GoldMarket.Buy";
    public const string ExpensiveActionId = "GoldMarket.Expensive";
    public const string FailureActionId = "GoldMarket.AtomicFailure";
    public const string RefillActionId = "GoldMarket.Refill";

    public static bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, ShowcaseMapId, System.StringComparison.OrdinalIgnoreCase);
    }
}
