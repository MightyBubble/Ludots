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
    /// JSON format: array of ability objects with "id", "exec", "onActivateEffects", "blockTags" etc.
    /// </summary>
    public sealed class AbilityExecLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly AbilityDefinitionRegistry _registry;
        private const int MaxToggleActiveEffects = 4;

        public AbilityExecLoader(ConfigPipeline pipeline, AbilityDefinitionRegistry registry)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// Load abilities from the config pipeline and register them.
        /// </summary>
        public void Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/abilities.json")
        {
            _registry.Clear();
            AbilityIdRegistry.Clear();

            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var mergedEntries = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
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

            // ── exec block ──
            if (obj["exec"] is not JsonObject execObj)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' field 'exec' is required in '{path}'.");
            }

            def.ExecSpec = CompileExecSpec(execObj, id, path);
            CompileCallerParamsPool(execObj, id, path, out var pool, out bool hasPool);
            def.ExecCallerParamsPool = pool;
            def.HasExecCallerParamsPool = hasPool;

            // ── onActivateEffects ──
            if (obj["onActivateEffects"] is JsonArray effectArr)
            {
                var onActivate = default(AbilityOnActivateEffects);
                for (int effectIndex = 0; effectIndex < effectArr.Count; effectIndex++)
                {
                    var item = effectArr[effectIndex];
                    string effectName = item?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(effectName))
                    {
                        throw new InvalidOperationException(
                            $"Ability '{id}' field 'onActivateEffects[{effectIndex}]' in '{path}' must be a non-empty effect template key.");
                    }

                    int tid = EffectTemplateIdRegistry.GetId(effectName);
                    if (tid <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Ability '{id}' field 'onActivateEffects[{effectIndex}]' in '{path}' references unknown effect template '{effectName}'.");
                    }

                    onActivate.Add(tid);
                }
                def.HasOnActivateEffects = onActivate.Count > 0;
                def.OnActivateEffects = onActivate;
            }

            if (obj["cooldown"] is JsonObject cooldownObj)
            {
                def.Cooldown = CompileCooldown(cooldownObj, id, path);
                def.HasCooldown = def.Cooldown.CooldownValueAttributeId > 0 || def.Cooldown.CooldownTagId > 0;
            }

            // ── blockTags ──
            if (obj["blockTags"] is JsonObject blockObj)
            {
                var blockTags = default(AbilityActivationBlockTags);
                if (blockObj["requiredAll"] is JsonArray reqArr)
                {
                    for (int tagIndex = 0; tagIndex < reqArr.Count; tagIndex++)
                    {
                        string tag = RequireString(reqArr[tagIndex], id, path, $"blockTags.requiredAll[{tagIndex}]");
                        blockTags.RequiredAll.AddTag(TagRegistry.Register(tag));
                    }
                }
                if (blockObj["blockedAny"] is JsonArray blkArr)
                {
                    for (int tagIndex = 0; tagIndex < blkArr.Count; tagIndex++)
                    {
                        string tag = RequireString(blkArr[tagIndex], id, path, $"blockTags.blockedAny[{tagIndex}]");
                        blockTags.BlockedAny.AddTag(TagRegistry.Register(tag));
                    }
                }
                def.HasActivationBlockTags = true;
                def.ActivationBlockTags = blockTags;
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

            // ── indicator ──
            if (obj["indicator"] is JsonObject indicatorObj)
            {
                def.Indicator = CompileIndicator(indicatorObj, id, path);
                def.HasIndicator = true;
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

        // ──────────────── ExecSpec ────────────────

        private static AbilityExecSpec CompileExecSpec(JsonObject execObj, string id, string path)
        {
            var spec = default(AbilityExecSpec);

            // clockId
            string clockStr = RequireString(execObj, "clockId", id, path, "exec.clockId");
            spec.ClockId = ParseClockId(clockStr);

            // interruptAny
            if (execObj["interruptAny"] is JsonArray intArr)
            {
                for (int tagIndex = 0; tagIndex < intArr.Count; tagIndex++)
                {
                    string tag = RequireString(intArr[tagIndex], id, path, $"exec.interruptAny[{tagIndex}]");
                    spec.InterruptAny.AddTag(TagRegistry.Register(tag));
                }
            }

            // items
            if (execObj["items"] is JsonArray itemsArr)
            {
                if (itemsArr.Count > AbilityExecSpec.MAX_ITEMS)
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' field 'exec.items' in '{path}' contains {itemsArr.Count} items, max {AbilityExecSpec.MAX_ITEMS}.");
                }

                int idx = 0;
                for (int sourceIndex = 0; sourceIndex < itemsArr.Count; sourceIndex++)
                {
                    if (itemsArr[sourceIndex] is not JsonObject itemObj)
                    {
                        throw new InvalidOperationException(
                            $"Ability '{id}' field 'exec.items[{sourceIndex}]' in '{path}' must be an object.");
                    }

                    CompileItem(itemObj, ref spec, idx, sourceIndex, id, path);
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

        private static AbilityCooldown CompileCooldown(JsonObject cooldownObj, string id, string path)
        {
            var cooldown = new AbilityCooldown();

            string attrName = cooldownObj["valueAttribute"]?.GetValue<string>()
                           ?? cooldownObj["cooldownValueAttribute"]?.GetValue<string>()
                           ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(attrName))
            {
                int attrId = AttributeRegistry.GetId(attrName);
                if (attrId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' in '{path}' cooldown.valueAttribute references unknown attribute '{attrName}'.");
                }

                cooldown.CooldownValueAttributeId = attrId;
            }

            string tagName = cooldownObj["tag"]?.GetValue<string>()
                          ?? cooldownObj["cooldownTag"]?.GetValue<string>()
                          ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(tagName))
            {
                cooldown.CooldownTagId = TagRegistry.Register(tagName);
            }

            if (cooldown.CooldownValueAttributeId <= 0 && cooldown.CooldownTagId <= 0)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' cooldown must declare valueAttribute or tag.");
            }

            return cooldown;
        }

        private static void CompileItem(JsonObject itemObj, ref AbilityExecSpec spec, int idx, int sourceIndex, string id, string path)
        {
            string itemPath = $"exec.items[{sourceIndex}]";
            string kindStr = RequireString(itemObj, "kind", id, path, $"{itemPath}.kind");
            var kind = ParseItemKind(kindStr);
            int tick = RequireInt(itemObj, "tick", id, path, $"{itemPath}.tick");
            int durationTicks = TryGetInt(itemObj, "duration", out int durationValue) ? durationValue : 0;

            GasClockId clockId = default;
            string clockStr = itemObj["clock"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(clockStr)) clockId = ParseClockId(clockStr);

            int tagId = 0;
            string tagStr = itemObj["tag"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(tagStr)) tagId = TagRegistry.Register(tagStr);

            int templateId = 0;
            string templateStr = itemObj["template"]?.GetValue<string>();
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
                callerParamsIdx = (byte)cpNode.GetValue<int>();
            }

            int payloadA = TryGetInt(itemObj, "payloadA", out int payloadValue) ? payloadValue : 0;
            bool hasPayloadA = itemObj["payloadA"] is JsonNode;

            if ((kind == ExecItemKind.EffectClip || kind == ExecItemKind.EffectSignal) &&
                itemObj["dispatchTarget"] is JsonValue dispatchTargetNode)
            {
                string rawDispatchTarget = dispatchTargetNode.GetValue<string>();
                payloadA = (int)ParseExecEffectDispatchTarget(rawDispatchTarget, id, idx, path);
            }

            // For GraphSignal, "graph" field maps to payloadA via GraphIdRegistry
            if (kind == ExecItemKind.GraphSignal)
            {
                string graphName = RequireString(itemObj, "graph", id, path, $"{itemPath}.graph");
                payloadA = GraphIdRegistry.GetId(graphName);
                if (payloadA <= 0)
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' field '{itemPath}.graph' in '{path}' references unknown graph '{graphName}'.");
                }
            }

            ValidateItemFields(kind, templateId, tagId, durationTicks, hasPayloadA, id, path, itemPath);

            spec.SetItem(idx, kind, tick, durationTicks, clockId, tagId, templateId, callerParamsIdx, payloadA);
        }

        private static void ValidateItemFields(
            ExecItemKind kind,
            int templateId,
            int tagId,
            int durationTicks,
            bool hasPayloadA,
            string id,
            string path,
            string itemPath)
        {
            switch (kind)
            {
                case ExecItemKind.EffectClip:
                    if (templateId <= 0) throw RequiredItemField(id, path, itemPath, "template", kind);
                    if (durationTicks <= 0) throw RequiredItemField(id, path, itemPath, "duration", kind);
                    break;
                case ExecItemKind.TagClip:
                case ExecItemKind.TagClipTarget:
                    if (tagId <= 0) throw RequiredItemField(id, path, itemPath, "tag", kind);
                    if (durationTicks <= 0) throw RequiredItemField(id, path, itemPath, "duration", kind);
                    break;
                case ExecItemKind.EffectSignal:
                    if (templateId <= 0) throw RequiredItemField(id, path, itemPath, "template", kind);
                    break;
                case ExecItemKind.EventSignal:
                case ExecItemKind.TagSignal:
                case ExecItemKind.TagSignalTarget:
                    if (tagId <= 0) throw RequiredItemField(id, path, itemPath, "tag", kind);
                    break;
                case ExecItemKind.InputGate:
                case ExecItemKind.SelectionGate:
                    if (tagId <= 0) throw RequiredItemField(id, path, itemPath, "tag", kind);
                    if (!hasPayloadA) throw RequiredItemField(id, path, itemPath, "payloadA", kind);
                    break;
                case ExecItemKind.EventGate:
                    if (tagId <= 0 && !hasPayloadA)
                    {
                        throw new InvalidOperationException(
                            $"Ability '{id}' field '{itemPath}' in '{path}' requires either 'tag' or 'payloadA' for item kind '{kind}'.");
                    }
                    break;
            }
        }

        private static InvalidOperationException RequiredItemField(
            string id,
            string path,
            string itemPath,
            string fieldName,
            ExecItemKind kind)
        {
            return new InvalidOperationException(
                $"Ability '{id}' field '{itemPath}.{fieldName}' in '{path}' is required for item kind '{kind}'.");
        }

        private static ExecEffectDispatchTarget ParseExecEffectDispatchTarget(string rawValue, string abilityId, int itemIndex, string path)
        {
            if (Enum.TryParse(rawValue, ignoreCase: true, out ExecEffectDispatchTarget parsed))
            {
                return parsed;
            }

            throw new InvalidOperationException(
                $"Ability '{abilityId}' item[{itemIndex}] in '{path}' uses unsupported dispatchTarget '{rawValue}'. " +
                "Supported values: Default, Source, Target, TargetContext.");
        }

        // ──────────────── CallerParamsPool ────────────────

        private static void CompileCallerParamsPool(JsonObject execObj, string id, string path,
            out AbilityExecCallerParamsPool pool, out bool hasPool)
        {
            pool = default;
            hasPool = false;

            if (execObj["callerParams"] is not JsonArray paramsArr) return;

            for (int setIndex = 0; setIndex < paramsArr.Count; setIndex++)
            {
                if (paramsArr[setIndex] is not JsonObject setObj)
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' field 'exec.callerParams[{setIndex}]' in '{path}' must be an object.");
                }

                var cp = default(EffectConfigParams);

                if (setObj["entries"] is not JsonArray entriesArr)
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' field 'exec.callerParams[{setIndex}].entries' in '{path}' must be a non-empty array.");
                }

                if (entriesArr.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' field 'exec.callerParams[{setIndex}].entries' in '{path}' must be a non-empty array.");
                }

                for (int entryIndex = 0; entryIndex < entriesArr.Count; entryIndex++)
                {
                    if (entriesArr[entryIndex] is not JsonObject entryObj)
                    {
                        throw new InvalidOperationException(
                            $"Ability '{id}' field 'exec.callerParams[{setIndex}].entries[{entryIndex}]' in '{path}' must be an object.");
                    }

                    string key = RequireString(entryObj, "key", id, path, $"exec.callerParams[{setIndex}].entries[{entryIndex}].key");
                    int keyId = ConfigKeyRegistry.Register(key);

                    if (entryObj["value"] is not JsonNode valNode)
                    {
                        throw new InvalidOperationException(
                            $"Ability '{id}' field 'exec.callerParams[{setIndex}].entries[{entryIndex}].value' is required in '{path}'.");
                    }

                    float val = valNode.GetValue<JsonElement>().ValueKind == JsonValueKind.Number
                        ? valNode.GetValue<float>()
                        : float.Parse(valNode.GetValue<string>(), CultureInfo.InvariantCulture);
                    cp.TryAddFloat(keyId, val);
                }

                if (!pool.TryAdd(in cp))
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' exceeded max {AbilityExecCallerParamsPool.MAX_SETS} callerParams sets.");
                }
                hasPool = true;
            }
        }

        // ──────────────── Toggle / Indicator ────────────────

        private static AbilityToggleSpec CompileToggleSpec(JsonObject toggleObj, string id, string path)
        {
            string toggleTag = toggleObj["toggleTag"]?.GetValue<string>()
                ?? toggleObj["tag"]?.GetValue<string>()
                ?? string.Empty;
            if (string.IsNullOrWhiteSpace(toggleTag))
            {
                throw new InvalidOperationException($"Ability '{id}' in '{path}' toggleSpec requires 'toggleTag'.");
            }

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
                for (int effectIndex = 0; effectIndex < activeEffects.Count; effectIndex++)
                {
                    string effectId = RequireString(activeEffects[effectIndex], id, path, $"toggleSpec.activeEffects[{effectIndex}]");

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
                toggleSpec.DeactivateExecSpec = CompileExecSpec(deactivateExec, id, path);
            }

            return toggleSpec;
        }

        private static AbilityIndicatorConfig CompileIndicator(JsonObject indicatorObj, string id, string path)
        {
            string shapeValue = RequireString(indicatorObj, "shape", id, path, "indicator.shape");
            TargetShape shape = ParseTargetShape(shapeValue, id, path);
            bool showRangeCircle = RequireBool(indicatorObj, "showRangeCircle", id, path, "indicator.showRangeCircle");
            if (!showRangeCircle)
            {
                RequireAbsent(indicatorObj, "rangeCircleColor", id, path, "indicator.rangeCircleColor");
            }

            bool requiresRange = showRangeCircle || ShapeRequiresRange(shape);
            bool requiresRadius = ShapeRequiresRadius(shape);
            float range = requiresRange
                ? RequirePositiveFloat(indicatorObj, "range", id, path, "indicator.range")
                : ReadOptionalPositiveFloat(indicatorObj, "range", id, path, "indicator.range");
            float radius = requiresRadius
                ? RequirePositiveFloat(indicatorObj, "radius", id, path, "indicator.radius")
                : ReadOptionalPositiveFloat(indicatorObj, "radius", id, path, "indicator.radius");
            float innerRadius = TryGetFloat(indicatorObj, "innerRadius", out float innerRadiusValue) ? innerRadiusValue : 0f;
            float angle = TryGetFloat(indicatorObj, "angle", out float angleValue) ? angleValue : 0f;

            var indicator = new AbilityIndicatorConfig
            {
                Shape = shape,
                Range = range,
                Radius = radius,
                InnerRadius = innerRadius,
                Angle = angle,
                ValidColor = ParseColor(
                    indicatorObj["validColor"],
                    new System.Numerics.Vector4(0.20f, 0.85f, 0.45f, 0.35f),
                    id,
                    path,
                    "indicator.validColor"),
                InvalidColor = ParseColor(
                    indicatorObj["invalidColor"],
                    new System.Numerics.Vector4(0.95f, 0.30f, 0.25f, 0.35f),
                    id,
                    path,
                    "indicator.invalidColor"),
                RangeCircleColor = showRangeCircle ? ParseColor(indicatorObj["rangeCircleColor"], id, path, "indicator.rangeCircleColor") : default,
                ShowRangeCircle = showRangeCircle
            };

            if (indicatorObj["preview"] is JsonObject previewObj)
            {
                indicator.Preview = new AbilityIndicatorPreviewConfig
                {
                    PerformerId = previewObj["performerId"]?.GetValue<string>() ?? string.Empty,
                    ScaleX = previewObj["scaleX"]?.GetValue<float>() ?? 0f,
                    ScaleY = previewObj["scaleY"]?.GetValue<float>() ?? 0f,
                    ScaleZ = previewObj["scaleZ"]?.GetValue<float>() ?? 0f,
                    OffsetY = previewObj["offsetY"]?.GetValue<float>() ?? 0f,
                };
            }

            if (indicatorObj["angleDeg"] is JsonNode angleDegNode)
            {
                indicator.Angle = MathF.PI * angleDegNode.GetValue<float>() / 180f;
            }

            ValidateIndicatorShapeSemantics(indicator, id, path);
            return indicator;
        }

        private static void ValidateIndicatorShapeSemantics(AbilityIndicatorConfig indicator, string id, string path)
        {
            if (indicator.Shape == TargetShape.Single && indicator.Radius > 0f)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' field 'indicator.radius' in '{path}' is not valid for shape 'Single'.");
            }

            if (indicator.InnerRadius < 0f)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' field 'indicator.innerRadius' in '{path}' must be non-negative.");
            }

            if (indicator.Shape == TargetShape.Ring && indicator.InnerRadius <= 0f)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' field 'indicator.innerRadius' in '{path}' is required and must be > 0 for shape 'Ring'.");
            }

            if (indicator.Shape == TargetShape.Ring && indicator.InnerRadius >= indicator.Radius)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' field 'indicator.innerRadius' in '{path}' must be less than indicator.radius for shape 'Ring'.");
            }

            if (indicator.Shape == TargetShape.Cone && indicator.Angle <= 0f)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' field 'indicator.angleDeg' in '{path}' is required for shape 'Cone'.");
            }
        }

        private static bool ShapeRequiresRange(TargetShape shape)
        {
            return shape is TargetShape.Cone or TargetShape.Line or TargetShape.Rectangle;
        }

        private static bool ShapeRequiresRadius(TargetShape shape)
        {
            return shape is TargetShape.Circle or TargetShape.Cone or TargetShape.Line or TargetShape.Ring or TargetShape.Rectangle;
        }

        private static AbilityPresentationConfig? CompilePresentation(JsonObject presentationObj, string id, string path)
        {
            var displayName = presentationObj["displayName"]?.GetValue<string>() ?? string.Empty;
            var iconGlyph = presentationObj["iconGlyph"]?.GetValue<string>() ?? string.Empty;
            var accentColorHex = presentationObj["accentColor"]?.GetValue<string>() ?? string.Empty;
            var hintText = presentationObj["hintText"]?.GetValue<string>() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(displayName) &&
                string.IsNullOrWhiteSpace(iconGlyph) &&
                string.IsNullOrWhiteSpace(accentColorHex) &&
                string.IsNullOrWhiteSpace(hintText) &&
                presentationObj["modeIconGlyphs"] is not JsonObject &&
                presentationObj["modeHints"] is not JsonObject)
            {
                return null;
            }

            var config = new AbilityPresentationConfig
            {
                DisplayName = displayName,
                IconGlyph = iconGlyph,
                AccentColorHex = accentColorHex,
                HintText = hintText
            };

            if (presentationObj["modeIconGlyphs"] is JsonObject modeIconGlyphs)
            {
                foreach ((string? modeKey, JsonNode? valueNode) in modeIconGlyphs)
                {
                    string canonicalModeKey = RequireInteractionModeKey(modeKey, id, path, "presentation.modeIconGlyphs");
                    string glyph = RequireString(valueNode, id, path, $"presentation.modeIconGlyphs.{canonicalModeKey}");
                    config.ModeIconGlyphOverrides[canonicalModeKey] = glyph;
                }
            }

            if (presentationObj["modeHints"] is JsonObject modeHints)
            {
                foreach ((string? modeKey, JsonNode? valueNode) in modeHints)
                {
                    string canonicalModeKey = RequireInteractionModeKey(modeKey, id, path, "presentation.modeHints");
                    string hint = RequireString(valueNode, id, path, $"presentation.modeHints.{canonicalModeKey}");
                    config.ModeHintOverrides[canonicalModeKey] = hint;
                }
            }

            return config;
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
                        $"Ability '{id}' in '{path}' input.trigger uses unsupported value '{rawTrigger}'.");
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
                        $"Ability '{id}' in '{path}' input.heldPolicy uses unsupported value '{rawHeldPolicy}'.");
                }

                result.HeldPolicy = heldPolicy;
                result.HasHeldPolicy = true;
                hasAny = true;
            }

            if (inputObj["castModeOverride"] is JsonValue castModeNode)
            {
                string rawCastMode = castModeNode.GetValue<string>();
                if (!Enum.TryParse(rawCastMode, ignoreCase: true, out InteractionModeType castMode))
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' in '{path}' input.castModeOverride uses unsupported value '{rawCastMode}'.");
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
                        $"Ability '{id}' in '{path}' input.autoTargetPolicy uses unsupported value '{rawAutoTargetPolicy}'.");
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

        private static TargetShape ParseTargetShape(string value, string id, string path)
        {
            return value switch
            {
                "Self" => TargetShape.Self,
                "Single" => TargetShape.Single,
                "Circle" => TargetShape.Circle,
                "Cone" => TargetShape.Cone,
                "Line" => TargetShape.Line,
                "Ring" => TargetShape.Ring,
                "Rectangle" => TargetShape.Rectangle,
                _ => throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' indicator uses unsupported shape '{value}'.")
            };
        }

        private static System.Numerics.Vector4 ParseColor(
            JsonNode? node,
            System.Numerics.Vector4 defaultValue,
            string id,
            string path,
            string fieldPath)
        {
            return node is null
                ? defaultValue
                : ParseColor(node, id, path, fieldPath);
        }

        private static System.Numerics.Vector4 ParseColor(JsonNode? node, string id, string path, string fieldPath)
        {
            if (node is JsonArray arr)
            {
                if (arr.Count != 4)
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' field '{fieldPath}' in '{path}' must contain exactly four numeric components.");
                }

                float r = arr[0]?.GetValue<float>() ?? throw RequiredField(id, path, $"{fieldPath}[0]");
                float g = arr[1]?.GetValue<float>() ?? throw RequiredField(id, path, $"{fieldPath}[1]");
                float b = arr[2]?.GetValue<float>() ?? throw RequiredField(id, path, $"{fieldPath}[2]");
                float a = arr[3]?.GetValue<float>() ?? throw RequiredField(id, path, $"{fieldPath}[3]");
                return new System.Numerics.Vector4(r, g, b, a);
            }

            string? hex = node?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(hex))
            {
                throw RequiredField(id, path, fieldPath);
            }

            hex = hex.Trim();
            if (hex.StartsWith('#'))
            {
                hex = hex[1..];
            }

            if (hex.Length != 6 && hex.Length != 8)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' field '{fieldPath}' in '{path}' must be #RRGGBB or #RRGGBBAA.");
            }

            if (!byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte rByte) ||
                !byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte gByte) ||
                !byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte bByte) ||
                (hex.Length == 8 && !byte.TryParse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)))
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' field '{fieldPath}' in '{path}' contains invalid hex color '{node.GetValue<string>()}'.");
            }

            byte aByte = hex.Length == 8 ? byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : (byte)255;

            return new System.Numerics.Vector4(
                rByte / 255f,
                gByte / 255f,
                bByte / 255f,
                aByte / 255f);
        }

        // ──────────────── Parsing helpers ────────────────

        private static GasClockId ParseClockId(string str)
        {
            return str switch
            {
                "FixedFrame" => GasClockId.FixedFrame,
                "Step" => GasClockId.Step,
                "EntityLocal" => GasClockId.EntityLocal,
                "Turn" => throw new InvalidOperationException("GasClockId 'Turn' has been removed. Use Step for turn durations or EntityLocal for entity-scoped logic time."),
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
                "GraphSignal" => ExecItemKind.GraphSignal,
                "TagSignal" => ExecItemKind.TagSignal,
                "TagSignalTarget" => ExecItemKind.TagSignalTarget,
                "InputGate" => ExecItemKind.InputGate,
                "EventGate" => ExecItemKind.EventGate,
                "SelectionGate" => ExecItemKind.SelectionGate,
                "End" => ExecItemKind.End,
                _ => throw new InvalidOperationException($"Unknown ExecItemKind '{str}'. Valid values: EffectClip, TagClip, TagClipTarget, EffectSignal, EventSignal, GraphSignal, TagSignal, TagSignalTarget, InputGate, EventGate, SelectionGate, End."),
            };
        }

        private static string RequireString(JsonObject obj, string propertyName, string abilityId, string path, string fieldPath)
        {
            if (obj[propertyName] is not JsonNode node)
            {
                throw RequiredField(abilityId, path, fieldPath);
            }

            string value = node.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw RequiredField(abilityId, path, fieldPath);
            }

            return value;
        }

        private static string RequireString(JsonNode? node, string abilityId, string path, string fieldPath)
        {
            if (node is not JsonValue valueNode ||
                !valueNode.TryGetValue<string>(out string? value) ||
                string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' field '{fieldPath}' in '{path}' must be a non-empty string.");
            }

            return value;
        }

        private static string RequireInteractionModeKey(string? modeKey, string abilityId, string path, string fieldPath)
        {
            if (string.IsNullOrWhiteSpace(modeKey))
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' field '{fieldPath}' in '{path}' must use non-empty interaction mode keys.");
            }

            if (!string.Equals(modeKey, modeKey.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' field '{fieldPath}.{modeKey}' in '{path}' must not contain leading or trailing whitespace.");
            }

            if (!Enum.TryParse(modeKey, ignoreCase: true, out InteractionModeType mode) ||
                !Enum.IsDefined(typeof(InteractionModeType), mode))
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' field '{fieldPath}.{modeKey}' in '{path}' references unknown interaction mode '{modeKey}'.");
            }

            return mode.ToString();
        }

        private static int RequireInt(JsonObject obj, string propertyName, string abilityId, string path, string fieldPath)
        {
            if (!TryGetInt(obj, propertyName, out int value))
            {
                throw RequiredField(abilityId, path, fieldPath);
            }

            return value;
        }

        private static bool TryGetInt(JsonObject obj, string propertyName, out int value)
        {
            value = 0;
            if (obj[propertyName] is not JsonNode node)
            {
                return false;
            }

            value = node.GetValue<int>();
            return true;
        }

        private static float RequireFloat(JsonObject obj, string propertyName, string abilityId, string path, string fieldPath)
        {
            if (!TryGetFloat(obj, propertyName, out float value))
            {
                throw RequiredField(abilityId, path, fieldPath);
            }

            return value;
        }

        private static float RequirePositiveFloat(JsonObject obj, string propertyName, string abilityId, string path, string fieldPath)
        {
            float value = RequireFloat(obj, propertyName, abilityId, path, fieldPath);
            if (value <= 0f)
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' field '{fieldPath}' in '{path}' must be > 0.");
            }

            return value;
        }

        private static float ReadOptionalPositiveFloat(JsonObject obj, string propertyName, string abilityId, string path, string fieldPath)
        {
            if (!TryGetFloat(obj, propertyName, out float value))
            {
                return 0f;
            }

            if (value <= 0f)
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' field '{fieldPath}' in '{path}' must be omitted or > 0.");
            }

            return value;
        }

        private static bool TryGetFloat(JsonObject obj, string propertyName, out float value)
        {
            value = 0f;
            if (obj[propertyName] is not JsonNode node)
            {
                return false;
            }

            value = node.GetValue<float>();
            return true;
        }

        private static bool RequireBool(JsonObject obj, string propertyName, string abilityId, string path, string fieldPath)
        {
            if (obj[propertyName] is not JsonNode node)
            {
                throw RequiredField(abilityId, path, fieldPath);
            }

            return node.GetValue<bool>();
        }

        private static bool TryGetBool(JsonObject obj, string propertyName, out bool value)
        {
            value = false;
            if (obj[propertyName] is not JsonNode node)
            {
                return false;
            }

            value = node.GetValue<bool>();
            return true;
        }

        private static void RequireAbsent(JsonObject obj, string propertyName, string abilityId, string path, string fieldPath)
        {
            if (obj[propertyName] is not null)
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' field '{fieldPath}' in '{path}' is only valid when indicator.showRangeCircle is true.");
            }
        }

        private static InvalidOperationException RequiredField(string abilityId, string path, string fieldPath)
        {
            return new InvalidOperationException(
                $"Ability '{abilityId}' field '{fieldPath}' is required in '{path}'.");
        }
    }
}
