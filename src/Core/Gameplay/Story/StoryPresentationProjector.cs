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
        private uint _generation;

        public StoryPresentationProjector(StoryDefinitionRegistry story)
        {
            _story = story ?? throw new ArgumentNullException(nameof(story));
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
            if (string.Equals(surfaceKind, StoryPresentationSurfaceKinds.StandingPortrait, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(imageId))
            {
                throw new InvalidOperationException(
                    $"Presentation profile '{view.PresentationProfile}' (StandingPortrait) requires speaker '{view.SpeakerId}' standingImageId.");
            }

            float offsetX = profile.OffsetX;
            float offsetY = profile.OffsetY;
            if (string.IsNullOrWhiteSpace(profile.Anchor))
            {
                throw new InvalidOperationException(
                    $"Presentation profile '{view.PresentationProfile}' requires anchor.");
            }

            if (profile.Width <= 0f)
            {
                throw new InvalidOperationException(
                    $"Presentation profile '{view.PresentationProfile}' requires width > 0.");
            }

            string anchor = profile.Anchor.Trim();
            if (profile.Backend == StoryPresentationBackend.WorldProjected)
            {
                if (!worldScreenX.HasValue || !worldScreenY.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Presentation profile '{view.PresentationProfile}' (WorldProjected) requires projected speaker screen coordinates.");
                }

                anchor = "TopLeft";
                offsetX = worldScreenX.Value + profile.OffsetX;
                if (profile.WorldScreenHeadOffsetPx <= 0f)
                {
                    throw new InvalidOperationException(
                        $"Presentation profile '{view.PresentationProfile}' requires worldScreenHeadOffsetPx > 0 (WorldProjected).");
                }

                offsetY = worldScreenY.Value - profile.WorldScreenHeadOffsetPx + profile.OffsetY;
            }

            if (profile.ImageSize <= 0f)
            {
                throw new InvalidOperationException(
                    $"Presentation profile '{view.PresentationProfile}' requires imageSize > 0.");
            }

            if (profile.DimBackdrop && string.IsNullOrWhiteSpace(profile.BackdropHex))
            {
                throw new InvalidOperationException(
                    $"Presentation profile '{view.PresentationProfile}' requires backdropHex when dimBackdrop is set.");
            }

            float imageSize = profile.ImageSize;
            var surfaces = new List<StoryPresentationSurface>(2)
            {
                new StoryPresentationSurface(
                    SurfaceKey: $"dialogue.{view.DialogueId}.{surfaceKind}",
                    SurfaceKind: surfaceKind,
                    LayoutId: profile.LayoutId,
                    StyleClass: profile.StyleClass,
                    Anchor: anchor,
                    Title: string.IsNullOrWhiteSpace(view.ResolvedSpeakerName) ? view.SpeakerId : view.ResolvedSpeakerName,
                    Body: view.ResolvedText ?? string.Empty,
                    ImageId: imageId,
                    ImageSize: imageSize,
                    Width: profile.Width,
                    OffsetX: offsetX,
                    OffsetY: offsetY,
                    ZIndex: profile.ZIndex,
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
                    MutedHex: profile.MutedHex,
                    BodyRuns: view.BodyRuns)
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

                if (string.IsNullOrWhiteSpace(profile.ChoiceAnchor) || profile.ChoiceWidth <= 0f)
                {
                    throw new InvalidOperationException(
                        $"Presentation profile '{view.PresentationProfile}' requires choiceAnchor and choiceWidth when the node offers choices.");
                }

                if (string.IsNullOrWhiteSpace(profile.ChoiceLayoutId))
                {
                    throw new InvalidOperationException(
                        $"Presentation profile '{view.PresentationProfile}' requires choiceLayoutId when the node offers choices.");
                }

                // Choice list is a companion surface: geometry from the same profile (single writer).
                surfaces.Add(new StoryPresentationSurface(
                    SurfaceKey: $"dialogue.{view.DialogueId}.ChoiceList",
                    SurfaceKind: StoryPresentationSurfaceKinds.ChoiceList,
                    LayoutId: profile.ChoiceLayoutId,
                    StyleClass: string.Empty,
                    Anchor: profile.ChoiceAnchor,
                    Title: string.Empty,
                    Width: profile.ChoiceWidth,
                    OffsetY: profile.ChoiceOffsetY,
                    ZIndex: profile.ChoiceZIndex,
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
                profile.DimBackdrop ? profile.BackdropHex : string.Empty,
                surfaces);
        }

        public StoryPresentationFrame ProjectSequence(SequenceView view)
        {
            ArgumentNullException.ThrowIfNull(view);
            SequenceSubtitleView? subtitle = view.ActiveSubtitles.Count > 0 ? view.ActiveSubtitles[0] : null;
            if (subtitle == null)
            {
                _generation++;
                return new StoryPresentationFrame(
                    new StoryPresentationStreamHandle(view.SequenceId, _generation),
                    StoryPresentationStreamKind.Sequence,
                    view.SequenceId,
                    string.Empty,
                    Array.Empty<StoryPresentationSurface>());
            }

            if (string.IsNullOrWhiteSpace(subtitle.PresentationProfile))
            {
                throw new InvalidOperationException(
                    $"Sequence '{view.SequenceId}' active subtitle requires presentationProfile.");
            }

            string profileId = subtitle.PresentationProfile.Trim();
            StoryPresentationProfileDefinition profile = _story.RequireProfile(profileId);
            string surfaceKind = profile.SurfaceKind.Trim();
            ValidateSurfaceKind(surfaceKind, profileId);

            if (string.IsNullOrWhiteSpace(profile.Anchor))
            {
                throw new InvalidOperationException(
                    $"Presentation profile '{profileId}' requires anchor.");
            }

            if (profile.Width <= 0f)
            {
                throw new InvalidOperationException(
                    $"Presentation profile '{profileId}' requires width > 0.");
            }

            string title = subtitle.ResolvedSpeakerName;
            string body = subtitle.ResolvedText ?? string.Empty;
            float progress01 = subtitle.Duration > 0f
                ? Math.Clamp(subtitle.LocalElapsed / subtitle.Duration, 0f, 1f)
                : -1f;
            float countdown = Math.Max(0f, subtitle.Duration - subtitle.LocalElapsed);

            string imageId = string.Empty;
            if (_story.TryGetSpeaker(subtitle.SpeakerId, out StorySpeakerDefinition speaker))
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
                        LayoutId: profile.LayoutId,
                        StyleClass: profile.StyleClass,
                        Anchor: profile.Anchor.Trim(),
                        Title: title,
                        Body: body,
                        ImageId: imageId,
                        ImageSize: profile.ImageSize,
                        Width: profile.Width,
                        OffsetX: profile.OffsetX,
                        OffsetY: profile.OffsetY,
                        ZIndex: profile.ZIndex,
                        Progress01: progress01,
                        CountdownSeconds: countdown,
                        Skippable: surfaceKind is StoryPresentationSurfaceKinds.SubtitleBubble
                            or StoryPresentationSurfaceKinds.TransmissionOverlay,
                        AccentHex: profile.AccentHex,
                        BackgroundHex: profile.BackgroundHex,
                        BorderHex: profile.BorderHex,
                        ForegroundHex: profile.ForegroundHex,
                        MutedHex: profile.MutedHex)
                });
        }

        private static string ResolveImageId(string surfaceKind, DialogueView view)
        {
            if (string.Equals(surfaceKind, StoryPresentationSurfaceKinds.StandingPortrait, StringComparison.OrdinalIgnoreCase))
            {
                return view.StandingImageId ?? string.Empty;
            }

            return view.PortraitImageId ?? string.Empty;
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
                case StoryPresentationSurfaceKinds.OverlayDialogue:
                case StoryPresentationSurfaceKinds.DialogueBubble:
                case StoryPresentationSurfaceKinds.StandingPortrait:
                case StoryPresentationSurfaceKinds.SubtitleBubble:
                case StoryPresentationSurfaceKinds.ChoiceList:
                case StoryPresentationSurfaceKinds.TransmissionOverlay:
                    return;
                default:
                    throw new InvalidOperationException(
                        $"Story presentation profile '{profileId}' surfaceKind '{surfaceKind}' is unknown.");
            }
        }
    }
}
