using System;
using System.Collections.Generic;
using FlexLayoutSharp;

namespace Ludots.UI.Runtime;

public static class UiInlineFlowEngine
{
	public static bool IsInlineFormattingContext(UiNode node)
	{
		if (node.Style.Display is not (UiDisplay.Block or UiDisplay.Text))
		{
			return false;
		}
		if (node.Children.Count == 0)
		{
			return false;
		}
		bool hasInline = false;
		for (int i = 0; i < node.Children.Count; i++)
		{
			UiNode child = node.Children[i];
			if (!child.Style.Visible || child.Style.Display == UiDisplay.None)
			{
				continue;
			}
			if (IsBlockLevel(child))
			{
				return false;
			}
			if (IsInlineLevel(child))
			{
				hasInline = true;
			}
		}
		return hasInline;
	}

	public static bool IsInlineLevel(UiNode node)
	{
		if (node.Style.Display is UiDisplay.Inline or UiDisplay.Text)
		{
			return true;
		}
		if (node.Kind == UiNodeKind.Text)
		{
			return node.Style.Display != UiDisplay.Block && node.Style.Display != UiDisplay.Grid && node.Style.Display != UiDisplay.Flex;
		}
		if (node.Kind == UiNodeKind.Image)
		{
			return node.Style.Display is not (UiDisplay.Block or UiDisplay.Grid or UiDisplay.None);
		}
		return false;
	}

	public static bool IsBlockLevel(UiNode node)
	{
		if (node.Kind == UiNodeKind.Image)
		{
			return node.Style.Display is UiDisplay.Block or UiDisplay.Grid;
		}
		return node.Style.Display is UiDisplay.Block or UiDisplay.Flex or UiDisplay.Grid;
	}

	internal static Size Measure(UiNode node, IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider, float width, MeasureMode widthMode, float height, MeasureMode heightMode, UiLayoutScratch scratch)
	{
		float available = widthMode == MeasureMode.Undefined ? float.PositiveInfinity : Math.Max(0f, width - node.Style.Padding.Horizontal);
		List<UiInlineItem> items = scratch.BeginLineItems();
		List<UiInlineLineBox> lines = scratch.BeginLines();
		BuildLines(node, textMeasurer, imageSizeProvider, available, items, lines);
		float contentWidth = 0f;
		float contentHeight = 0f;
		for (int i = 0; i < lines.Count; i++)
		{
			contentWidth = Math.Max(contentWidth, lines[i].Width);
			contentHeight += lines[i].Height;
		}
		float measuredWidth = contentWidth + node.Style.Padding.Horizontal;
		float measuredHeight = contentHeight + node.Style.Padding.Vertical;
		if (widthMode == MeasureMode.Exactly)
		{
			measuredWidth = width;
		}
		else if (widthMode == MeasureMode.AtMost)
		{
			measuredWidth = Math.Min(measuredWidth, width);
		}
		if (heightMode == MeasureMode.Exactly)
		{
			measuredHeight = height;
		}
		else if (heightMode == MeasureMode.AtMost)
		{
			measuredHeight = Math.Min(measuredHeight, height);
		}
		return new Size(measuredWidth, measuredHeight);
	}

	internal static void LayoutSubtree(UiNode root, IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider, UiLayoutScratch scratch)
	{
		ArgumentNullException.ThrowIfNull(root, nameof(root));
		LayoutNode(root, textMeasurer, imageSizeProvider, scratch);
	}

	private static void LayoutNode(UiNode node, IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider, UiLayoutScratch scratch)
	{
		if (IsInlineFormattingContext(node))
		{
			LayoutInlineContext(node, textMeasurer, imageSizeProvider, scratch);
		}
		for (int i = 0; i < node.Children.Count; i++)
		{
			LayoutNode(node.Children[i], textMeasurer, imageSizeProvider, scratch);
		}
	}

	private static void LayoutInlineContext(UiNode node, IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider, UiLayoutScratch scratch)
	{
		float available = Math.Max(0f, node.LayoutRect.Width - node.Style.Padding.Horizontal);
		List<UiInlineItem> items = scratch.BeginLineItems();
		List<UiInlineLineBox> lines = scratch.BeginLines();
		BuildLines(node, textMeasurer, imageSizeProvider, available, items, lines);
		float x0 = node.LayoutRect.X + node.Style.Padding.Left;
		float y = node.LayoutRect.Y + node.Style.Padding.Top;
		for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
		{
			UiInlineLineBox line = lines[lineIndex];
			float x = x0;
			float baseline = y + line.MaxAscent;
			int end = line.ItemStart + line.ItemCount;
			for (int i = line.ItemStart; i < end; i++)
			{
				UiInlineItem item = items[i];
				float itemY = baseline - item.Ascent;
				item.Node.SetLayout(new UiRect(x, itemY, item.Width, item.Height));
				item.Node.SetScrollMetrics(item.Width, item.Height);
				x += item.Width;
			}
			y += line.Height;
		}
		node.SetScrollMetrics(node.LayoutRect.Width, Math.Max(node.LayoutRect.Height, y - node.LayoutRect.Y + node.Style.Padding.Bottom));
	}

