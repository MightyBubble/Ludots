using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Config;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Loader for <c>Input/cast_dispatch_profiles.json</c> (RFC-0065 DSP-1). Follows the
    /// <c>CommandIntentProfileConfigLoader</c> mounting pattern: catalog-declared DeepObject merge
    /// through the shared <see cref="ConfigPipeline"/>, structural validation fails fast.
    /// Kind resolution (selector/scorer/router registry lookups) happens at registry install.
    /// </summary>
    public sealed class CastDispatchProfileConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public CastDispatchProfileConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        /// <summary>Load and validate the merged cast dispatch profile config from the pipeline.</summary>
        public CastDispatchProfilesConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Input/cast_dispatch_profiles.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            var config = mergedObject.Deserialize<CastDispatchProfilesConfig>(JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
            Validate(config, relativePath);
            return config;
        }

        /// <summary>Structural fail-fast validation; kind resolution happens at registry install.</summary>
        public static void Validate(CastDispatchProfilesConfig config, string source)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (config.Profiles == null)
            {
                throw new InvalidOperationException($"Cast dispatch config '{source}' must explicitly define profiles.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                CastDispatchProfileDefinition profile = config.Profiles[i]
                    ?? throw new InvalidOperationException($"Cast dispatch config '{source}' profiles[{i}] must be an object.");
                string path = $"{source}.profiles[{i}]";
                RequireTrimmedNonEmpty(profile.Id, $"{path}.id");
                if (!ids.Add(profile.Id))
                {
                    throw new InvalidOperationException($"{path}.id duplicates cast dispatch profile '{profile.Id}'.");
                }

                CastDispatchSelectorDefinition selector = profile.Selector
                    ?? throw new InvalidOperationException($"{path}.selector must be an object.");
                RequireTrimmedNonEmpty(selector.Kind, $"{path}.selector.kind");
                if (selector.AdvanceOn != null)
                {
                    RequireTrimmedNonEmpty(selector.AdvanceOn, $"{path}.selector.advanceOn");
                }

                CastDispatchRouterDefinition router = profile.Router
                    ?? throw new InvalidOperationException($"{path}.router must be an object.");
                RequireTrimmedNonEmpty(router.Kind, $"{path}.router.kind");

                CastDispatchScorerDefinition scorer = profile.Scorer;
                if (scorer != null)
                {
                    RequireTrimmedNonEmpty(scorer.Kind, $"{path}.scorer.kind");
                    if (scorer.Considerations == null || scorer.Considerations.Count == 0)
                    {
                        throw new InvalidOperationException($"{path}.scorer.considerations must declare at least one entry.");
                    }

                    for (int c = 0; c < scorer.Considerations.Count; c++)
                    {
                        RequireTrimmedNonEmpty(scorer.Considerations[c], $"{path}.scorer.considerations[{c}]");
                    }
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
