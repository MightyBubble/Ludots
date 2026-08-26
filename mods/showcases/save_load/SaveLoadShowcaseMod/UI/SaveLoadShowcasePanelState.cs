using System;
using System.Collections.Generic;

namespace SaveLoadShowcaseMod.UI;

public sealed record SaveLoadShowcasePanelState(
    string Header,
    string Summary,
    string Controls,
    string Status,
    string? Error,
    string Ablation,
    string StorageRoot,
    bool ExcludeEphemeral,
    int AutosaveRetention,
    string BeforeDigest,
    string AfterDigest,
    int BeforeEntityCount,
    int AfterEntityCount,
    IReadOnlyList<string> DiffLines,
    IReadOnlyList<string> LogLines)
{
    public bool Equals(SaveLoadShowcasePanelState? other)
    {
        if (other is null) return false;
        if (!string.Equals(Status, other.Status, StringComparison.Ordinal)) return false;
        if (!string.Equals(Error, other.Error, StringComparison.Ordinal)) return false;
        if (!string.Equals(Ablation, other.Ablation, StringComparison.Ordinal)) return false;
        if (ExcludeEphemeral != other.ExcludeEphemeral) return false;
        if (AutosaveRetention != other.AutosaveRetention) return false;
        if (BeforeEntityCount != other.BeforeEntityCount || AfterEntityCount != other.AfterEntityCount) return false;
        if (!string.Equals(BeforeDigest, other.BeforeDigest, StringComparison.Ordinal)) return false;
        if (!string.Equals(AfterDigest, other.AfterDigest, StringComparison.Ordinal)) return false;
        if (DiffLines.Count != other.DiffLines.Count || LogLines.Count != other.LogLines.Count) return false;
        for (int i = 0; i < DiffLines.Count; i++)
            if (!string.Equals(DiffLines[i], other.DiffLines[i], StringComparison.Ordinal)) return false;
        return true;
    }

    public override int GetHashCode() => HashCode.Combine(Status, Error, Ablation, AutosaveRetention, BeforeDigest, AfterDigest);
}
