using System;

namespace Ludots.UI.Runtime;

public static class UiScrollGeometry
{
	public const float ScrollbarThickness = 10f;

	public const float ScrollbarPadding = 2f;

	public const float MinThumbSize = 18f;

	public static bool HasHorizontalScrollbar(UiNode node)
	{
		ArgumentNullException.ThrowIfNull(node, "node");
		return node.CanScrollHorizontally;
	}

	public static bool HasVerticalScrollbar(UiNode node)
	{
		ArgumentNullException.ThrowIfNull(node, "node");
		return node.CanScrollVertically;
	}

	public static UiRect GetHorizontalTrackRect(UiNode node)
	{
		ArgumentNullException.ThrowIfNull(node, "node");
		float width = Math.Max(0f, node.LayoutRect.Width - 4f - (HasVerticalScrollbar(node) ? 10f : 0f));
		float x = node.LayoutRect.X + 2f;
		float y = node.LayoutRect.Bottom - 10f - 2f;
		return new UiRect(x, y, width, 10f);
	}

	public static UiRect GetVerticalTrackRect(UiNode node)
	{
		ArgumentNullException.ThrowIfNull(node, "node");
		float height = Math.Max(0f, node.LayoutRect.Height - 4f - (HasHorizontalScrollbar(node) ? 10f : 0f));
		float x = node.LayoutRect.Right - 10f - 2f;
		float y = node.LayoutRect.Y + 2f;
		return new UiRect(x, y, 10f, height);
	}

	public static UiRect GetHorizontalThumbRect(UiNode node)
	{
		ArgumentNullException.ThrowIfNull(node, "node");
		UiRect horizontalTrackRect = GetHorizontalTrackRect(node);
		if (!HasHorizontalScrollbar(node) || horizontalTrackRect.Width <= 0.01f)
		{
			return new UiRect(horizontalTrackRect.X, horizontalTrackRect.Y, 0f, 0f);
		}
		float num = Math.Clamp(horizontalTrackRect.Width * (node.LayoutRect.Width / Math.Max(node.LayoutRect.Width, node.ScrollContentWidth)), 18f, horizontalTrackRect.Width);
		float num2 = Math.Max(0f, horizontalTrackRect.Width - num);
		float num3 = ((node.MaxScrollX <= 0.01f) ? 0f : (node.ScrollOffsetX / node.MaxScrollX));
		return new UiRect(horizontalTrackRect.X + num2 * num3, horizontalTrackRect.Y, num, horizontalTrackRect.Height);
	}

	public static UiRect GetVerticalThumbRect(UiNode node)
	{
		ArgumentNullException.ThrowIfNull(node, "node");
		UiRect verticalTrackRect = GetVerticalTrackRect(node);
		if (!HasVerticalScrollbar(node) || verticalTrackRect.Height <= 0.01f)
		{
			return new UiRect(verticalTrackRect.X, verticalTrackRect.Y, 0f, 0f);
		}
		float num = Math.Clamp(verticalTrackRect.Height * (node.LayoutRect.Height / Math.Max(node.LayoutRect.Height, node.ScrollContentHeight)), 18f, verticalTrackRect.Height);
		float num2 = Math.Max(0f, verticalTrackRect.Height - num);
		float num3 = ((node.MaxScrollY <= 0.01f) ? 0f : (node.ScrollOffsetY / node.MaxScrollY));
		return new UiRect(verticalTrackRect.X, verticalTrackRect.Y + num2 * num3, verticalTrackRect.Width, num);
	}
}
