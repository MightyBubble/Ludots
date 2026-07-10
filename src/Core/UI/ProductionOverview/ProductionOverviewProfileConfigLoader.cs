using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Config;

namespace Ludots.Core.UI.ProductionOverview
{
    /// <summary>
    /// Loader for <c>UI/production_overview_profiles.json</c> (WPK-4). Catalog-declared DeepObject
    /// merge through <see cref="ConfigPipeline"/>; structural validation fails fast. Reference
    /// existence is checked by <see cref="ProductionOverviewProfileRegistry.Install"/>.
    /// </summary>
    public sealed class ProductionOverviewProfileConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public ProductionOverviewProfileConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public ProductionOverviewProfilesConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "UI/production_overview_profiles.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            var config = mergedObject.Deserialize<ProductionOverviewProfilesConfig>(JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
            Validate(config, relativePath);
            return config;
        }

        public static void Validate(ProductionOverviewProfilesConfig config, string source)
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
                ProductionOverviewProfileDefinition profile = config.Profiles[i]
                    ?? throw new InvalidOperationException($"{source}.profiles[{i}] must be an object.");
                string path = $"{source}.profiles[{i}]";
                RequireTrimmedNonEmpty(profile.Id, $"{path}.id");
                if (!ids.Add(profile.Id))
                {
                    throw new InvalidOperationException($"{path}.id duplicates profile id '{profile.Id}'.");
                }

                RequireTrimmedNonEmpty(profile.SourceKind, $"{path}.sourceKind");
                RequireTrimmedNonEmpty(profile.CommandPanelSourceId, $"{path}.commandPanelSourceId");
                RequireTrimmedNonEmpty(profile.QueueSourceKind, $"{path}.queueSourceKind");

                if (profile.WorkerBuckets == null)
                {
                    throw new InvalidOperationException($"{path}.workerBuckets must be an explicit array (may be empty).");
                }

                var bucketIds = new HashSet<string>(StringComparer.Ordinal);
                for (int b = 0; b < profile.WorkerBuckets.Count; b++)
                {
                    ProductionWorkerBucketDefinition bucket = profile.WorkerBuckets[b]
                        ?? throw new InvalidOperationException($"{path}.workerBuckets[{b}] must be an object.");
                    string bucketPath = $"{path}.workerBuckets[{b}]";
                    RequireTrimmedNonEmpty(bucket.BucketId, $"{bucketPath}.bucketId");
                    if (!bucketIds.Add(bucket.BucketId))
                    {
                        throw new InvalidOperationException($"{bucketPath}.bucketId duplicates '{bucket.BucketId}'.");
                    }

                    RequireTrimmedNonEmpty(bucket.DisplayTokenId, $"{bucketPath}.displayTokenId");
                    RequireTrimmedNonEmpty(bucket.MatchKind, $"{bucketPath}.matchKind");
                }
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
