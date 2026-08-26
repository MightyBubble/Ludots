namespace ReconnectRecoveryShowcaseMod;

public static class ReconnectRecoveryShowcaseIds
{
    public const string InstalledKey = "ReconnectRecoveryShowcase.Installed";
    public const string RuntimeKey = "ReconnectRecoveryShowcase.Runtime";
    public const string MapId = "reconnect_recovery";
    public const string InputContext = "ReconnectRecoveryShowcase.Controls";
    public const string Checkpoint = "Reconnect.Checkpoint";
    public const string Disconnect = "Reconnect.Disconnect";
    public const string ReconnectAuthority = "Reconnect.Authority";
    public const string ReconnectReset = "Reconnect.LocalReset";
    public const string InjectMissing = "Reconnect.InjectMissing";
    public const string InjectDuplicate = "Reconnect.InjectDuplicate";
    public const string InjectStale = "Reconnect.InjectStale";
    public const string InjectOutOfOrder = "Reconnect.InjectOutOfOrder";
    public const string AdvanceAuthority = "Reconnect.AdvanceAuthority";

    public static bool IsShowcaseMap(string? mapId) =>
        string.Equals(mapId, MapId, StringComparison.Ordinal);
}
