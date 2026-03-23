namespace ItemSystemShowcaseMod;

internal static class ItemSystemShowcaseIds
{
    public const string HubMapId = "item_system_showcase_hub";
    public const string InstalledKey = "ItemSystemShowcaseMod.Installed";
    public const string RuntimeKey = "ItemSystemShowcaseMod.Runtime";

    public static bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, HubMapId, System.StringComparison.OrdinalIgnoreCase);
    }
}
