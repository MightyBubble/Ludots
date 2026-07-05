using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Config;

namespace Ludots.Core.UI.EntityCommandPanels
{
    /// <summary>
    /// Loader for <c>UI/ability_aggregation_profiles.json</c> (RFC-0065 PNL-1/2). Follows the
    /// <c>CommandIntentProfileConfigLoader</c> mounting pattern: catalog-declared DeepObject merge
    /// through the shared <see cref="ConfigPipeline"/>, structural validation fails fast here;
    /// <c>groupBy</c> prefix resolution fails fast at registry install.
    /// </summary>
    public sealed class AbilityAggregationProfileConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public AbilityAggregationProfileConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        /// <summary>Load and validate the merged aggregation profile config from the pipeline.</summary>
        public AbilityAggregationProfilesConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "UI/ability_aggregation_profiles.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            var config = mergedObject.Deserialize<AbilityAggregationProfilesConfig>(JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
            Validate(config, relativePath);
            return config;
        }

        /// <summary>Structural fail-fast validation; groupBy prefix resolution happens at registry install.</summary>
        public static void Validate(AbilityAggregationProfilesConfig config, string source)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (config.Profiles == null)
            {
                throw new InvalidOperationException($"Aggregation config '{source}' must explicitly define profiles.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                AbilityAggregationProfileDefinition profile = config.Profiles[i]
                    ?? throw new InvalidOperationException($"Aggregation config '{source}' profiles[{i}] must be an object.");
                string path = $"{source}.profiles[{i}]";
                RequireTrimmedNonEmpty(profile.Id, $"{path}.id");
                if (!ids.Add(profile.Id))
                {
                    throw new InvalidOperationException($"{path}.id duplicates aggregation profile '{profile.Id}'.");
                }

                RequireTrimmedNonEmpty(profile.GroupBy, $"{path}.groupBy");
                if (profile.Overflow != null)
                {
                    RequireTrimmedNonEmpty(profile.Overflow, $"{path}.overflow");
                }
            }
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
