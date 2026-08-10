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

	public static Size Measure(UiNode node, IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
	{
		float available = widthMode == MeasureMode.Undefined ? float.PositiveInfinity : Math.Max(0f, width - node.Style.Padding.Horizontal);
		List<LineBox> lines = BuildLines(node, textMeasurer, imageSizeProvider, available);
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

	public static void LayoutSubtree(UiNode root, IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
	{
		ArgumentNullException.ThrowIfNull(root, nameof(root));
		LayoutNode(root, textMeasurer, imageSizeProvider);
	}

	private static void LayoutNode(UiNode node, IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
	{
		if (IsInlineFormattingContext(node))
		{
			LayoutInlineContext(node, textMeasurer, imageSizeProvider);
		}
		for (int i = 0; i < node.Children.Count; i++)
		{
			LayoutNode(node.Children[i], textMeasurer, imageSizeProvider);
		}
	}

	private static void LayoutInlineContext(UiNode node, IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
	{
		float available = Math.Max(0f, node.LayoutRect.Width - node.Style.Padding.Horizontal);
		List<LineBox> lines = BuildLines(node, textMeasurer, imageSizeProvider, available);
		float x0 = node.LayoutRect.X + node.Style.Padding.Left;
		float y = node.LayoutRect.Y + node.Style.Padding.Top;
		for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
		{
			LineBox line = lines[lineIndex];
			float x = x0;
			float baseline = y + line.MaxAscent;
			for (int i = 0; i < line.Items.Count; i++)
			{
				InlineItem item = line.Items[i];
				float itemY = baseline - item.Ascent;
				item.Node.SetLayout(new UiRect(x, itemY, item.Width, item.Height));
				item.Node.SetScrollMetrics(item.Width, item.Height);
				x += item.Width;
			}
			y += line.Height;
		}
		node.SetScrollMetrics(node.LayoutRect.Width, Math.Max(node.LayoutRect.Height, y - node.LayoutRect.Y + node.Style.Padding.Bottom));
	}

	private static List<LineBox> BuildLines(UiNode node, IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider, float availableWidth)
	{
		List<LineBox> lines = new List<LineBox>();
		LineBox current = new LineBox();
		float maxWidth = float.IsInfinity(availableWidth) ? float.MaxValue : Math.Max(0f, availableWidth);

		for (int i = 0; i < node.Children.Count; i++)
		{
			UiNode child = node.Children[i];
			if (!child.Style.Visible || child.Style.Display == UiDisplay.None || !IsInlineLevel(child))
			{
				continue;
			}

			InlineItem item = MeasureInlineItem(child, textMeasurer, imageSizeProvider, maxWidth);
			bool fits = current.Items.Count == 0 || current.Width + item.Width <= maxWidth + 0.01f || maxWidth >= float.MaxValue - 1f;
			if (!fits)
			{
				lines.Add(current);
				current = new LineBox();
				item = MeasureInlineItem(child, textMeasurer, imageSizeProvider, maxWidth);
			}
			current.Add(item);
		}

		if (current.Items.Count > 0 || lines.Count == 0)
		{
			lines.Add(current);
		}
		return lines;
	}

	private static InlineItem MeasureInlineItem(UiNode node, IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider, float maxWidth)
	{
		if (node.Kind == UiNodeKind.Image)
		{
			(float width, float height) = ResolveImageSize(node, imageSizeProvider);
			float ascent = height * 0.8f;
			return new InlineItem(node, width, height, ascent, height - ascent);
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
				InlineItem nested = MeasureInlineItem(node.Children[i], textMeasurer, imageSizeProvider, maxWidth);
				width += nested.Width;
				ascent = Math.Max(ascent, nested.Ascent);
				descent = Math.Max(descent, nested.Descent);
			}
			if (width > 0f || !string.IsNullOrEmpty(node.TextContent))
			{
				if (!string.IsNullOrEmpty(node.TextContent))
				{
					float textWidth = textMeasurer.MeasureWidth(node.TextContent, node.Style);
					width += textWidth;
				}
				return new InlineItem(node, width, ascent + descent, ascent, descent);
			}
		}

		string text = node.TextContent ?? string.Empty;
		if (text.Length == 0)
		{
			float emptyHeight = Math.Max(node.Style.FontSize * 1.4f, 1f);
			float emptyAscent = node.Style.FontSize;
			return new InlineItem(node, 0f, emptyHeight, emptyAscent, emptyHeight - emptyAscent);
		}

		bool constrain = node.Style.WhiteSpace != UiWhiteSpace.NoWrap && maxWidth < float.MaxValue - 1f;
		float preferred = textMeasurer.MeasureWidth(text, node.Style);
		float measureWidth = constrain ? Math.Min(preferred, maxWidth) : preferred;
		UiTextLayoutResult measured = textMeasurer.Measure(text, node.Style, measureWidth, constrain);
		float ascentMetric = measured.Ascent > 0f ? measured.Ascent : node.Style.FontSize;
		float descentMetric = measured.Descent > 0f ? measured.Descent : Math.Max(0f, measured.LineHeight - ascentMetric);
		if (measured.Lines.Count > 1)
		{
			descentMetric = Math.Max(descentMetric, measured.Height - ascentMetric);
		}
		return new InlineItem(node, measured.Width, measured.Height, ascentMetric, Math.Max(0f, measured.Height - ascentMetric));
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

	private sealed class LineBox
	{
		public List<InlineItem> Items { get; } = new List<InlineItem>();
		public float Width { get; private set; }
		public float MaxAscent { get; private set; }
		public float MaxDescent { get; private set; }
		public float Height => Math.Max(MaxAscent + MaxDescent, 0f);

		public void Add(InlineItem item)
		{
			Items.Add(item);
			Width += item.Width;
			MaxAscent = Math.Max(MaxAscent, item.Ascent);
			MaxDescent = Math.Max(MaxDescent, item.Descent);
		}
	}

	private sealed class InlineItem
	{
		public UiNode Node { get; }
		public float Width { get; }
		public float Height { get; }
		public float Ascent { get; }
		public float Descent { get; }

		public InlineItem(UiNode node, float width, float height, float ascent, float descent)
		{
			Node = node;
			Width = width;
			Height = height;
			Ascent = ascent;
			Descent = descent;
		}
	}
}
