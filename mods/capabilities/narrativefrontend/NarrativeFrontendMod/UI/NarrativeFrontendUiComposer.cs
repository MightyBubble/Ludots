using System;
using System.Collections.Generic;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using NarrativeFrontendMod.Runtime;

namespace NarrativeFrontendMod.UI;

/// <summary>
/// Layout-only composer. Visual chrome (fill / border / radius / wash / ornament)
/// belongs to the active panelTheme stylesheet — do not hardcode a parallel skin here.
/// </summary>
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
            NarrativeFrontendSurfaceKind.DialogueBubble => BuildBubble(surface, subtitle: false),
            NarrativeFrontendSurfaceKind.SubtitleBubble => BuildBubble(surface, subtitle: true),
            NarrativeFrontendSurfaceKind.OverlayDialogue => BuildOverlayDialogue(surface),
            NarrativeFrontendSurfaceKind.ObjectiveTracker => BuildCard(surface, "story-objective"),
            NarrativeFrontendSurfaceKind.ChoiceList => BuildChoiceList(surface),
            NarrativeFrontendSurfaceKind.NotificationStack => BuildNotificationStack(surface),
            NarrativeFrontendSurfaceKind.HistoryJournal => BuildCard(surface, "story-history"),
            NarrativeFrontendSurfaceKind.EventCard => BuildEventCard(surface),
            NarrativeFrontendSurfaceKind.StatusPanel => BuildCard(surface, "story-status"),
            NarrativeFrontendSurfaceKind.PromptRibbon => BuildPromptRibbon(surface),
            NarrativeFrontendSurfaceKind.ThreatBanner => BuildThreatBanner(surface),
            NarrativeFrontendSurfaceKind.RelationshipNotebook => BuildCard(surface, "story-relationship"),
            NarrativeFrontendSurfaceKind.InspectPanel => BuildCard(surface, "story-inspect"),
            NarrativeFrontendSurfaceKind.FlowReview => BuildCard(surface, "story-flow-review"),
            NarrativeFrontendSurfaceKind.TransmissionOverlay => BuildTransmission(surface),
            NarrativeFrontendSurfaceKind.StandingPortrait => BuildStandingPortrait(surface),
            NarrativeFrontendSurfaceKind.WorldNameplate => BuildWorldNameplate(surface),
            _ => BuildCard(surface, "story-card"),
        };

        (float left, float top) = ResolvePosition(surface);
        return builder
            .Class("story-surface")
            .Width(surface.Width)
            .Absolute(left, top)
            .ZIndex(surface.ZIndex);
    }

    private static UiElementBuilder BuildBubble(NarrativeFrontendSurfaceModel surface, bool subtitle)
    {
        string surfaceClass = subtitle ? "story-subtitle-bubble" : "story-dialogue-bubble";

        // Surface root must be Absolute: Absolute+Column with Height:Auto measures as 0x0 in Flex.
        return ApplyAuthorChrome(
                Ui.Column(
                        BuildBubbleTail(surface),
                        BuildEyebrow(surface),
                        BuildPortraitTitleRow(surface),
                        BuildBody(surface),
                        BuildMetaRow(surface))
                    .Classes(surfaceClass, "story-card")
                    .Gap(8f)
                    .Align(subtitle ? UiAlignItems.End : UiAlignItems.Start),
                surface);
    }

    private static UiElementBuilder BuildWorldNameplate(NarrativeFrontendSurfaceModel surface)
    {
        return ApplyAuthorChrome(
            Ui.Column(
                    BuildEyebrow(surface),
                    BuildTitle(surface, fontSize: 16f))
                .Classes("story-nameplate", "story-card")
                .Gap(2f)
                .Align(UiAlignItems.Center),
            surface);
    }

    private static UiElementBuilder BuildStandingPortrait(NarrativeFrontendSurfaceModel surface)
    {
        if (string.IsNullOrWhiteSpace(surface.PortraitSrc))
        {
            throw new InvalidOperationException(
                $"StandingPortrait surface '{surface.SurfaceId}' requires PortraitSrc (standing image). Author standingImageId on the speaker.");
        }

        float standingHeight = surface.PortraitSize > 0f ? surface.PortraitSize : 980f;
        float standingWidth = standingHeight * (1024f / 1536f);

        var dialogueCard = ApplyAuthorChrome(
            Ui.Column(
                    BuildEyebrow(surface),
                    BuildTitle(surface, fontSize: 24f),
                    BuildBody(surface, fontSize: 16f),
                    BuildItemsColumn(surface),
                    BuildMetaRow(surface))
                .Classes("story-standing-dialogue", "story-card")
                .Gap(12f)
                .Width(Math.Max(420f, surface.Width - standingWidth - 32f)),
            surface);

        return Ui.Row(
                Ui.Image(surface.PortraitSrc)
                    .Class("story-standing-portrait")
                    .Width(standingWidth)
                    .Height(standingHeight),
                dialogueCard)
            .Class("story-standing-portrait-row")
            .Gap(28f)
            .Align(UiAlignItems.End);
    }

    private static UiElementBuilder BuildOverlayDialogue(NarrativeFrontendSurfaceModel surface)
    {
        return ApplyAuthorChrome(
            Ui.Column(
                    BuildEyebrow(surface),
                    BuildPortraitTitleRow(surface),
                    BuildBody(surface, fontSize: 15f),
                    BuildItemsColumn(surface),
                    BuildMetaRow(surface))
                .Classes("story-overlay-dialogue", "story-card")
                .Gap(12f),
            surface);
    }

    private static UiElementBuilder BuildPortraitTitleRow(NarrativeFrontendSurfaceModel surface)
    {
        var titleColumn = Ui.Column(BuildTitle(surface, fontSize: 22f)).Gap(4f);
        if (string.IsNullOrWhiteSpace(surface.PortraitSrc))
        {
            return titleColumn;
        }

        float size = surface.PortraitSize > 0f ? surface.PortraitSize : 96f;
        return Ui.Row(
                Ui.Image(surface.PortraitSrc)
                    .Class("story-portrait")
                    .Width(size)
                    .Height(size),
                titleColumn)
            .Class("story-portrait-row")
            .Gap(16f)
            .Align(UiAlignItems.Center);
    }

    private static UiElementBuilder BuildCard(NarrativeFrontendSurfaceModel surface, string extraClass)
    {
        return ApplyAuthorChrome(
            Ui.Column(
                    BuildEyebrow(surface),
                    BuildTitle(surface, fontSize: 16f),
                    string.IsNullOrWhiteSpace(surface.Body)
                        ? Ui.Column()
                        : BuildBody(surface, fontSize: 12f),
                    BuildItemsColumn(surface),
                    BuildFooter(surface))
                .Classes("story-card", extraClass)
                .Gap(8f),
            surface);
    }

    private static UiElementBuilder BuildChoiceList(NarrativeFrontendSurfaceModel surface)
    {
        var items = new List<UiElementBuilder>
        {
            BuildEyebrow(surface),
            BuildTitle(surface, fontSize: 16f)
        };

        foreach (NarrativeFrontendSurfaceItem item in surface.Items ?? Array.Empty<NarrativeFrontendSurfaceItem>())
        {
            string itemClass = item.Active ? "story-choice-item-active" : "story-choice-item";
            items.Add(Ui.Row(
                    Ui.Text(string.IsNullOrWhiteSpace(item.Shortcut) ? "?" : item.Shortcut)
                        .Class("story-eyebrow")
                        .FontSize(12f)
                        .Bold(),
                    Ui.Column(
                            ApplyOptionalColor(
                                Ui.Text(item.Label).Class("story-title").FontSize(13f).Bold().WhiteSpace(UiWhiteSpace.Normal),
                                surface.ForegroundHex),
                            string.IsNullOrWhiteSpace(item.Caption)
                                ? Ui.Column()
                                : ApplyOptionalColor(
                                    Ui.Text(item.Caption).Class("story-muted").FontSize(11f).WhiteSpace(UiWhiteSpace.Normal),
                                    surface.MutedHex))
                        .Gap(4f))
                .Class(itemClass)
                .Gap(10f));
        }

        items.Add(BuildMetaRow(surface));

        return ApplyAuthorChrome(
            Ui.Column(items.ToArray())
                .Classes("story-choice-list", "story-card")
                .Gap(8f),
            surface);
    }

    private static UiElementBuilder BuildNotificationStack(NarrativeFrontendSurfaceModel surface)
    {
        var items = new List<UiElementBuilder>();
        foreach (NarrativeFrontendSurfaceItem item in surface.Items ?? Array.Empty<NarrativeFrontendSurfaceItem>())
        {
            items.Add(Ui.Row(
                    Ui.Text(item.Label).Class("story-eyebrow").FontSize(11f).Bold(),
                    Ui.Column(
                            ApplyOptionalColor(
                                Ui.Text(item.Value).Class("story-title").FontSize(12f).Bold().WhiteSpace(UiWhiteSpace.Normal),
                                surface.ForegroundHex),
                            string.IsNullOrWhiteSpace(item.Caption)
                                ? Ui.Column()
                                : ApplyOptionalColor(
                                    Ui.Text(item.Caption).Class("story-muted").FontSize(11f).WhiteSpace(UiWhiteSpace.Normal),
                                    surface.MutedHex))
                        .Gap(2f))
                .Classes("story-notification", "story-card")
                .Gap(8f));
        }

        return Ui.Column(items.ToArray()).Gap(8f).Align(UiAlignItems.Stretch);
    }

    private static UiElementBuilder BuildEventCard(NarrativeFrontendSurfaceModel surface)
    {
        return ApplyAuthorChrome(
            Ui.Column(
                    BuildEyebrow(surface),
                    BuildTitle(surface, fontSize: 18f),
                    BuildBody(surface, fontSize: 13f),
                    BuildItemsColumn(surface),
                    BuildMetaRow(surface))
                .Classes("story-event-card", "story-card")
                .Gap(8f),
            surface);
    }

    private static UiElementBuilder BuildPromptRibbon(NarrativeFrontendSurfaceModel surface)
    {
        return ApplyAuthorChrome(
            Ui.Row(
                    Ui.Text(surface.Title).Class("story-eyebrow").FontSize(11f).Bold(),
                    BuildBody(surface, fontSize: 12f))
                .Class("story-prompt-ribbon")
                .Gap(10f),
            surface);
    }

    private static UiElementBuilder BuildThreatBanner(NarrativeFrontendSurfaceModel surface)
    {
        return ApplyAuthorChrome(
            Ui.Column(
                    BuildEyebrow(surface),
                    BuildTitle(surface, fontSize: 18f),
                    BuildBody(surface, fontSize: 13f),
                    BuildFooter(surface))
                .Classes("story-threat-banner", "story-card")
                .Gap(6f),
            surface);
    }

    private static UiElementBuilder BuildTransmission(NarrativeFrontendSurfaceModel surface)
    {
        return ApplyAuthorChrome(
            Ui.Column(
                    BuildEyebrow(surface),
                    BuildTitle(surface, fontSize: 15f),
                    BuildBody(surface, fontSize: 12f),
                    BuildFooter(surface))
                .Classes("story-transmission", "story-card")
                .Gap(6f),
            surface);
    }

    private static UiElementBuilder BuildItemsColumn(NarrativeFrontendSurfaceModel surface)
    {
        IReadOnlyList<NarrativeFrontendSurfaceItem> items = surface.Items ?? Array.Empty<NarrativeFrontendSurfaceItem>();
        if (items.Count == 0)
        {
            return Ui.Column();
        }

        var rows = new List<UiElementBuilder>(items.Count);
        foreach (NarrativeFrontendSurfaceItem item in items)
        {
            string rowClass = item.Active ? "story-item-row-active" : "story-item-row";
            rows.Add(Ui.Column(
                    Ui.Row(
                            string.IsNullOrWhiteSpace(item.Shortcut)
                                ? Ui.Column()
                                : Ui.Text(item.Shortcut).Class("story-eyebrow").FontSize(10f).Bold(),
                            ApplyOptionalColor(
                                Ui.Text(item.Label).Class("story-title").FontSize(12f).Bold().WhiteSpace(UiWhiteSpace.Normal),
                                surface.ForegroundHex),
                            string.IsNullOrWhiteSpace(item.Value)
                                ? Ui.Column()
                                : ApplyOptionalColor(
                                    Ui.Text(item.Value).Class(item.Muted ? "story-muted" : "story-body").FontSize(12f).WhiteSpace(UiWhiteSpace.Normal),
                                    item.Muted ? surface.MutedHex : surface.ForegroundHex))
                        .Gap(8f)
                        .Justify(UiJustifyContent.SpaceBetween),
                    string.IsNullOrWhiteSpace(item.Caption)
                        ? Ui.Column()
                        : ApplyOptionalColor(
                            Ui.Text(item.Caption).Class("story-muted").FontSize(11f).WhiteSpace(UiWhiteSpace.Normal),
                            surface.MutedHex),
                    item.Progress01 >= 0f
                        ? BuildProgressBar(item.Progress01, FirstNonEmpty(item.AccentHex, surface.AccentHex))
                        : Ui.Column())
                .Class(rowClass)
                .Gap(4f));
        }

        return Ui.Column(rows.ToArray()).Gap(6f);
    }

    private static UiElementBuilder BuildProgressBar(float progress01, string accentHex)
    {
        float clamped = Math.Clamp(progress01, 0f, 1f);
        var fill = Ui.Text(" ")
            .Height(6f)
            .WidthPercent(Math.Max(4f, clamped * 100f))
            .Radius(999f);
        if (!string.IsNullOrWhiteSpace(accentHex))
        {
            fill = fill.Background(accentHex);
        }

        return Ui.Column(fill)
            .Class("story-progress")
            .Height(6f)
            .WidthPercent(100f)
            .Background("#1E2A35")
            .Radius(999f)
            .Overflow(UiOverflow.Hidden);
    }

    private static UiElementBuilder BuildBubbleTail(NarrativeFrontendSurfaceModel surface)
    {
        var tail = Ui.Text(" ")
            .Class("story-bubble-tail")
            .Width(18f)
            .Height(18f)
            .Rotate(45f)
            .Margin(24f, -8f);
        if (!string.IsNullOrWhiteSpace(surface.BackgroundHex))
        {
            tail = tail.Background(surface.BackgroundHex);
        }

        return tail;
    }

    private static UiElementBuilder BuildMetaRow(NarrativeFrontendSurfaceModel surface)
    {
        var parts = new List<UiElementBuilder>();
        if (surface.Progress01 >= 0f)
        {
            parts.Add(BuildProgressBar(surface.Progress01, surface.AccentHex));
        }

        if (surface.CountdownSeconds > 0f)
        {
            parts.Add(ApplyOptionalColor(
                Ui.Text($"{surface.CountdownSeconds:0.0}s").Class("story-muted").FontSize(11f),
                surface.MutedHex));
        }

        if (!string.IsNullOrWhiteSpace(surface.Footer))
        {
            parts.Add(ApplyOptionalColor(
                Ui.Text(surface.Footer).Class("story-muted").FontSize(11f).WhiteSpace(UiWhiteSpace.Normal),
                surface.MutedHex));
        }

        return Ui.Row(parts.ToArray()).Gap(10f).Align(UiAlignItems.Center);
    }

    private static UiElementBuilder BuildFooter(NarrativeFrontendSurfaceModel surface)
    {
        return string.IsNullOrWhiteSpace(surface.Footer)
            ? Ui.Column()
            : ApplyOptionalColor(
                Ui.Text(surface.Footer).Class("story-muted").FontSize(11f).WhiteSpace(UiWhiteSpace.Normal),
                surface.MutedHex);
    }

    private static UiElementBuilder BuildEyebrow(NarrativeFrontendSurfaceModel surface)
    {
        if (string.IsNullOrWhiteSpace(surface.Subtitle))
        {
            return Ui.Column();
        }

        var text = Ui.Text(surface.Subtitle)
            .Class("story-eyebrow")
            .FontSize(10f)
            .Bold();
        if (!string.IsNullOrWhiteSpace(surface.AccentHex))
        {
            text = text.Background(surface.AccentHex);
        }

        return text;
    }

    private static UiElementBuilder BuildTitle(NarrativeFrontendSurfaceModel surface, float fontSize)
    {
        return ApplyOptionalColor(
            Ui.Text(surface.Title).Class("story-title").FontSize(fontSize).Bold(),
            surface.ForegroundHex);
    }

    private static UiElementBuilder BuildBody(NarrativeFrontendSurfaceModel surface, float fontSize = 13f)
    {
        return ApplyOptionalColor(
            Ui.Text(surface.Body).Class("story-body").FontSize(fontSize).WhiteSpace(UiWhiteSpace.Normal),
            surface.ForegroundHex);
    }

    private static UiElementBuilder ApplyAuthorChrome(UiElementBuilder builder, NarrativeFrontendSurfaceModel surface)
    {
        // Author overrides only. Empty means theme.css owns the skin (NO parallel hardcoded chrome).
        if (!string.IsNullOrWhiteSpace(surface.BackgroundHex))
        {
            builder = builder.Background(surface.BackgroundHex);
        }

        // 有九宫格框图时，边线交给 frame 图；再画 BorderHex 会盖住拟物框。
        if (string.IsNullOrWhiteSpace(surface.FrameImageSrc) &&
            !string.IsNullOrWhiteSpace(surface.BorderHex))
        {
            builder = builder.Border(1f, Color(surface.BorderHex));
        }

        return WrapWithThemeFrame(builder, surface);
    }

    private static UiElementBuilder WrapWithThemeFrame(UiElementBuilder content, NarrativeFrontendSurfaceModel surface)
    {
        if (string.IsNullOrWhiteSpace(surface.FrameImageSrc))
        {
            return content;
        }

        string frameClass = surface.Kind == NarrativeFrontendSurfaceKind.ChoiceList
            ? "story-choice-frame"
            : "story-frame";

        // 对齐 UiShowcase 九宫格：框图叠在内容之上（中心透明），内容用 story-framed-body 留出切边内边距。
        return Ui.Panel(
                content.Class("story-framed-body"),
                Ui.Image(surface.FrameImageSrc)
                    .Class(frameClass)
                    .Absolute(0f, 0f)
                    .WidthPercent(100f)
                    .HeightPercent(100f)
                    .ZIndex(40))
            .Class("story-framed");
    }

    private static UiElementBuilder ApplyOptionalColor(UiElementBuilder builder, string hex)
    {
        return string.IsNullOrWhiteSpace(hex) ? builder : builder.Color(hex);
    }

    private static string FirstNonEmpty(string a, string b)
    {
        if (!string.IsNullOrWhiteSpace(a))
        {
            return a;
        }

        return string.IsNullOrWhiteSpace(b) ? string.Empty : b;
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

    private static UiColor Color(string value)
    {
        return UiColor.TryParse(value, out UiColor color)
            ? color
            : UiColor.White;
    }
}
