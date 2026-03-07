using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;

namespace UiShowcaseCoreMod.Showcase;

internal static class UiShowcaseScaffolding
{
    internal static readonly UiStyleSheet AuthoringStyleSheet = UiShowcaseStyles.BuildAuthoringStyleSheet();

    internal static UiElementBuilder BuildThemeToolbar(string prefix, Action<UiActionContext> toLight, Action<UiActionContext> toDark, Action<UiActionContext> toHud)
    {
        return Ui.Row(
                Ui.Button("Light", toLight).Id(prefix + "-theme-light"),
                Ui.Button("Dark", toDark).Id(prefix + "-theme-dark").Class("skin-primary"),
                Ui.Button("GameHUD", toHud).Id(prefix + "-theme-hud"))
            .Class("control-row");
    }

    internal static UiElementBuilder BuildHubCard(string id, string title, string subtitle, string body)
    {
        return Ui.Card(
                Ui.Text(title).Class("page-card-title"),
                Ui.Text(subtitle).Class("page-copy"),
                Ui.Text(body).Class("muted"))
            .Id(id)
            .Class("skin-card")
            .FlexGrow(1);
    }

    internal static UiElementBuilder BuildProgressBar(float percent)
    {
        return Ui.Panel(new UiElementBuilder(UiNodeKind.Panel, "div").Class("progress-fill").WidthPercent(percent).Height(10))
            .Class("progress-track");
    }

    internal static UiElementBuilder BuildChip(string text, bool active, string? id = null, Action<UiActionContext>? onClick = null)
    {
        UiElementBuilder builder = onClick != null
            ? Ui.Button(text, onClick)
            : new UiElementBuilder(UiNodeKind.Custom, "div").Text(text);

        builder.Class("control-chip");
        if (active)
        {
            builder.Class("active");
        }

        if (!string.IsNullOrWhiteSpace(id))
        {
            builder.Id(id);
        }

        return builder;
    }

    internal static UiElementBuilder BuildSelectableCard(string text, bool selected, string id, Action<UiActionContext> onClick)
    {
        UiElementBuilder builder = Ui.Button(text, onClick).Id(id).Class("control-chip");
        if (selected)
        {
            builder.Class("selected-item");
        }

        return builder;
    }

    internal static string ThemeLabel(string themeClass)
    {
        return themeClass switch
        {
            "theme-light" => "Light",
            "theme-hud" => "GameHUD",
            _ => "Dark"
        };
    }

    internal static string DensityLabel(string densityClass)
    {
        return densityClass switch
        {
            "density-compact" => "Compact",
            "density-comfortable" => "Comfortable",
            _ => "Cozy"
        };
    }
}
