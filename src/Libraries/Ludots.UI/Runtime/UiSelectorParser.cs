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

        List<string> selectorParts = SplitTopLevel(selectorText, ',');
        List<UiSelector> selectors = new(selectorParts.Count);
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
            bool consumedWhitespace = false;
            while (index < selectorText.Length && char.IsWhiteSpace(selectorText[index]))
            {
                consumedWhitespace = true;
                index++;
            }

            if (consumedWhitespace && parts.Count > 0 && combinator == UiSelectorCombinator.None)
            {
                combinator = UiSelectorCombinator.Descendant;
            }

            if (index >= selectorText.Length)
            {
                break;
            }

            combinator = selectorText[index] switch
            {
                '>' => UiSelectorCombinator.Child,
                '+' => UiSelectorCombinator.AdjacentSibling,
                '~' => UiSelectorCombinator.GeneralSibling,
                _ => combinator
            };

            if (selectorText[index] is '>' or '+' or '~')
            {
                index++;
                continue;
            }

            int start = index;
            int bracketDepth = 0;
            int parenthesisDepth = 0;
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
                else if (current == '(')
                {
                    parenthesisDepth++;
                }
                else if (current == ')')
                {
                    parenthesisDepth = Math.Max(0, parenthesisDepth - 1);
                }
                else if (bracketDepth == 0 && parenthesisDepth == 0 && (char.IsWhiteSpace(current) || current is '>' or '+' or '~'))
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

            UiSelectorCombinator resolvedCombinator = parts.Count == 0
                ? UiSelectorCombinator.None
                : combinator == UiSelectorCombinator.None ? UiSelectorCombinator.Descendant : combinator;
            parts.Add(ParseToken(token, resolvedCombinator));
            combinator = UiSelectorCombinator.None;
        }

        return new UiSelector(parts);
    }

    private static UiSelectorPart ParseToken(string token, UiSelectorCombinator combinator)
    {
        string? tagName = null;
        string? id = null;
        List<string> classes = new();
        List<UiSelectorAttribute> attributes = new();
        List<UiStructuralPseudo> structuralPseudos = new();
        List<UiSelectorLogicalPseudo> logicalPseudos = new();
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
                if (index < token.Length && token[index] == ':')
                {
                    index++;
                }

                ApplyPseudo(ReadPseudoToken(token, ref index), ref pseudoState, structuralPseudos, logicalPseudos);
                continue;
            }

            if (current == '[')
            {
                index++;
                int start = index;
                int depth = 1;
                while (index < token.Length && depth > 0)
                {
                    if (token[index] == '[')
                    {
                        depth++;
                    }
                    else if (token[index] == ']')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            break;
                        }
                    }

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
        return new UiSelectorPart(tagName, id, classes, attributes, structuralPseudos, logicalPseudos, pseudoState, combinator);
    }

    private static string ReadIdentifier(string token, ref int index)
    {
        StringBuilder builder = new();
        while (index < token.Length)
        {
            char current = token[index];
            if (current is '#' or '.' or ':' or '[' or ']' or '(' or ')')
            {
                break;
            }

            builder.Append(current);
            index++;
        }

        return builder.ToString().Trim();
    }

    private static string ReadPseudoToken(string token, ref int index)
    {
        int start = index;
        while (index < token.Length)
        {
            char current = token[index];
            if (current is '#' or '.' or ':' or '[' or ']')
            {
                break;
            }

            if (current == '(')
            {
                int depth = 1;
                index++;
                while (index < token.Length && depth > 0)
                {
                    if (token[index] == '(')
                    {
                        depth++;
                    }
                    else if (token[index] == ')')
                    {
                        depth--;
                    }

                    index++;
                }

                break;
            }

            index++;
        }

        return token[start..Math.Min(index, token.Length)].Trim();
    }

    private static UiSelectorAttribute ParseAttribute(string expression)
    {
        string[] operators = ["~=", "|=", "^=", "$=", "*=", "="];
        foreach (string operatorToken in operators)
        {
            int operatorIndex = expression.IndexOf(operatorToken, StringComparison.Ordinal);
            if (operatorIndex < 0)
            {
                continue;
            }

            string name = expression[..operatorIndex].Trim();
            string value = expression[(operatorIndex + operatorToken.Length)..].Trim().Trim('"', '\'');
            return new UiSelectorAttribute(name, value, ParseAttributeOperator(operatorToken));
        }

        return new UiSelectorAttribute(expression.Trim(), null, UiSelectorAttributeOperator.Exists);
    }

    private static UiSelectorAttributeOperator ParseAttributeOperator(string operatorToken)
    {
        return operatorToken switch
        {
            "=" => UiSelectorAttributeOperator.Equals,
            "~=" => UiSelectorAttributeOperator.Includes,
            "|=" => UiSelectorAttributeOperator.DashMatch,
            "^=" => UiSelectorAttributeOperator.Prefix,
            "$=" => UiSelectorAttributeOperator.Suffix,
            "*=" => UiSelectorAttributeOperator.Substring,
            _ => UiSelectorAttributeOperator.Exists
        };
    }

    private static void ApplyPseudo(
        string value,
        ref UiPseudoState pseudoState,
        ICollection<UiStructuralPseudo> structuralPseudos,
        ICollection<UiSelectorLogicalPseudo> logicalPseudos)
    {
        string normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "hover":
                pseudoState |= UiPseudoState.Hover;
                return;
            case "active":
                pseudoState |= UiPseudoState.Active;
                return;
            case "focus":
                pseudoState |= UiPseudoState.Focus;
                return;
            case "disabled":
                pseudoState |= UiPseudoState.Disabled;
                return;
            case "checked":
                pseudoState |= UiPseudoState.Checked;
                return;
            case "selected":
                pseudoState |= UiPseudoState.Selected;
                return;
            case "required":
                pseudoState |= UiPseudoState.Required;
                return;
            case "invalid":
                pseudoState |= UiPseudoState.Invalid;
                return;
            case "root":
                pseudoState |= UiPseudoState.Root;
                return;
            case "first-child":
                structuralPseudos.Add(new UiStructuralPseudo(UiStructuralPseudoKind.FirstChild));
                return;
            case "last-child":
                structuralPseudos.Add(new UiStructuralPseudo(UiStructuralPseudoKind.LastChild));
                return;
        }

        if (normalized.StartsWith("nth-child(", StringComparison.Ordinal) && normalized.EndsWith(')'))
        {
            string expression = normalized["nth-child(".Length..^1].Trim();
            structuralPseudos.Add(new UiStructuralPseudo(UiStructuralPseudoKind.NthChild, expression));
            return;
        }

        if (normalized.StartsWith("nth-last-child(", StringComparison.Ordinal) && normalized.EndsWith(')'))
        {
            string expression = normalized["nth-last-child(".Length..^1].Trim();
            structuralPseudos.Add(new UiStructuralPseudo(UiStructuralPseudoKind.NthLastChild, expression));
            return;
        }

        if (TryParseLogicalPseudo(normalized, "not", UiSelectorLogicalPseudoKind.Not, out UiSelectorLogicalPseudo? logicalPseudo)
            || TryParseLogicalPseudo(normalized, "is", UiSelectorLogicalPseudoKind.Is, out logicalPseudo)
            || TryParseLogicalPseudo(normalized, "where", UiSelectorLogicalPseudoKind.Where, out logicalPseudo))
        {
            logicalPseudos.Add(logicalPseudo);
        }
    }

    private static bool TryParseLogicalPseudo(string value, string name, UiSelectorLogicalPseudoKind kind, out UiSelectorLogicalPseudo? pseudo)
    {
        string prefix = name + '(';
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || !value.EndsWith(')'))
        {
            pseudo = null;
            return false;
        }

        string selectorText = value[prefix.Length..^1].Trim();
        IReadOnlyList<UiSelector> selectors = ParseMany(selectorText);
        pseudo = new UiSelectorLogicalPseudo(kind, selectors);
        return true;
    }

    private static List<string> SplitTopLevel(string text, char separator)
    {
        List<string> items = new();
        int bracketDepth = 0;
        int parenthesisDepth = 0;
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];
            if (current == '[')
            {
                bracketDepth++;
            }
            else if (current == ']')
            {
                bracketDepth--;
            }
            else if (current == '(')
            {
                parenthesisDepth++;
            }
            else if (current == ')')
            {
                parenthesisDepth--;
            }
            else if (current == separator && bracketDepth == 0 && parenthesisDepth == 0)
            {
                string value = text[start..i].Trim();
                if (value.Length > 0)
                {
                    items.Add(value);
                }

                start = i + 1;
            }
        }

        string tail = text[start..].Trim();
        if (tail.Length > 0)
        {
            items.Add(tail);
        }

        return items;
    }
}
