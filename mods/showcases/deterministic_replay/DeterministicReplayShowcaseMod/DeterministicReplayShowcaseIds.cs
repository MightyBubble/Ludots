namespace DeterministicReplayShowcaseMod;

public static class DeterministicReplayShowcaseIds
{
    public const string InstalledKey = "DeterministicReplayShowcase.Installed";
    public const string RuntimeKey = "DeterministicReplayShowcase.Runtime";
    public const string MapId = "deterministic_replay";
    public const string InputContext = "DeterministicReplayShowcase.Controls";
    public const string RequestCheckpoint = "DetReplay.Checkpoint";
    public const string StartRecording = "DetReplay.StartRecording";
    public const string StopRecording = "DetReplay.StopRecording";
    public const string Play = "DetReplay.Play";
    public const string Pause = "DetReplay.Pause";
    public const string Step = "DetReplay.Step";
    public const string Reset = "DetReplay.Reset";
    public const string Speed = "DetReplay.Speed";
    public const string JumpMid = "DetReplay.JumpMid";
    public const string InjectDuringPlay = "DetReplay.InjectDuringPlay";
    public const string SnapshotAblation = "DetReplay.SnapshotAblation";

    public static bool IsShowcaseMap(string? mapId) =>
        string.Equals(mapId, MapId, StringComparison.Ordinal);
}
