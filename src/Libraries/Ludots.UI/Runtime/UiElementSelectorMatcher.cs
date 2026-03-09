namespace Ludots.UI.Runtime;

public static class UiElementSelectorMatcher
{
    private const UiPseudoState RuntimePseudoMask = UiPseudoState.Hover
        | UiPseudoState.Active
        | UiPseudoState.Focus
        | UiPseudoState.Disabled
        | UiPseudoState.Checked
        | UiPseudoState.Selected
        | UiPseudoState.Required
        | UiPseudoState.Invalid;

    public static bool Matches(UiElement element, UiSelector selector)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(selector);
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

        UiSelectorPart part = parts[selectorIndex];
        if (!MatchesPart(element, part))
        {
            return false;
        }

        if (selectorIndex == 0)
        {
            return true;
        }

        return part.Combinator switch
        {
            UiSelectorCombinator.Child => Matches(element.Parent, selectorIndex - 1, parts),
            UiSelectorCombinator.AdjacentSibling => Matches(GetPreviousSibling(element), selectorIndex - 1, parts),
            UiSelectorCombinator.GeneralSibling => MatchesAnyPreviousSibling(element, selectorIndex - 1, parts),
            _ => MatchesAnyAncestor(element.Parent, selectorIndex - 1, parts)
        };
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
        UiElement? sibling = GetPreviousSibling(element);
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

        for (int i = 0; i < part.Attributes.Count; i++)
        {
            UiSelectorAttribute attribute = part.Attributes[i];
            if (!element.Attributes.TryGetValue(attribute.Name, out string value) || !MatchesAttributeValue(value, attribute))
            {
                return false;
            }
        }

        if ((part.PseudoState & RuntimePseudoMask) != UiPseudoState.None)
        {
            return false;
        }

        if (part.PseudoState.HasFlag(UiPseudoState.Root) && element.Parent != null)
        {
            return false;
        }

        for (int i = 0; i < part.StructuralPseudos.Count; i++)
        {
            if (!MatchesStructuralPseudo(element, part.StructuralPseudos[i]))
            {
                return false;
            }
        }

        for (int i = 0; i < part.LogicalPseudos.Count; i++)
        {
            if (!MatchesLogicalPseudo(element, part.LogicalPseudos[i]))
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

    private static bool MatchesStructuralPseudo(UiElement element, UiStructuralPseudo pseudo)
    {
        if (element.Parent == null)
        {
            return false;
        }

        int index = GetChildIndex(element);
        if (index < 1)
        {
            return false;
        }

        return UiStructuralPseudoMatcher.Matches(element.Parent.Children.Count, index, pseudo);
    }

    private static bool MatchesLogicalPseudo(UiElement element, UiSelectorLogicalPseudo pseudo)
    {
        bool anyMatch = pseudo.Selectors.Any(selector => Matches(element, selector));
        return pseudo.Kind switch
        {
            UiSelectorLogicalPseudoKind.Not => !anyMatch,
            UiSelectorLogicalPseudoKind.Is or UiSelectorLogicalPseudoKind.Where => anyMatch,
            _ => false
        };
    }

    private static int GetChildIndex(UiElement element)
    {
        if (element.Parent == null)
        {
            return -1;
        }

        for (int i = 0; i < element.Parent.Children.Count; i++)
        {
            if (ReferenceEquals(element.Parent.Children[i], element))
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
            if (ReferenceEquals(element.Parent.Children[i], element))
            {
                return i > 0 ? element.Parent.Children[i - 1] : null;
            }
        }

        return null;
    }
}
