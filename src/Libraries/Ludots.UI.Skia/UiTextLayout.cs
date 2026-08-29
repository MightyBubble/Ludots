using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ludots.UI.Runtime;
using SkiaSharp;

namespace Ludots.UI.Skia;

public sealed class SkiaTextMeasurer : IUiTextMeasurer
{
	UiTextLayoutResult IUiTextMeasurer.Measure(string? text, UiStyle style, float availableWidth, bool constrainWidth)
		=> UiTextLayout.Measure(text, style, availableWidth, constrainWidth);

	float IUiTextMeasurer.MeasureWidth(string? text, UiStyle style)
		=> UiTextLayout.MeasureWidth(text, style);

	UiTextLayoutResult IUiTextMeasurer.Measure(IReadOnlyList<UiStyledTextRun> runs, UiStyle style, float availableWidth, bool constrainWidth)
		=> UiTextLayout.Measure(runs, style, availableWidth, constrainWidth);

	float IUiTextMeasurer.MeasureWidth(IReadOnlyList<UiStyledTextRun> runs, UiStyle style)
		=> UiTextLayout.MeasureWidth(runs, style);
}

public static class UiTextLayout
{
	public static UiTextLayoutResult Measure(string? text, UiStyle style, float availableWidth, bool constrainWidth)
	{
		if (string.IsNullOrEmpty(text))
		{
			float emptyLine = ResolveLineHeight(style);
			return new UiTextLayoutResult(Array.Empty<string>(), 0f, 0f, emptyLine, style.FontSize, Math.Max(0f, emptyLine - style.FontSize));
		}
		using SKPaint paint = CreatePaint(style);
		List<string> list = BreakLines(text, style, paint, availableWidth, constrainWidth);
		float num = ResolveLineHeight(style);
		float num2 = 0f;
		for (int i = 0; i < list.Count; i++)
		{
			float num3 = MeasureLineWidth(style, paint, list[i]);
			if (num3 > num2)
			{
				num2 = num3;
			}
		}
		float ascent = ResolveAscent(style);
		float descent = Math.Max(0f, num - ascent);
		return new UiTextLayoutResult(list, num2, num * (float)list.Count, num, ascent, descent);
	}

	public static float MeasureWidth(string? text, UiStyle style)
	{
		if (string.IsNullOrEmpty(text))
		{
			return 0f;
		}
		using SKPaint paint = CreatePaint(style);
		return MeasureLineWidth(style, paint, text);
	}

	public static UiTextLayoutResult Measure(IReadOnlyList<UiStyledTextRun> runs, UiStyle style, float availableWidth, bool constrainWidth)
	{
		IReadOnlyList<UiStyledTextRun> normalized = UiStyledTextRunNormalization.NormalizeWordBoundaries(runs);
		if (normalized.Count == 0)
		{
			float emptyLine = ResolveLineHeight(style);
			return new UiTextLayoutResult(Array.Empty<string>(), 0f, 0f, emptyLine, style.FontSize, Math.Max(0f, emptyLine - style.FontSize));
		}

		using SKPaint paint = CreatePaint(style);
		IReadOnlyList<IReadOnlyList<UiStyledTextRun>> styledLines = BreakStyledLines(normalized, style, paint, availableWidth, constrainWidth);
		float lineHeight = ResolveLineHeight(style);
		float maxWidth = 0f;
		var plainLines = new string[styledLines.Count];
		for (int i = 0; i < styledLines.Count; i++)
		{
			float lineWidth = MeasureStyledLineWidth(styledLines[i], style, paint);
			if (lineWidth > maxWidth)
			{
				maxWidth = lineWidth;
			}

			plainLines[i] = UiStyledTextRunNormalization.ConcatPlain(styledLines[i]);
		}

		float ascent = ResolveAscent(style);
		float descent = Math.Max(0f, lineHeight - ascent);
		return new UiTextLayoutResult(plainLines, maxWidth, lineHeight * styledLines.Count, lineHeight, ascent, descent);
	}

