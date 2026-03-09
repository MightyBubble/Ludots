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
        List<string> lines = BreakLines(text, style, paint, availableWidth, constrainWidth);
        float lineHeight = ResolveLineHeight(style);
        float maxLineWidth = 0f;

        for (int i = 0; i < lines.Count; i++)
        {
            float lineWidth = MeasureLineWidth(style, paint, lines[i]);
            if (lineWidth > maxLineWidth)
            {
                maxLineWidth = lineWidth;
            }
        }

        return new UiTextLayoutResult(lines, maxLineWidth, lineHeight * lines.Count, lineHeight);
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

        List<UiTextRun> runs = new();
        StringBuilder buffer = new();
        SKTypeface? currentTypeface = null;
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            string element = enumerator.GetTextElement();
            SKTypeface typeface = UiFontRegistry.ResolveTypefaceForTextElement(style.FontFamily, style.Bold, element);
            if (currentTypeface != null && !UiFontRegistry.SameTypeface(currentTypeface, typeface))
            {
                runs.Add(new UiTextRun(buffer.ToString(), currentTypeface));
                buffer.Clear();
            }

            currentTypeface = typeface;
            buffer.Append(element);
        }

        if (buffer.Length > 0 && currentTypeface != null)
        {
            runs.Add(new UiTextRun(buffer.ToString(), currentTypeface));
        }

        return runs;
    }

    public static UiTextDirection ResolveDirection(string? text, UiTextDirection preferredDirection)
    {
        if (preferredDirection is UiTextDirection.Ltr or UiTextDirection.Rtl)
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
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        bool shouldWrap = constrainWidth && availableWidth > 0.01f && style.WhiteSpace != UiWhiteSpace.NoWrap;
        string[] paragraphs = normalized.Split('\n');
        List<string> lines = new();

        foreach (string rawParagraph in paragraphs)
        {
            string paragraph = style.WhiteSpace == UiWhiteSpace.PreWrap ? rawParagraph : CollapseWhitespace(rawParagraph);
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            if (!shouldWrap)
            {
                string singleLine = paragraph;
                if (constrainWidth && style.WhiteSpace == UiWhiteSpace.NoWrap && style.TextOverflow == UiTextOverflow.Ellipsis)
                {
                    singleLine = ApplyEllipsis(singleLine, style, paint, availableWidth);
                }

                lines.Add(singleLine);
                continue;
            }

            WrapParagraph(paragraph, style, paint, availableWidth, lines);
        }

        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        return lines;
    }

    private static void WrapParagraph(string paragraph, UiStyle style, SKPaint paint, float availableWidth, ICollection<string> lines)
    {
        int start = 0;
        while (start < paragraph.Length)
        {
            int bestBreak = -1;
            int lastWhitespaceBreak = -1;

            for (int index = start + 1; index <= paragraph.Length; index++)
            {
                string candidate = paragraph[start..index];
                if (MeasureLineWidth(style, paint, candidate) <= availableWidth)
                {
                    bestBreak = index;
                    if (index < paragraph.Length && char.IsWhiteSpace(paragraph[index - 1]))
                    {
                        lastWhitespaceBreak = index;
                    }

                    continue;
                }

                break;
            }

            if (bestBreak < 0)
            {
                bestBreak = Math.Min(paragraph.Length, start + 1);
            }

            int lineBreak = lastWhitespaceBreak > start ? lastWhitespaceBreak : bestBreak;
            string line = paragraph[start..lineBreak].TrimEnd();
            if (line.Length == 0)
            {
                line = paragraph[start..bestBreak];
                lineBreak = bestBreak;
            }

            lines.Add(line);
            start = lineBreak;
            while (start < paragraph.Length && char.IsWhiteSpace(paragraph[start]))
            {
                start++;
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

        const string ellipsis = "…";
        float ellipsisWidth = MeasureLineWidth(style, paint, ellipsis);
        if (ellipsisWidth > availableWidth)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            string element = enumerator.GetTextElement();
            string candidate = builder + element + ellipsis;
            if (MeasureLineWidth(style, paint, candidate) > availableWidth)
            {
                break;
            }

            builder.Append(element);
        }

        return builder.Length == 0 ? ellipsis : builder.ToString().TrimEnd() + ellipsis;
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

        float totalWidth = 0f;
        foreach (UiTextRun run in CreateRuns(text, style))
        {
            using SKFont font = new(run.Typeface, style.FontSize);
            totalWidth += font.MeasureText(run.Text, paint);
        }

        return totalWidth;
    }

    private static string CollapseWhitespace(string text)
    {
        Span<char> buffer = stackalloc char[text.Length];
        int count = 0;
        bool previousWhitespace = false;

        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];
            if (char.IsWhiteSpace(current))
            {
                if (previousWhitespace)
                {
                    continue;
                }

                buffer[count++] = ' ';
                previousWhitespace = true;
                continue;
            }

            buffer[count++] = current;
            previousWhitespace = false;
        }

        return new string(buffer[..count]).Trim();
    }

    private static bool IsStrongRtl(char ch)
    {
        return (ch >= '\u0590' && ch <= '\u08FF')
            || (ch >= '\uFB1D' && ch <= '\uFDFF')
            || (ch >= '\uFE70' && ch <= '\uFEFF');
    }

    private static bool IsStrongLtr(char ch)
    {
        return (ch >= 'A' && ch <= 'Z')
            || (ch >= 'a' && ch <= 'z')
            || (ch >= '\u0041' && ch <= '\u024F');
    }
}

public sealed record UiTextLayoutResult(IReadOnlyList<string> Lines, float Width, float Height, float LineHeight);

internal sealed record UiTextRun(string Text, SKTypeface Typeface);
