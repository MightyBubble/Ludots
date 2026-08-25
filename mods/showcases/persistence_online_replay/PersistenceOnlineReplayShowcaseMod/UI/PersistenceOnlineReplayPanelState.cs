namespace PersistenceOnlineReplayShowcaseMod.UI;

internal sealed record PersistenceOnlineReplayPanelState(
    string Header,
    string Summary,
    string Status,
    string[] Metrics,
    string[] Controls,
    string[] LogLines);
