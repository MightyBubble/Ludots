using System;
using System.Collections.Generic;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using NarrativeFrontendMod.Runtime;

namespace NarrativeFrontendMod.UI;

internal static class NarrativeFrontendUiComposer
{
    private const float CanvasWidth = 1920f;
    private const float CanvasHeight = 1080f;
    private const float Margin = 24f;

    public static UiElementBuilder BuildRoot(ReactiveContext<NarrativeFrontendRenderState> context)
    {
        NarrativeFrontendRenderState state = context.State;
        var children = new List<UiElementBuilder>(state.Surfaces.Count + 1);

        if (!string.IsNullOrWhiteSpace(state.BackdropHex))
        {
            children.Add(Ui.Text(" ")
                .WidthPercent(100f)
                .HeightPercent(100f)
                .Absolute(0f, 0f)
                .Background(state.BackdropHex)
                .ZIndex(5));
        }

        foreach (NarrativeFrontendSurfaceModel surface in state.Surfaces)
        {
            children.Add(BuildSurface(surface));
        }

        return Ui.Column(children.ToArray())
            .Class("story-root")
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(10);
    }

    private static UiElementBuilder BuildSurface(NarrativeFrontendSurfaceModel surface)
    {
        UiElementBuilder builder = surface.Kind switch
        {
            NarrativeFrontendSurfaceKind.DialogueBubble => BuildBubble(surface, tailRight: false),
            NarrativeFrontendSurfaceKind.SubtitleBubble => BuildBubble(surface, tailRight: true),
            NarrativeFrontendSurfaceKind.OverlayDialogue => BuildOverlayDialogue(surface),
            NarrativeFrontendSurfaceKind.ObjectiveTracker => BuildCard(surface, "#74D7FF"),
            NarrativeFrontendSurfaceKind.ChoiceList => BuildChoiceList(surface),
            NarrativeFrontendSurfaceKind.NotificationStack => BuildNotificationStack(surface),
            NarrativeFrontendSurfaceKind.HistoryJournal => BuildCard(surface, "#8BE9FD"),
            NarrativeFrontendSurfaceKind.EventCard => BuildEventCard(surface),
            NarrativeFrontendSurfaceKind.StatusPanel => BuildCard(surface, "#78E3B1"),
            NarrativeFrontendSurfaceKind.PromptRibbon => BuildPromptRibbon(surface),
            NarrativeFrontendSurfaceKind.ThreatBanner => BuildThreatBanner(surface),
            NarrativeFrontendSurfaceKind.RelationshipNotebook => BuildCard(surface, "#F6C56B"),
            NarrativeFrontendSurfaceKind.InspectPanel => BuildCard(surface, "#D9F99D"),
            NarrativeFrontendSurfaceKind.FlowReview => BuildCard(surface, "#C4B5FD"),
            NarrativeFrontendSurfaceKind.TransmissionOverlay => BuildTransmission(surface),
            NarrativeFrontendSurfaceKind.StandingPortrait => BuildStandingPortrait(surface),
            _ => BuildCard(surface, "#78E3B1"),
        };

        (float left, float top) = ResolvePosition(surface);
        return builder
            .Class("story-surface")
            .Width(surface.Width)
            .Absolute(left, top)
            .ZIndex(surface.ZIndex);
    }

    private static UiElementBuilder BuildBubble(NarrativeFrontendSurfaceModel surface, bool tailRight)
    {
        string background = ColorOrDefault(surface.BackgroundHex, "#0C1622E8");
        string foreground = ColorOrDefault(surface.ForegroundHex, "#F5F7FA");
        string muted = ColorOrDefault(surface.MutedHex, "#A9B9C9");
        string accent = ColorOrDefault(surface.AccentHex, "#F0C36B");
        string surfaceClass = tailRight ? "story-subtitle-bubble" : "story-dialogue-bubble";

        return Ui.Column(
                BuildBubbleTail(background, tailRight),
                Ui.Card(
                        BuildEyebrow(surface.Subtitle, accent),
                        BuildPortraitTitleRow(surface, foreground, accent),
                        Ui.Text(surface.Body).Class("story-body").FontSize(13f).Color(foreground).WhiteSpace(UiWhiteSpace.Normal),
                        BuildMetaRow(surface, muted, accent))
                    .Classes(surfaceClass, "story-card")
                    .Gap(8f)
                    .Padding(18f)
                    .Radius(26f)
                    .Background(background)
                    .Border(1f, Color(ColorOrDefault(surface.BorderHex, "#49FFFFFF")))
                    .BoxShadow(0f, 18f, 36f, Color("#44000000"))
                    .BackdropBlur(10f))
            .Gap(0f)
            .Align(tailRight ? UiAlignItems.End : UiAlignItems.Start);
    }

