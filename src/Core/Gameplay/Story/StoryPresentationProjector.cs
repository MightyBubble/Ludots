using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.Dialogue;
using Ludots.Core.Gameplay.Sequencer;
using Ludots.Core.Presentation;

namespace Ludots.Core.Gameplay.Story
{
    /// <summary>
    /// Projects dialogue/sequence session views into a string-bag presentation frame.
    /// Profile → surfaceKind/anchor come from Story/presentation_profiles.json (fail-closed).
    /// Does not resolve image paths — frontend resolves <see cref="StoryPresentationSurface.ImageId"/>.
    /// </summary>
    public sealed class StoryPresentationProjector
    {
        private readonly StoryDefinitionRegistry _story;
        private readonly PresentationDisplayResolver? _display;
        private uint _generation;

        public StoryPresentationProjector(
            StoryDefinitionRegistry story,
            PresentationDisplayResolver? display = null)
        {
            _story = story ?? throw new ArgumentNullException(nameof(story));
            _display = display;
        }

        public StoryPresentationFrame ProjectDialogue(
            DialogueView view,
            float? worldScreenX = null,
            float? worldScreenY = null)
        {
            ArgumentNullException.ThrowIfNull(view);
            if (string.IsNullOrWhiteSpace(view.PresentationProfile))
            {
                throw new InvalidOperationException(
                    $"Dialogue '{view.DialogueId}' node '{view.NodeId}' requires presentationProfile.");
            }

            StoryPresentationProfileDefinition profile = _story.RequireProfile(view.PresentationProfile);
            string surfaceKind = profile.SurfaceKind.Trim();
            ValidateSurfaceKind(surfaceKind, view.PresentationProfile);

            string imageId = ResolveImageId(surfaceKind, view);
            if (string.Equals(surfaceKind, "StandingPortrait", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(imageId))
            {
                throw new InvalidOperationException(
                    $"Presentation profile '{view.PresentationProfile}' (StandingPortrait) requires speaker '{view.SpeakerId}' standingImageId.");
            }

            float offsetX = profile.OffsetX;
            float offsetY = profile.OffsetY;
            string anchor = string.IsNullOrWhiteSpace(profile.Anchor) ? "BottomCenter" : profile.Anchor.Trim();
            if (profile.Backend == StoryPresentationBackend.WorldProjected)
            {
                if (!worldScreenX.HasValue || !worldScreenY.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Presentation profile '{view.PresentationProfile}' (WorldProjected) requires projected speaker screen coordinates.");
                }

                anchor = "TopLeft";
                offsetX = worldScreenX.Value;
                offsetY = worldScreenY.Value - 96f;
            }

            float imageSize = ImageSizeFor(surfaceKind);
            var surfaces = new List<StoryPresentationSurface>(2)
            {
                new StoryPresentationSurface(
                    SurfaceKey: $"dialogue.{view.DialogueId}.{surfaceKind}",
                    SurfaceKind: surfaceKind,
                    Anchor: anchor,
                    Title: string.IsNullOrWhiteSpace(view.ResolvedSpeakerName) ? view.SpeakerId : view.ResolvedSpeakerName,
                    Body: view.ResolvedText ?? string.Empty,
                    ImageId: imageId,
                    ImageSize: imageSize,
                    Width: profile.Width > 0f ? profile.Width : 720f,
                    OffsetX: offsetX,
                    OffsetY: offsetY,
                    ZIndex: 50,
                    WaitForInput: view.WaitForInput || profile.WaitForInput,
                    DimBackdrop: profile.DimBackdrop,
                    Progress01: view.Progress01,
                    CountdownSeconds: view.AutoAdvanceSeconds > 0f
                        ? Math.Max(0f, view.AutoAdvanceSeconds - view.ElapsedSeconds)
                        : 0f,
                    AccentHex: profile.AccentHex,
                    BackgroundHex: profile.BackgroundHex,
                    BorderHex: profile.BorderHex,
                    ForegroundHex: profile.ForegroundHex,
                    MutedHex: profile.MutedHex)
            };

            if (view.Choices.Count > 0)
            {
                var choices = new List<StoryPresentationChoice>(view.Choices.Count);
                for (int i = 0; i < view.Choices.Count; i++)
                {
                    DialogueChoiceView choice = view.Choices[i];
                    choices.Add(new StoryPresentationChoice(
                        choice.ChoiceId,
                        choice.ResolvedText,
                        (i + 1).ToString()));
                }

                surfaces.Add(new StoryPresentationSurface(
                    SurfaceKey: $"dialogue.{view.DialogueId}.ChoiceList",
                    SurfaceKind: "ChoiceList",
                    Anchor: "BottomCenter",
                    Title: "选项",
                    Width: 520f,
                    OffsetY: 120f,
                    ZIndex: 55,
                    Choices: choices,
                    AccentHex: profile.AccentHex,
                    BackgroundHex: profile.BackgroundHex,
                    BorderHex: profile.BorderHex,
                    ForegroundHex: profile.ForegroundHex,
                    MutedHex: profile.MutedHex));
            }

            _generation++;
            return new StoryPresentationFrame(
                new StoryPresentationStreamHandle(view.DialogueId, _generation),
                StoryPresentationStreamKind.Dialogue,
                view.DialogueId,
                profile.DimBackdrop ? "#00000099" : string.Empty,
                surfaces);
        }

        public StoryPresentationFrame ProjectSequence(SequenceView view, bool transmission = false)
        {
            ArgumentNullException.ThrowIfNull(view);
            SequenceSubtitleView? subtitle = view.ActiveSubtitles.Count > 0 ? view.ActiveSubtitles[0] : null;
            string profileId = subtitle != null && !string.IsNullOrWhiteSpace(subtitle.PresentationProfile)
                ? subtitle.PresentationProfile
                : "story.immersive_subtitle";
            StoryPresentationProfileDefinition profile = _story.RequireProfile(profileId);
            string surfaceKind = transmission ? "TransmissionOverlay" : profile.SurfaceKind.Trim();
            ValidateSurfaceKind(surfaceKind, profileId);

            string title = subtitle != null
                ? ResolveSpeakerDisplayName(subtitle.SpeakerId)
                : view.DisplayName;
            string body = subtitle?.ResolvedText ?? string.Empty;
            float progress01 = subtitle != null && subtitle.Duration > 0f
                ? Math.Clamp(subtitle.LocalElapsed / subtitle.Duration, 0f, 1f)
                : -1f;
            float countdown = subtitle != null
                ? Math.Max(0f, subtitle.Duration - subtitle.LocalElapsed)
                : 0f;

            string imageId = string.Empty;
            if (subtitle != null &&
                _story.TryGetSpeaker(subtitle.SpeakerId, out StorySpeakerDefinition speaker))
            {
                imageId = speaker.PortraitImageId ?? string.Empty;
            }

            _generation++;
            return new StoryPresentationFrame(
                new StoryPresentationStreamHandle(view.SequenceId, _generation),
                StoryPresentationStreamKind.Sequence,
                view.SequenceId,
                string.Empty,
                new[]
                {
                    new StoryPresentationSurface(
                        SurfaceKey: $"sequence.{view.SequenceId}.{surfaceKind}",
                        SurfaceKind: surfaceKind,
                        Anchor: string.IsNullOrWhiteSpace(profile.Anchor) ? "BottomCenter" : profile.Anchor.Trim(),
                        Title: title,
                        Body: body,
                        ImageId: imageId,
                        ImageSize: 72f,
                        Width: profile.Width > 0f ? profile.Width : 720f,
                        OffsetX: profile.OffsetX,
                        OffsetY: profile.OffsetY,
                        ZIndex: 45,
                        Progress01: progress01,
                        CountdownSeconds: countdown,
                        AccentHex: profile.AccentHex,
                        BackgroundHex: profile.BackgroundHex,
                        BorderHex: profile.BorderHex,
                        ForegroundHex: profile.ForegroundHex,
                        MutedHex: profile.MutedHex)
                });
        }

        private static string ResolveImageId(string surfaceKind, DialogueView view)
        {
            if (string.Equals(surfaceKind, "StandingPortrait", StringComparison.OrdinalIgnoreCase))
            {
                return view.StandingImageId ?? string.Empty;
            }

            return view.PortraitImageId ?? string.Empty;
        }

        private string ResolveSpeakerDisplayName(string speakerId)
        {
            if (string.IsNullOrWhiteSpace(speakerId) ||
                !_story.TryGetSpeaker(speakerId, out StorySpeakerDefinition speaker))
            {
                return speakerId ?? string.Empty;
            }

            if (_display != null)
            {
                return _display.FormatTokenOrThrow(speaker.DisplayNameToken);
            }

            return speaker.DisplayNameToken;
        }

        private static float ImageSizeFor(string surfaceKind)
        {
            if (string.Equals(surfaceKind, "StandingPortrait", StringComparison.OrdinalIgnoreCase))
            {
                return 980f;
            }

            if (string.Equals(surfaceKind, "OverlayDialogue", StringComparison.OrdinalIgnoreCase))
            {
                return 112f;
            }

            return 84f;
        }

        private static void ValidateSurfaceKind(string surfaceKind, string profileId)
        {
            if (string.IsNullOrWhiteSpace(surfaceKind))
            {
                throw new InvalidOperationException(
                    $"Story presentation profile '{profileId}' requires surfaceKind.");
            }

            switch (surfaceKind)
            {
                case "OverlayDialogue":
                case "DialogueBubble":
                case "StandingPortrait":
                case "SubtitleBubble":
                case "ChoiceList":
                case "TransmissionOverlay":
                    return;
                default:
                    throw new InvalidOperationException(
                        $"Story presentation profile '{profileId}' surfaceKind '{surfaceKind}' is unknown.");
            }
        }
    }
}
