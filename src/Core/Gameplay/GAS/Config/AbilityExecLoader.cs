using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Progression.Registry;
using Ludots.Core.Input.Orders;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Core.Gameplay.GAS.Config
{
    /// <summary>
    /// Loads ability definitions from JSON and populates AbilityDefinitionRegistry
    /// with the new AbilityExecSpec execution model.
    /// JSON format: array of ability objects with "id", "exec", "blockTags" etc.
    /// </summary>
    public sealed class AbilityExecLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly AbilityDefinitionRegistry _registry;
        private const int MaxToggleActiveEffects = 4;
        private static readonly string[] AbilityPropertyNames =
        {
            "id",
            "exec",
            "blockTags",
            "categories",
            "interactionContextProfile",
            "activationPrecondition",
            "toggleSpec",
            "targeting",
            "presentation",
            "input",
            "useRequirement",
            "showRequirement",
            "triggerGraphs",
        };

        private static readonly string[] RemovedAimVisualFieldNames =
        {
            "aimVisual",
            "areaPresenterId",
            "rangeCirclePresenterId",
            "previewPresenterId",
            "presenterId",
        };

        public AbilityExecLoader(ConfigPipeline pipeline, AbilityDefinitionRegistry registry)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// Load abilities from the config pipeline and register them.
        /// </summary>
        public void Load(
            ConfigCatalog? catalog = null,
            ConfigConflictReport? report = null,
            string relativePath = "GAS/abilities.json")
        {
            _registry.Clear();
            AbilityIdRegistry.Clear();

            var entry = ConfigPipeline.RequireEntry(catalog!, relativePath, ConfigMergePolicy.ArrayById, "id");
            var mergedEntries = _pipeline.MergeArrayByIdFromCatalog(in entry, report!);
            var merged = new List<(string Id, JsonObject Node)>(mergedEntries.Count);
            for (int i = 0; i < mergedEntries.Count; i++)
            {
                merged.Add((mergedEntries[i].Id, mergedEntries[i].Node));
            }

            merged.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id));
            for (int i = 0; i < merged.Count; i++)
            {
                AbilityIdRegistry.Register(merged[i].Id);
            }

            var errors = new List<string>();
            for (int i = 0; i < merged.Count; i++)
            {
                try
                {
                    var def = CompileAbility(merged[i].Node, merged[i].Id, relativePath);
                    int abilityId = AbilityIdRegistry.GetId(merged[i].Id);
                    if (abilityId <= 0)
                    {
                        throw new InvalidOperationException($"Failed to resolve ability id '{merged[i].Id}'.");
                    }

                    _registry.Register(abilityId, in def);
                }
                catch (Exception ex)
                {
                    errors.Add($"Ability '{merged[i].Id}': {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                throw new AggregateException(
                    $"[AbilityExecLoader] {errors.Count} ability compilation error(s) in '{relativePath}'.",
                    errors.ConvertAll(e => (Exception)new InvalidOperationException(e)));
            }
        }

        /// <summary>
        /// Compile a single ability from a JSON object (for testing / external callers).
        /// </summary>
        public static AbilityDefinition CompileAbility(JsonObject obj, string id, string path)
        {
            var def = new AbilityDefinition();

            if (obj["indicator"] != null)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field 'indicator' is removed; use 'targeting.castRangeCm' and 'targeting.impactEffect'. Aim visuals belong in Presenter rules.");
            }

            RejectRemovedAimVisualFields(obj, id, path, currentPath: string.Empty);

            // ── exec block ──
            if (obj["exec"] is JsonObject execObj)
            {
                CompileCallerParamsPool(
                    execObj,
                    id,
                    path,
                    out var pool,
                    out bool hasPool,
                    out bool[] callerParamSetsWithDurationTicks,
                    out int[] callerParamSetParamCounts);
                def.ExecSpec = CompileExecSpec(
                    execObj,
                    id,
                    path,
                    hasPool,
                    pool.Count,
                    callerParamSetsWithDurationTicks,
                    callerParamSetParamCounts);
                def.ExecCallerParamsPool = pool;
                def.HasExecCallerParamsPool = hasPool;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field 'exec' is required.");
            }

            // ── onActivateEffects ──
            if (obj["onActivateEffects"] != null)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field 'onActivateEffects': " +
                    "author effects once in exec.items with EffectSignal or EffectClip.");
            }

            RequireKnownRootProperties(obj, id, path);

            // ── blockTags ──
            if (obj["blockTags"] is JsonObject blockObj)
            {
                var blockTags = default(AbilityActivationBlockTags);
                if (blockObj["requiredAll"] is JsonArray reqArr)
                {
                    for (int i = 0; i < reqArr.Count; i++)
                    {
                        string tag = RequireNonEmptyString(reqArr[i], $"blockTags.requiredAll[{i}]", id, path);
                        blockTags.RequiredAll.AddTag(TagRegistry.Register(tag));
                    }
                }
                if (blockObj["blockedAny"] is JsonArray blkArr)
                {
                    for (int i = 0; i < blkArr.Count; i++)
                    {
                        string tag = RequireNonEmptyString(blkArr[i], $"blockTags.blockedAny[{i}]", id, path);
                        blockTags.BlockedAny.AddTag(TagRegistry.Register(tag));
                    }
                }
                def.HasActivationBlockTags = true;
                def.ActivationBlockTags = blockTags;
            }

            // ── categories (ability classification; not gameplay tags) ──
            if (obj["catalogTags"] != null)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field 'catalogTags' was renamed to 'categories'.");
            }

            if (obj["categories"] is JsonArray catalogArr)
            {
                var categories = default(GameplayTagContainer);
                bool hasCategories = false;
                foreach (var entry in catalogArr)
                {
                    string? categoryName = entry?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(categoryName))
                    {
                        throw new InvalidOperationException(
                            $"Ability '{id}' in '{path}' categories entries must be non-empty strings.");
                    }

                    categories.AddTag(AbilityCategoryRegistry.Register(categoryName));
                    hasCategories = true;
                }

                def.Categories = categories;
                def.HasCategories = hasCategories;
            }

            // ── interactionContextProfile (RFC-0065 CTX-6) ──
            if (obj["interactionContextProfile"] != null)
            {
                string? contextProfileId = obj["interactionContextProfile"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(contextProfileId))
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' in '{path}' interactionContextProfile must be a non-empty string.");
                }

                def.InteractionContextProfileId = contextProfileId.Trim();
                def.HasInteractionContextProfile = true;
            }

            if (obj["activationPrecondition"] is JsonObject preconditionObj)
            {
                def.ActivationPrecondition = CompileActivationPrecondition(preconditionObj, id, path);
                def.HasActivationPrecondition = def.ActivationPrecondition.ValidationGraphId > 0;
            }

            // ── toggleSpec ──
            if (obj["toggleSpec"] is JsonObject toggleObj)
            {
                def.ToggleSpec = CompileToggleSpec(toggleObj, id, path);
                def.HasToggleSpec = def.ToggleSpec.ToggleTagId > 0;
            }

            // ── targeting ──
            if (obj["targeting"] is JsonObject targetingObj)
            {
                def.Targeting = CompileTargeting(targetingObj, id, path);
                def.HasTargeting = true;
            }

            if (obj["presentation"] is JsonObject presentationObj)
            {
                def.Presentation = CompilePresentation(presentationObj, id, path);
                def.HasPresentation = def.Presentation != null;
            }

            if (obj["input"] is JsonObject inputObj)
            {
                def.InputBindingOverride = CompileInputBindingOverride(inputObj, id, path);
                def.HasInputBindingOverride = true;
            }

            def.UseProgressionRequirementId = ResolveProgressionRequirement(obj, "useRequirement", id, path);
            def.HasUseProgressionRequirement = def.UseProgressionRequirementId > 0;
            def.ShowProgressionRequirementId = ResolveProgressionRequirement(obj, "showRequirement", id, path);
            def.HasShowProgressionRequirement = def.ShowProgressionRequirementId > 0;

            if (obj["triggerGraphs"] is JsonArray triggerGraphs)
            {
                def.TriggerGraphs = new List<string>(triggerGraphs.Count);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < triggerGraphs.Count; i++)
                {
                    if (triggerGraphs[i] is not JsonValue value || !value.TryGetValue<string>(out string? graph))
                    {
                        throw new InvalidOperationException(
                            $"Ability '{id}' in '{path}' field 'triggerGraphs' entries must be strings.");
                    }

                    if (string.IsNullOrWhiteSpace(graph) || !string.Equals(graph, graph.Trim(), StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Ability '{id}' in '{path}' field 'triggerGraphs' entries must be trimmed non-empty strings.");
                    }

                    if (!seen.Add(graph))
                    {
                        throw new InvalidOperationException(
                            $"Ability '{id}' in '{path}' field 'triggerGraphs' repeats graph id '{graph}'.");
                    }

                    def.TriggerGraphs.Add(graph);
                }
            }

            return def;
        }

        private static int ResolveProgressionRequirement(JsonObject obj, string fieldName, string id, string path)
        {
            string requirementName = obj[fieldName]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(requirementName))
            {
                return 0;
            }

            int requirementId = ProgressionRequirementIdRegistry.GetId(requirementName);
            if (requirementId <= 0)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field '{fieldName}' references unknown progression requirement '{requirementName}'.");
            }

            return requirementId;
        }

        private static void RequireKnownRootProperties(JsonObject obj, string id, string path)
        {
            foreach (KeyValuePair<string, JsonNode?> kvp in obj)
            {
                bool allowed = false;
                for (int i = 0; i < AbilityPropertyNames.Length; i++)
                {
                    if (string.Equals(kvp.Key, AbilityPropertyNames[i], StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' in '{path}' has unexpected field '{kvp.Key}'. Use only: {string.Join(", ", AbilityPropertyNames)}.");
                }
            }
        }

        // ──────────────── ExecSpec ────────────────

        private static AbilityExecSpec CompileExecSpec(
            JsonObject execObj,
            string id,
            string path,
            bool hasCallerParamsPool,
            int callerParamsCount,
            bool[] callerParamSetsWithDurationTicks,
            int[] callerParamSetParamCounts)
        {
            var spec = default(AbilityExecSpec);

            // clockId
            string clockStr = RequireNonEmptyString(execObj["clockId"], "exec.clockId", id, path);
            spec.ClockId = ParseClockId(clockStr);

            // interruptAny
            if (execObj["interruptAny"] is JsonArray intArr)
            {
                for (int i = 0; i < intArr.Count; i++)
                {
                    string tag = RequireNonEmptyString(intArr[i], $"exec.interruptAny[{i}]", id, path);
                    spec.InterruptAny.AddTag(TagRegistry.Register(tag));
                }
            }

            // items
            if (execObj["items"] is JsonArray itemsArr)
            {
                if (itemsArr.Count > AbilityExecSpec.MAX_ITEMS)
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' in '{path}' field 'exec.items' contains {itemsArr.Count} items, max {AbilityExecSpec.MAX_ITEMS}.");
                }

                int idx = 0;
                foreach (var itemNode in itemsArr)
                {
                    if (itemNode is not JsonObject itemObj)
                    {
                        throw new InvalidOperationException(
                            $"Ability '{id}' in '{path}' field 'exec.items[{idx}]' must be an object.");
                    }

                    CompileItem(
                        itemObj,
                        ref spec,
                        idx,
                        id,
                        path,
                        hasCallerParamsPool,
                        callerParamsCount,
                        callerParamSetsWithDurationTicks,
                        callerParamSetParamCounts);
                    idx++;
                }
            }

            return spec;
        }

        private static AbilityActivationPrecondition CompileActivationPrecondition(JsonObject preconditionObj, string id, string path)
        {
            string graphName = preconditionObj["validationGraph"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(graphName))
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' field 'activationPrecondition.validationGraph' is required in '{path}'.");
            }

            int graphId = GraphIdRegistry.GetId(graphName);
            if (graphId <= 0)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' field 'activationPrecondition.validationGraph' references unknown graph '{graphName}'.");
            }

            return new AbilityActivationPrecondition
            {
                ValidationGraphId = graphId
            };
        }

        private static void CompileItem(
            JsonObject itemObj,
            ref AbilityExecSpec spec,
            int idx,
            string id,
            string path,
            bool hasCallerParamsPool,
            int callerParamsCount,
            bool[] callerParamSetsWithDurationTicks,
            int[] callerParamSetParamCounts)
        {
            string kindStr = RequireNonEmptyString(itemObj["kind"], $"exec.items[{idx}].kind", id, path);
            var kind = ParseItemKind(kindStr);
            GasOperatorWhitelist.ValidateExecItemKind(kindStr, id);
            if (itemObj["tick"] is not JsonNode tickNode)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field 'exec.items[{idx}].tick' is required.");
            }

            int tick = tickNode.GetValue<int>();
            int durationTicks = 0;
            if (kind == ExecItemKind.EffectClip)
            {
                if (itemObj["durationTicks"] is not JsonNode durationTicksNode)
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' in '{path}' field 'exec.items[{idx}].durationTicks' is required for EffectClip.");
                }

                durationTicks = durationTicksNode.GetValue<int>();
                if (durationTicks < 0)
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' in '{path}' field 'exec.items[{idx}].durationTicks' must be non-negative for EffectClip.");
                }
            }
            else
            {
                durationTicks = itemObj["duration"]?.GetValue<int>() ?? 0;
            }

            GasClockId clockId = default;
            string? clockStr = itemObj["clockId"]?.GetValue<string>() ?? itemObj["clock"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(clockStr)) clockId = ParseClockId(clockStr);

            int tagId = 0;
            string? tagStr = itemObj["tag"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(tagStr)) tagId = TagRegistry.Register(tagStr);

            int templateId = 0;
            string? templateStr = itemObj["template"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(templateStr))
            {
                templateId = EffectTemplateIdRegistry.GetId(templateStr);
                if (templateId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' item[{idx}] references unknown effect template '{templateStr}'.");
                }
            }

            byte callerParamsIdx = 0xFF;
            if (itemObj["callerParamsIdx"] is JsonNode cpNode)
            {
                int rawCallerParamsIdx = cpNode.GetValue<int>();
                if (rawCallerParamsIdx < 0 || rawCallerParamsIdx > byte.MaxValue)
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' in '{path}' field 'exec.items[{idx}].callerParamsIdx' must be in byte range.");
                }

                if (!hasCallerParamsPool || rawCallerParamsIdx >= callerParamsCount)
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' in '{path}' field 'exec.items[{idx}].callerParamsIdx' references missing exec.callerParams[{rawCallerParamsIdx}].");
                }

                if (kind == ExecItemKind.EffectClip &&
                    rawCallerParamsIdx < callerParamSetsWithDurationTicks.Length &&
                    callerParamSetsWithDurationTicks[rawCallerParamsIdx])
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' in '{path}' field 'exec.items[{idx}].durationTicks' duplicates exec.callerParams[{rawCallerParamsIdx}] key '_ep.durationTicks'.");
                }

                if (kind == ExecItemKind.EffectClip &&
                    rawCallerParamsIdx < callerParamSetParamCounts.Length &&
                    callerParamSetParamCounts[rawCallerParamsIdx] >= EffectConfigParams.MAX_PARAMS)
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' in '{path}' field 'exec.items[{idx}].durationTicks' cannot be added because exec.callerParams[{rawCallerParamsIdx}] is at fixed capacity {EffectConfigParams.MAX_PARAMS}.");
                }

                callerParamsIdx = (byte)rawCallerParamsIdx;
            }

            int payloadA = itemObj["payloadA"]?.GetValue<int>() ?? 0;

            if (kind == ExecItemKind.InputGate && itemObj["payloadA"] is null)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field 'exec.items[{idx}].payloadA' is required for InputGate.");
            }

            if ((kind == ExecItemKind.EffectClip || kind == ExecItemKind.EffectSignal) &&
                itemObj["dispatchTarget"] is JsonValue dispatchTargetNode)
            {
                string rawDispatchTarget = dispatchTargetNode.GetValue<string>();
                payloadA = (int)ParseExecEffectDispatchTarget(rawDispatchTarget, id, idx, path);
            }

            spec.SetItem(idx, kind, tick, durationTicks, clockId, tagId, templateId, callerParamsIdx, payloadA);
        }

        private static ExecEffectDispatchTarget ParseExecEffectDispatchTarget(string rawValue, string abilityId, int itemIndex, string path)
        {
            if (Enum.TryParse(rawValue, ignoreCase: true, out ExecEffectDispatchTarget parsed))
            {
                return parsed;
            }

            throw new InvalidOperationException(
                $"Ability '{abilityId}' item[{itemIndex}] in '{path}' uses unknown dispatchTarget '{rawValue}'. " +
                "Valid values: Default, Source, Target, TargetContext.");
        }

        // ──────────────── CallerParamsPool ────────────────

        private static void CompileCallerParamsPool(
            JsonObject execObj,
            string id,
            string path,
            out AbilityExecCallerParamsPool pool,
            out bool hasPool,
            out bool[] callerParamSetsWithDurationTicks,
            out int[] callerParamSetParamCounts)
        {
            pool = default;
            hasPool = false;
            callerParamSetsWithDurationTicks = Array.Empty<bool>();
            callerParamSetParamCounts = Array.Empty<int>();

            if (execObj["callerParams"] is not JsonArray paramsArr) return;

            callerParamSetsWithDurationTicks = new bool[paramsArr.Count];
            callerParamSetParamCounts = new int[paramsArr.Count];
            for (int setIndex = 0; setIndex < paramsArr.Count; setIndex++)
            {
                if (paramsArr[setIndex] is not JsonObject setObj)
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' in '{path}' field 'exec.callerParams[{setIndex}]' must be an object.");
                }

                var cp = default(EffectConfigParams);
                int[] seenKeys = new int[EffectConfigParams.MAX_PARAMS];
                int seenKeyCount = 0;

                if (setObj["entries"] is JsonArray entriesArr)
                {
                    for (int entryIndex = 0; entryIndex < entriesArr.Count; entryIndex++)
                    {
                        if (entriesArr[entryIndex] is not JsonObject entryObj)
                        {
                            throw new InvalidOperationException(
                                $"Ability '{id}' in '{path}' field 'exec.callerParams[{setIndex}].entries[{entryIndex}]' must be an object.");
                        }

                        string key = RequireNonEmptyString(entryObj["key"], $"exec.callerParams[{setIndex}].entries[{entryIndex}].key", id, path);
                        int keyId = ConfigKeyRegistry.Register(key);
                        for (int seenIndex = 0; seenIndex < seenKeyCount; seenIndex++)
                        {
                            if (seenKeys[seenIndex] != keyId)
                            {
                                continue;
                            }

                            throw new InvalidOperationException(
                                $"Ability '{id}' in '{path}' field 'exec.callerParams[{setIndex}].entries[{entryIndex}].key' duplicates " +
                                $"exec.callerParams[{setIndex}].entries[{seenIndex}].key '{key}'.");
                        }

                        if (seenKeyCount >= seenKeys.Length)
                        {
                            throw new InvalidOperationException(
                                $"Ability '{id}' in '{path}' field 'exec.callerParams[{setIndex}].entries' exceeded max {EffectConfigParams.MAX_PARAMS} params.");
                        }

                        seenKeys[seenKeyCount++] = keyId;
                        if (string.Equals(key, "_ep.durationTicks", StringComparison.OrdinalIgnoreCase))
                        {
                            callerParamSetsWithDurationTicks[setIndex] = true;
                        }

                        CompileCallerParamEntry(
                            ref cp,
                            keyId,
                            entryObj,
                            id,
                            path,
                            $"exec.callerParams[{setIndex}].entries[{entryIndex}]",
                            $"exec.callerParams[{setIndex}].entries");
                    }
                }

                callerParamSetParamCounts[setIndex] = cp.Count;
                if (!pool.TryAdd(in cp))
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' exceeded max {AbilityExecCallerParamsPool.MAX_SETS} callerParams sets.");
                }
                hasPool = true;
            }
        }

        // ──────────────── Toggle / Targeting ────────────────

        private static void CompileCallerParamEntry(
            ref EffectConfigParams callerParams,
            int keyId,
            JsonObject entryObj,
            string id,
            string path,
            string fieldPath,
            string capacityFieldPath)
        {
            JsonNode? valueNode = entryObj["value"];
            if (valueNode == null)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field '{fieldPath}.value' is required.");
            }

            string type = entryObj["type"] is JsonNode typeNode
                ? RequireNonEmptyString(typeNode, $"{fieldPath}.type", id, path)
                : "Float";

            bool added = type switch
            {
                "Float" => callerParams.TryAddFloat(keyId, ParseFloatValue(valueNode, id, path, $"{fieldPath}.value")),
                "Int" => callerParams.TryAddInt(keyId, ParseIntValue(valueNode, id, path, $"{fieldPath}.value")),
                "EffectTemplate" => callerParams.TryAddEffectTemplateId(
                    keyId,
                    ResolveEffectTemplateParam(valueNode, id, path, $"{fieldPath}.value")),
                "Attribute" => callerParams.TryAddAttributeId(
                    keyId,
                    ResolveAttributeParam(valueNode, id, path, $"{fieldPath}.value")),
                _ => throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field '{fieldPath}.type' has unknown caller param type '{type}'. Valid values: Float, Int, EffectTemplate, Attribute."),
            };

            if (!added)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field '{capacityFieldPath}' exceeded max {EffectConfigParams.MAX_PARAMS} params.");
            }
        }

        private static float ParseFloatValue(JsonNode node, string id, string path, string fieldPath)
        {
            try
            {
                JsonElement json = node.GetValue<JsonElement>();
                return json.ValueKind == JsonValueKind.Number
                    ? json.GetSingle()
                    : float.Parse(json.GetString() ?? string.Empty, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field '{fieldPath}' must be a float value.", ex);
            }
        }

        private static int ParseIntValue(JsonNode node, string id, string path, string fieldPath)
        {
            try
            {
                JsonElement json = node.GetValue<JsonElement>();
                return json.ValueKind == JsonValueKind.Number
                    ? json.GetInt32()
                    : int.Parse(json.GetString() ?? string.Empty, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field '{fieldPath}' must be an integer value.", ex);
            }
        }

        private static int ResolveEffectTemplateParam(JsonNode node, string id, string path, string fieldPath)
        {
            string templateName = RequireNonEmptyString(node, fieldPath, id, path);
            int templateId = EffectTemplateIdRegistry.GetId(templateName);
            if (templateId <= 0)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field '{fieldPath}' references unknown effect template '{templateName}'.");
            }

            return templateId;
        }

        private static int ResolveAttributeParam(JsonNode node, string id, string path, string fieldPath)
        {
            string attributeName = RequireNonEmptyString(node, fieldPath, id, path);
            return AttributeRegistry.Register(attributeName);
        }

        private static AbilityToggleSpec CompileToggleSpec(JsonObject toggleObj, string id, string path)
        {
            if (toggleObj.ContainsKey("tag"))
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' uses toggleSpec field 'tag'. Use 'toggleTag'.");
            }

            string toggleTag = RequireNonEmptyString(toggleObj["toggleTag"], "toggleSpec.toggleTag", id, path);

            var toggleSpec = new AbilityToggleSpec
            {
                ToggleTagId = TagRegistry.Register(toggleTag)
            };

            if (toggleObj["activeEffects"] is JsonArray activeEffects)
            {
                if (activeEffects.Count > MaxToggleActiveEffects)
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' field 'toggleSpec.activeEffects' in '{path}' contains {activeEffects.Count} effects, max {MaxToggleActiveEffects}.");
                }

                int activeCount = 0;
                for (int i = 0; i < activeEffects.Count; i++)
                {
                    string effectId = RequireNonEmptyString(activeEffects[i], $"toggleSpec.activeEffects[{i}]", id, path);

                    int templateId = EffectTemplateIdRegistry.GetId(effectId);
                    if (templateId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Ability '{id}' in '{path}' toggleSpec references unknown effect template '{effectId}'.");
                    }

                    unsafe
                    {
                        toggleSpec.ActiveEffectTemplateIds[activeCount] = templateId;
                    }

                    activeCount++;
                }

                toggleSpec.ActiveEffectCount = activeCount;
            }

            if (toggleObj["deactivateExec"] is JsonObject deactivateExec)
            {
                toggleSpec.DeactivateExecSpec = CompileExecSpec(
                    deactivateExec,
                    id,
                    path,
                    hasCallerParamsPool: false,
                    callerParamsCount: 0,
                    callerParamSetsWithDurationTicks: Array.Empty<bool>(),
                    callerParamSetParamCounts: Array.Empty<int>());
            }

            return toggleSpec;
        }

        private static AbilityTargetingConfig CompileTargeting(JsonObject targetingObj, string id, string path)
        {
            if (targetingObj["range"] != null)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field 'targeting.range' is deprecated; use 'targeting.castRangeCm'.");
            }

            if (targetingObj["castRangeCm"] == null)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field 'targeting.castRangeCm' is required when 'targeting' is present.");
            }

            if (targetingObj["impactEffect"] == null)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field 'targeting.impactEffect' is required when 'targeting' is present.");
            }

            string impactEffect = targetingObj["impactEffect"]!.GetValue<string>();
            if (string.IsNullOrWhiteSpace(impactEffect))
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field 'targeting.impactEffect' must be a non-empty effect template id.");
            }

            int impactEffectTemplateId = EffectTemplateIdRegistry.GetId(impactEffect);
            if (impactEffectTemplateId <= 0)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field 'targeting.impactEffect' references unknown effect template '{impactEffect}'.");
            }

            var targeting = new AbilityTargetingConfig
            {
                CastRangeCm = targetingObj["castRangeCm"]!.GetValue<float>(),
                ImpactEffectTemplateId = impactEffectTemplateId
            };

            if (targeting.CastRangeCm < 0f)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field 'targeting.castRangeCm' must be non-negative.");
            }

            return targeting;
        }

        private static void RejectRemovedAimVisualFields(JsonNode? node, string id, string path, string currentPath)
        {
            if (node is JsonObject obj)
            {
                foreach ((string key, JsonNode? child) in obj)
                {
                    string fieldPath = string.IsNullOrEmpty(currentPath) ? key : $"{currentPath}.{key}";
                    for (int i = 0; i < RemovedAimVisualFieldNames.Length; i++)
                    {
                        if (string.Equals(key, RemovedAimVisualFieldNames[i], StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Ability '{id}' in '{path}' field '{fieldPath}' is removed; aim visuals belong in Presenter event-condition-action rules.");
                        }
                    }

                    RejectRemovedAimVisualFields(child, id, path, fieldPath);
                }

                return;
            }

            if (node is not JsonArray arr)
            {
                return;
            }

            for (int i = 0; i < arr.Count; i++)
            {
                RejectRemovedAimVisualFields(arr[i], id, path, $"{currentPath}[{i}]");
            }
        }

        private static AbilityPresentationConfig? CompilePresentation(JsonObject presentationObj, string id, string path)
        {
            var displayName = presentationObj["displayName"]?.GetValue<string>() ?? string.Empty;
            var iconGlyph = presentationObj["iconGlyph"]?.GetValue<string>() ?? string.Empty;
            var accentColorHex = presentationObj["accentColor"]?.GetValue<string>() ?? string.Empty;
            var hintText = presentationObj["hintText"]?.GetValue<string>() ?? string.Empty;
            var displayNameToken = presentationObj["displayNameToken"]?.GetValue<string>() ?? string.Empty;
            var hintTextToken = presentationObj["hintTextToken"]?.GetValue<string>() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(displayName) &&
                string.IsNullOrWhiteSpace(iconGlyph) &&
                string.IsNullOrWhiteSpace(accentColorHex) &&
                string.IsNullOrWhiteSpace(hintText) &&
                string.IsNullOrWhiteSpace(displayNameToken) &&
                string.IsNullOrWhiteSpace(hintTextToken) &&
                presentationObj["modeIconGlyphs"] is not JsonObject &&
                presentationObj["modeHints"] is not JsonObject &&
                presentationObj["modeHintTokens"] is not JsonObject)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(displayNameToken))
            {
                displayNameToken = RequireNonEmptyString(
                    presentationObj["displayNameToken"],
                    "presentation.displayNameToken",
                    id,
                    path);
            }

            if (!string.IsNullOrWhiteSpace(hintTextToken))
            {
                hintTextToken = RequireNonEmptyString(
                    presentationObj["hintTextToken"],
                    "presentation.hintTextToken",
                    id,
                    path);
            }

            var config = new AbilityPresentationConfig
            {
                DisplayName = displayName,
                IconGlyph = iconGlyph,
                AccentColorHex = accentColorHex,
                HintText = hintText,
                DisplayNameToken = displayNameToken,
                HintTextToken = hintTextToken
            };

            if (presentationObj["modeIconGlyphs"] is JsonObject modeIconGlyphs)
            {
                foreach ((string? modeKey, JsonNode? valueNode) in modeIconGlyphs)
                {
                    string modeName = RequireKnownInteractionMode(modeKey, $"presentation.modeIconGlyphs.{modeKey}", id, path);
                    string glyph = RequireNonEmptyString(valueNode, $"presentation.modeIconGlyphs.{modeKey}", id, path);
                    config.ModeIconGlyphOverrides[modeName] = glyph;
                }
            }

            if (presentationObj["modeHints"] is JsonObject modeHints)
            {
                foreach ((string? modeKey, JsonNode? valueNode) in modeHints)
                {
                    string modeName = RequireKnownInteractionMode(modeKey, $"presentation.modeHints.{modeKey}", id, path);
                    string hint = RequireNonEmptyString(valueNode, $"presentation.modeHints.{modeKey}", id, path);
                    config.ModeHintOverrides[modeName] = hint;
                }
            }

            if (presentationObj["modeHintTokens"] is JsonObject modeHintTokens)
            {
                foreach ((string? modeKey, JsonNode? valueNode) in modeHintTokens)
                {
                    string modeName = RequireKnownInteractionMode(modeKey, $"presentation.modeHintTokens.{modeKey}", id, path);
                    string token = RequireNonEmptyString(valueNode, $"presentation.modeHintTokens.{modeKey}", id, path);
                    config.ModeHintTokenOverrides[modeName] = token;
                }
            }

            return config;
        }

        private static string RequireNonEmptyString(JsonNode? node, string fieldPath, string id, string path)
        {
            if (node is null)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field '{fieldPath}' is required and must be a non-empty string.");
            }

            string value = node.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field '{fieldPath}' must be a non-empty string.");
            }

            return value.Trim();
        }

        private static string RequireKnownInteractionMode(string? modeKey, string fieldPath, string id, string path)
        {
            if (string.IsNullOrWhiteSpace(modeKey))
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field '{fieldPath}' must use a non-empty interaction mode key.");
            }

            if (!Enum.TryParse(modeKey, ignoreCase: true, out CastModeType parsed))
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field '{fieldPath}' uses unknown interaction mode '{modeKey}'.");
            }

            return parsed.ToString();
        }

        private static AbilityInputBindingOverride CompileInputBindingOverride(JsonObject inputObj, string id, string path)
        {
            var result = new AbilityInputBindingOverride();
            bool hasAny = false;

            if (inputObj["trigger"] is JsonValue triggerNode)
            {
                string rawTrigger = triggerNode.GetValue<string>();
                if (!Enum.TryParse(rawTrigger, ignoreCase: true, out InputTriggerType trigger))
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' in '{path}' input.trigger uses unknown value '{rawTrigger}'.");
                }

                result.Trigger = trigger;
                result.HasTrigger = true;
                hasAny = true;
            }

            if (inputObj["heldPolicy"] is JsonValue heldPolicyNode)
            {
                string rawHeldPolicy = heldPolicyNode.GetValue<string>();
                if (!Enum.TryParse(rawHeldPolicy, ignoreCase: true, out HeldPolicy heldPolicy))
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' in '{path}' input.heldPolicy uses unknown value '{rawHeldPolicy}'.");
                }

                result.HeldPolicy = heldPolicy;
                result.HasHeldPolicy = true;
                hasAny = true;
            }

            if (inputObj["castModeOverride"] is JsonValue castModeNode)
            {
                string rawCastMode = castModeNode.GetValue<string>();
                if (!Enum.TryParse(rawCastMode, ignoreCase: true, out CastModeType castMode))
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' in '{path}' input.castModeOverride uses unknown value '{rawCastMode}'.");
                }

                result.CastModeOverride = castMode;
                result.HasCastModeOverride = true;
                hasAny = true;
            }

            if (inputObj["autoTargetPolicy"] is JsonValue autoTargetPolicyNode)
            {
                string rawAutoTargetPolicy = autoTargetPolicyNode.GetValue<string>();
                if (!Enum.TryParse(rawAutoTargetPolicy, ignoreCase: true, out AutoTargetPolicy autoTargetPolicy))
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' in '{path}' input.autoTargetPolicy uses unknown value '{rawAutoTargetPolicy}'.");
                }

                result.AutoTargetPolicy = autoTargetPolicy;
                result.HasAutoTargetPolicy = true;
                hasAny = true;
            }

            if (inputObj["autoTargetRangeCm"] is JsonValue autoTargetRangeNode)
            {
                result.AutoTargetRangeCm = autoTargetRangeNode.GetValue<int>();
                result.HasAutoTargetRangeCm = true;
                hasAny = true;
            }

            if (!hasAny)
            {
                throw new InvalidOperationException($"Ability '{id}' in '{path}' input must declare at least one override field.");
            }

            return result;
        }

        // ──────────────── Parsing helpers ────────────────

        private static GasClockId ParseClockId(string str)
        {
            return str switch
            {
                "FixedFrame" => GasClockId.FixedFrame,
                "Step" => GasClockId.Step,
                "EntityLocal" => GasClockId.EntityLocal,
                "Turn" => throw new InvalidOperationException("GasClockId 'Turn': use Step for turn durations or EntityLocal for entity-scoped logic time."),
                _ => throw new InvalidOperationException($"Unknown GasClockId '{str}'. Valid values: FixedFrame, Step, EntityLocal."),
            };
        }

        private static ExecItemKind ParseItemKind(string str)
        {
            return str switch
            {
                "EffectClip" => ExecItemKind.EffectClip,
                "TagClip" => ExecItemKind.TagClip,
                "TagClipTarget" => ExecItemKind.TagClipTarget,
                "EffectSignal" => ExecItemKind.EffectSignal,
                "EventSignal" => ExecItemKind.EventSignal,
                "TagSignal" => ExecItemKind.TagSignal,
                "TagSignalTarget" => ExecItemKind.TagSignalTarget,
                "InputGate" => ExecItemKind.InputGate,
                "EventGate" => ExecItemKind.EventGate,
                "TargetCollectionGate" => ExecItemKind.TargetCollectionGate,
                "End" => ExecItemKind.End,
                _ => throw new InvalidOperationException($"Unknown ExecItemKind '{str}'. Valid values: EffectClip, TagClip, TagClipTarget, EffectSignal, EventSignal, TagSignal, TagSignalTarget, InputGate, EventGate, TargetCollectionGate, End."),
            };
        }
    }
}