    private static UiElementBuilder BuildStandingPortrait(NarrativeFrontendSurfaceModel surface)
    {
        string foreground = ColorOrDefault(surface.ForegroundHex, "#F8FAFC");
        string muted = ColorOrDefault(surface.MutedHex, "#C4D1DD");
        string accent = ColorOrDefault(surface.AccentHex, "#F6C56B");
        string background = ColorOrDefault(surface.BackgroundHex, "#0A1220EE");
        float standingHeight = surface.PortraitSize > 0f ? surface.PortraitSize : 980f;
        float standingWidth = standingHeight * (1024f / 1536f);

        var dialogueCard = Ui.Card(
                BuildEyebrow(surface.Subtitle, accent),
                Ui.Text(surface.Title).Class("story-title").FontSize(24f).Bold().Color(foreground),
                Ui.Text(surface.Body).Class("story-body").FontSize(16f).Color(foreground).WhiteSpace(UiWhiteSpace.Normal),
                BuildItemsColumn(surface, accent, foreground, muted),
                BuildMetaRow(surface, muted, accent))
            .Classes("story-standing-dialogue", "story-card")
            .Gap(12f)
            .Padding(24f)
            .Radius(28f)
            .Background(background)
            .Border(1f, Color(ColorOrDefault(surface.BorderHex, "#5AD7E9FF")))
            .BoxShadow(0f, 24f, 48f, Color("#55000000"))
            .BackdropBlur(12f)
            .Width(Math.Max(420f, surface.Width - standingWidth - 32f));

        if (string.IsNullOrWhiteSpace(surface.PortraitSrc))
        {
            throw new InvalidOperationException(
                $"StandingPortrait surface '{surface.SurfaceId}' requires PortraitSrc (standing image). Author standingImageId on the speaker.");
        }

        return Ui.Row(
                Ui.Image(surface.PortraitSrc)
                    .Class("story-standing-portrait")
                    .Width(standingWidth)
                    .Height(standingHeight)
                    .Radius(8f)
                    .BoxShadow(0f, 28f, 64f, Color("#66000000")),
                dialogueCard)
            .Class("story-standing-portrait-row")
            .Gap(28f)
            .Align(UiAlignItems.End);
    }

    private static UiElementBuilder BuildOverlayDialogue(NarrativeFrontendSurfaceModel surface)
    {
        string foreground = ColorOrDefault(surface.ForegroundHex, "#F8FAFC");
        string muted = ColorOrDefault(surface.MutedHex, "#C4D1DD");
        string accent = ColorOrDefault(surface.AccentHex, "#F6C56B");
        return Ui.Card(
                BuildEyebrow(surface.Subtitle, accent),
                BuildPortraitTitleRow(surface, foreground, accent),
                Ui.Text(surface.Body).Class("story-body").FontSize(15f).Color(foreground).WhiteSpace(UiWhiteSpace.Normal),
                BuildItemsColumn(surface, accent, foreground, muted),
                BuildMetaRow(surface, muted, accent))
            .Classes("story-overlay-dialogue", "story-card")
            .Gap(12f)
            .Padding(22f)
            .Radius(28f)
            .Background(ColorOrDefault(surface.BackgroundHex, "#0A1220EE"))
            .Border(1f, Color(ColorOrDefault(surface.BorderHex, "#5AD7E9FF")))
            .BoxShadow(0f, 24f, 48f, Color("#55000000"))
            .BackdropBlur(12f);
    }

