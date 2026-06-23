using System.Collections.Generic;

namespace FourXAssociationShowcaseMod.UI;

internal sealed record FourXAssociationPanelState(
    string Header,
    string Summary,
    string Controls,
    string Status,
    IReadOnlyList<string> ContractLines,
    IReadOnlyList<string> LogLines);
