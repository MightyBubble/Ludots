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
    private const float Margin = 24f;

    public static UiElementBuilder BuildRoot(
        ReactiveContext<NarrativeFrontendRenderState> context,
        PanelLayoutTemplateCatalog layouts,
        PanelLayoutComposer layoutComposer,
        float viewportWidth,
        float viewportHeight)
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

        for (int i = 0; i < state.Surfaces.Count; i++)
        {
            NarrativeFrontendSurfaceModel surface = state.Surfaces[i];
            if (string.IsNullOrWhiteSpace(surface.LayoutId))
            {
                throw new InvalidOperationException(
                    $"Narrative surface '{surface.SurfaceId}' requires layoutId.");
            }

            PanelLayoutTemplate template = layouts.Require(surface.LayoutId);
            UiElementBuilder content = layoutComposer.ComposeControls(
                new[] { template.Root },
                new NarrativeSurfaceBindingScope(surface));
            children.Add(BuildSurface(content, surface, viewportWidth, viewportHeight));
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
        float viewportWidth,
        float viewportHeight)
    {
        UiElementBuilder builder = ApplyAuthorChrome(content, surface)
            .Class("story-surface")
            .Width(surface.Width)
            .ZIndex(surface.ZIndex);

        float left = surface.Anchor switch
        {
            NarrativeFrontendAnchor.TopLeft
                or NarrativeFrontendAnchor.LeftCenter
                or NarrativeFrontendAnchor.BottomLeft => Margin + surface.OffsetX,
            NarrativeFrontendAnchor.TopCenter
                or NarrativeFrontendAnchor.Center
                or NarrativeFrontendAnchor.BottomCenter =>
                ((viewportWidth - surface.Width) * 0.5f) + surface.OffsetX,
            _ => 0f
        };

        return surface.Anchor switch
        {
            NarrativeFrontendAnchor.TopLeft or NarrativeFrontendAnchor.TopCenter =>
                builder.AbsoluteEdges(left: left, top: Margin + surface.OffsetY),
            NarrativeFrontendAnchor.TopRight =>
                builder.AbsoluteEdges(top: Margin + surface.OffsetY, right: Margin + surface.OffsetX),
            NarrativeFrontendAnchor.BottomLeft or NarrativeFrontendAnchor.BottomCenter =>
                builder.AbsoluteEdges(left: left, bottom: Margin + surface.OffsetY),
            NarrativeFrontendAnchor.BottomRight =>
                builder.AbsoluteEdges(right: Margin + surface.OffsetX, bottom: Margin + surface.OffsetY),
            NarrativeFrontendAnchor.LeftCenter =>
                builder.AbsoluteEdges(left: left, top: (viewportHeight * 0.5f) + surface.OffsetY),
            NarrativeFrontendAnchor.Center =>
                builder.AbsoluteEdges(left: left, top: (viewportHeight * 0.5f) + surface.OffsetY),
            NarrativeFrontendAnchor.RightCenter =>
                builder.AbsoluteEdges(right: Margin + surface.OffsetX, top: (viewportHeight * 0.5f) + surface.OffsetY),
            _ => throw new InvalidOperationException(
                $"Narrative surface '{surface.SurfaceId}' has unsupported anchor '{surface.Anchor}'.")
        };
    }

    private static UiElementBuilder ApplyAuthorChrome(
        UiElementBuilder builder,
        NarrativeFrontendSurfaceModel surface)
    {
        if (!string.IsNullOrWhiteSpace(surface.BackgroundHex))
        {
            builder = builder.Background(surface.BackgroundHex);
        }

        if (string.IsNullOrWhiteSpace(surface.FrameImageSrc) &&
            !string.IsNullOrWhiteSpace(surface.BorderHex))
        {
            if (!UiColor.TryParse(surface.BorderHex, out UiColor border))
            {
                throw new InvalidOperationException(
                    $"Narrative surface '{surface.SurfaceId}' has invalid border color '{surface.BorderHex}'.");
            }

            builder = builder.Border(1f, border);
        }

        if (string.IsNullOrWhiteSpace(surface.FrameImageSrc))
        {
            return builder;
        }

        string frameClass = surface.Kind == NarrativeFrontendSurfaceKind.ChoiceList
            ? "story-choice-frame"
            : "story-frame";
        return Ui.Panel(
                builder.Class("story-framed-body"),
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
        private const float StandingAspect = 1024f / 1536f;
        private const float StandingCardGap = 32f;
        private const float StandingCardMinWidth = 420f;

        private readonly NarrativeFrontendSurfaceModel _surface;

        public NarrativeSurfaceBindingScope(NarrativeFrontendSurfaceModel surface)
        {
            _surface = surface;
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
                "standingWidth" => _surface.PortraitSize * StandingAspect,
                "standingCardWidth" => Math.Max(
                    StandingCardMinWidth,
                    _surface.Width - (_surface.PortraitSize * StandingAspect) - StandingCardGap),
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
