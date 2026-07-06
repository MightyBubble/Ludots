using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Config;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Loader for <c>Input/filter_profiles.json</c> (RFC-0065 CTX-4). Follows the
    /// <c>InputOrderMappingLoader</c> mounting pattern: catalog-declared DeepObject merge
    /// through the shared <see cref="ConfigPipeline"/>, structural validation fails fast.
    /// </summary>
    public sealed class FilterProfileConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public FilterProfileConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        /// <summary>Load and validate the merged filter profile config from the pipeline.</summary>
        public FilterProfilesConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Input/filter_profiles.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            var config = mergedObject.Deserialize<FilterProfilesConfig>(JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
            Validate(config, relativePath);
            return config;
        }

        /// <summary>Structural fail-fast validation; expand/tag resolution happens at registry install.</summary>
        public static void Validate(FilterProfilesConfig config, string source)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (config.Profiles == null)
            {
                throw new InvalidOperationException($"Filter profile config '{source}' must explicitly define profiles.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                FilterProfileDefinition profile = config.Profiles[i]
                    ?? throw new InvalidOperationException($"Filter profile config '{source}' profiles[{i}] must be an object.");
                string path = $"{source}.profiles[{i}]";
                RequireTrimmedNonEmpty(profile.Id, $"{path}.id");
                if (!ids.Add(profile.Id))
                {
                    throw new InvalidOperationException($"{path}.id duplicates filter profile '{profile.Id}'.");
                }

                FilterProfileAssociationQuery query = profile.AssociationQuery
                    ?? throw new InvalidOperationException($"{path}.associationQuery must be an object.");
                RequireTrimmedNonEmpty(query.Anchor, $"{path}.associationQuery.anchor");
                RequireTrimmedNonEmpty(query.Expand, $"{path}.associationQuery.expand");

                ValidateTagRule(profile.Exclude, $"{path}.exclude");
                ValidateTagRule(profile.Include, $"{path}.include");
            }
        }

        private static void ValidateTagRule(FilterProfileTagRule rule, string path)
        {
            if (rule?.AnyTags == null)
            {
                return;
            }

            for (int i = 0; i < rule.AnyTags.Count; i++)
            {
                RequireTrimmedNonEmpty(rule.AnyTags[i], $"{path}.anyTags[{i}]");
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
