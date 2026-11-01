using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Loader for <c>Input/interaction_prefs.json</c>: declared preference keys plus the
    /// game-instance seed planted on every bound player representative that lacks
    /// <see cref="InteractionPref"/>. Catalog policy stays DeepObject. <c>keys</c> merge by
    /// <c>id</c> and <c>defaults.prefs</c> merge by <c>key</c>; later fields win. Id resolution
    /// against installed registries is fail fast.
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

        /// <summary>Load and validate the merged interaction preference config.</summary>
        public InteractionPrefsConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Input/interaction_prefs.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            List<ConfigFragment> fragments = _pipeline.CollectFragmentsWithSources(in entry);
            if (fragments.Count == 0)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            JsonObject mergedObject = ConfigMerger.MergeDeepObjectArrayById(fragments, in entry, "keys", report);
            MergePlayerPrefSeeds(mergedObject, fragments, relativePath);
            var config = mergedObject.Deserialize<InteractionPrefsConfig>(JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
            Validate(config, relativePath);
            return config;
        }

        /// <summary>
        /// <c>defaults.prefs</c> is an array inside an object, so DeepObject would let the last
        /// fragment that mentions it replace the whole seed. Rebuild it by <c>key</c>; later wins.
        /// </summary>
        private static void MergePlayerPrefSeeds(JsonObject merged, IReadOnlyList<ConfigFragment> fragments, string source)
        {
            var byKey = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            var order = new List<string>();
            for (int i = 0; i < fragments.Count; i++)
            {
                if (fragments[i].Node is not JsonObject root ||
                    root["defaults"] is not JsonObject defaults ||
                    !defaults.TryGetPropertyValue("prefs", out JsonNode? prefsNode))
                {
                    continue;
                }

                if (prefsNode is not JsonArray prefs)
                {
                    throw new InvalidOperationException(
                        $"{source} fragment '{fragments[i].SourceUri}' defaults.prefs must be an array.");
                }

                for (int p = 0; p < prefs.Count; p++)
                {
                    if (prefs[p] is not JsonObject pair)
                    {
                        throw new InvalidOperationException(
                            $"{source} fragment '{fragments[i].SourceUri}' defaults.prefs[{p}] must be an object.");
                    }

                    if (pair["key"] is not JsonValue keyValue ||
                        !keyValue.TryGetValue<string>(out string? keyName) ||
                        string.IsNullOrWhiteSpace(keyName) ||
                        !string.Equals(keyName, keyName.Trim(), StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"{source} fragment '{fragments[i].SourceUri}' defaults.prefs[{p}].key must be a non-empty token.");
                    }

                    if (!byKey.ContainsKey(keyName))
                    {
                        order.Add(keyName);
                    }

                    byKey[keyName] = pair;
                }
            }

            if (order.Count == 0)
            {
                return;
            }

            if (merged["defaults"] is not JsonObject mergedDefaults)
            {
                throw new InvalidOperationException($"{source} defaults must be an object.");
            }

            var mergedPrefs = new JsonArray();
            for (int i = 0; i < order.Count; i++)
            {
                mergedPrefs.Add(byKey[order[i]].DeepClone());
            }

            mergedDefaults["prefs"] = mergedPrefs;
        }

        /// <summary>Structural fail-fast validation. Profile installation is checked in <see cref="ResolveSeed"/>.</summary>
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
            Dictionary<string, HashSet<string>> declared = ValidateKeys(config.Keys, source);
            ValidatePlayerPrefs(defaults.Prefs, declared, source);
            ValidateAbilityPrefs(defaults.AbilityPrefs, declared, source);
        }

        /// <summary>
        /// Install declared keys into <paramref name="catalog"/> and resolve the seed ids.
        /// Context-valued keys require each value to be an installed interaction context profile.
        /// </summary>
        public static InteractionPrefSeed ResolveSeed(
            InteractionPrefsConfig config,
            CommandIntentProfileRegistry commandIntents,
            CastDispatchProfileRegistry castDispatchProfiles,
            InteractionPrefCatalog catalog,
            InteractionContextProfileRegistry contextProfiles,
            AbilityDefinitionRegistry abilityDefinitions,
            string source = "Input/interaction_prefs.json")
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (contextProfiles == null)
            {
                throw new ArgumentNullException(nameof(contextProfiles));
            }

            if (abilityDefinitions == null)
            {
                throw new ArgumentNullException(nameof(abilityDefinitions));
            }

            Validate(config, source);
            InteractionPrefDefaultsDefinition defaults = config.Defaults!;
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

            List<InteractionPrefKeyDefinition>? keys = config.Keys;
            if (keys != null)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    InteractionPrefKeyDefinition key = keys[i]!;
                    catalog.DefineKey(key.Id!, key.ContextValued, key.Values!, contextProfiles, $"{source}.keys[{i}]");
                }
            }

            InteractionPrefPairSeed[] playerPrefs = ResolvePlayerPrefs(defaults.Prefs, catalog, source);
            InteractionPrefAbilityPrefSeed[] abilityPrefs = ResolveAbilityPrefs(
                defaults.AbilityPrefs,
                catalog,
                abilityDefinitions,
                source);
            return new InteractionPrefSeed(intentRegistryId, dispatchRegistryId, playerPrefs, abilityPrefs);
        }

        private static Dictionary<string, HashSet<string>> ValidateKeys(List<InteractionPrefKeyDefinition>? keys, string source)
        {
            var declared = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (keys == null)
            {
                return declared;
            }

            for (int i = 0; i < keys.Count; i++)
            {
                InteractionPrefKeyDefinition key = keys[i]
                    ?? throw new InvalidOperationException($"{source}.keys[{i}] must be an object.");
                string keyPath = $"{source}.keys[{i}].id";
                string keyName = key.Id ?? string.Empty;
                RequireTrimmedNonEmpty(keyName, keyPath);
                if (declared.ContainsKey(keyName))
                {
                    throw new InvalidOperationException($"{keyPath} duplicates preference key '{keyName}'.");
                }

                List<string>? values = key.Values;
                if (values == null || values.Count == 0)
                {
                    throw new InvalidOperationException($"{source}.keys[{i}] '{keyName}' must declare at least one value.");
                }

                var allowed = new HashSet<string>(values.Count, StringComparer.Ordinal);
                for (int v = 0; v < values.Count; v++)
                {
                    string valuePath = $"{source}.keys[{i}].values[{v}]";
                    string valueName = values[v] ?? string.Empty;
                    RequireTrimmedNonEmpty(valueName, valuePath);
                    if (!allowed.Add(valueName))
                    {
                        throw new InvalidOperationException($"{valuePath} duplicates value '{valueName}' on key '{keyName}'.");
                    }
                }

                declared[keyName] = allowed;
            }

            return declared;
        }

        private static void ValidatePlayerPrefs(
            List<InteractionPrefPairDefinition>? prefs,
            Dictionary<string, HashSet<string>> declared,
            string source)
        {
            if (prefs == null)
            {
                return;
            }

            if (prefs.Count > InteractionPref.MaxPlayerPrefs)
            {
                throw new InvalidOperationException(
                    $"{source}.defaults.prefs exceeds {InteractionPref.MaxPlayerPrefs} entries.");
            }

            var seen = new HashSet<string>(prefs.Count, StringComparer.Ordinal);
            for (int i = 0; i < prefs.Count; i++)
            {
                InteractionPrefPairDefinition pair = prefs[i]
                    ?? throw new InvalidOperationException($"{source}.defaults.prefs[{i}] must be an object.");
                string keyPath = $"{source}.defaults.prefs[{i}].key";
                string valuePath = $"{source}.defaults.prefs[{i}].value";
                string keyName = pair.Key ?? string.Empty;
                string valueName = pair.Value ?? string.Empty;
                RequireTrimmedNonEmpty(keyName, keyPath);
                RequireTrimmedNonEmpty(valueName, valuePath);
                if (!seen.Add(keyName))
                {
                    throw new InvalidOperationException($"{keyPath} duplicates preference key '{keyName}'.");
                }

                RequireDeclaredValue(declared, keyName, valueName, keyPath, valuePath);
            }
        }

        private static void ValidateAbilityPrefs(
            List<InteractionPrefAbilityPrefDefinition>? prefs,
            Dictionary<string, HashSet<string>> declared,
            string source)
        {
            if (prefs == null)
            {
                return;
            }

            if (prefs.Count > InteractionPref.MaxAbilityPrefs)
            {
                throw new InvalidOperationException(
                    $"{source}.defaults.abilityPrefs exceeds {InteractionPref.MaxAbilityPrefs} entries.");
            }

            var seen = new HashSet<string>(prefs.Count, StringComparer.Ordinal);
            for (int i = 0; i < prefs.Count; i++)
            {
                InteractionPrefAbilityPrefDefinition pair = prefs[i]
                    ?? throw new InvalidOperationException($"{source}.defaults.abilityPrefs[{i}] must be an object.");
                string abilityPath = $"{source}.defaults.abilityPrefs[{i}].abilityId";
                string keyPath = $"{source}.defaults.abilityPrefs[{i}].key";
                string valuePath = $"{source}.defaults.abilityPrefs[{i}].value";
                string abilityName = pair.AbilityId ?? string.Empty;
                string keyName = pair.Key ?? string.Empty;
                string valueName = pair.Value ?? string.Empty;
                RequireTrimmedNonEmpty(abilityName, abilityPath);
                RequireTrimmedNonEmpty(keyName, keyPath);
                RequireTrimmedNonEmpty(valueName, valuePath);
                string pin = abilityName + "\n" + keyName;
                if (!seen.Add(pin))
                {
                    throw new InvalidOperationException($"{keyPath} duplicates preference key '{keyName}' for ability '{abilityName}'.");
                }

                RequireDeclaredValue(declared, keyName, valueName, keyPath, valuePath);
            }
        }

        private static void RequireDeclaredValue(
            Dictionary<string, HashSet<string>> declared,
            string keyName,
            string valueName,
            string keyPath,
            string valuePath)
        {
            if (!declared.TryGetValue(keyName, out HashSet<string>? allowed))
            {
                throw new InvalidOperationException($"{keyPath} references preference key '{keyName}' which is not declared.");
            }

            if (!allowed.Contains(valueName))
            {
                throw new InvalidOperationException(
                    $"{valuePath} references value '{valueName}' which is not allowed for preference key '{keyName}'.");
            }
        }

        private static InteractionPrefPairSeed[] ResolvePlayerPrefs(
            List<InteractionPrefPairDefinition>? prefs,
            InteractionPrefCatalog catalog,
            string source)
        {
            if (prefs == null || prefs.Count == 0)
            {
                return Array.Empty<InteractionPrefPairSeed>();
            }

            var resolved = new InteractionPrefPairSeed[prefs.Count];
            for (int i = 0; i < prefs.Count; i++)
            {
                InteractionPrefPairDefinition pair = prefs[i]!;
                resolved[i] = new InteractionPrefPairSeed(
                    catalog.Keys.GetId(pair.Key!),
                    catalog.Values.GetId(pair.Value!));
            }

            return resolved;
        }

        private static InteractionPrefAbilityPrefSeed[] ResolveAbilityPrefs(
            List<InteractionPrefAbilityPrefDefinition>? prefs,
            InteractionPrefCatalog catalog,
            AbilityDefinitionRegistry abilityDefinitions,
            string source)
        {
            if (prefs == null || prefs.Count == 0)
            {
                return Array.Empty<InteractionPrefAbilityPrefSeed>();
            }

            var resolved = new InteractionPrefAbilityPrefSeed[prefs.Count];
            for (int i = 0; i < prefs.Count; i++)
            {
                InteractionPrefAbilityPrefDefinition pair = prefs[i]!;
                string abilityName = pair.AbilityId!;
                int abilityId = AbilityIdRegistry.GetId(abilityName);
                if (abilityId == AbilityIdRegistry.InvalidId || !abilityDefinitions.TryGet(abilityId, out _))
                {
                    throw new InvalidOperationException(
                        $"{source} defaults.abilityPrefs[{i}].abilityId references ability '{abilityName}' which is not installed.");
                }

                resolved[i] = new InteractionPrefAbilityPrefSeed(
                    abilityId,
                    catalog.Keys.GetId(pair.Key!),
                    catalog.Values.GetId(pair.Value!));
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
    }
}
