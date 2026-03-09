using System.Linq;
using ExCSS;
using Ludots.UI.Runtime;

namespace Ludots.UI.HtmlEngine.Markup;

public static class UiCssParser
{
    public static UiStyleSheet ParseStyleSheet(string css)
    {
        if (string.IsNullOrWhiteSpace(css))
        {
            return new UiStyleSheet();
        }

        StylesheetParser parser = new();
        Stylesheet stylesheet = parser.Parse(css);
        UiStyleSheet result = new();
        List<(string SelectorText, string DeclarationText)> rawRules = ParseRawRules(css).ToList();

        foreach (UiKeyframeDefinition definition in ParseRawKeyframes(css))
        {
            result.AddKeyframes(definition);
        }

        Dictionary<string, Queue<IStyleRule>> styleRulesBySelector = stylesheet.StyleRules
            .GroupBy(static rule => NormalizeSelector(rule.SelectorText), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => new Queue<IStyleRule>(group),
                StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < rawRules.Count; i++)
        {
            (string selectorText, string declarationText) = rawRules[i];
            UiStyleDeclaration declaration = new();
            if (styleRulesBySelector.TryGetValue(NormalizeSelector(selectorText), out Queue<IStyleRule>? rules) && rules.Count > 0)
            {
                IStyleRule rule = rules.Dequeue();
                foreach (Property property in rule.Style)
                {
                    declaration.Set(property.Name, property.Value);
                }
            }

            declaration.Merge(ParseInline(declarationText));

            foreach (UiSelector selector in UiSelectorParser.ParseMany(selectorText))
            {
                result.AddRule(selector, declaration);
            }
        }

        foreach ((string selectorText, Queue<IStyleRule> rules) in styleRulesBySelector)
        {
            while (rules.Count > 0)
            {
                IStyleRule rule = rules.Dequeue();
                UiStyleDeclaration declaration = new();
                foreach (Property property in rule.Style)
                {
                    declaration.Set(property.Name, property.Value);
                }

                foreach (UiSelector selector in UiSelectorParser.ParseMany(rule.SelectorText))
                {
                    result.AddRule(selector, declaration);
                }
            }
        }

        return result;
    }

    public static UiStyleDeclaration ParseInline(string inlineCss)
    {
        UiStyleDeclaration declaration = new();
        if (string.IsNullOrWhiteSpace(inlineCss))
        {
            return declaration;
        }

        foreach (string property in SplitInlineDeclarations(inlineCss))
        {
            if (TrySplitDeclaration(property, out string name, out string value))
            {
                declaration.Set(name, value);
            }
        }

        return declaration;
    }

    private static IEnumerable<string> SplitInlineDeclarations(string inlineCss)
    {
        int depth = 0;
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        int segmentStart = 0;

        for (int i = 0; i < inlineCss.Length; i++)
        {
            char current = inlineCss[i];
            char previous = i > 0 ? inlineCss[i - 1] : '\0';

            if (current == '\'' && !inDoubleQuote && previous != '\\')
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (current == '"' && !inSingleQuote && previous != '\\')
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
            {
                continue;
            }

            if (current == '(')
            {
                depth++;
                continue;
            }

            if (current == ')' && depth > 0)
            {
                depth--;
                continue;
            }

            if (current == ';' && depth == 0)
            {
                string declaration = inlineCss[segmentStart..i].Trim();
                if (!string.IsNullOrWhiteSpace(declaration))
                {
                    yield return declaration;
                }

                segmentStart = i + 1;
            }
        }

        string tail = inlineCss[segmentStart..].Trim();
        if (!string.IsNullOrWhiteSpace(tail))
        {
            yield return tail;
        }
    }

    private static bool TrySplitDeclaration(string declaration, out string name, out string value)
    {
        int depth = 0;
        bool inSingleQuote = false;
        bool inDoubleQuote = false;

        for (int i = 0; i < declaration.Length; i++)
        {
            char current = declaration[i];
            char previous = i > 0 ? declaration[i - 1] : '\0';

            if (current == '\'' && !inDoubleQuote && previous != '\\')
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (current == '"' && !inSingleQuote && previous != '\\')
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
            {
                continue;
            }

            if (current == '(')
            {
                depth++;
                continue;
            }

            if (current == ')' && depth > 0)
            {
                depth--;
                continue;
            }

            if (current == ':' && depth == 0)
            {
                name = declaration[..i].Trim();
                value = declaration[(i + 1)..].Trim();
                return !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value);
            }
        }

