using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Ludots.UI.Runtime;
using SkiaSharp;

namespace Ludots.UI.Skia;

public sealed class SkiaTextMeasurer : IUiTextMeasurer
{
	UiTextLayoutResult IUiTextMeasurer.Measure(string? text, UiStyle style, float availableWidth, bool constrainWidth)
		=> UiTextLayout.Measure(text, style, availableWidth, constrainWidth);

	UiTextSize IUiTextMeasurer.MeasureSize(ReadOnlySpan<char> text, UiStyle style, float availableWidth, bool constrainWidth)
		=> UiTextLayout.MeasureSize(text, style, availableWidth, constrainWidth);

	float IUiTextMeasurer.MeasureWidth(string? text, UiStyle style)
		=> UiTextLayout.MeasureWidth(text, style);

	UiTextLayoutResult IUiTextMeasurer.Measure(IReadOnlyList<UiStyledTextRun> runs, UiStyle style, float availableWidth, bool constrainWidth)
		=> UiTextLayout.Measure(runs, style, availableWidth, constrainWidth);

	float IUiTextMeasurer.MeasureWidth(IReadOnlyList<UiStyledTextRun> runs, UiStyle style)
		=> UiTextLayout.MeasureWidth(runs, style);
}

public static class UiTextLayout
{
	private const string EllipsisText = "…";

	[ThreadStatic]
	private static UiTextLines? t_measureLines;

	[ThreadStatic]
	private static Dictionary<FontKey, SKFont>? t_fonts;

	public static UiTextLayoutResult Measure(string? text, UiStyle style, float availableWidth, bool constrainWidth)
	{
		if (string.IsNullOrEmpty(text))
		{
			float emptyLine = ResolveLineHeight(style);
			return new UiTextLayoutResult(Array.Empty<string>(), 0f, 0f, emptyLine, style.FontSize, Math.Max(0f, emptyLine - style.FontSize));
		}
		UiTextLines lines = MeasureLines;
		LayoutLines(text, style, availableWidth, constrainWidth, lines);
		var plainLines = new string[lines.Count];
		for (int i = 0; i < lines.Count; i++)
		{
			plainLines[i] = lines.LineString(i);
		}
		float lineHeight = ResolveLineHeight(style);
		float ascent = ResolveAscent(style);
		float descent = Math.Max(0f, lineHeight - ascent);
		return new UiTextLayoutResult(plainLines, lines.MaxWidth, lineHeight * lines.Count, lineHeight, ascent, descent);
	}

	public static UiTextSize MeasureSize(ReadOnlySpan<char> text, UiStyle style, float availableWidth, bool constrainWidth)
	{
		if (text.IsEmpty)
		{
			return default;
		}
		UiTextLines lines = MeasureLines;
		LayoutLines(text, style, availableWidth, constrainWidth, lines);
		return new UiTextSize(lines.MaxWidth, ResolveLineHeight(style) * lines.Count);
	}

