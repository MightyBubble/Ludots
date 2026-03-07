using System.Text;

namespace Ludots.UI.Runtime;

public static class UiSelectorParser
{
    public static UiSelector Parse(string selectorText)
    {
        UiSelector[] selectors = ParseMany(selectorText).ToArray();
        if (selectors.Length != 1)
        {
            throw new InvalidOperationException($"Expected a single selector, got {selectors.Length} from '{selectorText}'.");
        }

        return selectors[0];
    }

    public static IReadOnlyList<UiSelector> ParseMany(string selectorText)
    {
        if (string.IsNullOrWhiteSpace(selectorText))
        {
            throw new ArgumentException("Selector text is required.", nameof(selectorText));
        }

        string[] selectorParts = selectorText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<UiSelector> selectors = new(selectorParts.Length);
        foreach (string selectorPart in selectorParts)
        {
            selectors.Add(ParseSingle(selectorPart));
        }

        return selectors;
    }

    private static UiSelector ParseSingle(string selectorText)
    {
        List<UiSelectorPart> parts = new();
        UiSelectorCombinator combinator = UiSelectorCombinator.None;
        int index = 0;

        while (index < selectorText.Length)
        {
            while (index < selectorText.Length && char.IsWhiteSpace(selectorText[index]))
            {
                combinator = UiSelectorCombinator.Descendant;
                index++;
            }

            if (index >= selectorText.Length)
            {
                break;
            }

            if (selectorText[index] == '>')
            {
                combinator = UiSelectorCombinator.Child;
                index++;
                continue;
            }

            int start = index;
            int bracketDepth = 0;
            while (index < selectorText.Length)
            {
                char current = selectorText[index];
                if (current == '[')
                {
                    bracketDepth++;
                }
                else if (current == ']')
                {
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                }
                else if (bracketDepth == 0 && (char.IsWhiteSpace(current) || current == '>'))
                {
                    break;
                }

                index++;
            }

            string token = selectorText[start..index].Trim();
            if (token.Length == 0)
            {
                continue;
            }

            parts.Add(ParseToken(token, combinator));
            combinator = UiSelectorCombinator.Descendant;
        }

        return new UiSelector(parts);
    }

    private static UiSelectorPart ParseToken(string token, UiSelectorCombinator combinator)
    {
        string? tagName = null;
        string? id = null;
        List<string> classes = new();
        List<UiSelectorAttribute> attributes = new();
        UiPseudoState pseudoState = UiPseudoState.None;
        int index = 0;

        while (index < token.Length)
        {
            char current = token[index];
            if (current == '#')
            {
                index++;
                id = ReadIdentifier(token, ref index);
                continue;
            }

            if (current == '.')
            {
                index++;
                string className = ReadIdentifier(token, ref index);
                if (!string.IsNullOrWhiteSpace(className))
                {
                    classes.Add(className);
                }

                continue;
            }

            if (current == ':')
            {
                index++;
                pseudoState |= ParsePseudo(ReadIdentifier(token, ref index));
                continue;
            }

            if (current == '[')
            {
                index++;
                int start = index;
                while (index < token.Length && token[index] != ']')
                {
                    index++;
                }

                string expression = token[start..Math.Min(index, token.Length)].Trim();
                if (index < token.Length && token[index] == ']')
                {
                    index++;
                }

                if (!string.IsNullOrWhiteSpace(expression))
                {
                    attributes.Add(ParseAttribute(expression));
                }

                continue;
            }

            string identifier = ReadIdentifier(token, ref index);
            if (!string.IsNullOrWhiteSpace(identifier))
            {
                tagName = identifier;
            }
            else
            {
                index++;
            }
        }

        tagName ??= "*";
        return new UiSelectorPart(tagName, id, classes, attributes, pseudoState, combinator);
    }

    private static string ReadIdentifier(string token, ref int index)
    {
        StringBuilder builder = new();
        while (index < token.Length)
        {
            char current = token[index];
            if (current is '#' or '.' or ':' or '[' or ']')
            {
                break;
            }

            builder.Append(current);
            index++;
        }

        return builder.ToString().Trim();
    }

    private static UiSelectorAttribute ParseAttribute(string expression)
    {
        int equalsIndex = expression.IndexOf('=');
        if (equalsIndex < 0)
        {
            return new UiSelectorAttribute(expression.Trim(), null);
        }

        string name = expression[..equalsIndex].Trim();
        string value = expression[(equalsIndex + 1)..].Trim().Trim('"', '\'');
        return new UiSelectorAttribute(name, value);
    }

    private static UiPseudoState ParsePseudo(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "hover" => UiPseudoState.Hover,
            "active" => UiPseudoState.Active,
            "focus" => UiPseudoState.Focus,
            "disabled" => UiPseudoState.Disabled,
            "checked" => UiPseudoState.Checked,
            "selected" => UiPseudoState.Selected,
            "root" => UiPseudoState.Root,
            _ => UiPseudoState.None
        };
    }
}
