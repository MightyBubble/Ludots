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
        private static readonly string[] RemovedAimVisualFieldNames =
        {
            "aimVisual",
            "areaPerformerId",
            "rangeCirclePerformerId",
            "previewPerformerId",
            "performerId",
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
            if (obj["exec"] is JsonObject execObj)
            {
                def.ExecSpec = CompileExecSpec(execObj, id, path);
                CompileCallerParamsPool(execObj, id, path, out var pool, out bool hasPool);
                def.ExecCallerParamsPool = pool;
                def.HasExecCallerParamsPool = hasPool;
            }

            // ── onActivateEffects ──
            if (obj["onActivateEffects"] is JsonArray effectArr)
            {
                var onActivate = default(AbilityOnActivateEffects);
                foreach (var item in effectArr)
                {
                    string effectName = item?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(effectName)) continue;
                    int tid = EffectTemplateIdRegistry.GetId(effectName);
                    if (tid > 0) onActivate.Add(tid);
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
                    foreach (var t in reqArr)
                    {
                        string tag = t?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(tag)) blockTags.RequiredAll.AddTag(TagRegistry.Register(tag));
                    }
                }
                if (blockObj["blockedAny"] is JsonArray blkArr)
                {
                    foreach (var t in blkArr)
                    {
                        string tag = t?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(tag)) blockTags.BlockedAny.AddTag(TagRegistry.Register(tag));
                    }
                }
                def.HasActivationBlockTags = true;
                def.ActivationBlockTags = blockTags;
            }

            // ── catalogTags (RFC-0065 DEC-14) ──
            if (obj["catalogTags"] is JsonArray catalogArr)
            {
                var catalogTags = default(GameplayTagContainer);
                bool hasCatalogTags = false;
                foreach (var t in catalogArr)
                {
                    string tag = t?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        throw new InvalidOperationException(
                            $"Ability '{id}' in '{path}' catalogTags entries must be non-empty strings.");
                    }

                    catalogTags.AddTag(TagRegistry.Register(tag));
                    hasCatalogTags = true;
                }

                def.CatalogTags = catalogTags;
                def.HasCatalogTags = hasCatalogTags;
            }

            // ── interactionContextProfile (RFC-0065 CTX-6) ──
            if (obj["interactionContextProfile"] != null)
            {
                string contextProfileId = obj["interactionContextProfile"]?.GetValue<string>();
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
            if (obj["indicator"] != null)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field 'indicator' is removed; use 'targeting.castRangeCm' and 'targeting.impactEffect'. Aim visuals belong in Performer rules.");
            }

            RejectRemovedAimVisualFields(obj, id, path, currentPath: string.Empty);

            if (obj["targeting"] is JsonObject targetingObj)
            {
                def.Targeting = CompileTargeting(targetingObj, id, path);
                def.HasTargeting = true;
            }

            if (obj["presentation"] is JsonObject presentationObj)
            {
                def.Presentation = CompilePresentation(presentationObj);
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
            string clockStr = execObj["clockId"]?.GetValue<string>() ?? "Step";
            spec.ClockId = ParseClockId(clockStr);

            // interruptAny
            if (execObj["interruptAny"] is JsonArray intArr)
            {
                foreach (var t in intArr)
                {
                    string tag = t?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(tag)) spec.InterruptAny.AddTag(TagRegistry.Register(tag));
                }
            }

            // items
            if (execObj["items"] is JsonArray itemsArr)
            {
                int idx = 0;
                foreach (var itemNode in itemsArr)
                {
                    if (idx >= AbilityExecSpec.MAX_ITEMS) break;
                    if (itemNode is not JsonObject itemObj) continue;
                    CompileItem(itemObj, ref spec, idx, id, path);
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

        private static void CompileItem(JsonObject itemObj, ref AbilityExecSpec spec, int idx, string id, string path)
        {
            string kindStr = itemObj["kind"]?.GetValue<string>() ?? "None";
            var kind = ParseItemKind(kindStr);
            int tick = itemObj["tick"]?.GetValue<int>() ?? 0;
            int durationTicks = itemObj["duration"]?.GetValue<int>() ?? 0;

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

            int payloadA = itemObj["payloadA"]?.GetValue<int>() ?? 0;

            if ((kind == ExecItemKind.EffectClip || kind == ExecItemKind.EffectSignal) &&
                itemObj["dispatchTarget"] is JsonValue dispatchTargetNode)
            {
                string rawDispatchTarget = dispatchTargetNode.GetValue<string>();
                payloadA = (int)ParseExecEffectDispatchTarget(rawDispatchTarget, id, idx, path);
            }

            // For GraphSignal, "graph" field maps to payloadA via GraphIdRegistry
            if (kind == ExecItemKind.GraphSignal)
            {
                string graphName = itemObj["graph"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(graphName))
                {
                    payloadA = Ludots.Core.NodeLibraries.GASGraph.Host.GraphIdRegistry.Register(graphName);
                }
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

            foreach (var setNode in paramsArr)
            {
                if (setNode is not JsonObject setObj) continue;
                var cp = default(EffectConfigParams);

                if (setObj["entries"] is JsonArray entriesArr)
                {
                    foreach (var entryNode in entriesArr)
                    {
                        if (entryNode is not JsonObject entryObj) continue;
                        string key = entryObj["key"]?.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(key)) continue;
                        int keyId = ConfigKeyRegistry.Register(key);

                        if (entryObj["value"] is JsonNode valNode)
                        {
                            float val = valNode.GetValue<JsonElement>().ValueKind == JsonValueKind.Number
                                ? valNode.GetValue<float>()
                                : float.Parse(valNode.GetValue<string>(), CultureInfo.InvariantCulture);
                            cp.TryAddFloat(keyId, val);
                        }
                    }
                }

                if (!pool.TryAdd(in cp))
                {
                    throw new InvalidOperationException(
                        $"Ability '{id}' exceeded max {AbilityExecCallerParamsPool.MAX_SETS} callerParams sets.");
                }
                hasPool = true;
            }
        }

        // ──────────────── Toggle / Targeting ────────────────

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
                foreach (var effectNode in activeEffects)
                {
                    string effectId = effectNode?.GetValue<string>() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(effectId))
                    {
                        continue;
                    }

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
                                $"Ability '{id}' in '{path}' field '{fieldPath}' is removed; aim visuals belong in Performer event-condition-action rules.");
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

        private static AbilityPresentationConfig? CompilePresentation(JsonObject presentationObj)
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
                    if (string.IsNullOrWhiteSpace(modeKey) || valueNode == null)
                    {
                        continue;
                    }

                    string glyph = valueNode.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(glyph))
                    {
                        config.ModeIconGlyphOverrides[modeKey] = glyph;
                    }
                }
            }

            if (presentationObj["modeHints"] is JsonObject modeHints)
            {
                foreach ((string? modeKey, JsonNode? valueNode) in modeHints)
                {
                    if (string.IsNullOrWhiteSpace(modeKey) || valueNode == null)
                    {
                        continue;
                    }

                    string hint = valueNode.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(hint))
                    {
                        config.ModeHintOverrides[modeKey] = hint;
                    }
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

        // ──────────────── Parsing helpers ────────────────

        private static GasClockId ParseClockId(string str)
        {
            return str switch
            {
                "FixedFrame" => GasClockId.FixedFrame,
                "Step" => GasClockId.Step,
                "Turn" => GasClockId.Turn,
                _ => throw new InvalidOperationException($"Unknown GasClockId '{str}'. Valid values: FixedFrame, Step, Turn."),
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
    }
}
