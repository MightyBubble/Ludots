using Ludots.Core.Map.Authoring;

namespace LiveMapEditorMod.Runtime;

internal sealed class LiveMapEditorMapState
{
    public string SelectedBoardName { get; set; } = "default";
    public string Status { get; set; } = "idle";
    public string Message { get; set; } = string.Empty;
    public bool ReloadRequired { get; set; }
    public string TargetModId { get; set; } = string.Empty;
    public string MapConfigPath { get; set; } = string.Empty;
    public BoardAllocationPreview? CreateMapPreview { get; set; }
    public BoardAllocationPreview? AddBoardPreview { get; set; }
}
