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

	[ThreadStatic]
	private static Dictionary<FontKey, GlyphTable>? t_glyphTables;

	[ThreadStatic]
	private static GlyphTable? t_lastGlyphs;

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
		if (!char.IsWhiteSpace(p[length - 1]))
		{
			float whole = MeasureRunWidth(p, style);
			if (whole <= availableWidth)
			{
				lines.AddLine(start, length, whole, whole, ellipsis: false);
				return;
			}
		}
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

	/// <summary>
	/// 不折行地量一段文字的宽度：逐字素簇按所选字体的步进相加，和 Skia 对同一段文字的 MeasureText 一致。
	/// 常见字符的字体和步进按（首选字体，字号）缓存在本线程，命中时不进 Skia。
	/// </summary>
	internal static float MeasureRunWidth(ReadOnlySpan<char> text, UiStyle style)
	{
		if (text.IsEmpty)
		{
			return 0f;
		}
		GlyphTable glyphs = Glyphs(style);
		float width = 0f;
		int position = 0;
		while (position < text.Length)
		{
			char c = text[position];
			if (IsStandaloneAt(text, position))
			{
				width += glyphs.Get(c, style).Advance;
				position++;
				continue;
			}
			ReadOnlySpan<char> element = text.Slice(position, StringInfo.GetNextTextElementLength(text.Slice(position)));
			width += GetFont(ResolveElementTypeface(element, style, glyphs.Preferred), style.FontSize).MeasureText(element);
			position += element.Length;
		}
		return width;
	}

	/// <summary>从 <paramref name="x"/> 起画一段不折行的文字。</summary>
	internal static void DrawRun(SKCanvas canvas, ReadOnlySpan<char> text, float x, float baselineY, UiStyle style, SKPaint paint)
	{
		var runs = new TypefaceRuns(text, style);
		if (!runs.MoveNext())
		{
			return;
		}
		while (true)
		{
			ReadOnlySpan<char> run = runs.Current;
			canvas.DrawText(run, x, baselineY, GetFont(runs.Typeface, style.FontSize), paint);
			if (!runs.MoveNext())
			{
				return;
			}
			x += MeasureRunWidth(run, style);
		}
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

	private static GlyphTable Glyphs(UiStyle style)
	{
		GlyphTable? last = t_lastGlyphs;
		if (last != null && last.Matches(style))
		{
			return last;
		}
		SKTypeface preferred = UiFontRegistry.ResolveTypeface(style.FontFamily, style.Bold, style.Italic);
		Dictionary<FontKey, GlyphTable> tables = t_glyphTables ??= new Dictionary<FontKey, GlyphTable>(FontKeyComparer.Instance);
		var key = new FontKey(preferred, style.FontSize);
		if (!tables.TryGetValue(key, out GlyphTable? table))
		{
			table = new GlyphTable(preferred, style.FontSize);
			tables[key] = table;
		}
		table.Remember(style);
		t_lastGlyphs = table;
		return table;
	}

	/// <summary>
	/// 这些字符自成一个字素簇，且不会和前一个字符粘成簇（不是组合附标、ZWJ、变体选择符、代理项、CR），
	/// 本字符和下一个字符都满足时可跳过 Unicode 分簇。
	/// </summary>
	private static bool IsStandaloneChar(char c)
	{
		return (c < '\u0300' && c != '\r')
			|| (c >= '\u2010' && c <= '\u2027')
			|| (c >= '\u2030' && c <= '\u205E')
			|| (c >= '\u2190' && c <= '\u23FF')
			|| (c >= '\u2500' && c <= '\u25FF')
			|| (c >= '\u3000' && c <= '\u3029')
			|| (c >= '\u4E00' && c <= '\u9FFF')
			|| (c >= '\uFF01' && c <= '\uFF60');
	}

	private static bool IsStandaloneAt(ReadOnlySpan<char> text, int position)
	{
		return IsStandaloneChar(text[position]) && (position + 1 == text.Length || IsStandaloneChar(text[position + 1]));
	}

	private static SKTypeface ResolveElementTypeface(ReadOnlySpan<char> element, UiStyle style, SKTypeface preferred)
	{
		Rune.DecodeFromUtf16(element, out Rune rune, out int consumed);
		return consumed == element.Length
			? UiFontRegistry.ResolveTypefaceForCodePoint(style.FontFamily, style.Bold, style.Italic, preferred, rune.Value)
			: UiFontRegistry.ResolveTypefaceForTextElement(style.FontFamily, style.Bold, new string(element), style.Italic);
	}

	private readonly struct Glyph
	{
		public readonly SKTypeface Typeface;
		public readonly float Advance;

		public Glyph(SKTypeface typeface, float advance)
		{
			Typeface = typeface;
			Advance = advance;
		}
	}

	/// <summary>
	/// 一个（首选字体，字号）下单字符的字体和步进。字体注册表变了（<see cref="UiFontRegistry.Generation"/>）就整表作废，
	/// 因为回退字体可能跟着换。
	/// </summary>
	private sealed class GlyphTable
	{
		private const int DirectCount = 0x300;

		private const int OtherCapacity = 512;

		private readonly Glyph[] _direct = new Glyph[DirectCount];

		// 预留容量，界面里第一次出现的字符填缓存时不扩容。
		private readonly Dictionary<char, Glyph> _other = new Dictionary<char, Glyph>(OtherCapacity);

		private int _generation;

		public GlyphTable(SKTypeface preferred, float size)
		{
			Preferred = preferred;
			Size = size;
			_generation = UiFontRegistry.Generation;
		}

		public SKTypeface Preferred { get; }

		public float Size { get; }

		private string? _lastFamily;
		private bool _lastBold;
		private bool _lastItalic;
		private bool _seeded;

		/// <summary>同一个字体族串（按引用）、粗斜体、字号且注册表没变时直接命中，省掉按族名查首选字体。</summary>
		public bool Matches(UiStyle style)
		{
			return ReferenceEquals(style.FontFamily, _lastFamily)
				&& style.Bold == _lastBold
				&& style.Italic == _lastItalic
				&& style.FontSize == Size
				&& _generation == UiFontRegistry.Generation;
		}

		public void Remember(UiStyle style)
		{
			_lastFamily = style.FontFamily;
			_lastBold = style.Bold;
			_lastItalic = style.Italic;
			if (_seeded)
			{
				return;
			}
			_seeded = true;
			// 数字格式会晚于第一段文字才出现的符号（负号、单位、千分位）在建表时就解析完，
			// 否则第一次写出它们要现选字体，稳态帧会分配。
			ReadOnlySpan<char> seed = "0123456789+-. ,%/\u00B7\u2013\u2014\u00B2\u2026KBMNAx";
			for (int i = 0; i < seed.Length; i++)
			{
				Get(seed[i], style);
			}
		}

		public Glyph Get(char c, UiStyle style)
		{
			int generation = UiFontRegistry.Generation;
			if (generation != _generation)
			{
				Array.Clear(_direct);
				_other.Clear();
				_generation = generation;
			}
			if (c < DirectCount)
			{
				ref Glyph slot = ref _direct[c];
				if (slot.Typeface == null)
				{
					slot = Resolve(c, style);
				}
				return slot;
			}
			if (!_other.TryGetValue(c, out Glyph glyph))
			{
				glyph = Resolve(c, style);
				_other[c] = glyph;
			}
			return glyph;
		}

		private Glyph Resolve(char c, UiStyle style)
		{
			SKTypeface typeface = UiFontRegistry.ResolveTypefaceForCodePoint(style.FontFamily, style.Bold, style.Italic, Preferred, c);
			return new Glyph(typeface, GetFont(typeface, Size).MeasureText(new ReadOnlySpan<char>(in c)));
		}
	}

	private ref struct TypefaceRuns
	{
		private readonly ReadOnlySpan<char> _text;
		private readonly UiStyle _style;
		private readonly GlyphTable _glyphs;
		private int _position;

		public TypefaceRuns(ReadOnlySpan<char> text, UiStyle style)
		{
			_text = text;
			_style = style;
			_glyphs = Glyphs(style);
			_position = 0;
			Current = default;
			Typeface = _glyphs.Preferred;
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
				SKTypeface typeface;
				int elementLength;
				if (IsStandaloneAt(_text, _position))
				{
					elementLength = 1;
					typeface = _glyphs.Get(_text[_position], _style).Typeface;
				}
				else
				{
					ReadOnlySpan<char> rest = _text.Slice(_position);
					elementLength = StringInfo.GetNextTextElementLength(rest);
					typeface = ResolveElementTypeface(rest.Slice(0, elementLength), _style, _glyphs.Preferred);
				}
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
