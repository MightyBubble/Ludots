using System;
using System.Collections.Generic;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI.Compose;
using Ludots.UI.Panels;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using NarrativeFrontendMod.Runtime;

namespace NarrativeFrontendMod.UI;

internal static class NarrativeFrontendUiComposer
{
    public static UiElementBuilder BuildRoot(
        ReactiveContext<NarrativeFrontendRenderState> context,
        PanelLayoutTemplateCatalog layouts,
        PanelLayoutComposer layoutComposer,
        NarrativeFrontendLayoutMetrics metrics)
    {
        NarrativeFrontendRenderState state = context.State;
        var children = new List<UiElementBuilder>(state.Surfaces.Count + 1);
        var bottomLane = new List<(UiElementBuilder Content, NarrativeFrontendSurfaceModel Surface)>();
        if (!string.IsNullOrWhiteSpace(state.BackdropHex))
        {
            children.Add(Ui.Text(" ")
                .WidthPercent(100f)
                .HeightPercent(100f)
                .Absolute(0f, 0f)
                .Background(state.BackdropHex)
                .ZIndex(5));
        }

        for (int i = 0; i < state.Surfaces.Count; i++)
        {
            NarrativeFrontendSurfaceModel surface = state.Surfaces[i];
            if (string.IsNullOrWhiteSpace(surface.LayoutId))
            {
                throw new InvalidOperationException(
                    $"Narrative surface '{surface.SurfaceId}' requires layoutId.");
            }

            PanelLayoutTemplate template = layouts.Require(surface.LayoutId);
            UiElementBuilder content = layoutComposer.Compose(
                template.Root,
                new NarrativeSurfaceBindingScope(surface, metrics),
                static resolvedSource => resolvedSource);
            if (surface.Anchor is NarrativeFrontendAnchor.BottomLeft
                or NarrativeFrontendAnchor.BottomCenter
                or NarrativeFrontendAnchor.BottomRight)
            {
                bottomLane.Add((content, surface));
            }
            else
            {
                children.Add(BuildSurface(content, surface, metrics));
            }
        }

        if (bottomLane.Count == 1)
        {
            children.Add(BuildSurface(bottomLane[0].Content, bottomLane[0].Surface, metrics));
        }
        else if (bottomLane.Count > 1)
        {
            children.Add(BuildBottomLane(bottomLane, metrics));
        }

        return Ui.Column(children.ToArray())
            .Class("story-root")
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(10);
    }