	private static void BuildLines(UiNode node, IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider, float availableWidth, List<UiInlineItem> items, List<UiInlineLineBox> lines)
	{
		float maxWidth = float.IsInfinity(availableWidth) ? float.MaxValue : Math.Max(0f, availableWidth);
		UiInlineLineBox current = default;
		bool hasCurrent = false;

		for (int i = 0; i < node.Children.Count; i++)
		{
			UiNode child = node.Children[i];
			if (!child.Style.Visible || child.Style.Display == UiDisplay.None || !IsInlineLevel(child))
			{
				continue;
			}

			UiInlineItem item = MeasureInlineItem(child, textMeasurer, imageSizeProvider, maxWidth);
			bool fits = !hasCurrent || current.Width + item.Width <= maxWidth + 0.01f || maxWidth >= float.MaxValue - 1f;
			if (!fits)
			{
				lines.Add(current);
				current = default;
				hasCurrent = false;
				item = MeasureInlineItem(child, textMeasurer, imageSizeProvider, maxWidth);
			}
			if (!hasCurrent)
			{
				current.ItemStart = items.Count;
				hasCurrent = true;
			}
			items.Add(item);
			current.ItemCount++;
			current.Width += item.Width;
			current.MaxAscent = Math.Max(current.MaxAscent, item.Ascent);
			current.MaxDescent = Math.Max(current.MaxDescent, item.Descent);
		}

		if (hasCurrent || lines.Count == 0)
		{
			lines.Add(current);
		}
	}

	private static UiInlineItem MeasureInlineItem(UiNode node, IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider, float maxWidth)
	{
		if (node.Kind == UiNodeKind.Image)
		{
			(float width, float height) = ResolveImageSize(node, imageSizeProvider);
			float ascent = height * 0.8f;
			return new UiInlineItem
			{
				Node = node,
				Width = width,
				Height = height,
				Ascent = ascent,
				Descent = height - ascent
			};
		}

		if (node.Children.Count > 0)
		{
			float width = 0f;
			float ascent = node.Style.FontSize;
			float descent = Math.Max(0f, node.Style.FontSize * 0.4f);
			for (int i = 0; i < node.Children.Count; i++)
			{
				if (!IsInlineLevel(node.Children[i]))
				{
					continue;
				}
				UiInlineItem nested = MeasureInlineItem(node.Children[i], textMeasurer, imageSizeProvider, maxWidth);
				width += nested.Width;
				ascent = Math.Max(ascent, nested.Ascent);
				descent = Math.Max(descent, nested.Descent);
			}
			if (width > 0f || !string.IsNullOrEmpty(node.TextContent))
			{
				if (!string.IsNullOrEmpty(node.TextContent))
				{
					width += textMeasurer.MeasureWidth(node.TextContent, node.Style);
				}
				return new UiInlineItem
				{
					Node = node,
					Width = width,
					Height = ascent + descent,
					Ascent = ascent,
					Descent = descent
				};
			}
		}

		string text = node.TextContent ?? string.Empty;
		if (text.Length == 0)
		{
			float emptyHeight = Math.Max(node.Style.FontSize * 1.4f, 1f);
			float emptyAscent = node.Style.FontSize;
			return new UiInlineItem
			{
				Node = node,
				Width = 0f,
				Height = emptyHeight,
				Ascent = emptyAscent,
				Descent = emptyHeight - emptyAscent
			};
		}

		bool constrain = node.Style.WhiteSpace != UiWhiteSpace.NoWrap && maxWidth < float.MaxValue - 1f;
		float preferred = textMeasurer.MeasureWidth(text, node.Style);
		float measureWidth = constrain ? Math.Min(preferred, maxWidth) : preferred;
		UiTextLayoutResult measured = textMeasurer.Measure(text, node.Style, measureWidth, constrain);
		float ascentMetric = measured.Ascent > 0f ? measured.Ascent : node.Style.FontSize;
		return new UiInlineItem
		{
			Node = node,
			Width = measured.Width,
			Height = measured.Height,
			Ascent = ascentMetric,
			Descent = Math.Max(0f, measured.Height - ascentMetric)
		};
	}

	private static (float Width, float Height) ResolveImageSize(UiNode node, IUiImageSizeProvider imageSizeProvider)
	{
		if (node.Style.Width.Unit == UiLengthUnit.Pixel && node.Style.Height.Unit == UiLengthUnit.Pixel)
		{
			return (node.Style.Width.Value, node.Style.Height.Value);
		}
		string? src = node.Attributes["src"];
		if (!string.IsNullOrWhiteSpace(src) && imageSizeProvider.TryGetSize(src, out float width, out float height))
		{
			if (node.Style.Width.Unit == UiLengthUnit.Pixel)
			{
				float scale = width <= 0f ? 1f : node.Style.Width.Value / width;
				return (node.Style.Width.Value, height * scale);
			}
			if (node.Style.Height.Unit == UiLengthUnit.Pixel)
			{
				float scale = height <= 0f ? 1f : node.Style.Height.Value / height;
				return (width * scale, node.Style.Height.Value);
			}
			return (width, height);
		}
		float fallbackWidth = node.Style.Width.Unit == UiLengthUnit.Pixel ? node.Style.Width.Value : 16f;
		float fallbackHeight = node.Style.Height.Unit == UiLengthUnit.Pixel ? node.Style.Height.Value : 16f;
		return (fallbackWidth, fallbackHeight);
	}
}
