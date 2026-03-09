using SkiaSharp;
using Ludots.UI.Runtime;

namespace UiShowcaseCoreMod.Showcase;

public static class UiSkinThemes
{
    public static UiThemePack Classic { get; } = new("classic", BuildClassic());

    public static UiThemePack SciFiHud { get; } = new("scifi-hud", BuildSciFi());

    public static UiThemePack Paper { get; } = new("paper", BuildPaper());

    private static UiStyleSheet BuildClassic()
    {
        return new UiStyleSheet()
            .AddRule(".skin-root", style =>
            {
                style.Set("background-color", "#1d2433");
                style.Set("color", "#f8fbff");
                style.Set("padding", "24px");
                style.Set("gap", "16px");
            })
            .AddRule(".skin-header", style =>
            {
                style.Set("font-size", "28px");
                style.Set("font-weight", "700");
                style.Set("color", "#ffffff");
            })
            .AddRule(".skin-card", style =>
            {
                style.Set("background-color", "#283349");
                style.Set("color", "#f8fbff");
                style.Set("border-radius", "14px");
                style.Set("padding", "16px");
                style.Set("gap", "12px");
            })
            .AddRule(".skin-primary", style =>
            {
                style.Set("background-color", "#3c82ff");
                style.Set("color", "#ffffff");
            });
    }

    private static UiStyleSheet BuildSciFi()
    {
        return new UiStyleSheet()
            .AddRule(".skin-root", style =>
            {
                style.Set("background-color", "#08131f");
                style.Set("color", "#d7fdff");
                style.Set("padding", "24px");
                style.Set("gap", "16px");
            })
            .AddRule(".skin-header", style =>
            {
                style.Set("font-size", "30px");
                style.Set("font-weight", "700");
                style.Set("color", "#6ff9ff");
            })
            .AddRule(".skin-card", style =>
            {
                style.Set("background-color", "rgba(8, 34, 52, 0.92)");
                style.Set("color", "#d7fdff");
                style.Set("border-color", "#38f0ff");
                style.Set("border-width", "1px");
                style.Set("border-radius", "10px");
                style.Set("padding", "16px");
                style.Set("gap", "12px");
            })
            .AddRule(".skin-primary", style =>
            {
                style.Set("background-color", "#00a6ff");
                style.Set("color", "#02101d");
            });
    }

    private static UiStyleSheet BuildPaper()
    {
        return new UiStyleSheet()
            .AddRule(".skin-root", style =>
            {
                style.Set("background-color", "#f4ead7");
                style.Set("color", "#2f2417");
                style.Set("padding", "24px");
                style.Set("gap", "16px");
            })
            .AddRule(".skin-header", style =>
            {
                style.Set("font-size", "28px");
                style.Set("font-weight", "700");
                style.Set("color", "#4a3721");
            })
            .AddRule(".skin-card", style =>
            {
                style.Set("background-color", "#fff9ef");
                style.Set("color", "#2f2417");
                style.Set("border-color", "#b59c78");
                style.Set("border-width", "1px");
                style.Set("border-radius", "8px");
                style.Set("padding", "16px");
                style.Set("gap", "12px");
            })
            .AddRule(".skin-primary", style =>
            {
                style.Set("background-color", "#c69c5d");
                style.Set("color", "#2f2417");
            });
    }
}