    private static UiElementBuilder BuildPortraitTitleRow(
        NarrativeFrontendSurfaceModel surface,
        string foreground,
        string accent)
    {
        var titleColumn = Ui.Column(
                Ui.Text(surface.Title).Class("story-title").FontSize(22f).Bold().Color(foreground),
                string.IsNullOrWhiteSpace(surface.Subtitle)
                    ? Ui.Column()
                    : Ui.Column())
            .Gap(4f);

        if (string.IsNullOrWhiteSpace(surface.PortraitSrc))
        {
            return titleColumn;
        }

        float size = surface.PortraitSize > 0f ? surface.PortraitSize : 96f;
        return Ui.Row(
                Ui.Image(surface.PortraitSrc)
                    .Class("story-portrait")
                    .Width(size)
                    .Height(size)
                    .Radius(18f)
                    .Border(2f, Color(accent))
                    .BoxShadow(0f, 10f, 24f, Color("#66000000")),
                titleColumn)
            .Class("story-portrait-row")
            .Gap(16f)
            .Align(UiAlignItems.Center);
    }

    private static UiElementBuilder BuildCard(NarrativeFrontendSurfaceModel surface, string defaultAccent)
    {
        string foreground = ColorOrDefault(surface.ForegroundHex, "#F5F7FA");
        string muted = ColorOrDefault(surface.MutedHex, "#B7C5D2");
        string accent = ColorOrDefault(surface.AccentHex, defaultAccent);
        return Ui.Card(
                BuildEyebrow(surface.Subtitle, accent),
                Ui.Text(surface.Title).FontSize(16f).Bold().Color(foreground),
                string.IsNullOrWhiteSpace(surface.Body)
                    ? Ui.Column()
                    : Ui.Text(surface.Body).FontSize(12f).Color(foreground).WhiteSpace(UiWhiteSpace.Normal),
                BuildItemsColumn(surface, accent, foreground, muted),
                BuildFooter(surface.Footer, muted))
            .Gap(8f)
            .Padding(16f)
            .Radius(22f)
            .Background(ColorOrDefault(surface.BackgroundHex, "#0D1722E6"))
            .Border(1f, Color(ColorOrDefault(surface.BorderHex, "#25465C")))
            .BackdropBlur(8f)
            .Class("story-card");
    }

    private static UiElementBuilder BuildChoiceList(NarrativeFrontendSurfaceModel surface)
    {
        string foreground = ColorOrDefault(surface.ForegroundHex, "#F8FAFC");
        string muted = ColorOrDefault(surface.MutedHex, "#B7C5D2");
        string accent = ColorOrDefault(surface.AccentHex, "#F6C56B");
        var items = new List<UiElementBuilder>
        {
            BuildEyebrow(surface.Subtitle, accent),
            Ui.Text(surface.Title).FontSize(16f).Bold().Color(foreground)
        };

        foreach (NarrativeFrontendSurfaceItem item in surface.Items ?? Array.Empty<NarrativeFrontendSurfaceItem>())
        {
            items.Add(Ui.Row(
                    Ui.Text(string.IsNullOrWhiteSpace(item.Shortcut) ? "?" : item.Shortcut)
                        .FontSize(12f).Bold().Color("#08111A").Background(accent).Padding(8f, 6f).Radius(999f),
                    Ui.Column(
                            Ui.Text(item.Label).FontSize(13f).Bold().Color(foreground).WhiteSpace(UiWhiteSpace.Normal),
                            string.IsNullOrWhiteSpace(item.Caption)
                                ? Ui.Column()
                                : Ui.Text(item.Caption).FontSize(11f).Color(muted).WhiteSpace(UiWhiteSpace.Normal))
                        .Gap(4f))
                .Gap(10f)
                .Padding(12f)
                .Radius(18f)
                .Background(item.Active ? "#1A2734" : "#101926")
                .Border(1f, Color(item.Active ? accent : "#233241")));
        }

        items.Add(BuildMetaRow(surface, muted, accent));

        return Ui.Card(items.ToArray())
            .Classes("story-choice-list", "story-card")
            .Gap(8f)
            .Padding(18f)
            .Radius(24f)
            .Background(ColorOrDefault(surface.BackgroundHex, "#0A1220E8"))
            .Border(1f, Color(ColorOrDefault(surface.BorderHex, "#2F4358")))
            .BackdropBlur(10f);
    }

