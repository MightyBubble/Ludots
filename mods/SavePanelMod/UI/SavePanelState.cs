using System;
using System.Collections.Generic;

namespace SavePanelMod.UI;

public sealed record SavePanelSlotRow(
    string Kind,
    string Name,
    string Slot,
    int Tick,
    string MapId,
    string CreatedUtc,
    int Bytes,
    int SchemaVersion,
    string ModSetHashShort,
    string RegistryFingerprintShort);

public sealed record SavePanelState(
    string Header,
    string Summary,
    string Controls,
    string Status,
    string? Error,
    string StorageRoot,
    string ManualName,
    string? SelectedSlot,
    bool PendingCapture,
    IReadOnlyList<SavePanelSlotRow> Slots,
    IReadOnlyList<string> AutosaveLines)
{
    public bool Equals(SavePanelState? other)
    {
        if (other is null) return false;
        if (!string.Equals(Header, other.Header, StringComparison.Ordinal)) return false;
        if (!string.Equals(Summary, other.Summary, StringComparison.Ordinal)) return false;
        if (!string.Equals(Controls, other.Controls, StringComparison.Ordinal)) return false;
        if (!string.Equals(Status, other.Status, StringComparison.Ordinal)) return false;
        if (!string.Equals(Error, other.Error, StringComparison.Ordinal)) return false;
        if (!string.Equals(StorageRoot, other.StorageRoot, StringComparison.Ordinal)) return false;
        if (!string.Equals(ManualName, other.ManualName, StringComparison.Ordinal)) return false;
        if (!string.Equals(SelectedSlot, other.SelectedSlot, StringComparison.Ordinal)) return false;
        if (PendingCapture != other.PendingCapture) return false;
        if (Slots.Count != other.Slots.Count) return false;
        if (AutosaveLines.Count != other.AutosaveLines.Count) return false;
        for (int i = 0; i < Slots.Count; i++)
        {
            if (!Slots[i].Equals(other.Slots[i])) return false;
        }

        for (int i = 0; i < AutosaveLines.Count; i++)
        {
            if (!string.Equals(AutosaveLines[i], other.AutosaveLines[i], StringComparison.Ordinal)) return false;
        }

        return true;
    }

    public override int GetHashCode() => HashCode.Combine(Header, Status, Error, SelectedSlot, Slots.Count, PendingCapture);
}
