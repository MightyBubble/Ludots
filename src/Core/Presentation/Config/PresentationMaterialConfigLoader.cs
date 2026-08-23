using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;
using Ludots.Platform.Abstractions;

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
            var fragments = PresentationAssetConfigIdGuard.CollectUniqueArrayByIdFragments(_configs, in entry);
            var merged = ConfigMerger.MergeArrayByIdToEntries(fragments, in entry, report);

            for (int i = 0; i < merged.Count; i++)
            {
                JsonNode node = merged[i].Node;
                string key = RequireString(node, "id", "material asset row");
                if (node["sourceUris"] != null || node["textures"] != null)
                {
                    throw new InvalidOperationException(
                        $"Presentation material asset '{key}' declares texture URIs. Platform paths belong in Presentation/host_assets.json.");
                }

                string? parentKey = ParseOptionalKey(node["parent"], key, "parent");
                string? shaderKey = ParseOptionalKey(node["shaderKey"], key, "shaderKey");
                MaterialAssetDomain domain = ParseDomain(node["domain"], key);
                MaterialAssetFlags flags = ParseFlags(node["flags"], key);
                MaterialBlendModeResolver.Resolve(flags);
                if (parentKey != null && (shaderKey != null || node["flags"] != null))
                {
                    throw new InvalidOperationException(
                        $"Presentation material asset '{key}' is an instance (parent='{parentKey}'); instances cannot declare shaderKey/flags.");
                }

                Dictionary<string, float> floats = ParseFloatParams(node["params"]?["floats"], key);
                Dictionary<string, Vector4> colors = ParseColorParams(node["params"]?["colors"], key);
                InjectWellKnownScalar(floats, node["roughness"], key, "roughness", MaterialParameterNames.Roughness);
                InjectWellKnownScalar(floats, node["metalness"], key, "metalness", MaterialParameterNames.Metallic);

                _materials.Register(
                    key,
                    domain,
                    flags,
                    shaderKey,
                    parentKey,
                    floats.Count > 0 ? floats : null,
                    colors.Count > 0 ? colors : null);
            }
        }

        private static void InjectWellKnownScalar(Dictionary<string, float> floats, JsonNode? node, string key, string field, string paramName)
        {
            if (node == null)
            {
                return;
            }

            if (floats.ContainsKey(paramName))
            {
                throw new InvalidOperationException(
                    $"Presentation material asset '{key}' declares '{field}' both at top level and in params.floats.");
            }

            floats[paramName] = ParseUnitScalar(node, key, field);
        }

        private static string? ParseOptionalKey(JsonNode? node, string key, string field)
        {
            if (node == null)
            {
                return null;
            }

            return ReadRequiredString(node, $"Presentation material asset '{key}' field '{field}'");
        }

        private static Dictionary<string, float> ParseFloatParams(JsonNode? node, string key)
        {
            var floats = new Dictionary<string, float>(StringComparer.Ordinal);
            if (node == null)
            {
                return floats;
            }

            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"Presentation material asset '{key}' params.floats must be an object of name → number.");
            }

            foreach (KeyValuePair<string, JsonNode?> pair in obj)
            {
                RequireParamName(pair.Key, key, "params.floats");
                bool wellKnownUnit =
                    string.Equals(pair.Key, MaterialParameterNames.Roughness, StringComparison.Ordinal) ||
                    string.Equals(pair.Key, MaterialParameterNames.Metallic, StringComparison.Ordinal);
                string label = $"params.floats.{pair.Key}";
                if (pair.Value?.GetValueKind() != JsonValueKind.Number)
                {
                    throw new InvalidOperationException(
                        $"Presentation material asset '{key}' field '{label}' must be a number.");
                }

                float parsed = (float)pair.Value.GetValue<double>();
                if (wellKnownUnit && (parsed < 0f || parsed > 1f))
                {
                    throw new InvalidOperationException(
                        $"Presentation material asset '{key}' field '{label}' must be within [0, 1].");
                }

                floats[pair.Key] = parsed;
            }

            return floats;
        }

        private static Dictionary<string, Vector4> ParseColorParams(JsonNode? node, string key)
        {
            var colors = new Dictionary<string, Vector4>(StringComparer.Ordinal);
            if (node == null)
            {
                return colors;
            }

            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"Presentation material asset '{key}' params.colors must be an object of name → [r, g, b, a].");
            }

            foreach (KeyValuePair<string, JsonNode?> pair in obj)
            {
                RequireParamName(pair.Key, key, "params.colors");
                string label = $"Presentation material asset '{key}' params.colors.{pair.Key}";
                if (pair.Value is not JsonArray arr || arr.Count != 4)
                {
                    throw new InvalidOperationException($"{label} must be a [r, g, b, a] number array.");
                }

                var components = new float[4];
                for (int c = 0; c < 4; c++)
                {
                    if (arr[c]?.GetValueKind() != JsonValueKind.Number)
                    {
                        throw new InvalidOperationException($"{label}[{c}] must be a number.");
                    }

                    components[c] = (float)arr[c]!.GetValue<double>();
                }

                colors[pair.Key] = new Vector4(components[0], components[1], components[2], components[3]);
            }

            return colors;
        }

        private static void RequireParamName(string name, string key, string field)
        {
            if (string.IsNullOrWhiteSpace(name) || !string.Equals(name, name.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Presentation material asset '{key}' {field} contains an invalid parameter name '{name}'.");
            }
        }

        private static float ParseUnitScalar(JsonNode? node, string key, string field)
        {
            if (node?.GetValueKind() != JsonValueKind.Number)
            {
                throw new InvalidOperationException(
                    $"Presentation material asset '{key}' field '{field}' must be a number.");
            }

            float parsed = (float)node.GetValue<double>();
            if (parsed < 0f || parsed > 1f)
            {
                throw new InvalidOperationException(
                    $"Presentation material asset '{key}' field '{field}' must be within [0, 1].");
            }

            return parsed;
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