	public static float MeasureWidth(IReadOnlyList<UiStyledTextRun> runs, UiStyle style)
	{
		IReadOnlyList<UiStyledTextRun> normalized = UiStyledTextRunNormalization.NormalizeWordBoundaries(runs);
		if (normalized.Count == 0)
		{
			return 0f;
		}

		using SKPaint paint = CreatePaint(style);
		return MeasureStyledLineWidth(normalized, style, paint);
	}

	public static SKPaint CreatePaint(UiStyle style)
	{
		return new SKPaint
		{
			Color = style.Color.ToSKColor(),
			IsAntialias = true
		};
	}

	public static SKFont CreateFont(UiStyle style)
	{
		return new SKFont(UiFontRegistry.ResolveTypeface(style.FontFamily, style.Bold, style.Italic), style.FontSize);
	}

	internal static IReadOnlyList<UiTextRun> CreateRuns(string text, UiStyle style)
	{
		if (string.IsNullOrEmpty(text))
		{
			return Array.Empty<UiTextRun>();
		}
		List<UiTextRun> list = new List<UiTextRun>();
		StringBuilder stringBuilder = new StringBuilder();
		SKTypeface sKTypeface = null;
		TextElementEnumerator textElementEnumerator = StringInfo.GetTextElementEnumerator(text);
		while (textElementEnumerator.MoveNext())
		{
			string textElement = textElementEnumerator.GetTextElement();
			SKTypeface sKTypeface2 = UiFontRegistry.ResolveTypefaceForTextElement(style.FontFamily, style.Bold, textElement, style.Italic);
			if (sKTypeface != null && !UiFontRegistry.SameTypeface(sKTypeface, sKTypeface2))
			{
				list.Add(new UiTextRun(stringBuilder.ToString(), sKTypeface, style.Bold, style.Italic));
				stringBuilder.Clear();
			}
			sKTypeface = sKTypeface2;
			stringBuilder.Append(textElement);
		}
		if (stringBuilder.Length > 0 && sKTypeface != null)
		{
			list.Add(new UiTextRun(stringBuilder.ToString(), sKTypeface, style.Bold, style.Italic));
		}
		return list;
	}

	internal static IReadOnlyList<UiTextRun> CreateRuns(IReadOnlyList<UiStyledTextRun> segments, UiStyle style)
	{
		IReadOnlyList<UiStyledTextRun> normalized = UiStyledTextRunNormalization.NormalizeWordBoundaries(segments);
		if (normalized.Count == 0)
		{
			return Array.Empty<UiTextRun>();
		}

		var list = new List<UiTextRun>();
		for (int i = 0; i < normalized.Count; i++)
		{
			UiStyledTextRun segment = normalized[i];
			if (string.IsNullOrEmpty(segment.Text))
			{
				continue;
			}

			bool bold = segment.Bold || style.Bold;
			bool italic = segment.Italic || style.Italic;
			UiStyle segmentStyle = style with { Bold = bold, Italic = italic };
			IReadOnlyList<UiTextRun> glyphRuns = CreateRuns(segment.Text, segmentStyle);
			for (int j = 0; j < glyphRuns.Count; j++)
			{
				UiTextRun glyphRun = glyphRuns[j];
				list.Add(new UiTextRun(
					glyphRun.Text,
					glyphRun.Typeface,
					bold,
					italic,
					segment.HasColor,
					segment.Color));
			}
		}

		return list;
	}

