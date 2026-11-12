using System.Collections.Generic;
using System.Linq;

namespace Ludots.UI.Runtime;

public static class UiVisualTreeOrdering
{
	public static bool IsBackToFront(IReadOnlyList<UiNode> children)
	{
		for (int i = 1; i < children.Count; i++)
		{
			if (children[i].RenderStyle.ZIndex < children[i - 1].RenderStyle.ZIndex)
			{
				return false;
			}
		}
		return true;
	}

	/// <summary>按 z-index 升序、同值保持文档顺序，把子节点拷进 <paramref name="destination"/>。</summary>
	public static void CopyBackToFront(IReadOnlyList<UiNode> children, List<UiNode> destination)
	{
		destination.Clear();
		for (int i = 0; i < children.Count; i++)
		{
			UiNode child = children[i];
			int zIndex = child.RenderStyle.ZIndex;
			int insertAt = destination.Count;
			while (insertAt > 0 && destination[insertAt - 1].RenderStyle.ZIndex > zIndex)
			{
				insertAt--;
			}
			destination.Insert(insertAt, child);
		}
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
