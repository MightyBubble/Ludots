namespace DiplomacyTradeGateShowcaseMod.UI;

internal sealed record DiplomacyTradeGatePanelState(
    string Header,
    string Summary,
    string Controls,
    string Status,
    string[] Lines,
    string[] LogLines);
