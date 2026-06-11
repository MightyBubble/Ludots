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
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, DefaultRelativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                JsonNode node = merged[i].Node;
                string key = RequireString(node, "id", "material asset row");
                if (node["sourceUris"] != null)
                {
                    throw new InvalidOperationException(
                        $"Presentation material asset '{key}' declares sourceUris. Backend paths belong in Presentation/host_assets.json.");
                }

                MaterialAssetDomain domain = ParseEnumRequired<MaterialAssetDomain>(node["domain"], $"Presentation material asset '{key}'.domain");
                MaterialAssetFlags flags = ParseFlags(node["flags"], key);
                _materials.Register(key, domain, Array.Empty<string>(), flags);
            }
        }

        internal static string RequireString(JsonNode node, string fieldName, string context)
        {
            string value = node?[fieldName]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context} requires non-empty string field '{fieldName}'.");
            }

            return value;
        }

        internal static T ParseEnumRequired<T>(JsonNode node, string context) where T : struct, Enum
        {
            string text = node?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"{context} is required.");
            }

            if (!Enum.TryParse(text, ignoreCase: false, out T parsed))
            {
                throw new InvalidOperationException($"{context} has invalid value '{text}'. Enum values are case-sensitive.");
            }

            return parsed;
        }

        private static MaterialAssetFlags ParseFlags(JsonNode node, string key)
        {
            if (node == null)
            {
                return MaterialAssetFlags.None;
            }

            if (node is JsonValue value)
            {
                string text = value.GetValue<string>();
                return ParseEnumRequired<MaterialAssetFlags>(node, $"Presentation material asset '{key}'.flags");
            }

            if (node is not JsonArray arr)
            {
                throw new InvalidOperationException($"Presentation material asset '{key}'.flags must be a string or string array.");
            }

            MaterialAssetFlags flags = MaterialAssetFlags.None;
            for (int i = 0; i < arr.Count; i++)
            {
                flags |= ParseEnumRequired<MaterialAssetFlags>(arr[i], $"Presentation material asset '{key}'.flags[{i}]");
            }

            return flags;
        }
    }
}
