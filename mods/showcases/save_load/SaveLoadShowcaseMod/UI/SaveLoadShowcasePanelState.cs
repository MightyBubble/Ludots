using System;
using System.Collections.Generic;

namespace SaveLoadShowcaseMod.UI;

public sealed record SaveLoadShowcasePanelState(
    string Header,
    string Hook,
    string StepGuide,
    int StepIndex,
    string Controls,
    string Status,
    string? Error,
    string Outcome,
    string Ablation,
    string StorageRoot,
    bool ExcludeScout,
    int AutosaveRetention,
    string PatrolNow,
    string SavedPoint,
    int MoveCount,
    bool HasSavedPoint,
    IReadOnlyList<string> LogLines)
{
    public bool Equals(SaveLoadShowcasePanelState? other)
    {
        if (other is null) return false;
        return Status == other.Status
            && Error == other.Error
            && Outcome == other.Outcome
            && StepIndex == other.StepIndex
            && PatrolNow == other.PatrolNow
            && SavedPoint == other.SavedPoint
            && MoveCount == other.MoveCount
            && HasSavedPoint == other.HasSavedPoint
            && ExcludeScout == other.ExcludeScout
            && AutosaveRetention == other.AutosaveRetention
            && Ablation == other.Ablation;
    }

    public override int GetHashCode() =>
        HashCode.Combine(Status, Outcome, StepIndex, PatrolNow, SavedPoint, MoveCount, HasSavedPoint, Ablation);
}
