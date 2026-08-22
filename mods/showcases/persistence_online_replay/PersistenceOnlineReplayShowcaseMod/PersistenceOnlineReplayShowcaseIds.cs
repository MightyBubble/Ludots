namespace PersistenceOnlineReplayShowcaseMod;

internal static class PersistenceOnlineReplayShowcaseIds
{
    public const string InstalledKey = "PersistenceOnlineReplayShowcase.Installed";
    public const string RuntimeKey = "PersistenceOnlineReplayShowcase.Runtime";
    public const string InputContext = "PersistenceOnlineReplayShowcase.Controls";
    public const string MapId = "persistence_online_replay";
    public const string RequestCheckpoint = "PersistenceOnlineReplay.RequestCheckpoint";
    public const string SaveSlot = "PersistenceOnlineReplay.SaveSlot";
    public const string RestoreSlot = "PersistenceOnlineReplay.RestoreSlot";
    public const string StartRecording = "PersistenceOnlineReplay.StartRecording";
    public const string StopRecording = "PersistenceOnlineReplay.StopRecording";
    public const string PlayReplay = "PersistenceOnlineReplay.PlayReplay";
    public const string SimulateDisconnect = "PersistenceOnlineReplay.SimulateDisconnect";
    public const string Reconnect = "PersistenceOnlineReplay.Reconnect";
    public const string AblateFrame = "PersistenceOnlineReplay.AblateFrame";
    public const string ToggleReplayPause = "PersistenceOnlineReplay.ToggleReplayPause";
    public const string StepReplay = "PersistenceOnlineReplay.StepReplay";
    public const string ResetReplay = "PersistenceOnlineReplay.ResetReplay";
    public static bool IsShowcaseMap(string? mapId) => string.Equals(mapId, MapId, System.StringComparison.OrdinalIgnoreCase);
}
