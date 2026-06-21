namespace DiplomacyTradeGateShowcaseMod;

public static class DiplomacyTradeGateIds
{
    public const string ShowcaseMapId = "diplomacy_trade_gate_showcase";
    public const string InstalledKey = "DiplomacyTradeGateShowcase.Installed";
    public const string RuntimeServiceKey = "DiplomacyTradeGateShowcase.Runtime";
    public const string TryTradeActionId = "DiplomacyTradeGate.TryTrade";
    public const string SignPactActionId = "DiplomacyTradeGate.SignPact";
    public const string EmbargoActionId = "DiplomacyTradeGate.Embargo";
    public const string ClearEmbargoActionId = "DiplomacyTradeGate.ClearEmbargo";

    public static bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, ShowcaseMapId, System.StringComparison.OrdinalIgnoreCase);
    }
}
