namespace Ludots.UI.Runtime;

public sealed class UiThemePack
{
    public UiThemePack(string key, params UiStyleSheet[] styleSheets)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Theme key is required.", nameof(key));
        }

        Key = key;
        StyleSheets = styleSheets?.Length > 0 ? styleSheets : Array.Empty<UiStyleSheet>();
    }

    public string Key { get; }

    public IReadOnlyList<UiStyleSheet> StyleSheets { get; }
}
