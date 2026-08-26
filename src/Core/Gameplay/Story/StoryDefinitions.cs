using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.Story
{
    public sealed class StorySpeakerDefinition : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayNameToken { get; set; } = string.Empty;
        public string PortraitImageId { get; set; } = string.Empty;
    }

    public sealed class StoryLineDefinition : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string SpeakerId { get; set; } = string.Empty;
        public string TextToken { get; set; } = string.Empty;
        public List<PresentationTextArg> Args { get; set; } = new();
        public List<string> Tags { get; set; } = new();
    }

    public enum StoryPresentationBackend : byte
    {
        ScreenOverlay = 0,
        WorldProjected = 1,
        ScreenSubtitle = 2,
    }

    public sealed class StoryPresentationProfileDefinition : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public StoryPresentationBackend Backend { get; set; } = StoryPresentationBackend.ScreenOverlay;
        public string SurfaceKind { get; set; } = string.Empty;
        public string Anchor { get; set; } = string.Empty;
        public float Width { get; set; } = 720f;
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        public float WorldHeadOffsetYCm { get; set; } = 120f;
        public bool WaitForInput { get; set; }
        public bool DimBackdrop { get; set; }
        public string AccentHex { get; set; } = string.Empty;
        public string BackgroundHex { get; set; } = string.Empty;
        public string BorderHex { get; set; } = string.Empty;
        public string ForegroundHex { get; set; } = string.Empty;
        public string MutedHex { get; set; } = string.Empty;
    }

    public sealed class StoryDefinitionRegistry
    {
        private readonly Dictionary<string, StoryLineDefinition> _lines = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StoryPresentationProfileDefinition> _profiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StorySpeakerDefinition> _speakers = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<StoryLineDefinition> Lines => _lines.Values;
        public IReadOnlyCollection<StoryPresentationProfileDefinition> Profiles => _profiles.Values;
        public IReadOnlyCollection<StorySpeakerDefinition> Speakers => _speakers.Values;

        public void Clear()
        {
            _lines.Clear();
            _profiles.Clear();
            _speakers.Clear();
        }

        public void Register(StoryLineDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Id))
            {
                throw new InvalidOperationException("Story line id is required.");
            }

            if (string.IsNullOrWhiteSpace(definition.TextToken))
            {
                throw new InvalidOperationException($"Story line '{definition.Id}' requires textToken.");
            }

            _lines[definition.Id] = definition;
        }

        public void Register(StoryPresentationProfileDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Id))
            {
                throw new InvalidOperationException("Story presentation profile id is required.");
            }

            if (string.IsNullOrWhiteSpace(definition.SurfaceKind))
            {
                throw new InvalidOperationException($"Story presentation profile '{definition.Id}' requires surfaceKind.");
            }

            _profiles[definition.Id] = definition;
        }

        public void Register(StorySpeakerDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Id))
            {
                throw new InvalidOperationException("Story speaker id is required.");
            }

            if (string.IsNullOrWhiteSpace(definition.DisplayNameToken))
            {
                throw new InvalidOperationException($"Story speaker '{definition.Id}' requires displayNameToken.");
            }

            _speakers[definition.Id] = definition;
        }

        public bool TryGetLine(string lineId, out StoryLineDefinition definition)
            => _lines.TryGetValue(lineId ?? string.Empty, out definition!);

        public bool TryGetProfile(string profileId, out StoryPresentationProfileDefinition definition)
            => _profiles.TryGetValue(profileId ?? string.Empty, out definition!);

        public bool TryGetSpeaker(string speakerId, out StorySpeakerDefinition definition)
            => _speakers.TryGetValue(speakerId ?? string.Empty, out definition!);

        public StoryLineDefinition RequireLine(string lineId)
        {
            if (!TryGetLine(lineId, out StoryLineDefinition definition))
            {
                throw new InvalidOperationException(
                    $"Story line '{lineId}' is not registered. Author it under Story/lines.json.");
            }

            return definition;
        }

        public StoryPresentationProfileDefinition RequireProfile(string profileId)
        {
            if (!TryGetProfile(profileId, out StoryPresentationProfileDefinition definition))
            {
                throw new InvalidOperationException(
                    $"Story presentation profile '{profileId}' is not registered. Author it under Story/presentation_profiles.json.");
            }

            return definition;
        }

        public StorySpeakerDefinition RequireSpeaker(string speakerId)
        {
            if (!TryGetSpeaker(speakerId, out StorySpeakerDefinition definition))
            {
                throw new InvalidOperationException(
                    $"Story speaker '{speakerId}' is not registered. Author it under Story/speakers.json.");
            }

            return definition;
        }
    }
}
