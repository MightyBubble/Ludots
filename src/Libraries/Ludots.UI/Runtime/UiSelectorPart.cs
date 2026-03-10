using System.Collections.Generic;

namespace Ludots.UI.Runtime;

public sealed record UiSelectorPart(string? TagName, string? Id, IReadOnlyList<string> Classes, IReadOnlyList<UiSelectorAttribute> Attributes, IReadOnlyList<UiStructuralPseudo> StructuralPseudos, IReadOnlyList<UiSelectorLogicalPseudo> LogicalPseudos, UiPseudoState PseudoState, UiSelectorCombinator Combinator);
