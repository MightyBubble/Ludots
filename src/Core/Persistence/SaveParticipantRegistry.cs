using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ludots.Core.Persistence
{
    public sealed class SaveParticipantRegistry
    {
        private readonly Dictionary<string, ISaveParticipant> _participants =
            new(StringComparer.Ordinal);

        private static readonly HashSet<string> RetiredDomains = new(StringComparer.Ordinal)
        {
            "quests",
        };

        public IReadOnlyCollection<ISaveParticipant> Participants => _participants.Values;

        public void Register(ISaveParticipant participant)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            if (string.IsNullOrWhiteSpace(participant.DomainKey))
            {
                throw new SaveContextException("Save participant domain key must not be empty.");
            }

            if (_participants.ContainsKey(participant.DomainKey))
            {
                throw new SaveContextException(
                    $"Save participant domain '{participant.DomainKey}' is duplicate.");
            }

            _participants.Add(participant.DomainKey, participant);
        }

        public JsonObject CaptureDomains()
        {
            var domains = new JsonObject();
            foreach (ISaveParticipant participant in _participants.Values)
            {
                domains[participant.DomainKey] = participant.CaptureState();
            }

            return domains;
        }

        public void RestoreDomains(JsonObject domains)
        {
            if (domains == null) throw new ArgumentNullException(nameof(domains));

            foreach (KeyValuePair<string, JsonNode?> pair in domains)
            {
                if (!_participants.TryGetValue(pair.Key, out ISaveParticipant? participant))
                {
                    if (RetiredDomains.Contains(pair.Key))
                    {
                        throw new SaveContextException(
                            $"Save domain '{pair.Key}' belongs to a retired subsystem (Quest was removed after the one-time migration to Task; explicit save version break). This save cannot be loaded; start a new game.");
                    }

                    throw new SaveContextException(
                        $"Save domains contain unknown domain '{pair.Key}'.");
                }

                participant.RestoreState(pair.Value?.DeepClone() ?? new JsonObject());
            }

            foreach (ISaveParticipant participant in _participants.Values)
            {
                if (!domains.ContainsKey(participant.DomainKey))
                {
                    throw new SaveContextException(
                        $"Save domains missing required domain '{participant.DomainKey}'.");
                }
            }
        }
    }
}
