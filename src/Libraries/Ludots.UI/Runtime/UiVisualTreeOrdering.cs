using System.Collections.Generic;
using System.Linq;

namespace Ludots.UI.Runtime;

internal static class UiVisualTreeOrdering
{
	public static IReadOnlyList<UiNode> BackToFront(IReadOnlyList<UiNode> children)
	{
		if (children.Count <= 1)
		{
			return children;
		}
		return (from entry in children.Select((UiNode child, int index) => (Child: child, Index: index))
			orderby entry.Child.RenderStyle.ZIndex, entry.Index
			select entry.Child).ToArray();
	}

	public static IReadOnlyList<UiNode> FrontToBack(IReadOnlyList<UiNode> children)
	{
		if (children.Count <= 1)
		{
			return children;
		}
		return (from entry in children.Select((UiNode child, int index) => (Child: child, Index: index))
			orderby entry.Child.RenderStyle.ZIndex descending, entry.Index descending
			select entry.Child).ToArray();
	}
}
