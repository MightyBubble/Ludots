using System;

namespace Ludots.UI.Runtime;

public static class UiFlexAnonymousText
{
	public static bool ShouldAlignAsAnonymousFlexItem(UiNode node)
	{
		ArgumentNullException.ThrowIfNull(node);
		if (node.Children.Count > 0 || string.IsNullOrEmpty(node.TextContent))
		{
			return false;
		}
		return node.Style.Display == UiDisplay.Flex;
	}

	public static UiRect ResolveTextBox(UiStyle hostStyle, UiRect hostBorderBox, float textWidth, float textHeight)
	{
		ArgumentNullException.ThrowIfNull(hostStyle);
		float inset = Math.Max(0f, hostStyle.BorderWidth);
		float left = hostBorderBox.X + inset + hostStyle.Padding.Left;
		float top = hostBorderBox.Y + inset + hostStyle.Padding.Top;
		float width = Math.Max(0f, hostBorderBox.Width - inset * 2f - hostStyle.Padding.Horizontal);
		float height = Math.Max(0f, hostBorderBox.Height - inset * 2f - hostStyle.Padding.Vertical);
		float boxWidth = Math.Min(Math.Max(0f, textWidth), width);
		float boxHeight = Math.Min(Math.Max(0f, textHeight), height);
		bool row = hostStyle.FlexDirection == UiFlexDirection.Row;
		float mainFree = row ? width - boxWidth : height - boxHeight;
		float crossFree = row ? height - boxHeight : width - boxWidth;
		float offsetMain = ResolveMainOffset(hostStyle.JustifyContent, mainFree);
		float offsetCross = ResolveCrossOffset(hostStyle.AlignItems, crossFree);
		float x = left + (row ? offsetMain : offsetCross);
		float y = top + (row ? offsetCross : offsetMain);
		return new UiRect(x, y, boxWidth, boxHeight);
	}

	private static float ResolveMainOffset(UiJustifyContent justify, float freeSpace)
	{
		if (freeSpace <= 0f)
		{
			return 0f;
		}
		return justify switch
		{
			UiJustifyContent.Center => freeSpace * 0.5f,
			UiJustifyContent.End => freeSpace,
			UiJustifyContent.SpaceAround => freeSpace / 3f,
			UiJustifyContent.SpaceEvenly => freeSpace * 0.5f,
			_ => 0f,
		};
	}

	private static float ResolveCrossOffset(UiAlignItems align, float freeSpace)
	{
		if (freeSpace <= 0f)
		{
			return 0f;
		}
		return align switch
		{
			UiAlignItems.Center => freeSpace * 0.5f,
			UiAlignItems.End => freeSpace,
			_ => 0f,
		};
	}
}
