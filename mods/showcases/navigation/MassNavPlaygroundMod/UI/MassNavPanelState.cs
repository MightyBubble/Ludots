namespace MassNavPlaygroundMod.UI;

internal readonly record struct MassNavPanelState(
    bool Visible,
    int TotalAgents,
    int ControllableAgents,
    int Blockers,
    int SelectedCount,
    uint SelectionRevision,
    float Fps,
    float FrameMs,
    int SelectionSnapshotsFrame,
    int StructuralChangesFrame,
    int FlowReconcileFrame)
{
    public static MassNavPanelState Empty => new(
        Visible: false,
        TotalAgents: 0,
        ControllableAgents: 0,
        Blockers: 0,
        SelectedCount: 0,
        SelectionRevision: 0,
        Fps: 0f,
        FrameMs: 0f,
        SelectionSnapshotsFrame: 0,
        StructuralChangesFrame: 0,
        FlowReconcileFrame: 0);
}
