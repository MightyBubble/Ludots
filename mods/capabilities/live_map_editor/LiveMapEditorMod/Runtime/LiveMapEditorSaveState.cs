namespace LiveMapEditorMod.Runtime;

internal sealed class LiveMapEditorSaveState
{
    public string Status { get; set; } = "idle";
    public string Message { get; set; } = string.Empty;
    public string MapConfigPath { get; set; } = string.Empty;
    public int EntityCount { get; set; }
    public int NavTileCount { get; set; }
}