    private static UiElementBuilder BuildNotificationStack(NarrativeFrontendSurfaceModel surface)
    {
        string accent = ColorOrDefault(surface.AccentHex, "#F6C56B");
        string foreground = ColorOrDefault(surface.ForegroundHex, "#F5F7FA");
        string muted = ColorOrDefault(surface.MutedHex, "#B7C6D5");
        var items = new List<UiElementBuilder>();
        foreach (NarrativeFrontendSurfaceItem item in surface.Items ?? Array.Empty<NarrativeFrontendSurfaceItem>())
        {
            string itemAccent = ColorOrDefault(item.AccentHex, accent);
            items.Add(Ui.Row(
                    Ui.Text(item.Label).FontSize(11f).Bold().Color("#08111A").Background(itemAccent).Padding(8f, 6f).Radius(999f),
                    Ui.Column(
                            Ui.Text(item.Value).FontSize(12f).Bold().Color(foreground).WhiteSpace(UiWhiteSpace.Normal),
                            string.IsNullOrWhiteSpace(item.Caption)
                                ? Ui.Column()
                                : Ui.Text(item.Caption).FontSize(11f).Color(muted).WhiteSpace(UiWhiteSpace.Normal))
                        .Gap(2f))
                .Gap(8f)
                .Padding(12f)
                .Radius(18f)
                .Background(ColorOrDefault(surface.BackgroundHex, "#0C1622E6"))
                .Border(1f, Color(ColorOrDefault(surface.BorderHex, "#2E455B")))
                .BackdropBlur(8f));
        }

        return Ui.Column(items.ToArray()).Gap(8f).Align(UiAlignItems.Stretch);
    }

    private static UiElementBuilder BuildEventCard(NarrativeFrontendSurfaceModel surface)
    {
        string foreground = ColorOrDefault(surface.ForegroundHex, "#08111A");
        string accent = ColorOrDefault(surface.AccentHex, "#F6C56B");
        string muted = ColorOrDefault(surface.MutedHex, "#364A5D");
        return Ui.Card(
                BuildEyebrow(surface.Subtitle, "#253341"),
                Ui.Text(surface.Title).FontSize(18f).Bold().Color(foreground),
                Ui.Text(surface.Body).FontSize(13f).Color(foreground).WhiteSpace(UiWhiteSpace.Normal),
                BuildItemsColumn(surface, "#253341", foreground, muted),
                BuildMetaRow(surface, muted, "#253341"))
            .Gap(8f)
            .Padding(18f)
            .Radius(24f)
            .Background(ColorOrDefault(surface.BackgroundHex, accent))
            .Border(1f, Color(ColorOrDefault(surface.BorderHex, "#FFF6D0")))
            .BoxShadow(0f, 20f, 40f, Color("#44000000"));
    }

    private static UiElementBuilder BuildPromptRibbon(NarrativeFrontendSurfaceModel surface)
    {
        string foreground = ColorOrDefault(surface.ForegroundHex, "#F5F7FA");
        string accent = ColorOrDefault(surface.AccentHex, "#F6C56B");
        return Ui.Row(
                Ui.Text(surface.Title).FontSize(11f).Bold().Color("#08111A").Background(accent).Padding(10f, 7f).Radius(999f),
                Ui.Text(surface.Body).FontSize(12f).Color(foreground).WhiteSpace(UiWhiteSpace.Normal))
            .Gap(10f)
            .Padding(12f)
            .Radius(999f)
            .Background(ColorOrDefault(surface.BackgroundHex, "#071019E8"))
            .Border(1f, Color(ColorOrDefault(surface.BorderHex, "#365875")))
            .BackdropBlur(8f);
    }

