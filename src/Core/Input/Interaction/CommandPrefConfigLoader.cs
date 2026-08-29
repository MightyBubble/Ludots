using System;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.Registry;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Loader for <c>Input/command_prefs.json</c>: the game-instance seed of the player-level
    /// default command intent + cast dispatch profile that map binding plants on every bound
    /// player representative lacking a <see cref="CommandPref"/>. Catalog-declared DeepObject
    /// merge through the shared <see cref="ConfigPipeline"/>; id resolution against the
    /// installed profile registries is fail fast.
    /// </summary>
    public sealed class CommandPrefConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public CommandPrefConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        /// <summary>Load and validate the merged command preference seed config.</summary>
        public CommandPrefsConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Input/command_prefs.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            var config = mergedObject.Deserialize<CommandPrefsConfig>(JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
            Validate(config, relativePath);
            return config;
        }

        /// <summary>Structural fail-fast validation; id resolution happens in <see cref="ResolveSeed"/>.</summary>
        public static void Validate(CommandPrefsConfig config, string source)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (config.Defaults == null)
            {
                throw new InvalidOperationException($"Command preference config '{source}' must explicitly define defaults.");
            }

            RequireTrimmedNonEmpty(config.Defaults.CommandIntentId, $"{source}.defaults.commandIntentId");
            RequireTrimmedNonEmpty(config.Defaults.CastDispatchProfileId, $"{source}.defaults.castDispatchProfileId");
        }

        /// <summary>
        /// Resolve the seed ids: the command intent profile must be installed and is registered
        /// into the interaction stack's id space (the space frames and the arbiter resolve in);
        /// the cast dispatch profile must be installed and resolves in its own registry space.
        /// </summary>
        public static CommandPrefSeed ResolveSeed(
            CommandPrefsConfig config,
            CommandIntentProfileRegistry commandIntents,
            CastDispatchProfileRegistry castDispatchProfiles,
            StringIntRegistry commandIntentIdSpace,
            string source = "Input/command_prefs.json")
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            string intentName = config.Defaults.CommandIntentId;
            if (!commandIntents.ProfileIdRegistry.TryGetId(intentName, out int intentRegistryId) ||
                !commandIntents.IsInstalled(intentRegistryId))
            {
                throw new InvalidOperationException(
                    $"{source} defaults.commandIntentId references command intent profile '{intentName}' which is not installed.");
            }

            string dispatchName = config.Defaults.CastDispatchProfileId;
            if (!castDispatchProfiles.ProfileIdRegistry.TryGetId(dispatchName, out int dispatchRegistryId) ||
                !castDispatchProfiles.IsInstalled(dispatchRegistryId))
            {
                throw new InvalidOperationException(
                    $"{source} defaults.castDispatchProfileId references cast dispatch profile '{dispatchName}' which is not installed.");
            }

            return new CommandPrefSeed(commandIntentIdSpace.Register(intentName), dispatchRegistryId);
        }

        private static void RequireTrimmedNonEmpty(string value, string path)
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