	internal static IReadOnlyList<IReadOnlyList<UiStyledTextRun>> BreakStyledLines(
		IReadOnlyList<UiStyledTextRun> runs,
		UiStyle style,
		SKPaint paint,
		float availableWidth,
		bool constrainWidth)
	{
		bool wrap = constrainWidth && availableWidth > 0.01f && style.WhiteSpace != UiWhiteSpace.NoWrap;
		List<IReadOnlyList<UiStyledTextRun>> paragraphs = SplitStyledParagraphs(runs);
		if (!wrap)
		{
			return paragraphs.Count == 0
				? new IReadOnlyList<UiStyledTextRun>[] { Array.Empty<UiStyledTextRun>() }
				: paragraphs;
		}

		var lines = new List<IReadOnlyList<UiStyledTextRun>>();
		for (int p = 0; p < paragraphs.Count; p++)
		{
			IReadOnlyList<UiStyledTextRun> paragraph = paragraphs[p];
			if (paragraph.Count == 0)
			{
				lines.Add(Array.Empty<UiStyledTextRun>());
				continue;
			}

			var current = new List<UiStyledTextRun>();
			float currentWidth = 0f;

			for (int i = 0; i < paragraph.Count; i++)
			{
				UiStyledTextRun run = paragraph[i];
				string text = run.Text ?? string.Empty;
				if (text.Length == 0)
				{
					continue;
				}

				string[] tokens = SplitKeepSeparators(text);
				for (int t = 0; t < tokens.Length; t++)
				{
					string token = tokens[t];
					if (token.Length == 0)
					{
						continue;
					}

					if (char.IsWhiteSpace(token[0]))
					{
						float wsWidth = MeasureStyledSegmentWidth(token, run, style, paint);
						AppendToStyledLine(current, run with { Text = token });
						currentWidth += wsWidth;
						continue;
					}

					AppendFittingToken(lines, ref current, ref currentWidth, token, run, style, paint, availableWidth);
				}
			}

			if (current.Count > 0)
			{
				lines.Add(CompactStyledLine(current));
			}
		}

		if (lines.Count == 0)
		{
			lines.Add(Array.Empty<UiStyledTextRun>());
		}

		return lines;
	}

