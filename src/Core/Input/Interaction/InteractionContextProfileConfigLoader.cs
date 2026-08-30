using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Config;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Loader for <c>Input/interaction_context_profiles.json</c> (RFC-0065 CTX-6, §5.3). Catalog-
    /// declared DeepObject merge through the shared <see cref="ConfigPipeline"/> (a mod fragment's
    /// profiles array replaces the root's, matching the filter profile family); structural
    /// validation fails fast. Referenced filter/intent ids resolve at registry install, not here.
    /// The engine-reserved steady-state profile installs programmatically in GameEngine, not here.
    /// </summary>
    public sealed class InteractionContextProfileConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public InteractionContextProfileConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        /// <summary>Load and validate the merged interaction context profile config from the pipeline.</summary>
        public InteractionContextProfilesConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Input/interaction_context_profiles.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            var config = mergedObject.Deserialize<InteractionContextProfilesConfig>(JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
            Validate(config, relativePath);
            return config;
        }

        /// <summary>Structural fail-fast validation; id resolution happens at profile registry install time.</summary>
        public static void Validate(InteractionContextProfilesConfig config, string source)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (config.Profiles == null)
            {
                throw new InvalidOperationException($"Interaction context config '{source}' must explicitly define profiles.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                InteractionContextProfileDefinition profile = config.Profiles[i]
                    ?? throw new InvalidOperationException($"Interaction context config '{source}' profiles[{i}] must be an object.");
                string path = $"{source}.profiles[{i}]";
                RequireTrimmedNonEmpty(profile.Id, $"{path}.id");
                if (!ids.Add(profile.Id))
                {
                    throw new InvalidOperationException($"{path}.id duplicates interaction context profile '{profile.Id}'.");
                }

                RequireTrimmedNonEmpty(profile.ActiveCollectionKey, $"{path}.activeCollectionKey");
                RequireTrimmedNonEmpty(profile.ActiveEntityViewKey, $"{path}.activeEntityViewKey");
                RequireTrimmedWhenPresent(profile.FilterProfileId, $"{path}.filterProfileId");
                RequireTrimmedWhenPresent(profile.InputContextId, $"{path}.inputContextId");
                RequireTrimmedWhenPresent(profile.CommandIntentId, $"{path}.commandIntentId");
            }
        }

        private static void RequireTrimmedWhenPresent(string value, string path)
        {
            if (value == null)
            {
                return;
            }

            RequireTrimmedNonEmpty(value, path);
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
