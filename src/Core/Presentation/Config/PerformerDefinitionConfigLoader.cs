using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Config
{
    /// <summary>
    /// Loads <see cref="PerformerDefinition"/> entries from
    /// <c>Presentation/performers.json</c> via <see cref="ConfigPipeline"/>.
    /// </summary>
    public sealed class PerformerDefinitionConfigLoader
    {
        private readonly ConfigPipeline _configs;
        private readonly PerformerDefinitionRegistry _registry;
        private readonly Func<string, int> _resolveAttributeName;

        /// <param name="resolveAttributeName">
        /// Resolves an attribute name (e.g. "Health") to its integer ID.
        /// Injected to avoid direct coupling to <c>Gameplay.GAS.Registry.AttributeRegistry</c>.
        /// </param>
        public PerformerDefinitionConfigLoader(ConfigPipeline configs, PerformerDefinitionRegistry registry, Func<string, int> resolveAttributeName = null)
        {
            _configs = configs;
            _registry = registry;
            _resolveAttributeName = resolveAttributeName ?? (_ => 0);
        }

        /// <summary>
        /// Load and register all performer definitions. Safe to call if config file is missing (no-op).
        /// </summary>
        public void Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Presentation/performers.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            if (merged.Count == 0) return;

            for (int i = 0; i < merged.Count; i++)
            {
                var def = ParseDefinition(merged[i].Node);
                if (def != null)
                    _registry.Register(def.Id, def);
            }
        }

        private PerformerDefinition? ParseDefinition(JsonNode node)
        {
            var def = new PerformerDefinition();
            def.Id = node["id"]?.GetValue<int>() ?? 0;
            if (def.Id <= 0) return null;

            def.VisualKind = ParseEnum(node["visualKind"]?.GetValue<string>(), PerformerVisualKind.GroundOverlay);
            def.EntityScope = ParseEnum(node["entityScope"]?.GetValue<string>(), EntityScopeFilter.None);
            def.MeshOrShapeId = node["meshOrShapeId"]?.GetValue<int>() ?? 0;
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

            return def;
        }

        // ── Rules ──

        private PerformerRule[] ParseRules(JsonNode? node)
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

        private EventFilter ParseEventFilter(JsonNode? node)
        {
            if (node == null) return default;
            return new EventFilter
            {
                Kind = ParseEnum(node["kind"]?.GetValue<string>(), PresentationEventKind.None),
                KeyId = node["keyId"]?.GetValue<int>() ?? -1,
            };
        }

        private PerformerCommand ParsePerformerCommand(JsonNode? node)
        {
            if (node == null) return default;
            return new PerformerCommand
            {
                CommandKind = ParseEnum(node["commandKind"]?.GetValue<string>(), PresentationCommandKind.None),
                PerformerDefinitionId = node["performerDefinitionId"]?.GetValue<int>() ?? 0,
                ScopeId = node["scopeId"]?.GetValue<int>() ?? -1,
                ParamKey = node["paramKey"]?.GetValue<int>() ?? 0,
                ParamValue = node["paramValue"]?.GetValue<float>() ?? 0f,
                ParamGraphProgramId = node["paramGraphProgramId"]?.GetValue<int>() ?? 0,
            };
        }

        // ── Bindings ──

        private PerformerParamBinding[] ParseBindings(JsonNode? node)
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
            string? source = node["source"]?.GetValue<string>();
            return source?.ToLowerInvariant() switch
            {
                "attribute" => ValueRef.FromAttribute(ResolveAttributeId(node)),
                "attributeratio" => ValueRef.FromAttributeRatio(ResolveAttributeId(node)),
                "attributebase" => ValueRef.FromAttributeBase(ResolveAttributeId(node)),
                "graph" => ValueRef.FromGraph(node["sourceId"]?.GetValue<int>() ?? 0),
                "entitycolor" => ValueRef.FromEntityColor(node["sourceId"]?.GetValue<int>() ?? 0),
                _ => ValueRef.FromConstant(node["constantValue"]?.GetValue<float>() ?? 0f),
            };
        }

        /// <summary>
        /// Resolves an attribute reference from JSON. Supports both:
        ///   "sourceId": 3              (numeric ID directly)
        ///   "attributeName": "Health"  (resolved via injected resolver)
        /// </summary>
        private int ResolveAttributeId(JsonNode node)
        {
            // Prefer explicit numeric ID
            var idNode = node["sourceId"];
            if (idNode != null) return idNode.GetValue<int>();

            // Fall back to name-based resolution via injected delegate
            string? name = node["attributeName"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(name))
                return _resolveAttributeName(name);

            return 0;
        }

        // ── ConditionRef ──

        private ConditionRef ParseConditionRef(JsonNode? node)
        {
            if (node == null) return ConditionRef.AlwaysTrue;

            var cond = new ConditionRef();
            string? inline = node["inline"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(inline))
            {
                cond.Inline = ParseEnum(inline, InlineConditionKind.None);
            }
            cond.GraphProgramId = node["graphProgramId"]?.GetValue<int>() ?? 0;
            return cond;
        }

        // ── Vector3 ──

        private Vector3 ParseVector3(JsonNode? node)
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

        private Vector4 ParseColor(JsonNode? node)
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

        private static T ParseEnum<T>(string? s, T fallback) where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            if (Enum.TryParse<T>(s, ignoreCase: true, out var parsed)) return parsed;
            return fallback;
        }
    }
}
