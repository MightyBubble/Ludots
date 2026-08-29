using System;
using Arch.Core;
using Ludots.Core.Registry;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// InteractionContextProfile registry (RFC-0065 CTX-6, §5.3). Profiles are declared in
    /// <c>Input/interaction_context_profiles.json</c> and installed as immutable rows with
    /// every id field resolved up front: profile ids and input context ids register into this
    /// registry's own spaces, collection keys register into the shared
    /// <c>EntityCollectionStore</c> key space, and filter / command intent names must already
    /// be registered in their kernel registries (install those first — unknown names fail fast
    /// here). Context mounting (<see cref="TryCreateActiveContext"/>) is allocation free after
    /// install because it only copies pre-resolved ints.
    /// </summary>
    public sealed class InteractionContextProfileRegistry
    {
        private readonly StringIntRegistry _profileIds;
        private readonly StringIntRegistry _inputContextIds;
        private InteractionContextProfileDefinition[] _profiles = new InteractionContextProfileDefinition[8];
        private int[] _collectionKeyIds = new int[8];
        private int[] _filterProfileIds = new int[8];
        private int[] _commandIntentProfileIds = new int[8];
        private int[] _inputContextIdsByProfile = new int[8];

        public InteractionContextProfileRegistry(StringIntRegistry profileIdRegistry)
        {
            _profileIds = profileIdRegistry ?? throw new ArgumentNullException(nameof(profileIdRegistry));
            _inputContextIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
        }

        /// <summary>Profile id space; ability declarations and mounts resolve profile names through it.</summary>
        public StringIntRegistry ProfileIdRegistry => _profileIds;

        /// <summary>
        /// IMC input context id space for profile-declared <c>inputContextId</c> values; the
        /// input context projection reads names back through it.
        /// </summary>
        public StringIntRegistry InputContextIdRegistry => _inputContextIds;

        /// <summary>
        /// Install every profile in the config, resolving id fields against the given spaces;
        /// fails fast on duplicates, unknown filter or command intent names.
        /// </summary>
        public void Install(
            InteractionContextProfilesConfig config,
            StringIntRegistry collectionKeyRegistry,
            StringIntRegistry filterProfileIdRegistry,
            StringIntRegistry commandIntentProfileIdRegistry)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (collectionKeyRegistry == null)
            {
                throw new ArgumentNullException(nameof(collectionKeyRegistry));
            }

            if (filterProfileIdRegistry == null)
            {
                throw new ArgumentNullException(nameof(filterProfileIdRegistry));
            }

            if (commandIntentProfileIdRegistry == null)
            {
                throw new ArgumentNullException(nameof(commandIntentProfileIdRegistry));
            }

            InteractionContextProfileConfigLoader.Validate(config, nameof(InteractionContextProfilesConfig));
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                InstallProfile(
                    config.Profiles[i],
                    collectionKeyRegistry,
                    filterProfileIdRegistry,
                    commandIntentProfileIdRegistry);
            }
        }

        /// <summary>True when the profile id has been installed.</summary>
        public bool IsInstalled(int profileId)
        {
            return profileId > 0 && profileId < _profiles.Length && _profiles[profileId] != null;
        }

        /// <summary>Installed profile row by id; false when not installed.</summary>
        public bool TryGetDefinition(int profileId, out InteractionContextProfileDefinition definition)
        {
            if (!IsInstalled(profileId))
            {
                definition = null!;
                return false;
            }

            definition = _profiles[profileId];
            return true;
        }

        /// <summary>
        /// Materialize the entity-mounted active context for the profile with
        /// <paramref name="contextEntity"/> as the carrier entity and
        /// <paramref name="source"/> as the mounting lifecycle. Returns false when the profile
        /// id is not installed. Allocation free after install.
        /// </summary>
        public bool TryCreateActiveContext(
            int profileId,
            Entity contextEntity,
            ActiveInteractionContextSource source,
            out ActiveInteractionContext context)
        {
            if (!IsInstalled(profileId))
            {
                context = default;
                return false;
            }

            context = new ActiveInteractionContext
            {
                ContextId = profileId,
                ContextEntity = contextEntity,
                ActiveCollectionKeyId = _collectionKeyIds[profileId],
                FilterProfileId = _filterProfileIds[profileId],
                CommandIntentProfileId = _commandIntentProfileIds[profileId],
                InputContextId = _inputContextIdsByProfile[profileId],
                Source = source,
            };
            return true;
        }

        /// <summary>
        /// Steady-state routing anchor: the reserved default profile's resolved collection key
        /// and filter profile ids (the data-declared home of the retired engine default frame).
        /// Returns false when the default profile is not installed.
        /// </summary>
        public bool TryGetSteadyStateRouting(out int collectionKeyId, out int filterProfileId)
        {
            int defaultProfileId = _profileIds.GetId(InteractionContextIds.Default);
            if (!IsInstalled(defaultProfileId))
            {
                collectionKeyId = 0;
                filterProfileId = 0;
                return false;
            }

            collectionKeyId = _collectionKeyIds[defaultProfileId];
            filterProfileId = _filterProfileIds[defaultProfileId];
            return true;
        }

        private int _inputContextIdsFor(int profileId)
        {
            string inputContextId = _profiles[profileId].InputContextId;
            return string.IsNullOrWhiteSpace(inputContextId)
                ? _inputContextIds.InvalidId
                : _inputContextIds.Register(inputContextId);
        }

        private void InstallProfile(
            InteractionContextProfileDefinition definition,
            StringIntRegistry collectionKeyRegistry,
            StringIntRegistry filterProfileIdRegistry,
            StringIntRegistry commandIntentProfileIdRegistry)
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
                Array.Resize(ref _collectionKeyIds, next);
                Array.Resize(ref _filterProfileIds, next);
                Array.Resize(ref _commandIntentProfileIds, next);
                Array.Resize(ref _inputContextIdsByProfile, next);
            }

            int filterProfileId = ResolveDeclaredId(
                filterProfileIdRegistry,
                definition.FilterProfileId,
                definition.Id,
                nameof(definition.FilterProfileId),
                "filter profile");
            int commandIntentProfileId = ResolveDeclaredId(
                commandIntentProfileIdRegistry,
                definition.CommandIntentId,
                definition.Id,
                nameof(definition.CommandIntentId),
                "command intent profile");

            _profiles[profileId] = definition;
            _collectionKeyIds[profileId] = collectionKeyRegistry.Register(definition.ActiveCollectionKey);
            _filterProfileIds[profileId] = filterProfileId;
            _commandIntentProfileIds[profileId] = commandIntentProfileId;
            _inputContextIdsByProfile[profileId] = _inputContextIdsFor(profileId);
        }

        private static int ResolveDeclaredId(
            StringIntRegistry registry,
            string declaredName,
            string profileId,
            string fieldName,
            string kindLabel)
        {
            if (string.IsNullOrWhiteSpace(declaredName))
            {
                return registry.InvalidId;
            }

            if (!registry.TryGetId(declaredName, out int id))
            {
                throw new InvalidOperationException(
                    $"Interaction context profile '{profileId}' {fieldName} references unknown {kindLabel} '{declaredName}'.");
            }

            return id;
        }
    }
}
