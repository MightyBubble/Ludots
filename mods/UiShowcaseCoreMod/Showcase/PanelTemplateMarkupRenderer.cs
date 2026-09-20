using System.Globalization;
using System.Text.RegularExpressions;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;

namespace UiShowcaseCoreMod.Showcase;

/// <summary>
/// Markup surface adapter (#1010 MVP / #1011): renders a panel template's HTML with
/// {variable} tokens substituted from the evaluated <see cref="PanelVariableSet"/>.
/// The adapter only consumes variables; it never fetches graph/attribute data.
/// </summary>
public static class PanelTemplateMarkupRenderer
{
    private static readonly Regex TokenPattern = new(@"\{([A-Za-z0-9_.]+)\}", RegexOptions.Compiled);

    public static UiScene Render(
        string html,
        string css,
        PanelVariableSet variables,
        IUiTextMeasurer textMeasurer,
        IUiImageSizeProvider imageSizeProvider)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            throw new System.ArgumentException("Panel markup HTML is required.", nameof(html));
        }

        ArgumentNullException.ThrowIfNull(variables);

        string resolved = TokenPattern.Replace(html, match =>
        {
            string name = match.Groups[1].Value;
            if (!variables.TryGet(name, out float value))
            {
                throw new System.InvalidOperationException(
                    $"Panel '{variables.TemplateId}' markup references unknown variable '{name}'.");
            }

            return value.ToString("0.##", CultureInfo.InvariantCulture);
        });

        return new UiMarkupLoader().LoadScene(textMeasurer, imageSizeProvider, resolved, css);
    }
}
