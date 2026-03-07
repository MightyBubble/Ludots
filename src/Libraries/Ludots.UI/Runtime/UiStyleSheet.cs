namespace Ludots.UI.Runtime;

public sealed class UiStyleSheet
{
    private readonly List<UiStyleRule> _rules = new();

    public IReadOnlyList<UiStyleRule> Rules => _rules;

    public UiStyleSheet AddRule(string selectorText, Action<UiStyleDeclaration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        UiStyleDeclaration declaration = new();
        configure(declaration);
        foreach (UiSelector selector in UiSelectorParser.ParseMany(selectorText))
        {
            _rules.Add(new UiStyleRule(selector, declaration, _rules.Count));
        }

        return this;
    }

    public UiStyleSheet AddRule(UiSelector selector, UiStyleDeclaration declaration)
    {
        _rules.Add(new UiStyleRule(selector, declaration, _rules.Count));
        return this;
    }
}
