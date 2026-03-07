namespace Ludots.UI.Runtime;

public sealed class UiSelector
{
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
            if (part.PseudoState != UiPseudoState.None)
            {
                classCount++;
            }

            if (!string.IsNullOrWhiteSpace(part.TagName) && part.TagName != "*")
            {
                tagCount++;
            }
        }

        return (idCount * 100) + (classCount * 10) + tagCount;
    }

    public override string ToString()
    {
        return string.Join(' ', Parts.Select(FormatPart));
    }

    private static string FormatPart(UiSelectorPart part)
    {
        string prefix = part.Combinator == UiSelectorCombinator.Child ? "> " : string.Empty;
        string classes = part.Classes.Count == 0 ? string.Empty : string.Concat(part.Classes.Select(value => $".{value}"));
        string attributes = part.Attributes.Count == 0
            ? string.Empty
            : string.Concat(part.Attributes.Select(attribute => attribute.Value == null ? $"[{attribute.Name}]" : $"[{attribute.Name}={attribute.Value}]"));
        string id = string.IsNullOrWhiteSpace(part.Id) ? string.Empty : $"#{part.Id}";
        string pseudo = part.PseudoState == UiPseudoState.None ? string.Empty : $":{part.PseudoState.ToString().ToLowerInvariant()}";
        return $"{prefix}{part.TagName ?? "*"}{id}{classes}{attributes}{pseudo}";
    }
}
