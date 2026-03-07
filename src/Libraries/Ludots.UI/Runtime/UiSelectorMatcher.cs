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

        UiSelectorCombinator combinator = part.Combinator;
        if (combinator == UiSelectorCombinator.Child)
        {
            return Matches(node.Parent, selectorIndex - 1, parts);
        }

        UiNode? ancestor = node.Parent;
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
            if (!node.Attributes.TryGetValue(attribute.Name, out string value))
            {
                return false;
            }

            if (attribute.Value != null && !string.Equals(value, attribute.Value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (part.PseudoState != UiPseudoState.None && (node.PseudoState & part.PseudoState) != part.PseudoState)
        {
            return false;
        }

        return true;
    }
}
