using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Config
{
    /// <summary>
    /// Loads <see cref="PerformerDefinition"/> entries from
    /// <c>Presentation/performers.json</c>.
    /// </summary>
    public sealed class PerformerDefinitionConfigLoader
    {
        private readonly ConfigPipeline _configs;
        private readonly PerformerDefinitionRegistry _registry;
        private readonly Func<string, int> _resolveAttributeName;
        private readonly Func<string, int> _resolveMeshId;
        private readonly Func<string, int> _resolveTextTokenId;
        private readonly Func<string, int> _resolveEntityTemplateKey;
        private readonly Func<string, int> _resolveEffectTemplateId;
        private readonly Func<string, int> _resolveMaterialId;
        private readonly Func<string, int> _resolveAnimatorControllerId;
        private readonly Func<string, int> _resolveAnimationProfileId;
        private readonly Func<AssetKind, string, int> _resolveBehaviorAssetId;

        public PerformerDefinitionConfigLoader(
            ConfigPipeline configs,
            PerformerDefinitionRegistry registry,
            Func<string, int> resolveAttributeName = null,
            Func<string, int> resolveMeshId = null,
            Func<string, int> resolveTextTokenId = null,
            Func<string, int> resolveEntityTemplateKey = null,
            Func<string, int> resolveEffectTemplateId = null,
            Func<string, int> resolveMaterialId = null,
            Func<string, int> resolveAnimatorControllerId = null,
            Func<string, int> resolveAnimationProfileId = null,
            Func<AssetKind, string, int> resolveBehaviorAssetId = null)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _resolveAttributeName = resolveAttributeName ?? (_ => 0);
            _resolveMeshId = resolveMeshId ?? (_ => 0);
            _resolveTextTokenId = resolveTextTokenId ?? (_ => 0);
            _resolveEntityTemplateKey = resolveEntityTemplateKey ?? (_ => 0);
            _resolveEffectTemplateId = resolveEffectTemplateId ?? (_ => 0);
            _resolveMaterialId = resolveMaterialId ?? (_ => 0);
            _resolveAnimatorControllerId = resolveAnimatorControllerId ?? (_ => 0);
            _resolveAnimationProfileId = resolveAnimationProfileId ?? (_ => 0);
            _resolveBehaviorAssetId = resolveBehaviorAssetId ?? ((_, __) => 0);
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Presentation/performers.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            if (report != null)
            {
                var deletions = report.GetDeletions(entry.RelativePath);
                for (int i = 0; i < deletions.Count; i++)
                {
                    _registry.Unregister(deletions[i].Id);
                }
            }

            if (merged.Count == 0)
            {
                return;
            }

            var mergedByKey = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
            var parsedByKey = new Dictionary<string, PerformerDefinition>(StringComparer.OrdinalIgnoreCase);
            var parsedOrder = new List<string>(merged.Count);
            for (int i = 0; i < merged.Count; i++)
            {
                if (merged[i].Node is not JsonObject obj)
                {
                    continue;
                }

                string key = obj["id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                mergedByKey[key] = obj;
                _registry.GetOrRegisterId(key);
            }

            foreach ((string key, JsonObject _) in mergedByKey)
            {
                try
                {
                    JsonObject expanded = ExpandDefinition(key, mergedByKey, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    var (_, def) = ParseDefinition(expanded);
                    if (def != null)
                    {
                        parsedByKey[key] = def;
                        parsedOrder.Add(key);
                    }
                }
                catch (Exception ex)
                {
                    _registry.Unregister(key);
                    Trace.WriteLine($"[PerformerDefinitionConfigLoader] Skipping performer '{key}': {ex.Message}");
                }
            }

            var validByKey = new Dictionary<string, PerformerDefinition>(parsedByKey, StringComparer.OrdinalIgnoreCase);
            bool removedInvalidDefinition;
            do
            {
                removedInvalidDefinition = false;
                for (int i = 0; i < parsedOrder.Count; i++)
                {
                    string key = parsedOrder[i];
                    if (!validByKey.TryGetValue(key, out PerformerDefinition? definition))
                    {
                        continue;
                    }

                    try
                    {
                        ValidateRuleReferences(key, definition, validByKey);
                        ValidateChildGraph(key, validByKey, new HashSet<int>(), new List<string>());
                    }
                    catch (Exception ex)
                    {
                        validByKey.Remove(key);
                        _registry.Unregister(key);
                        Trace.WriteLine($"[PerformerDefinitionConfigLoader] Skipping performer '{key}': {ex.Message}");
                        removedInvalidDefinition = true;
                    }
                }
            }
            while (removedInvalidDefinition);

            for (int i = 0; i < parsedOrder.Count; i++)
            {
                string key = parsedOrder[i];
                if (!validByKey.TryGetValue(key, out PerformerDefinition? definition))
                {
                    continue;
                }

                _registry.Register(key, definition);
            }
        }

        private JsonObject ExpandDefinition(
            string key,
            IReadOnlyDictionary<string, JsonObject> mergedByKey,
            HashSet<string> expansionStack)
        {
            if (!mergedByKey.TryGetValue(key, out JsonObject? node))
            {
                throw new InvalidOperationException($"Performer '{key}' is missing from merged config.");
            }

            if (!expansionStack.Add(key))
            {
                throw new InvalidOperationException($"Performer definition inheritance cycle detected at '{key}'.");
            }

            try
            {
                string parentKey = node["extends"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(parentKey))
                {
                    return (JsonObject)node.DeepClone();
                }

                if (!mergedByKey.TryGetValue(parentKey, out _))
                {
                    throw new InvalidOperationException($"Performer '{key}' extends unknown definition '{parentKey}'.");
                }

                JsonObject expandedParent = ExpandDefinition(parentKey, mergedByKey, expansionStack);
                return MergeDefinitionObjects(expandedParent, node);
            }
            finally
            {
                expansionStack.Remove(key);
            }
        }

        private static JsonObject MergeDefinitionObjects(JsonObject parent, JsonObject child)
        {
            var merged = (JsonObject)parent.DeepClone();
            foreach ((string propertyName, JsonNode? childValue) in child)
            {
                if (propertyName.Equals("bindings", StringComparison.OrdinalIgnoreCase))
                {
                    merged[propertyName] = MergeByIntKey(parent[propertyName], childValue, "paramKey");
                    continue;
                }

                if (propertyName.Equals("paramDefaults", StringComparison.OrdinalIgnoreCase))
                {
                    merged[propertyName] = MergeParamDefaultsJson(parent[propertyName], childValue);
                    continue;
                }

                if (propertyName.Equals("behaviors", StringComparison.OrdinalIgnoreCase))
                {
                    merged[propertyName] = MergeByIntKey(parent[propertyName], childValue, "slot");
                    continue;
                }

                if (propertyName.Equals("rules", StringComparison.OrdinalIgnoreCase) ||
                    propertyName.Equals("children", StringComparison.OrdinalIgnoreCase))
                {
                    merged[propertyName] = AppendArrays(parent[propertyName], childValue);
                    continue;
                }

                merged[propertyName] = childValue?.DeepClone();
            }

            return merged;
        }

        private static JsonArray AppendArrays(JsonNode? existingNode, JsonNode? incomingNode)
        {
            var merged = new JsonArray();
            AppendArrayItems(merged, existingNode);
            AppendArrayItems(merged, incomingNode);
            return merged;
        }

        private static void AppendArrayItems(JsonArray destination, JsonNode? node)
        {
            if (node is not JsonArray array)
            {
                return;
            }

            for (int i = 0; i < array.Count; i++)
            {
                destination.Add(array[i]?.DeepClone());
            }
        }

        private static JsonArray MergeByIntKey(JsonNode? existingNode, JsonNode? incomingNode, string keyField)
        {
            var byKey = new Dictionary<int, JsonNode>();
            var order = new List<int>();
            AppendByIntKey(existingNode, keyField, byKey, order);
            AppendByIntKey(incomingNode, keyField, byKey, order);

            var merged = new JsonArray();
            for (int i = 0; i < order.Count; i++)
            {
                merged.Add(byKey[order[i]].DeepClone());
            }

            return merged;
        }

        private static void AppendByIntKey(JsonNode? node, string keyField, Dictionary<int, JsonNode> byKey, List<int> order)
        {
            if (node is not JsonArray array)
            {
                return;
            }

            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject obj)
                {
                    continue;
                }

                int key = obj[keyField]?.GetValue<int>() ?? i;
                if (!byKey.ContainsKey(key))
                {
                    order.Add(key);
                }

                byKey[key] = obj;
            }
        }

        private static JsonArray MergeParamDefaultsJson(JsonNode? existingNode, JsonNode? incomingNode)
        {
            var byKey = new Dictionary<(int ParamKey, ParamLane Lane), JsonNode>();
            var order = new List<(int ParamKey, ParamLane Lane)>();
            AppendParamDefaultsJson(existingNode, byKey, order);
            AppendParamDefaultsJson(incomingNode, byKey, order);

            var merged = new JsonArray();
            for (int i = 0; i < order.Count; i++)
            {
                merged.Add(byKey[order[i]].DeepClone());
            }

            return merged;
        }

        private static void AppendParamDefaultsJson(
            JsonNode? node,
            Dictionary<(int ParamKey, ParamLane Lane), JsonNode> byKey,
            List<(int ParamKey, ParamLane Lane)> order)
        {
            if (node is not JsonArray array)
            {
                return;
            }

            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject obj)
                {
                    continue;
                }

                int paramKey = obj["paramKey"]?.GetValue<int>() ?? 0;
                ParamLane lane = ParseRequiredParamLane(obj, $"paramDefaults[{i}]");
                var compositeKey = (paramKey, lane);
                if (!byKey.ContainsKey(compositeKey))
                {
                    order.Add(compositeKey);
                }

                byKey[compositeKey] = obj;
            }
        }

        private (string key, PerformerDefinition def) ParseDefinition(JsonNode node)
        {
            string key = node["id"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                return (null, null);
            }

            RejectLegacyFields(node, key);

            var def = new PerformerDefinition
            {
                Key = key,
                Extends = node["extends"]?.GetValue<string>() ?? string.Empty,
                DefaultColor = ParseColor(node["defaultColor"]),
                DefaultLifetime = node["defaultLifetime"]?.GetValue<float>() ?? 0f,
                DefaultFontSize = node["defaultFontSize"]?.GetValue<int>() ?? 16,
                DefaultTextId = ResolveOptionalTextTokenId(node["defaultTextId"]),
                LegacyWorldTextMode = ParseEnum(node["legacyWorldTextMode"]?.GetValue<string>(), WorldHudValueMode.None),
                PositionOffset = ParseVector3(node["positionOffset"]),
                PositionYDriftPerSecond = node["positionYDriftPerSecond"]?.GetValue<float>() ?? 0f,
                AlphaFadeOverLifetime = node["alphaFadeOverLifetime"]?.GetValue<bool>() ?? false,
                VisibilityCondition = ParseConditionRef(node["visibility"]),
                Rules = ParseRules(node["rules"]),
                Bindings = ParseBindings(node["bindings"]),
                Children = ParseChildren(node["children"]),
                Behaviors = ParseBehaviors(node["behaviors"], key),
                ParamDefaults = ParseParamDefaults(node["paramDefaults"]),
            };

            def.Id = _registry.GetId(key);

            if (node["surface"] != null)
            {
                def.Surface = ParseSurface(node["surface"], key);
            }

            StampRuleOwners(def.Id, def.Rules);
            return (key, def);
        }

        private static void RejectLegacyFields(JsonNode node, string key)
        {
            if (node["entityScope"] != null)
            {
                throw new InvalidOperationException(
                    $"Performer '{key}' still uses removed field 'entityScope'. Migrate it to lifecycle rules.");
            }

            if (node["requiredTemplate"] != null)
            {
                throw new InvalidOperationException(
                    $"Performer '{key}' still uses removed field 'requiredTemplate'. Migrate it to event.key + lifecycle rules.");
            }

            string[] removedVisualFields =
            {
                "visualKind",
                "meshOrShapeId",
                "defaultScale",
            };

            for (int i = 0; i < removedVisualFields.Length; i++)
            {
                string field = removedVisualFields[i];
                if (node[field] != null)
                {
                    throw new InvalidOperationException(
                        $"Performer '{key}' still uses removed field '{field}'. Migrate visual output to behaviors[].assetBinding.");
                }
            }
        }

        private PerformerRule[] ParseRules(JsonNode? node)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                return Array.Empty<PerformerRule>();
            }

            var rules = new PerformerRule[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                rules[i] = new PerformerRule
                {
                    Event = ParseEventFilter(arr[i]!["event"]),
                    Condition = ParseConditionRef(arr[i]!["condition"]),
                    Command = ParsePerformerCommand(arr[i]!["command"]),
                };
            }

            return rules;
        }

        private EventFilter ParseEventFilter(JsonNode? node)
        {
            if (node == null)
            {
                return default;
            }

            PresentationEventKind kind = ParseEnum(node["kind"]?.GetValue<string>(), PresentationEventKind.None);
            return new EventFilter
            {
                Kind = kind,
                KeyId = ResolveEventKey(kind, node),
            };
        }

        private int ResolveEventKey(PresentationEventKind kind, JsonNode node)
        {
            if (node["keyId"] is JsonValue keyIdValue)
            {
                if (keyIdValue.TryGetValue<int>(out int numericId))
                {
                    return numericId;
                }

                if (keyIdValue.TryGetValue<string>(out string textKey))
                {
                    return ResolveEventKey(kind, textKey);
                }
            }

            string key = node["key"]?.GetValue<string>() ?? string.Empty;
            return string.IsNullOrWhiteSpace(key) ? -1 : ResolveEventKey(kind, key);
        }

        private int ResolveEventKey(PresentationEventKind kind, string key)
        {
            return kind switch
            {
                PresentationEventKind.EntitySpawned => ResolveRequired(_resolveEntityTemplateKey(key), kind, "entity template", key),
                PresentationEventKind.EntityDestroyed => ResolveRequired(_resolveEntityTemplateKey(key), kind, "entity template", key),
                PresentationEventKind.ProjectileSpawned => ResolveRequired(_resolveEffectTemplateId(key), kind, "effect template", key),
                PresentationEventKind.TagEffectiveChanged => TagRegistry.Register(key),
                PresentationEventKind.GameplayEvent => TagRegistry.Register(key),
                _ => throw new InvalidOperationException($"Presentation event kind '{kind}' does not support string key '{key}'."),
            };
        }

        private static int ResolveRequired(int id, PresentationEventKind kind, string subject, string key)
        {
            if (id <= 0)
            {
                throw new InvalidOperationException($"Presentation event '{kind}' references unknown {subject} '{key}'.");
            }

            return id;
        }

        private PerformerCommand ParsePerformerCommand(JsonNode? node)
        {
            if (node == null)
            {
                return default;
            }

            return new PerformerCommand
            {
                CommandKind = ParseEnum(node["kind"]?.GetValue<string>(), PerformerCommandKind.None),
                PerformerDefinitionId = ResolvePerformerDefinitionId(node["definitionId"]),
                ParentEntity = Entity.Null, // resolved at runtime
                ScopeTag = ParseScopeTag(node["scopeTag"]),
                ScopeSource = ParseEnum(node["scopeSource"]?.GetValue<string>(), PerformerCommandScopeSource.Fixed),
                ParamKey = node["paramKey"]?.GetValue<int>() ?? 0,
                ParamLane = ParseEnum(node["paramLane"]?.GetValue<string>(), ParamLane.Float),
                ParamValue = node["paramValue"]?.GetValue<float>() ?? 0f,
                IntValue = node["intValue"]?.GetValue<int>() ?? 0,
                VectorValue = ParseVector4(node["vectorValue"]),
                ValueSource = ParseEnum(node["valueSource"]?.GetValue<string>(), PerformerCommandValueSource.Fixed),
                ParamGraphProgramId = node["paramGraphProgramId"]?.GetValue<int>() ?? 0,
                TargetBehaviorSlot = node["targetBehaviorSlot"]?.GetValue<int>() ?? -1,
            };
        }

        private int ResolvePerformerDefinitionId(JsonNode? node)
        {
            if (node == null)
            {
                return 0;
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    return numericId;
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    int id = _registry.GetId(key);
                    if (id <= 0)
                    {
                        throw new InvalidOperationException($"Performer command references unknown definition '{key}'.");
                    }

                    return id;
                }
            }

            return 0;
        }

        private PerformerParamBinding[] ParseBindings(JsonNode? node)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                return Array.Empty<PerformerParamBinding>();
            }

            var bindings = new PerformerParamBinding[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                bindings[i] = new PerformerParamBinding
                {
                    ParamKey = arr[i]!["paramKey"]?.GetValue<int>() ?? 0,
                    Value = ParseValueRef(arr[i]!),
                };
            }

            return bindings;
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
                "entitycolorvector" => ValueRef.FromEntityColorVector(),
                "facingradians" => ValueRef.FromFacingRadians(),
                "facingdegrees" => ValueRef.FromFacingDegrees(),
                "texttoken" => ValueRef.FromConstant(ResolveTextTokenId(node)),
                _ => ValueRef.FromConstant(node["constantValue"]?.GetValue<float>() ?? 0f),
            };
        }

        private int ResolveTextTokenId(JsonNode node)
        {
            string tokenKey = node["textToken"]?.GetValue<string>() ?? string.Empty;
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

        private int ResolveOptionalTextTokenId(JsonNode? node)
        {
            if (node == null)
            {
                return 0;
            }

            if (node is JsonValue textValue && textValue.TryGetValue<string>(out string? tokenKey))
            {
                if (string.IsNullOrWhiteSpace(tokenKey))
                {
                    return 0;
                }

                int tokenId = _resolveTextTokenId(tokenKey);
                if (tokenId < 0)
                {
                    throw new InvalidOperationException($"Performer defaultTextId references unknown text token '{tokenKey}'.");
                }

                return tokenId;
            }

            throw new InvalidOperationException("Performer defaultTextId must be a text token string.");
        }

        private int ResolveAttributeId(JsonNode node)
        {
            JsonNode? idNode = node["attributeId"];
            if (idNode is JsonValue value && value.TryGetValue<int>(out int numericId))
            {
                return numericId;
            }

            string name = node["attributeName"]?.GetValue<string>() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
            {
                int id = _resolveAttributeName(name);
                if (id >= 0)
                {
                    return id;
                }
            }

            throw new InvalidOperationException("Performer attribute binding requires 'attributeId' or non-empty 'attributeName'.");
        }

        private static int ParseScopeTag(JsonNode? node)
        {
            if (node == null)
            {
                return -1;
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    return numericId;
                }

                if (value.TryGetValue<string>(out string text))
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return -1;
                    }

                    if (int.TryParse(text, out int parsed))
                    {
                        return parsed;
                    }

                    return PerformerScopeTagRegistry.Register(text);
                }
            }

            throw new InvalidOperationException("Performer scopeTag must be an int or non-empty string.");
        }

        private ChildPerformerRef[] ParseChildren(JsonNode? node)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                return Array.Empty<ChildPerformerRef>();
            }

            var children = new ChildPerformerRef[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Performer child[{i}] must be an object.");
                }

                children[i] = new ChildPerformerRef
                {
                    DefinitionId = ResolvePerformerDefinitionId(obj["definitionId"]),
                    ScopeTag = ParseScopeTag(obj["scopeTag"]),
                    ParamOverrides = ParseParamDefaults(obj["paramOverrides"]),
                };
            }

            return children;
        }

        private static void ValidateChildGraph(
            string key,
            IReadOnlyDictionary<string, PerformerDefinition> parsedByKey,
            HashSet<int> pathIds,
            List<string> path)
        {
            if (!parsedByKey.TryGetValue(key, out PerformerDefinition definition))
            {
                throw new InvalidOperationException($"Performer '{key}' is missing from the parsed definition graph.");
            }

            path.Add(key);
            pathIds.Add(definition.Id);

            try
            {
                ChildPerformerRef[] children = definition.Children;
                if (children == null || children.Length == 0)
                {
                    return;
                }

                for (int i = 0; i < children.Length; i++)
                {
                    int childDefinitionId = children[i].DefinitionId;
                    if (childDefinitionId <= 0)
                    {
                        throw new InvalidOperationException($"Performer '{key}' child[{i}] references an unknown definition.");
                    }

                    string childKey = definition.Key;
                    childKey = string.Empty;
                    foreach ((string parsedKey, PerformerDefinition parsedDefinition) in parsedByKey)
                    {
                        if (parsedDefinition.Id == childDefinitionId)
                        {
                            childKey = parsedKey;
                            break;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(childKey))
                    {
                        throw new InvalidOperationException($"Performer '{key}' child[{i}] references definition id={childDefinitionId} that failed to load.");
                    }

                    if (pathIds.Contains(childDefinitionId))
                    {
                        var cyclePath = new List<string>(path.Count + 1);
                        cyclePath.AddRange(path);
                        cyclePath.Add(childKey);
                        throw new InvalidOperationException($"Circular child reference detected: {string.Join("->", cyclePath)}");
                    }

                    ValidateChildGraph(childKey, parsedByKey, pathIds, path);
                }
            }
            finally
            {
                path.RemoveAt(path.Count - 1);
                pathIds.Remove(definition.Id);
            }
        }

        private static void ValidateRuleReferences(
            string key,
            PerformerDefinition definition,
            IReadOnlyDictionary<string, PerformerDefinition> parsedByKey)
        {
            PerformerRule[] rules = definition.Rules;
            if (rules == null || rules.Length == 0)
            {
                return;
            }

            for (int i = 0; i < rules.Length; i++)
            {
                ref readonly PerformerRule rule = ref rules[i];
                if (rule.Command.CommandKind != PerformerCommandKind.CreatePerformer)
                {
                    continue;
                }

                int referencedDefinitionId = rule.Command.PerformerDefinitionId;
                if (referencedDefinitionId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Performer '{key}' rule[{i}] references an unknown child definition.");
                }

                if (!ContainsDefinitionId(parsedByKey, referencedDefinitionId))
                {
                    throw new InvalidOperationException(
                        $"Performer '{key}' rule[{i}] references definition id={referencedDefinitionId} that failed to load.");
                }
            }
        }

        private static bool ContainsDefinitionId(
            IReadOnlyDictionary<string, PerformerDefinition> parsedByKey,
            int definitionId)
        {
            foreach ((string _, PerformerDefinition parsedDefinition) in parsedByKey)
            {
                if (parsedDefinition.Id == definitionId)
                {
                    return true;
                }
            }

            return false;
        }

        private BehaviorSlot[] ParseBehaviors(JsonNode? node, string ownerKey)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                return Array.Empty<BehaviorSlot>();
            }

            if (arr.Count > 32)
            {
                throw new InvalidOperationException($"Performer '{ownerKey}' exceeds the max 32 behaviors per performer limit.");
            }

            var slots = new BehaviorSlot[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Performer behavior[{i}] must be an object.");
                }

                BehaviorKind kind = ParseRequiredEnum<BehaviorKind>(obj["kind"], $"Performer '{ownerKey}' behavior[{i}].kind");
                int slotIndex = obj["slot"]?.GetValue<int>() ?? i;
                if (slotIndex is < 0 or >= 32)
                {
                    throw new InvalidOperationException($"Performer '{ownerKey}' behavior[{i}] uses slot {slotIndex}, but valid behavior slots are 0-31.");
                }

                var slot = new BehaviorSlot
                {
                    SlotIndex = slotIndex,
                    Kind = kind,
                    ActiveByDefault = obj["activeByDefault"]?.GetValue<bool>() ?? false,
                    ActivationCondition = ParseConditionRef(obj["activationCondition"]),
                };

                switch (kind)
                {
                    case BehaviorKind.AssetBinding:
                        slot.AssetBinding = ParseAssetBinding(obj["assetBinding"]);
                        break;
                    case BehaviorKind.AttributeBinding:
                        slot.AttributeBinding = ParseAttributeBinding(obj["attributeBinding"]);
                        break;
                    case BehaviorKind.TagBinding:
                        slot.TagBinding = ParseTagBinding(obj["tagBinding"]);
                        break;
                    case BehaviorKind.Animator:
                        slot.Animator = ParseAnimator(obj["animator"]);
                        break;
                    case BehaviorKind.Attachment:
                        slot.Attachment = ParseAttachment(obj["attachment"]);
                        break;
                    case BehaviorKind.Sound:
                        slot.Sound = ParseSound(obj["sound"]);
                        break;
                    case BehaviorKind.Material:
                        slot.Material = ParseMaterial(obj["material"]);
                        break;
                    case BehaviorKind.Spline:
                        slot.Spline = ParseSpline(obj["spline"]);
                        break;
                    case BehaviorKind.Grounding:
                        slot.Grounding = ParseGrounding(obj["grounding"]);
                        break;
                    case BehaviorKind.MinimapMarker:
                        slot.MinimapMarker = ParseMinimapMarker(obj["minimapMarker"]);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported performer behavior kind '{kind}'.");
                }

                slots[i] = slot;
            }

            return slots;
        }

        private ParamDefault[] ParseParamDefaults(JsonNode? node)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                return Array.Empty<ParamDefault>();
            }

            var defaults = new ParamDefault[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"paramDefaults[{i}] must be an object.");
                }

                ParamLane lane = ParseRequiredParamLane(obj, $"paramDefaults[{i}]");
                var paramDefault = new ParamDefault
                {
                    ParamKey = obj["paramKey"]?.GetValue<int>() ?? 0,
                    Lane = lane,
                };

                switch (lane)
                {
                    case ParamLane.Int:
                        if (obj["intValue"] is not JsonValue intValueNode || !intValueNode.TryGetValue<int>(out int intValue))
                        {
                            throw new InvalidOperationException($"paramDefaults[{i}] lane '{ParamLane.Int}' requires integer field 'intValue'.");
                        }

                        paramDefault.IntValue = intValue;
                        break;
                    case ParamLane.Vector:
                        if (obj["vectorValue"] is not JsonArray vectorValueNode || vectorValueNode.Count < 4)
                        {
                            throw new InvalidOperationException($"paramDefaults[{i}] lane '{ParamLane.Vector}' requires 4-component array field 'vectorValue'.");
                        }

                        paramDefault.VectorValue = ParseVector4(vectorValueNode);
                        break;
                    default:
                        if (obj["floatValue"] is not JsonValue floatValueNode || !floatValueNode.TryGetValue<float>(out float floatValue))
                        {
                            throw new InvalidOperationException($"paramDefaults[{i}] lane '{ParamLane.Float}' requires numeric field 'floatValue'.");
                        }

                        paramDefault.FloatValue = floatValue;
                        break;
                }

                defaults[i] = paramDefault;
            }

            return defaults;
        }

        private AssetBindingConfig ParseAssetBinding(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException("AssetBinding behavior requires object field 'assetBinding'.");
            }

            if (obj.ContainsKey("grounding") || obj.ContainsKey("groundingOffset"))
            {
                throw new InvalidOperationException(
                    "AssetBinding must not declare grounding or groundingOffset. Use a Grounding behavior with an explicit updatePolicy.");
            }

            AssetKind assetKind = ParseEnum(obj["assetKind"]?.GetValue<string>(), AssetKind.Mesh);
            return new AssetBindingConfig
            {
                AssetKind = assetKind,
                AssetId = assetKind == AssetKind.GroundOverlay
                    ? ResolveGroundOverlayShapeId(obj["assetId"])
                    : ResolveBehaviorAssetId(assetKind, obj["assetId"]),
                MaterialId = ResolveRegisteredId(_resolveMaterialId, obj["materialId"], "material"),
                RenderPath = ParseEnum(obj["renderPath"]?.GetValue<string>(), VisualRenderPath.None),
                Mobility = ParseEnum(obj["mobility"]?.GetValue<string>(), VisualMobility.Movable),
                LocalOffset = ParseVector3(obj["localOffset"]),
                LocalRotation = ParseQuaternion(obj["localRotation"]),
                LocalScale = ParseVector3OrDefault(obj["localScale"], Vector3.One),
                ScaleParamKey = obj["scaleParamKey"]?.GetValue<int>() ?? -1,
                ColorParamKey = obj["colorParamKey"]?.GetValue<int>() ?? -1,
                MaterialParamKey = obj["materialParamKey"]?.GetValue<int>() ?? -1,
                AssetSwapParamKey = obj["assetSwapParamKey"]?.GetValue<int>() ?? -1,
                AssetSwapTable = ParseAssetSwapTable(assetKind, obj["assetSwapTable"]),
                VisibilityParamKey = obj["visibilityParamKey"]?.GetValue<int>() ?? -1,
                HasMaxLod = obj.ContainsKey("maxLod"),
                MaxLod = ParseEnum(obj["maxLod"]?.GetValue<string>(), LODLevel.Low),
            };
        }

        private AttributeBindingConfig ParseAttributeBinding(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException("AttributeBinding behavior requires object field 'attributeBinding'.");
            }

            return new AttributeBindingConfig
            {
                AttributeId = ResolveAttributeId(obj),
                TargetParamKey = obj["targetParamKey"]?.GetValue<int>() ?? 0,
                Mode = ParseEnum(obj["mode"]?.GetValue<string>(), ValueSourceKind.Attribute),
                Thresholds = ParseThresholds(obj["thresholds"]),
            };
        }

        private TagBindingConfig ParseTagBinding(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException("TagBinding behavior requires object field 'tagBinding'.");
            }

            int tagId = ResolveTagId(obj["tagId"]);
            if (tagId <= 0)
            {
                throw new InvalidOperationException("TagBinding behavior requires non-empty field 'tagBinding.tagId'.");
            }

            return new TagBindingConfig
            {
                TagId = tagId,
                TargetParamKey = obj["targetParamKey"]?.GetValue<int>() ?? 0,
                InvertLogic = obj["invertLogic"]?.GetValue<bool>() ?? false,
            };
        }

        private AnimatorConfig ParseAnimator(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException("Animator behavior requires object field 'animator'.");
            }

            return new AnimatorConfig
            {
                AnimatorControllerId = ResolveRegisteredId(_resolveAnimatorControllerId, obj["animatorControllerId"], "animatorController"),
                AnimationProfileId = ResolveRegisteredId(_resolveAnimationProfileId, obj["animationProfileId"], "animationProfile"),
                SpeedParamKey = obj["speedParamKey"]?.GetValue<int>() ?? -1,
                StateParamKey = obj["stateParamKey"]?.GetValue<int>() ?? -1,
            };
        }

        private static AttachmentConfig ParseAttachment(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException("Attachment behavior requires object field 'attachment'.");
            }

            return new AttachmentConfig
            {
                Target = ParseEnum(obj["target"]?.GetValue<string>(), AttachmentTarget.Parent),
                BoneId = obj["boneId"]?.GetValue<int>() ?? 0,
                Offset = ParseVector3(obj["offset"]),
                RotationOffset = ParseQuaternion(obj["rotationOffset"]),
                InheritScale = obj["inheritScale"]?.GetValue<bool>() ?? false,
            };
        }

        private static GroundingConfig ParseGrounding(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException("Grounding behavior requires object field 'grounding'.");
            }

            return new GroundingConfig
            {
                Mode = ParseEnum(obj["mode"]?.GetValue<string>(), GroundingMode.SnapToGround),
                Offset = obj["offset"]?.GetValue<float>() ?? 0f,
                UpdatePolicy = ParseEnum(obj["updatePolicy"]?.GetValue<string>(), GroundingUpdatePolicy.Once),
            };
        }

        private static MinimapMarkerConfig ParseMinimapMarker(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException("MinimapMarker behavior requires object field 'minimapMarker'.");
            }

            MinimapMarkerShape shape = ParseRequiredEnum<MinimapMarkerShape>(
                obj["shape"],
                "MinimapMarker.shape");
            if (shape != MinimapMarkerShape.Circle)
            {
                throw new InvalidOperationException($"MinimapMarker shape '{shape}' is not supported. Use Circle.");
            }

            float sizePx = obj["sizePx"]?.GetValue<float>() ?? 6f;
            if (!float.IsFinite(sizePx) || sizePx <= 0f)
            {
                throw new InvalidOperationException("MinimapMarker sizePx must be a positive finite number.");
            }

            MinimapMarkerOrientationMode orientationMode = ParseRequiredEnumOrDefault(
                obj["orientationMode"],
                MinimapMarkerOrientationMode.None,
                "MinimapMarker.orientationMode");
            int orientationParamKey = obj["orientationParamKey"]?.GetValue<int>() ?? -1;
            float orientationOffsetRad = obj["orientationOffsetRad"]?.GetValue<float>() ?? 0f;
            float orientationLengthPx = obj["orientationLengthPx"]?.GetValue<float>() ?? 0f;
            if (!float.IsFinite(orientationOffsetRad))
            {
                throw new InvalidOperationException("MinimapMarker orientationOffsetRad must be finite.");
            }

            if (orientationMode == MinimapMarkerOrientationMode.ParamRadians ||
                orientationMode == MinimapMarkerOrientationMode.ParamDegrees)
            {
                if (orientationParamKey < 0)
                {
                    throw new InvalidOperationException("MinimapMarker orientationParamKey is required when orientationMode reads a param.");
                }

                if (!float.IsFinite(orientationLengthPx) || orientationLengthPx <= 0f)
                {
                    throw new InvalidOperationException("MinimapMarker orientationLengthPx must be a positive finite number when orientationMode is not None.");
                }
            }
            else if (orientationMode == MinimapMarkerOrientationMode.PerformerForward)
            {
                orientationParamKey = -1;
                if (!float.IsFinite(orientationLengthPx) || orientationLengthPx <= 0f)
                {
                    throw new InvalidOperationException("MinimapMarker orientationLengthPx must be a positive finite number when orientationMode is not None.");
                }
            }
            else
            {
                orientationParamKey = -1;
                orientationOffsetRad = 0f;
                orientationLengthPx = 0f;
            }

            return new MinimapMarkerConfig
            {
                Shape = shape,
                Color = ParseColor(obj["color"]),
                SizePx = sizePx,
                ColorParamKey = obj["colorParamKey"]?.GetValue<int>() ?? -1,
                SizeParamKey = obj["sizeParamKey"]?.GetValue<int>() ?? -1,
                VisibilityParamKey = obj["visibilityParamKey"]?.GetValue<int>() ?? -1,
                OrientationMode = orientationMode,
                OrientationParamKey = orientationParamKey,
                OrientationOffsetRad = orientationOffsetRad,
                OrientationLengthPx = orientationLengthPx,
            };
        }

        private SoundConfig ParseSound(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException("Sound behavior requires object field 'sound'.");
            }

            return new SoundConfig
            {
                SoundAssetId = ResolveBehaviorAssetId(AssetKind.Sound, obj["soundAssetId"]),
                Loop = obj["loop"]?.GetValue<bool>() ?? false,
                Volume = obj["volume"]?.GetValue<float>() ?? 1f,
                VolumeParamKey = obj["volumeParamKey"]?.GetValue<int>() ?? -1,
            };
        }

        private MaterialConfig ParseMaterial(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException("Material behavior requires object field 'material'.");
            }

            return new MaterialConfig
            {
                BaseMaterialId = ResolveRegisteredId(_resolveMaterialId, obj["baseMaterialId"], "material"),
                MaterialSwapParamKey = obj["materialSwapParamKey"]?.GetValue<int>() ?? -1,
                SwapTable = ParseMaterialSwapTable(obj["swapTable"]),
            };
        }

        private SplineConfig ParseSpline(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException("Spline behavior requires object field 'spline'.");
            }

            return new SplineConfig
            {
                SplineAssetId = ResolveBehaviorAssetId(AssetKind.Spline, obj["splineAssetId"]),
                Usage = ParseEnum(obj["usage"]?.GetValue<string>(), SplineUsage.Render),
                WidthParamKey = obj["widthParamKey"]?.GetValue<int>() ?? -1,
                ColorParamKey = obj["colorParamKey"]?.GetValue<int>() ?? -1,
                SpeedParamKey = obj["speedParamKey"]?.GetValue<int>() ?? -1,
                ProgressParamKey = obj["progressParamKey"]?.GetValue<int>() ?? -1,
                Loop = obj["loop"]?.GetValue<bool>() ?? false,
                PingPong = obj["pingPong"]?.GetValue<bool>() ?? false,
                WaypointEventId = obj["waypointEventId"]?.GetValue<int>() ?? 0,
            };
        }

        private static ThresholdMapping[] ParseThresholds(JsonNode? node)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                return Array.Empty<ThresholdMapping>();
            }

            var thresholds = new ThresholdMapping[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"thresholds[{i}] must be an object.");
                }

                thresholds[i] = new ThresholdMapping
                {
                    Threshold = obj["threshold"]?.GetValue<float>() ?? 0f,
                    OutputParamKey = obj["outputParamKey"]?.GetValue<int>() ?? 0,
                    OutputValue = obj["outputValue"]?.GetValue<float>() ?? 0f,
                };
            }

            return thresholds;
        }

        private MaterialSwapEntry[] ParseMaterialSwapTable(JsonNode? node)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                return Array.Empty<MaterialSwapEntry>();
            }

            var table = new MaterialSwapEntry[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"swapTable[{i}] must be an object.");
                }

                table[i] = new MaterialSwapEntry
                {
                    ParamValue = obj["paramValue"]?.GetValue<float>() ?? 0f,
                    MaterialId = ResolveRegisteredId(_resolveMaterialId, obj["materialId"], "material"),
                };
            }

            return table;
        }

        private AssetSwapEntry[] ParseAssetSwapTable(AssetKind assetKind, JsonNode? node)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                return Array.Empty<AssetSwapEntry>();
            }

            var table = new AssetSwapEntry[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"assetSwapTable[{i}] must be an object.");
                }

                table[i] = new AssetSwapEntry
                {
                    ParamValue = obj["paramValue"]?.GetValue<float>() ?? 0f,
                    AssetId = ResolveBehaviorAssetId(assetKind, obj["assetId"]),
                };
            }

            return table;
        }

        private int ResolveBehaviorAssetId(AssetKind kind, JsonNode? node)
        {
            if (node == null)
            {
                return 0;
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    return numericId;
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    int id = kind == AssetKind.WorldText
                        ? _resolveTextTokenId(key)
                        : _resolveBehaviorAssetId(kind, key);
                    if (id <= 0)
                    {
                        throw new InvalidOperationException($"Performer behavior references unknown {kind} asset '{key}'.");
                    }

                    return id;
                }
            }

            return 0;
        }

        private static int ResolveGroundOverlayShapeId(JsonNode? node)
        {
            if (node == null)
            {
                throw new InvalidOperationException("GroundOverlay AssetBinding requires explicit assetId shape 'Circle', 'Cone', 'Line', or 'Ring'.");
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    if (numericId is >= 0 and <= 3)
                    {
                        return numericId;
                    }

                    throw new InvalidOperationException($"GroundOverlay AssetBinding assetId '{numericId}' is invalid. Use 0=Circle, 1=Cone, 2=Line, or 3=Ring.");
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    if (Enum.TryParse(key, ignoreCase: true, out GroundOverlayShape shape))
                    {
                        return (int)shape;
                    }

                    throw new InvalidOperationException($"GroundOverlay AssetBinding assetId '{key}' is invalid. Use Circle, Cone, Line, or Ring.");
                }
            }

            throw new InvalidOperationException("GroundOverlay AssetBinding assetId must be a shape string or numeric shape id.");
        }

        private static int ResolveRegisteredId(Func<string, int> resolver, JsonNode? node, string subject)
        {
            if (node == null)
            {
                return 0;
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    return numericId;
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    int id = resolver(key);
                    if (id <= 0)
                    {
                        throw new InvalidOperationException($"Performer behavior references unknown {subject} '{key}'.");
                    }

                    return id;
                }
            }

            return 0;
        }

        private static int ResolveTagId(JsonNode? node)
        {
            if (node == null)
            {
                return 0;
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    return numericId;
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    return TagRegistry.Register(key);
                }
            }

            return 0;
        }

        private static ParamLane ParseRequiredParamLane(JsonObject obj, string context)
        {
            if (obj["lane"] is not JsonValue laneNode || !laneNode.TryGetValue<string>(out string? laneText) || string.IsNullOrWhiteSpace(laneText))
            {
                throw new InvalidOperationException($"{context} requires explicit string field 'lane'.");
            }

            if (!Enum.TryParse(laneText, ignoreCase: true, out ParamLane lane))
            {
                throw new InvalidOperationException($"{context} has invalid lane '{laneText}'.");
            }

            return lane;
        }

        private SurfaceAuthoringBlock ParseSurface(JsonNode? node, string key)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"Performer '{key}' declares 'surface' but is missing the required surface object.");
            }

            return new SurfaceAuthoringBlock
            {
                Kind = ParseEnum(obj["kind"]?.GetValue<string>(), PerformerSurfaceKind.SplineRibbon),
                ProfileId = obj["profileId"]?.GetValue<string>() ?? string.Empty,
                GeometrySource = ParseSurfaceGeometrySource(obj["geometrySource"], key),
                ChunkBake = ParseChunkBakePolicy(obj["chunkBake"], key),
                MaterialSet = ParseMaterialSet(obj["materialSet"], key),
                LodProfileId = obj["lodProfileId"]?.GetValue<string>() ?? string.Empty,
                Grounding = ParseGroundingPolicy(obj["grounding"]),
                BoundsPolicy = obj["boundsPolicy"]?.GetValue<string>() ?? string.Empty,
            };
        }

        private PerformerSurfaceGeometrySource ParseSurfaceGeometrySource(JsonNode? node, string key)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"SurfaceSource performer '{key}' must declare object field 'surface.geometrySource'.");
            }

            return new PerformerSurfaceGeometrySource
            {
                ControlPointSource = ParseSurfaceValueSource(obj["controlPointSource"]),
                WidthSource = ParseSurfaceValueSource(obj["widthSource"]),
                FlowDirectionSource = ParseSurfaceValueSource(obj["flowDirectionSource"]),
                SegmentationPolicy = obj["segmentationPolicy"]?.GetValue<string>() ?? string.Empty,
                BoundaryPointSource = ParseSurfaceValueSource(obj["boundaryPointSource"]),
                TriangulationPolicy = obj["triangulationPolicy"]?.GetValue<string>() ?? string.Empty,
                MeshPayloadSource = ParseSurfaceValueSource(obj["meshPayloadSource"]),
            };
        }

        private PerformerSurfaceChunkBakePolicy ParseChunkBakePolicy(JsonNode? node, string key)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"SurfaceSource performer '{key}' must declare object field 'surface.chunkBake'.");
            }

            return new PerformerSurfaceChunkBakePolicy
            {
                Enabled = obj["enabled"]?.GetValue<bool>() ?? true,
                Ownership = ParseEnum(obj["ownership"]?.GetValue<string>(), PerformerSurfaceChunkOwnership.PerChunk),
                ChunkInfluencePolicy = obj["chunkInfluencePolicy"]?.GetValue<string>() ?? string.Empty,
                RebakePolicy = obj["rebakePolicy"]?.GetValue<string>() ?? string.Empty,
                UsageHint = ParseEnum(obj["usageHint"]?.GetValue<string>(), Assets.ProceduralMeshUsageHint.Static),
            };
        }

        private PerformerSurfaceMaterialSet ParseMaterialSet(JsonNode? node, string key)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"SurfaceSource performer '{key}' must declare object field 'surface.materialSet'.");
            }

            return new PerformerSurfaceMaterialSet
            {
                PrimaryMaterialId = obj["primaryMaterialId"]?.GetValue<string>() ?? string.Empty,
                SecondaryMaterialId = obj["secondaryMaterialId"]?.GetValue<string>() ?? string.Empty,
                AllowInstanceOverride = obj["allowInstanceOverride"]?.GetValue<bool>() ?? false,
            };
        }

        private static PerformerSurfaceGroundingPolicy ParseGroundingPolicy(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                return new PerformerSurfaceGroundingPolicy();
            }

            return new PerformerSurfaceGroundingPolicy
            {
                Mode = obj["mode"]?.GetValue<string>() ?? string.Empty,
            };
        }

        private static PerformerSurfaceValueSource? ParseSurfaceValueSource(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                return null;
            }

            return new PerformerSurfaceValueSource
            {
                Kind = ParseEnum(obj["kind"]?.GetValue<string>(), PerformerSurfaceValueSourceKind.Constant),
                Id = obj["id"]?.GetValue<string>() ?? string.Empty,
                GraphProgramId = obj["graphProgramId"]?.GetValue<int>() ?? 0,
            };
        }

        private static ConditionRef ParseConditionRef(JsonNode? node)
        {
            if (node == null)
            {
                return ConditionRef.AlwaysTrue;
            }

            var cond = new ConditionRef();
            string inline = node["inline"]?.GetValue<string>() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(inline))
            {
                cond.Inline = ParseEnum(inline, InlineConditionKind.None);
            }

            cond.GraphProgramId = node["graphProgramId"]?.GetValue<int>() ?? 0;
            return cond;
        }

        private static void StampRuleOwners(int ownerDefinitionId, PerformerRule[] rules)
        {
            if (rules == null)
            {
                return;
            }

            for (int i = 0; i < rules.Length; i++)
            {
                rules[i].OwnerDefinitionId = ownerDefinitionId;
            }
        }

        private static Vector3 ParseVector3(JsonNode? node)
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

        private static Vector3 ParseVector3OrDefault(JsonNode? node, Vector3 defaultValue)
        {
            if (node is JsonArray arr && arr.Count >= 3)
            {
                return new Vector3(
                    arr[0]?.GetValue<float>() ?? defaultValue.X,
                    arr[1]?.GetValue<float>() ?? defaultValue.Y,
                    arr[2]?.GetValue<float>() ?? defaultValue.Z);
            }

            return defaultValue;
        }

        private static Quaternion ParseQuaternion(JsonNode? node)
        {
            if (node is JsonArray arr && arr.Count >= 4)
            {
                return new Quaternion(
                    arr[0]?.GetValue<float>() ?? 0f,
                    arr[1]?.GetValue<float>() ?? 0f,
                    arr[2]?.GetValue<float>() ?? 0f,
                    arr[3]?.GetValue<float>() ?? 1f);
            }

            return Quaternion.Identity;
        }

        private static Vector4 ParseVector4(JsonNode? node)
        {
            if (node is JsonArray arr && arr.Count >= 4)
            {
                return new Vector4(
                    arr[0]?.GetValue<float>() ?? 0f,
                    arr[1]?.GetValue<float>() ?? 0f,
                    arr[2]?.GetValue<float>() ?? 0f,
                    arr[3]?.GetValue<float>() ?? 0f);
            }

            return Vector4.Zero;
        }

        private static Vector4 ParseColor(JsonNode? node)
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

        private static T ParseEnum<T>(string? s, T defaultValue) where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return defaultValue;
            }

            return Enum.TryParse<T>(s, ignoreCase: true, out var parsed) ? parsed : defaultValue;
        }

        private static T ParseRequiredEnum<T>(JsonNode? node, string context) where T : struct, Enum
        {
            if (node is not JsonValue value || !value.TryGetValue<string>(out string? text) || string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"{context} requires a non-empty enum string.");
            }

            if (!Enum.TryParse<T>(text, ignoreCase: true, out T parsed))
            {
                throw new InvalidOperationException($"{context} has invalid value '{text}'.");
            }

            return parsed;
        }

        private static T ParseRequiredEnumOrDefault<T>(JsonNode? node, T defaultValue, string context) where T : struct, Enum
        {
            if (node == null)
            {
                return defaultValue;
            }

            return ParseRequiredEnum<T>(node, context);
        }
    }
}