    private static UiElementBuilder BuildThreatBanner(NarrativeFrontendSurfaceModel surface)
    {
        return Ui.Card(
                BuildEyebrow(surface.Subtitle, ColorOrDefault(surface.AccentHex, "#FFAA55")),
                Ui.Text(surface.Title).FontSize(18f).Bold().Color(ColorOrDefault(surface.ForegroundHex, "#FFF7F0")),
                Ui.Text(surface.Body).FontSize(13f).Color(ColorOrDefault(surface.ForegroundHex, "#FFF7F0")).WhiteSpace(UiWhiteSpace.Normal),
                BuildFooter(surface.Footer, ColorOrDefault(surface.MutedHex, "#FADBC1")))
            .Gap(6f)
            .Padding(16f)
            .Radius(22f)
            .Background(ColorOrDefault(surface.BackgroundHex, "#4E1610E8"))
            .Border(1f, Color(ColorOrDefault(surface.BorderHex, "#FFB88A")))
            .BoxShadow(0f, 18f, 36f, Color("#55000000"));
    }

    private static UiElementBuilder BuildTransmission(NarrativeFrontendSurfaceModel surface)
    {
        string accent = ColorOrDefault(surface.AccentHex, "#7DD3FC");
        string foreground = ColorOrDefault(surface.ForegroundHex, "#E7F5FF");
        string muted = ColorOrDefault(surface.MutedHex, "#A7C6D8");
        return Ui.Card(
                BuildEyebrow(surface.Subtitle, accent),
                Ui.Text(surface.Title).FontSize(15f).Bold().Color(foreground),
                Ui.Text(surface.Body).FontSize(12f).Color(foreground).WhiteSpace(UiWhiteSpace.Normal),
                BuildFooter(surface.Footer, muted))
            .Gap(6f)
            .Padding(14f)
            .Radius(18f)
            .Background(ColorOrDefault(surface.BackgroundHex, "#08121CE0"))
            .Border(1f, Color(ColorOrDefault(surface.BorderHex, "#2A536B")))
            .BackdropBlur(10f);
    }

    private static UiElementBuilder BuildItemsColumn(
        NarrativeFrontendSurfaceModel surface,
        string accent,
        string foreground,
        string muted)
    {
        IReadOnlyList<NarrativeFrontendSurfaceItem> items = surface.Items ?? Array.Empty<NarrativeFrontendSurfaceItem>();
        if (items.Count == 0)
        {
            return Ui.Column();
        }

        var rows = new List<UiElementBuilder>(items.Count);
        foreach (NarrativeFrontendSurfaceItem item in items)
        {
            rows.Add(Ui.Column(
                    Ui.Row(
                            string.IsNullOrWhiteSpace(item.Shortcut)
                                ? Ui.Column()
                                : Ui.Text(item.Shortcut).FontSize(10f).Bold().Color("#08111A").Background(ColorOrDefault(item.AccentHex, accent)).Padding(6f, 4f).Radius(999f),
                            Ui.Text(item.Label).FontSize(12f).Bold().Color(foreground).WhiteSpace(UiWhiteSpace.Normal),
                            string.IsNullOrWhiteSpace(item.Value)
                                ? Ui.Column()
                                : Ui.Text(item.Value).FontSize(12f).Color(item.Muted ? muted : foreground).WhiteSpace(UiWhiteSpace.Normal))
                        .Gap(8f)
                        .Justify(UiJustifyContent.SpaceBetween),
                    string.IsNullOrWhiteSpace(item.Caption)
                        ? Ui.Column()
                        : Ui.Text(item.Caption).FontSize(11f).Color(muted).WhiteSpace(UiWhiteSpace.Normal),
                    item.Progress01 >= 0f
                        ? BuildProgressBar(item.Progress01, ColorOrDefault(item.AccentHex, accent))
                        : Ui.Column())
                .Gap(4f)
                .Padding(10f)
                .Radius(16f)
                .Background(item.Active ? "#162434" : "#0E1823"));
        }

        return Ui.Column(rows.ToArray()).Gap(6f);
    }