        name = string.Empty;
        value = string.Empty;
        return false;
    }

    private static IEnumerable<(string SelectorText, string DeclarationText)> ParseRawRules(string css)
    {
        string content = StripComments(css);
        int index = 0;
        while (index < content.Length)
        {
            int openBrace = content.IndexOf('{', index);
            if (openBrace < 0)
            {
                yield break;
            }

            string selectorText = content[index..openBrace].Trim();
            int closeBrace = FindMatchingBrace(content, openBrace + 1);
            if (closeBrace < 0)
            {
                yield break;
            }

            string declarationText = content[(openBrace + 1)..closeBrace].Trim();
            if (!string.IsNullOrWhiteSpace(selectorText)
                && !selectorText.StartsWith("@", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(declarationText))
            {
                yield return (selectorText, declarationText);
            }

            index = closeBrace + 1;
        }
    }

    private static IEnumerable<UiKeyframeDefinition> ParseRawKeyframes(string css)
    {
        string content = StripComments(css);
        int index = 0;
        while (index < content.Length)
        {
            int atRuleIndex = content.IndexOf("@keyframes", index, StringComparison.OrdinalIgnoreCase);
            if (atRuleIndex < 0)
            {
                yield break;
            }

            int nameStart = atRuleIndex + "@keyframes".Length;
            int openBrace = content.IndexOf('{', nameStart);
            if (openBrace < 0)
            {
                yield break;
            }

            string name = content[nameStart..openBrace].Trim();
            int closeBrace = FindMatchingBrace(content, openBrace + 1);
            if (closeBrace < 0)
            {
                yield break;
            }

            UiKeyframeDefinition? definition = ParseRawKeyframeDefinition(name, content[(openBrace + 1)..closeBrace]);
            if (definition != null)
            {
                yield return definition;
            }

            index = closeBrace + 1;
        }
    }

    private static UiKeyframeDefinition? ParseRawKeyframeDefinition(string name, string body)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        List<UiKeyframeStop> stops = new();
        int index = 0;
        while (index < body.Length)
        {
            int openBrace = body.IndexOf('{', index);
            if (openBrace < 0)
            {
                break;
            }

            string selectorText = body[index..openBrace].Trim();
            int closeBrace = FindMatchingBrace(body, openBrace + 1);
            if (closeBrace < 0)
            {
                break;
            }

            string declarationText = body[(openBrace + 1)..closeBrace].Trim();
            UiStyleDeclaration declaration = ParseInline(declarationText);
            foreach (string selectorPart in selectorText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (TryParseKeyframeOffset(selectorPart, out float offset))
                {
                    stops.Add(new UiKeyframeStop(offset, declaration));
                }
            }

            index = closeBrace + 1;
        }

        return stops.Count == 0 ? null : new UiKeyframeDefinition(name, stops);
    }

    private static bool TryParseKeyframeOffset(string selectorText, out float offset)
    {
        string value = selectorText.Trim();
        if (value.Equals("from", StringComparison.OrdinalIgnoreCase))
        {
            offset = 0f;
            return true;
        }

        if (value.Equals("to", StringComparison.OrdinalIgnoreCase))
        {
            offset = 1f;
            return true;
        }

        if (value.EndsWith('%') && float.TryParse(value[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float percent))
        {
            offset = Math.Clamp(percent / 100f, 0f, 1f);
            return true;
        }

        offset = 0f;
        return false;
    }

    private static int FindMatchingBrace(string content, int startIndex)
    {
        int depth = 1;
        for (int i = startIndex; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                depth++;
            }
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static string StripComments(string css)
    {
        return string.IsNullOrWhiteSpace(css)
            ? string.Empty
            : System.Text.RegularExpressions.Regex.Replace(css, @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
    }

    private static string NormalizeSelector(string selectorText)
    {
        return selectorText.Trim();
    }
}
