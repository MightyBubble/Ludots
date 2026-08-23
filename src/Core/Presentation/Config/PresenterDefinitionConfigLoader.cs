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
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Config
{
    /// <summary>
    /// Loads <see cref="PresenterDefinition"/> entries from
    /// <c>Presentation/presenters.json</c>.
    /// </summary>
    public sealed class PresenterDefinitionConfigLoader
    {
        private readonly ConfigPipeline _configs;
        private readonly PresenterDefinitionRegistry _registry;
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
        private readonly PerformerCommandKindRegistry? _commandKinds;
        private readonly PerformerBehaviorKindRegistry? _behaviorKinds;

        public PresenterDefinitionConfigLoader(
            ConfigPipeline configs,
            PresenterDefinitionRegistry registry,
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
            Func<string, int> resolveEntityCollectionKeyId = null,
            PerformerCommandKindRegistry? commandKinds = null,
            PerformerBehaviorKindRegistry? behaviorKinds = null)
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
            _commandKinds = commandKinds;
            _behaviorKinds = behaviorKinds;
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, "Presentation/presenters.json", ConfigMergePolicy.ArrayById, "id");
            var fragments = _configs.CollectFragmentsWithSources(in entry);
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
            var parsedByKey = new Dictionary<string, PresenterDefinition>(StringComparer.Ordinal);
            var parsedOrder = new List<string>(merged.Count);
            for (int i = 0; i < merged.Count; i++)
            {
                if (merged[i].Node is not JsonObject obj)
                {
                    throw new InvalidOperationException(
                        $"Presentation/presenters.json entry '{merged[i].Id}' must merge to a JSON object.");
                }

                string key = RequireCanonicalString(
                    obj["id"]?.GetValue<string>() ?? string.Empty,
                    "Presentation/presenters.json entry id");

                mergedByKey[key] = obj;
                _registry.GetOrRegisterId(key);
            }

            foreach ((string key, JsonObject _) in mergedByKey)
            {
                JsonObject expanded = ExpandDefinition(key, mergedByKey, new HashSet<string>(StringComparer.Ordinal));
                var (_, def) = ParseDefinition(expanded);
                if (def == null)
                {
                    throw new InvalidOperationException($"Presenter '{key}' failed to parse.");
                }

                parsedByKey[key] = def;
                parsedOrder.Add(key);
            }

            var validByKey = new Dictionary<string, PresenterDefinition>(parsedByKey, StringComparer.Ordinal);
            for (int i = 0; i < parsedOrder.Count; i++)
            {
                string key = parsedOrder[i];
                PresenterDefinition definition = validByKey[key];
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
                throw new InvalidOperationException($"Presenter '{key}' is missing from merged config.");
            }

            if (!expansionStack.Add(key))
            {
                throw new InvalidOperationException($"Presenter definition inheritance cycle detected at '{key}'.");
            }

            try
            {
                string parentKey = ParseOptionalCanonicalString(node["extends"], $"Presenter '{key}' extends");
                if (parentKey.Length == 0)
                {
                    return (JsonObject)node.DeepClone();
                }

                if (!mergedByKey.TryGetValue(parentKey, out _))
                {
                    throw new InvalidOperationException($"Presenter '{key}' extends unknown definition '{parentKey}'.");
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
                    merged[propertyName] = MergeBehaviors(parent[propertyName], childValue);
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

        private static JsonArray MergeBehaviors(JsonNode? existingNode, JsonNode? incomingNode)
        {
            var byKey = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            var order = new List<string>();
            AppendByValueKey(existingNode, "slot", "behaviors", byKey, order);
            if (incomingNode is not JsonArray incomingArray)
            {
                return BuildMergedArray(byKey, order);
            }

            for (int i = 0; i < incomingArray.Count; i++)
            {
                if (incomingArray[i] is not JsonObject incomingObj)
                {
                    continue;
                }

                string key = GetSemanticMergeKey(incomingObj["slot"], $"behaviors[{i}].slot");
                if (!byKey.TryGetValue(key, out JsonNode? existing) ||
                    existing is not JsonObject existingObj ||
                    ShouldReplaceBehavior(existingObj, incomingObj))
                {
                    if (!byKey.ContainsKey(key))
                    {
                        order.Add(key);
                    }

                    byKey[key] = incomingObj;
                    continue;
                }

                byKey[key] = MergeBehaviorObjects(existingObj, incomingObj);
            }

            return BuildMergedArray(byKey, order);
        }

        private static JsonArray BuildMergedArray(Dictionary<string, JsonNode> byKey, List<string> order)
        {
            var merged = new JsonArray();
            for (int i = 0; i < order.Count; i++)
            {
                merged.Add(byKey[order[i]].DeepClone());
            }

            return merged;
        }

        private static bool ShouldReplaceBehavior(JsonObject existingObj, JsonObject incomingObj)
        {
            if (incomingObj["kind"] == null)
            {
                return false;
            }

            string existingKind = existingObj["kind"]?.GetValue<string>() ?? string.Empty;
            string incomingKind = incomingObj["kind"]?.GetValue<string>() ?? string.Empty;
            return existingKind.Length > 0 &&
                   incomingKind.Length > 0 &&
                   !string.Equals(existingKind, incomingKind, StringComparison.Ordinal);
        }

        private static JsonObject MergeJsonObjects(JsonObject existingObj, JsonObject incomingObj)
        {
            var merged = (JsonObject)existingObj.DeepClone();
            foreach ((string propertyName, JsonNode? incomingValue) in incomingObj)
            {
                if (merged[propertyName] is JsonObject existingChild &&
                    incomingValue is JsonObject incomingChild)
                {
                    merged[propertyName] = MergeJsonObjects(existingChild, incomingChild);
                    continue;
                }

                merged[propertyName] = incomingValue?.DeepClone();
            }

            return merged;
        }

        private static JsonObject MergeBehaviorObjects(JsonObject existingObj, JsonObject incomingObj)
        {
            bool incomingDeclaresKind = incomingObj["kind"] != null;
            var merged = (JsonObject)existingObj.DeepClone();
            foreach ((string propertyName, JsonNode? incomingValue) in incomingObj)
            {
                if (incomingDeclaresKind && IsBehaviorPayloadProperty(propertyName))
                {
                    merged[propertyName] = incomingValue?.DeepClone();
                    continue;
                }

                if (merged[propertyName] is JsonObject existingChild &&
                    incomingValue is JsonObject incomingChild)
                {
                    merged[propertyName] = MergeJsonObjects(existingChild, incomingChild);
                    continue;
                }

                merged[propertyName] = incomingValue?.DeepClone();
            }

            return merged;
        }

        private static bool IsBehaviorPayloadProperty(string propertyName)
        {
            return propertyName is "assetBinding" or
                "attributeBinding" or
                "tagBinding" or
                "animator" or
                "attachment" or
                "sound" or
                "material" or
                "spline" or
                "grounding" or
                "minimapMarker" or
                "worldText" or
                "surfaceSource" or
                "instancedBatch";
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

        private (string key, PresenterDefinition def) ParseDefinition(JsonNode node)
        {
            string key = RequireCanonicalString(node["id"]?.GetValue<string>() ?? string.Empty, "Presenter id");

            if (node is not JsonObject definitionObject)
            {
                throw new InvalidOperationException($"Presenter '{key}' definition must be an object.");
            }

            RejectRemovedFields(node, key);
            RejectUnknownFields(definitionObject, $"Presenter '{key}'", DefinitionFields);

            BehaviorSlot[] behaviors = ParseBehaviors(node["behaviors"], key);
            PresenterDefinitionAuthoringFacts behaviorFacts = BuildDefinitionAuthoringFacts(key, behaviors);
            ChildPresenterRef[] children = ParseChildren(node["children"]);
            ValidateChildrenCapacity(key, children);

            var def = new PresenterDefinition
            {
                Key = key,
                Extends = ParseOptionalCanonicalString(node["extends"], $"Presenter '{key}' extends"),
                DefaultColor = behaviorFacts.DefaultColor,
                DefaultLifetime = ParseLifecycle(node["lifecycle"], key),
                DefaultFontSize = behaviorFacts.DefaultFontSize,
                WorldTextMode = behaviorFacts.WorldTextMode,
                PositionOffset = ParseAnchorOffset(node["anchor"], key),
                PositionYDriftPerSecond = behaviorFacts.PositionYDriftPerSecond,
                AlphaFadeOverLifetime = behaviorFacts.AlphaFadeOverLifetime,
                VisibilityCondition = ParseDefinitionVisibility(node["visibility"], key),
                Rules = ParseRules(node["rules"], key),
                Bindings = ParseBindings(node["bindings"], key),
                Children = children,
                Behaviors = behaviors,
                InstancedBatches = behaviorFacts.InstancedBatches,
                ParamDefaults = ParseParamDefaults(node["paramDefaults"], key),
                Surface = behaviorFacts.Surface,
            };

            def.Id = _registry.GetId(key);

            StampRuleOwners(def.Id, def.Rules);
            return (key, def);
        }

        private static void ValidateChildrenCapacity(string key, ChildPresenterRef[] children)
        {
            if (children == null || children.Length <= PresenterChildren.MAX_CHILDREN)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Presenter '{key}' declares {children.Length} direct children; capacity={PresenterChildren.MAX_CHILDREN}.");
        }

        private static void RejectRemovedFields(JsonNode node, string key)
        {
            if (node["entityScope"] != null)
            {
                throw new InvalidOperationException(
                    $"Presenter '{key}' still uses removed field 'entityScope'. Migrate it to lifecycle rules.");
            }

            if (node["requiredTemplate"] != null)
            {
                throw new InvalidOperationException(
                    $"Presenter '{key}' still uses removed field 'requiredTemplate'. Migrate it to event.key + lifecycle rules.");
            }

            string[] removedVisualFields =
            {
                "visualKind",
                "meshOrShapeId",
                "defaultScale",
                "defaultTextId",
                "legacyWorldTextMode",
                "defaultColor",
                "defaultLifetime",
                "defaultFontSize",
                "worldTextMode",
                "positionOffset",
                "positionYDriftPerSecond",
                "alphaFadeOverLifetime",
                "instancedBatches",
                "surface",
                "requiredAttributeIds",
                "requiredAttributes",
            };

            for (int i = 0; i < removedVisualFields.Length; i++)
            {
                string field = removedVisualFields[i];
                if (node[field] != null)
                {
                    throw new InvalidOperationException(
                        $"Presenter '{key}' still uses removed field '{field}'. Migrate Presenter authoring to lifecycle, anchor, visibility, and behaviors[].");
                }
            }
        }

        private static readonly string[] DefinitionFields =
        {
            "id", "extends", "lifecycle", "anchor", "visibility",
            "rules", "bindings", "paramDefaults", "behaviors", "children",
            "_comment", "maxVisibilityDistanceCm",
        };

        private static readonly string[] BehaviorSlotFields =
        {
            "kind", "slot", "activeByDefault", "activationCondition", "execution",
            "style", "motion",
            "assetBinding", "attributeBinding", "tagBinding", "animator",
            "attachment", "sound", "material", "spline", "grounding",
            "minimapMarker", "worldText", "surfaceSource", "instancedBatch",
        };

        private static readonly string[] ChildFields =
        {
            "definitionId", "scopeTag", "overrides",
        };

        private static readonly string[] RuleFields =
        {
            "event", "condition", "command",
        };

        private static readonly string[] EventFilterFields =
        {
            "kind", "key", "keyId", "gained",
        };

        private static readonly string[] ConditionFields =
        {
            "inline", "graphProgramId",
        };

        private static readonly string[] LifecycleFields =
        {
            "durationSeconds", "persistence",
        };

        private static readonly string[] AnchorFields =
        {
            "offset",
        };

        private static readonly string[] BindingFields =
        {
            "paramKey", "source", "constantValue", "sourceId", "textToken", "attributeId",
        };

        private static readonly string[] ParamDefaultFields =
        {
            "paramKey", "lane", "floatValue", "intValue", "vectorValue",
        };

        private static readonly string[] AssetBindingFields =
        {
            "assetKind", "assetId", "materialId", "renderPath", "mobility",
            "localOffset", "localRotation", "localScale",
            "scaleParamKey", "colorParamKey", "materialParamKey",
            "assetIdParamKey", "assetSwapParamKey", "assetSwapTable",
            "visibilityParamKey", "surfaceLayerKey", "sortId",
            "materialCustomData", "maxLod",
            "grounding", "groundingOffset",
        };

        private static readonly string[] WorldTextFields =
        {
            "textToken", "mode", "valueParamKey", "secondaryValueParamKey", "fontSize",
        };

        private static readonly string[] StyleFields =
        {
            "color", "alphaPolicy",
        };

        private static readonly string[] MotionFields =
        {
            "yDriftPerSecond",
        };

        private static readonly string[] AttachmentFields =
        {
            "target", "boneId", "offset", "rotationOffset", "inheritScale",
        };

        private static readonly string[] AttributeBindingFields =
        {
            "attributeId", "attributeName", "targetParamKey", "mode", "thresholds",
        };

        private static readonly string[] TagBindingFields =
        {
            "tagId", "tag", "targetParamKey", "invertLogic",
        };

        private static readonly string[] AnimatorFields =
        {
            "animatorControllerId", "animationProfileId", "speedParamKey", "stateParamKey",
        };

        private static readonly string[] SoundFields =
        {
            "soundAssetId", "loop", "volume", "volumeParamKey",
        };

        private static readonly string[] MaterialFields =
        {
            "baseMaterialId", "materialSwapParamKey", "swapTable",
        };

        private static readonly string[] SplineFields =
        {
            "splineAssetId", "usage", "widthParamKey", "colorParamKey",
            "speedParamKey", "progressParamKey", "loop", "pingPong", "waypointEventId",
        };

        private static readonly string[] GroundingFields =
        {
            "mode", "offset", "updatePolicy",
        };

        private static readonly string[] MinimapMarkerFields =
        {
            "shape", "color", "sizePx", "colorParamKey", "sizeParamKey",
            "visibilityParamKey", "orientationMode", "orientationParamKey",
            "orientationOffsetRad", "orientationLengthPx",
        };

        private static readonly string[] SurfaceSourceFields =
        {
            "kind", "profileId", "geometrySource", "chunkBake", "materialSet",
            "lodProfileId", "grounding", "boundsPolicy",
        };

        private static readonly string[] SurfaceGeometrySourceFields =
        {
            "controlPointSource", "widthSource", "flowDirectionSource", "segmentationPolicy",
            "boundaryPointSource", "triangulationPolicy", "meshPayloadSource",
        };

        private static readonly string[] SurfaceChunkBakeFields =
        {
            "enabled", "ownership", "chunkInfluencePolicy", "rebakePolicy", "usageHint",
        };

        private static readonly string[] SurfaceMaterialSetFields =
        {
            "primaryMaterialId", "secondaryMaterialId", "allowInstanceOverride",
        };

        private static readonly string[] SurfaceGroundingPolicyFields =
        {
            "mode",
        };

        private static readonly string[] SurfaceValueSourceFields =
        {
            "kind", "id", "graphProgramId",
        };

        private static readonly string[] InstancedBatchFields =
        {
            "batchAssetId",
        };

        private static readonly string[] ThresholdFields =
        {
            "threshold", "outputParamKey", "outputValue",
        };

        private static readonly string[] MaterialCustomDataSlotFields =
        {
            "slot", "lane", "paramKey", "defaultFloatValue", "defaultIntValue", "defaultVectorValue",
        };

        private static readonly string[] MaterialSwapEntryFields =
        {
            "paramValue", "materialId",
        };

        private static readonly string[] AssetSwapEntryFields =
        {
            "paramValue", "assetId",
        };

        private static readonly string[] CommandFields =
        {
            "kind", "route", "definitionId", "scopeTag", "scopeSource", "ownerSource",
            "useEventPosition", "paramKey", "paramLane", "valueSource",
            "paramValue", "intValue", "vectorValue", "paramGraphProgramId",
            "vectorXSource", "vectorYSource", "vectorZSource", "vectorWSource",
            "targetBehaviorSlot", "timerName", "durationSeconds", "durationRangeSeconds",
        };

        private static void RejectUnknownFields(JsonObject obj, string context, string[] allowedFields)
        {
            foreach (var property in obj)
            {
                bool known = false;
                for (int i = 0; i < allowedFields.Length; i++)
                {
                    if (string.Equals(property.Key, allowedFields[i], StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    throw new InvalidOperationException(
                        $"{context} contains unknown field '{property.Key}'. Allowed fields: {string.Join(", ", allowedFields)}.");
                }
            }
        }
        private readonly struct PresenterDefinitionAuthoringFacts
        {
            public PresenterDefinitionAuthoringFacts(
                Vector4 defaultColor,
                int defaultFontSize,
                WorldHudValueMode worldTextMode,
                float positionYDriftPerSecond,
                bool alphaFadeOverLifetime,
                SurfaceAuthoringBlock? surface,
                InstancedBatchBinding[] instancedBatches)
            {
                DefaultColor = defaultColor;
                DefaultFontSize = defaultFontSize;
                WorldTextMode = worldTextMode;
                PositionYDriftPerSecond = positionYDriftPerSecond;
                AlphaFadeOverLifetime = alphaFadeOverLifetime;
                Surface = surface;
                InstancedBatches = instancedBatches ?? Array.Empty<InstancedBatchBinding>();
            }

            public readonly Vector4 DefaultColor;
            public readonly int DefaultFontSize;
            public readonly WorldHudValueMode WorldTextMode;
            public readonly float PositionYDriftPerSecond;
            public readonly bool AlphaFadeOverLifetime;
            public readonly SurfaceAuthoringBlock? Surface;
            public readonly InstancedBatchBinding[] InstancedBatches;
        }

        private static PresenterDefinitionAuthoringFacts BuildDefinitionAuthoringFacts(string key, BehaviorSlot[] behaviors)
        {
            Vector4 defaultColor = new(1f, 1f, 1f, 1f);
            int defaultFontSize = 16;
            WorldHudValueMode worldTextMode = WorldHudValueMode.None;
            float positionYDriftPerSecond = 0f;
            bool alphaFadeOverLifetime = false;
            bool hasWorldTextDefinitionFacts = false;
            bool hasOutputStyleFacts = false;
            SurfaceAuthoringBlock? surface = null;
            List<InstancedBatchBinding>? instancedBatches = null;

            if (behaviors != null)
            {
                for (int i = 0; i < behaviors.Length; i++)
                {
                    ref readonly BehaviorSlot slot = ref behaviors[i];
                    switch (slot.Kind)
                    {
                        case BehaviorKind.AssetBinding:
                            ApplyOutputBehaviorFacts(
                                key,
                                in slot,
                                ref defaultColor,
                                ref positionYDriftPerSecond,
                                ref alphaFadeOverLifetime,
                                ref hasOutputStyleFacts);
                            break;

                        case BehaviorKind.WorldText:
                            if (hasWorldTextDefinitionFacts)
                            {
                                throw new InvalidOperationException(
                                    $"Presenter '{key}' declares multiple WorldText behaviors. Runtime WorldText style and value mode are definition-scoped; split them into child presenters.");
                            }

                            hasWorldTextDefinitionFacts = true;
                            defaultFontSize = slot.WorldText.FontSize > 0 ? slot.WorldText.FontSize : 16;
                            worldTextMode = slot.WorldText.Mode;
                            ApplyOutputBehaviorFacts(
                                key,
                                in slot,
                                ref defaultColor,
                                ref positionYDriftPerSecond,
                                ref alphaFadeOverLifetime,
                                ref hasOutputStyleFacts);
                            break;

                        case BehaviorKind.SurfaceSource:
                            if (surface != null)
                            {
                                throw new InvalidOperationException(
                                    $"Presenter '{key}' declares multiple SurfaceSource behaviors. A presenter instance may own only one surface source.");
                            }

                            surface = slot.SurfaceSource ?? throw new InvalidOperationException(
                                $"Presenter '{key}' SurfaceSource behavior is missing parsed authoring payload.");
                            break;

                        case BehaviorKind.InstancedBatch:
                            instancedBatches ??= new List<InstancedBatchBinding>(2);
                            instancedBatches.Add(new InstancedBatchBinding(slot.InstancedBatch.BatchAssetId, slot.SlotIndex));
                            break;
                    }
                }
            }

            return new PresenterDefinitionAuthoringFacts(
                defaultColor,
                defaultFontSize,
                worldTextMode,
                positionYDriftPerSecond,
                alphaFadeOverLifetime,
                surface,
                instancedBatches?.ToArray() ?? Array.Empty<InstancedBatchBinding>());
        }

        private static void ApplyOutputBehaviorFacts(
            string key,
            in BehaviorSlot slot,
            ref Vector4 defaultColor,
            ref float positionYDriftPerSecond,
            ref bool alphaFadeOverLifetime,
            ref bool hasOutputStyleFacts)
        {
            bool hasFacts = slot.Style.HasColor ||
                            slot.Style.AlphaPolicy != BehaviorAlphaPolicy.None ||
                            slot.Motion.YDriftPerSecond != 0f;
            if (!hasFacts)
            {
                return;
            }

            if (hasOutputStyleFacts)
            {
                throw new InvalidOperationException(
                    $"Presenter '{key}' declares style or motion on multiple output behaviors. Current runtime stores these facts at definition scope; split the outputs into child presenters.");
            }

            hasOutputStyleFacts = true;
            if (slot.Style.HasColor)
            {
                defaultColor = slot.Style.Color;
            }

            if (slot.Style.AlphaPolicy == BehaviorAlphaPolicy.FadeOverLifetime)
            {
                alphaFadeOverLifetime = true;
            }

            positionYDriftPerSecond = slot.Motion.YDriftPerSecond;
        }

        private static float ParseLifecycle(JsonNode? node, string key)
        {
            if (node == null)
            {
                return 0f;
            }

            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"Presenter '{key}' lifecycle must be an object.");
            }

            RejectUnknownFields(obj, $"Presenter '{key}' lifecycle", LifecycleFields);

            bool hasDuration = obj["durationSeconds"] != null;
            bool hasPersistence = obj["persistence"] != null;
            if (hasDuration && hasPersistence)
            {
                throw new InvalidOperationException(
                    $"Presenter '{key}' lifecycle must declare only one of durationSeconds or persistence.");
            }

            if (hasDuration)
            {
                float duration = ParseRequiredFiniteFloat(obj["durationSeconds"], $"Presenter '{key}' lifecycle.durationSeconds");
                if (duration <= 0f)
                {
                    throw new InvalidOperationException($"Presenter '{key}' lifecycle.durationSeconds must be > 0.");
                }

                return duration;
            }

            if (hasPersistence)
            {
                string persistence = ParseRequiredSemanticString(obj["persistence"], $"Presenter '{key}' lifecycle.persistence");
                if (!string.Equals(persistence, "Scoped", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Presenter '{key}' lifecycle.persistence must be 'Scoped'.");
                }

                return 0f;
            }

            throw new InvalidOperationException(
                $"Presenter '{key}' lifecycle must declare durationSeconds or persistence.");
        }

        private static Vector3 ParseAnchorOffset(JsonNode? node, string key)
        {
            if (node == null)
            {
                return Vector3.Zero;
            }

            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"Presenter '{key}' anchor must be an object.");
            }

            RejectUnknownFields(obj, $"Presenter '{key}' anchor", AnchorFields);

            if (obj["offset"] == null)
            {
                throw new InvalidOperationException($"Presenter '{key}' anchor requires field 'offset'.");
            }

            return ParseVector3(obj["offset"]);
        }

        private PresenterRule[] ParseRules(JsonNode? node, string owner)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                return Array.Empty<PresenterRule>();
            }

            var rules = new PresenterRule[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject ruleObj)
                {
                throw new InvalidOperationException($"Presenter '{owner}' rules[{i}] must be an object.");
                }

                string ruleContext = $"Presenter '{owner}' rules[{i}]";
                RejectUnknownFields(ruleObj, ruleContext, RuleFields);

                rules[i] = new PresenterRule
                {
                    Event = ParseEventFilter(ruleObj["event"], $"{ruleContext}.event"),
                    Condition = ParseConditionRef(ruleObj["condition"], $"{ruleContext}.condition", allowGraphProgramId: true),
                    Command = ParsePresenterCommand(ruleObj["command"], $"{ruleContext}.command"),
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

            RejectUnknownFields(obj, context, EventFilterFields);
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
                PresentationEventKind.TimerExpired => PresenterTimerNameRegistry.Register(key),
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

        private PresenterCommand ParsePresenterCommand(JsonNode? node, string context)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"{context} requires an object with explicit field 'kind'.");
            }

            RejectUnknownFields(obj, context, CommandFields);
            string kindText = ParseRequiredSemanticString(obj["kind"], $"{context}.kind");
            if (!TryParseDefinedEnum(kindText, out PresenterCommandKind commandKind))
            {
                if (_commandKinds == null)
                {
                    throw new InvalidOperationException($"{context}.kind has invalid value '{kindText}'.");
                }

                return ParseExtensionPresenterCommand(obj, kindText, context);
            }

            if (commandKind == PresenterCommandKind.None)
            {
                throw new InvalidOperationException($"{context}.kind must not be 'None'.");
            }

            if (commandKind == PresenterCommandKind.Extension)
            {
                throw new InvalidOperationException($"{context}.kind must be a concrete built-in command or mod-qualified extension key.");
            }

            ParamLane paramLane = ParseCommandParamLane(obj, commandKind, context);
            PresenterCommandValueSource valueSource = ParseCommandValueSource(obj, commandKind, context);
            int paramGraphProgramId = ParseCommandParamGraphProgramId(obj, commandKind, valueSource, context);
            int presenterDefinitionId = ParseCommandDefinitionId(obj, commandKind, context);
            bool hasVectorSources = HasCommandVectorSources(obj);
            bool hasParamPayload = HasCommandParamPayload(obj, commandKind);
            PerformerCommandRouteStrategy routeStrategy = ResolveBuiltinCommandRoute(commandKind, presenterDefinitionId);
            return new PresenterCommand
            {
                CommandKind = commandKind,
                CommandKindId = (byte)commandKind,
                RouteStrategy = routeStrategy,
                PresenterDefinitionId = presenterDefinitionId,
                ParentEntity = Entity.Null, // resolved at runtime
                ScopeTag = ParseScopeTag(obj["scopeTag"]),
                ScopeSource = ParseCommandScopeSource(obj, commandKind, context),
                OwnerSource = ParseCommandOwnerSource(obj, commandKind, context),
                UseEventPosition = ParseCommandUseEventPosition(obj, commandKind, context),
                HasParamPayload = hasParamPayload,
                ParamKey = commandKind == PresenterCommandKind.SetParam || hasParamPayload
                    ? ParseRequiredParamKey(obj["paramKey"], "Presenter command paramKey")
                    : ParseOptionalCommandParamKey(obj["paramKey"], "Presenter command paramKey"),
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
                TargetBehaviorSlot = commandKind is PresenterCommandKind.ActivateBehavior or PresenterCommandKind.DeactivateBehavior
                    ? ParseRequiredBehaviorSlot(obj["targetBehaviorSlot"], "Presenter command targetBehaviorSlot")
                    : ParseOptionalBehaviorSlot(obj["targetBehaviorSlot"], "Presenter command targetBehaviorSlot"),
                TimerNameId = ParseCommandTimerNameId(obj, commandKind, context),
                TimerDurationSeconds = ParseCommandTimerDurationSeconds(obj, commandKind, context),
                TimerDurationRangeSeconds = ParseCommandTimerDurationRangeSeconds(obj, commandKind, context),
            };
        }

        private static int ParseCommandTimerNameId(JsonObject obj, PresenterCommandKind commandKind, string context)
        {
            JsonNode? node = obj["timerName"];
            if (commandKind is not (PresenterCommandKind.TimerSet or PresenterCommandKind.TimerKill))
            {
                if (node != null)
                {
                    throw new InvalidOperationException($"{context}.timerName is only valid for TimerSet and TimerKill commands.");
                }

                return 0;
            }

            string name = ParseRequiredSemanticString(node, $"{context}.timerName");
            if (string.Equals(name, "*", StringComparison.Ordinal))
            {
                if (commandKind == PresenterCommandKind.TimerKill)
                {
                    return PresenterTimerNameRegistry.AllTimersId;
                }

                // "*" 是 TimerKill 的保留通配名；TimerSet 注册它会得到一个无法被精确 TimerExpired 匹配的名字
                throw new InvalidOperationException($"{context}.timerName \"*\" is reserved as the TimerKill wildcard and cannot be registered by TimerSet.");
            }

            return PresenterTimerNameRegistry.Register(name);
        }

        private static float ParseCommandTimerDurationSeconds(JsonObject obj, PresenterCommandKind commandKind, string context)
        {
            JsonNode? node = obj["durationSeconds"];
            if (commandKind != PresenterCommandKind.TimerSet)
            {
                if (node != null)
                {
                    throw new InvalidOperationException($"{context}.durationSeconds is only valid for TimerSet commands.");
                }

                return 0f;
            }

            float duration = ParseRequiredFiniteFloat(node, $"{context}.durationSeconds");
            if (duration <= 0f)
            {
                throw new InvalidOperationException($"{context}.durationSeconds must be > 0.");
            }

            return duration;
        }

        private static float ParseCommandTimerDurationRangeSeconds(JsonObject obj, PresenterCommandKind commandKind, string context)
        {
            JsonNode? node = obj["durationRangeSeconds"];
            if (commandKind != PresenterCommandKind.TimerSet)
            {
                if (node != null)
                {
                    throw new InvalidOperationException($"{context}.durationRangeSeconds is only valid for TimerSet commands.");
                }

                return 0f;
            }

            if (node == null)
            {
                return 0f;
            }

            float range = ParseRequiredFiniteFloat(node, $"{context}.durationRangeSeconds");
            if (range < 0f)
            {
                throw new InvalidOperationException($"{context}.durationRangeSeconds must be >= 0.");
            }

            return range;
        }

        private PresenterCommand ParseExtensionPresenterCommand(JsonObject obj, string kindText, string context)
        {
            int commandKindId = _commandKinds?.GetId(kindText) ?? 0;
            if (commandKindId < PerformerCommandKindRegistry.FirstModCommandKindId)
            {
                throw new InvalidOperationException($"{context}.kind references unregistered presenter command kind '{kindText}'.");
            }

            if (!_commandKinds!.TryGetDescriptor(commandKindId, out PerformerCommandExtensionDescriptor descriptor))
            {
                throw new InvalidOperationException($"{context}.kind references presenter command kind '{kindText}' without a registered descriptor.");
            }

            PerformerCommandRouteStrategy routeStrategy =
                ParseRequiredEnum<PerformerCommandRouteStrategy>(obj["route"], $"{context}.route");
            if (routeStrategy != descriptor.RouteStrategy)
            {
                throw new InvalidOperationException(
                    $"{context}.route '{routeStrategy}' does not match registered route '{descriptor.RouteStrategy}' for '{kindText}'.");
            }

            bool hasParamPayload = HasAnyCommandParamPayload(obj);
            ParamLane paramLane = hasParamPayload
                ? ParseRequiredEnum<ParamLane>(obj["paramLane"], $"{context}.paramLane")
                : ParamLane.Float;
            PresenterCommandValueSource valueSource = hasParamPayload
                ? ParseRequiredEnum<PresenterCommandValueSource>(obj["valueSource"], $"{context}.valueSource")
                : PresenterCommandValueSource.Fixed;
            int paramGraphProgramId = ParseExtensionCommandParamGraphProgramId(obj, hasParamPayload, valueSource, context);
            bool hasVectorSources = HasCommandVectorSources(obj);

            return new PresenterCommand
            {
                CommandKind = PresenterCommandKind.Extension,
                CommandKindId = commandKindId,
                RouteStrategy = routeStrategy,
                PresenterDefinitionId = obj["definitionId"] != null
                    ? ResolveRequiredPresenterDefinitionId(obj["definitionId"], $"{context}.definitionId")
                    : 0,
                ParentEntity = Entity.Null,
                ScopeTag = ParseScopeTag(obj["scopeTag"]),
                ScopeSource = obj["scopeSource"] != null
                    ? ParseRequiredEnum<PresenterCommandScopeSource>(obj["scopeSource"], $"{context}.scopeSource")
                    : PresenterCommandScopeSource.Fixed,
                OwnerSource = obj["ownerSource"] != null
                    ? ParseRequiredEnum<PresenterCommandEntitySource>(obj["ownerSource"], $"{context}.ownerSource")
                    : PresenterCommandEntitySource.EventSource,
                UseEventPosition = obj["useEventPosition"] != null &&
                    ParseRequiredBool(obj["useEventPosition"], $"{context}.useEventPosition"),
                HasParamPayload = hasParamPayload,
                ParamKey = hasParamPayload
                    ? ParseRequiredParamKey(obj["paramKey"], "Presenter extension command paramKey")
                    : ParseOptionalCommandParamKey(obj["paramKey"], "Presenter extension command paramKey"),
                ParamLane = paramLane,
                ParamValue = hasParamPayload
                    ? ParseCommandParamValue(obj, PresenterCommandKind.SetParam, paramLane, valueSource, paramGraphProgramId, context)
                    : 0f,
                IntValue = hasParamPayload
                    ? ParseCommandIntValue(obj, PresenterCommandKind.SetParam, paramLane, valueSource, paramGraphProgramId, context)
                    : 0,
                VectorValue = hasParamPayload
                    ? ParseCommandVectorValue(obj, PresenterCommandKind.SetParam, paramLane, valueSource, hasVectorSources, paramGraphProgramId, context)
                    : Vector4.Zero,
                ValueSource = valueSource,
                VectorXSource = hasParamPayload
                    ? ParseCommandVectorSource(obj["vectorXSource"], PresenterCommandKind.SetParam, paramLane, hasVectorSources, $"{context}.vectorXSource")
                    : PresenterCommandValueSource.Fixed,
                VectorYSource = hasParamPayload
                    ? ParseCommandVectorSource(obj["vectorYSource"], PresenterCommandKind.SetParam, paramLane, hasVectorSources, $"{context}.vectorYSource")
                    : PresenterCommandValueSource.Fixed,
                VectorZSource = hasParamPayload
                    ? ParseCommandVectorSource(obj["vectorZSource"], PresenterCommandKind.SetParam, paramLane, hasVectorSources, $"{context}.vectorZSource")
                    : PresenterCommandValueSource.Fixed,
                VectorWSource = hasParamPayload
                    ? ParseCommandVectorSource(obj["vectorWSource"], PresenterCommandKind.SetParam, paramLane, hasVectorSources, $"{context}.vectorWSource")
                    : PresenterCommandValueSource.Fixed,
                ParamGraphProgramId = paramGraphProgramId,
                TargetBehaviorSlot = ParseOptionalBehaviorSlot(obj["targetBehaviorSlot"], "Presenter extension command targetBehaviorSlot"),
            };
        }

        private static PerformerCommandRouteStrategy ResolveBuiltinCommandRoute(
            PresenterCommandKind commandKind,
            int presenterDefinitionId)
        {
            return commandKind switch
            {
                PresenterCommandKind.CreatePresenter => PerformerCommandRouteStrategy.CreatePerformer,
                PresenterCommandKind.DestroyPresenterScope => PerformerCommandRouteStrategy.DestroyScope,
                PresenterCommandKind.DestroyScopedPresenter => PerformerCommandRouteStrategy.ScopedInstance,
                PresenterCommandKind.SetParam when presenterDefinitionId > 0 => PerformerCommandRouteStrategy.ScopedInstance,
                PresenterCommandKind.SetParam => PerformerCommandRouteStrategy.ExistingInstances,
                PresenterCommandKind.ActivateBehavior => PerformerCommandRouteStrategy.ExistingInstances,
                PresenterCommandKind.DeactivateBehavior => PerformerCommandRouteStrategy.ExistingInstances,
                PresenterCommandKind.InitializeTransform => PerformerCommandRouteStrategy.ExistingInstances,
                PresenterCommandKind.DestroyPresenter => PerformerCommandRouteStrategy.ExistingInstances,
                PresenterCommandKind.TimerSet => PerformerCommandRouteStrategy.ExistingInstances,
                PresenterCommandKind.TimerKill => PerformerCommandRouteStrategy.ExistingInstances,
                PresenterCommandKind.SinkParamToAsset => PerformerCommandRouteStrategy.SingleRuntime,
                _ => throw new InvalidOperationException($"Unsupported presenter command kind '{commandKind}'."),
            };
        }

        private static bool HasAnyCommandParamPayload(JsonObject obj)
        {
            return obj["paramKey"] != null ||
                   obj["paramLane"] != null ||
                   obj["valueSource"] != null ||
                   obj["paramValue"] != null ||
                   obj["intValue"] != null ||
                   obj["vectorValue"] != null ||
                   obj["paramGraphProgramId"] != null ||
                   obj["vectorXSource"] != null ||
                   obj["vectorYSource"] != null ||
                   obj["vectorZSource"] != null ||
                   obj["vectorWSource"] != null;
        }

        private static int ParseExtensionCommandParamGraphProgramId(
            JsonObject obj,
            bool hasParamPayload,
            PresenterCommandValueSource valueSource,
            string context)
        {
            JsonNode? node = obj["paramGraphProgramId"];
            if (node == null)
            {
                return 0;
            }

            if (!hasParamPayload)
            {
                throw new InvalidOperationException($"{context}.paramGraphProgramId requires param payload fields.");
            }

            if (valueSource != PresenterCommandValueSource.Fixed)
            {
                throw new InvalidOperationException($"{context}.paramGraphProgramId requires valueSource '{PresenterCommandValueSource.Fixed}'.");
            }

            int graphProgramId = ParseRequiredInt(node, $"{context}.paramGraphProgramId");
            if (graphProgramId <= 0)
            {
                throw new InvalidOperationException($"{context}.paramGraphProgramId must be positive.");
            }

            return graphProgramId;
        }

        private static bool HasCommandParamPayload(JsonObject obj, PresenterCommandKind commandKind)
        {
            if (commandKind == PresenterCommandKind.SetParam)
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

            if (commandKind != PresenterCommandKind.CreatePresenter)
            {
                throw new InvalidOperationException($"{nameof(PresenterCommand)} param payload fields are only valid for CreatePresenter and SetParam commands.");
            }

            return true;
        }

        private static PresenterCommandEntitySource ParseCommandOwnerSource(
            JsonObject obj,
            PresenterCommandKind commandKind,
            string context)
        {
            JsonNode? node = obj["ownerSource"];
            if (node == null)
            {
                return PresenterCommandEntitySource.EventSource;
            }

            if (commandKind is not (
                    PresenterCommandKind.CreatePresenter or
                    PresenterCommandKind.SetParam or
                    PresenterCommandKind.DestroyScopedPresenter or
                    PresenterCommandKind.DestroyPresenterScope))
            {
                throw new InvalidOperationException(
                    $"{context}.ownerSource is only valid for scoped presenter commands.");
            }

            return ParseRequiredEnum<PresenterCommandEntitySource>(node, $"{context}.ownerSource");
        }

        private static PresenterCommandScopeSource ParseCommandScopeSource(
            JsonObject obj,
            PresenterCommandKind commandKind,
            string context)
        {
            JsonNode? node = obj["scopeSource"];
            if (commandKind == PresenterCommandKind.SetParam &&
                obj["definitionId"] != null &&
                node == null)
            {
                throw new InvalidOperationException($"{context}.scopeSource is required for scoped SetParam commands with definitionId.");
            }

            if (CommandRequiresScopeSource(commandKind) || node != null)
            {
                return ParseRequiredEnum<PresenterCommandScopeSource>(node, $"{context}.scopeSource");
            }

            return PresenterCommandScopeSource.Fixed;
        }

        private static bool ParseCommandUseEventPosition(
            JsonObject obj,
            PresenterCommandKind commandKind,
            string context)
        {
            JsonNode? node = obj["useEventPosition"];
            if (node == null)
            {
                return false;
            }

            if (commandKind is not (PresenterCommandKind.CreatePresenter or PresenterCommandKind.SetParam or PresenterCommandKind.DestroyScopedPresenter))
            {
                throw new InvalidOperationException($"{context}.useEventPosition is only valid for CreatePresenter, SetParam, and DestroyScopedPresenter commands.");
            }

            return ParseRequiredBool(node, $"{context}.useEventPosition");
        }

        private static ParamLane ParseCommandParamLane(JsonObject obj, PresenterCommandKind commandKind, string context)
        {
            JsonNode? node = obj["paramLane"];
            if (commandKind == PresenterCommandKind.SetParam || HasCommandParamPayload(obj, commandKind))
            {
                return ParseRequiredEnum<ParamLane>(node, $"{context}.paramLane");
            }

            return ParamLane.Float;
        }

        private static PresenterCommandValueSource ParseCommandValueSource(
            JsonObject obj,
            PresenterCommandKind commandKind,
            string context)
        {
            JsonNode? node = obj["valueSource"];
            if (commandKind == PresenterCommandKind.SetParam || HasCommandParamPayload(obj, commandKind))
            {
                return ParseRequiredEnum<PresenterCommandValueSource>(node, $"{context}.valueSource");
            }

            return PresenterCommandValueSource.Fixed;
        }

        private static int ParseCommandParamGraphProgramId(
            JsonObject obj,
            PresenterCommandKind commandKind,
            PresenterCommandValueSource valueSource,
            string context)
        {
            JsonNode? node = obj["paramGraphProgramId"];
            if (node == null)
            {
                return 0;
            }

            if (commandKind is not (PresenterCommandKind.SetParam or PresenterCommandKind.CreatePresenter))
            {
                throw new InvalidOperationException($"{context}.paramGraphProgramId is only valid for CreatePresenter and SetParam commands.");
            }

            if (valueSource != PresenterCommandValueSource.Fixed)
            {
                throw new InvalidOperationException($"{context}.paramGraphProgramId requires valueSource '{PresenterCommandValueSource.Fixed}'.");
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
            PresenterCommandKind commandKind,
            ParamLane lane,
            PresenterCommandValueSource valueSource,
            int paramGraphProgramId,
            string context)
        {
            JsonNode? node = obj["paramValue"];
            if (node == null)
            {
                if ((commandKind == PresenterCommandKind.SetParam || HasCommandParamPayload(obj, commandKind)) &&
                    valueSource == PresenterCommandValueSource.Fixed &&
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
            PresenterCommandKind commandKind,
            ParamLane lane,
            PresenterCommandValueSource valueSource,
            int paramGraphProgramId,
            string context)
        {
            JsonNode? node = obj["intValue"];
            if (node == null)
            {
                if ((commandKind == PresenterCommandKind.SetParam || HasCommandParamPayload(obj, commandKind)) &&
                    valueSource == PresenterCommandValueSource.Fixed &&
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
            PresenterCommandKind commandKind,
            ParamLane lane,
            PresenterCommandValueSource valueSource,
            bool hasVectorSources,
            int paramGraphProgramId,
            string context)
        {
            JsonNode? node = obj["vectorValue"];
            if (node == null)
            {
                if ((commandKind == PresenterCommandKind.SetParam || HasCommandParamPayload(obj, commandKind)) &&
                    lane == ParamLane.Vector &&
                    paramGraphProgramId == 0 &&
                    !hasVectorSources)
                {
                    if (valueSource != PresenterCommandValueSource.Fixed)
                    {
                        throw new InvalidOperationException($"{context}.vectorValue requires valueSource '{PresenterCommandValueSource.Fixed}' for Vector SetParam.");
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

        private static PresenterCommandValueSource ParseCommandVectorSource(
            JsonNode? node,
            PresenterCommandKind commandKind,
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

                return PresenterCommandValueSource.Fixed;
            }

            if (commandKind is not (PresenterCommandKind.SetParam or PresenterCommandKind.CreatePresenter) || lane != ParamLane.Vector)
            {
                throw new InvalidOperationException($"{context} is only valid for Vector CreatePresenter and SetParam commands.");
            }

            return ParseRequiredEnum<PresenterCommandValueSource>(node, context);
        }

        private static bool CommandRequiresScopeSource(PresenterCommandKind commandKind)
        {
            return commandKind is PresenterCommandKind.CreatePresenter
                or PresenterCommandKind.DestroyPresenterScope
                or PresenterCommandKind.DestroyScopedPresenter;
        }

        private int ParseCommandDefinitionId(JsonObject obj, PresenterCommandKind commandKind, string context)
        {
            JsonNode? node = obj["definitionId"];
            if (CommandRequiresDefinitionId(commandKind))
            {
                return ResolveRequiredPresenterDefinitionId(node, $"{context}.definitionId");
            }

            if (node != null)
            {
                if (commandKind == PresenterCommandKind.SetParam)
                {
                    return ResolveRequiredPresenterDefinitionId(node, $"{context}.definitionId");
                }

                throw new InvalidOperationException(
                    $"{context}.definitionId is only valid for CreatePresenter, SetParam, and DestroyScopedPresenter commands.");
            }

            return 0;
        }

        private static bool CommandRequiresDefinitionId(PresenterCommandKind commandKind)
        {
            return commandKind is PresenterCommandKind.CreatePresenter
                or PresenterCommandKind.DestroyScopedPresenter;
        }

        private int ResolveRequiredPresenterDefinitionId(JsonNode? node, string context)
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
                        $"Presenter command definitionId must be a semantic string, not numeric id {numericId}.");
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    key = RequireCanonicalString(key, context);
                    int id = _registry.GetId(key);
                    if (id <= 0)
                    {
                        throw new InvalidOperationException($"Presenter command references unknown definition '{key}'.");
                    }

                    return id;
                }
            }

            throw new InvalidOperationException($"{context} must be a non-empty semantic string.");
        }

        private PresenterParamBinding[] ParseBindings(JsonNode? node, string owner)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                return Array.Empty<PresenterParamBinding>();
            }

            var bindings = new PresenterParamBinding[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject bindingObj)
                {
                    throw new InvalidOperationException($"Presenter '{owner}' bindings[{i}] must be an object.");
                }

                string bindingContext = $"Presenter '{owner}' bindings[{i}]";
                RejectRemovedBindingFields(bindingObj, bindingContext);
                RejectUnknownFields(bindingObj, bindingContext, BindingFields);

                bindings[i] = new PresenterParamBinding
                {
                    ParamKey = ParseRequiredParamKey(bindingObj["paramKey"], $"{bindingContext}.paramKey"),
                    Value = ParseValueRef(bindingObj, bindingContext),
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
                "attribute" or "attributeRatio" or "attributeBase" => throw new InvalidOperationException(
                    $"{context} source '{source}' duplicates AttributeBinding behavior. Use an AttributeBinding behavior with attributeBinding.targetParamKey instead."),
                "graph" => ValueRef.FromGraph(ParseRequiredInt(node["sourceId"], "Presenter binding graph.sourceId")),
                "entityColor" => ValueRef.FromEntityColor(ParseRequiredInt(node["sourceId"], "Presenter binding entityColor.sourceId")),
                "entityColorVector" => ValueRef.FromEntityColorVector(),
                "facingRadians" => ValueRef.FromFacingRadians(),
                "facingDegrees" => ValueRef.FromFacingDegrees(),
                "textToken" => ValueRef.FromConstant(ResolveTextTokenId(node)),
                "constant" => ValueRef.FromConstant(ParseRequiredFloat(node["constantValue"], "Presenter binding constant.constantValue")),
                null or "" => throw new InvalidOperationException("Presenter binding must declare explicit source."),
                _ => throw new InvalidOperationException($"Presenter binding source has invalid value '{source}'."),
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

        private int ResolveTextTokenId(JsonNode node)
        {
            return ResolveTextTokenId(node["textToken"], "Presenter WorldText textToken binding textToken");
        }

        private int ResolveTextTokenId(JsonNode? node, string context)
        {
            string tokenKey = ParseRequiredSemanticString(node, context);
            int tokenId = _resolveTextTokenId(tokenKey);
            if (tokenId <= 0)
            {
                throw new InvalidOperationException($"Presenter WorldText references unknown text token '{tokenKey}'.");
            }

            return tokenId;
        }

        private int ResolveAttributeId(JsonNode node)
        {
            if (node["attributeName"] != null)
            {
                throw new InvalidOperationException(
                    "Presenter attribute binding uses removed field 'attributeName'; use canonical semantic field 'attributeId'.");
            }

            JsonNode? idNode = node["attributeId"];
            if (idNode is JsonValue value && value.TryGetValue<int>(out int numericId))
            {
                throw new InvalidOperationException(
                    $"Presenter attribute binding attributeId must be a semantic string, not numeric id {numericId}.");
            }

            string name = string.Empty;
            if (node["attributeId"] is JsonValue attributeIdValue &&
                attributeIdValue.TryGetValue<string>(out string? attributeIdText))
            {
                name = attributeIdText;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                name = RequireCanonicalString(name, "Presenter attribute binding attribute id");
                int id = _resolveAttributeName(name);
                if (id >= 0)
                {
                    return id;
                }
            }

            throw new InvalidOperationException("Presenter attribute binding requires non-empty semantic field 'attributeId'.");
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
                        $"Presenter scopeTag must be a semantic string, not numeric id {numericId}.");
                }

                if (value.TryGetValue<string>(out string text))
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        throw new InvalidOperationException("Presenter scopeTag must be omitted or a non-empty semantic string.");
                    }

                    text = RequireCanonicalString(text, "Presenter scopeTag");
                    if (int.TryParse(text, out int parsed))
                    {
                        throw new InvalidOperationException(
                            $"Presenter scopeTag must be a semantic string, not numeric string '{parsed}'.");
                    }

                    return PresenterScopeTagRegistry.Register(text);
                }
            }

            throw new InvalidOperationException("Presenter scopeTag must be a non-empty semantic string.");
        }

        private ChildPresenterRef[] ParseChildren(JsonNode? node)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                return Array.Empty<ChildPresenterRef>();
            }

            var children = new ChildPresenterRef[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Presenter child[{i}] must be an object.");
                }

                if (obj["paramOverrides"] != null)
                {
                    throw new InvalidOperationException(
                        $"children[{i}] uses removed field 'paramOverrides'. Author 'overrides.params'.");
                }

                if (obj["local_position"] != null || obj["local_rotation"] != null || obj["local_scale"] != null)
                {
                    throw new InvalidOperationException(
                        $"children[{i}] uses snake_case transform fields. Author 'overrides.transform.localPosition/localRotation/localScale'.");
                }

                RejectUnknownFields(obj, $"children[{i}]", ChildFields);

                children[i] = new ChildPresenterRef
                {
                    DefinitionId = ResolveRequiredPresenterDefinitionId(obj["definitionId"], $"children[{i}].definitionId"),
                    ScopeTag = ParseScopeTag(obj["scopeTag"]),
                    ParamOverrides = ParseChildParamOverrides(obj["overrides"], i),
                    TransformOverride = ParseChildTransformOverride(obj["overrides"], i),
                };
            }

            return children;
        }

        private ParamDefault[] ParseChildParamOverrides(JsonNode? overridesNode, int childIndex)
        {
            if (overridesNode == null)
            {
                return Array.Empty<ParamDefault>();
            }

            if (overridesNode is not JsonObject obj)
            {
                throw new InvalidOperationException($"children[{childIndex}].overrides must be an object.");
            }

            RejectUnknownOverrideKeys(obj, childIndex);
            return ParseParamDefaults(obj["params"], $"children[{childIndex}].overrides.params");
        }

        private static PresenterInstanceTransformOverride ParseChildTransformOverride(JsonNode? overridesNode, int childIndex)
        {
            if (overridesNode == null)
            {
                return PresenterInstanceTransformOverride.Identity;
            }

            if (overridesNode is not JsonObject obj)
            {
                throw new InvalidOperationException($"children[{childIndex}].overrides must be an object.");
            }

            RejectUnknownOverrideKeys(obj, childIndex);
            JsonNode? transformNode = obj["transform"];
            if (transformNode == null)
            {
                return PresenterInstanceTransformOverride.Identity;
            }

            if (transformNode is not JsonObject transform)
            {
                throw new InvalidOperationException($"children[{childIndex}].overrides.transform must be an object.");
            }

            if (transform["local_position"] != null || transform["local_rotation"] != null || transform["local_scale"] != null)
            {
                throw new InvalidOperationException(
                    $"children[{childIndex}].overrides.transform uses snake_case. Author localPosition/localRotation/localScale.");
            }

            foreach (var property in transform)
            {
                if (property.Key is not ("localPosition" or "localRotation" or "localScale"))
                {
                    throw new InvalidOperationException(
                        $"children[{childIndex}].overrides.transform field '{property.Key}' is unsupported. Expected localPosition, localRotation (XYZ degrees), localScale.");
                }
            }

            Vector3 localPosition = ParseRequiredVector3(
                transform["localPosition"],
                $"children[{childIndex}].overrides.transform.localPosition",
                Vector3.Zero,
                required: false);
            Vector3 eulerDegrees = ParseRequiredVector3(
                transform["localRotation"],
                $"children[{childIndex}].overrides.transform.localRotation",
                Vector3.Zero,
                required: false);
            Vector3 localScale = ParseRequiredVector3(
                transform["localScale"],
                $"children[{childIndex}].overrides.transform.localScale",
                Vector3.One,
                required: false);

            return new PresenterInstanceTransformOverride
            {
                LocalPosition = localPosition,
                LocalRotation = PresenterInstanceTransformOverride.RotationFromEulerDegreesXyz(eulerDegrees),
                LocalScale = localScale,
                HasOverride = true,
            };
        }

        private static void RejectUnknownOverrideKeys(JsonObject obj, int childIndex)
        {
            foreach (var property in obj)
            {
                if (property.Key is not ("transform" or "params"))
                {
                    throw new InvalidOperationException(
                        $"children[{childIndex}].overrides field '{property.Key}' is unsupported. Expected transform and/or params.");
                }
            }
        }

        private static Vector3 ParseRequiredVector3(JsonNode? node, string context, Vector3 defaultValue, bool required)
        {
            if (node == null)
            {
                if (required)
                {
                    throw new InvalidOperationException($"{context} requires a 3-number array.");
                }

                return defaultValue;
            }

            if (node is not JsonArray arr || arr.Count != 3)
            {
                throw new InvalidOperationException($"{context} must be a 3-number array.");
            }

            return new Vector3(
                arr[0]?.GetValue<float>() ?? throw new InvalidOperationException($"{context}[0] must be a number."),
                arr[1]?.GetValue<float>() ?? throw new InvalidOperationException($"{context}[1] must be a number."),
                arr[2]?.GetValue<float>() ?? throw new InvalidOperationException($"{context}[2] must be a number."));
        }

        private static void ValidateChildGraph(
            string key,
            IReadOnlyDictionary<string, PresenterDefinition> parsedByKey,
            HashSet<int> pathIds,
            List<string> path)
        {
            if (!parsedByKey.TryGetValue(key, out PresenterDefinition definition))
            {
                throw new InvalidOperationException($"Presenter '{key}' is missing from the parsed definition graph.");
            }

            path.Add(key);
            pathIds.Add(definition.Id);

            try
            {
                ChildPresenterRef[] children = definition.Children;
                if (children == null || children.Length == 0)
                {
                    return;
                }

                for (int i = 0; i < children.Length; i++)
                {
                    int childDefinitionId = children[i].DefinitionId;
                    if (childDefinitionId <= 0)
                    {
                        throw new InvalidOperationException($"Presenter '{key}' child[{i}] references an unknown definition.");
                    }

                    string childKey = definition.Key;
                    childKey = string.Empty;
                    foreach ((string parsedKey, PresenterDefinition parsedDefinition) in parsedByKey)
                    {
                        if (parsedDefinition.Id == childDefinitionId)
                        {
                            childKey = parsedKey;
                            break;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(childKey))
                    {
                        throw new InvalidOperationException($"Presenter '{key}' child[{i}] references definition id={childDefinitionId} that failed to load.");
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
            PresenterDefinition definition,
            IReadOnlyDictionary<string, PresenterDefinition> parsedByKey)
        {
            PresenterRule[] rules = definition.Rules;
            if (rules == null || rules.Length == 0)
            {
                return;
            }

            for (int i = 0; i < rules.Length; i++)
            {
                ref readonly PresenterRule rule = ref rules[i];
                if (!CommandRequiresDefinitionId(rule.Command.CommandKind) &&
                    rule.Command.PresenterDefinitionId <= 0)
                {
                    continue;
                }

                int referencedDefinitionId = rule.Command.PresenterDefinitionId;
                if (referencedDefinitionId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Presenter '{key}' rule[{i}] references an unknown presenter definition.");
                }

                if (!ContainsDefinitionId(parsedByKey, referencedDefinitionId))
                {
                    throw new InvalidOperationException(
                        $"Presenter '{key}' rule[{i}] references definition id={referencedDefinitionId} that failed to load.");
                }
            }
        }

        private static bool ContainsDefinitionId(
            IReadOnlyDictionary<string, PresenterDefinition> parsedByKey,
            int definitionId)
        {
            foreach ((string _, PresenterDefinition parsedDefinition) in parsedByKey)
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
                throw new InvalidOperationException($"Presenter '{ownerKey}' exceeds the max 32 behaviors per presenter limit.");
            }

            var slots = new BehaviorSlot[arr.Count];
            uint seenSlots = 0u;
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Presenter behavior[{i}] must be an object.");
                }

                RejectUnknownFields(obj, $"Presenter '{ownerKey}' behavior[{i}]", BehaviorSlotFields);
                string kindText = ParseRequiredSemanticString(obj["kind"], $"Presenter '{ownerKey}' behavior[{i}].kind");
                bool parsedBuiltinKind = TryParseDefinedEnum(kindText, out BehaviorKind kind);
                if (parsedBuiltinKind && kind is BehaviorKind.None or BehaviorKind.Extension)
                {
                    throw new InvalidOperationException($"Presenter '{ownerKey}' behavior[{i}].kind must be a concrete built-in behavior or mod-qualified extension key.");
                }

                bool isBuiltinKind = parsedBuiltinKind;
                int kindId;
                PerformerBehaviorExecutionLane extensionLane = PerformerBehaviorExecutionLane.None;
                int extensionTriggerId = 0;
                if (isBuiltinKind)
                {
                    kindId = (byte)kind;
                }
                else
                {
                    if (_behaviorKinds == null)
                    {
                        throw new InvalidOperationException(
                            $"Presenter '{ownerKey}' behavior[{i}].kind has invalid value '{kindText}'.");
                    }

                    kindId = _behaviorKinds?.GetId(kindText) ?? 0;
                    if (kindId < PerformerBehaviorKindRegistry.FirstModBehaviorKindId)
                    {
                        throw new InvalidOperationException(
                            $"Presenter '{ownerKey}' behavior[{i}].kind references unregistered presenter behavior kind '{kindText}'.");
                    }

                    if (!_behaviorKinds!.TryGetDescriptor(kindId, out PerformerBehaviorExtensionDescriptor descriptor))
                    {
                        throw new InvalidOperationException(
                            $"Presenter '{ownerKey}' behavior[{i}].kind references presenter behavior kind '{kindText}' without a registered descriptor.");
                    }

                    (extensionLane, extensionTriggerId) = ParseExtensionBehaviorExecution(
                        obj,
                        descriptor,
                        $"Presenter '{ownerKey}' behavior[{i}]");
                    kind = BehaviorKind.Extension;
                }

                int slotIndex = ParseRequiredBehaviorSlot(obj["slot"], $"Presenter '{ownerKey}' behavior[{i}].slot");
                if (slotIndex is < 0 or >= 32)
                {
                    throw new InvalidOperationException($"Presenter '{ownerKey}' behavior[{i}] uses slot {slotIndex}, but valid behavior slots are 0-31.");
                }

                uint slotBit = 1u << slotIndex;
                if ((seenSlots & slotBit) != 0u)
                {
                    throw new InvalidOperationException($"Presenter '{ownerKey}' defines duplicate behavior slot '{obj["slot"]?.GetValue<string>()}'.");
                }

                seenSlots |= slotBit;
                if (obj["activationCondition"] != null)
                {
                    throw new InvalidOperationException(
                        $"Presenter '{ownerKey}' behavior[{i}] declares activationCondition, but behavior activation conditions are not wired into runtime consumption.");
                }

                var slot = new BehaviorSlot
                {
                    SlotIndex = slotIndex,
                    Kind = kind,
                    KindId = kindId,
                    ExtensionLane = extensionLane,
                    ExtensionTriggerId = extensionTriggerId,
                    ActiveByDefault = obj["activeByDefault"]?.GetValue<bool>() ?? false,
                };

                switch (kind)
                {
                    case BehaviorKind.AssetBinding:
                        RejectBehaviorScopedFields(obj, ownerKey, i, "worldText", "surfaceSource", "instancedBatch");
                        slot.AssetBinding = ParseAssetBinding(obj["assetBinding"]);
                        slot.Style = ParseBehaviorStyle(obj["style"], ownerKey, i);
                        slot.Motion = ParseBehaviorMotion(obj["motion"], ownerKey, i);
                        break;
                    case BehaviorKind.AttributeBinding:
                        RejectBehaviorScopedFields(obj, ownerKey, i, "worldText", "style", "motion", "surfaceSource", "instancedBatch");
                        slot.AttributeBinding = ParseAttributeBinding(obj["attributeBinding"]);
                        break;
                    case BehaviorKind.TagBinding:
                        RejectBehaviorScopedFields(obj, ownerKey, i, "worldText", "style", "motion", "surfaceSource", "instancedBatch");
                        slot.TagBinding = ParseTagBinding(obj["tagBinding"]);
                        break;
                    case BehaviorKind.Animator:
                        RejectBehaviorScopedFields(obj, ownerKey, i, "worldText", "style", "motion", "surfaceSource", "instancedBatch");
                        slot.Animator = ParseAnimator(obj["animator"]);
                        break;
                    case BehaviorKind.Attachment:
                        RejectBehaviorScopedFields(obj, ownerKey, i, "worldText", "style", "motion", "surfaceSource", "instancedBatch");
                        slot.Attachment = ParseAttachment(obj["attachment"]);
                        break;
                    case BehaviorKind.Sound:
                        RejectBehaviorScopedFields(obj, ownerKey, i, "worldText", "style", "motion", "surfaceSource", "instancedBatch");
                        slot.Sound = ParseSound(obj["sound"]);
                        break;
                    case BehaviorKind.Material:
                        RejectBehaviorScopedFields(obj, ownerKey, i, "worldText", "style", "motion", "surfaceSource", "instancedBatch");
                        slot.Material = ParseMaterial(obj["material"]);
                        break;
                    case BehaviorKind.Spline:
                        RejectBehaviorScopedFields(obj, ownerKey, i, "worldText", "style", "motion", "surfaceSource", "instancedBatch");
                        slot.Spline = ParseSpline(obj["spline"]);
                        break;
                    case BehaviorKind.Grounding:
                        RejectBehaviorScopedFields(obj, ownerKey, i, "worldText", "style", "motion", "surfaceSource", "instancedBatch");
                        slot.Grounding = ParseGrounding(obj["grounding"]);
                        break;
                    case BehaviorKind.MinimapMarker:
                        RejectBehaviorScopedFields(obj, ownerKey, i, "worldText", "style", "motion", "surfaceSource", "instancedBatch");
                        slot.MinimapMarker = ParseMinimapMarker(obj["minimapMarker"]);
                        break;
                    case BehaviorKind.WorldText:
                        RejectBehaviorScopedFields(obj, ownerKey, i, "assetBinding", "surfaceSource", "instancedBatch");
                        slot.WorldText = ParseWorldText(obj["worldText"], ownerKey, i);
                        slot.Style = ParseBehaviorStyle(obj["style"], ownerKey, i);
                        slot.Motion = ParseBehaviorMotion(obj["motion"], ownerKey, i);
                        slot.AssetBinding = BuildWorldTextAssetBinding(slot.WorldText);
                        break;
                    case BehaviorKind.SurfaceSource:
                        RejectBehaviorScopedFields(obj, ownerKey, i, "assetBinding", "worldText", "style", "motion", "instancedBatch");
                        slot.SurfaceSource = ParseSurface(obj["surfaceSource"], ownerKey, $"Presenter '{ownerKey}' behavior[{i}].surfaceSource");
                        break;
                    case BehaviorKind.InstancedBatch:
                        RejectBehaviorScopedFields(obj, ownerKey, i, "assetBinding", "worldText", "style", "motion", "surfaceSource");
                        slot.InstancedBatch = ParseInstancedBatchBehavior(obj["instancedBatch"], ownerKey, i);
                        break;
                    case BehaviorKind.Extension:
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported presenter behavior kind '{kind}'.");
                }

                slots[i] = slot;
            }

            return slots;
        }

        private (PerformerBehaviorExecutionLane Lane, int TriggerId) ParseExtensionBehaviorExecution(
            JsonObject obj,
            in PerformerBehaviorExtensionDescriptor descriptor,
            string context)
        {
            if (obj["execution"] is not JsonObject execution)
            {
                throw new InvalidOperationException($"{context}.execution is required for extension presenter behavior.");
            }

            PerformerBehaviorExecutionLane lane =
                ParseRequiredEnum<PerformerBehaviorExecutionLane>(execution["lane"], $"{context}.execution.lane");
            if (lane != descriptor.Lane)
            {
                throw new InvalidOperationException(
                    $"{context}.execution.lane '{lane}' does not match registered lane '{descriptor.Lane}'.");
            }

            return lane switch
            {
                PerformerBehaviorExecutionLane.Bootstrap => (lane, 0),
                PerformerBehaviorExecutionLane.ContinuousTick => (lane, 0),
                PerformerBehaviorExecutionLane.OwnerAttributeDirty => (lane, ParseExtensionAttributeTrigger(execution, context)),
                PerformerBehaviorExecutionLane.OwnerTagDirty => (lane, ParseExtensionTagTrigger(execution, context)),
                _ => throw new InvalidOperationException(
                    $"{context}.execution.lane '{lane}' is not supported by the current presenter runtime."),
            };
        }

        private int ParseExtensionAttributeTrigger(JsonObject execution, string context)
        {
            if (execution["trigger"] is not JsonObject trigger)
            {
                throw new InvalidOperationException($"{context}.execution.trigger is required for OwnerAttributeDirty.");
            }

            int attributeId = ResolveRegisteredId(
                _resolveAttributeName,
                trigger["attributeId"],
                $"{context}.execution.trigger.attributeId");
            if (attributeId <= 0)
            {
                throw new InvalidOperationException($"{context}.execution.trigger.attributeId must resolve to a positive attribute id.");
            }

            return attributeId;
        }

        private static int ParseExtensionTagTrigger(JsonObject execution, string context)
        {
            if (execution["trigger"] is not JsonObject trigger)
            {
                throw new InvalidOperationException($"{context}.execution.trigger is required for OwnerTagDirty.");
            }

            int tagId = ResolveTagId(trigger["tagId"]);
            if (tagId <= 0)
            {
                throw new InvalidOperationException($"{context}.execution.trigger.tagId must resolve to a positive tag id.");
            }

            return tagId;
        }

        private static void RejectBehaviorScopedFields(JsonObject obj, string ownerKey, int behaviorIndex, params string[] fieldNames)
        {
            for (int i = 0; i < fieldNames.Length; i++)
            {
                string field = fieldNames[i];
                if (obj[field] != null)
                {
                    throw new InvalidOperationException(
                        $"Presenter '{ownerKey}' behavior[{behaviorIndex}] field '{field}' is not valid for this behavior kind.");
                }
            }
        }

        private WorldTextConfig ParseWorldText(JsonNode? node, string ownerKey, int behaviorIndex)
        {
            string context = $"Presenter '{ownerKey}' behavior[{behaviorIndex}].worldText";
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"{context} requires an object.");
            }

            RejectUnknownFields(obj, context, WorldTextFields);

            return new WorldTextConfig
            {
                TextTokenId = ResolveTextTokenId(obj["textToken"], $"{context}.textToken"),
                Mode = ParseRequiredEnum<WorldHudValueMode>(obj["mode"], $"{context}.mode"),
                ValueParamKey = ParseOptionalParamKey(obj["valueParamKey"], $"{context}.valueParamKey"),
                SecondaryValueParamKey = ParseOptionalParamKey(obj["secondaryValueParamKey"], $"{context}.secondaryValueParamKey"),
                FontSize = ParseWorldTextFontSize(obj["fontSize"], context),
            };
        }

        private static int ParseWorldTextFontSize(JsonNode? node, string context)
        {
            if (node == null)
            {
                return 16;
            }

            int fontSize = ParseRequiredInt(node, $"{context}.fontSize");
            if (fontSize <= 0)
            {
                throw new InvalidOperationException($"{context}.fontSize must be > 0.");
            }

            return fontSize;
        }

        private static AssetBindingConfig BuildWorldTextAssetBinding(in WorldTextConfig worldText)
        {
            return new AssetBindingConfig
            {
                AssetKind = AssetKind.WorldText,
                AssetId = worldText.TextTokenId,
                RenderPath = VisualRenderPath.None,
                Mobility = VisualMobility.Movable,
                LocalOffset = Vector3.Zero,
                LocalRotation = Quaternion.Identity,
                LocalScale = Vector3.One,
                ScaleParamKey = worldText.ValueParamKey,
                MaterialParamKey = worldText.SecondaryValueParamKey,
                AssetIdParamKey = PresenterParamKeyRegistry.UnsetParamKey,
                AssetSwapParamKey = PresenterParamKeyRegistry.UnsetParamKey,
                AssetSwapTable = Array.Empty<AssetSwapEntry>(),
                VisibilityParamKey = PresenterParamKeyRegistry.UnsetParamKey,
                SurfaceLayerKey = string.Empty,
                MaterialCustomData = MaterialCustomDataBinding.Empty,
            };
        }

        private static BehaviorStyleConfig ParseBehaviorStyle(JsonNode? node, string ownerKey, int behaviorIndex)
        {
            if (node == null)
            {
                return default;
            }

            string context = $"Presenter '{ownerKey}' behavior[{behaviorIndex}].style";
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"{context} must be an object.");
            }

            RejectUnknownFields(obj, context, StyleFields);

            var style = new BehaviorStyleConfig();
            if (obj["color"] != null)
            {
                style.HasColor = true;
                style.Color = ParseRequiredVector4(obj["color"], $"{context}.color");
            }

            if (obj["alphaPolicy"] != null)
            {
                style.AlphaPolicy = ParseRequiredEnum<BehaviorAlphaPolicy>(obj["alphaPolicy"], $"{context}.alphaPolicy");
            }

            return style;
        }

        private static BehaviorMotionConfig ParseBehaviorMotion(JsonNode? node, string ownerKey, int behaviorIndex)
        {
            if (node == null)
            {
                return default;
            }

            string context = $"Presenter '{ownerKey}' behavior[{behaviorIndex}].motion";
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"{context} must be an object.");
            }

            RejectUnknownFields(obj, context, MotionFields);

            return new BehaviorMotionConfig
            {
                YDriftPerSecond = obj["yDriftPerSecond"] == null
                    ? 0f
                    : ParseRequiredFiniteFloat(obj["yDriftPerSecond"], $"{context}.yDriftPerSecond"),
            };
        }

        private InstancedBatchConfig ParseInstancedBatchBehavior(JsonNode? node, string ownerKey, int behaviorIndex)
        {
            string context = $"Presenter '{ownerKey}' behavior[{behaviorIndex}].instancedBatch";
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"{context} requires an object.");
            }

            RejectUnknownFields(obj, context, InstancedBatchFields);

            string batchKey = ParseRequiredSemanticString(obj["batchAssetId"], $"{context}.batchAssetId");
            int batchAssetId = _resolveInstancedBatchAssetId(batchKey);
            if (batchAssetId <= 0)
            {
                throw new InvalidOperationException(
                    $"Presenter '{ownerKey}' references unknown instanced batch asset '{batchKey}'.");
            }

            return new InstancedBatchConfig { BatchAssetId = batchAssetId };
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

        private ParamDefault[] ParseParamDefaults(JsonNode? node, string owner)
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
                    throw new InvalidOperationException($"Presenter '{owner}' paramDefaults[{i}] must be an object.");
                }

                string defaultContext = $"Presenter '{owner}' paramDefaults[{i}]";

                if (obj["value"] != null)
                {
                    throw new InvalidOperationException(
                        $"{defaultContext} uses removed field 'value'. Use explicit lane-specific fields 'floatValue', 'intValue', or 'vectorValue'.");
                }

                RejectUnknownFields(obj, defaultContext, ParamDefaultFields);

                ParamLane lane = ParseRequiredParamLane(obj, defaultContext);
                var paramDefault = new ParamDefault
                {
                    ParamKey = ParseRequiredParamKey(obj["paramKey"], $"{defaultContext}.paramKey"),
                    Lane = lane,
                };

                switch (lane)
                {
                    case ParamLane.Int:
                        if (obj["intValue"] is not JsonValue intValueNode || !intValueNode.TryGetValue<int>(out int intValue))
                        {
                            throw new InvalidOperationException($"{defaultContext} lane '{ParamLane.Int}' requires integer field 'intValue'.");
                        }

                        paramDefault.IntValue = intValue;
                        break;
                    case ParamLane.Vector:
                        if (obj["vectorValue"] is not JsonArray vectorValueNode || vectorValueNode.Count < 4)
                        {
                            throw new InvalidOperationException($"{defaultContext} lane '{ParamLane.Vector}' requires 4-component array field 'vectorValue'.");
                        }

                        paramDefault.VectorValue = ParseVector4(vectorValueNode);
                        break;
                    default:
                        if (obj["floatValue"] is not JsonValue floatValueNode || !floatValueNode.TryGetValue<float>(out float floatValue))
                        {
                            throw new InvalidOperationException($"{defaultContext} lane '{ParamLane.Float}' requires numeric field 'floatValue'.");
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

            RejectUnknownFields(obj, "AssetBinding", AssetBindingFields);

            AssetKind assetKind = ParseRequiredEnum<AssetKind>(obj["assetKind"], "AssetBinding.assetKind");
            if (assetKind == AssetKind.Sound)
            {
                throw new InvalidOperationException(
                    "AssetBinding.assetKind 'Sound' has been removed from presenter config. Author behavior kind 'Sound' with sound.soundAssetId, loop, volume, and volumeParamKey.");
            }

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

            RejectUnknownFields(obj, "AttributeBinding", AttributeBindingFields);

            return new AttributeBindingConfig
            {
                AttributeId = ResolveAttributeId(obj),
                TargetParamKey = ParseRequiredParamKey(obj["targetParamKey"], "AttributeBinding.targetParamKey"),
                Mode = ParseAttributeBindingMode(obj["mode"], "AttributeBinding.mode"),
                Thresholds = ParseThresholds(obj["thresholds"]),
            };
        }

        private static ValueSourceKind ParseAttributeBindingMode(JsonNode? node, string context)
        {
            ValueSourceKind mode = ParseRequiredEnumOrDefault(node, ValueSourceKind.Attribute, context);
            if (mode is ValueSourceKind.Attribute or ValueSourceKind.AttributeRatio or ValueSourceKind.AttributeBase)
            {
                return mode;
            }

            throw new InvalidOperationException(
                $"{context} must be Attribute, AttributeRatio, or AttributeBase.");
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

            RejectUnknownFields(obj, "TagBinding", TagBindingFields);

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

            RejectUnknownFields(obj, "Animator", AnimatorFields);

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

            RejectUnknownFields(obj, "Attachment", AttachmentFields);

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

            RejectUnknownFields(obj, "Grounding", GroundingFields);

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

                RejectUnknownFields(obj, $"materialCustomData[{i}]", MaterialCustomDataSlotFields);

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

            RejectUnknownFields(obj, "MinimapMarker", MinimapMarkerFields);

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
            else if (orientationMode == MinimapMarkerOrientationMode.PresenterForward)
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

            RejectUnknownFields(obj, "Sound", SoundFields);

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

            RejectUnknownFields(obj, "Material", MaterialFields);

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

            RejectUnknownFields(obj, "Spline", SplineFields);

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

                    return PresenterParamKeyRegistry.Register(key);
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
                    return PresenterBehaviorSlotRegistry.Register(key);
                }
            }

            throw new InvalidOperationException($"{context} must be a non-empty semantic string.");
        }

        private static class PresenterBehaviorSlotRegistry
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
                ["surface"] = 12,
                ["attributeRatio"] = 13,
                ["attributeCurrent"] = 14,
                ["attributeBase"] = 15,
            };

            public static int Register(string key)
            {
                if (!Slots.TryGetValue(key, out int slot))
                {
                    throw new InvalidOperationException(
                        $"Unknown presenter behavior slot '{key}'. Register a semantic slot in PresenterBehaviorSlotRegistry instead of relying on load order.");
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

                RejectUnknownFields(obj, $"thresholds[{i}]", ThresholdFields);

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

                RejectUnknownFields(obj, $"swapTable[{i}]", MaterialSwapEntryFields);

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

                RejectUnknownFields(obj, $"assetSwapTable[{i}]", AssetSwapEntryFields);

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
                throw new InvalidOperationException($"Presenter behavior {kind} assetId requires an explicit semantic string.");
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int numericId))
                {
                    throw new InvalidOperationException(
                        $"Presenter behavior {kind} assetId must be a semantic string, not numeric id {numericId}.");
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    key = RequireCanonicalString(key, $"Presenter behavior {kind} assetId");
                    int id = kind == AssetKind.WorldText
                        ? _resolveTextTokenId(key)
                        : _resolveBehaviorAssetId(kind, key);
                    if (id <= 0)
                    {
                        throw new InvalidOperationException($"Presenter behavior references unknown {kind} asset '{key}'.");
                    }

                    return id;
                }
            }

            throw new InvalidOperationException($"Presenter behavior {kind} assetId must be a semantic string.");
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
                        $"Presenter behavior {subject} must be a semantic string, not numeric id {numericId}.");
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    key = RequireCanonicalString(key, $"Presenter behavior {subject}");
                    int id = resolver(key);
                    if (id <= 0)
                    {
                        throw new InvalidOperationException($"Presenter behavior references unknown {subject} '{key}'.");
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
                        $"Presenter behavior tagId must be a semantic string, not numeric id {numericId}.");
                }

                if (value.TryGetValue<string>(out string key) && !string.IsNullOrWhiteSpace(key))
                {
                    key = RequireCanonicalString(key, "Presenter behavior tagId");
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

        private SurfaceAuthoringBlock ParseSurface(JsonNode? node, string key, string context)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException(
                    $"{context} requires the surface authoring object.");
            }

            RejectUnknownFields(obj, context, SurfaceSourceFields);

            return new SurfaceAuthoringBlock
            {
                Kind = ParseEnum(obj["kind"]?.GetValue<string>(), PresenterSurfaceKind.SplineRibbon),
                ProfileId = obj["profileId"]?.GetValue<string>() ?? string.Empty,
                GeometrySource = ParseSurfaceGeometrySource(obj["geometrySource"], key),
                ChunkBake = ParseChunkBakePolicy(obj["chunkBake"], key),
                MaterialSet = ParseMaterialSet(obj["materialSet"], key),
                LodProfileId = obj["lodProfileId"]?.GetValue<string>() ?? string.Empty,
                Grounding = ParseGroundingPolicy(obj["grounding"]),
                BoundsPolicy = obj["boundsPolicy"]?.GetValue<string>() ?? string.Empty,
            };
        }

        private PresenterSurfaceGeometrySource ParseSurfaceGeometrySource(JsonNode? node, string key)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"SurfaceSource presenter '{key}' must declare object field 'surface.geometrySource'.");
            }

            RejectUnknownFields(obj, $"SurfaceSource presenter '{key}' surface.geometrySource", SurfaceGeometrySourceFields);

            return new PresenterSurfaceGeometrySource
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

        private PresenterSurfaceChunkBakePolicy ParseChunkBakePolicy(JsonNode? node, string key)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"SurfaceSource presenter '{key}' must declare object field 'surface.chunkBake'.");
            }

            RejectUnknownFields(obj, $"SurfaceSource presenter '{key}' surface.chunkBake", SurfaceChunkBakeFields);

            return new PresenterSurfaceChunkBakePolicy
            {
                Enabled = obj["enabled"]?.GetValue<bool>() ?? true,
                Ownership = ParseEnum(obj["ownership"]?.GetValue<string>(), PresenterSurfaceChunkOwnership.PerChunk),
                ChunkInfluencePolicy = obj["chunkInfluencePolicy"]?.GetValue<string>() ?? string.Empty,
                RebakePolicy = obj["rebakePolicy"]?.GetValue<string>() ?? string.Empty,
                UsageHint = ParseEnum(obj["usageHint"]?.GetValue<string>(), ProceduralMeshUsageHint.Static),
            };
        }

        private PresenterSurfaceMaterialSet ParseMaterialSet(JsonNode? node, string key)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"SurfaceSource presenter '{key}' must declare object field 'surface.materialSet'.");
            }

            RejectUnknownFields(obj, $"SurfaceSource presenter '{key}' surface.materialSet", SurfaceMaterialSetFields);

            return new PresenterSurfaceMaterialSet
            {
                PrimaryMaterialId = obj["primaryMaterialId"]?.GetValue<string>() ?? string.Empty,
                SecondaryMaterialId = obj["secondaryMaterialId"]?.GetValue<string>() ?? string.Empty,
                AllowInstanceOverride = obj["allowInstanceOverride"]?.GetValue<bool>() ?? false,
            };
        }

        private static PresenterSurfaceGroundingPolicy ParseGroundingPolicy(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                return new PresenterSurfaceGroundingPolicy();
            }

            RejectUnknownFields(obj, "surface.grounding", SurfaceGroundingPolicyFields);

            return new PresenterSurfaceGroundingPolicy
            {
                Mode = obj["mode"]?.GetValue<string>() ?? string.Empty,
            };
        }

        private static PresenterSurfaceValueSource? ParseSurfaceValueSource(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                return null;
            }

            RejectUnknownFields(obj, "surface value source", SurfaceValueSourceFields);

            return new PresenterSurfaceValueSource
            {
                Kind = ParseEnum(obj["kind"]?.GetValue<string>(), PresenterSurfaceValueSourceKind.Constant),
                Id = obj["id"]?.GetValue<string>() ?? string.Empty,
                GraphProgramId = obj["graphProgramId"]?.GetValue<int>() ?? 0,
            };
        }

        private static ConditionRef ParseDefinitionVisibility(JsonNode? node, string key)
        {
            if (node != null && node["graphProgramId"] != null)
            {
                throw new InvalidOperationException(
                    $"Presenter '{key}' visibility.graphProgramId is not wired into runtime visibility evaluation and cannot be authored.");
            }

            return ParseConditionRef(node, $"Presenter '{key}' visibility", allowGraphProgramId: false);
        }

        private static ConditionRef ParseConditionRef(JsonNode? node, string context, bool allowGraphProgramId)
        {
            if (node == null)
            {
                return ConditionRef.AlwaysTrue;
            }

            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"{context} must be an object.");
            }

            RejectUnknownFields(obj, context, ConditionFields);

            var cond = new ConditionRef();
            if (obj["inline"] != null)
            {
                cond.Inline = ParseRequiredEnumOrDefault(obj["inline"], InlineConditionKind.None, $"{context}.inline");
            }

            if (obj["graphProgramId"] != null)
            {
                if (!allowGraphProgramId)
                {
                    throw new InvalidOperationException($"{context}.graphProgramId is not authorable here.");
                }

                cond.GraphProgramId = obj["graphProgramId"]?.GetValue<int>() ?? 0;
            }

            return cond;
        }

        private static void StampRuleOwners(int ownerDefinitionId, PresenterRule[] rules)
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
