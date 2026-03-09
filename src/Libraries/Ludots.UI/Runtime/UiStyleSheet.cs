namespace Ludots.UI.Runtime;

public sealed class UiStyleSheet
{
    private readonly List<UiStyleRule> _rules = new();
    private readonly Dictionary<string, UiKeyframeDefinition> _keyframes = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<UiStyleRule> Rules => _rules;

    public IReadOnlyCollection<UiKeyframeDefinition> Keyframes => _keyframes.Values;

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

    public UiStyleSheet AddKeyframes(UiKeyframeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _keyframes[definition.Name] = definition;
        return this;
    }

    public bool TryGetKeyframes(string name, out UiKeyframeDefinition? definition)
    {
        return _keyframes.TryGetValue(name, out definition);
    }
}
