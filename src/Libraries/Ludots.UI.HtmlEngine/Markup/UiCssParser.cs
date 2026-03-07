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
        foreach (IStyleRule rule in stylesheet.StyleRules)
        {
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

        return result;
    }

    public static UiStyleDeclaration ParseInline(string inlineCss)
    {
        UiStyleDeclaration declaration = new();
        if (string.IsNullOrWhiteSpace(inlineCss))
        {
            return declaration;
        }

        string[] properties = inlineCss.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string property in properties)
        {
            string[] parts = property.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                declaration.Set(parts[0], parts[1]);
            }
        }

        return declaration;
    }
}
