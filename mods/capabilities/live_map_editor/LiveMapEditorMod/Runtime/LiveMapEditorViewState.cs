namespace LiveMapEditorMod.Runtime;

internal sealed class LiveMapEditorViewState
{
    public bool ShowGrid { get; set; } = true;
    public bool ShowChunks { get; set; } = true;
    public bool ShowNavMesh { get; set; } = true;
    public bool ShowPath { get; set; } = true;
    public bool ShowTransport { get; set; } = true;
    public bool ShowEntities { get; set; } = true;
    public bool ShowMinimap { get; set; } = true;
}