    private static UiElementBuilder BuildSurface(
        UiElementBuilder content,
        NarrativeFrontendSurfaceModel surface,
        NarrativeFrontendLayoutMetrics metrics)
    {
        UiElementBuilder builder = PrepareSurface(content, surface);

        UiAlignItems horizontal = surface.Anchor switch
        {
            NarrativeFrontendAnchor.TopLeft
                or NarrativeFrontendAnchor.LeftCenter
                or NarrativeFrontendAnchor.BottomLeft => UiAlignItems.Start,
            NarrativeFrontendAnchor.TopCenter
                or NarrativeFrontendAnchor.Center
                or NarrativeFrontendAnchor.BottomCenter => UiAlignItems.Center,
            NarrativeFrontendAnchor.TopRight
                or NarrativeFrontendAnchor.RightCenter
                or NarrativeFrontendAnchor.BottomRight => UiAlignItems.End,
            _ => throw new InvalidOperationException(
                $"Narrative surface '{surface.SurfaceId}' has unsupported anchor '{surface.Anchor}'.")
        };
        UiJustifyContent vertical = surface.Anchor switch
        {
            NarrativeFrontendAnchor.TopLeft
                or NarrativeFrontendAnchor.TopCenter
                or NarrativeFrontendAnchor.TopRight => UiJustifyContent.Start,
            NarrativeFrontendAnchor.LeftCenter
                or NarrativeFrontendAnchor.Center
                or NarrativeFrontendAnchor.RightCenter => UiJustifyContent.Center,
            NarrativeFrontendAnchor.BottomLeft
                or NarrativeFrontendAnchor.BottomCenter
                or NarrativeFrontendAnchor.BottomRight => UiJustifyContent.End,
            _ => throw new InvalidOperationException(
                $"Narrative surface '{surface.SurfaceId}' has unsupported anchor '{surface.Anchor}'.")
        };

        bool leftAnchor = horizontal == UiAlignItems.Start;
        bool rightAnchor = horizontal == UiAlignItems.End;
        bool topAnchor = vertical == UiJustifyContent.Start;
        bool bottomAnchor = vertical == UiJustifyContent.End;
        float leftPadding = leftAnchor ? Math.Max(0f, metrics.SafeAreaMargin + surface.OffsetX) : metrics.SafeAreaMargin;
        float rightPadding = rightAnchor ? Math.Max(0f, metrics.SafeAreaMargin + surface.OffsetX) : metrics.SafeAreaMargin;
        float topPadding = topAnchor ? Math.Max(0f, metrics.SafeAreaMargin + surface.OffsetY) : 0f;
        float bottomPadding = bottomAnchor ? Math.Max(0f, metrics.SafeAreaMargin + surface.OffsetY) : 0f;
        if (!leftAnchor && !rightAnchor && Math.Abs(surface.OffsetX) > 0.01f)
        {
            builder = builder.Translate(surface.OffsetX);
        }

        if (!topAnchor && !bottomAnchor && Math.Abs(surface.OffsetY) > 0.01f)
        {
            builder = builder.Translate(0f, surface.OffsetY);
        }

        return Ui.Column(builder)
            .Class("story-surface-dock")
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Padding(leftPadding, topPadding, rightPadding, bottomPadding)
            .Justify(vertical)
            .Align(horizontal)
            .Absolute(0f, 0f)
            .ZIndex(surface.ZIndex);
    }

    private static UiElementBuilder BuildBottomLane(
        List<(UiElementBuilder Content, NarrativeFrontendSurfaceModel Surface)> surfaces,
        NarrativeFrontendLayoutMetrics metrics)
    {
        surfaces.Sort(static (left, right) =>
        {
            int byAnchor = left.Surface.Anchor.CompareTo(right.Surface.Anchor);
            return byAnchor != 0 ? byAnchor : left.Surface.ZIndex.CompareTo(right.Surface.ZIndex);
        });

        var children = new UiElementBuilder[surfaces.Count];
        int maxZIndex = 0;
        for (int i = 0; i < surfaces.Count; i++)
        {
            NarrativeFrontendSurfaceModel surface = surfaces[i].Surface;
            float leftInset = surface.Anchor == NarrativeFrontendAnchor.BottomRight
                ? 0f
                : Math.Max(0f, surface.OffsetX);
            float rightInset = surface.Anchor == NarrativeFrontendAnchor.BottomRight
                ? Math.Max(0f, surface.OffsetX)
                : Math.Max(0f, -surface.OffsetX);
            children[i] = Ui.Column(PrepareSurface(surfaces[i].Content, surface))
                .Padding(leftInset, 0f, rightInset, Math.Max(0f, surface.OffsetY))
                .Justify(UiJustifyContent.End);
            maxZIndex = Math.Max(maxZIndex, surface.ZIndex);
        }

        return Ui.Column(
                Ui.Row(children)
                    .Class("story-bottom-lane-row")
                    .WidthPercent(100f)
                    .Wrap()
                    .Gap(metrics.BottomLaneGap)
                    .Justify(UiJustifyContent.Center)
                    .Align(UiAlignItems.End))
            .Class("story-surface-dock")
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Padding(metrics.SafeAreaMargin)
            .Justify(UiJustifyContent.End)
            .Align(UiAlignItems.Stretch)
            .Absolute(0f, 0f)
            .ZIndex(maxZIndex);
    }

