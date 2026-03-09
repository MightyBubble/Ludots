namespace Ludots.UI.Runtime;

internal static class UiVisualTreeOrdering
{
    public static IReadOnlyList<UiNode> BackToFront(IReadOnlyList<UiNode> children)
    {
        if (children.Count <= 1)
        {
            return children;
        }

        return children
            .Select((child, index) => (Child: child, Index: index))
            .OrderBy(static entry => entry.Child.RenderStyle.ZIndex)
            .ThenBy(static entry => entry.Index)
            .Select(static entry => entry.Child)
            .ToArray();
    }

    public static IReadOnlyList<UiNode> FrontToBack(IReadOnlyList<UiNode> children)
    {
        if (children.Count <= 1)
        {
            return children;
        }

        return children
            .Select((child, index) => (Child: child, Index: index))
            .OrderByDescending(static entry => entry.Child.RenderStyle.ZIndex)
            .ThenByDescending(static entry => entry.Index)
            .Select(static entry => entry.Child)
            .ToArray();
    }
}
