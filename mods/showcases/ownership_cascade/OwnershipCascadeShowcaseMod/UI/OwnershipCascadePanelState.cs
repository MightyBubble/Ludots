namespace OwnershipCascadeShowcaseMod.UI;

internal sealed record OwnershipCascadePanelState(
    string Header,
    string Summary,
    string Controls,
    string Status,
    string[] Lines,
    string[] LogLines);
