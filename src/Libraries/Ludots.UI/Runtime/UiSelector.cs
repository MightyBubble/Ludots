using System.Text;

namespace Ludots.UI.Runtime;

public sealed class UiSelector
{
    private const int IdWeight = 10_000;
    private const int ClassWeight = 100;

    public UiSelector(IReadOnlyList<UiSelectorPart> parts)
    {
        if (parts == null || parts.Count == 0)
        {
            throw new ArgumentException("Selector must contain at least one part.", nameof(parts));
        }

        Parts = parts;
        Specificity = CalculateSpecificity(parts);
    }

    public IReadOnlyList<UiSelectorPart> Parts { get; }

    public int Specificity { get; }

    private static int CalculateSpecificity(IReadOnlyList<UiSelectorPart> parts)
    {
        int idCount = 0;
        int classCount = 0;
        int tagCount = 0;

        foreach (UiSelectorPart part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part.Id))
            {
                idCount++;
            }

            classCount += part.Classes.Count;
            classCount += part.Attributes.Count;
            classCount += part.StructuralPseudos.Count;
            classCount += CountPseudoStateFlags(part.PseudoState);

            for (int i = 0; i < part.LogicalPseudos.Count; i++)
            {
                int specificity = part.LogicalPseudos[i].Specificity;
                idCount += specificity / IdWeight;
                specificity %= IdWeight;
                classCount += specificity / ClassWeight;
                tagCount += specificity % ClassWeight;
            }

            if (!string.IsNullOrWhiteSpace(part.TagName) && part.TagName != "*")
            {
                tagCount++;
            }
        }

        return (idCount * IdWeight) + (classCount * ClassWeight) + tagCount;
    }

    public override string ToString()
    {
        StringBuilder builder = new();
        for (int i = 0; i < Parts.Count; i++)
        {
            UiSelectorPart part = Parts[i];
            if (i > 0)
            {
                builder.Append(FormatCombinator(part.Combinator));
            }

            builder.Append(FormatPart(part));
        }

        return builder.ToString();
    }

    private static string FormatPart(UiSelectorPart part)
    {
        string classes = part.Classes.Count == 0 ? string.Empty : string.Concat(part.Classes.Select(value => $".{value}"));
        string attributes = part.Attributes.Count == 0 ? string.Empty : string.Concat(part.Attributes.Select(FormatAttribute));
        string id = string.IsNullOrWhiteSpace(part.Id) ? string.Empty : $"#{part.Id}";
        string structuralPseudo = part.StructuralPseudos.Count == 0 ? string.Empty : string.Concat(part.StructuralPseudos.Select(FormatStructuralPseudo));
        string logicalPseudo = part.LogicalPseudos.Count == 0 ? string.Empty : string.Concat(part.LogicalPseudos.Select(FormatLogicalPseudo));
        string pseudo = FormatPseudoState(part.PseudoState);
        return $"{part.TagName ?? "*"}{id}{classes}{attributes}{structuralPseudo}{logicalPseudo}{pseudo}";
    }

    private static string FormatCombinator(UiSelectorCombinator combinator)
    {
        return combinator switch
        {
            UiSelectorCombinator.Child => " > ",
            UiSelectorCombinator.AdjacentSibling => " + ",
            UiSelectorCombinator.GeneralSibling => " ~ ",
            _ => " "
        };
    }

    private static string FormatAttribute(UiSelectorAttribute attribute)
    {
        if (attribute.Operator == UiSelectorAttributeOperator.Exists || attribute.Value == null)
        {
            return $"[{attribute.Name}]";
        }

        string operatorText = attribute.Operator switch
        {
            UiSelectorAttributeOperator.Equals => "=",
            UiSelectorAttributeOperator.Includes => "~=",
            UiSelectorAttributeOperator.DashMatch => "|=",
            UiSelectorAttributeOperator.Prefix => "^=",
            UiSelectorAttributeOperator.Suffix => "$=",
            UiSelectorAttributeOperator.Substring => "*=",
            _ => string.Empty
        };

        return $"[{attribute.Name}{operatorText}{attribute.Value}]";
    }

    private static string FormatStructuralPseudo(UiStructuralPseudo pseudo)
    {
        return pseudo.Kind switch
        {
            UiStructuralPseudoKind.FirstChild => ":first-child",
            UiStructuralPseudoKind.LastChild => ":last-child",
            UiStructuralPseudoKind.NthChild when !string.IsNullOrWhiteSpace(pseudo.Expression) => $":nth-child({pseudo.Expression})",
            UiStructuralPseudoKind.NthChild => ":nth-child(1)",
            UiStructuralPseudoKind.NthLastChild when !string.IsNullOrWhiteSpace(pseudo.Expression) => $":nth-last-child({pseudo.Expression})",
            UiStructuralPseudoKind.NthLastChild => ":nth-last-child(1)",
            _ => string.Empty
        };
    }

    private static string FormatLogicalPseudo(UiSelectorLogicalPseudo pseudo)
    {
        string name = pseudo.Kind switch
        {
            UiSelectorLogicalPseudoKind.Not => "not",
            UiSelectorLogicalPseudoKind.Is => "is",
            UiSelectorLogicalPseudoKind.Where => "where",
            _ => string.Empty
        };

        return string.IsNullOrEmpty(name)
            ? string.Empty
            : $":{name}({string.Join(", ", pseudo.Selectors.Select(static selector => selector.ToString()))})";
    }

    private static string FormatPseudoState(UiPseudoState pseudoState)
    {
        if (pseudoState == UiPseudoState.None)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        AppendPseudoState(builder, pseudoState, UiPseudoState.Hover, "hover");
        AppendPseudoState(builder, pseudoState, UiPseudoState.Active, "active");
        AppendPseudoState(builder, pseudoState, UiPseudoState.Focus, "focus");
        AppendPseudoState(builder, pseudoState, UiPseudoState.Disabled, "disabled");
        AppendPseudoState(builder, pseudoState, UiPseudoState.Checked, "checked");
        AppendPseudoState(builder, pseudoState, UiPseudoState.Selected, "selected");
        AppendPseudoState(builder, pseudoState, UiPseudoState.Root, "root");
        AppendPseudoState(builder, pseudoState, UiPseudoState.Required, "required");
        AppendPseudoState(builder, pseudoState, UiPseudoState.Invalid, "invalid");
        return builder.ToString();
    }

    private static int CountPseudoStateFlags(UiPseudoState pseudoState)
    {
        int count = 0;
        count += pseudoState.HasFlag(UiPseudoState.Hover) ? 1 : 0;
        count += pseudoState.HasFlag(UiPseudoState.Active) ? 1 : 0;
        count += pseudoState.HasFlag(UiPseudoState.Focus) ? 1 : 0;
        count += pseudoState.HasFlag(UiPseudoState.Disabled) ? 1 : 0;
        count += pseudoState.HasFlag(UiPseudoState.Checked) ? 1 : 0;
        count += pseudoState.HasFlag(UiPseudoState.Selected) ? 1 : 0;
        count += pseudoState.HasFlag(UiPseudoState.Root) ? 1 : 0;
        count += pseudoState.HasFlag(UiPseudoState.Required) ? 1 : 0;
        count += pseudoState.HasFlag(UiPseudoState.Invalid) ? 1 : 0;
        return count;
    }

    private static void AppendPseudoState(StringBuilder builder, UiPseudoState pseudoState, UiPseudoState flag, string text)
    {
        if (pseudoState.HasFlag(flag))
        {
            builder.Append(':').Append(text);
        }
    }
}
