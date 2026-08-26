namespace SaveLoadShowcaseMod;

public static class SaveLoadShowcaseIds
{
    public const string InstalledKey = "SaveLoadShowcase.Installed";
    public const string RuntimeKey = "SaveLoadShowcase.Runtime";
    public const string MapId = "save_load";
    public const string InputContext = "SaveLoadShowcase.Controls";

    public const string PatrolInstanceId = "save_load_patrol";
    public const string PatrolName = "巡逻兵";
    public const string ScoutName = "临时侦察";
    public const string SlotName = "showcase";

    public const string MoveNorth = "SaveLoad.MoveNorth";
    public const string MoveSouth = "SaveLoad.MoveSouth";
    public const string MoveWest = "SaveLoad.MoveWest";
    public const string MoveEast = "SaveLoad.MoveEast";
    public const string QuickSave = "SaveLoad.QuickSave";
    public const string QuickLoad = "SaveLoad.QuickLoad";
    public const string AblateReset = "SaveLoad.AblateReset";
    public const string TamperSlot = "SaveLoad.TamperSlot";
    public const string ToggleExclude = "SaveLoad.ToggleExclude";
    public const string ColdStartStory = "SaveLoad.ColdStartStory";
    public const string RetentionDown = "SaveLoad.RetentionDown";
    public const string RetentionUp = "SaveLoad.RetentionUp";

    public const int OverlayCurrent = 1205_001;
    public const int OverlaySaved = 1205_002;
    public const int OverlayScout = 1205_003;

    public const int MoveStepCm = 400;

    public static bool IsShowcaseMap(string? mapId) =>
        string.Equals(mapId, MapId, StringComparison.Ordinal);
}
