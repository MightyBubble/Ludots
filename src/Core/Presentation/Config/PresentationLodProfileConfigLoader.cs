using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Config
{
    public sealed class PresentationLodProfileConfigLoader
    {
        public const string DefaultRelativePath = "Presentation/lod_profiles.json";

        private readonly ConfigPipeline _configs;
        private readonly PresentationLodProfileRegistry _profiles;

        public PresentationLodProfileConfigLoader(
            ConfigPipeline configs,
            PresentationLodProfileRegistry profiles)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, DefaultRelativePath, ConfigMergePolicy.ArrayById, "id");
            var fragments = PresentationAssetConfigIdGuard.CollectUniqueArrayByIdFragments(_configs, in entry);
            var merged = ConfigMerger.MergeArrayByIdToEntries(fragments, in entry, report);

            for (int i = 0; i < merged.Count; i++)
            {
                JsonNode node = merged[i].Node;
                string key = RequireString(node?["id"], "Presentation LOD profile id");
                PresentationLodEntry high = ParseEntry(node?["high"], key, "high");
                PresentationLodEntry medium = ParseEntry(node?["medium"], key, "medium");
                PresentationLodEntry low = ParseEntry(node?["low"], key, "low");
                ValidateDistanceOrder(key, in high, in medium, in low);
                _profiles.Register(key, new PresentationLodProfile(high, medium, low));
            }
        }

        private static PresentationLodEntry ParseEntry(JsonNode? node, string key, string fieldName)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"Presentation LOD profile '{key}' must declare object field '{fieldName}'.");
            }

            float maxDistanceCm = RequirePositiveFiniteFloat(
                obj["maxDistanceCm"],
                $"Presentation LOD profile '{key}' {fieldName}.maxDistanceCm");
            float minScreenCoverage01 = RequireFiniteFloat(
                obj["minScreenCoverage01"],
                $"Presentation LOD profile '{key}' {fieldName}.minScreenCoverage01");
            if (minScreenCoverage01 < 0f || minScreenCoverage01 > 1f)
            {
                throw new InvalidOperationException(
                    $"Presentation LOD profile '{key}' {fieldName}.minScreenCoverage01 must be in [0, 1].");
            }

            return new PresentationLodEntry(maxDistanceCm, minScreenCoverage01);
        }

        private static void ValidateDistanceOrder(
            string key,
            in PresentationLodEntry high,
            in PresentationLodEntry medium,
            in PresentationLodEntry low)
        {
            if (high.MaxDistanceCm >= medium.MaxDistanceCm ||
                medium.MaxDistanceCm >= low.MaxDistanceCm)
            {
                throw new InvalidOperationException(
                    $"Presentation LOD profile '{key}' distances must increase from high to medium to low.");
            }
        }

        private static string RequireString(JsonNode? node, string label)
        {
            if (node is JsonValue value &&
                value.TryGetValue(out string? text) &&
                !string.IsNullOrWhiteSpace(text))
            {
                if (!string.Equals(text, text.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{label} must not include leading or trailing whitespace.");
                }

                return text;
            }

            throw new InvalidOperationException($"{label} must be a non-empty string.");
        }

        private static float RequirePositiveFiniteFloat(JsonNode? node, string label)
        {
            float value = RequireFiniteFloat(node, label);
            if (value <= 0f)
            {
                throw new InvalidOperationException($"{label} must be positive.");
            }

            return value;
        }

        private static float RequireFiniteFloat(JsonNode? node, string label)
        {
            if (node is JsonValue value &&
                value.TryGetValue(out float result) &&
                float.IsFinite(result))
            {
                return result;
            }

            throw new InvalidOperationException($"{label} must be a finite number.");
        }
    }
}
