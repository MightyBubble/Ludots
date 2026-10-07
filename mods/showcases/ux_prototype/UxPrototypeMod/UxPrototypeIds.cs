using System;

namespace UxPrototypeMod;

internal static class UxPrototypeIds
{
    public const string BattleMapId = "ux_prototype_battle";
    public const string PlayModeId = "UxPrototype.Mode.Play";

    public const string PlayModeSwitchActionId = "ModePlay";
    public const string RoadEditorModeSwitchActionId = "ModeRoad";
    public const string ObstacleEditorModeSwitchActionId = "ModeObstacle";
    public const string NavmeshModeSwitchActionId = "ModeNavmesh";
    public const string RoadEditorModeId = "UxPrototype.Mode.RoadEditor";
    public const string ObstacleEditorModeId = "UxPrototype.Mode.ObstacleEditor";
    public const string NavmeshModeId = "UxPrototype.Mode.Navmesh";

    public const string FactionDiplomacy = "diplomacy";
    public const string FactionPersonnel = "personnel";
    public const string FactionPublic = "public";
    public const string FactionTech = "tech";
    public const string FactionTrade = "trade";

    public const string RosterEconomy = "economy";
    public const string RosterProduction = "production";
    public const string RosterDefense = "defense";
    public const string RosterTech = "tech";
    public const string RosterUnits = "units";
    public const string RosterSiege = "siege";
    public const string RosterEquipment = "equipment";
    public const string RosterStratagems = "stratagems";

    public const string GlobalBuild = "build";
    public const string GlobalUnits = "units";
    public const string GlobalDefense = "defense";
    public const string GlobalEditors = "editors";

    public static readonly string[] RosterTabs =
    {
        RosterEconomy,
        RosterProduction,
        RosterDefense,
        RosterTech,
        RosterUnits,
        RosterSiege,
        RosterEquipment,
        RosterStratagems
    };

    public static readonly string[] FactionTabs =
    {
        FactionDiplomacy,
        FactionPersonnel,
        FactionPublic,
        FactionTech,
        FactionTrade
    };

    public static readonly string[] GlobalTabs =
    {
        GlobalBuild,
        GlobalUnits,
        GlobalDefense,
        GlobalEditors
    };

    public static readonly string[] BuildTemplates =
    {
        "ux_farm",
        "ux_mine",
        "ux_barracks",
        "ux_stable",
        "ux_workshop",
        "ux_tower",
        "ux_watchtower",
        "ux_fort"
    };

    public static readonly string[] UnitTemplates =
    {
        "ux_worker",
        "ux_soldier",
        "ux_archer",
        "ux_mage",
        "ux_medic",
        "ux_heavy_cavalry",
        "ux_horse_archer",
        "ux_ram",
        "ux_catapult",
        "ux_well_column"
    };

    public static bool IsPrototypeMap(string? mapId) =>
        string.Equals(mapId, BattleMapId, StringComparison.OrdinalIgnoreCase);

    public static bool IsPrototypeMode(string? modeId) =>
        string.Equals(modeId, PlayModeId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(modeId, RoadEditorModeId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(modeId, ObstacleEditorModeId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(modeId, NavmeshModeId, StringComparison.OrdinalIgnoreCase);
}
