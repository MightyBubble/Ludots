namespace Ludots.UI.Runtime;

public sealed class UiDocument
{
    public UiDocument(UiElement root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public string? Title { get; set; }

    public UiElement Root { get; }

    public List<UiStyleSheet> StyleSheets { get; } = new();

    public string? ThemeKey { get; set; }

    public UiElement? QuerySelector(string selectorText)
    {
        return QuerySelectorAll(selectorText).FirstOrDefault();
    }

    public IReadOnlyList<UiElement> QuerySelectorAll(string selectorText)
    {
        IReadOnlyList<UiSelector> selectors = UiSelectorParser.ParseMany(selectorText);
        List<UiElement> matches = new();
        Traverse(Root, selectors, matches);
        return matches;
    }

    public UiElement? FindById(string elementId)
    {
        return QuerySelector($"#{elementId}");
    }

    private static void Traverse(UiElement element, IReadOnlyList<UiSelector> selectors, List<UiElement> matches)
    {
        for (int i = 0; i < selectors.Count; i++)
        {
            if (UiElementSelectorMatcher.Matches(element, selectors[i]))
            {
                matches.Add(element);
                break;
            }
        }

        foreach (UiElement child in element.Children)
        {
            Traverse(child, selectors, matches);
        }
    }
}
