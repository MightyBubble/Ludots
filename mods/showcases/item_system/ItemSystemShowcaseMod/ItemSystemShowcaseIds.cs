using ItemSystemShowcaseMod.UI;

namespace ItemSystemShowcaseMod;

internal static class ItemSystemShowcaseIds
{
    public const string HubMapId = "item_system_showcase_hub";
    public const string LoadoutGarageMapId = "item_system_showcase_loadout_garage";
    public const string WeaponBenchMapId = "item_system_showcase_weapon_bench";
    public const string RaidLoopMapId = "item_system_showcase_raid_loop";
    public const string InstalledKey = "ItemSystemShowcaseMod.Installed";
    public const string RuntimeKey = "ItemSystemShowcaseMod.Runtime";

    public static bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, HubMapId, System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mapId, LoadoutGarageMapId, System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mapId, WeaponBenchMapId, System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mapId, RaidLoopMapId, System.StringComparison.OrdinalIgnoreCase);
    }

    public static ItemSystemShowcaseSceneKind GetSceneKind(string? mapId)
    {
        if (string.Equals(mapId, LoadoutGarageMapId, System.StringComparison.OrdinalIgnoreCase))
        {
            return ItemSystemShowcaseSceneKind.LoadoutGarage;
        }

        if (string.Equals(mapId, WeaponBenchMapId, System.StringComparison.OrdinalIgnoreCase))
        {
            return ItemSystemShowcaseSceneKind.WeaponBench;
        }

        if (string.Equals(mapId, RaidLoopMapId, System.StringComparison.OrdinalIgnoreCase))
        {
            return ItemSystemShowcaseSceneKind.RaidLoop;
        }

        return ItemSystemShowcaseSceneKind.Hub;
    }
}
