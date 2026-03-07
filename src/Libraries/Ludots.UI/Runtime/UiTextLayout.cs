using SkiaSharp;

namespace Ludots.UI.Runtime;

public static class UiTextLayout
{
    public static UiTextLayoutResult Measure(string? text, UiStyle style, float availableWidth, bool constrainWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new UiTextLayoutResult(Array.Empty<string>(), 0f, 0f, style.FontSize * 1.4f);
        }

        using SKPaint paint = CreatePaint(style);
        using SKFont font = CreateFont(style);
        List<string> lines = BreakLines(text, style, font, paint, availableWidth, constrainWidth);
        float lineHeight = style.FontSize * 1.4f;
        float maxLineWidth = 0f;

        for (int i = 0; i < lines.Count; i++)
        {
            float lineWidth = MeasureLineWidth(font, paint, lines[i]);
            if (lineWidth > maxLineWidth)
            {
                maxLineWidth = lineWidth;
            }
        }

        return new UiTextLayoutResult(lines, maxLineWidth, lineHeight * lines.Count, lineHeight);
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

    private static List<string> BreakLines(string text, UiStyle style, SKFont font, SKPaint paint, float availableWidth, bool constrainWidth)
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
                lines.Add(paragraph);
                continue;
            }

            WrapParagraph(paragraph, font, paint, availableWidth, lines);
        }

        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        return lines;
    }

    private static void WrapParagraph(string paragraph, SKFont font, SKPaint paint, float availableWidth, ICollection<string> lines)
    {
        int start = 0;
        while (start < paragraph.Length)
        {
            int bestBreak = -1;
            int lastWhitespaceBreak = -1;

            for (int index = start + 1; index <= paragraph.Length; index++)
            {
                string candidate = paragraph[start..index];
                if (MeasureLineWidth(font, paint, candidate) <= availableWidth)
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

    private static float MeasureLineWidth(SKFont font, SKPaint paint, string text)
    {
        return string.IsNullOrEmpty(text) ? 0f : font.MeasureText(text, paint);
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
}

public sealed record UiTextLayoutResult(IReadOnlyList<string> Lines, float Width, float Height, float LineHeight);
