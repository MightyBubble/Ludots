using System;
using System.Collections.Generic;
using System.Linq;

namespace Ludots.UI.Runtime;

public static class UiElementSelectorMatcher
{
	private const UiPseudoState RuntimePseudoMask = UiPseudoState.Hover | UiPseudoState.Active | UiPseudoState.Focus | UiPseudoState.Disabled | UiPseudoState.Checked | UiPseudoState.Selected | UiPseudoState.Required | UiPseudoState.Invalid;

	public static bool Matches(UiElement element, UiSelector selector)
	{
		ArgumentNullException.ThrowIfNull(element, "element");
		ArgumentNullException.ThrowIfNull(selector, "selector");
		return Matches(element, selector.Parts.Count - 1, selector.Parts);
	}

	private static bool Matches(UiElement? element, int selectorIndex, IReadOnlyList<UiSelectorPart> parts)
	{
		if (element == null)
		{
			return false;
		}
		if (selectorIndex < 0)
		{
			return true;
		}
		UiSelectorPart uiSelectorPart = parts[selectorIndex];
		if (!MatchesPart(element, uiSelectorPart))
		{
			return false;
		}
		if (selectorIndex == 0)
		{
			return true;
		}
		UiSelectorCombinator combinator = uiSelectorPart.Combinator;
		if (1 == 0)
		{
		}
		bool result = combinator switch
		{
			UiSelectorCombinator.Child => Matches(element.Parent, selectorIndex - 1, parts), 
			UiSelectorCombinator.AdjacentSibling => Matches(GetPreviousSibling(element), selectorIndex - 1, parts), 
			UiSelectorCombinator.GeneralSibling => MatchesAnyPreviousSibling(element, selectorIndex - 1, parts), 
			_ => MatchesAnyAncestor(element.Parent, selectorIndex - 1, parts), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static bool MatchesAnyAncestor(UiElement? ancestor, int selectorIndex, IReadOnlyList<UiSelectorPart> parts)
	{
		while (ancestor != null)
		{
			if (Matches(ancestor, selectorIndex, parts))
			{
				return true;
			}
			ancestor = ancestor.Parent;
		}
		return false;
	}

	private static bool MatchesAnyPreviousSibling(UiElement element, int selectorIndex, IReadOnlyList<UiSelectorPart> parts)
	{
		for (UiElement previousSibling = GetPreviousSibling(element); previousSibling != null; previousSibling = GetPreviousSibling(previousSibling))
		{
			if (Matches(previousSibling, selectorIndex, parts))
			{
				return true;
			}
		}
		return false;
	}

	private static bool MatchesPart(UiElement element, UiSelectorPart part)
	{
		if (!string.IsNullOrWhiteSpace(part.TagName) && part.TagName != "*" && !string.Equals(element.TagName, part.TagName, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (!string.IsNullOrWhiteSpace(part.Id) && !string.Equals(element.ElementId, part.Id, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		for (int i = 0; i < part.Classes.Count; i++)
		{
			if (!element.HasClass(part.Classes[i]))
			{
				return false;
			}
		}
		for (int j = 0; j < part.Attributes.Count; j++)
		{
			UiSelectorAttribute uiSelectorAttribute = part.Attributes[j];
			if (!element.Attributes.TryGetValue(uiSelectorAttribute.Name, out string value) || !MatchesAttributeValue(value, uiSelectorAttribute))
			{
				return false;
			}
		}
		if ((part.PseudoState & (UiPseudoState.Hover | UiPseudoState.Active | UiPseudoState.Focus | UiPseudoState.Disabled | UiPseudoState.Checked | UiPseudoState.Selected | UiPseudoState.Required | UiPseudoState.Invalid)) != UiPseudoState.None)
		{
			return false;
		}
		if (part.PseudoState.HasFlag(UiPseudoState.Root) && element.Parent != null)
		{
			return false;
		}
		for (int k = 0; k < part.StructuralPseudos.Count; k++)
		{
			if (!MatchesStructuralPseudo(element, part.StructuralPseudos[k]))
			{
				return false;
			}
		}
		for (int l = 0; l < part.LogicalPseudos.Count; l++)
		{
			if (!MatchesLogicalPseudo(element, part.LogicalPseudos[l]))
			{
				return false;
			}
		}
		return true;
	}

	private static bool MatchesAttributeValue(string actualValue, UiSelectorAttribute attribute)
	{
		if (attribute.Operator == UiSelectorAttributeOperator.Exists || attribute.Value == null)
		{
			return true;
		}
		UiSelectorAttributeOperator uiSelectorAttributeOperator = attribute.Operator;
		if (1 == 0)
		{
		}
		bool result = uiSelectorAttributeOperator switch
		{
			UiSelectorAttributeOperator.Equals => string.Equals(actualValue, attribute.Value, StringComparison.OrdinalIgnoreCase), 
			UiSelectorAttributeOperator.Includes => actualValue.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains<string>(attribute.Value, StringComparer.OrdinalIgnoreCase), 
			UiSelectorAttributeOperator.DashMatch => string.Equals(actualValue, attribute.Value, StringComparison.OrdinalIgnoreCase) || actualValue.StartsWith(attribute.Value + "-", StringComparison.OrdinalIgnoreCase), 
			UiSelectorAttributeOperator.Prefix => actualValue.StartsWith(attribute.Value, StringComparison.OrdinalIgnoreCase), 
			UiSelectorAttributeOperator.Suffix => actualValue.EndsWith(attribute.Value, StringComparison.OrdinalIgnoreCase), 
			UiSelectorAttributeOperator.Substring => actualValue.Contains(attribute.Value, StringComparison.OrdinalIgnoreCase), 
			_ => false, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static bool MatchesStructuralPseudo(UiElement element, UiStructuralPseudo pseudo)
	{
		if (element.Parent == null)
		{
			return false;
		}
		int childIndex = GetChildIndex(element);
		if (childIndex < 1)
		{
			return false;
		}
		return UiStructuralPseudoMatcher.Matches(element.Parent.Children.Count, childIndex, pseudo);
	}

	private static bool MatchesLogicalPseudo(UiElement element, UiSelectorLogicalPseudo pseudo)
	{
		bool flag = pseudo.Selectors.Any((UiSelector selector) => Matches(element, selector));
		UiSelectorLogicalPseudoKind kind = pseudo.Kind;
		if (1 == 0)
		{
		}
		bool result;
		switch (kind)
		{
		case UiSelectorLogicalPseudoKind.Not:
			result = !flag;
			break;
		case UiSelectorLogicalPseudoKind.Is:
		case UiSelectorLogicalPseudoKind.Where:
			result = flag;
			break;
		default:
			result = false;
			break;
		}
		if (1 == 0)
		{
		}
		return result;
	}

	private static int GetChildIndex(UiElement element)
	{
		if (element.Parent == null)
		{
			return -1;
		}
		for (int i = 0; i < element.Parent.Children.Count; i++)
		{
			if (element.Parent.Children[i] == element)
			{
				return i + 1;
			}
		}
		return -1;
	}

	private static UiElement? GetPreviousSibling(UiElement element)
	{
		if (element.Parent == null)
		{
			return null;
		}
		for (int i = 0; i < element.Parent.Children.Count; i++)
		{
			if (element.Parent.Children[i] == element)
			{
				return (i > 0) ? element.Parent.Children[i - 1] : null;
			}
		}
		return null;
	}
}
