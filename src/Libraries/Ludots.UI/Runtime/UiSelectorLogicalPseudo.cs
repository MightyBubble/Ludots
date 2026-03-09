using System;
using System.Collections.Generic;
using System.Linq;

namespace Ludots.UI.Runtime;

public sealed class UiSelectorLogicalPseudo
{
	public UiSelectorLogicalPseudoKind Kind { get; }

	public IReadOnlyList<UiSelector> Selectors { get; }

	public int Specificity => (Kind != UiSelectorLogicalPseudoKind.Where) ? Selectors.Max((UiSelector selector) => selector.Specificity) : 0;

	public UiSelectorLogicalPseudo(UiSelectorLogicalPseudoKind kind, IReadOnlyList<UiSelector> selectors)
	{
		if (selectors == null || selectors.Count == 0)
		{
			throw new ArgumentException("Logical pseudo requires at least one selector.", "selectors");
		}
		Kind = kind;
		Selectors = selectors;
	}
}
