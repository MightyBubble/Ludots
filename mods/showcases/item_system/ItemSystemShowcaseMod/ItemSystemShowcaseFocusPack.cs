namespace ItemSystemShowcaseMod;

public static class ItemSystemShowcaseFocusPack
{
    public const string Key = "ItemSystemShowcaseMod.FocusPack";
    public const string AllInOne = "all_in_one";
    public const string Loadout = "loadout";
    public const string WeaponBench = "weapon_bench";
    public const string ForgeSocket = "forge_socket";
    public const string RaidLoop = "raid_loop";

    public static string Normalize(string? focusPack)
    {
        return focusPack switch
        {
            Loadout => Loadout,
            WeaponBench => WeaponBench,
            ForgeSocket => ForgeSocket,
            RaidLoop => RaidLoop,
            _ => AllInOne
        };
    }

    public static bool IsFocused(string? focusPack)
    {
        return Normalize(focusPack) != AllInOne;
    }

    public static string GetStartupMapId(string? focusPack)
    {
        return Normalize(focusPack) switch
        {
            Loadout => ItemSystemShowcaseIds.LoadoutGarageMapId,
            WeaponBench => ItemSystemShowcaseIds.WeaponBenchMapId,
            ForgeSocket => ItemSystemShowcaseIds.ForgeSocketLabMapId,
            RaidLoop => ItemSystemShowcaseIds.RaidLoopMapId,
            _ => ItemSystemShowcaseIds.HubMapId
        };
    }
}
