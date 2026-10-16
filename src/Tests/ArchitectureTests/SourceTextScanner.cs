using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ludots.Tests.Architecture
{
    internal static class SourceTextScanner
    {
        public static IEnumerable<(int LineNumber, string Text)> ReadCodeLines(string file)
        {
            if (!string.Equals(Path.GetExtension(file), ".cs", StringComparison.OrdinalIgnoreCase))
            {
                int rawLineNumber = 0;
                foreach (string line in File.ReadLines(file))
                {
                    rawLineNumber++;
                    yield return (rawLineNumber, line);
                }

                yield break;
            }

            bool inBlockComment = false;
            bool inVerbatimString = false;
            int rawStringQuoteCount = 0;
            int lineNumber = 0;
            foreach (string line in File.ReadLines(file))
            {
                lineNumber++;
                yield return (lineNumber, StripCSharpCommentsAndStrings(line, ref inBlockComment, ref inVerbatimString, ref rawStringQuoteCount));
            }
        }

        private static string StripCSharpCommentsAndStrings(
            string line,
            ref bool inBlockComment,
            ref bool inVerbatimString,
            ref int rawStringQuoteCount)
        {
            var output = new StringBuilder(line.Length);
            int index = 0;
            while (index < line.Length)
            {
                if (inBlockComment)
                {
                    int end = line.IndexOf("*/", index, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        AppendSpaces(output, line.Length - index);
                        break;
                    }

                    AppendSpaces(output, end + 2 - index);
                    index = end + 2;
                    inBlockComment = false;
                    continue;
                }

                if (inVerbatimString)
                {
                    int end = FindVerbatimStringEnd(line, index);
                    if (end < 0)
                    {
                        AppendSpaces(output, line.Length - index);
                        break;
                    }

                    AppendSpaces(output, end + 1 - index);
                    index = end + 1;
                    inVerbatimString = false;
                    continue;
                }

                if (rawStringQuoteCount > 0)
                {
                    int end = FindQuoteRun(line, index, rawStringQuoteCount);
                    if (end < 0)
                    {
                        AppendSpaces(output, line.Length - index);
                        break;
                    }

                    AppendSpaces(output, end + rawStringQuoteCount - index);
                    index = end + rawStringQuoteCount;
                    rawStringQuoteCount = 0;
                    continue;
                }

                if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '/')
                {
                    AppendSpaces(output, line.Length - index);
                    break;
                }

                if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '*')
                {
                    AppendSpaces(output, 2);
                    index += 2;
                    inBlockComment = true;
                    continue;
                }

                if (line[index] == '"')
                {
                    int quoteCount = CountQuoteRun(line, index);
                    if (quoteCount >= 3)
                    {
                        int sameLineEnd = FindQuoteRun(line, index + quoteCount, quoteCount);
                        if (sameLineEnd < 0)
                        {
                            rawStringQuoteCount = quoteCount;
                            AppendSpaces(output, line.Length - index);
                            break;
                        }

                        AppendSpaces(output, sameLineEnd + quoteCount - index);
                        index = sameLineEnd + quoteCount;
                        continue;
                    }

                    if (IsInterpolatedStringStart(line, index))
                    {
                        output.Append(' ');
                        int interpolatedEnd = IsVerbatimStringStart(line, index)
                            ? StripInterpolatedVerbatimString(line, index + 1, output)
                            : StripInterpolatedRegularString(line, index + 1, output);
                        index = interpolatedEnd;
                        continue;
                    }

                    if (IsVerbatimStringStart(line, index))
                    {
                        int end = FindVerbatimStringEnd(line, index + 1);
                        if (end < 0)
                        {
                            inVerbatimString = true;
                            AppendSpaces(output, line.Length - index);
                            break;
                        }

                        AppendSpaces(output, end + 1 - index);
                        index = end + 1;
                        continue;
                    }

                    int regularEnd = FindRegularStringEnd(line, index + 1);
                    AppendSpaces(output, (regularEnd < 0 ? line.Length : regularEnd + 1) - index);
                    index = regularEnd < 0 ? line.Length : regularEnd + 1;
                    continue;
                }

                if (line[index] == '\'')
                {
                    int charEnd = FindCharLiteralEnd(line, index + 1);
                    AppendSpaces(output, (charEnd < 0 ? line.Length : charEnd + 1) - index);
                    index = charEnd < 0 ? line.Length : charEnd + 1;
                    continue;
                }

                output.Append(line[index]);
                index++;
            }

            return output.ToString();
        }

        private static bool IsInterpolatedStringStart(string line, int quoteIndex)
        {
            return quoteIndex > 0 &&
                   (line[quoteIndex - 1] == '$' ||
                    (quoteIndex > 1 && line[quoteIndex - 2] == '$' && line[quoteIndex - 1] == '@') ||
                    (quoteIndex > 1 && line[quoteIndex - 2] == '@' && line[quoteIndex - 1] == '$'));
        }

        private static bool IsVerbatimStringStart(string line, int quoteIndex)
        {
            return quoteIndex > 0 &&
                   (line[quoteIndex - 1] == '@' ||
                    (quoteIndex > 1 && line[quoteIndex - 2] == '@' && line[quoteIndex - 1] == '$'));
        }

        private static int StripInterpolatedRegularString(string line, int start, StringBuilder output)
        {
            int index = start;
            bool escaped = false;
            while (index < line.Length)
            {
                if (escaped)
                {
                    output.Append(' ');
                    escaped = false;
                    index++;
                    continue;
                }

                char ch = line[index];
                if (ch == '\\')
                {
                    output.Append(' ');
                    escaped = true;
                    index++;
                    continue;
                }

                if (ch == '"')
                {
                    output.Append(' ');
                    return index + 1;
                }

                if (ch == '{')
                {
                    if (index + 1 < line.Length && line[index + 1] == '{')
                    {
                        AppendSpaces(output, 2);
                        index += 2;
                        continue;
                    }

                    output.Append(' ');
                    index = AppendInterpolatedExpressionCode(line, index + 1, output);
                    continue;
                }

                if (ch == '}' && index + 1 < line.Length && line[index + 1] == '}')
                {
                    AppendSpaces(output, 2);
                    index += 2;
                    continue;
                }

                output.Append(' ');
                index++;
            }

            return line.Length;
        }

        private static int StripInterpolatedVerbatimString(string line, int start, StringBuilder output)
        {
            int index = start;
            while (index < line.Length)
            {
                char ch = line[index];
                if (ch == '"')
                {
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        AppendSpaces(output, 2);
                        index += 2;
                        continue;
                    }

                    output.Append(' ');
                    return index + 1;
                }

                if (ch == '{')
                {
                    if (index + 1 < line.Length && line[index + 1] == '{')
                    {
                        AppendSpaces(output, 2);
                        index += 2;
                        continue;
                    }

                    output.Append(' ');
                    index = AppendInterpolatedExpressionCode(line, index + 1, output);
                    continue;
                }

                if (ch == '}' && index + 1 < line.Length && line[index + 1] == '}')
                {
                    AppendSpaces(output, 2);
                    index += 2;
                    continue;
                }

                output.Append(' ');
                index++;
            }

            return line.Length;
        }

        private static int AppendInterpolatedExpressionCode(string line, int start, StringBuilder output)
        {
            int depth = 0;
            int index = start;
            while (index < line.Length)
            {
                if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '/')
                {
                    AppendSpaces(output, line.Length - index);
                    return line.Length;
                }

                if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '*')
                {
                    int end = line.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        AppendSpaces(output, line.Length - index);
                        return line.Length;
                    }

                    AppendSpaces(output, end + 2 - index);
                    index = end + 2;
                    continue;
                }

                if (line[index] == '"')
                {
                    int quoteCount = CountQuoteRun(line, index);
                    if (quoteCount >= 3)
                    {
                        int sameLineEnd = FindQuoteRun(line, index + quoteCount, quoteCount);
                        AppendSpaces(output, (sameLineEnd < 0 ? line.Length : sameLineEnd + quoteCount) - index);
                        index = sameLineEnd < 0 ? line.Length : sameLineEnd + quoteCount;
                        continue;
                    }

                    int end = IsVerbatimStringStart(line, index)
                        ? FindVerbatimStringEnd(line, index + 1)
                        : FindRegularStringEnd(line, index + 1);
                    AppendSpaces(output, (end < 0 ? line.Length : end + 1) - index);
                    index = end < 0 ? line.Length : end + 1;
                    continue;
                }

                if (line[index] == '\'')
                {
                    int charEnd = FindCharLiteralEnd(line, index + 1);
                    AppendSpaces(output, (charEnd < 0 ? line.Length : charEnd + 1) - index);
                    index = charEnd < 0 ? line.Length : charEnd + 1;
                    continue;
                }

                if (line[index] == '{')
                {
                    depth++;
                    output.Append(line[index]);
                    index++;
                    continue;
                }

                if (line[index] == '}')
                {
                    if (depth == 0)
                    {
                        output.Append(' ');
                        return index + 1;
                    }

                    depth--;
                    output.Append(line[index]);
                    index++;
                    continue;
                }

                output.Append(line[index]);
                index++;
            }

            return line.Length;
        }

        private static int FindVerbatimStringEnd(string line, int start)
        {
            for (int i = start; i < line.Length; i++)
            {
                if (line[i] != '"')
                {
                    continue;
                }

                if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                return i;
            }

            return -1;
        }

        private static int FindRegularStringEnd(string line, int start)
        {
            bool escaped = false;
            for (int i = start; i < line.Length; i++)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (line[i] == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (line[i] == '"')
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindCharLiteralEnd(string line, int start)
        {
            bool escaped = false;
            for (int i = start; i < line.Length; i++)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (line[i] == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (line[i] == '\'')
                {
                    return i;
                }
            }

            return -1;
        }

        private static int CountQuoteRun(string line, int start)
        {
            int count = 0;
            while (start + count < line.Length && line[start + count] == '"')
            {
                count++;
            }

            return count;
        }

        private static int FindQuoteRun(string line, int start, int quoteCount)
        {
            for (int i = start; i <= line.Length - quoteCount; i++)
            {
                int run = CountQuoteRun(line, i);
                if (run >= quoteCount)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void AppendSpaces(StringBuilder builder, int count)
        {
            for (int i = 0; i < count; i++)
            {
                builder.Append(' ');
            }
        }

        public static IEnumerable<(int LineNumber, string CommentText)> ReadCommentSegments(string file)
        {
            if (!string.Equals(Path.GetExtension(file), ".cs", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            string text = File.ReadAllText(file);
            int line = 1;
            int index = 0;
            bool inLineComment = false;
            bool inBlockComment = false;
            bool inVerbatimString = false;
            int rawStringQuoteCount = 0;

            while (index < text.Length)
            {
                if (inLineComment)
                {
                    int start = index;
                    while (index < text.Length && text[index] != '\n')
                    {
                        index++;
                    }

                    yield return (line, text[start..index]);
                    inLineComment = false;
                    continue;
                }

                if (inBlockComment)
                {
                    int start = index;
                    while (index < text.Length - 1 && text[index..(index + 2)] != "*/")
                    {
                        if (text[index] == '\n')
                        {
                            line++;
                        }

                        index++;
                    }

                    int end = index;
                    if (index < text.Length - 1)
                    {
                        index += 2;
                    }

                    yield return (line, text[start..end]);
                    inBlockComment = false;
                    continue;
                }

                if (inVerbatimString)
                {
                    index = SkipVerbatimStringContent(text, index, ref line);
                    inVerbatimString = false;
                    continue;
                }

                if (rawStringQuoteCount > 0)
                {
                    index = SkipRawStringContent(text, index, rawStringQuoteCount, ref line);
                    rawStringQuoteCount = 0;
                    continue;
                }

                if (index + 1 < text.Length && text[index] == '/' && text[index + 1] == '/')
                {
                    inLineComment = true;
                    index += 2;
                    continue;
                }

                if (index + 1 < text.Length && text[index] == '/' && text[index + 1] == '*')
                {
                    inBlockComment = true;
                    index += 2;
                    continue;
                }

                if (text[index] == '"')
                {
                    int quoteCount = CountQuoteRunInText(text, index);
                    if (quoteCount >= 3)
                    {
                        int sameLineEnd = FindQuoteRunInText(text, index + quoteCount, quoteCount);
                        if (sameLineEnd < 0)
                        {
                            rawStringQuoteCount = quoteCount;
                            index += quoteCount;
                            continue;
                        }

                        index = sameLineEnd + quoteCount;
                        continue;
                    }

                    if (IsInterpolatedStringStartInText(text, index))
                    {
                        index = IsVerbatimStringStartInText(text, index)
                            ? SkipInterpolatedVerbatimStringContent(text, index + 1, ref line)
                            : SkipInterpolatedRegularStringContent(text, index + 1, ref line);
                        continue;
                    }

                    if (IsVerbatimStringStartInText(text, index))
                    {
                        int end = FindVerbatimStringEndInText(text, index + 1);
                        if (end < 0)
                        {
                            inVerbatimString = true;
                            index += 1;
                            continue;
                        }

                        index = end + 1;
                        continue;
                    }

                    int regularEnd = FindRegularStringEndInText(text, index + 1);
                    index = regularEnd < 0 ? text.Length : regularEnd + 1;
                    continue;
                }

                if (text[index] == '\'')
                {
                    int charEnd = FindCharLiteralEndInText(text, index + 1);
                    index = charEnd < 0 ? text.Length : charEnd + 1;
                    continue;
                }

                if (text[index] == '\n')
                {
                    line++;
                }

                index++;
            }
        }

        private static int SkipVerbatimStringContent(string text, int start, ref int line)
        {
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }

                if (text[i] != '"')
                {
                    continue;
                }

                if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                return i + 1;
            }

            return text.Length;
        }

        private static int SkipRawStringContent(string text, int start, int quoteCount, ref int line)
        {
            int end = FindQuoteRunInText(text, start, quoteCount);
            if (end < 0)
            {
                for (int i = start; i < text.Length; i++)
                {
                    if (text[i] == '\n')
                    {
                        line++;
                    }
                }

                return text.Length;
            }

            for (int i = start; i < end; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return end + quoteCount;
        }

        private static int SkipInterpolatedRegularStringContent(string text, int start, ref int line)
        {
            int index = start;
            bool escaped = false;
            while (index < text.Length)
            {
                if (escaped)
                {
                    escaped = false;
                    index++;
                    continue;
                }

                char ch = text[index];
                if (ch == '\\')
                {
                    escaped = true;
                    index++;
                    continue;
                }

                if (ch == '"')
                {
                    return index + 1;
                }

                if (ch == '{')
                {
                    if (index + 1 < text.Length && text[index + 1] == '{')
                    {
                        index += 2;
                        continue;
                    }

                    index = SkipInterpolatedExpression(text, index + 1, ref line);
                    continue;
                }

                if (ch == '\n')
                {
                    line++;
                }

                index++;
            }

            return text.Length;
        }

        private static int SkipInterpolatedVerbatimStringContent(string text, int start, ref int line)
        {
            int index = start;
            while (index < text.Length)
            {
                char ch = text[index];
                if (ch == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        index += 2;
                        continue;
                    }

                    return index + 1;
                }

                if (ch == '{')
                {
                    if (index + 1 < text.Length && text[index + 1] == '{')
                    {
                        index += 2;
                        continue;
                    }

                    index = SkipInterpolatedExpression(text, index + 1, ref line);
                    continue;
                }

                if (ch == '\n')
                {
                    line++;
                }

                index++;
            }

            return text.Length;
        }

        private static int SkipInterpolatedExpression(string text, int start, ref int line)
        {
            int depth = 0;
            int index = start;
            while (index < text.Length)
            {
                if (index + 1 < text.Length && text[index] == '/' && text[index + 1] == '/')
                {
                    while (index < text.Length && text[index] != '\n')
                    {
                        index++;
                    }

                    continue;
                }

                if (index + 1 < text.Length && text[index] == '/' && text[index + 1] == '*')
                {
                    index += 2;
                    while (index < text.Length - 1 && text[index..(index + 2)] != "*/")
                    {
                        if (text[index] == '\n')
                        {
                            line++;
                        }

                        index++;
                    }

                    index += 2;
                    continue;
                }

                if (text[index] == '"')
                {
                    int quoteCount = CountQuoteRunInText(text, index);
                    if (quoteCount >= 3)
                    {
                        index = SkipRawStringContent(text, index + quoteCount, quoteCount, ref line);
                        continue;
                    }

                    int end = IsVerbatimStringStartInText(text, index)
                        ? FindVerbatimStringEndInText(text, index + 1)
                        : FindRegularStringEndInText(text, index + 1);
                    index = end < 0 ? text.Length : end + 1;
                    continue;
                }

                if (text[index] == '\'')
                {
                    int charEnd = FindCharLiteralEndInText(text, index + 1);
                    index = charEnd < 0 ? text.Length : charEnd + 1;
                    continue;
                }

                if (text[index] == '{')
                {
                    depth++;
                    index++;
                    continue;
                }

                if (text[index] == '}')
                {
                    if (depth == 0)
                    {
                        return index + 1;
                    }

                    depth--;
                    index++;
                    continue;
                }

                if (text[index] == '\n')
                {
                    line++;
                }

                index++;
            }

            return text.Length;
        }

        private static bool IsInterpolatedStringStartInText(string text, int quoteIndex)
        {
            return quoteIndex > 0 &&
                   (text[quoteIndex - 1] == '$' ||
                    (quoteIndex > 1 && text[quoteIndex - 2] == '$' && text[quoteIndex - 1] == '@') ||
                    (quoteIndex > 1 && text[quoteIndex - 2] == '@' && text[quoteIndex - 1] == '$'));
        }

        private static bool IsVerbatimStringStartInText(string text, int quoteIndex)
        {
            return quoteIndex > 0 &&
                   (text[quoteIndex - 1] == '@' ||
                    (quoteIndex > 1 && text[quoteIndex - 2] == '@' && text[quoteIndex - 1] == '$'));
        }

        private static int FindVerbatimStringEndInText(string text, int start)
        {
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] != '"')
                {
                    continue;
                }

                if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                return i;
            }

            return -1;
        }

        private static int FindRegularStringEndInText(string text, int start)
        {
            bool escaped = false;
            for (int i = start; i < text.Length; i++)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (text[i] == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (text[i] == '"')
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindCharLiteralEndInText(string text, int start)
        {
            bool escaped = false;
            for (int i = start; i < text.Length; i++)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (text[i] == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (text[i] == '\'')
                {
                    return i;
                }
            }

            return -1;
        }

        private static int CountQuoteRunInText(string text, int start)
        {
            int count = 0;
            while (start + count < text.Length && text[start + count] == '"')
            {
                count++;
            }

            return count;
        }

        private static int FindQuoteRunInText(string text, int start, int quoteCount)
        {
            for (int i = start; i <= text.Length - quoteCount; i++)
            {
                if (CountQuoteRunInText(text, i) >= quoteCount)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