    private static UiElementBuilder BuildProgressBar(float progress01, string accent)
    {
        float clamped = Math.Clamp(progress01, 0f, 1f);
        return Ui.Column(Ui.Text(" ")
                .Height(6f)
                .WidthPercent(Math.Max(4f, clamped * 100f))
                .Background(accent)
                .Radius(999f))
            .Height(6f)
            .WidthPercent(100f)
            .Background("#1E2A35")
            .Radius(999f)
            .Overflow(UiOverflow.Hidden);
    }

    private static UiElementBuilder BuildBubbleTail(string background, bool rightAligned)
    {
        return Ui.Text(" ")
            .Width(18f)
            .Height(18f)
            .Background(background)
            .Rotate(45f)
            .Margin(24f, -8f)
            .Border(1f, Color("#00000000"))
            .Align(rightAligned ? UiAlignItems.End : UiAlignItems.Start);
    }

    private static UiElementBuilder BuildMetaRow(NarrativeFrontendSurfaceModel surface, string muted, string accent)
    {
        var parts = new List<UiElementBuilder>();
        if (surface.Progress01 >= 0f)
        {
            parts.Add(BuildProgressBar(surface.Progress01, accent));
        }

        if (surface.CountdownSeconds > 0f)
        {
            parts.Add(Ui.Text($"{surface.CountdownSeconds:0.0}s").FontSize(11f).Color(muted));
        }

        if (!string.IsNullOrWhiteSpace(surface.Footer))
        {
            parts.Add(Ui.Text(surface.Footer).FontSize(11f).Color(muted).WhiteSpace(UiWhiteSpace.Normal));
        }

        return Ui.Row(parts.ToArray()).Gap(10f).Align(UiAlignItems.Center);
    }

    private static UiElementBuilder BuildFooter(string footer, string muted)
    {
        return string.IsNullOrWhiteSpace(footer)
            ? Ui.Column()
            : Ui.Text(footer).FontSize(11f).Color(muted).WhiteSpace(UiWhiteSpace.Normal);
    }

    private static UiElementBuilder BuildEyebrow(string text, string accent)
    {
        return string.IsNullOrWhiteSpace(text)
            ? Ui.Column()
            : Ui.Text(text)
                .FontSize(10f)
                .Bold()
                .Color("#08111A")
                .Background(accent)
                .Padding(8f, 6f)
                .Radius(999f);
    }

    private static (float Left, float Top) ResolvePosition(NarrativeFrontendSurfaceModel surface)
    {
        float left = surface.Anchor switch
        {
            NarrativeFrontendAnchor.TopLeft or NarrativeFrontendAnchor.LeftCenter or NarrativeFrontendAnchor.BottomLeft => Margin,
            NarrativeFrontendAnchor.TopCenter or NarrativeFrontendAnchor.Center or NarrativeFrontendAnchor.BottomCenter => (CanvasWidth - surface.Width) * 0.5f,
            _ => CanvasWidth - surface.Width - Margin,
        };

        float top = surface.Anchor switch
        {
            NarrativeFrontendAnchor.TopLeft or NarrativeFrontendAnchor.TopCenter or NarrativeFrontendAnchor.TopRight => Margin,
            NarrativeFrontendAnchor.LeftCenter or NarrativeFrontendAnchor.Center or NarrativeFrontendAnchor.RightCenter => (CanvasHeight * 0.5f) - 170f,
            _ => CanvasHeight - 280f,
        };

        if (surface.Kind == NarrativeFrontendSurfaceKind.StandingPortrait)
        {
            top = surface.Anchor switch
            {
                NarrativeFrontendAnchor.TopLeft or NarrativeFrontendAnchor.TopCenter or NarrativeFrontendAnchor.TopRight => Margin,
                NarrativeFrontendAnchor.LeftCenter or NarrativeFrontendAnchor.Center or NarrativeFrontendAnchor.RightCenter => (CanvasHeight - 980f) * 0.5f,
                _ => CanvasHeight - 1000f,
            };
        }

        return (left + surface.OffsetX, top + surface.OffsetY);
    }

    private static string ColorOrDefault(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static UiColor Color(string value)
    {
        return UiColor.TryParse(value, out UiColor color)
            ? color
            : UiColor.White;
    }
}
