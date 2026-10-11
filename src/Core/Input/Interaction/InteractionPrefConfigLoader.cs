using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Registry;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Loader for <c>Input/interaction_prefs.json</c>: the game-instance seed of the player-level
    /// default command intent + cast dispatch profile that map binding plants on every bound
    /// player representative lacking a <see cref="InteractionPref"/>. Catalog-declared DeepObject
    /// merge through the shared <see cref="ConfigPipeline"/>; id resolution against the
    /// installed profile registries is fail fast.
    /// </summary>
    public sealed class InteractionPrefConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public InteractionPrefConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        /// <summary>Load and validate the merged interaction preference seed config.</summary>
        public InteractionPrefsConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Input/interaction_prefs.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            var config = mergedObject.Deserialize<InteractionPrefsConfig>(JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
            Validate(config, relativePath);
            return config;
        }

        /// <summary>Structural fail-fast validation; id resolution happens in <see cref="ResolveSeed"/>.</summary>
        public static void Validate(InteractionPrefsConfig config, string source)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            InteractionPrefDefaultsDefinition defaults = config.Defaults
                ?? throw new InvalidOperationException($"Command preference config '{source}' must explicitly define defaults.");

            RequireTrimmedNonEmpty(defaults.CommandIntentId, $"{source}.defaults.commandIntentId");
            RequireTrimmedNonEmpty(defaults.CastDispatchProfileId, $"{source}.defaults.castDispatchProfileId");
            ValidateCastModeFields(defaults, source);
        }

        /// <summary>
        /// Resolve the seed ids: both the command intent profile and the cast dispatch profile
        /// must be installed; the seed ids are the registries' own id spaces (the spaces the
        /// arbiter, mounted contexts, and dispatch resolution resolve in).
        /// </summary>
        public static InteractionPrefSeed ResolveSeed(
            InteractionPrefsConfig config,
            CommandIntentProfileRegistry commandIntents,
            CastDispatchProfileRegistry castDispatchProfiles,
            StringIntRegistry castModes,
            AbilityDefinitionRegistry abilityDefinitions,
            string source = "Input/interaction_prefs.json")
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (castModes == null)
            {
                throw new ArgumentNullException(nameof(castModes));
            }

            if (abilityDefinitions == null)
            {
                throw new ArgumentNullException(nameof(abilityDefinitions));
            }

            InteractionPrefDefaultsDefinition defaults = config.Defaults
                ?? throw new InvalidOperationException($"Command preference config '{source}' must explicitly define defaults.");
            ValidateCastModeFields(defaults, source);

            string intentName = defaults.CommandIntentId;
            if (!commandIntents.ProfileIdRegistry.TryGetId(intentName, out int intentRegistryId) ||
                !commandIntents.IsInstalled(intentRegistryId))
            {
                throw new InvalidOperationException(
                    $"{source} defaults.commandIntentId references command intent profile '{intentName}' which is not installed.");
            }

            string dispatchName = defaults.CastDispatchProfileId;
            if (!castDispatchProfiles.ProfileIdRegistry.TryGetId(dispatchName, out int dispatchRegistryId) ||
                !castDispatchProfiles.IsInstalled(dispatchRegistryId))
            {
                throw new InvalidOperationException(
                    $"{source} defaults.castDispatchProfileId references cast dispatch profile '{dispatchName}' which is not installed.");
            }

            int castModeId = 0;
            string? castModeName = defaults.CastModeId;
            if (castModeName != null)
            {
                if (!castModes.TryGetId(castModeName, out castModeId))
                {
                    throw new InvalidOperationException(
                        $"{source} defaults.castModeId references cast mode '{castModeName}' which is not installed.");
                }
            }

            InteractionPrefAbilityCastModeSeed[] abilityCastModes = ResolveAbilityCastModes(
                defaults.AbilityCastModes,
                castModes,
                abilityDefinitions,
                source);
            return new InteractionPrefSeed(intentRegistryId, dispatchRegistryId, castModeId, abilityCastModes);
        }

        private static void ValidateCastModeFields(InteractionPrefDefaultsDefinition defaults, string source)
        {
            if (defaults == null)
            {
                throw new InvalidOperationException($"Command preference config '{source}' must explicitly define defaults.");
            }

            RequireOptionalTrimmedNonEmpty(defaults.CastModeId, $"{source}.defaults.castModeId");
            List<InteractionPrefAbilityCastModeDefinition>? pairs = defaults.AbilityCastModes;
            if (pairs == null)
            {
                return;
            }

            if (pairs.Count > InteractionPref.MaxAbilityOverrides)
            {
                throw new InvalidOperationException(
                    $"{source}.defaults.abilityCastModes exceeds {InteractionPref.MaxAbilityOverrides} entries.");
            }

            var seen = new HashSet<string>(pairs.Count, StringComparer.Ordinal);
            for (int i = 0; i < pairs.Count; i++)
            {
                InteractionPrefAbilityCastModeDefinition pair = pairs[i];
                if (pair == null)
                {
                    throw new InvalidOperationException($"{source}.defaults.abilityCastModes[{i}] must be an object.");
                }

                string abilityPath = $"{source}.defaults.abilityCastModes[{i}].abilityId";
                string modePath = $"{source}.defaults.abilityCastModes[{i}].castModeId";
                string abilityName = pair.AbilityId ?? string.Empty;
                string modeName = pair.CastModeId ?? string.Empty;
                RequireTrimmedNonEmpty(abilityName, abilityPath);
                RequireTrimmedNonEmpty(modeName, modePath);
                if (!seen.Add(abilityName))
                {
                    throw new InvalidOperationException($"{abilityPath} duplicates ability '{abilityName}'.");
                }
            }
        }

        private static InteractionPrefAbilityCastModeSeed[] ResolveAbilityCastModes(
            List<InteractionPrefAbilityCastModeDefinition>? pairs,
            StringIntRegistry castModes,
            AbilityDefinitionRegistry abilityDefinitions,
            string source)
        {
            if (pairs == null || pairs.Count == 0)
            {
                return Array.Empty<InteractionPrefAbilityCastModeSeed>();
            }

            var resolved = new InteractionPrefAbilityCastModeSeed[pairs.Count];
            for (int i = 0; i < pairs.Count; i++)
            {
                InteractionPrefAbilityCastModeDefinition pair = pairs[i]
                    ?? throw new InvalidOperationException($"{source}.defaults.abilityCastModes[{i}] must be an object.");
                string abilityName = pair.AbilityId
                    ?? throw new InvalidOperationException($"{source}.defaults.abilityCastModes[{i}].abilityId must be a non-empty string.");
                int abilityId = AbilityIdRegistry.GetId(abilityName);
                if (abilityId == AbilityIdRegistry.InvalidId || !abilityDefinitions.TryGet(abilityId, out _))
                {
                    throw new InvalidOperationException(
                        $"{source} defaults.abilityCastModes[{i}].abilityId references ability '{abilityName}' which is not installed.");
                }

                string modeName = pair.CastModeId
                    ?? throw new InvalidOperationException($"{source}.defaults.abilityCastModes[{i}].castModeId must be a non-empty string.");
                if (!castModes.TryGetId(modeName, out int modeId))
                {
                    throw new InvalidOperationException(
                        $"{source} defaults.abilityCastModes[{i}].castModeId references cast mode '{modeName}' which is not installed.");
                }

                resolved[i] = new InteractionPrefAbilityCastModeSeed(abilityId, modeId);
            }

            return resolved;
        }

        private static void RequireTrimmedNonEmpty(string? value, string path)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{path} must be a non-empty string.");
            }

            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path} must not contain leading or trailing whitespace.");
            }
        }

        private static void RequireOptionalTrimmedNonEmpty(string? value, string path)
        {
            if (value == null)
            {
                return;
            }

            RequireTrimmedNonEmpty(value, path);
        }
    }
}