    private static UiElementBuilder PrepareSurface(
        UiElementBuilder content,
        NarrativeFrontendSurfaceModel surface)
    {
        return ApplyAuthorChrome(content, surface)
            .Class("story-surface")
            .Attribute("data-surface-kind", surface.Kind.ToString())
            .Width(surface.Width)
            .ZIndex(surface.ZIndex);
    }

    private static UiElementBuilder ApplyAuthorChrome(
        UiElementBuilder builder,
        NarrativeFrontendSurfaceModel surface)
    {
        if (string.IsNullOrWhiteSpace(surface.FrameImageSrc))
        {
            return builder;
        }

        bool choiceFrame = surface.Kind == NarrativeFrontendSurfaceKind.ChoiceList;
        string frameClass = choiceFrame
            ? "story-choice-frame"
            : "story-frame";
        string bodyFrameClass = choiceFrame
            ? "story-choice-framed-body"
            : "story-panel-framed-body";
        return Ui.Panel(
                builder.Classes("story-framed-body", bodyFrameClass),
                Ui.Image(surface.FrameImageSrc)
                    .Class(frameClass)
                    .Absolute(0f, 0f)
                    .WidthPercent(100f)
                    .HeightPercent(100f)
                    .ZIndex(40))
            .Class("story-framed");
    }

    private sealed class NarrativeSurfaceBindingScope : IPanelLayoutBindingScope
    {
        private readonly NarrativeFrontendSurfaceModel _surface;
        private readonly NarrativeFrontendLayoutMetrics _metrics;

        public NarrativeSurfaceBindingScope(
            NarrativeFrontendSurfaceModel surface,
            NarrativeFrontendLayoutMetrics metrics)
        {
            _surface = surface;
            _metrics = metrics;
        }

        public string ReadText(string bind)
        {
            return bind switch
            {
                "title" => _surface.Title,
                "subtitle" => _surface.Subtitle,
                "body" => _surface.Body,
                "footer" => _surface.Footer,
                "portraitSrc" => _surface.PortraitSrc,
                "foregroundHex" => _surface.ForegroundHex,
                "mutedHex" => _surface.MutedHex,
                "accentHex" => _surface.AccentHex,
                "surfaceClass" => _surface.StyleClass,
                _ => throw new InvalidOperationException(
                    $"Narrative surface binding '{bind}' is not a text binding.")
            };
        }

        public float ReadFloat(string bind)
        {
            return bind switch
            {
                "portraitSize" => _surface.PortraitSize,
                "standingWidth" => _surface.PortraitSize * _metrics.StandingImageAspect,
                "standingCardWidth" => Math.Max(
                    _metrics.StandingCardMinWidth,
                    _surface.Width - (_surface.PortraitSize * _metrics.StandingImageAspect) - _metrics.StandingCardGap),
                "progress01" => _surface.Progress01,
                "countdownSeconds" => _surface.CountdownSeconds,
                _ => throw new InvalidOperationException(
                    $"Narrative surface binding '{bind}' is not a numeric binding.")
            };
        }

        public bool ReadBool(string bind)
        {
            throw new InvalidOperationException(
                $"Narrative surface binding '{bind}' is not a bool binding.");
        }

