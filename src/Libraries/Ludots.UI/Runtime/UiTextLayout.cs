using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SkiaSharp;

namespace Ludots.UI.Runtime;

public static class UiTextLayout
{
	public static UiTextLayoutResult Measure(string? text, UiStyle style, float availableWidth, bool constrainWidth)
	{
		if (string.IsNullOrEmpty(text))
		{
			return new UiTextLayoutResult(Array.Empty<string>(), 0f, 0f, ResolveLineHeight(style));
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
		return new UiTextLayoutResult(list, num2, num * (float)list.Count, num);
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

	public static SKPaint CreatePaint(UiStyle style)
	{
		return new SKPaint
		{
			Color = style.Color,
			IsAntialias = true
		};
	}

	public static SKFont CreateFont(UiStyle style)
	{
		return new SKFont(UiFontRegistry.ResolveTypeface(style.FontFamily, style.Bold), style.FontSize);
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
			SKTypeface sKTypeface2 = UiFontRegistry.ResolveTypefaceForTextElement(style.FontFamily, style.Bold, textElement);
			if (sKTypeface != null && !UiFontRegistry.SameTypeface(sKTypeface, sKTypeface2))
			{
				list.Add(new UiTextRun(stringBuilder.ToString(), sKTypeface));
				stringBuilder.Clear();
			}
			sKTypeface = sKTypeface2;
			stringBuilder.Append(textElement);
		}
		if (stringBuilder.Length > 0 && sKTypeface != null)
		{
			list.Add(new UiTextRun(stringBuilder.ToString(), sKTypeface));
		}
		return list;
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
