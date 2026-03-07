namespace Ludots.UI.Runtime;

public sealed record UiSelectorPart(
    string? TagName,
    string? Id,
    IReadOnlyList<string> Classes,
    IReadOnlyList<UiSelectorAttribute> Attributes,
    UiPseudoState PseudoState,
    UiSelectorCombinator Combinator);
