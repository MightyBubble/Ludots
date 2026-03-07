namespace Ludots.UI.Runtime;

public static class UiElementSelectorMatcher
{
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

        if (part.Combinator == UiSelectorCombinator.Child)
        {
            return Matches(element.Parent, selectorIndex - 1, parts);
        }

        UiElement? ancestor = element.Parent;
        while (ancestor != null)
        {
            if (Matches(ancestor, selectorIndex - 1, parts))
            {
                return true;
            }

            ancestor = ancestor.Parent;
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

        foreach (string className in part.Classes)
        {
            if (!element.HasClass(className))
            {
                return false;
            }
        }

        for (int i = 0; i < part.Attributes.Count; i++)
        {
            UiSelectorAttribute attribute = part.Attributes[i];
            if (!element.Attributes.TryGetValue(attribute.Name, out string value))
            {
                return false;
            }

            if (attribute.Value != null && !string.Equals(value, attribute.Value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (part.PseudoState != UiPseudoState.None && part.PseudoState != UiPseudoState.Root)
        {
            return false;
        }

        if (part.PseudoState == UiPseudoState.Root && element.Parent != null)
        {
            return false;
        }

        return true;
    }
}