	public static float MeasureWidth(string? text, UiStyle style)
	{
		return string.IsNullOrEmpty(text) ? 0f : MeasureRunWidth(text, style);
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

	/// <summary>
	/// 按 white-space / text-overflow 规则把纯文本折成行，写进 <paramref name="lines"/>。
	/// 每个词只量一次，放不下的长词按字素簇拆；尾随空白不占行宽（悬挂），和浏览器一致。
	/// </summary>
	internal static void LayoutLines(ReadOnlySpan<char> text, UiStyle style, float availableWidth, bool constrainWidth, UiTextLines lines)
	{
		lines.Reset();
		bool wrap = constrainWidth && availableWidth > 0.01f && style.WhiteSpace != UiWhiteSpace.NoWrap;
		bool ellipsis = !wrap && constrainWidth && style.WhiteSpace == UiWhiteSpace.NoWrap && style.TextOverflow == UiTextOverflow.Ellipsis;
		bool preserveWhiteSpace = style.WhiteSpace == UiWhiteSpace.PreWrap;
		int index = 0;
		while (true)
		{
			ReadOnlySpan<char> rest = text.Slice(index);
			int newline = rest.IndexOfAny('\r', '\n');
			ReadOnlySpan<char> paragraph = newline < 0 ? rest : rest.Slice(0, newline);
			int start = lines.CharCount;
			AppendParagraph(paragraph, preserveWhiteSpace, lines);
			int length = lines.CharCount - start;
			if (length == 0)
			{
				lines.AddLine(start, 0, 0f, 0f, ellipsis: false);
			}
			else if (wrap)
			{
				WrapParagraph(lines, start, length, style, availableWidth);
			}
			else if (ellipsis)
			{
				AddEllipsizedLine(lines, start, length, style, availableWidth);
			}
			else
			{
				float width = MeasureRunWidth(lines.Chars(start, length), style);
				lines.AddLine(start, length, width, width, ellipsis: false);
			}

			if (newline < 0)
			{
				break;
			}
			index += newline + (rest[newline] == '\r' && newline + 1 < rest.Length && rest[newline + 1] == '\n' ? 2 : 1);
		}
	}

	private static void AppendParagraph(ReadOnlySpan<char> paragraph, bool preserveWhiteSpace, UiTextLines lines)
	{
		if (preserveWhiteSpace)
		{
			for (int i = 0; i < paragraph.Length; i++)
			{
				lines.AppendChar(paragraph[i]);
			}
			return;
		}

		bool pendingSpace = false;
		bool any = false;
		for (int i = 0; i < paragraph.Length; i++)
		{
			char c = paragraph[i];
			if (char.IsWhiteSpace(c))
			{
				pendingSpace = any;
				continue;
			}
			if (pendingSpace)
			{
				lines.AppendChar(' ');
				pendingSpace = false;
			}
			lines.AppendChar(c);
			any = true;
		}
	}

	private static void WrapParagraph(UiTextLines lines, int start, int length, UiStyle style, float availableWidth)
	{
		ReadOnlySpan<char> p = lines.Chars(start, length);
		int lineStart = 0;
		float lineWidth = 0f;
		int contentEnd = 0;
		float contentWidth = 0f;
		int breakContentEnd = -1;
		float breakContentWidth = 0f;
		int breakNext = -1;
		int pos = 0;
		while (pos < length)
		{
			bool whiteSpace = char.IsWhiteSpace(p[pos]);
			int tokenEnd = pos + 1;
			while (tokenEnd < length && char.IsWhiteSpace(p[tokenEnd]) == whiteSpace)
			{
				tokenEnd++;
			}

			if (whiteSpace)
			{
				if (contentEnd > lineStart)
				{
					breakContentEnd = contentEnd;
					breakContentWidth = contentWidth;
					breakNext = tokenEnd;
				}
				lineWidth += MeasureRunWidth(p.Slice(pos, tokenEnd - pos), style);
				pos = tokenEnd;
				continue;
			}

			float tokenWidth = MeasureRunWidth(p.Slice(pos, tokenEnd - pos), style);
			if (lineWidth + tokenWidth <= availableWidth)
			{
				lineWidth += tokenWidth;
				pos = tokenEnd;
				contentEnd = pos;
				contentWidth = lineWidth;
				continue;
			}

			if (breakNext >= 0)
			{
				lines.AddLine(start + lineStart, breakContentEnd - lineStart, breakContentWidth, breakContentWidth, ellipsis: false);
				pos = lineStart = contentEnd = breakNext;
				lineWidth = contentWidth = 0f;
				breakNext = -1;
				continue;
			}

			int cut = pos;
			float cutWidth = lineWidth;
			while (cut < tokenEnd)
			{
				int elementLength = StringInfo.GetNextTextElementLength(p.Slice(cut, tokenEnd - cut));
				float elementWidth = MeasureRunWidth(p.Slice(cut, elementLength), style);
				if (cut > pos && cutWidth + elementWidth > availableWidth)
				{
					break;
				}
				cutWidth += elementWidth;
				cut += elementLength;
				if (cutWidth > availableWidth)
				{
					break;
				}
			}

			lines.AddLine(start + lineStart, cut - lineStart, cutWidth, cutWidth, ellipsis: false);
			pos = cut;
			while (pos < length && char.IsWhiteSpace(p[pos]))
			{
				pos++;
			}
			lineStart = contentEnd = pos;
			lineWidth = contentWidth = 0f;
		}

		if (lineStart < length)
		{
			if (contentEnd > lineStart)
			{
				lines.AddLine(start + lineStart, contentEnd - lineStart, contentWidth, contentWidth, ellipsis: false);
			}
			else
			{
				lines.AddLine(start + lineStart, length - lineStart, lineWidth, lineWidth, ellipsis: false);
			}
		}
	}

	private static void AddEllipsizedLine(UiTextLines lines, int start, int length, UiStyle style, float availableWidth)
	{
		ReadOnlySpan<char> p = lines.Chars(start, length);
		if (availableWidth <= 0.01f)
		{
			lines.AddLine(start, 0, 0f, 0f, ellipsis: false);
			return;
		}
		float fullWidth = MeasureRunWidth(p, style);
		if (fullWidth <= availableWidth)
		{
			lines.AddLine(start, length, fullWidth, fullWidth, ellipsis: false);
			return;
		}
		float ellipsisWidth = MeasureRunWidth(EllipsisText, style);
		if (ellipsisWidth > availableWidth)
		{
			lines.AddLine(start, 0, 0f, 0f, ellipsis: false);
			return;
		}

		int cut = 0;
		float cutWidth = 0f;
		int contentEnd = 0;
		float contentWidth = 0f;
		while (cut < length)
		{
			int elementLength = StringInfo.GetNextTextElementLength(p.Slice(cut));
			float elementWidth = MeasureRunWidth(p.Slice(cut, elementLength), style);
			if (cutWidth + elementWidth + ellipsisWidth > availableWidth)
			{
				break;
			}
			cutWidth += elementWidth;
			cut += elementLength;
			if (!char.IsWhiteSpace(p[cut - 1]))
			{
				contentEnd = cut;
				contentWidth = cutWidth;
			}
		}
		lines.AddLine(start, contentEnd, contentWidth, contentWidth + ellipsisWidth, ellipsis: true);
	}

	/// <summary>不折行地量一段文字的宽度；按字素簇选字体，同字体的连续段合起来量。</summary>
	internal static float MeasureRunWidth(ReadOnlySpan<char> text, UiStyle style)
	{
		if (text.IsEmpty)
		{
			return 0f;
		}
		var runs = new TypefaceRuns(text, style);
		float width = 0f;
		while (runs.MoveNext())
		{
			width += GetFont(runs.Typeface, style.FontSize).MeasureText(runs.Current);
		}
		return width;
	}

	/// <summary>从 <paramref name="x"/> 起画一段不折行的文字，返回画完后的横坐标。</summary>
	internal static float DrawRun(SKCanvas canvas, ReadOnlySpan<char> text, float x, float baselineY, UiStyle style, SKPaint paint)
	{
		var runs = new TypefaceRuns(text, style);
		while (runs.MoveNext())
		{
			SKFont font = GetFont(runs.Typeface, style.FontSize);
			canvas.DrawText(runs.Current, x, baselineY, font, paint);
			x += font.MeasureText(runs.Current);
		}
		return x;
	}

	internal static void DrawEllipsis(SKCanvas canvas, float x, float baselineY, UiStyle style, SKPaint paint)
	{
		DrawRun(canvas, EllipsisText, x, baselineY, style, paint);
	}

	private static UiTextLines MeasureLines => t_measureLines ??= new UiTextLines();

	private static SKFont GetFont(SKTypeface typeface, float size)
	{
		Dictionary<FontKey, SKFont> fonts = t_fonts ??= new Dictionary<FontKey, SKFont>(FontKeyComparer.Instance);
		var key = new FontKey(typeface, size);
		if (!fonts.TryGetValue(key, out SKFont? font))
		{
			font = new SKFont(typeface, size);
			fonts[key] = font;
		}
		return font;
	}

	private ref struct TypefaceRuns
	{
		private readonly ReadOnlySpan<char> _text;
		private readonly string? _family;
		private readonly bool _bold;
		private readonly bool _italic;
		private readonly SKTypeface _preferred;
		private int _position;

		public TypefaceRuns(ReadOnlySpan<char> text, UiStyle style)
		{
			_text = text;
			_family = style.FontFamily;
			_bold = style.Bold;
			_italic = style.Italic;
			_preferred = UiFontRegistry.ResolveTypeface(style.FontFamily, style.Bold, style.Italic);
			_position = 0;
			Current = default;
			Typeface = _preferred;
		}

		public ReadOnlySpan<char> Current { get; private set; }

		public SKTypeface Typeface { get; private set; }

		public bool MoveNext()
		{
			if (_position >= _text.Length)
			{
				return false;
			}
			int start = _position;
			SKTypeface? run = null;
			while (_position < _text.Length)
			{
				ReadOnlySpan<char> rest = _text.Slice(_position);
				int elementLength = StringInfo.GetNextTextElementLength(rest);
				SKTypeface typeface = ResolveElementTypeface(rest.Slice(0, elementLength));
				if (run != null && !ReferenceEquals(run, typeface))
				{
					break;
				}
				run = typeface;
				_position += elementLength;
			}
			Current = _text.Slice(start, _position - start);
			Typeface = run!;
			return true;
		}

		private SKTypeface ResolveElementTypeface(ReadOnlySpan<char> element)
		{
			Rune.DecodeFromUtf16(element, out Rune rune, out int consumed);
			return consumed == element.Length
				? UiFontRegistry.ResolveTypefaceForCodePoint(_family, _bold, _italic, _preferred, rune.Value)
				: UiFontRegistry.ResolveTypefaceForTextElement(_family, _bold, new string(element), _italic);
		}
	}

	private readonly record struct FontKey(SKTypeface Typeface, float Size);

	private sealed class FontKeyComparer : IEqualityComparer<FontKey>
	{
		public static readonly FontKeyComparer Instance = new FontKeyComparer();

		public bool Equals(FontKey x, FontKey y) => x.Size == y.Size && ReferenceEquals(x.Typeface, y.Typeface);

		public int GetHashCode(FontKey key) => HashCode.Combine(RuntimeHelpers.GetHashCode(key.Typeface), key.Size);
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
	/// Mirrors plain <c>LayoutLines</c> paragraph splitting.
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
		return ResolveDirection(text.AsSpan(), preferredDirection);
	}

	public static UiTextDirection ResolveDirection(ReadOnlySpan<char> text, UiTextDirection preferredDirection)
	{
		if (preferredDirection <= UiTextDirection.Rtl)
		{
			return preferredDirection;
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

	private static float ResolveLineHeight(UiStyle style)
	{
		return style.FontSize * style.LineHeight;
	}

	private static float ResolveAscent(UiStyle style)
	{
		return style.FontSize * 0.8f;
	}

	private static float MeasureLineWidth(UiStyle style, SKPaint paint, string text)
	{
		return string.IsNullOrEmpty(text) ? 0f : MeasureRunWidth(text, style);
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
