using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.Story;
using Ludots.Core.Presentation;

namespace NarrativeFrontendMod.Runtime;

/// <summary>
/// Maps Core story presentation frames (strings + imageId) into frontend surface models.
/// Image paths are resolved here — never in DialogueRuntime.
/// </summary>
public static class StoryPresentationFrontendAdapter
{
    public static NarrativeFrontendPageState ToPage(
        string ownerId,
        StoryPresentationFrame frame,
        PresentationDisplayResolver? display,
        string frameImageSrc = "")
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("Owner id is required.", nameof(ownerId));
        }

        if (frame.Surfaces.Count == 0)
        {
            return new NarrativeFrontendPageState(ownerId, "empty", Visible: false);
        }

        var surfaces = new List<NarrativeFrontendSurfaceModel>(frame.Surfaces.Count);
        for (int i = 0; i < frame.Surfaces.Count; i++)
        {
            surfaces.Add(ToSurface(frame.Surfaces[i], display, frameImageSrc));
        }

        string signature =
            $"{frame.Handle.StreamId}|{frame.Handle.Generation}|{frame.Surfaces.Count}|{frame.BackdropHex}";
        for (int i = 0; i < frame.Surfaces.Count; i++)
        {
            StoryPresentationSurface s = frame.Surfaces[i];
            signature += $"|{s.SurfaceKey}|{s.Body}|{s.ImageId}|{s.Progress01:0.00}|{s.Choices?.Count ?? 0}";
        }

        return new NarrativeFrontendPageState(
            ownerId,
            signature,
            Visible: true,
            BackdropHex: frame.BackdropHex,
            Surfaces: surfaces);
    }

    private static NarrativeFrontendSurfaceModel ToSurface(
        StoryPresentationSurface surface,
        PresentationDisplayResolver? display,
        string frameImageSrc)
    {
        NarrativeFrontendSurfaceKind kind = ParseKind(surface.SurfaceKind);
        NarrativeFrontendAnchor anchor = ParseAnchor(surface.Anchor);
        string portraitSrc = string.Empty;
        if (!string.IsNullOrWhiteSpace(surface.ImageId))
        {
            if (display == null)
            {
                throw new InvalidOperationException(
                    $"Story surface '{surface.SurfaceKey}' has imageId '{surface.ImageId}' but PresentationDisplayResolver is missing.");
            }

            portraitSrc = display.ResolveImageSourceOrThrow(surface.ImageId);
        }

        List<NarrativeFrontendSurfaceItem>? items = null;
        if (surface.Choices is { Count: > 0 })
        {
            items = new List<NarrativeFrontendSurfaceItem>(surface.Choices.Count);
            for (int i = 0; i < surface.Choices.Count; i++)
            {
                StoryPresentationChoice choice = surface.Choices[i];
                items.Add(new NarrativeFrontendSurfaceItem(
                    Label: choice.Text,
                    Caption: string.Empty,
                    Active: i == 0,
                    Shortcut: choice.Shortcut));
            }
        }

        return new NarrativeFrontendSurfaceModel(
            SurfaceId: surface.SurfaceKey,
            Kind: kind,
            Anchor: anchor,
            Title: surface.Title,
            Subtitle: surface.Subtitle,
            Body: surface.Body,
            Footer: surface.Footer,
            Items: items,
            Width: surface.Width,
            OffsetX: surface.OffsetX,
            OffsetY: surface.OffsetY,
            ZIndex: surface.ZIndex,
            WaitForInput: surface.WaitForInput,
            Skippable: surface.Skippable,
            Progress01: surface.Progress01,
            CountdownSeconds: surface.CountdownSeconds,
            AccentHex: surface.AccentHex,
            BackgroundHex: surface.BackgroundHex,
            BorderHex: surface.BorderHex,
            ForegroundHex: surface.ForegroundHex,
            MutedHex: surface.MutedHex,
            ImageId: surface.ImageId,
            PortraitSrc: portraitSrc,
            PortraitSize: surface.ImageSize,
            FrameImageSrc: frameImageSrc);
    }

    private static NarrativeFrontendSurfaceKind ParseKind(string surfaceKind)
    {
        return surfaceKind.Trim() switch
        {
            "OverlayDialogue" => NarrativeFrontendSurfaceKind.OverlayDialogue,
            "DialogueBubble" => NarrativeFrontendSurfaceKind.DialogueBubble,
            "StandingPortrait" => NarrativeFrontendSurfaceKind.StandingPortrait,
            "SubtitleBubble" => NarrativeFrontendSurfaceKind.SubtitleBubble,
            "ChoiceList" => NarrativeFrontendSurfaceKind.ChoiceList,
            "TransmissionOverlay" => NarrativeFrontendSurfaceKind.TransmissionOverlay,
            _ => throw new InvalidOperationException(
                $"Unknown story surfaceKind '{surfaceKind}' for NarrativeFrontend."),
        };
    }

    private static NarrativeFrontendAnchor ParseAnchor(string anchor)
    {
        return anchor.Trim() switch
        {
            "TopLeft" => NarrativeFrontendAnchor.TopLeft,
            "TopCenter" => NarrativeFrontendAnchor.TopCenter,
            "TopRight" => NarrativeFrontendAnchor.TopRight,
            "LeftCenter" => NarrativeFrontendAnchor.LeftCenter,
            "Center" => NarrativeFrontendAnchor.Center,
            "RightCenter" => NarrativeFrontendAnchor.RightCenter,
            "BottomLeft" => NarrativeFrontendAnchor.BottomLeft,
            "BottomCenter" => NarrativeFrontendAnchor.BottomCenter,
            "BottomRight" => NarrativeFrontendAnchor.BottomRight,
            _ => throw new InvalidOperationException(
                $"Unknown story presentation anchor '{anchor}' for NarrativeFrontend."),
        };
    }
}
