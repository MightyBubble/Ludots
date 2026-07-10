using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Instancing;
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
        private readonly Func<string, int> _resolveEntityCollectionKeyId;
        private readonly Func<string, int> _resolveInstancedBatchAssetId;

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
            Func<AssetKind, string, int> resolveBehaviorAssetId = null,
            Func<string, int> resolveInstancedBatchAssetId = null,
            Func<string, int> resolveEntityCollectionKeyId = null)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _resolveAttributeName = resolveAttributeName ?? (_ => AttributeRegistry.InvalidId);
            _resolveMeshId = resolveMeshId ?? (_ => 0);
            _resolveTextTokenId = resolveTextTokenId ?? (_ => 0);
            _resolveEntityTemplateKey = resolveEntityTemplateKey ?? (_ => 0);
            _resolveEffectTemplateId = resolveEffectTemplateId ?? (_ => 0);
            _resolveMaterialId = resolveMaterialId ?? (_ => 0);
            _resolveAnimatorControllerId = resolveAnimatorControllerId ?? (_ => 0);
            _resolveAnimationProfileId = resolveAnimationProfileId ?? (_ => 0);
            _resolveBehaviorAssetId = resolveBehaviorAssetId ?? ((_, __) => 0);
            _resolveEntityCollectionKeyId = resolveEntityCollectionKeyId ?? (_ => 0);
            _resolveInstancedBatchAssetId = resolveInstancedBatchAssetId ?? (_ => 0);
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, "Presentation/performers.json", ConfigMergePolicy.ArrayById, "id");
            var fragments = _configs.CollectFragmentsWithSources(entry.RelativePath);
            ValidateRawDefinitionIds(fragments, entry.RelativePath);
            var merged = ConfigMerger.MergeArrayByIdToEntries(fragments, in entry, report);
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

            var mergedByKey = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            var parsedByKey = new Dictionary<string, PerformerDefinition>(StringComparer.Ordinal);
            var parsedOrder = new List<string>(merged.Count);
            for (int i = 0; i < merged.Count; i++)
            {
                if (merged[i].Node is not JsonObject obj)
                {
                    throw new InvalidOperationException(
                        $"Presentation/performers.json entry '{merged[i].Id}' must merge to a JSON object.");
                }

                string key = RequireCanonicalString(
                    obj["id"]?.GetValue<string>() ?? string.Empty,
                    "Presentation/performers.json entry id");

                mergedByKey[key] = obj;
                _registry.GetOrRegisterId(key);
            }

            foreach ((string key, JsonObject _) in mergedByKey)
            {
                JsonObject expanded = ExpandDefinition(key, mergedByKey, new HashSet<string>(StringComparer.Ordinal));
                var (_, def) = ParseDefinition(expanded);
                if (def == null)
                {
                    throw new InvalidOperationException($"Performer '{key}' failed to parse.");
                }

                parsedByKey[key] = def;
                parsedOrder.Add(key);
            }

            var validByKey = new Dictionary<string, PerformerDefinition>(parsedByKey, StringComparer.Ordinal);
            for (int i = 0; i < parsedOrder.Count; i++)
            {
                string key = parsedOrder[i];
                PerformerDefinition definition = validByKey[key];
                ValidateRuleReferences(key, definition, validByKey);
                ValidateChildGraph(key, validByKey, new HashSet<int>(), new List<string>());
            }

            for (int i = 0; i < parsedOrder.Count; i++)
            {
                string key = parsedOrder[i];
                _registry.Register(key, validByKey[key]);
            }
        }

        private static void ValidateRawDefinitionIds(IReadOnlyList<ConfigFragment> fragments, string relativePath)
        {
            for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                if (fragments[fragmentIndex].Node is not JsonArray arr)
                {
                    continue;
                }

                for (int entryIndex = 0; entryIndex < arr.Count; entryIndex++)
                {
                    if (arr[entryIndex] is not JsonObject obj)
                    {
                        continue;
                    }

                    if (!obj.TryGetPropertyValue("id", out JsonNode? idNode) ||
                        idNode is not JsonValue idValue ||
                        !idValue.TryGetValue<string>(out string? id))
                    {
                        throw new InvalidOperationException(
                            $"{relativePath} entry at index {entryIndex} must declare explicit string id.");
                    }

                    RequireCanonicalString(id, $"{relativePath} entry id");
                }
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
                string parentKey = ParseOptionalCanonicalString(node["extends"], $"Performer '{key}' extends");
                if (parentKey.Length == 0)
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
                if (propertyName.Equals("bindings", StringComparison.Ordinal))
                {
                    merged[propertyName] = MergeByValueKey(parent[propertyName], childValue, "paramKey", "bindings");
                    continue;
                }

                if (propertyName.Equals("paramDefaults", StringComparison.Ordinal))
                {
                    merged[propertyName] = MergeParamDefaultsJson(parent[propertyName], childValue);
                    continue;
                }

                if (propertyName.Equals("behaviors", StringComparison.Ordinal))
                {
                    merged[propertyName] = MergeByValueKey(parent[propertyName], childValue, "slot", "behaviors");
                    continue;
                }

                if (propertyName.Equals("rules", StringComparison.Ordinal) ||
                    propertyName.Equals("children", StringComparison.Ordinal))
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

        private static JsonArray MergeByValueKey(JsonNode? existingNode, JsonNode? incomingNode, string keyField, string arrayName)
        {
            var byKey = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            var order = new List<string>();
            AppendByValueKey(existingNode, keyField, arrayName, byKey, order);
            AppendByValueKey(incomingNode, keyField, arrayName, byKey, order);

            var merged = new JsonArray();
            for (int i = 0; i < order.Count; i++)
            {
                merged.Add(byKey[order[i]].DeepClone());
            }

            return merged;
        }

        private static void AppendByValueKey(
            JsonNode? node,
            string keyField,
            string arrayName,
            Dictionary<string, JsonNode> byKey,
            List<string> order)
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

                string key = GetSemanticMergeKey(obj[keyField], $"{arrayName}[{i}].{keyField}");
                if (!byKey.ContainsKey(key))
                {
                    order.Add(key);
                }

                byKey[key] = obj;
            }
        }

        private static string GetSemanticMergeKey(JsonNode? node, string context)
        {
            if (node is not JsonValue value)
            {
                throw new InvalidOperationException($"{context} requires an explicit semantic string.");
            }

            if (value.TryGetValue<int>(out int numericId))
            {
                throw new InvalidOperationException(
                    $"{context} uses numeric authoring value {numericId}. Use a semantic string instead.");
            }

            if (value.TryGetValue<string>(out string? text) && !string.IsNullOrWhiteSpace(text))
            {
                return RequireCanonicalString(text, context);
            }

            throw new InvalidOperationException($"{context} requires a non-empty semantic string.");
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

                int paramKey = ParseRequiredParamKey(obj["paramKey"], $"paramDefaults[{i}].paramKey");
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
            string key = RequireCanonicalString(node["id"]?.GetValue<string>() ?? string.Empty, "Performer id");

            RejectRemovedFields(node, key);

            BehaviorSlot[] behaviors = ParseBehaviors(node["behaviors"], key);
            WorldHudValueMode worldTextMode = ParseWorldTextMode(node, key, behaviors);

            var def = new PerformerDefinition
            {
                Key = key,
                Extends = ParseOptionalCanonicalString(node["extends"], $"Performer '{key}' extends"),
                DefaultColor = ParseColor(node["defaultColor"]),
                DefaultLifetime = node["defaultLifetime"]?.GetValue<float>() ?? 0f,
                DefaultFontSize = node["defaultFontSize"]?.GetValue<int>() ?? 16,
                WorldTextMode = worldTextMode,
                PositionOffset = ParseVector3(node["positionOffset"]),
                PositionYDriftPerSecond = node["positionYDriftPerSecond"]?.GetValue<float>() ?? 0f,
                AlphaFadeOverLifetime = node["alphaFadeOverLifetime"]?.GetValue<bool>() ?? false,
                VisibilityCondition = ParseConditionRef(node["visibility"]),
                Rules = ParseRules(node["rules"]),
                Bindings = ParseBindings(node["bindings"]),
                Children = ParseChildren(node["children"]),
                Behaviors = behaviors,
                InstancedBatches = ParseInstancedBatchBindings(node["instancedBatches"], key),
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

        private static void RejectRemovedFields(JsonNode node, string key)
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
                "defaultTextId",
                "legacyWorldTextMode",
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
                    Event = ParseEventFilter(arr[i]!["event"], $"rules[{i}].event"),
                    Condition = ParseConditionRef(arr[i]!["condition"]),
                    Command = ParsePerformerCommand(arr[i]!["command"], $"rules[{i}].command"),
                };
            }

            return rules;
        }

        private EventFilter ParseEventFilter(JsonNode? node, string context)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"{context} requires an object with explicit field 'kind'.");
            }

            PresentationEventKind kind = ParseRequiredNonNoneEnum<PresentationEventKind>(obj["kind"], $"{context}.kind");
            return new EventFilter
            {
                Kind = kind,
                KeyId = ResolveEventKey(kind, obj),
            };
        }

        private int ResolveEventKey(PresentationEventKind kind, JsonNode node)
        {
            bool hasKey = node["key"] != null;
            bool hasKeyId = node["keyId"] != null;
            if (hasKey && hasKeyId)
            {
                throw new InvalidOperationException(
                    $"Presentation event '{kind}' must declare only one of 'key' or 'keyId'.");
            }

            if (node["keyId"] is JsonValue keyIdValue)
            {
                if (keyIdValue.TryGetValue<int>(out int numericId))
                {
                    throw new InvalidOperationException(
                        $"Presentation event '{kind}' keyId must be a semantic string, not numeric id {numericId}.");
                }

                if (keyIdValue.TryGetValue<string>(out string textKey))
                {
                    return ResolveEventKey(kind, textKey, "keyId");
                }
            }

            if (node["key"] is JsonValue keyValue && keyValue.TryGetValue<string>(out string key))
            {
                return ResolveEventKey(kind, key, "key");
            }

            if (node["key"] is JsonValue numericKeyValue && numericKeyValue.TryGetValue<int>(out int numericKey))
            {
                throw new InvalidOperationException(
                    $"Presentation event '{kind}' key must be a semantic string, not numeric id {numericKey}.");
            }

            throw new InvalidOperationException(
                $"Presentation event '{kind}' requires explicit key or keyId. Use key \"*\" for wildcard.");
        }

        private int ResolveEventKey(PresentationEventKind kind, string key, string fieldName)
        {
            key = RequireCanonicalString(key, $"Presentation event '{kind}' {fieldName}");
            if (string.Equals(key, "*", StringComparison.Ordinal))
            {
                return -1;
            }

            return kind switch
            {
                PresentationEventKind.EntitySpawned => ResolveRequired(_resolveEntityTemplateKey(key), kind, "entity template", key),
                PresentationEventKind.EntityDestroyed => ResolveRequired(_resolveEntityTemplateKey(key), kind, "entity template", key),
                PresentationEventKind.ProjectileSpawned => ResolveRequired(_resolveEffectTemplateId(key), kind, "effect template", key),
                PresentationEventKind.TagEffectiveChanged => TagRegistry.Register(key),
                PresentationEventKind.GameplayEvent => TagRegistry.Register(key),
                PresentationEventKind.EffectApplied => ResolveRequired(_resolveEffectTemplateId(key), kind, "effect template", key),
                PresentationEventKind.EffectActivated => ResolveRequired(_resolveEffectTemplateId(key), kind, "effect template", key),
                PresentationEventKind.CastCommitted => ResolveRequired(AbilityIdRegistry.GetId(key), kind, "ability", key),
                PresentationEventKind.CastFailed => ResolveRequired(AbilityIdRegistry.GetId(key), kind, "ability", key),
                PresentationEventKind.EntityCollectionMemberAdded => ResolveRequired(_resolveEntityCollectionKeyId(key), kind, "entity collection", key),
                PresentationEventKind.EntityCollectionMemberRemoved => ResolveRequired(_resolveEntityCollectionKeyId(key), kind, "entity collection", key),
                PresentationEventKind.AbilityAimBegun => TagRegistry.Register(key),
                PresentationEventKind.AbilityAimSlotAdvanced => TagRegistry.Register(key),
                PresentationEventKind.AbilityAimUpdated => TagRegistry.Register(key),
                PresentationEventKind.AbilityAimEnded => TagRegistry.Register(key),
                PresentationEventKind.MovePathBegun => TagRegistry.Register(key),
                PresentationEventKind.MovePathUpdated => TagRegistry.Register(key),
                PresentationEventKind.MovePathEnded => TagRegistry.Register(key),
                PresentationEventKind.WorldOverlayUpdated => TagRegistry.Register(key),
                PresentationEventKind.WorldOverlayEnded => TagRegistry.Register(key),
                PresentationEventKind.WorldHudUpdated => TagRegistry.Register(key),
                PresentationEventKind.WorldHudEnded => TagRegistry.Register(key),
                PresentationEventKind.WorldSplineUpdated => TagRegistry.Register(key),
                PresentationEventKind.WorldSplineEnded => TagRegistry.Register(key),
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

        private PerformerCommand ParsePerformerCommand(JsonNode? node, string context)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"{context} requires an object with explicit field 'kind'.");
            }

            PerformerCommandKind commandKind = ParseRequiredNonNoneEnum<PerformerCommandKind>(obj["kind"], $"{context}.kind");
            ParamLane paramLane = ParseCommandParamLane(obj, commandKind, context);
            PerformerCommandValueSource valueSource = ParseCommandValueSource(obj, commandKind, context);
            int paramGraphProgramId = ParseCommandParamGraphProgramId(obj, commandKind, valueSource, context);
            int performerDefinitionId = ParseCommandDefinitionId(obj, commandKind, context);
            bool hasVectorSources = HasCommandVectorSources(obj);
            bool hasParamPayload = HasCommandParamPayload(obj, commandKind);
            return new PerformerCommand
            {
                CommandKind = commandKind,
                PerformerDefinitionId = performerDefinitionId,
                ParentEntity = Entity.Null, // resolved at runtime
                ScopeTag = ParseScopeTag(obj["scopeTag"]),
                ScopeSource = ParseCommandScopeSource(obj, commandKind, context),
                OwnerSource = ParseCommandOwnerSource(obj, commandKind, context),
                UseEventPosition = ParseCommandUseEventPosition(obj, commandKind, context),
                HasParamPayload = hasParamPayload,
                ParamKey = commandKind == PerformerCommandKind.SetParam || hasParamPayload
                    ? ParseRequiredParamKey(obj["paramKey"], "Performer command paramKey")
                    : ParseOptionalCommandParamKey(obj["paramKey"], "Performer command paramKey"),
                ParamLane = paramLane,
                ParamValue = ParseCommandParamValue(obj, commandKind, paramLane, valueSource, paramGraphProgramId, context),
                IntValue = ParseCommandIntValue(obj, commandKind, paramLane, valueSource, paramGraphProgramId, context),
                VectorValue = ParseCommandVectorValue(obj, commandKind, paramLane, valueSource, hasVectorSources, paramGraphProgramId, context),
                ValueSource = valueSource,
                VectorXSource = ParseCommandVectorSource(obj["vectorXSource"], commandKind, paramLane, hasVectorSources, $"{context}.vectorXSource"),
                VectorYSource = ParseCommandVectorSource(obj["vectorYSource"], commandKind, paramLane, hasVectorSources, $"{context}.vectorYSource"),
                VectorZSource = ParseCommandVectorSource(obj["vectorZSource"], commandKind, paramLane, hasVectorSources, $"{context}.vectorZSource"),
                VectorWSource = ParseCommandVectorSource(obj["vectorWSource"], commandKind, paramLane, hasVectorSources, $"{context}.vectorWSource"),
                ParamGraphProgramId = paramGraphProgramId,
                TargetBehaviorSlot = commandKind is PerformerCommandKind.ActivateBehavior or PerformerCommandKind.DeactivateBehavior
                    ? ParseRequiredBehaviorSlot(obj["targetBehaviorSlot"], "Performer command targetBehaviorSlot")
                    : ParseOptionalBehaviorSlot(obj["targetBehaviorSlot"], "Performer command targetBehaviorSlot"),
            };
        }

        private static bool HasCommandParamPayload(JsonObject obj, PerformerCommandKind commandKind)
        {
            if (commandKind == PerformerCommandKind.SetParam)
            {
                return true;
            }

            if (obj["paramKey"] == null &&
                obj["paramLane"] == null &&
                obj["valueSource"] == null &&
                obj["paramValue"] == null &&
                obj["intValue"] == null &&
                obj["vectorValue"] == null &&
                obj["paramGraphProgramId"] == null &&
                obj["vectorXSource"] == null &&
                obj["vectorYSource"] == null &&
                obj["vectorZSource"] == null &&
                obj["vectorWSource"] == null)
            {
                return false;
            }

            if (commandKind != PerformerCommandKind.CreatePerformer)
            {
                throw new InvalidOperationException($"{nameof(PerformerCommand)} param payload fields are only valid for CreatePerformer and SetParam commands.");
            }

            return true;
        }

        private static PerformerCommandEntitySource ParseCommandOwnerSource(
            JsonObject obj,
            PerformerCommandKind commandKind,
            string context)
        {
            JsonNode? node = obj["ownerSource"];
            if (node == null)
            {
                return PerformerCommandEntitySource.EventSource;
            }

            if (commandKind is not (
                    PerformerCommandKind.CreatePerformer or
                    PerformerCommandKind.SetParam or
                    PerformerCommandKind.DestroyScopedPerformer or
                    PerformerCommandKind.DestroyPerformerScope))
            {
                throw new InvalidOperationException(
                    $"{context}.ownerSource is only valid for scoped performer commands.");
            }

            return ParseRequiredEnum<PerformerCommandEntitySource>(node, $"{context}.ownerSource");
        }

        private static PerformerCommandScopeSource ParseCommandScopeSource(
            JsonObject obj,
            PerformerCommandKind commandKind,
            string context)
        {
            JsonNode? node = obj["scopeSource"];
            if (commandKind == PerformerCommandKind.SetParam &&
                obj["definitionId"] != null &&
                node == null)
            {
                throw new InvalidOperationException($"{context}.scopeSource is required for scoped SetParam commands with definitionId.");
            }

            if (CommandRequiresScopeSource(commandKind) || node != null)
            {
                return ParseRequiredEnum<PerformerCommandScopeSource>(node, $"{context}.scopeSource");
            }

            return PerformerCommandScopeSource.Fixed;
        }

        private static bool ParseCommandUseEventPosition(
            JsonObject obj,
            PerformerCommandKind commandKind,
            string context)
        {
            JsonNode? node = obj["useEventPosition"];
            if (node == null)
            {
                return false;
            }

            if (commandKind is not (PerformerCommandKind.CreatePerformer or PerformerCommandKind.SetParam or PerformerCommandKind.DestroyScopedPerformer))
            {
                throw new InvalidOperationException($"{context}.useEventPosition is only valid for CreatePerformer, SetParam, and DestroyScopedPerformer commands.");
            }

            return ParseRequiredBool(node, $"{context}.useEventPosition");
        }

        private static ParamLane ParseCommandParamLane(JsonObject obj, PerformerCommandKind commandKind, string context)
        {
            JsonNode? node = obj["paramLane"];
            if (commandKind == PerformerCommandKind.SetParam || HasCommandParamPayload(obj, commandKind))
            {
                return ParseRequiredEnum<ParamLane>(node, $"{context}.paramLane");
            }

            return ParamLane.Float;
        }

        private static PerformerCommandValueSource ParseCommandValueSource(
            JsonObject obj,
            PerformerCommandKind commandKind,
            string context)
        {
            JsonNode? node = obj["valueSource"];
            if (commandKind == PerformerCommandKind.SetParam || HasCommandParamPayload(obj, commandKind))
            {
                return ParseRequiredEnum<PerformerCommandValueSource>(node, $"{context}.valueSource");
            }

            return PerformerCommandValueSource.Fixed;
        }

        private static int ParseCommandParamGraphProgramId(
            JsonObject obj,
            PerformerCommandKind commandKind,
            PerformerCommandValueSource valueSource,
            string context)
        {
            JsonNode? node = obj["paramGraphProgramId"];
            if (node == null)
            {
                return 0;
            }

            if (commandKind is not (PerformerCommandKind.SetParam or PerformerCommandKind.CreatePerformer))
            {
                throw new InvalidOperationException($"{context}.paramGraphProgramId is only valid for CreatePerformer and SetParam commands.");
            }

            if (valueSource != PerformerCommandValueSource.Fixed)
            {
                throw new InvalidOperationException($"{context}.paramGraphProgramId requires valueSource '{PerformerCommandValueSource.Fixed}'.");
            }

            int graphProgramId = ParseRequiredInt(node, $"{context}.paramGraphProgramId");
            if (graphProgramId <= 0)
            {
                throw new InvalidOperationException($"{context}.paramGraphProgramId must be positive.");
            }

            return graphProgramId;
        }

        private static float ParseCommandParamValue(
            JsonObject obj,
            PerformerCommandKind commandKind,
            ParamLane lane,
            PerformerCommandValueSource valueSource,
            int paramGraphProgramId,
            string context)
        {
            JsonNode? node = obj["paramValue"];
            if (node == null)
            {
                if ((commandKind == PerformerCommandKind.SetParam || HasCommandParamPayload(obj, commandKind)) &&
                    valueSource == PerformerCommandValueSource.Fixed &&
                    lane == ParamLane.Float &&
                    paramGraphProgramId == 0)
                {
                    throw new InvalidOperationException($"{context}.paramValue requires an explicit numeric field for Fixed Float SetParam.");
                }

                return 0f;
            }

            return ParseRequiredFloat(node, $"{context}.paramValue");
        }

        private static int ParseCommandIntValue(
            JsonObject obj,
            PerformerCommandKind commandKind,
            ParamLane lane,
            PerformerCommandValueSource valueSource,
            int paramGraphProgramId,
            string context)
        {
            JsonNode? node = obj["intValue"];
            if (node == null)
            {
                if ((commandKind == PerformerCommandKind.SetParam || HasCommandParamPayload(obj, commandKind)) &&
                    valueSource == PerformerCommandValueSource.Fixed &&
                    lane == ParamLane.Int &&
                    paramGraphProgramId == 0)
                {
                    throw new InvalidOperationException($"{context}.intValue requires an explicit integer field for Fixed Int SetParam.");
                }

                return 0;
            }

            return ParseRequiredInt(node, $"{context}.intValue");
        }

        private static Vector4 ParseCommandVectorValue(
            JsonObject obj,
            PerformerCommandKind commandKind,
            ParamLane lane,
            PerformerCommandValueSource valueSource,
            bool hasVectorSources,
            int paramGraphProgramId,
            string context)
        {
            JsonNode? node = obj["vectorValue"];
            if (node == null)
            {
                if ((commandKind == PerformerCommandKind.SetParam || HasCommandParamPayload(obj, commandKind)) &&
                    lane == ParamLane.Vector &&
                    paramGraphProgramId == 0 &&
                    !hasVectorSources)
                {
                    if (valueSource != PerformerCommandValueSource.Fixed)
                    {
                        throw new InvalidOperationException($"{context}.vectorValue requires valueSource '{PerformerCommandValueSource.Fixed}' for Vector SetParam.");
                    }

                    throw new InvalidOperationException($"{context}.vectorValue requires an explicit 4-component array field for Fixed Vector SetParam.");
                }

                return Vector4.Zero;
            }

            return ParseRequiredVector4(node, $"{context}.vectorValue");
        }

        private static bool HasCommandVectorSources(JsonObject obj)
        {
            return obj["vectorXSource"] != null ||
                   obj["vectorYSource"] != null ||
                   obj["vectorZSource"] != null ||
                   obj["vectorWSource"] != null;
        }

        private static PerformerCommandValueSource ParseCommandVectorSource(
            JsonNode? node,
            PerformerCommandKind commandKind,
            ParamLane lane,
            bool hasVectorSources,
            string context)
        {
            if (node == null)
            {
                if (hasVectorSources)
                {
                    throw new InvalidOperationException($"{context} is required when any vector source is declared.");
                }

                return PerformerCommandValueSource.Fixed;
            }

            if (commandKind is not (PerformerCommandKind.SetParam or PerformerCommandKind.CreatePerformer) || lane != ParamLane.Vector)
            {
                throw new InvalidOperationException($"{context} is only valid for Vector CreatePerformer and SetParam commands.");
            }

            return ParseRequiredEnum<PerformerCommandValueSource>(node, context);
        }

        private static bool CommandRequiresScopeSource(PerformerCommandKind commandKind)
        {
            return commandKind is PerformerCommandKind.CreatePerformer
                or PerformerCommandKind.DestroyPerformerScope
                or PerformerCommandKind.DestroyScopedPerformer;
        }

        private int ParseCommandDefinitionId(JsonObject obj, PerformerCommandKind commandKind, string context)
        {
            JsonNode? node = obj["definitionId"];
            if (CommandRequiresDefinitionId(commandKind))
            {
                return ResolveRequiredPerformerDefinitionId(node, $"{context}.definitionId");
            }

            if (node != null)
            {
                if (commandKind == PerformerCommandKind.SetParam)
                {
                    return ResolveRequiredPerformerDefinitionId(node, $"{context}.definitionId");
                }

                throw new InvalidOperationException(
                    $"{context}.definitionId is only valid for CreatePerformer, SetParam, and DestroyScopedPerformer commands.");
            }

            return 0;
        }

        private static bool CommandRequiresDefinitionId(PerformerCommandKind commandKind)
        {
            return commandKind is PerformerCommandKind.CreatePerformer
                or PerformerCommandKind.DestroyScopedPerformer;
        }

        private int ResolveRequiredPerformerDefinitionId(JsonNode? node, string context)
        {
            if (node == null)
            {
                throw new InvalidOperationException($"{context} requires an explicit semantic string.");
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    throw new InvalidOperationException(
                        $"Performer command definitionId must be a semantic string, not numeric id {numericId}.");
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    key = RequireCanonicalString(key, context);
                    int id = _registry.GetId(key);
                    if (id <= 0)
                    {
                        throw new InvalidOperationException($"Performer command references unknown definition '{key}'.");
                    }

                    return id;
                }
            }

            throw new InvalidOperationException($"{context} must be a non-empty semantic string.");
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
                    ParamKey = ParseRequiredParamKey(arr[i]!["paramKey"], $"bindings[{i}].paramKey"),
                    Value = ParseValueRef(arr[i]!, $"bindings[{i}]"),
                };
            }

            return bindings;
        }

        private ValueRef ParseValueRef(JsonNode node, string context)
        {
            RejectRemovedBindingFields(node, context);
            string source = node["source"]?.GetValue<string>();
            return source switch
            {
                "attribute" => ValueRef.FromAttribute(ResolveAttributeId(node)),
                "attributeRatio" => ValueRef.FromAttributeRatio(ResolveAttributeId(node)),
                "attributeBase" => ValueRef.FromAttributeBase(ResolveAttributeId(node)),
                "graph" => ValueRef.FromGraph(ParseRequiredInt(node["sourceId"], "Performer binding graph.sourceId")),
                "entityColor" => ValueRef.FromEntityColor(ParseRequiredInt(node["sourceId"], "Performer binding entityColor.sourceId")),
                "entityColorVector" => ValueRef.FromEntityColorVector(),
                "facingRadians" => ValueRef.FromFacingRadians(),
                "facingDegrees" => ValueRef.FromFacingDegrees(),
                "textToken" => ValueRef.FromConstant(ResolveTextTokenId(node)),
                "constant" => ValueRef.FromConstant(ParseRequiredFloat(node["constantValue"], "Performer binding constant.constantValue")),
                null or "" => throw new InvalidOperationException("Performer binding must declare explicit source."),
                _ => throw new InvalidOperationException($"Performer binding source has invalid value '{source}'."),
            };
        }

        private static void RejectRemovedBindingFields(JsonNode node, string context)
        {
            if (node["sourceKey"] != null)
            {
                throw new InvalidOperationException(
                    $"{context} uses removed field 'sourceKey'. Use the canonical source-specific field.");
            }
        }

        private static WorldHudValueMode ParseWorldTextMode(JsonNode node, string key, BehaviorSlot[] behaviors)
        {
            bool hasWorldText = HasWorldTextAssetBinding(behaviors);
            if (!hasWorldText)
            {
                if (node["worldTextMode"] != null)
                {
                    throw new InvalidOperationException(
                        $"Performer '{key}' declares worldTextMode without a WorldText AssetBinding behavior.");
                }

                return WorldHudValueMode.None;
            }

            return ParseRequiredEnum<WorldHudValueMode>(
                node["worldTextMode"],
                $"Performer '{key}' worldTextMode");
        }

        private static bool HasWorldTextAssetBinding(BehaviorSlot[] behaviors)
        {
            if (behaviors == null)
            {
                return false;
            }

            for (int i = 0; i < behaviors.Length; i++)
            {
                if (behaviors[i].Kind == BehaviorKind.AssetBinding &&
                    behaviors[i].AssetBinding.AssetKind == AssetKind.WorldText)
                {
                    return true;
                }
            }

            return false;
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

        private int ResolveAttributeId(JsonNode node)
        {
            if (node["attributeName"] != null)
            {
                throw new InvalidOperationException(
                    "Performer attribute binding uses removed field 'attributeName'; use canonical semantic field 'attributeId'.");
            }

            JsonNode? idNode = node["attributeId"];
            if (idNode is JsonValue value && value.TryGetValue<int>(out int numericId))
            {
                throw new InvalidOperationException(
                    $"Performer attribute binding attributeId must be a semantic string, not numeric id {numericId}.");
            }

            string name = string.Empty;
            if (node["attributeId"] is JsonValue attributeIdValue &&
                attributeIdValue.TryGetValue<string>(out string? attributeIdText))
            {
                name = attributeIdText;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                name = RequireCanonicalString(name, "Performer attribute binding attribute id");
                int id = _resolveAttributeName(name);
                if (id >= 0)
                {
                    return id;
                }
            }

            throw new InvalidOperationException("Performer attribute binding requires non-empty semantic field 'attributeId'.");
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
                    throw new InvalidOperationException(
                        $"Performer scopeTag must be a semantic string, not numeric id {numericId}.");
                }

                if (value.TryGetValue<string>(out string text))
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        throw new InvalidOperationException("Performer scopeTag must be omitted or a non-empty semantic string.");
                    }

                    text = RequireCanonicalString(text, "Performer scopeTag");
                    if (int.TryParse(text, out int parsed))
                    {
                        throw new InvalidOperationException(
                            $"Performer scopeTag must be a semantic string, not numeric string '{parsed}'.");
                    }

                    return PerformerScopeTagRegistry.Register(text);
                }
            }

            throw new InvalidOperationException("Performer scopeTag must be a non-empty semantic string.");
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
                    DefinitionId = ResolveRequiredPerformerDefinitionId(obj["definitionId"], $"children[{i}].definitionId"),
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
                if (!CommandRequiresDefinitionId(rule.Command.CommandKind) &&
                    rule.Command.PerformerDefinitionId <= 0)
                {
                    continue;
                }

                int referencedDefinitionId = rule.Command.PerformerDefinitionId;
                if (referencedDefinitionId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Performer '{key}' rule[{i}] references an unknown performer definition.");
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
            uint seenSlots = 0u;
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Performer behavior[{i}] must be an object.");
                }

                BehaviorKind kind = ParseRequiredEnum<BehaviorKind>(obj["kind"], $"Performer '{ownerKey}' behavior[{i}].kind");
                int slotIndex = ParseRequiredBehaviorSlot(obj["slot"], $"Performer '{ownerKey}' behavior[{i}].slot");
                if (slotIndex is < 0 or >= 32)
                {
                    throw new InvalidOperationException($"Performer '{ownerKey}' behavior[{i}] uses slot {slotIndex}, but valid behavior slots are 0-31.");
                }

                uint slotBit = 1u << slotIndex;
                if ((seenSlots & slotBit) != 0u)
                {
                    throw new InvalidOperationException($"Performer '{ownerKey}' defines duplicate behavior slot '{obj["slot"]?.GetValue<string>()}'.");
                }

                seenSlots |= slotBit;
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

        private InstancedBatchBinding[] ParseInstancedBatchBindings(JsonNode? node, string ownerKey)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                return Array.Empty<InstancedBatchBinding>();
            }

            var bindings = new InstancedBatchBinding[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Performer '{ownerKey}' instancedBatches[{i}] must be an object.");
                }

                string batchKey = ParseRequiredSemanticString(
                    obj["batchAssetId"],
                    $"Performer '{ownerKey}' instancedBatches[{i}].batchAssetId");
                int batchAssetId = _resolveInstancedBatchAssetId(batchKey);
                if (batchAssetId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Performer '{ownerKey}' references unknown instanced batch asset '{batchKey}'.");
                }

                bindings[i] = new InstancedBatchBinding(batchAssetId);
            }

            return bindings;
        }

        private static string ParseRequiredSemanticString(JsonNode? node, string context)
        {
            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    throw new InvalidOperationException($"{context} must be a semantic string, not numeric id {numericId}.");
                }

                if (value.TryGetValue<string>(out string? text) && !string.IsNullOrWhiteSpace(text))
                {
                    return RequireCanonicalString(text, context);
                }
            }

            throw new InvalidOperationException($"{context} must be a semantic string.");
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

                if (obj["value"] != null)
                {
                    throw new InvalidOperationException(
                        $"paramDefaults[{i}] uses removed field 'value'. Use explicit lane-specific fields 'floatValue', 'intValue', or 'vectorValue'.");
                }

                ParamLane lane = ParseRequiredParamLane(obj, $"paramDefaults[{i}]");
                var paramDefault = new ParamDefault
                {
                    ParamKey = ParseRequiredParamKey(obj["paramKey"], $"paramDefaults[{i}].paramKey"),
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

            AssetKind assetKind = ParseRequiredEnum<AssetKind>(obj["assetKind"], "AssetBinding.assetKind");
            VisualRenderPath renderPath = ParseRequiredEnum<VisualRenderPath>(obj["renderPath"], "AssetBinding.renderPath");
            ValidateAssetBindingRenderPath(assetKind, renderPath);
            int assetIdParamKey = ParseOptionalParamKey(obj["assetIdParamKey"], "AssetBinding.assetIdParamKey");
            int assetSwapParamKey = ParseOptionalParamKey(obj["assetSwapParamKey"], "AssetBinding.assetSwapParamKey");
            string surfaceLayerKey = ParseOptionalCanonicalString(obj["surfaceLayerKey"], "AssetBinding.surfaceLayerKey");
            int sortId = obj["sortId"]?.GetValue<int>() ?? 0;
            MaterialCustomDataBinding materialCustomData = ParseMaterialCustomData(obj["materialCustomData"], renderPath);
            ValidateSurfaceMetadata(assetKind, surfaceLayerKey, obj.ContainsKey("surfaceLayerKey"), obj.ContainsKey("sortId"));
            if (assetKind == AssetKind.WorldHud &&
                (obj.ContainsKey("assetId") || assetIdParamKey >= 0 || assetSwapParamKey >= 0 || obj.ContainsKey("assetSwapTable")))
            {
                throw new InvalidOperationException("WorldHud AssetBinding must not declare assetId, assetIdParamKey, assetSwapParamKey, or assetSwapTable.");
            }

            AssetSwapEntry[] assetSwapTable = assetKind == AssetKind.WorldHud
                ? Array.Empty<AssetSwapEntry>()
                : ParseAssetSwapTable(assetKind, obj["assetSwapTable"]);
            if (assetIdParamKey >= 0 && assetSwapParamKey >= 0)
            {
                throw new InvalidOperationException("AssetBinding must not declare both assetIdParamKey and assetSwapParamKey.");
            }

            if (assetSwapParamKey < 0 && assetSwapTable.Length != 0)
            {
                throw new InvalidOperationException("AssetBinding.assetSwapTable requires explicit assetSwapParamKey.");
            }

            if (assetSwapParamKey >= 0 && assetSwapTable.Length == 0)
            {
                throw new InvalidOperationException("AssetBinding.assetSwapParamKey requires a non-empty assetSwapTable.");
            }

            return new AssetBindingConfig
            {
                AssetKind = assetKind,
                AssetId = ResolveAssetBindingAssetId(assetKind, obj["assetId"]),
                MaterialId = ResolveRegisteredId(_resolveMaterialId, obj["materialId"], "material"),
                RenderPath = renderPath,
                Mobility = ParseRequiredEnum<VisualMobility>(obj["mobility"], "AssetBinding.mobility"),
                LocalOffset = ParseVector3(obj["localOffset"]),
                LocalRotation = ParseQuaternion(obj["localRotation"]),
                LocalScale = ParseVector3OrDefault(obj["localScale"], Vector3.One),
                ScaleParamKey = ParseOptionalParamKey(obj["scaleParamKey"], "AssetBinding.scaleParamKey"),
                ColorParamKey = ParseOptionalParamKey(obj["colorParamKey"], "AssetBinding.colorParamKey"),
                MaterialParamKey = ParseOptionalParamKey(obj["materialParamKey"], "AssetBinding.materialParamKey"),
                AssetIdParamKey = assetIdParamKey,
                AssetSwapParamKey = assetSwapParamKey,
                AssetSwapTable = assetSwapTable,
                VisibilityParamKey = ParseOptionalParamKey(obj["visibilityParamKey"], "AssetBinding.visibilityParamKey"),
                SurfaceLayerKey = surfaceLayerKey,
                SortId = sortId,
                MaterialCustomData = materialCustomData,
                HasMaxLod = obj.ContainsKey("maxLod"),
                MaxLod = ParseEnum(obj["maxLod"]?.GetValue<string>(), LODLevel.Low),
            };
        }

        private static void ValidateSurfaceMetadata(
            AssetKind assetKind,
            string surfaceLayerKey,
            bool hasSurfaceLayerKey,
            bool hasSortId)
        {
            if (assetKind == AssetKind.Surface)
            {
                if (string.IsNullOrWhiteSpace(surfaceLayerKey))
                {
                    throw new InvalidOperationException("Surface AssetBinding requires non-empty surfaceLayerKey.");
                }

                return;
            }

            if (hasSurfaceLayerKey)
            {
                throw new InvalidOperationException("AssetBinding.surfaceLayerKey is only valid for Surface assets.");
            }

            if (hasSortId)
            {
                throw new InvalidOperationException("AssetBinding.sortId is only valid for Surface assets.");
            }
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
                TargetParamKey = ParseRequiredParamKey(obj["targetParamKey"], "AttributeBinding.targetParamKey"),
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

            if (obj["tag"] != null)
            {
                throw new InvalidOperationException(
                    "TagBinding behavior uses removed field 'tag'; use canonical semantic field 'tagBinding.tagId'.");
            }

            int tagId = ResolveTagId(obj["tagId"]);
            if (tagId <= 0)
            {
                throw new InvalidOperationException("TagBinding behavior requires non-empty field 'tagBinding.tagId'.");
            }

            return new TagBindingConfig
            {
                TagId = tagId,
                TargetParamKey = ParseRequiredParamKey(obj["targetParamKey"], "TagBinding.targetParamKey"),
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
                SpeedParamKey = ParseOptionalParamKey(obj["speedParamKey"], "Animator.speedParamKey"),
                StateParamKey = ParseOptionalParamKey(obj["stateParamKey"], "Animator.stateParamKey"),
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
                Mode = ParseRequiredEnum<GroundingMode>(obj["mode"], "Grounding.mode"),
                Offset = obj["offset"]?.GetValue<float>() ?? 0f,
                UpdatePolicy = ParseRequiredEnum<GroundingUpdatePolicy>(obj["updatePolicy"], "Grounding.updatePolicy"),
            };
        }

        private static void ValidateAssetBindingRenderPath(AssetKind assetKind, VisualRenderPath renderPath)
        {
            switch (assetKind)
            {
                case AssetKind.Mesh:
                case AssetKind.Decal:
                case AssetKind.VFX:
                    if (!renderPath.IsStaticInstanceLane())
                    {
                        throw new InvalidOperationException(
                            $"AssetBinding assetKind '{assetKind}' requires a static visual renderPath, not '{renderPath}'.");
                    }

                    break;
                case AssetKind.SkinnedMesh:
                    if (!renderPath.IsSkinnedLane())
                    {
                        throw new InvalidOperationException(
                            $"AssetBinding assetKind '{assetKind}' requires a skinned visual renderPath, not '{renderPath}'.");
                    }

                    break;
                case AssetKind.Surface:
                    if (renderPath != VisualRenderPath.Surface)
                    {
                        throw new InvalidOperationException(
                            $"AssetBinding assetKind '{assetKind}' requires renderPath 'Surface', not '{renderPath}'.");
                    }

                    break;
                case AssetKind.WorldHud:
                case AssetKind.WorldText:
                case AssetKind.Sound:
                case AssetKind.Spline:
                case AssetKind.GroundOverlay:
                    if (renderPath != VisualRenderPath.None)
                    {
                        throw new InvalidOperationException(
                            $"AssetBinding assetKind '{assetKind}' requires renderPath 'None', not '{renderPath}'.");
                    }

                    break;
                default:
                    throw new InvalidOperationException($"AssetBinding assetKind '{assetKind}' has no renderPath contract.");
            }
        }

        private static MaterialCustomDataBinding ParseMaterialCustomData(JsonNode? node, VisualRenderPath renderPath)
        {
            if (node == null)
            {
                return MaterialCustomDataBinding.Empty;
            }

            if (!MaterialCustomDataSupported(renderPath))
            {
                throw new InvalidOperationException(
                    $"AssetBinding.materialCustomData is not supported by renderPath '{renderPath}'.");
            }

            if (node is not JsonArray arr)
            {
                throw new InvalidOperationException("AssetBinding.materialCustomData must be an array.");
            }

            if (arr.Count == 0)
            {
                throw new InvalidOperationException("AssetBinding.materialCustomData must not be empty when declared.");
            }

            if (arr.Count > MaterialCustomDataBinding.MaxSlots)
            {
                throw new InvalidOperationException(
                    $"AssetBinding.materialCustomData supports at most {MaterialCustomDataBinding.MaxSlots} slots.");
            }

            var slots = new MaterialCustomDataSlotBinding[arr.Count];
            bool[] seen = new bool[MaterialCustomDataBinding.MaxSlots];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"materialCustomData[{i}] must be an object.");
                }

                int slot = ParseRequiredInt(obj["slot"], $"materialCustomData[{i}].slot");
                if ((uint)slot >= MaterialCustomDataBinding.MaxSlots)
                {
                    throw new InvalidOperationException(
                        $"materialCustomData[{i}].slot must be between 0 and {MaterialCustomDataBinding.MaxSlots - 1}.");
                }

                if (seen[slot])
                {
                    throw new InvalidOperationException($"materialCustomData[{i}].slot duplicates slot {slot}.");
                }

                seen[slot] = true;
                MaterialCustomDataLane lane = ParseRequiredEnum<MaterialCustomDataLane>(obj["lane"], $"materialCustomData[{i}].lane");
                slots[i] = new MaterialCustomDataSlotBinding
                {
                    Slot = slot,
                    Lane = lane,
                    ParamKey = ParseOptionalParamKey(obj["paramKey"], $"materialCustomData[{i}].paramKey"),
                    DefaultFloatValue = obj["defaultFloatValue"]?.GetValue<float>() ?? 0f,
                    DefaultIntValue = obj["defaultIntValue"]?.GetValue<int>() ?? 0,
                    DefaultVectorValue = ParseVector4(obj["defaultVectorValue"]),
                };
            }

            SortMaterialCustomDataSlots(slots);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Slot != i)
                {
                    throw new InvalidOperationException(
                        "AssetBinding.materialCustomData slots must be contiguous starting at 0.");
                }
            }

            return new MaterialCustomDataBinding { Slots = slots };
        }

        private static bool MaterialCustomDataSupported(VisualRenderPath renderPath)
        {
            return renderPath.IsStaticInstanceLane() ||
                   renderPath.IsSkinnedLane() ||
                   renderPath == VisualRenderPath.Surface;
        }

        private static void SortMaterialCustomDataSlots(MaterialCustomDataSlotBinding[] slots)
        {
            for (int i = 1; i < slots.Length; i++)
            {
                MaterialCustomDataSlotBinding slot = slots[i];
                int j = i - 1;
                while (j >= 0 && slots[j].Slot > slot.Slot)
                {
                    slots[j + 1] = slots[j];
                    j--;
                }

                slots[j + 1] = slot;
            }
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
            int orientationParamKey = ParseOptionalParamKey(obj["orientationParamKey"], "MinimapMarker.orientationParamKey");
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
                ColorParamKey = ParseOptionalParamKey(obj["colorParamKey"], "MinimapMarker.colorParamKey"),
                SizeParamKey = ParseOptionalParamKey(obj["sizeParamKey"], "MinimapMarker.sizeParamKey"),
                VisibilityParamKey = ParseOptionalParamKey(obj["visibilityParamKey"], "MinimapMarker.visibilityParamKey"),
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
                VolumeParamKey = ParseOptionalParamKey(obj["volumeParamKey"], "Sound.volumeParamKey"),
            };
        }

        private MaterialConfig ParseMaterial(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException("Material behavior requires object field 'material'.");
            }

            int materialSwapParamKey = ParseOptionalParamKey(obj["materialSwapParamKey"], "Material.materialSwapParamKey");
            MaterialSwapEntry[] swapTable = ParseMaterialSwapTable(obj["swapTable"]);
            if (materialSwapParamKey < 0 && swapTable.Length != 0)
            {
                throw new InvalidOperationException("Material.swapTable requires explicit materialSwapParamKey.");
            }

            if (materialSwapParamKey >= 0 && swapTable.Length == 0)
            {
                throw new InvalidOperationException("Material.materialSwapParamKey requires a non-empty swapTable.");
            }

            return new MaterialConfig
            {
                BaseMaterialId = ResolveRegisteredId(_resolveMaterialId, obj["baseMaterialId"], "material"),
                MaterialSwapParamKey = materialSwapParamKey,
                SwapTable = swapTable,
            };
        }

        private SplineConfig ParseSpline(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException("Spline behavior requires object field 'spline'.");
            }

            SplineUsage usage = ParseEnum(obj["usage"]?.GetValue<string>(), SplineUsage.Render);
            int speedParamKey = ParseOptionalParamKey(obj["speedParamKey"], "Spline.speedParamKey");
            int progressParamKey = ParseOptionalParamKey(obj["progressParamKey"], "Spline.progressParamKey");
            if (usage == SplineUsage.Patrol && progressParamKey < 0)
            {
                throw new InvalidOperationException("Spline usage 'Patrol' requires explicit progressParamKey.");
            }

            return new SplineConfig
            {
                SplineAssetId = ResolveBehaviorAssetId(AssetKind.Spline, obj["splineAssetId"]),
                Usage = usage,
                WidthParamKey = ParseOptionalParamKey(obj["widthParamKey"], "Spline.widthParamKey"),
                ColorParamKey = ParseOptionalParamKey(obj["colorParamKey"], "Spline.colorParamKey"),
                SpeedParamKey = speedParamKey,
                ProgressParamKey = progressParamKey,
                Loop = obj["loop"]?.GetValue<bool>() ?? false,
                PingPong = obj["pingPong"]?.GetValue<bool>() ?? false,
                WaypointEventId = obj["waypointEventId"]?.GetValue<int>() ?? 0,
            };
        }

        private static int ParseOptionalParamKey(JsonNode? node, string context)
        {
            return ParseParamKey(node, -1, context, allowMissing: true, allowNone: true);
        }

        private static int ParseOptionalCommandParamKey(JsonNode? node, string context)
        {
            return ParseParamKey(node, 0, context, allowMissing: true, allowNone: false);
        }

        private static int ParseRequiredParamKey(JsonNode? node, string context)
        {
            return ParseParamKey(node, 0, context, allowMissing: false, allowNone: false);
        }

        private static int ParseParamKey(
            JsonNode? node,
            int defaultValue,
            string context,
            bool allowMissing,
            bool allowNone)
        {
            if (node == null)
            {
                if (!allowMissing)
                {
                    throw new InvalidOperationException($"{context} requires an explicit semantic string.");
                }

                return defaultValue;
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    throw new InvalidOperationException(
                        $"{context} uses numeric authoring value {numericId}. Use a semantic string instead.");
                }

                if (value.TryGetValue<string>(out string? key))
                {
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        throw new InvalidOperationException($"{context} must be a non-empty semantic string.");
                    }

                    key = RequireCanonicalString(key, context);
                    if (string.Equals(key, "none", StringComparison.Ordinal))
                    {
                        if (!allowNone)
                        {
                            throw new InvalidOperationException($"{context} does not allow the 'none' sentinel.");
                        }

                        return -1;
                    }

                    if (IsNonCanonicalNoneSentinel(key))
                    {
                        throw new InvalidOperationException($"{context} uses invalid sentinel '{key}'. Use lowercase 'none'.");
                    }

                    return PerformerParamKeyRegistry.Register(key);
                }
            }

            throw new InvalidOperationException($"{context} must be a non-empty semantic string.");
        }

        private static bool IsNonCanonicalNoneSentinel(string key)
        {
            return key.Length == 4 &&
                   (key[0] == 'n' || key[0] == 'N') &&
                   (key[1] == 'o' || key[1] == 'O') &&
                   (key[2] == 'n' || key[2] == 'N') &&
                   (key[3] == 'e' || key[3] == 'E');
        }

        private static string RequireCanonicalString(string text, string context)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"{context} must be a non-empty semantic string.");
            }

            string trimmed = text.Trim();
            if (!string.Equals(text, trimmed, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{context} must not include leading or trailing whitespace.");
            }

            return text;
        }

        private static string ParseOptionalCanonicalString(JsonNode? node, string context)
        {
            if (node == null)
            {
                return string.Empty;
            }

            if (node is JsonValue value && value.TryGetValue<string>(out string? text))
            {
                return RequireCanonicalString(text, context);
            }

            throw new InvalidOperationException($"{context} must be a non-empty semantic string.");
        }

        private static int ParseOptionalBehaviorSlot(JsonNode? node, string context)
        {
            return ParseBehaviorSlot(node, -1, context, allowMissing: true);
        }

        private static int ParseRequiredBehaviorSlot(JsonNode? node, string context)
        {
            return ParseBehaviorSlot(node, -1, context, allowMissing: false);
        }

        private static int ParseBehaviorSlot(JsonNode? node, int defaultValue, string context, bool allowMissing)
        {
            if (node == null)
            {
                if (!allowMissing)
                {
                    throw new InvalidOperationException($"{context} requires an explicit semantic string.");
                }

                return defaultValue;
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    throw new InvalidOperationException(
                        $"{context} uses numeric authoring value {numericId}. Use a semantic string instead.");
                }

                if (value.TryGetValue<string>(out string? key))
                {
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        throw new InvalidOperationException($"{context} must be a non-empty semantic string.");
                    }

                    key = RequireCanonicalString(key, context);
                    return PerformerBehaviorSlotRegistry.Register(key);
                }
            }

            throw new InvalidOperationException($"{context} must be a non-empty semantic string.");
        }

        private static class PerformerBehaviorSlotRegistry
        {
            private static readonly Dictionary<string, int> Slots = new(StringComparer.Ordinal)
            {
                ["body"] = 0,
                ["attachment"] = 1,
                ["minimap"] = 2,
                ["grounding"] = 3,
                ["animator"] = 4,
                ["material"] = 5,
                ["sound"] = 6,
                ["spline"] = 7,
                ["attribute"] = 8,
                ["tag"] = 9,
                ["orientation"] = 10,
                ["hud"] = 11,
            };

            public static int Register(string key)
            {
                if (!Slots.TryGetValue(key, out int slot))
                {
                    throw new InvalidOperationException(
                        $"Unknown performer behavior slot '{key}'. Register a semantic slot in PerformerBehaviorSlotRegistry instead of relying on load order.");
                }

                return slot;
            }
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
                    OutputParamKey = ParseRequiredParamKey(obj["outputParamKey"], $"thresholds[{i}].outputParamKey"),
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
                    ParamValue = ParseRequiredFiniteFloat(obj["paramValue"], $"swapTable[{i}].paramValue"),
                    MaterialId = ResolveRegisteredId(_resolveMaterialId, obj["materialId"], "material"),
                };

                ValidateUniqueParamValue(table, i, table[i].ParamValue, $"swapTable[{i}].paramValue");
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
                    ParamValue = ParseRequiredFiniteFloat(obj["paramValue"], $"assetSwapTable[{i}].paramValue"),
                    AssetId = ResolveAssetBindingAssetId(assetKind, obj["assetId"]),
                };

                ValidateUniqueParamValue(table, i, table[i].ParamValue, $"assetSwapTable[{i}].paramValue");
            }

            return table;
        }

        private int ResolveAssetBindingAssetId(AssetKind kind, JsonNode? node)
        {
            return kind switch
            {
                AssetKind.WorldHud => 0,
                AssetKind.GroundOverlay => ResolveGroundOverlayShapeId(node),
                _ => ResolveBehaviorAssetId(kind, node),
            };
        }

        private int ResolveBehaviorAssetId(AssetKind kind, JsonNode? node)
        {
            if (node == null)
            {
                throw new InvalidOperationException($"Performer behavior {kind} assetId requires an explicit semantic string.");
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    throw new InvalidOperationException(
                        $"Performer behavior {kind} assetId must be a semantic string, not numeric id {numericId}.");
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    key = RequireCanonicalString(key, $"Performer behavior {kind} assetId");
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

            throw new InvalidOperationException($"Performer behavior {kind} assetId must be a semantic string.");
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
                    throw new InvalidOperationException(
                        $"GroundOverlay AssetBinding assetId must be a shape string, not numeric id {numericId}.");
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    key = RequireCanonicalString(key, "GroundOverlay AssetBinding assetId");
                    if (TryParseDefinedEnum(key, out GroundOverlayShape shape))
                    {
                        return (int)shape;
                    }

                    throw new InvalidOperationException($"GroundOverlay AssetBinding assetId '{key}' is invalid. Use Circle, Cone, Line, or Ring.");
                }
            }

            throw new InvalidOperationException("GroundOverlay AssetBinding assetId must be a shape string.");
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
                    throw new InvalidOperationException(
                        $"Performer behavior {subject} must be a semantic string, not numeric id {numericId}.");
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    key = RequireCanonicalString(key, $"Performer behavior {subject}");
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
                    throw new InvalidOperationException(
                        $"Performer behavior tagId must be a semantic string, not numeric id {numericId}.");
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    key = RequireCanonicalString(key, "Performer behavior tagId");
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

            if (!TryParseDefinedEnum(laneText, out ParamLane lane))
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
            if (node["inline"] != null)
            {
                cond.Inline = ParseRequiredEnumOrDefault(node["inline"], InlineConditionKind.None, "Performer visibility.inline");
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

        private static Vector4 ParseRequiredVector4(JsonNode? node, string context)
        {
            if (node is not JsonArray arr || arr.Count < 4)
            {
                throw new InvalidOperationException($"{context} requires an explicit 4-component array field.");
            }

            return new Vector4(
                ParseRequiredFloat(arr[0], $"{context}[0]"),
                ParseRequiredFloat(arr[1], $"{context}[1]"),
                ParseRequiredFloat(arr[2], $"{context}[2]"),
                ParseRequiredFloat(arr[3], $"{context}[3]"));
        }

        private static float ParseRequiredFloat(JsonNode? node, string context)
        {
            if (node is not JsonValue value || !value.TryGetValue<float>(out float parsed))
            {
                throw new InvalidOperationException($"{context} requires an explicit numeric field.");
            }

            return parsed;
        }

        private static float ParseRequiredFiniteFloat(JsonNode? node, string context)
        {
            float parsed = ParseRequiredFloat(node, context);
            if (!float.IsFinite(parsed))
            {
                throw new InvalidOperationException($"{context} must be finite.");
            }

            return parsed;
        }

        private static void ValidateUniqueParamValue(MaterialSwapEntry[] table, int count, float value, string context)
        {
            for (int i = 0; i < count; i++)
            {
                if (MathF.Abs(table[i].ParamValue - value) <= 0.0001f)
                {
                    throw new InvalidOperationException($"{context} duplicates swapTable[{i}].paramValue.");
                }
            }
        }

        private static void ValidateUniqueParamValue(AssetSwapEntry[] table, int count, float value, string context)
        {
            for (int i = 0; i < count; i++)
            {
                if (MathF.Abs(table[i].ParamValue - value) <= 0.0001f)
                {
                    throw new InvalidOperationException($"{context} duplicates assetSwapTable[{i}].paramValue.");
                }
            }
        }

        private static int ParseRequiredInt(JsonNode? node, string context)
        {
            if (node is not JsonValue value || !value.TryGetValue<int>(out int parsed))
            {
                throw new InvalidOperationException($"{context} requires an explicit integer field.");
            }

            return parsed;
        }

        private static bool ParseRequiredBool(JsonNode? node, string context)
        {
            if (node is not JsonValue value || !value.TryGetValue<bool>(out bool parsed))
            {
                throw new InvalidOperationException($"{context} requires an explicit boolean field.");
            }

            return parsed;
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
            if (s == null)
            {
                return defaultValue;
            }

            if (string.IsNullOrWhiteSpace(s))
            {
                throw new InvalidOperationException($"Enum {typeof(T).Name} requires a non-empty value when explicitly configured.");
            }

            if (!TryParseDefinedEnum(s, out T parsed))
            {
                throw new InvalidOperationException($"Enum {typeof(T).Name} has invalid value '{s}'.");
            }

            return parsed;
        }

        private static T ParseRequiredEnum<T>(JsonNode? node, string context) where T : struct, Enum
        {
            if (node is not JsonValue value)
            {
                throw new InvalidOperationException($"{context} requires a non-empty enum string. Field must be explicit.");
            }

            if (value.TryGetValue<int>(out int numericValue))
            {
                throw new InvalidOperationException(
                    $"{context} must be an enum string, not numeric value {numericValue}.");
            }

            if (!value.TryGetValue<string>(out string? text) || string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"{context} requires a non-empty enum string. Field must be explicit.");
            }

            if (!TryParseDefinedEnum(text, out T parsed))
            {
                throw new InvalidOperationException($"{context} has invalid value '{text}'.");
            }

            return parsed;
        }

        private static T ParseRequiredNonNoneEnum<T>(JsonNode? node, string context) where T : struct, Enum
        {
            T parsed = ParseRequiredEnum<T>(node, context);
            if (EqualityComparer<T>.Default.Equals(parsed, default))
            {
                throw new InvalidOperationException($"{context} must not be '{parsed}'.");
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

        private static bool TryParseDefinedEnum<T>(string text, out T parsed) where T : struct, Enum
        {
            parsed = default;
            if (!string.Equals(text, text.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }

            return Enum.TryParse(text, ignoreCase: false, out parsed) && Enum.IsDefined(typeof(T), parsed);
        }
    }
}
