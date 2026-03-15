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
            def.VisualKind = ParseEnum(node["visualKind"]?.GetValue<string>(), PerformerVisualKind.GroundOverlay);
            def.EntityScope = ParseEnum(node["entityScope"]?.GetValue<string>(), EntityScopeFilter.None);
            def.MeshOrShapeId = ResolveMeshOrShape(node["meshOrShapeId"], def.VisualKind);
            def.DefaultColor = ParseColor(node["defaultColor"]);
            def.DefaultScale = node["defaultScale"]?.GetValue<float>() ?? 1f;
            def.DefaultLifetime = node["defaultLifetime"]?.GetValue<float>() ?? 0f;
            def.DefaultFontSize = node["defaultFontSize"]?.GetValue<int>() ?? 16;
            def.PositionOffset = ParseVector3(node["positionOffset"]);
            def.PositionYDriftPerSecond = node["positionYDriftPerSecond"]?.GetValue<float>() ?? 0f;
            def.AlphaFadeOverLifetime = node["alphaFadeOverLifetime"]?.GetValue<bool>() ?? false;
            def.VisibilityCondition = ParseConditionRef(node["visibility"]);
            def.Rules = ParseRules(node["rules"]);
            def.Bindings = ParseBindings(node["bindings"]);

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

            return new PerformerCommand
            {
                CommandKind = ParseEnum(node["commandKind"]?.GetValue<string>(), PresentationCommandKind.None),
                PerformerDefinitionId = perfId,
                ScopeId = node["scopeId"]?.GetValue<int>() ?? -1,
                ParamKey = node["paramKey"]?.GetValue<int>() ?? 0,
                ParamValue = node["paramValue"]?.GetValue<float>() ?? 0f,
                ParamGraphProgramId = node["paramGraphProgramId"]?.GetValue<int>() ?? 0,
            };
        }

        // ── Bindings ──

        private PerformerParamBinding[] ParseBindings(JsonNode node)
        {
            if (node is not JsonArray arr || arr.Count == 0)
                return Array.Empty<PerformerParamBinding>();

            var bindings = new PerformerParamBinding[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                bindings[i] = ParseBinding(arr[i]!);
            }
            return bindings;
        }

        private PerformerParamBinding ParseBinding(JsonNode node)
        {
            return new PerformerParamBinding
            {
                ParamKey = node["paramKey"]?.GetValue<int>() ?? 0,
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
    }
}
