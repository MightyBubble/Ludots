using System;
using System.Collections.Generic;

namespace Ludots.Core.Navigation.AgentProfiles
{
    public sealed class AgentProfileRegistry
    {
        private readonly Dictionary<string, int> _indexById;
        private readonly List<AgentProfileConfig> _profiles;

        public AgentProfileRegistry(IReadOnlyList<AgentProfileConfig> profiles)
        {
            if (profiles == null || profiles.Count == 0)
            {
                throw new InvalidOperationException("AgentProfileRegistry requires at least one profile.");
            }

            _indexById = new Dictionary<string, int>(profiles.Count, StringComparer.Ordinal);
            _profiles = new List<AgentProfileConfig>(profiles.Count);

            for (int i = 0; i < profiles.Count; i++)
            {
                AgentProfileConfig profile = profiles[i]
                    ?? throw new InvalidOperationException($"AgentProfile[{i}] must be an object.");
                profile.Validate(i);
                if (!_indexById.TryAdd(profile.Id, _profiles.Count))
                {
                    throw new InvalidOperationException($"Duplicate AgentProfile id '{profile.Id}'.");
                }

                _profiles.Add(profile);
            }
        }

        public int Count => _profiles.Count;

        public AgentProfileConfig this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_profiles.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _profiles[index];
            }
        }

        public bool TryGet(string profileId, out AgentProfileConfig profile)
        {
            if (!string.IsNullOrWhiteSpace(profileId) &&
                string.Equals(profileId.Trim(), profileId, StringComparison.Ordinal) &&
                _indexById.TryGetValue(profileId, out int index))
            {
                profile = _profiles[index];
                return true;
            }

            profile = null!;
            return false;
        }

        public AgentProfileConfig Require(string profileId, string context)
        {
            if (TryGet(profileId, out AgentProfileConfig profile))
            {
                return profile;
            }

            throw new InvalidOperationException($"{context} references unknown agent profile '{profileId}'.");
        }
    }
}
