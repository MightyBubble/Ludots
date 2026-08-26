using System;
using System.Collections.Generic;

namespace ReconnectRecoveryShowcaseMod.UI;

public sealed record ReconnectRecoveryShowcasePanelState(
    string Header,
    string Banner,
    string Summary,
    string Controls,
    string Status,
    string? Error,
    string Ablation,
    string RecoverySource,
    int AuthorityTick,
    int ClientTick,
    long NextSequence,
    bool Disconnected,
    string LastFault,
    string Timeline,
    IReadOnlyList<string> LogLines)
{
    public bool Equals(ReconnectRecoveryShowcasePanelState? other)
    {
        if (other is null) return false;
        return Status == other.Status && Error == other.Error && AuthorityTick == other.AuthorityTick
            && ClientTick == other.ClientTick && Disconnected == other.Disconnected
            && RecoverySource == other.RecoverySource && LastFault == other.LastFault
            && Timeline == other.Timeline;
    }

    public override int GetHashCode() => HashCode.Combine(Status, AuthorityTick, ClientTick, Disconnected, LastFault, Timeline);
}
