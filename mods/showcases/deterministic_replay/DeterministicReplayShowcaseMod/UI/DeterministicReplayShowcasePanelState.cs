using System;
using System.Collections.Generic;

namespace DeterministicReplayShowcaseMod.UI;

public sealed record DeterministicReplayShowcasePanelState(
    string Header,
    string Summary,
    string Controls,
    string Status,
    string? Error,
    string Mode,
    string ArchivePath,
    int SchemaVersion,
    int Tick,
    int TotalFrames,
    int PlaybackIndex,
    int Speed,
    bool Recording,
    bool Playing,
    bool Paused,
    string RecordingDigest,
    string PlaybackDigest,
    string Compare,
    IReadOnlyList<string> HashRows,
    IReadOnlyList<string> LogLines)
{
    public bool Equals(DeterministicReplayShowcasePanelState? other)
    {
        if (other is null) return false;
        return Status == other.Status && Error == other.Error && Mode == other.Mode
            && Tick == other.Tick && PlaybackIndex == other.PlaybackIndex && Speed == other.Speed
            && Recording == other.Recording && Playing == other.Playing && Paused == other.Paused
            && RecordingDigest == other.RecordingDigest && PlaybackDigest == other.PlaybackDigest
            && Compare == other.Compare && HashRows.Count == other.HashRows.Count;
    }

    public override int GetHashCode() => HashCode.Combine(Status, Tick, PlaybackIndex, RecordingDigest, PlaybackDigest, Compare);
}