	/// <summary>
	/// Split styled runs on U+000A hard newlines, preserving style across the break.
	/// Mirrors plain <c>BreakLines</c> paragraph splitting.
	/// </summary>
	private static List<IReadOnlyList<UiStyledTextRun>> SplitStyledParagraphs(IReadOnlyList<UiStyledTextRun> runs)
	{
		var paragraphs = new List<IReadOnlyList<UiStyledTextRun>>();
		var current = new List<UiStyledTextRun>();

		for (int i = 0; i < runs.Count; i++)
		{
			UiStyledTextRun run = runs[i];
			string text = (run.Text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
			if (text.Length == 0)
			{
				continue;
			}

			int start = 0;
			for (int c = 0; c < text.Length; c++)
			{
				if (text[c] != '\n')
				{
					continue;
				}

				if (c > start)
				{
					current.Add(run with { Text = text.Substring(start, c - start) });
				}

				paragraphs.Add(current.Count == 0 ? Array.Empty<UiStyledTextRun>() : current.ToArray());
				current = new List<UiStyledTextRun>();
				start = c + 1;
			}

			if (start < text.Length)
			{
				current.Add(run with { Text = start == 0 ? text : text.Substring(start) });
			}
		}

		if (current.Count > 0)
		{
			paragraphs.Add(current.ToArray());
		}

		if (paragraphs.Count == 0)
		{
			paragraphs.Add(Array.Empty<UiStyledTextRun>());
		}

		return paragraphs;
	}

	private static void AppendFittingToken(
		List<IReadOnlyList<UiStyledTextRun>> lines,
		ref List<UiStyledTextRun> current,
		ref float currentWidth,
		string token,
		UiStyledTextRun run,
		UiStyle style,
		SKPaint paint,
		float availableWidth)
	{
		int index = 0;
		while (index < token.Length)
		{
			int fitEnd = -1;
			for (int end = index + 1; end <= token.Length; end++)
			{
				string candidate = token.Substring(index, end - index);
				float candidateWidth = MeasureStyledSegmentWidth(candidate, run, style, paint);
				if (currentWidth + candidateWidth <= availableWidth)
				{
					fitEnd = end;
					continue;
				}

				break;
			}

			if (fitEnd < 0)
			{
				if (current.Count > 0)
				{
					lines.Add(CompactStyledLine(current));
					current = new List<UiStyledTextRun>();
					currentWidth = 0f;
					continue;
				}

				fitEnd = Math.Min(token.Length, index + 1);
			}

			string piece = token.Substring(index, fitEnd - index);
			float pieceWidth = MeasureStyledSegmentWidth(piece, run, style, paint);
			AppendToStyledLine(current, run with { Text = piece });
			currentWidth += pieceWidth;
			index = fitEnd;

			if (index < token.Length)
			{
				lines.Add(CompactStyledLine(current));
				current = new List<UiStyledTextRun>();
				currentWidth = 0f;
			}
		}
	}

	private static string[] SplitKeepSeparators(string text)
	{
		var parts = new List<string>();
		int i = 0;
		while (i < text.Length)
		{
			if (char.IsWhiteSpace(text[i]))
			{
				int start = i;
				while (i < text.Length && char.IsWhiteSpace(text[i]))
				{
					i++;
				}

				parts.Add(text.Substring(start, i - start));
				continue;
			}

			int wordStart = i;
			while (i < text.Length && !char.IsWhiteSpace(text[i]))
			{
				i++;
			}

			parts.Add(text.Substring(wordStart, i - wordStart));
		}

		return parts.ToArray();
	}

	private static void AppendToStyledLine(List<UiStyledTextRun> line, UiStyledTextRun segment)
	{
		if (line.Count > 0)
		{
			UiStyledTextRun last = line[^1];
			if (last.Bold == segment.Bold &&
				last.Italic == segment.Italic &&
				last.HasColor == segment.HasColor &&
				last.Color.Equals(segment.Color))
			{
				line[^1] = last with { Text = last.Text + segment.Text };
				return;
			}
		}

		line.Add(segment);
	}

	private static IReadOnlyList<UiStyledTextRun> CompactStyledLine(List<UiStyledTextRun> line) => line.ToArray();

	private static float MeasureStyledLineWidth(IReadOnlyList<UiStyledTextRun> runs, UiStyle style, SKPaint paint)
	{
		float width = 0f;
		for (int i = 0; i < runs.Count; i++)
		{
			width += MeasureStyledSegmentWidth(runs[i].Text, runs[i], style, paint);
		}

		return width;
	}

	private static float MeasureStyledSegmentWidth(string text, UiStyledTextRun segment, UiStyle style, SKPaint paint)
	{
		if (string.IsNullOrEmpty(text))
		{
			return 0f;
		}

		bool bold = segment.Bold || style.Bold;
		UiStyle measureStyle = style with { Bold = bold };
		return MeasureLineWidth(measureStyle, paint, text);
	}

	public static UiTextDirection ResolveDirection(string? text, UiTextDirection preferredDirection)
	{
		if (preferredDirection <= UiTextDirection.Rtl)
		{
			return preferredDirection;
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			return UiTextDirection.Ltr;
		}
		foreach (char ch in text)
		{
			if (IsStrongRtl(ch))
			{
				return UiTextDirection.Rtl;
			}
			if (IsStrongLtr(ch))
			{
				return UiTextDirection.Ltr;
			}
		}
		return UiTextDirection.Ltr;
	}

	public static string PrepareForRendering(string text, UiTextDirection direction)
	{
		return text;
	}

	private static List<string> BreakLines(string text, UiStyle style, SKPaint paint, float availableWidth, bool constrainWidth)
	{
		string text2 = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
		bool flag = constrainWidth && availableWidth > 0.01f && style.WhiteSpace != UiWhiteSpace.NoWrap;
		string[] array = text2.Split('\n');
		List<string> list = new List<string>();
		string[] array2 = array;
		foreach (string text3 in array2)
		{
			string text4 = ((style.WhiteSpace == UiWhiteSpace.PreWrap) ? text3 : CollapseWhitespace(text3));
			if (text4.Length == 0)
			{
				list.Add(string.Empty);
			}
			else if (!flag)
			{
				string text5 = text4;
				if (constrainWidth && style.WhiteSpace == UiWhiteSpace.NoWrap && style.TextOverflow == UiTextOverflow.Ellipsis)
				{
					text5 = ApplyEllipsis(text5, style, paint, availableWidth);
				}
				list.Add(text5);
			}
			else
			{
				WrapParagraph(text4, style, paint, availableWidth, list);
			}
		}
		if (list.Count == 0)
		{
			list.Add(string.Empty);
		}
		return list;
	}

	private static void WrapParagraph(string paragraph, UiStyle style, SKPaint paint, float availableWidth, ICollection<string> lines)
	{
		int i = 0;
		while (i < paragraph.Length)
		{
			int num = -1;
			int num2 = -1;
			int num3;
			for (int j = i + 1; j <= paragraph.Length; j++)
			{
				num3 = i;
				string text = paragraph.Substring(num3, j - num3);
				if (MeasureLineWidth(style, paint, text) <= availableWidth)
				{
					num = j;
					if (j < paragraph.Length && char.IsWhiteSpace(paragraph[j - 1]))
					{
						num2 = j;
					}
					continue;
				}
				break;
			}
			if (num < 0)
			{
				num = Math.Min(paragraph.Length, i + 1);
			}
			int num4 = ((num < paragraph.Length && num2 > i) ? num2 : num);
			num3 = i;
			string text2 = paragraph.Substring(num3, num4 - num3).TrimEnd();
			if (text2.Length == 0)
			{
				num3 = i;
				text2 = paragraph.Substring(num3, num - num3);
				num4 = num;
			}
			lines.Add(text2);
			for (i = num4; i < paragraph.Length && char.IsWhiteSpace(paragraph[i]); i++)
			{
			}
		}
	}

	private static string ApplyEllipsis(string text, UiStyle style, SKPaint paint, float availableWidth)
	{
		if (string.IsNullOrEmpty(text) || availableWidth <= 0.01f)
		{
			return string.Empty;
		}
		if (MeasureLineWidth(style, paint, text) <= availableWidth)
		{
			return text;
		}
		float num = MeasureLineWidth(style, paint, "…");
		if (num > availableWidth)
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder();
		TextElementEnumerator textElementEnumerator = StringInfo.GetTextElementEnumerator(text);
		while (textElementEnumerator.MoveNext())
		{
			string textElement = textElementEnumerator.GetTextElement();
			string text2 = stringBuilder.ToString() + textElement + "…";
			if (MeasureLineWidth(style, paint, text2) > availableWidth)
			{
				break;
			}
			stringBuilder.Append(textElement);
		}
		return (stringBuilder.Length == 0) ? "…" : (stringBuilder.ToString().TrimEnd() + "…");
	}

	private static float ResolveLineHeight(UiStyle style)
	{
		return style.FontSize * 1.4f;
	}

	private static float ResolveAscent(UiStyle style)
	{
		return style.FontSize * 0.8f;
	}

	private static float MeasureLineWidth(SKFont font, SKPaint paint, string text)
	{
		return string.IsNullOrEmpty(text) ? 0f : font.MeasureText(text, paint);
	}

	private static float MeasureLineWidth(UiStyle style, SKPaint paint, string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return 0f;
		}
		float num = 0f;
		foreach (UiTextRun item in CreateRuns(text, style))
		{
			using SKFont sKFont = new SKFont(item.Typeface, style.FontSize);
			num += sKFont.MeasureText(item.Text, paint);
		}
		return num;
	}

	private static string CollapseWhitespace(string text)
	{
		Span<char> span = stackalloc char[text.Length];
		int length = 0;
		bool flag = false;
		foreach (char c in text)
		{
			if (char.IsWhiteSpace(c))
			{
				if (!flag)
				{
					span[length++] = ' ';
					flag = true;
				}
			}
			else
			{
				span[length++] = c;
				flag = false;
			}
		}
		return new string(span.Slice(0, length)).Trim();
	}

	private static bool IsStrongRtl(char ch)
	{
		return (ch >= '\u0590' && ch <= '\u08ff') || (ch >= '\ufb1d' && ch <= '\ufdff') || (ch >= '\ufe70' && ch <= '\ufeff');
	}

	private static bool IsStrongLtr(char ch)
	{
		return (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '\u00c0' && ch <= '\u02af');
	}
}
