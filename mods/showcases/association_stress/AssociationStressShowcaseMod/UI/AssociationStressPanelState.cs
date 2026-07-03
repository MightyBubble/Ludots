namespace AssociationStressShowcaseMod.UI;

internal sealed record AssociationStressPanelState(
    string Header,
    string Summary,
    string Controls,
    string ScaleLine,
    string[] Metrics,
    string[] LogLines);
