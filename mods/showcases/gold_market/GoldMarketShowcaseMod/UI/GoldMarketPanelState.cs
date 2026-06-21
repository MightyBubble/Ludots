namespace GoldMarketShowcaseMod.UI;

internal readonly record struct GoldMarketPanelState(
    string Header,
    string Summary,
    string Controls,
    string Status,
    string[] Lines,
    string[] LogLines);
