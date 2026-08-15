using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;

namespace Ludots.Core.Presentation.Config
{
    public sealed class PresentationMaterialConfigLoader
    {
        public const string DefaultRelativePath = "Presentation/material_assets.json";

        private readonly ConfigPipeline _configs;
        private readonly PresentationMaterialRegistry _materials;

        public PresentationMaterialConfigLoader(ConfigPipeline configs, PresentationMaterialRegistry materials)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, DefaultRelativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);

            for (int i = 0; i < merged.Count; i++)
            {
                JsonNode node = merged[i].Node;
                string key = RequireString(node, "id", "material asset row");
                if (node["sourceUris"] != null)
                {
                    throw new InvalidOperationException(
                        $"Presentation material asset '{key}' declares sourceUris. Platform paths belong in Presentation/host_assets.json.");
                }

                MaterialAssetDomain domain = ParseDomain(node["domain"], key);
                MaterialAssetFlags flags = ParseFlags(node["flags"], key);
                MaterialBlendModeResolver.Resolve(flags);
                _materials.Register(key, domain, Array.Empty<string>(), flags);
            }
        }

        private static MaterialAssetDomain ParseDomain(JsonNode? node, string key)
        {
            string value = ReadRequiredString(node, $"Presentation material asset '{key}' field 'domain'");
            if (!Enum.TryParse(value, ignoreCase: false, out MaterialAssetDomain domain) ||
                !Enum.IsDefined(typeof(MaterialAssetDomain), domain))
            {
                throw new InvalidOperationException(
                    $"Presentation material asset '{key}' has invalid domain '{value}'.");
            }

            return domain;
        }

        private static string ReadRequiredString(JsonNode? node, string label)
        {
            string value = node?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{label} must be a non-empty string.");
            }

            RequireNoBoundaryWhitespace(value, label);
            return value;
        }

        private static MaterialAssetFlags ParseFlags(JsonNode? node, string key)
        {
            if (node == null)
            {
                return MaterialAssetFlags.None;
            }

            if (node is not JsonArray arr)
            {
                throw new InvalidOperationException(
                    $"Presentation material asset '{key}' flags must be an array of enum strings.");
            }

            MaterialAssetFlags flags = MaterialAssetFlags.None;
            for (int i = 0; i < arr.Count; i++)
            {
                string value = arr[i]?.GetValue<string>() ?? string.Empty;
                RequireNoBoundaryWhitespace(value, $"Presentation material asset '{key}' flags[{i}]");
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException(
                        $"Presentation material asset '{key}' has invalid flag '{value}' at index {i}.");
                }

                if (string.Equals(value, "AlphaBlend", StringComparison.Ordinal))
                {
                    flags |= MaterialAssetFlags.Transparent;
                    continue;
                }

                if (string.Equals(value, "Opaque", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!Enum.TryParse(value, ignoreCase: false, out MaterialAssetFlags parsed) ||
                    !Enum.IsDefined(typeof(MaterialAssetFlags), parsed) ||
                    parsed == MaterialAssetFlags.None)
                {
                    throw new InvalidOperationException(
                        $"Presentation material asset '{key}' has invalid flag '{value}' at index {i}.");
                }

                flags |= parsed;
            }

            return flags;
        }

        private static string RequireString(JsonNode? node, string fieldName, string rowLabel)
        {
            string value = node?[fieldName]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Presentation material asset '{rowLabel}' must declare '{fieldName}'.");
            }

            RequireNoBoundaryWhitespace(value, $"Presentation material asset '{rowLabel}' field '{fieldName}'");
            return value;
        }

        private static void RequireNoBoundaryWhitespace(string value, string label)
        {
            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{label} must not include leading or trailing whitespace.");
            }
        }
    }
}
