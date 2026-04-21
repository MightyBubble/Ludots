using System;
using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Config
{
    /// <summary>
    /// Loads <see cref="PerformerDefinition"/> entries from
    /// <c>Presentation/performers.json</c> via <see cref="ConfigPipeline"/>.
    /// All ID fields are string-only — no numeric IDs accepted.
    /// </summary>
    public sealed class PerformerDefinitionConfigLoader
    {
        private readonly ConfigPipeline _configs;
        private readonly PerformerDefinitionRegistry _registry;
        private readonly Func<string, int> _resolveAttributeName;
        private readonly Func<string, int> _resolveMeshId;
        private readonly Func<string, int> _resolveTextTokenId;
        private readonly Func<string, int> _resolveTemplateId;

        /// <param name="resolveMeshId">
        /// Resolves a mesh asset key (e.g. "cube") to its int ID.
        /// Injected from <c>MeshAssetRegistry.GetId</c>.
        /// </param>
        /// <param name="resolveTemplateId">
        /// Resolves a visual template key (e.g. "moba_hero") to its int ID.
        /// Injected from <c>VisualTemplateRegistry.GetId</c>.
        /// </param>
        public PerformerDefinitionConfigLoader(
            ConfigPipeline configs,
            PerformerDefinitionRegistry registry,
            Func<string, int> resolveAttributeName = null,
            Func<string, int> resolveMeshId = null,
            Func<string, int> resolveTextTokenId = null,
            Func<string, int> resolveTemplateId = null)
        {
            _configs = configs;
            _registry = registry;
            _resolveAttributeName = resolveAttributeName ?? (_ => 0);
            _resolveMeshId = resolveMeshId ?? (_ => 0);
            _resolveTextTokenId = resolveTextTokenId ?? (_ => 0);
            _resolveTemplateId = resolveTemplateId ?? (_ => 0);
        }

        public void Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Presentation/performers.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            if (merged.Count == 0) return;

            for (int i = 0; i < merged.Count; i++)
            {
                var (key, def) = ParseDefinition(merged[i].Node);
                if (key != null && def != null)
                    _registry.Register(key, def);
            }
        }

        private (string key, PerformerDefinition def) ParseDefinition(JsonNode node)
        {
            string key = node["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(key)) return (null, null);

            var def = new PerformerDefinition();
            def.VisualKind = ParseEnumStrict(node["visualKind"]?.GetValue<string>(), PerformerVisualKind.GroundOverlay, "performer visualKind");
            def.EntityScope = ParseEnum(node["entityScope"]?.GetValue<string>(), EntityScopeFilter.None);
            def.MeshOrShapeId = ResolveMeshOrShape(node["meshOrShapeId"], def.VisualKind);
            def.VisualAssetKey = node["assetKey"]?.GetValue<string>() ?? node["asset"]?.GetValue<string>() ?? string.Empty;
            def.MaterialKey = node["materialKey"]?.GetValue<string>() ?? node["material"]?.GetValue<string>() ?? string.Empty;
            def.SurfaceLayerKey = node["surfaceLayerKey"]?.GetValue<string>() ?? node["surfaceLayer"]?.GetValue<string>() ?? string.Empty;
            def.DefaultPayload = PresentationPayloadConfigParser.ParsePayload(node["payload"], $"Performer '{key}'");
            def.DefaultColor = ParseColor(node["defaultColor"]);
            def.DefaultScale = node["defaultScale"]?.GetValue<float>() ?? 1f;
            def.DefaultLifetime = node["defaultLifetime"]?.GetValue<float>() ?? 0f;
            def.DefaultFontSize = node["defaultFontSize"]?.GetValue<int>() ?? 16;
            def.PositionOffset = ParseVector3(node["positionOffset"]);
            def.PositionYDriftPerSecond = node["positionYDriftPerSecond"]?.GetValue<float>() ?? 0f;
            def.AlphaFadeOverLifetime = node["alphaFadeOverLifetime"]?.GetValue<bool>() ?? false;
            def.VisibilityCondition = ParseConditionRef(node["visibility"]);
            def.Rules = ParseRules(node["rules"]);
            def.Bindings = ParseBindings(node["bindings"], def.VisualKind, key);
            ValidateTypedPerformerDefinition(def, key);

            // ── Entity-scoped filters ──
            string requiredTemplate = node["requiredTemplate"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(requiredTemplate))
                def.RequiredTemplateId = _resolveTemplateId(requiredTemplate);

            return (key, def);
        }

        private int ResolveMeshOrShape(JsonNode meshNode, PerformerVisualKind visualKind)
        {
            if (meshNode == null) return 0;
            string meshStr = meshNode.ToString().Trim('"');
            if (string.IsNullOrWhiteSpace(meshStr)) return 0;

            if (visualKind == PerformerVisualKind.GroundOverlay)
            {
                if (Enum.TryParse<GroundOverlayShape>(meshStr, ignoreCase: true, out var shape))
                    return (int)shape;
                return 0;
            }

            return _resolveMeshId(meshStr);
        }

        // ── Rules ──

        private PerformerRule[] ParseRules(JsonNode node)
        {
            if (node is not JsonArray arr || arr.Count == 0)
                return Array.Empty<PerformerRule>();

            var rules = new PerformerRule[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                rules[i] = ParseRule(arr[i]!);
            }
            return rules;
        }

        private PerformerRule ParseRule(JsonNode node)
        {
            return new PerformerRule
            {
                Event = ParseEventFilter(node["event"]),
                Condition = ParseConditionRef(node["condition"]),
                Command = ParsePerformerCommand(node["command"]),
            };
        }

        private EventFilter ParseEventFilter(JsonNode node)
        {
            if (node == null) return default;
            return new EventFilter
            {
                Kind = ParseEnum(node["kind"]?.GetValue<string>(), PresentationEventKind.None),
                KeyId = node["keyId"]?.GetValue<int>() ?? -1,
            };
        }

        private PerformerCommand ParsePerformerCommand(JsonNode node)
        {
            if (node == null) return default;

            string perfRef = node["performerDefinitionId"]?.GetValue<string>();
            int perfId = string.IsNullOrWhiteSpace(perfRef) ? 0 : _registry.GetId(perfRef);
            string fieldName = node["fieldName"]?.GetValue<string>() ?? node["field"]?.GetValue<string>() ?? string.Empty;
            string fieldKindText = node["fieldValueKind"]?.GetValue<string>() ?? node["valueKind"]?.GetValue<string>() ?? string.Empty;
            PresentationTypedValue fieldValue = default;
            if (!string.IsNullOrWhiteSpace(fieldName))
            {
                if (string.IsNullOrWhiteSpace(fieldKindText) ||
                    !PresentationPayloadConfigParser.TryParseTypedValueKind(fieldKindText, out var fieldKind))
                {
                    throw new InvalidOperationException($"Performer command field '{fieldName}' requires a valid fieldValueKind/valueKind.");
                }

                fieldValue = PresentationPayloadConfigParser.ParseTypedValue(
                    fieldKind,
                    node["fieldValue"] ?? node["value"] ?? node["paramValue"]);
            }

            return new PerformerCommand
            {
                CommandKind = ParseEnum(node["commandKind"]?.GetValue<string>(), PresentationCommandKind.None),
                PerformerDefinitionId = perfId,
                PerformerHandle = node["performerHandle"]?.GetValue<int>() ?? node["handle"]?.GetValue<int>() ?? 0,
                ScopeId = node["scopeId"]?.GetValue<int>() ?? -1,
                FieldName = fieldName,
                FieldValue = fieldValue,
                LegacyParamKey = node["legacyParamKey"]?.GetValue<int>() ?? node["paramKey"]?.GetValue<int>() ?? -1,
                LegacyParamValue = node["legacyParamValue"]?.GetValue<float>() ?? node["paramValue"]?.GetValue<float>() ?? 0f,
                LegacyParamGraphProgramId = node["legacyParamGraphProgramId"]?.GetValue<int>() ?? node["paramGraphProgramId"]?.GetValue<int>() ?? 0,
            };
        }

        // ── Bindings ──

        private PerformerParamBinding[] ParseBindings(JsonNode node, PerformerVisualKind visualKind, string definitionKey)
        {
            if (node is not JsonArray arr || arr.Count == 0)
                return Array.Empty<PerformerParamBinding>();

            var bindings = new PerformerParamBinding[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                bindings[i] = ParseBinding(arr[i]!, visualKind, definitionKey, i);
            }
            return bindings;
        }

        private PerformerParamBinding ParseBinding(JsonNode node, PerformerVisualKind visualKind, string definitionKey, int index)
        {
            string fieldName = node["fieldName"]?.GetValue<string>() ?? node["field"]?.GetValue<string>() ?? string.Empty;
            int paramKey = node["paramKey"]?.GetValue<int>() ?? -1;
            string valueKindText = node["valueKind"]?.GetValue<string>() ?? node["type"]?.GetValue<string>() ?? nameof(PresentationTypedValueKind.Float);
            if (!PresentationPayloadConfigParser.TryParseTypedValueKind(valueKindText, out var valueKind))
            {
                throw new InvalidOperationException($"Performer '{definitionKey}' binding[{index}] has invalid valueKind '{valueKindText}'.");
            }

            if (IsTypedRequestVisualKind(visualKind))
            {
                if (paramKey >= 0)
                {
                    throw new InvalidOperationException($"Performer '{definitionKey}' binding[{index}] targets typed visual kind '{visualKind}' and must use fieldName instead of paramKey.");
                }

                if (string.IsNullOrWhiteSpace(fieldName))
                {
                    throw new InvalidOperationException($"Performer '{definitionKey}' binding[{index}] targets typed visual kind '{visualKind}' and requires fieldName.");
                }

                if (!CanResolveDynamicValueKind(valueKind))
                {
                    throw new InvalidOperationException(
                        $"Performer '{definitionKey}' binding[{index}] valueKind '{valueKind}' cannot be resolved from dynamic scalar binding sources. Use static payload for structured/non-scalar values.");
                }
            }

            return new PerformerParamBinding
            {
                ParamKey = paramKey,
                FieldName = fieldName,
                ValueKind = valueKind,
                Value = ParseValueRef(node),
            };
        }

        private ValueRef ParseValueRef(JsonNode node)
        {
            string source = node["source"]?.GetValue<string>();
            return source?.ToLowerInvariant() switch
            {
                "attribute" => ValueRef.FromAttribute(ResolveAttributeId(node)),
                "attributeratio" => ValueRef.FromAttributeRatio(ResolveAttributeId(node)),
                "attributebase" => ValueRef.FromAttributeBase(ResolveAttributeId(node)),
                "graph" => ValueRef.FromGraph(node["sourceId"]?.GetValue<int>() ?? 0),
                "entitycolor" => ValueRef.FromEntityColor(node["sourceId"]?.GetValue<int>() ?? 0),
                "facingradians" => ValueRef.FromFacingRadians(),
                "facingdegrees" => ValueRef.FromFacingDegrees(),
                "texttoken" => ValueRef.FromConstant(ResolveTextTokenId(node)),
                _ => ValueRef.FromConstant(node["constantValue"]?.GetValue<float>() ?? 0f),
            };
        }

        private int ResolveTextTokenId(JsonNode node)
        {
            string tokenKey = node["textToken"]?.GetValue<string>() ?? node["sourceKey"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tokenKey))
            {
                throw new InvalidOperationException("Performer WorldText textToken binding requires a non-empty 'textToken'.");
            }

            int tokenId = _resolveTextTokenId(tokenKey);
            if (tokenId <= 0)
            {
                throw new InvalidOperationException($"Performer WorldText references unknown text token '{tokenKey}'.");
            }

            return tokenId;
        }

        private int ResolveAttributeId(JsonNode node)
        {
            var idNode = node["sourceId"];
            if (idNode != null) return idNode.GetValue<int>();

            string name = node["attributeName"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(name))
                return _resolveAttributeName(name);

            return 0;
        }

        // ── ConditionRef ──

        private ConditionRef ParseConditionRef(JsonNode node)
        {
            if (node == null) return ConditionRef.AlwaysTrue;

            var cond = new ConditionRef();
            string inline = node["inline"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(inline))
            {
                cond.Inline = ParseEnum(inline, InlineConditionKind.None);
            }
            cond.GraphProgramId = node["graphProgramId"]?.GetValue<int>() ?? 0;
            return cond;
        }

        // ── Vector3 ──

        private Vector3 ParseVector3(JsonNode node)
        {
            if (node is JsonArray arr && arr.Count >= 3)
            {
                return new Vector3(
                    arr[0]?.GetValue<float>() ?? 0f,
                    arr[1]?.GetValue<float>() ?? 0f,
                    arr[2]?.GetValue<float>() ?? 0f);
            }
            return Vector3.Zero;
        }

        // ── Color ──

        private Vector4 ParseColor(JsonNode node)
        {
            if (node is JsonArray arr && arr.Count >= 4)
            {
                return new Vector4(
                    arr[0]?.GetValue<float>() ?? 1f,
                    arr[1]?.GetValue<float>() ?? 1f,
                    arr[2]?.GetValue<float>() ?? 1f,
                    arr[3]?.GetValue<float>() ?? 1f);
            }
            return new Vector4(1f, 1f, 1f, 1f);
        }

        // ── Enum parsing ──

        private static T ParseEnum<T>(string s, T fallback) where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            if (Enum.TryParse<T>(s, ignoreCase: true, out var parsed)) return parsed;
            return fallback;
        }

        private static T ParseEnumStrict<T>(string s, T fallback, string label) where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            if (Enum.TryParse<T>(s, ignoreCase: true, out var parsed)) return parsed;
            throw new InvalidOperationException($"{label} has invalid value '{s}'.");
        }

        private static bool IsTypedRequestVisualKind(PerformerVisualKind kind)
        {
            return kind == PerformerVisualKind.Decal ||
                kind == PerformerVisualKind.Vfx ||
                kind == PerformerVisualKind.Surface ||
                kind == PerformerVisualKind.MaterialOverride ||
                kind == PerformerVisualKind.InstanceCustomData;
        }

        private static bool CanResolveDynamicValueKind(PresentationTypedValueKind kind)
        {
            return kind == PresentationTypedValueKind.Bool ||
                kind == PresentationTypedValueKind.Int ||
                kind == PresentationTypedValueKind.Float ||
                kind == PresentationTypedValueKind.Vector4 ||
                kind == PresentationTypedValueKind.Color;
        }

        private static void ValidateTypedPerformerDefinition(PerformerDefinition def, string key)
        {
            if (!IsTypedRequestVisualKind(def.VisualKind))
            {
                return;
            }

            if (def.VisualKind == PerformerVisualKind.Decal || def.VisualKind == PerformerVisualKind.Vfx)
            {
                if (string.IsNullOrWhiteSpace(def.VisualAssetKey))
                {
                    throw new InvalidOperationException($"Performer '{key}' visualKind '{def.VisualKind}' requires assetKey.");
                }
            }

            if (def.VisualKind == PerformerVisualKind.Surface &&
                string.IsNullOrWhiteSpace(def.SurfaceLayerKey))
            {
                throw new InvalidOperationException($"Performer '{key}' visualKind Surface requires surfaceLayerKey.");
            }

            if ((def.VisualKind == PerformerVisualKind.MaterialOverride ||
                    def.VisualKind == PerformerVisualKind.InstanceCustomData) &&
                string.IsNullOrWhiteSpace(def.MaterialKey))
            {
                throw new InvalidOperationException($"Performer '{key}' visualKind '{def.VisualKind}' requires materialKey.");
            }
        }
    }
}
