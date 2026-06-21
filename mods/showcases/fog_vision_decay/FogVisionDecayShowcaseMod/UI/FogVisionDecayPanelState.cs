namespace FogVisionDecayShowcaseMod.UI;

internal sealed record FogVisionDecayPanelState(
    string Header,
    string Summary,
    string Controls,
    string StatusLine,
    string[] Metrics,
    string[] ContactLines);
