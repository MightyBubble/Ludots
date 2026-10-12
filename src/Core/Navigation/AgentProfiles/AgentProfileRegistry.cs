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

        /// <summary>
        /// Bake body for a capability profile. The agent whose id equals the capability id wins,
        /// so extra bodies that share the capability do not replace that body's radius.
        /// Several referencers and no id match is ambiguous.
        /// </summary>
        public AgentProfileConfig ResolveForNavProfile(string navProfileId, string context)
        {
            AgentProfileConfig sameId = null;
            AgentProfileConfig only = null;
            int referencers = 0;
            for (int i = 0; i < _profiles.Count; i++)
            {
                AgentProfileConfig agent = _profiles[i];
                if (!string.Equals(agent.NavProfileId, navProfileId, StringComparison.Ordinal))
                {
                    continue;
                }

                referencers++;
                only = agent;
                if (string.Equals(agent.Id, navProfileId, StringComparison.Ordinal))
                {
                    sameId = agent;
                }
            }

            if (sameId != null)
            {
                return sameId;
            }

            if (referencers == 1)
            {
                return only;
            }

            if (referencers == 0)
            {
                throw new InvalidOperationException($"{context} nav profile '{navProfileId}' is not referenced by any agent profile.");
            }

            throw new InvalidOperationException(
                $"{context} nav profile '{navProfileId}' is referenced by {referencers} agent profiles and none shares its id.");
        }
    }
}
