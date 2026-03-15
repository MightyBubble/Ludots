using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;

namespace UiShowcaseCoreMod.Showcase;

internal static class UiSkinShowcaseSceneFactory
{
    internal static UiScene CreateScene(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
    {
        return BuildSkinScene(textMeasurer, imageSizeProvider, UiSkinThemes.Classic, includeSwitcher: true);
    }

    internal static UiScene CreateFixtureScene(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider, UiThemePack theme)
    {
        return BuildSkinScene(textMeasurer, imageSizeProvider, theme, includeSwitcher: false);
    }

    private static UiScene BuildSkinScene(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider, UiThemePack theme, bool includeSwitcher)
    {
        UiStyleSheet baseSheet = new UiStyleSheet()
            .AddRule(".skin-toolbar", style =>
            {
                style.Set("display", "flex");
                style.Set("flex-direction", "row");
                style.Set("gap", "12px");
            })
            .AddRule(".skin-caption", style =>
            {
                style.Set("font-size", "15px");
            })
            .AddRule(".skin-token", style =>
            {
                style.Set("font-size", "13px");
                style.Set("background-color", "rgba(0,0,0,0.12)");
                style.Set("padding", "8px 10px");
                style.Set("border-radius", "8px");
            });

        UiScene initial = UiSceneComposer.Compose(textMeasurer, imageSizeProvider, BuildFixture(string.Empty, includeSwitcher), theme, baseSheet);
        string domHash = UiDomHasher.Hash(initial);
        return UiSceneComposer.Compose(textMeasurer, imageSizeProvider, BuildFixture(domHash, includeSwitcher), theme, baseSheet);
    }

    private static UiElementBuilder BuildFixture(string domHash, bool includeSwitcher)
    {
        return Ui.Column(
                Ui.Text(includeSwitcher ? "Skin Swap Showcase" : "Stable DOM Fixture").FontSize(30).Bold().Class("skin-header"),
                Ui.Card(
                    Ui.Text("DOM fixture keeps semantic structure stable; skin mod only changes theme and assets.").Id("skin-description").FontSize(18),
                    Ui.Text($"DOM Hash: {domHash}").Id("skin-hash").Class("skin-caption"),
                    Ui.Row(
                        Ui.Button("Confirm").Class("skin-primary").Id("skin-confirm"),
                        Ui.Button("Cancel").Id("skin-cancel"),
                        Ui.Button("Open Drawer").Id("skin-drawer"))
                        .Class("skin-toolbar"),
                    Ui.Row(
                        new UiElementBuilder(UiNodeKind.Input, "input").Text("username@example.com").Class("skin-token").FlexGrow(1),
                        new UiElementBuilder(UiNodeKind.Select, "select").Text("Sci-Fi HUD").Class("skin-token").FlexGrow(1))
                        .Class("skin-toolbar"),
                    Ui.Row(
                        new UiElementBuilder(UiNodeKind.Custom, "div").Text("Modal").Class("skin-token"),
                        new UiElementBuilder(UiNodeKind.Custom, "div").Text("Tooltip").Class("skin-token"),
                        new UiElementBuilder(UiNodeKind.Custom, "div").Text("Toast").Class("skin-token"))
                        .Class("skin-toolbar"))
                    .Id("skin-fixture-card")
                    .Class("skin-card"),
                includeSwitcher ? BuildSwitchCard() : Ui.Text("Fixture mod intentionally omits runtime switching.").Class("skin-caption"))
            .Class("skin-root")
            .Width(1280)
            .Height(720)
            .Gap(16);
    }

    private static UiElementBuilder BuildSwitchCard()
    {
        return Ui.Card(
                Ui.Text("Runtime skin switch").FontSize(18).Bold(),
                Ui.Row(
                    Ui.Button("Classic", ctx => ctx.Scene.SetTheme(UiSkinThemes.Classic)).Id("skin-theme-classic").Class("skin-primary"),
                    Ui.Button("Sci-Fi", ctx => ctx.Scene.SetTheme(UiSkinThemes.SciFiHud)).Id("skin-theme-scifi"),
                    Ui.Button("Paper", ctx => ctx.Scene.SetTheme(UiSkinThemes.Paper)).Id("skin-theme-paper"))
                    .Class("skin-toolbar"),
                Ui.Text("结构 hash 不变，computed style 明显变化。").Class("skin-caption"))
            .Class("skin-card");
    }
}

