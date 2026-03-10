using System.Collections.Generic;
using System.Linq;
using Ludots.UI.Runtime.Actions;

namespace Ludots.UI.Runtime.Diff;

public sealed class UiNodeDiff
{
	public UiNodeId Id { get; }

	public UiNodeKind Kind { get; }

	public UiStyle Style { get; }

	public string? TextContent { get; }

	public IReadOnlyList<UiActionHandle> ActionHandles { get; }

	public IReadOnlyList<UiNodeDiff> Children { get; }

	public string TagName { get; }

	public string? ElementId { get; }

	public IReadOnlyList<string> ClassNames { get; }

	public string? ImageSource { get; }

	public UiRect LayoutRect { get; }

	public float ScrollOffsetX { get; }

	public float ScrollOffsetY { get; }

	public float ScrollContentWidth { get; }

	public float ScrollContentHeight { get; }

	public UiNodeDiff(UiNodeId id, UiNodeKind kind, UiStyle style, string? textContent, IReadOnlyList<UiActionHandle> actionHandles, IReadOnlyList<UiNodeDiff> children, string tagName, string? elementId, IReadOnlyList<string> classNames, string? imageSource, UiRect layoutRect, float scrollOffsetX, float scrollOffsetY, float scrollContentWidth, float scrollContentHeight)
	{
		Id = id;
		Kind = kind;
		Style = style;
		TextContent = textContent;
		ActionHandles = actionHandles;
		Children = children;
		TagName = tagName;
		ElementId = elementId;
		ClassNames = classNames;
		ImageSource = imageSource;
		LayoutRect = layoutRect;
		ScrollOffsetX = scrollOffsetX;
		ScrollOffsetY = scrollOffsetY;
		ScrollContentWidth = scrollContentWidth;
		ScrollContentHeight = scrollContentHeight;
	}

	public static UiNodeDiff FromNode(UiNode node)
	{
		UiNodeDiff[] children = node.Children.Select(FromNode).ToArray();
		UiActionHandle[] actionHandles = node.ActionHandles.ToArray();
		return new UiNodeDiff(node.Id, node.Kind, node.RenderStyle, node.TextContent, actionHandles, children, node.TagName, node.ElementId, node.ClassNames.ToArray(), node.Attributes["src"], node.LayoutRect, node.ScrollOffsetX, node.ScrollOffsetY, node.ScrollContentWidth, node.ScrollContentHeight);
	}
}
