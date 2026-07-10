using System;
using Arch.Core;
using Ludots.Core.Registry;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// InteractionContextProfile registry (RFC-0065 CTX-6, §5.3). Profiles are declared in
    /// <c>Input/interaction_context_profiles.json</c> and installed as immutable rows; frame pushers
    /// (ability exec lifecycle, cast commit ops) materialize an
    /// <see cref="InteractionContextFrameDescriptor"/> per push. Descriptor creation is
    /// allocation free after install (strings are pre-trimmed rows).
    /// </summary>
    public sealed class InteractionContextProfileRegistry
    {
        private readonly StringIntRegistry _profileIds;
        private InteractionContextProfileDefinition[] _profiles = new InteractionContextProfileDefinition[8];

        public InteractionContextProfileRegistry(StringIntRegistry profileIdRegistry)
        {
            _profileIds = profileIdRegistry ?? throw new ArgumentNullException(nameof(profileIdRegistry));
        }

        /// <summary>Profile id space; frame pushers resolve profile names through it.</summary>
        public StringIntRegistry ProfileIdRegistry => _profileIds;

        /// <summary>Install every profile in the config; fails fast on duplicates.</summary>
        public void Install(InteractionContextProfilesConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            InteractionContextProfileConfigLoader.Validate(config, nameof(InteractionContextProfilesConfig));
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                InstallProfile(config.Profiles[i]);
            }
        }

        /// <summary>True when the profile id has been installed.</summary>
        public bool IsInstalled(int profileId)
        {
            return profileId > 0 && profileId < _profiles.Length && _profiles[profileId] != null;
        }

        /// <summary>
        /// Materialize a frame descriptor for the profile with <paramref name="contextEntity"/> as
        /// the owning entity (e.g. the ability exec instance carrier; default for client-initiated
        /// pushes). Returns false when the profile id is not installed.
        /// </summary>
        public bool TryCreateFrameDescriptor(int profileId, Entity contextEntity, out InteractionContextFrameDescriptor descriptor)
        {
            if (!IsInstalled(profileId))
            {
                descriptor = default;
                return false;
            }

            InteractionContextProfileDefinition profile = _profiles[profileId];
            descriptor = new InteractionContextFrameDescriptor(
                profile.Id,
                profile.ActiveCollectionKey,
                profile.ActiveEntityViewKey,
                contextEntity,
                profile.FilterProfileId ?? string.Empty,
                profile.CommandIntentId ?? string.Empty,
                profile.InputContextId ?? string.Empty);
            return true;
        }

        private void InstallProfile(InteractionContextProfileDefinition definition)
        {
            int profileId = _profileIds.Register(definition.Id);
            if (profileId < _profiles.Length && _profiles[profileId] != null)
            {
                throw new InvalidOperationException($"Interaction context profile '{definition.Id}' is already installed.");
            }

            if (profileId >= _profiles.Length)
            {
                int next = _profiles.Length;
                while (next <= profileId)
                {
                    next *= 2;
                }

                Array.Resize(ref _profiles, next);
            }

            _profiles[profileId] = definition;
        }
    }
}
