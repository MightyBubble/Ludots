using System.Reflection;

namespace UiShowcaseCoreMod.Showcase;

internal static class UiShowcaseAssets
{
    private static readonly Assembly ResourceAssembly = typeof(UiShowcaseAssets).Assembly;
    private static readonly string ResourceRoot = ResourceAssembly.GetName().Name + ".Assets.Showcase.";
    private static readonly Lazy<string> AuthoringCss = new(() => ReadRequiredText("showcase_authoring.css"));
    private static readonly Lazy<string> MarkupShowcaseCss = new(() => ReadRequiredText("markup_showcase.css"));
    private static readonly Lazy<string> MarkupShowcaseHtml = new(() => ReadRequiredText("markup_showcase.html"));
    private static readonly Lazy<string> ShowcaseBadgeSvg = new(() => ReadRequiredText("showcase_badge.svg"));

    internal static string GetAuthoringCss()
    {
        return AuthoringCss.Value;
    }

    internal static string GetMarkupShowcaseCss()
    {
        return MarkupShowcaseCss.Value;
    }

    internal static string GetMarkupShowcaseHtmlTemplate()
    {
        return MarkupShowcaseHtml.Value;
    }

    internal static string GetShowcaseBadgeSvg()
    {
        return ShowcaseBadgeSvg.Value;
    }

    internal static string RenderTemplate(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        string result = template;
        foreach (KeyValuePair<string, string> pair in values)
        {
            result = result.Replace("{{" + pair.Key + "}}", pair.Value, StringComparison.Ordinal);
        }

        return result;
    }

    private static string ReadRequiredText(string fileName)
    {
        string resourceName = ResourceRoot + fileName;
        using Stream? stream = ResourceAssembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Embedded showcase asset '{resourceName}' was not found.");
        }

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
