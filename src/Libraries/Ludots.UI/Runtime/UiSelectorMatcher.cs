namespace Ludots.UI.Runtime;

public static class UiSelectorMatcher
{
    public static bool Matches(UiNode node, UiSelector selector)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(selector);

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

        UiSelectorPart part = parts[selectorIndex];
        if (!MatchesPart(node, part))
        {
            return false;
        }

        if (selectorIndex == 0)
        {
            return true;
        }

        return part.Combinator switch
        {
            UiSelectorCombinator.Child => Matches(node.Parent, selectorIndex - 1, parts),
            UiSelectorCombinator.AdjacentSibling => Matches(GetPreviousSibling(node), selectorIndex - 1, parts),
            UiSelectorCombinator.GeneralSibling => MatchesAnyPreviousSibling(node, selectorIndex - 1, parts),
            _ => MatchesAnyAncestor(node.Parent, selectorIndex - 1, parts)
        };
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
        UiNode? sibling = GetPreviousSibling(node);
        while (sibling != null)
        {
            if (Matches(sibling, selectorIndex, parts))
            {
                return true;
            }

            sibling = GetPreviousSibling(sibling);
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

        for (int i = 0; i < part.Attributes.Count; i++)
        {
            UiSelectorAttribute attribute = part.Attributes[i];
            if (!node.Attributes.TryGetValue(attribute.Name, out string value) || !MatchesAttributeValue(value, attribute))
            {
                return false;
            }
        }

        if (part.PseudoState != UiPseudoState.None && (node.PseudoState & part.PseudoState) != part.PseudoState)
        {
            return false;
        }

        for (int i = 0; i < part.StructuralPseudos.Count; i++)
        {
            if (!MatchesStructuralPseudo(node, part.StructuralPseudos[i]))
            {
                return false;
            }
        }

        for (int i = 0; i < part.LogicalPseudos.Count; i++)
        {
            if (!MatchesLogicalPseudo(node, part.LogicalPseudos[i]))
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

        return attribute.Operator switch
        {
            UiSelectorAttributeOperator.Equals => string.Equals(actualValue, attribute.Value, StringComparison.OrdinalIgnoreCase),
            UiSelectorAttributeOperator.Includes => actualValue
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(attribute.Value, StringComparer.OrdinalIgnoreCase),
            UiSelectorAttributeOperator.DashMatch => string.Equals(actualValue, attribute.Value, StringComparison.OrdinalIgnoreCase)
                || actualValue.StartsWith(attribute.Value + '-', StringComparison.OrdinalIgnoreCase),
            UiSelectorAttributeOperator.Prefix => actualValue.StartsWith(attribute.Value, StringComparison.OrdinalIgnoreCase),
            UiSelectorAttributeOperator.Suffix => actualValue.EndsWith(attribute.Value, StringComparison.OrdinalIgnoreCase),
            UiSelectorAttributeOperator.Substring => actualValue.Contains(attribute.Value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool MatchesStructuralPseudo(UiNode node, UiStructuralPseudo pseudo)
    {
        if (node.Parent == null)
        {
            return false;
        }

        int index = GetChildIndex(node);
        if (index < 1)
        {
            return false;
        }

        return UiStructuralPseudoMatcher.Matches(node.Parent.Children.Count, index, pseudo);
    }

    private static bool MatchesLogicalPseudo(UiNode node, UiSelectorLogicalPseudo pseudo)
    {
        bool anyMatch = pseudo.Selectors.Any(selector => Matches(node, selector));
        return pseudo.Kind switch
        {
            UiSelectorLogicalPseudoKind.Not => !anyMatch,
            UiSelectorLogicalPseudoKind.Is or UiSelectorLogicalPseudoKind.Where => anyMatch,
            _ => false
        };
    }

    private static int GetChildIndex(UiNode node)
    {
        if (node.Parent == null)
        {
            return -1;
        }

        for (int i = 0; i < node.Parent.Children.Count; i++)
        {
            if (ReferenceEquals(node.Parent.Children[i], node))
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
            if (ReferenceEquals(node.Parent.Children[i], node))
            {
                return i > 0 ? node.Parent.Children[i - 1] : null;
            }
        }

        return null;
    }
}
