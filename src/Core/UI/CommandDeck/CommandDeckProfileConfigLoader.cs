using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Config;

namespace Ludots.Core.UI.CommandDeck
{
    /// <summary>
    /// Loader for <c>UI/command_deck_profiles.json</c> (WPK-3). Catalog-declared DeepObject merge
    /// through <see cref="ConfigPipeline"/>; structural validation fails fast. Reference existence
    /// is checked by <see cref="CommandDeckProfileRegistry.Install"/>.
    /// </summary>
    public sealed class CommandDeckProfileConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public CommandDeckProfileConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public CommandDeckProfilesConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "UI/command_deck_profiles.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            var config = mergedObject.Deserialize<CommandDeckProfilesConfig>(JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
            Validate(config, relativePath);
            return config;
        }

        public static void Validate(CommandDeckProfilesConfig config, string source)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ArgumentException("Source label is required.", nameof(source));
            }

            if (config.Profiles == null)
            {
                throw new InvalidOperationException($"{source} must explicitly define profiles.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                CommandDeckProfileDefinition profile = config.Profiles[i]
                    ?? throw new InvalidOperationException($"{source}.profiles[{i}] must be an object.");
                string path = $"{source}.profiles[{i}]";
                RequireTrimmedNonEmpty(profile.Id, $"{path}.id");
                if (!ids.Add(profile.Id))
                {
                    throw new InvalidOperationException($"{path}.id duplicates profile id '{profile.Id}'.");
                }

                RequireTrimmedNonEmpty(profile.DisplayMode, $"{path}.displayMode");
                RequireTrimmedNonEmpty(profile.SourceKind, $"{path}.sourceKind");
                RequireTrimmedNonEmpty(profile.CommandPanelSourceId, $"{path}.commandPanelSourceId");
            }
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