        public IReadOnlyList<PresentationTextRun> ReadTextRuns(string bind)
        {
            if (!string.Equals(bind, "bodyRuns", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Narrative surface binding '{bind}' is not a styled-text binding.");
            }

            return _surface.BodyRuns ?? Array.Empty<PresentationTextRun>();
        }

        public IReadOnlyList<IPanelLayoutBindingScope> ReadList(string bind)
        {
            if (!string.Equals(bind, "items", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Narrative surface binding '{bind}' is not a list binding.");
            }

            IReadOnlyList<NarrativeFrontendSurfaceItem> items =
                _surface.Items ?? Array.Empty<NarrativeFrontendSurfaceItem>();
            var scopes = new IPanelLayoutBindingScope[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                scopes[i] = new NarrativeItemBindingScope(
                    items[i],
                    _surface.Kind,
                    _surface.ForegroundHex,
                    _surface.MutedHex);
            }

            return scopes;
        }

        public bool IsPresent(string bind)
        {
            return bind switch
            {
                "title" => !string.IsNullOrWhiteSpace(_surface.Title),
                "subtitle" => !string.IsNullOrWhiteSpace(_surface.Subtitle),
                "body" => !string.IsNullOrWhiteSpace(_surface.Body),
                "footer" => !string.IsNullOrWhiteSpace(_surface.Footer),
                "portraitSrc" => !string.IsNullOrWhiteSpace(_surface.PortraitSrc),
                "items" => _surface.Items is { Count: > 0 },
                "progressPresent" => _surface.Progress01 >= 0f,
                "countdownPresent" => _surface.CountdownSeconds > 0f,
                _ => throw new InvalidOperationException(
                    $"Narrative surface binding '{bind}' cannot be used as a presence condition.")
            };
        }
    }

    private sealed class NarrativeItemBindingScope : IPanelLayoutBindingScope
    {
        private readonly NarrativeFrontendSurfaceItem _item;
        private readonly NarrativeFrontendSurfaceKind _surfaceKind;
        private readonly string _foregroundHex;
        private readonly string _mutedHex;

        public NarrativeItemBindingScope(
            NarrativeFrontendSurfaceItem item,
            NarrativeFrontendSurfaceKind surfaceKind,
            string foregroundHex,
            string mutedHex)
        {
            _item = item;
            _surfaceKind = surfaceKind;
            _foregroundHex = foregroundHex;
            _mutedHex = mutedHex;
        }

        public string ReadText(string bind)
        {
            return bind switch
            {
                "label" => _item.Label,
                "value" => _item.Value,
                "caption" => _item.Caption,
                "shortcut" => _item.Shortcut,
                "itemClass" => _surfaceKind == NarrativeFrontendSurfaceKind.ChoiceList
                    ? (_item.Active ? "story-choice-item-active" : "story-choice-item")
                    : (_item.Active ? "story-item-row-active" : "story-item-row"),
                "itemColor" => _item.AccentHex,
                "foregroundHex" => _foregroundHex,
                "mutedHex" => _mutedHex,
                _ => throw new InvalidOperationException(
                    $"Narrative item binding '{bind}' is not a text binding.")
            };
        }

        public float ReadFloat(string bind)
        {
            return bind switch
            {
                "itemProgress" => _item.Progress01,
                _ => throw new InvalidOperationException(
                    $"Narrative item binding '{bind}' is not a numeric binding.")
            };
        }

        public bool ReadBool(string bind)
        {
            return bind switch
            {
                "active" => _item.Active,
                "muted" => _item.Muted,
                _ => throw new InvalidOperationException(
                    $"Narrative item binding '{bind}' is not a bool binding.")
            };
        }

        public IReadOnlyList<PresentationTextRun> ReadTextRuns(string bind)
        {
            throw new InvalidOperationException(
                $"Narrative item binding '{bind}' is not a styled-text binding.");
        }

        public IReadOnlyList<IPanelLayoutBindingScope> ReadList(string bind)
        {
            throw new InvalidOperationException(
                $"Narrative item binding '{bind}' is not a list binding.");
        }

        public bool IsPresent(string bind)
        {
            return bind switch
            {
                "label" => !string.IsNullOrWhiteSpace(_item.Label),
                "value" => !string.IsNullOrWhiteSpace(_item.Value),
                "caption" => !string.IsNullOrWhiteSpace(_item.Caption),
                "shortcut" => !string.IsNullOrWhiteSpace(_item.Shortcut),
                "itemProgressPresent" => _item.Progress01 >= 0f,
                _ => throw new InvalidOperationException(
                    $"Narrative item binding '{bind}' cannot be used as a presence condition.")
            };
        }
    }
}
