using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Assets;
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
        private readonly Func<int, bool> _materialSupportsCustomData;
        private readonly Func<int, MaterialAssetDomain?> _resolveMaterialDomain;

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
            Func<int, bool> materialSupportsCustomData = null,
            Func<int, MaterialAssetDomain?> resolveMaterialDomain = null)
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
            _materialSupportsCustomData = materialSupportsCustomData ?? (_ => false);
            _resolveMaterialDomain = resolveMaterialDomain ?? (_ => null);
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Presentation/performers.json", ConfigMergePolicy.ArrayById, "id");
            var fragments = _configs.CollectFragmentsWithSources(entry.RelativePath);
            RejectRawCaseVariantDefinitionIds(fragments, entry.IdField);
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

                RejectCaseVariantDuplicate(mergedByKey, key);
                mergedByKey[key] = obj;
                _registry.GetOrRegisterId(key);
            }

            foreach ((string key, JsonObject _) in mergedByKey)
            {
                JsonObject expanded = ExpandDefinition(key, mergedByKey, new HashSet<string>(StringComparer.Ordinal));
                var (_, def) = ParseDefinition(expanded);
                if (def != null)
                {
                    ValidateBehaviorReferences(def);
                    _registry.Register(key, def);
                }
            }
        }

        private static void RejectRawCaseVariantDefinitionIds(IReadOnlyList<ConfigFragment> fragments, string idField)
        {
            var seen = new Dictionary<string, PerformerIdOccurrence>(StringComparer.OrdinalIgnoreCase);
            for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                ConfigFragment fragment = fragments[fragmentIndex];
                if (fragment.Node is not JsonArray array)
                {
                    continue;
                }

                for (int itemIndex = 0; itemIndex < array.Count; itemIndex++)
                {
                    if (array[itemIndex] is not JsonObject obj ||
                        !TryReadDefinitionId(obj, idField, out string key))
                    {
                        continue;
                    }

                    if (seen.TryGetValue(key, out PerformerIdOccurrence existing))
                    {
                        if (!string.Equals(existing.Id, key, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Performer definition id '{key}' in {fragment.SourceUri} differs only by case from '{existing.Id}' in {existing.SourceUri}. Performer ids are case-sensitive SSOT keys.");
                        }

                        continue;
                    }

                    seen.Add(key, new PerformerIdOccurrence(key, fragment.SourceUri));
                }
            }
        }

        private static bool TryReadDefinitionId(JsonObject obj, string idField, out string key)
        {
            key = string.Empty;
            if (!obj.TryGetPropertyValue(idField, out JsonNode? idNode) ||
                idNode is not JsonValue value ||
                !value.TryGetValue(out string id) ||
                string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            key = id;
            return true;
        }

        private readonly struct PerformerIdOccurrence
        {
            public readonly string Id;
            public readonly string SourceUri;

            public PerformerIdOccurrence(string id, string sourceUri)
            {
                Id = id;
                SourceUri = sourceUri;
            }
        }

        private static void RejectCaseVariantDuplicate(IReadOnlyDictionary<string, JsonObject> definitions, string key)
        {
            foreach (string existing in definitions.Keys)
            {
                if (string.Equals(existing, key, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(existing, key, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Performer definition id '{key}' differs only by case from '{existing}'. Performer ids are case-sensitive SSOT keys.");
                }
            }
        }

        private static void ValidateBehaviorReferences(PerformerDefinition def)
        {
            if (def.Behaviors == null || def.Behaviors.Length == 0)
            {
                return;
            }

            for (int i = 0; i < def.Behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref def.Behaviors[i];
                if (slot.SlotIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Performer '{def.Key}' behavior[{i}] declares invalid negative slot '{slot.SlotIndex}'.");
                }

                for (int j = i + 1; j < def.Behaviors.Length; j++)
                {
                    if (slot.SlotIndex == def.Behaviors[j].SlotIndex)
                    {
                        throw new InvalidOperationException(
                            $"Performer '{def.Key}' declares duplicate behavior slot '{slot.SlotIndex}'.");
                    }
                }

                if (slot.Kind == BehaviorKind.Animator)
                {
                    if (slot.Animator.AnimatorControllerId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Performer '{def.Key}' Animator behavior slot '{slot.SlotIndex}' requires animatorControllerId.");
                    }

                    if (slot.Animator.AnimationProfileId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Performer '{def.Key}' Animator behavior slot '{slot.SlotIndex}' requires animationProfileId.");
                    }
                }

                if (slot.Kind != BehaviorKind.AssetBinding)
                {
                    continue;
                }

                ref readonly AssetBindingConfig asset = ref slot.AssetBinding;
                if (asset.RenderPath.IsSkinnedLane())
                {
                    int animatorIndex = FindBehaviorSlotIndex(def.Behaviors, asset.AnimatorSlot);
                    if (animatorIndex < 0)
                    {
                        throw new InvalidOperationException(
                            $"Performer '{def.Key}' AssetBinding slot '{slot.SlotIndex}' references missing animatorSlot '{asset.AnimatorSlot}'.");
                    }

                    if (def.Behaviors[animatorIndex].Kind != BehaviorKind.Animator)
                    {
                        throw new InvalidOperationException(
                            $"Performer '{def.Key}' AssetBinding slot '{slot.SlotIndex}' animatorSlot '{asset.AnimatorSlot}' points to '{def.Behaviors[animatorIndex].Kind}', not Animator.");
                    }
                }
                else if (asset.AnimatorSlot >= 0)
                {
                    throw new InvalidOperationException(
                        $"Performer '{def.Key}' AssetBinding slot '{slot.SlotIndex}' declares animatorSlot for non-skinned renderPath '{asset.RenderPath}'.");
                }
            }
        }

        private static int FindBehaviorSlotIndex(BehaviorSlot[] slots, int slotIndex)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].SlotIndex == slotIndex)
                {
                    return i;
                }
            }

            return -1;
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
                if (propertyName.Equals("bindings", StringComparison.Ordinal))
                {
                    merged[propertyName] = MergeByIntKey(parent[propertyName], childValue, "paramKey");
                    continue;
                }

                if (propertyName.Equals("paramDefaults", StringComparison.Ordinal))
                {
                    merged[propertyName] = MergeParamDefaultsJson(parent[propertyName], childValue);
                    continue;
                }

                if (propertyName.Equals("behaviors", StringComparison.Ordinal))
                {
                    merged[propertyName] = MergeByIntKey(parent[propertyName], childValue, "slot");
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
            if (node == null)
            {
                return;
            }

            if (node is not JsonArray array)
            {
                throw new InvalidOperationException($"Performer inherited '{keyField}' collection must be an array.");
            }

            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Performer inherited '{keyField}' collection item {i} must be an object.");
                }

                int key = RequireInt(obj, keyField, $"Performer inherited '{keyField}' collection item {i}");
                if (!byKey.ContainsKey(key))
                {
                    order.Add(key);
                }

                byKey[key] = obj;
            }
        }

        private static JsonArray MergeParamDefaultsJson(JsonNode? existingNode, JsonNode? incomingNode)
        {
            var byKey = new Dictionary<(int ParamKey, string Lane), JsonNode>();
            var order = new List<(int ParamKey, string Lane)>();
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
            Dictionary<(int ParamKey, string Lane), JsonNode> byKey,
            List<(int ParamKey, string Lane)> order)
        {
            if (node == null)
            {
                return;
            }

            if (node is not JsonArray array)
            {
                throw new InvalidOperationException("paramDefaults must be an array.");
            }

            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"paramDefaults[{i}] must be an object.");
                }

                RejectRemovedParamDefaultFields(obj, i);
                int paramKey = RequireInt(obj, "paramKey", $"paramDefaults[{i}]");
                string lane = RequireString(obj, "lane", $"paramDefaults[{i}]");
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

            PerformerVisualKind visualKind = ParseEnum(node["visualKind"]?.GetValue<string>(), PerformerVisualKind.GroundOverlay);
            var def = new PerformerDefinition
            {
                Key = key,
                Extends = node["extends"]?.GetValue<string>() ?? string.Empty,
                VisualKind = visualKind,
                MeshOrShapeId = ResolveMeshOrShape(node["meshOrShapeId"], visualKind),
                DefaultColor = ParseColor(node["defaultColor"]),
                DefaultScale = node["defaultScale"]?.GetValue<float>() ?? 1f,
                DefaultLifetime = node["defaultLifetime"]?.GetValue<float>() ?? 0f,
                DefaultFontSize = node["defaultFontSize"]?.GetValue<int>() ?? 16,
                DefaultTextId = ParseDefaultTextId(node["defaultTextId"], visualKind, key),
                PositionOffset = ParseVector3(node["positionOffset"]),
                PositionYDriftPerSecond = node["positionYDriftPerSecond"]?.GetValue<float>() ?? 0f,
                AlphaFadeOverLifetime = node["alphaFadeOverLifetime"]?.GetValue<bool>() ?? false,
                VisibilityCondition = ParseConditionRef(node["visibility"]),
                Rules = ParseRules(node["rules"]),
                Bindings = ParseBindings(node["bindings"], visualKind, key),
                Children = ParseChildren(node["children"]),
                Behaviors = ParseBehaviors(node["behaviors"]),
                ParamDefaults = ParseParamDefaults(node["paramDefaults"], visualKind, key),
                WorldTextValueMode = ParseWorldTextValueMode(node["worldTextValueMode"], visualKind, key),
            };

            if (def.VisualKind == PerformerVisualKind.SurfaceSource)
            {
                def.Surface = ParseSurface(node["surface"], key);
            }
            else if (node["surface"] != null)
            {
                throw new InvalidOperationException(
                    $"Performer '{key}' declares a surface block but visualKind '{def.VisualKind}' is not '{PerformerVisualKind.SurfaceSource}'.");
            }

            def.Rules = ExpandChildrenRules(def.Rules, def.Children);
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

            if (node["maxVisibilityDistanceCm"] != null)
            {
                throw new InvalidOperationException(
                    $"Performer '{key}' still uses removed field 'maxVisibilityDistanceCm'. Distance culling belongs to the culling/AOI contract.");
            }
        }

        private int ResolveMeshOrShape(JsonNode? meshNode, PerformerVisualKind visualKind)
        {
            if (meshNode == null)
            {
                return 0;
            }

            if (meshNode is JsonValue numericValue && numericValue.TryGetValue<int>(out int numericId))
            {
                throw new InvalidOperationException(
                    $"Performer meshId must be a registered string key, not numeric id '{numericId}'.");
            }

            string meshStr = meshNode.ToString().Trim('"');
            if (string.IsNullOrWhiteSpace(meshStr))
            {
                return 0;
            }

            if (visualKind == PerformerVisualKind.GroundOverlay)
            {
                if (Enum.TryParse<GroundOverlayShape>(meshStr, ignoreCase: false, out var shape))
                {
                    return (int)shape;
                }

                throw new InvalidOperationException(
                    $"Invalid GroundOverlayShape value '{meshStr}'. Enum values are case-sensitive.");
            }

            return _resolveMeshId(meshStr);
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
            if (node["keyId"] != null)
            {
                throw new InvalidOperationException("Presentation event keyId was removed. Use string field 'key'.");
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

            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException("Performer rule command must be an object.");
            }

            RejectRemovedCommandFields(obj);
            PerformerCommandKind commandKind = ParseRequiredEnum<PerformerCommandKind>(obj["kind"], "Performer command.kind");
            if (commandKind == PerformerCommandKind.None)
            {
                throw new InvalidOperationException("Performer command.kind must not be None.");
            }

            var command = new PerformerCommand
            {
                CommandKind = commandKind,
                PerformerDefinitionId = ResolvePerformerDefinitionId(obj["definitionId"]),
                ParentHandle = obj["parentHandle"]?.GetValue<int>() ?? -1,
                ScopeTag = ParseScopeTag(obj["scopeTag"]),
                ScopeSource = ParseEnum(obj["scopeSource"]?.GetValue<string>(), PerformerCommandScopeSource.Fixed),
                ParamKey = obj["paramKey"]?.GetValue<int>() ?? 0,
                ParamLane = ParseEnum(obj["paramLane"]?.GetValue<string>(), ParamLane.Float),
                ParamValue = obj["paramValue"]?.GetValue<float>() ?? 0f,
                IntValue = obj["intValue"]?.GetValue<int>() ?? 0,
                VectorValue = ParseVector4(obj["vectorValue"]),
                ParamGraphProgramId = obj["paramGraphProgramId"]?.GetValue<int>() ?? 0,
                TargetBehaviorSlot = obj["targetBehaviorSlot"]?.GetValue<int>() ?? -1,
            };

            ValidateCommandContract(in command, obj);
            return command;
        }

        private static void RejectRemovedCommandFields(JsonObject obj)
        {
            if (obj["commandKind"] != null)
            {
                throw new InvalidOperationException("Performer command field 'commandKind' was removed. Use 'kind'.");
            }

            if (obj["performerDefinitionId"] != null)
            {
                throw new InvalidOperationException("Performer command field 'performerDefinitionId' was removed. Use 'definitionId'.");
            }

            if (obj["scopeId"] != null)
            {
                throw new InvalidOperationException("Performer command field 'scopeId' was removed. Use 'scopeTag' or 'scopeSource'.");
            }

            if (obj["behaviorSlot"] != null)
            {
                throw new InvalidOperationException("Performer command field 'behaviorSlot' was removed. Use 'targetBehaviorSlot'.");
            }

            if (obj["lane"] != null)
            {
                throw new InvalidOperationException("Performer command field 'lane' was removed. Use 'paramLane'.");
            }

            if (obj["floatValue"] != null)
            {
                throw new InvalidOperationException("Performer command field 'floatValue' was removed. Use 'paramValue'.");
            }
        }

        private static void ValidateCommandContract(in PerformerCommand command, JsonObject obj)
        {
            switch (command.CommandKind)
            {
                case PerformerCommandKind.CreatePerformer:
                    if (command.PerformerDefinitionId <= 0)
                    {
                        throw new InvalidOperationException("CreatePerformer command requires registered string field 'definitionId'.");
                    }

                    ValidateCommandScope(command, obj, "CreatePerformer");
                    break;
                case PerformerCommandKind.DestroyPerformerScope:
                    ValidateCommandScope(command, obj, "DestroyPerformerScope");
                    break;
                case PerformerCommandKind.SetParam:
                    if (!obj.ContainsKey("paramKey"))
                    {
                        throw new InvalidOperationException("SetParam command requires integer field 'paramKey'.");
                    }

                    if (!obj.ContainsKey("paramLane"))
                    {
                        throw new InvalidOperationException("SetParam command requires explicit string field 'paramLane'.");
                    }

                    if (command.ParamGraphProgramId <= 0 && !obj.ContainsKey("paramValue"))
                    {
                        throw new InvalidOperationException("SetParam command requires 'paramValue' or 'paramGraphProgramId'.");
                    }

                    break;
                case PerformerCommandKind.DestroyPerformer:
                    break;
                default:
                    throw new InvalidOperationException($"Performer command kind '{command.CommandKind}' is not supported by PerformerRuleSystem.");
            }
        }

        private static void ValidateCommandScope(in PerformerCommand command, JsonObject obj, string commandName)
        {
            if (!obj.ContainsKey("scopeSource"))
            {
                throw new InvalidOperationException($"{commandName} command requires explicit string field 'scopeSource'.");
            }

            if (command.ScopeSource == PerformerCommandScopeSource.Fixed && command.ScopeTag < 0)
            {
                throw new InvalidOperationException($"{commandName} command with Fixed scopeSource requires 'scopeTag'.");
            }
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
                    throw new InvalidOperationException(
                        $"Performer command definitionId must be a registered string key, not numeric id '{numericId}'.");
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

        private PerformerParamBinding[] ParseBindings(JsonNode? node, PerformerVisualKind visualKind, string definitionKey)
        {
            if (node == null)
            {
                return Array.Empty<PerformerParamBinding>();
            }

            if (node is not JsonArray arr)
            {
                throw new InvalidOperationException("Performer bindings must be an array.");
            }

            if (arr.Count == 0)
            {
                return Array.Empty<PerformerParamBinding>();
            }

            var bindings = new PerformerParamBinding[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Performer bindings[{i}] must be an object.");
                }

                bindings[i] = new PerformerParamBinding
                {
                    ParamKey = ParseBindingParamKey(obj, i, visualKind, definitionKey),
                    Value = ParseValueRef(obj),
                };
            }

            return bindings;
        }

        private static int ParseBindingParamKey(JsonObject obj, int index, PerformerVisualKind visualKind, string definitionKey)
        {
            int paramKey = RequireInt(obj, "paramKey", $"Performer bindings[{index}]");
            RejectWorldTextSemanticParamKey(paramKey, visualKind, definitionKey, $"bindings[{index}]");
            return paramKey;
        }

        private ValueRef ParseValueRef(JsonNode node)
        {
            string source = node["source"]?.GetValue<string>() ?? string.Empty;
            return source switch
            {
                nameof(ValueSourceKind.Attribute) => ValueRef.FromAttribute(ResolveAttributeId(node)),
                nameof(ValueSourceKind.AttributeRatio) => ValueRef.FromAttributeRatio(ResolveAttributeId(node)),
                nameof(ValueSourceKind.AttributeBase) => ValueRef.FromAttributeBase(ResolveAttributeId(node)),
                nameof(ValueSourceKind.Graph) => ValueRef.FromGraph(ResolveGraphProgramId(node)),
                nameof(ValueSourceKind.EntityColor) => ValueRef.FromEntityColor(ResolveEntityColorChannel(node)),
                nameof(ValueSourceKind.FacingRadians) => ValueRef.FromFacingRadians(),
                nameof(ValueSourceKind.FacingDegrees) => ValueRef.FromFacingDegrees(),
                nameof(ValueSourceKind.Constant) => ValueRef.FromConstant(RequireFloat(node, "constantValue", "Performer binding source 'Constant'")),
                nameof(ValueSourceKind.TextToken) => throw new InvalidOperationException("Performer binding source 'TextToken' was removed. WorldText tokens belong in 'defaultTextId'."),
                "textToken" => throw new InvalidOperationException("Performer binding source 'textToken' was removed. WorldText tokens belong in 'defaultTextId'."),
                _ => throw new InvalidOperationException(
                    $"Invalid performer binding source '{source}'. Source values are case-sensitive."),
            };
        }

        private int ResolveDefaultTextTokenId(JsonNode node, string key)
        {
            if (node is not JsonValue value || !value.TryGetValue<string>(out string tokenKey))
            {
                throw new InvalidOperationException($"Performer '{key}' defaultTextId must be a registered string key.");
            }

            if (string.IsNullOrWhiteSpace(tokenKey))
            {
                throw new InvalidOperationException($"Performer '{key}' defaultTextId must be a non-empty string key.");
            }

            int tokenId = _resolveTextTokenId(tokenKey);
            if (tokenId <= 0)
            {
                throw new InvalidOperationException($"Performer '{key}' references unknown defaultTextId '{tokenKey}'.");
            }

            return tokenId;
        }

        private static int ResolveGraphProgramId(JsonNode node)
        {
            if (node["sourceId"] != null)
            {
                throw new InvalidOperationException("Performer Graph binding sourceId was removed. Use integer field 'graphProgramId'.");
            }

            if (node["graphProgramId"] == null)
            {
                throw new InvalidOperationException("Performer Graph binding requires integer field 'graphProgramId'.");
            }

            int graphProgramId = node["graphProgramId"]!.GetValue<int>();
            if (graphProgramId <= 0)
            {
                throw new InvalidOperationException("Performer Graph binding graphProgramId must be positive.");
            }

            return graphProgramId;
        }

        private static int ResolveEntityColorChannel(JsonNode node)
        {
            if (node["sourceId"] != null)
            {
                throw new InvalidOperationException("Performer EntityColor binding sourceId was removed. Use string field 'channel'.");
            }

            string channel = node["channel"]?.GetValue<string>() ?? string.Empty;
            return channel switch
            {
                "Red" => 0,
                "Green" => 1,
                "Blue" => 2,
                "Alpha" => 3,
                _ => throw new InvalidOperationException(
                    $"Performer EntityColor binding channel '{channel}' is invalid. Expected Red, Green, Blue, or Alpha."),
            };
        }

        private int ResolveAttributeId(JsonNode node)
        {
            if (node["sourceId"] != null || node["attributeId"] != null)
            {
                throw new InvalidOperationException("Performer attribute reference must be a registered string key in field 'attributeName'.");
            }

            string name = node["attributeName"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Performer attribute reference requires non-empty string field 'attributeName'.");
            }

            int id = _resolveAttributeName(name);
            if (id < 0)
            {
                throw new InvalidOperationException($"Performer references unknown attributeName '{name}'.");
            }

            return id;
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
                        $"Performer scopeTag must be a registered string key, not numeric id '{numericId}'.");
                }

                if (value.TryGetValue<string>(out string text))
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return -1;
                    }

                    if (int.TryParse(text, out int parsed))
                    {
                        throw new InvalidOperationException(
                            $"Performer scopeTag must be a registered string key, not numeric string '{parsed}'.");
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

        private BehaviorSlot[] ParseBehaviors(JsonNode? node)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                return Array.Empty<BehaviorSlot>();
            }

            var slots = new BehaviorSlot[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Performer behavior[{i}] must be an object.");
                }

                BehaviorKind kind = ParseRequiredEnum<BehaviorKind>(obj["kind"], $"Performer behavior[{i}].kind");
                var slot = new BehaviorSlot
                {
                    SlotIndex = RequireInt(obj, "slot", $"Performer behavior[{i}]"),
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
                    default:
                        throw new InvalidOperationException($"Unsupported performer behavior kind '{kind}'.");
                }

                slots[i] = slot;
            }

            return slots;
        }

        private ParamDefault[] ParseParamDefaults(JsonNode? node, PerformerVisualKind? visualKind = null, string definitionKey = "")
        {
            if (node == null)
            {
                return Array.Empty<ParamDefault>();
            }

            if (node is not JsonArray arr)
            {
                throw new InvalidOperationException("paramDefaults must be an array.");
            }

            if (arr.Count == 0)
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

                RejectRemovedParamDefaultFields(obj, i);
                ParamLane lane = ParseParamLane(obj);
                var paramDefault = new ParamDefault
                {
                    ParamKey = ParseParamDefaultKey(obj, i, visualKind, definitionKey),
                    Lane = lane,
                };

                switch (lane)
                {
                    case ParamLane.Int:
                        paramDefault.IntValue = RequireInt(obj, "intValue", $"paramDefaults[{i}] with lane 'Int'");
                        break;
                    case ParamLane.Vector:
                        paramDefault.VectorValue = ParseRequiredVector4(obj["vectorValue"], $"paramDefaults[{i}] with lane 'Vector'.vectorValue");
                        break;
                    default:
                        paramDefault.FloatValue = RequireFloat(obj, "floatValue", $"paramDefaults[{i}] with lane 'Float'");
                        break;
                }

                defaults[i] = paramDefault;
            }

            return defaults;
        }

        private static int ParseParamDefaultKey(JsonObject obj, int index, PerformerVisualKind? visualKind, string definitionKey)
        {
            int paramKey = RequireInt(obj, "paramKey", $"paramDefaults[{index}]");
            if (visualKind.HasValue)
            {
                RejectWorldTextSemanticParamKey(paramKey, visualKind.Value, definitionKey, $"paramDefaults[{index}]");
            }

            return paramKey;
        }

        private static void RejectWorldTextSemanticParamKey(int paramKey, PerformerVisualKind visualKind, string definitionKey, string context)
        {
            if (visualKind != PerformerVisualKind.WorldText)
            {
                return;
            }

            if (paramKey == WellKnownPerformerParamKeys.TextTokenId)
            {
                throw new InvalidOperationException(
                    $"Performer '{definitionKey}' {context} targets reserved WorldText paramKey {WellKnownPerformerParamKeys.TextTokenId}. Use 'defaultTextId'.");
            }

            if (paramKey == WellKnownPerformerParamKeys.TextValueMode)
            {
                throw new InvalidOperationException(
                    $"Performer '{definitionKey}' {context} targets reserved WorldText paramKey {WellKnownPerformerParamKeys.TextValueMode}. Use 'worldTextValueMode'.");
            }
        }

        private AssetBindingConfig ParseAssetBinding(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException("AssetBinding behavior requires object field 'assetBinding'.");
            }

            AssetKind assetKind = ParseRequiredEnum<AssetKind>(obj["assetKind"], "AssetBinding.assetKind");
            var binding = new AssetBindingConfig
            {
                AssetKind = assetKind,
                AssetId = ResolveBehaviorAssetId(assetKind, obj["assetId"]),
                MaterialId = ResolveOptionalRegisteredId(_resolveMaterialId, obj["materialId"], "material"),
                AnimatorSlot = obj["animatorSlot"]?.GetValue<int>() ?? -1,
                RenderPath = ParseRequiredEnum<VisualRenderPath>(obj["renderPath"], "AssetBinding.renderPath"),
                Mobility = ParseRequiredEnum<VisualMobility>(obj["mobility"], "AssetBinding.mobility"),
                LocalOffset = ParseVector3(obj["localOffset"]),
                LocalRotation = ParseQuaternion(obj["localRotation"]),
                LocalScale = ParseVector3OrDefault(obj["localScale"], Vector3.One),
                ScaleParamKey = obj["scaleParamKey"]?.GetValue<int>() ?? -1,
                ColorParamKey = obj["colorParamKey"]?.GetValue<int>() ?? -1,
                MaterialParamKey = obj["materialParamKey"]?.GetValue<int>() ?? -1,
                AssetSwapParamKey = obj["assetSwapParamKey"]?.GetValue<int>() ?? -1,
                VisibilityParamKey = obj["visibilityParamKey"]?.GetValue<int>() ?? -1,
                Grounding = ParseEnum(obj["grounding"]?.GetValue<string>(), GroundingMode.None),
                GroundingOffset = obj["groundingOffset"]?.GetValue<float>() ?? 0f,
                SurfaceLayerKey = ParseOptionalCanonicalString(obj["surfaceLayerKey"], "AssetBinding.surfaceLayerKey"),
                SortId = obj["sortId"]?.GetValue<int>() ?? 0,
                MaterialCustomData = ParseMaterialCustomDataBindings(obj["materialCustomData"]),
            };

            ValidateAssetBinding(in binding, obj);
            return binding;
        }

        private void ValidateAssetBinding(in AssetBindingConfig binding, JsonObject obj)
        {
            if (binding.AssetKind == AssetKind.Surface)
            {
                if (binding.RenderPath != VisualRenderPath.Surface)
                {
                    throw new InvalidOperationException(
                        $"AssetBinding assetKind '{binding.AssetKind}' requires renderPath 'Surface', not '{binding.RenderPath}'.");
                }

                if (string.IsNullOrWhiteSpace(binding.SurfaceLayerKey))
                {
                    throw new InvalidOperationException("Surface AssetBinding requires non-empty surfaceLayerKey.");
                }
            }
            else
            {
                if (binding.RenderPath == VisualRenderPath.Surface)
                {
                    throw new InvalidOperationException(
                        $"AssetBinding renderPath '{binding.RenderPath}' requires assetKind 'Surface', not '{binding.AssetKind}'.");
                }

                if (obj.ContainsKey("surfaceLayerKey"))
                {
                    throw new InvalidOperationException("AssetBinding.surfaceLayerKey is only valid for Surface assets.");
                }

                if (obj.ContainsKey("sortId"))
                {
                    throw new InvalidOperationException("AssetBinding.sortId is only valid for Surface assets.");
                }
            }

            if (binding.RenderPath.IsSkinnedLane())
            {
                if (binding.AssetKind != AssetKind.SkinnedMesh)
                {
                    throw new InvalidOperationException(
                        $"AssetBinding renderPath '{binding.RenderPath}' requires assetKind 'SkinnedMesh', not '{binding.AssetKind}'.");
                }

                if (binding.AnimatorSlot < 0)
                {
                    throw new InvalidOperationException(
                        $"AssetBinding assetKind '{binding.AssetKind}' with renderPath '{binding.RenderPath}' requires explicit animatorSlot.");
                }
            }
            else if (binding.AssetKind == AssetKind.SkinnedMesh)
            {
                throw new InvalidOperationException(
                    $"AssetBinding assetKind '{binding.AssetKind}' requires a skinned renderPath, not '{binding.RenderPath}'.");
            }
            else if (obj.ContainsKey("animatorSlot"))
            {
                throw new InvalidOperationException("AssetBinding.animatorSlot is only valid for skinned render paths.");
            }

            if (binding.AssetKind is AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Surface && binding.AssetId <= 0)
            {
                throw new InvalidOperationException($"AssetBinding assetKind '{binding.AssetKind}' requires a registered assetId.");
            }

            if (binding.AssetKind is AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Surface && binding.MaterialId <= 0)
            {
                throw new InvalidOperationException($"AssetBinding assetKind '{binding.AssetKind}' requires a registered materialId.");
            }

            ValidateMaterialDomain(in binding);

            if (binding.MaterialCustomData.Length > 0 &&
                !AssetKindSemantics.SupportsMaterialCustomData(binding.AssetKind, binding.RenderPath))
            {
                throw new InvalidOperationException(
                    $"AssetBinding assetKind '{binding.AssetKind}' with renderPath '{binding.RenderPath}' cannot consume materialCustomData.");
            }

            if (binding.MaterialCustomData.Length > 0 && !_materialSupportsCustomData(binding.MaterialId))
            {
                throw new InvalidOperationException(
                    $"AssetBinding materialId '{binding.MaterialId}' must declare SupportsPerInstanceCustomData before materialCustomData can be used.");
            }

            if (binding.ColorParamKey >= 0)
            {
                throw new InvalidOperationException("AssetBinding.colorParamKey requires the vector blackboard lane and is not enabled in this runtime. Use materialCustomData for renderer custom values.");
            }
        }

        private void ValidateMaterialDomain(in AssetBindingConfig binding)
        {
            MaterialAssetDomain expected = binding.AssetKind switch
            {
                AssetKind.Mesh => MaterialAssetDomain.Mesh,
                AssetKind.SkinnedMesh => MaterialAssetDomain.SkinnedMesh,
                AssetKind.Surface => MaterialAssetDomain.Surface,
                _ => 0,
            };

            if (expected == 0)
            {
                return;
            }

            MaterialAssetDomain? actual = _resolveMaterialDomain(binding.MaterialId);
            if (!actual.HasValue)
            {
                throw new InvalidOperationException(
                    $"AssetBinding materialId '{binding.MaterialId}' must resolve to a Presentation material asset descriptor.");
            }

            if (actual.Value != expected)
            {
                throw new InvalidOperationException(
                    $"AssetBinding assetKind '{binding.AssetKind}' requires material domain '{expected}', not '{actual.Value}'.");
            }
        }

        private static string ParseOptionalCanonicalString(JsonNode? node, string context)
        {
            if (node == null)
            {
                return string.Empty;
            }

            string value = node.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context} must be a non-empty string when declared.");
            }

            return value;
        }

        private static MaterialCustomDataBinding[] ParseMaterialCustomDataBindings(JsonNode? node)
        {
            if (node == null)
            {
                return Array.Empty<MaterialCustomDataBinding>();
            }

            if (node is not JsonArray arr)
            {
                throw new InvalidOperationException("materialCustomData must be an array when declared.");
            }

            if (arr.Count == 0)
            {
                return Array.Empty<MaterialCustomDataBinding>();
            }

            var bindings = new MaterialCustomDataBinding[arr.Count];
            uint usedSlots = 0;
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"materialCustomData[{i}] must be an object.");
                }

                int slot = obj["slot"]?.GetValue<int>() ?? -1;
                if ((uint)slot >= MaterialCustomData.MaxSlots)
                {
                    throw new InvalidOperationException($"materialCustomData[{i}].slot must be between 0 and {MaterialCustomData.MaxSlots - 1}.");
                }

                uint slotBit = 1u << slot;
                if ((usedSlots & slotBit) != 0)
                {
                    throw new InvalidOperationException($"materialCustomData declares duplicate slot {slot}.");
                }

                usedSlots |= slotBit;
                bindings[i] = new MaterialCustomDataBinding
                {
                    Slot = slot,
                    XParamKey = obj["xParamKey"]?.GetValue<int>() ?? -1,
                    YParamKey = obj["yParamKey"]?.GetValue<int>() ?? -1,
                    ZParamKey = obj["zParamKey"]?.GetValue<int>() ?? -1,
                    WParamKey = obj["wParamKey"]?.GetValue<int>() ?? -1,
                    DefaultValue = obj["defaultValue"] == null
                        ? Vector4.Zero
                        : ParseRequiredVector4(obj["defaultValue"], $"materialCustomData[{i}].defaultValue"),
                };
            }

            return bindings;
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

            return new TagBindingConfig
            {
                TagId = ResolveTagId(obj),
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
                BoneId = obj["boneId"]?.GetValue<int>() ?? 0,
                Offset = ParseVector3(obj["offset"]),
                RotationOffset = ParseQuaternion(obj["rotationOffset"]),
                InheritScale = obj["inheritScale"]?.GetValue<bool>() ?? false,
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

            if (obj["splinePathId"] != null)
            {
                throw new InvalidOperationException("Spline.splinePathId was removed. Use splineAssetId.");
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

        private int ResolveBehaviorAssetId(AssetKind kind, JsonNode? node)
        {
            if (node == null)
            {
                throw new InvalidOperationException($"AssetBinding assetKind '{kind}' requires string field 'assetId'.");
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    throw new InvalidOperationException(
                        $"AssetBinding assetKind '{kind}' assetId must be a registered string key, not numeric id '{numericId}'.");
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    int id = _resolveBehaviorAssetId(kind, key);
                    if (id <= 0)
                    {
                        throw new InvalidOperationException($"AssetBinding assetKind '{kind}' references unknown assetId '{key}'.");
                    }

                    return id;
                }
            }

            throw new InvalidOperationException($"AssetBinding assetKind '{kind}' assetId must be a non-empty registered string key.");
        }

        private static int ResolveRegisteredId(Func<string, int> resolver, JsonNode? node, string subject)
        {
            if (node == null)
            {
                throw new InvalidOperationException($"Performer {subject} reference requires a registered string key.");
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    throw new InvalidOperationException(
                        $"Performer {subject} reference must be a registered string key, not numeric id '{numericId}'.");
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    int id = resolver(key);
                    if (id <= 0)
                    {
                        throw new InvalidOperationException($"Performer references unknown {subject} '{key}'.");
                    }

                    return id;
                }
            }

            throw new InvalidOperationException($"Performer {subject} reference must be a non-empty registered string key.");
        }

        private static int ResolveOptionalRegisteredId(Func<string, int> resolver, JsonNode? node, string subject)
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
                        $"Performer {subject} reference must be a registered string key, not numeric id '{numericId}'.");
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    int id = resolver(key);
                    if (id <= 0)
                    {
                        throw new InvalidOperationException($"Performer references unknown {subject} '{key}'.");
                    }

                    return id;
                }
            }

            throw new InvalidOperationException($"Performer {subject} reference must be a non-empty registered string key.");
        }

        private static int ResolveTagId(JsonObject obj)
        {
            if (obj["tag"] != null)
            {
                throw new InvalidOperationException("TagBinding.tag was removed. Use string field 'tagId'.");
            }

            JsonNode? node = obj["tagId"];
            if (node == null)
            {
                throw new InvalidOperationException("TagBinding requires non-empty string field 'tagId'.");
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    throw new InvalidOperationException(
                        $"Performer tag reference must be a registered string key, not numeric id '{numericId}'.");
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    if (int.TryParse(key, out int numericString))
                    {
                        throw new InvalidOperationException(
                            $"Performer tag reference must be a registered string key, not numeric string '{numericString}'.");
                    }

                    return TagRegistry.Register(key);
                }
            }

            throw new InvalidOperationException("TagBinding.tagId must be a non-empty registered string key.");
        }

        private static ParamLane ParseParamLane(JsonObject obj)
        {
            return ParseRequiredEnum<ParamLane>(obj["lane"], "paramDefaults.lane");
        }

        private int ParseDefaultTextId(JsonNode? node, PerformerVisualKind visualKind, string key)
        {
            if (node == null)
            {
                return 0;
            }

            if (visualKind != PerformerVisualKind.WorldText)
            {
                throw new InvalidOperationException(
                    $"Performer '{key}' declares defaultTextId but visualKind '{visualKind}' is not '{PerformerVisualKind.WorldText}'.");
            }

            return ResolveDefaultTextTokenId(node, key);
        }

        private static WorldHudValueMode ParseWorldTextValueMode(JsonNode? node, PerformerVisualKind visualKind, string key)
        {
            if (node == null)
            {
                return WorldHudValueMode.None;
            }

            if (visualKind != PerformerVisualKind.WorldText)
            {
                throw new InvalidOperationException(
                    $"Performer '{key}' declares worldTextValueMode but visualKind '{visualKind}' is not '{PerformerVisualKind.WorldText}'.");
            }

            return ParseRequiredEnum<WorldHudValueMode>(node, $"Performer '{key}'.worldTextValueMode");
        }

        private static void RejectRemovedParamDefaultFields(JsonObject obj, int index)
        {
            if (obj["value"] != null)
            {
                throw new InvalidOperationException($"paramDefaults[{index}] field 'value' was removed. Use 'floatValue', 'intValue', or 'vectorValue' with explicit 'lane'.");
            }
        }

        private SurfaceAuthoringBlock ParseSurface(JsonNode? node, string key)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"Performer '{key}' uses visualKind '{PerformerVisualKind.SurfaceSource}' but is missing required object field 'surface'.");
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
                UsageHint = ParseEnum(obj["usageHint"]?.GetValue<string>(), PerformerSurfaceUsageHint.Static),
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

        private static PerformerRule[] ExpandChildrenRules(PerformerRule[] rules, ChildPerformerRef[] children)
        {
            if (children == null || children.Length == 0)
            {
                return rules ?? Array.Empty<PerformerRule>();
            }

            int baseCount = rules?.Length ?? 0;
            var expanded = new PerformerRule[baseCount + children.Length];
            if (baseCount > 0)
            {
                Array.Copy(rules!, expanded, baseCount);
            }

            for (int i = 0; i < children.Length; i++)
            {
                ref readonly ChildPerformerRef child = ref children[i];
                expanded[baseCount + i] = new PerformerRule
                {
                    Event = new EventFilter
                    {
                        Kind = PresentationEventKind.PerformerCreated,
                        KeyId = -1,
                    },
                    Condition = ConditionRef.AlwaysTrue,
                    Command = new PerformerCommand
                    {
                        CommandKind = PerformerCommandKind.CreatePerformer,
                        PerformerDefinitionId = child.DefinitionId,
                        ParentHandle = -1,
                        ScopeTag = child.ScopeTag,
                        ScopeSource = PerformerCommandScopeSource.Fixed,
                    },
                };
            }

            return expanded;
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
                throw new InvalidOperationException($"{context} requires a four-number array.");
            }

            return new Vector4(
                arr[0]?.GetValue<float>() ?? throw new InvalidOperationException($"{context}[0] is required."),
                arr[1]?.GetValue<float>() ?? throw new InvalidOperationException($"{context}[1] is required."),
                arr[2]?.GetValue<float>() ?? throw new InvalidOperationException($"{context}[2] is required."),
                arr[3]?.GetValue<float>() ?? throw new InvalidOperationException($"{context}[3] is required."));
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

        private static int RequireInt(JsonObject obj, string fieldName, string context)
        {
            if (obj[fieldName] == null)
            {
                throw new InvalidOperationException($"{context} requires integer field '{fieldName}'.");
            }

            return obj[fieldName]!.GetValue<int>();
        }

        private static string RequireString(JsonObject obj, string fieldName, string context)
        {
            string value = obj[fieldName]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context} requires non-empty string field '{fieldName}'.");
            }

            return value;
        }

        private static float RequireFloat(JsonNode node, string fieldName, string context)
        {
            if (node is not JsonObject obj || obj[fieldName] == null)
            {
                throw new InvalidOperationException($"{context} requires float field '{fieldName}'.");
            }

            return obj[fieldName]!.GetValue<float>();
        }

        private static T ParseRequiredEnum<T>(JsonNode? node, string context) where T : struct, Enum
        {
            string value = node?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context} is required.");
            }

            if (!Enum.TryParse<T>(value, ignoreCase: false, out var parsed))
            {
                throw new InvalidOperationException($"{context} has invalid value '{value}'. Enum values are case-sensitive.");
            }

            return parsed;
        }

        private static T ParseEnum<T>(string? s, T defaultValue) where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return defaultValue;
            }

            if (!Enum.TryParse<T>(s, ignoreCase: false, out var parsed))
            {
                throw new InvalidOperationException($"Invalid {typeof(T).Name} value '{s}'. Enum values are case-sensitive.");
            }

            return parsed;
        }
    }
}
