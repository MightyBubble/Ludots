using System;
using System.Collections.Generic;
using System.Linq;

namespace Ludots.UI.Runtime;

public static class UiSelectorMatcher
{
	public static bool Matches(UiNode node, UiSelector selector)
	{
		ArgumentNullException.ThrowIfNull(node, "node");
		ArgumentNullException.ThrowIfNull(selector, "selector");
		return Matches(node, selector.Parts.Count - 1, selector.Parts);
	}

	private static bool Matches(UiNode? node, int selectorIndex, IReadOnlyList<UiSelectorPart> parts)
	{
		if (node == null)
		{
			return false;
		}
		if (selectorIndex < 0)
		{
			return true;
		}
		UiSelectorPart uiSelectorPart = parts[selectorIndex];
		if (!MatchesPart(node, uiSelectorPart))
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
			UiSelectorCombinator.Child => Matches(node.Parent, selectorIndex - 1, parts), 
			UiSelectorCombinator.AdjacentSibling => Matches(GetPreviousSibling(node), selectorIndex - 1, parts), 
			UiSelectorCombinator.GeneralSibling => MatchesAnyPreviousSibling(node, selectorIndex - 1, parts), 
			_ => MatchesAnyAncestor(node.Parent, selectorIndex - 1, parts), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static bool MatchesAnyAncestor(UiNode? ancestor, int selectorIndex, IReadOnlyList<UiSelectorPart> parts)
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

	private static bool MatchesAnyPreviousSibling(UiNode node, int selectorIndex, IReadOnlyList<UiSelectorPart> parts)
	{
		for (UiNode previousSibling = GetPreviousSibling(node); previousSibling != null; previousSibling = GetPreviousSibling(previousSibling))
		{
			if (Matches(previousSibling, selectorIndex, parts))
			{
				return true;
			}
		}
		return false;
	}

	private static bool MatchesPart(UiNode node, UiSelectorPart part)
	{
		if (!string.IsNullOrWhiteSpace(part.TagName) && part.TagName != "*" && !string.Equals(node.TagName, part.TagName, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (!string.IsNullOrWhiteSpace(part.Id) && !string.Equals(node.ElementId, part.Id, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		for (int i = 0; i < part.Classes.Count; i++)
		{
			if (!node.HasClass(part.Classes[i]))
			{
				return false;
			}
		}
		for (int j = 0; j < part.Attributes.Count; j++)
		{
			UiSelectorAttribute uiSelectorAttribute = part.Attributes[j];
			if (!node.Attributes.TryGetValue(uiSelectorAttribute.Name, out string value) || !MatchesAttributeValue(value, uiSelectorAttribute))
			{
				return false;
			}
		}
		if (part.PseudoState != UiPseudoState.None && (node.PseudoState & part.PseudoState) != part.PseudoState)
		{
			return false;
		}
		for (int k = 0; k < part.StructuralPseudos.Count; k++)
		{
			if (!MatchesStructuralPseudo(node, part.StructuralPseudos[k]))
			{
				return false;
			}
		}
		for (int l = 0; l < part.LogicalPseudos.Count; l++)
		{
			if (!MatchesLogicalPseudo(node, part.LogicalPseudos[l]))
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

	private static bool MatchesStructuralPseudo(UiNode node, UiStructuralPseudo pseudo)
	{
		if (node.Parent == null)
		{
			return false;
		}
		int childIndex = GetChildIndex(node);
		if (childIndex < 1)
		{
			return false;
		}
		return UiStructuralPseudoMatcher.Matches(node.Parent.Children.Count, childIndex, pseudo);
	}

	private static bool MatchesLogicalPseudo(UiNode node, UiSelectorLogicalPseudo pseudo)
	{
		bool flag = pseudo.Selectors.Any((UiSelector selector) => Matches(node, selector));
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

	private static int GetChildIndex(UiNode node)
	{
		if (node.Parent == null)
		{
			return -1;
		}
		for (int i = 0; i < node.Parent.Children.Count; i++)
		{
			if (node.Parent.Children[i] == node)
			{
				return i + 1;
			}
		}
		return -1;
	}

	private static UiNode? GetPreviousSibling(UiNode node)
	{
		if (node.Parent == null)
		{
			return null;
		}
		for (int i = 0; i < node.Parent.Children.Count; i++)
		{
			if (node.Parent.Children[i] == node)
			{
				return (i > 0) ? node.Parent.Children[i - 1] : null;
			}
		}
		return null;
	}
}
