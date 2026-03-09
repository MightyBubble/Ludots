using System;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using SkiaSharp;

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
            .FlexGrow(1f)
            .FlexBasis(0f);
    }

    internal static UiElementBuilder BuildProgressBar(float percent)
    {
        return Ui.Panel(new UiElementBuilder(UiNodeKind.Panel, "div").Class("progress-fill").WidthPercent(percent).Height(10f))
            .Class("progress-track");
    }

    internal static UiElementBuilder BuildChip(string text, bool active, string? id = null, Action<UiActionContext>? onClick = null)
    {
        UiElementBuilder builder = onClick is null
            ? new UiElementBuilder(UiNodeKind.Custom, "div").Text(text)
            : Ui.Button(text, onClick);

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

    internal static UiElementBuilder BuildScrollClipRow(string prefix)
    {
        return Ui.Row(BuildScrollHost(prefix), BuildClipHost(prefix))
            .Id(prefix + "-scroll-demo-row")
            .Class("scroll-demo-row");
    }

    internal static UiElementBuilder BuildAdvancedAppearanceRow(string prefix)
    {
        return Ui.Row(
                Ui.Button("Focus Transition Probe")
                    .Id(prefix + "-transition-probe")
                    .Class("transition-probe")
                    .FlexGrow(1f)
                    .FlexBasis(0f),
                new UiElementBuilder(UiNodeKind.Panel, "div")
                    .Id(prefix + "-rtl-card")
                    .Class("appearance-mini-card")
                    .FlexGrow(1f)
                    .FlexBasis(0f)
                    .Children(
                        Ui.Text("שלום Ludots 你好").Id(prefix + "-rtl-text").Class("rtl-sample"),
                        Ui.Text("RTL / text-align / multilingual sample").Class("muted")),
                Ui.Row(
                        Ui.Image(UiShowcaseImageAssets.CoverArtDataUri).Id(prefix + "-image-cover").Class("image-sample-cover"),
                        Ui.Image(UiShowcaseImageAssets.FrameArtDataUri).Id(prefix + "-image-nine").Class("image-sample-nine"))
                    .Class("image-demo-row")
                    .FlexGrow(1f)
                    .FlexBasis(0f))
            .Id(prefix + "-appearance-row")
            .Class("page-grid-row");
    }

    internal static UiElementBuilder BuildPhaseOneRow(string prefix)
    {
        return Ui.Row(BuildSelectorLab(prefix), BuildStackHost(prefix)).Class("page-grid-row");
    }

    private static UiElementBuilder BuildSelectorLab(string prefix)
    {
        return new UiElementBuilder(UiNodeKind.Panel, "div")
            .Id(prefix + "-selector-panel")
            .Class("appearance-mini-card")
            .FlexGrow(1f)
            .FlexBasis(0f)
            .Children(
                Ui.Text("Selector Lab").Class("page-card-title"),
                Ui.Text("Sibling combinators / :not / :is / :where / nth-last-child / attribute operators.").Class("muted"),
                new UiElementBuilder(UiNodeKind.Container, "div")
                    .Id(prefix + "-selector-lab")
                    .Class("selector-lab")
                    .Children(
                        BuildSelectorPill(prefix, "alpha", "Alpha", "hero-main", "warm-ember", "featured lead", "zh-CN"),
                        BuildSelectorPill(prefix, "beta", "Beta", "support-core", "neutral-cold", "support secondary", null),
                        BuildSelectorPill(prefix, "gamma", "Gamma", "support-ops", "cobalt-cold", "secondary fallback", null),
                        BuildSelectorPill(prefix, "delta", "Delta", "utility", "warm-gold", "auxiliary", null)),
                Ui.Text("Adjacent and general siblings restyle chips after the featured entry.")
                    .Id(prefix + "-selector-note")
                    .Class("selector-note"));
    }

    private static UiElementBuilder BuildStackHost(string prefix)
    {
        return new UiElementBuilder(UiNodeKind.Container, "div")
            .Id(prefix + "-stack-host")
            .Class("stack-host")
            .FlexGrow(1f)
            .FlexBasis(0f)
            .Children(
                Ui.Text("Stack / Transform Lab").Class("page-card-title").Absolute(12f, 10f),
                Ui.Text("z-index and transformed hit area stay aligned.").Class("selector-note").Absolute(12f, 34f),
                new UiElementBuilder(UiNodeKind.Custom, "div").Id(prefix + "-stack-back").Classes("stack-card", "stack-back").Text("Back")
                    .Absolute(18f, 56f)
                    .ZIndex(1),
                new UiElementBuilder(UiNodeKind.Custom, "div").Id(prefix + "-stack-mid").Classes("stack-card", "stack-mid").Text("Mid")
                    .Absolute(82f, 48f)
                    .ZIndex(2),
                new UiElementBuilder(UiNodeKind.Custom, "div").Id(prefix + "-stack-front").Classes("stack-card", "stack-front").Text("Front")
                    .Absolute(138f, 42f)
                    .ZIndex(4)
                    .Translate(-18f, 10f)
                    .Rotate(-8f)
                    .Scale(1.04f));
    }

    private static UiElementBuilder BuildSelectorPill(string prefix, string suffix, string text, string role, string tone, string flags, string? lang)
    {
        UiElementBuilder builder = new UiElementBuilder(UiNodeKind.Custom, "div")
            .Id(prefix + "-selector-" + suffix)
            .Text(text)
            .Class("selector-pill")
            .Attribute("data-role", role)
            .Attribute("data-tone", tone)
            .Attribute("data-flags", flags);

        if (!string.IsNullOrWhiteSpace(lang))
        {
            builder.Attribute("lang", lang);
        }

        return builder;
    }

    private static UiElementBuilder BuildScrollHost(string prefix)
    {
        return Ui.ScrollView(
                BuildScrollItem(prefix, 1, "Scroll host · wheel / thumb drag", accent: true),
                BuildScrollItem(prefix, 2, "Token sync and skin swap stay intact", accent: false),
                BuildScrollItem(prefix, 3, "Native layout keeps deterministic offsets", accent: false),
                BuildScrollItem(prefix, 4, "Form section can sit inside one host", accent: false),
                BuildScrollItem(prefix, 5, "Table rows remain clipped inside bounds", accent: false),
                BuildScrollItem(prefix, 6, "Skia and Web overlay consume same diff", accent: false),
                BuildScrollItem(prefix, 7, "No JS bridge required", accent: false),
                BuildScrollItem(prefix, 8, "Pure C# runtime interaction path", accent: false))
            .Id(prefix + "-scroll-host")
            .Classes("scroll-host", "scroll-stack")
            .FlexGrow(1f)
            .FlexBasis(0f);
    }

    private static UiElementBuilder BuildClipHost(string prefix)
    {
        return new UiElementBuilder(UiNodeKind.Container, "div")
            .Id(prefix + "-clip-host")
            .Class("clip-host")
            .FlexGrow(1f)
            .FlexBasis(0f)
            .Children(
                Ui.Text("Clip host").Class("scroll-helper").Absolute(10f, 10f),
                new UiElementBuilder(UiNodeKind.Custom, "div")
                    .Id(prefix + "-clip-ribbon")
                    .Text("Overflow ribbon is clipped by native bounds")
                    .Class("clip-ribbon"),
                Ui.Text("Prototype export stays visually bounded.").Class("scroll-helper").Absolute(10f, 56f));
    }

    private static UiElementBuilder BuildScrollItem(string prefix, int index, string text, bool accent)
    {
        UiElementBuilder builder = new UiElementBuilder(UiNodeKind.Custom, "div")
            .Id($"{prefix}-scroll-item-{index}")
            .Text(text)
            .Class("scroll-item");

        if (accent)
        {
            builder.Class("scroll-item-accent");
        }

        return builder;
    }

    internal static UiElementBuilder BuildPhaseTwoPanel(string prefix)
    {
        return new UiElementBuilder(UiNodeKind.Container, "div")
            .Id(prefix + "-phase2-panel")
            .Class("phase-two-panel")
            .Children(
                new UiElementBuilder(UiNodeKind.Container, "div")
                    .Id(prefix + "-phase2-surface")
                    .Class("phase-two-layered-surface")
                    .Children(
                        Ui.Text("Phase 2 Visual Lab").Class("page-card-title"),
                        Ui.Text("Multi-background / multi-shadow / dashed border stay native.").Class("muted"),
                        new UiElementBuilder(UiNodeKind.Custom, "div").Text("Layered Surface").Class("phase-two-badge")),
                Ui.Row(
                        new UiElementBuilder(UiNodeKind.Custom, "div").Id(prefix + "-phase2-border-chip").Text("Dotted Border Chip").Class("phase-two-border-chip"),
                        new UiElementBuilder(UiNodeKind.Container, "div").Id(prefix + "-phase2-mask-host").Class("phase-two-mask-host").FlexGrow(1f)
                            .FlexBasis(0f)
                            .Children(
                                new UiElementBuilder(UiNodeKind.Custom, "div").Id(prefix + "-phase2-mask-label").Text("Gradient Mask").Class("phase-two-badge"),
                                Ui.Text("Alpha mask fades both edges for prototype cards.").Class("muted")),
                        new UiElementBuilder(UiNodeKind.Container, "div").Id(prefix + "-phase2-clip-host").Class("phase-two-clip-host").FlexGrow(1f)
                            .FlexBasis(0f)
                            .Children(
                                new UiElementBuilder(UiNodeKind.Custom, "div").Id(prefix + "-phase2-clip-label").Text("Circle Clip").Class("phase-two-badge"),
                                new UiElementBuilder(UiNodeKind.Custom, "div").Id(prefix + "-phase2-clip-ribbon").Text("clip-path keeps decorative ribbon bounded").Class("phase-two-clip-ribbon")))
                    .Class("phase-two-lab-row"));
    }

    internal static UiElementBuilder BuildPhaseThreePanel(string prefix)
    {
        return new UiElementBuilder(UiNodeKind.Container, "div")
            .Id(prefix + "-phase3-panel")
            .Class("phase-three-panel")
            .Children(
                Ui.Text("Phase 3 Text Lab").Class("page-card-title"),
                Ui.Text("Multilingual / RTL / ellipsis / decoration evidence in one native panel.").Class("muted"),
                Ui.Row(
                        Ui.Text("Hello 文本 שלום 😀 office ffi").Id(prefix + "-phase3-multilingual").Classes("phase-three-card", "phase-three-multilingual"),
                        Ui.Text("שלום Ludots ממשק RTL").Id(prefix + "-phase3-rtl").Classes("phase-three-card", "phase-three-rtl"))
                    .Class("phase-three-row"),
                Ui.Row(
                        Ui.Text("Prototype export keeps extremely long labels readable inside narrow HUD chips and table headers.").Id(prefix + "-phase3-ellipsis").Classes("phase-three-card", "phase-three-ellipsis"),
                        Ui.Text("Underline + strike decoration stays native").Id(prefix + "-phase3-decoration").Classes("phase-three-card", "phase-three-decoration"))
                    .Class("phase-three-row"));
    }

    internal static UiElementBuilder BuildPhaseFivePanel(string prefix)
    {
        return new UiElementBuilder(UiNodeKind.Container, "div")
            .Id(prefix + "-phase5-panel")
            .Class("phase-five-panel")
            .Children(
                Ui.Text("Phase 5 Keyframe Animation Lab").Class("page-card-title"),
                Ui.Text("Pure CSS @keyframes / animation reuse the same UiScene.AdvanceTime clock and Skia renderer.").Class("muted"),
                Ui.Row(
                        Ui.Button("Pulse Keyframes").Id(prefix + "-phase5-pulse").Classes("phase-five-card", "phase-five-pulse")
                            .FlexGrow(1f)
                            .FlexBasis(0f),
                        Ui.Button("Alternate + Delay").Id(prefix + "-phase5-breathe").Classes("phase-five-card", "phase-five-breathe")
                            .FlexGrow(1f)
                            .FlexBasis(0f))
                    .Class("phase-five-row"));
    }

    internal static UiElementBuilder BuildPhaseFourPanel(string prefix)
    {
        return new UiElementBuilder(UiNodeKind.Container, "div")
            .Id(prefix + "-phase4-panel")
            .Class("phase-four-panel")
            .Children(
                Ui.Text("Phase 4 Image + Vector Lab").Class("page-card-title"),
                Ui.Text("background-image:url / SVG image / native C# canvas share one Skia renderer.").Class("muted"),
                Ui.Row(
                        new UiElementBuilder(UiNodeKind.Container, "div").Id(prefix + "-phase4-bg-host").Class("phase-four-bg-host").FlexGrow(1f)
                            .FlexBasis(0f)
                            .Children(
                                new UiElementBuilder(UiNodeKind.Custom, "div").Id(prefix + "-phase4-bg-badge").Text("CSS Background").Class("phase-four-chip"),
                                Ui.Text("cover / center / no-repeat from CSS file.").Class("muted")),
                        Ui.Image(UiShowcaseImageAssets.BadgeSvgDataUri).Id(prefix + "-phase4-svg-image").Class("phase-four-svg-image"),
                        Ui.Canvas(DrawPhaseFourCanvas).Id(prefix + "-phase4-canvas").Class("phase-four-canvas")
                            .Attribute("width", "220")
                            .Attribute("height", "120"))
                    .Class("phase-four-row"));
    }

    internal static UiCanvasContent CreatePhaseFourCanvasContent()
    {
        return new UiCanvasContent(DrawPhaseFourCanvas);
    }

    private static void DrawPhaseFourCanvas(SKCanvas canvas, SKRect rect)
    {
        using SKPaint backgroundPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        using SKPaint framePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, Color = new SKColor(255, 255, 255, 86) };
        using SKPaint linePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f, Color = SKColor.Parse("#22d3ee"), StrokeCap = SKStrokeCap.Round };
        using SKPaint dotPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColor.Parse("#f59e0b") };
        using SKPaint textPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White };

        backgroundPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top),
            new SKPoint(rect.Right, rect.Bottom),
            new[] { SKColor.Parse("#0f172a"), SKColor.Parse("#1d4ed8"), SKColor.Parse("#22d3ee") },
            new[] { 0f, 0.58f, 1f },
            SKShaderTileMode.Clamp);

        canvas.DrawRoundRect(rect, 12f, 12f, backgroundPaint);
        canvas.DrawRoundRect(rect, 12f, 12f, framePaint);

        SKPoint[] points =
        {
            new(rect.Left + 20f, rect.Bottom - 24f),
            new(rect.Left + 64f, rect.Top + 54f),
            new(rect.Left + 112f, rect.Top + 62f),
            new(rect.Right - 26f, rect.Top + 24f)
        };

        using SKPath path = new();
        path.MoveTo(points[0]);
        path.CubicTo(points[1].X - 8f, points[1].Y + 16f, points[1].X + 10f, points[1].Y - 10f, points[1].X, points[1].Y);
        path.CubicTo(points[2].X - 10f, points[2].Y - 4f, points[2].X + 10f, points[2].Y + 18f, points[2].X, points[2].Y);
        path.CubicTo(points[3].X - 14f, points[3].Y + 26f, points[3].X - 4f, points[3].Y + 2f, points[3].X, points[3].Y);
        canvas.DrawPath(path, linePaint);

        foreach (SKPoint point in points)
        {
            canvas.DrawCircle(point, 5f, dotPaint);
        }

        using SKFont font = new(SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default);
        canvas.DrawText("Native Canvas", rect.Left + 14f, rect.Top + 18f, SKTextAlign.Left, font, textPaint);
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
