using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Core.Presentation.Config
{
    public sealed class PresentationSemanticCatalogLoader
    {
        private const string AttributePath = "Presentation/semantic_attributes.json";
        private const string MappingPath = "Presentation/semantic_mappings.json";

        private readonly ConfigPipeline _configs;
        private readonly PresentationTextCatalog _textCatalog;

        public PresentationSemanticCatalogLoader(
            ConfigPipeline configs,
            PresentationTextCatalog textCatalog)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _textCatalog = textCatalog ?? throw new ArgumentNullException(nameof(textCatalog));
        }

        public PresentationSemanticCatalog Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var (attributesByKey, attributesById) = LoadAttributes(catalog, report);
            var mappings = LoadMappings(catalog, report);
            if (attributesByKey.Count == 0 && mappings.Count == 0)
            {
                return PresentationSemanticCatalog.Empty;
            }

            return new PresentationSemanticCatalog(attributesByKey, attributesById, mappings);
        }

        private (Dictionary<string, PresentationSemanticAttributeDefinition> ByKey, Dictionary<int, PresentationSemanticAttributeDefinition> ById)
            LoadAttributes(ConfigCatalog catalog, ConfigConflictReport report)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, AttributePath, ConfigMergePolicy.ArrayById, "id");
            IReadOnlyList<MergedConfigEntry> nodes = _configs.MergeArrayByIdFromCatalog(in entry, report);
            var attributesByKey = new Dictionary<string, PresentationSemanticAttributeDefinition>(StringComparer.Ordinal);
            var attributesById = new Dictionary<int, PresentationSemanticAttributeDefinition>();

            for (int i = 0; i < nodes.Count; i++)
            {
                JsonObject node = nodes[i].Node;
                string semanticKey = node["id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(semanticKey))
                {
                    throw new InvalidOperationException("Presentation semantic attribute entry is missing required 'id'.");
                }

                if (attributesByKey.ContainsKey(semanticKey))
                {
                    throw new InvalidOperationException($"Presentation semantic attribute '{semanticKey}' is defined more than once.");
                }

                string attributeKey = node["attribute"]?.GetValue<string>() ?? string.Empty;
                int attributeId = AttributeRegistry.InvalidId;
                if (!string.IsNullOrWhiteSpace(attributeKey))
                {
                    attributeId = AttributeRegistry.Register(attributeKey);
                }

                var definition = new PresentationSemanticAttributeDefinition
                {
                    SemanticKey = semanticKey,
                    AttributeId = attributeId,
                    AttributeKey = attributeKey,
                    LabelTokenId = ResolveRequiredTokenId(node, "labelToken", semanticKey),
                    CurrentFormatTokenId = ResolveRequiredTokenId(node, "currentFormatToken", semanticKey),
                    CurrentOverBaseFormatTokenId = ResolveRequiredTokenId(node, "currentOverBaseFormatToken", semanticKey),
                    ConstantFormatTokenId = ResolveRequiredTokenId(node, "constantFormatToken", semanticKey),
                    UnitTokenId = ResolveOptionalTokenId(node, "unitToken"),
                };

                attributesByKey.Add(semanticKey, definition);
                if (attributeId > 0)
                {
                    attributesById.Add(attributeId, definition);
                }
            }

            return (attributesByKey, attributesById);
        }

        private Dictionary<string, PresentationSemanticValueMappingDefinition> LoadMappings(ConfigCatalog catalog, ConfigConflictReport report)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, MappingPath, ConfigMergePolicy.ArrayById, "id");
            IReadOnlyList<MergedConfigEntry> nodes = _configs.MergeArrayByIdFromCatalog(in entry, report);
            var mappings = new Dictionary<string, PresentationSemanticValueMappingDefinition>(StringComparer.Ordinal);

            for (int i = 0; i < nodes.Count; i++)
            {
                JsonObject node = nodes[i].Node;
                string mappingId = node["id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(mappingId))
                {
                    throw new InvalidOperationException("Presentation semantic mapping is missing required 'id'.");
                }

                if (node["values"] is not JsonObject valuesNode || valuesNode.Count == 0)
                {
                    throw new InvalidOperationException($"Presentation semantic mapping '{mappingId}' must define a non-empty 'values' object.");
                }

                var values = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, JsonNode?> pair in valuesNode)
                {
                    string key = pair.Key;
                    string tokenKey = pair.Value?.GetValue<string>() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(tokenKey))
                    {
                        throw new InvalidOperationException($"Presentation semantic mapping '{mappingId}' contains an empty value key or token.");
                    }

                    int tokenId = _textCatalog.GetTokenId(tokenKey);
                    if (tokenId <= 0)
                    {
                        throw new InvalidOperationException($"Presentation semantic mapping '{mappingId}' references unknown token '{tokenKey}'.");
                    }

                    values.Add(key, tokenId);
                }

                Dictionary<int, string>? runtimeValueKeys = null;
                if (node["runtimeValues"] is JsonObject runtimeValuesNode && runtimeValuesNode.Count > 0)
                {
                    runtimeValueKeys = new Dictionary<int, string>();
                    foreach (KeyValuePair<string, JsonNode?> pair in runtimeValuesNode)
                    {
                        if (!int.TryParse(pair.Key, out int runtimeValue))
                        {
                            throw new InvalidOperationException(
                                $"Presentation semantic mapping '{mappingId}' runtime value key '{pair.Key}' must parse as an integer.");
                        }

                        string mappedValueKey = pair.Value?.GetValue<string>() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(mappedValueKey))
                        {
                            throw new InvalidOperationException(
                                $"Presentation semantic mapping '{mappingId}' runtime value '{runtimeValue}' must map to a non-empty value key.");
                        }

                        if (!values.ContainsKey(mappedValueKey))
                        {
                            throw new InvalidOperationException(
                                $"Presentation semantic mapping '{mappingId}' runtime value '{runtimeValue}' references undefined value key '{mappedValueKey}'.");
                        }

                        runtimeValueKeys.Add(runtimeValue, mappedValueKey);
                    }
                }

                mappings.Add(
                    mappingId,
                    new PresentationSemanticValueMappingDefinition(
                        mappingId,
                        ResolveRequiredTokenId(node, "labelToken", mappingId),
                        values,
                        runtimeValueKeys));
            }

            return mappings;
        }

        private int ResolveRequiredTokenId(JsonObject node, string propertyName, string scope)
        {
            string tokenKey = node[propertyName]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tokenKey))
            {
                throw new InvalidOperationException($"Presentation semantic scope '{scope}' must define '{propertyName}'.");
            }

            int tokenId = _textCatalog.GetTokenId(tokenKey);
            if (tokenId <= 0)
            {
                throw new InvalidOperationException($"Presentation semantic scope '{scope}' references unknown token '{tokenKey}'.");
            }

            return tokenId;
        }

        private int ResolveOptionalTokenId(JsonObject node, string propertyName)
        {
            string tokenKey = node[propertyName]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tokenKey))
            {
                return 0;
            }

            int tokenId = _textCatalog.GetTokenId(tokenKey);
            if (tokenId <= 0)
            {
                throw new InvalidOperationException($"Presentation semantic token '{tokenKey}' is not registered.");
            }

            return tokenId;
        }
    }
}
