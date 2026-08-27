using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.Sequencer
{
    public enum SequenceTrackType : byte
    {
        Camera = 0,
        Subtitle = 1,
        Signal = 2,
    }

    public enum SequencePausePolicy : byte
    {
        Independent = 0,
        FollowWorld = 1,
    }

    public sealed class SequenceClockDefinition
    {
        public float Rate { get; set; } = 1f;
        public SequencePausePolicy PausePolicy { get; set; } = SequencePausePolicy.Independent;
    }

    public sealed class SequenceTrackDefinition
    {
        public SequenceTrackType Type { get; set; } = SequenceTrackType.Signal;
        public string Profile { get; set; } = string.Empty;
        public string LineId { get; set; } = string.Empty;
        public string PresentationProfile { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
        public string ActionGraphId { get; set; } = string.Empty;
        public float Start { get; set; }
        public float Duration { get; set; }
    }

    public sealed class SequenceDefinition : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool ClearCameraOnComplete { get; set; } = true;
        public SequenceClockDefinition Clock { get; set; } = new();
        public List<SequenceTrackDefinition> Tracks { get; set; } = new();
    }

    public sealed class SequenceDefinitionRegistry
    {
        private readonly Dictionary<string, SequenceDefinition> _sequences = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<SequenceDefinition> Sequences => _sequences.Values;

        public void Clear() => _sequences.Clear();

        public void Register(SequenceDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Id))
            {
                throw new InvalidOperationException("Sequence id is required.");
            }

            if (definition.Tracks == null || definition.Tracks.Count == 0)
            {
                throw new InvalidOperationException($"Sequence '{definition.Id}' requires at least one track.");
            }

            for (int i = 0; i < definition.Tracks.Count; i++)
            {
                ValidateTrack(definition.Id, definition.Tracks[i], i);
            }

            _sequences[definition.Id] = definition;
        }

        public bool TryGet(string sequenceId, out SequenceDefinition definition)
            => _sequences.TryGetValue(sequenceId ?? string.Empty, out definition!);

        public SequenceDefinition Require(string sequenceId)
        {
            if (!TryGet(sequenceId, out SequenceDefinition definition))
            {
                throw new InvalidOperationException(
                    $"Sequence '{sequenceId}' is not registered. Author it under Sequencer/sequences.json.");
            }

            return definition;
        }

        private static void ValidateTrack(string sequenceId, SequenceTrackDefinition track, int index)
        {
            if (track.Start < 0f)
            {
                throw new InvalidOperationException($"Sequence '{sequenceId}' track[{index}] start must be >= 0.");
            }

            switch (track.Type)
            {
                case SequenceTrackType.Camera:
                    if (string.IsNullOrWhiteSpace(track.Profile))
                    {
                        throw new InvalidOperationException($"Sequence '{sequenceId}' Camera track[{index}] requires profile.");
                    }

                    if (track.Duration <= 0f)
                    {
                        throw new InvalidOperationException($"Sequence '{sequenceId}' Camera track[{index}] requires duration > 0.");
                    }

                    break;
                case SequenceTrackType.Subtitle:
                    if (string.IsNullOrWhiteSpace(track.LineId) || string.IsNullOrWhiteSpace(track.PresentationProfile))
                    {
                        throw new InvalidOperationException($"Sequence '{sequenceId}' Subtitle track[{index}] requires lineId and presentationProfile.");
                    }

                    if (track.Duration <= 0f)
                    {
                        throw new InvalidOperationException($"Sequence '{sequenceId}' Subtitle track[{index}] requires duration > 0.");
                    }

                    break;
                case SequenceTrackType.Signal:
                    if (string.IsNullOrWhiteSpace(track.ActionGraphId))
                    {
                        throw new InvalidOperationException($"Sequence '{sequenceId}' Signal track[{index}] requires actionGraphId.");
                    }

                    break;
                default:
                    throw new InvalidOperationException($"Sequence '{sequenceId}' track[{index}] has unsupported type '{track.Type}'.");
            }
        }
    }
}
