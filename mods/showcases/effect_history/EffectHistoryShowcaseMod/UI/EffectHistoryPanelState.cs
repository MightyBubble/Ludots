using System.Collections.Generic;

namespace EffectHistoryShowcaseMod.UI;

internal sealed record EffectHistoryPanelState(
    string Header,
    string Summary,
    string Controls,
    string Mode,
    string Identity,
    string Replacement,
    string Knowledge,
    string Result,
    IReadOnlyList<string> History);
