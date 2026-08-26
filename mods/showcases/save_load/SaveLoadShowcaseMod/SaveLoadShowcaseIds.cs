namespace SaveLoadShowcaseMod;

public static class SaveLoadShowcaseIds
{
    public const string InstalledKey = "SaveLoadShowcase.Installed";
    public const string RuntimeKey = "SaveLoadShowcase.Runtime";
    public const string MapId = "save_load";
    public const string InputContext = "SaveLoadShowcase.Controls";
    public const string AblateReset = "SaveLoad.AblateReset";
    public const string AblateRestore = "SaveLoad.AblateRestore";
    public const string TamperSlot = "SaveLoad.TamperSlot";
    public const string ToggleExclude = "SaveLoad.ToggleExclude";
    public const string NudgeWorld = "SaveLoad.NudgeWorld";
    public const string ColdStartStory = "SaveLoad.ColdStartStory";
    public const string RetentionDown = "SaveLoad.RetentionDown";
    public const string RetentionUp = "SaveLoad.RetentionUp";

    public static bool IsShowcaseMap(string? mapId) =>
        string.Equals(mapId, MapId, StringComparison.Ordinal);
}
