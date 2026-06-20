using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Technology.Registry;
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
                def.Presentation = CompilePresentation(presentationObj);
                def.HasPresentation = def.Presentation != null;
            }

            if (obj["input"] is JsonObject inputObj)
            {
                def.InputBindingOverride = CompileInputBindingOverride(inputObj, id, path);
                def.HasInputBindingOverride = true;
            }

            def.UseTechnologyRequirementId = ResolveTechnologyRequirement(obj, "useRequirement", id, path);
            def.HasUseTechnologyRequirement = def.UseTechnologyRequirementId > 0;
            def.ShowTechnologyRequirementId = ResolveTechnologyRequirement(obj, "showRequirement", id, path);
            def.HasShowTechnologyRequirement = def.ShowTechnologyRequirementId > 0;

            return def;
        }

        private static int ResolveTechnologyRequirement(JsonObject obj, string fieldName, string id, string path)
        {
            string requirementName = obj[fieldName]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(requirementName))
            {
                return 0;
            }

            int requirementId = TechnologyRequirementIdRegistry.GetId(requirementName);
            if (requirementId <= 0)
            {
                throw new InvalidOperationException(
                    $"Ability '{id}' in '{path}' field '{fieldName}' references unknown technology requirement '{requirementName}'.");
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
                int activeCount = 0;
                foreach (var effectNode in activeEffects)
                {
                    if (activeCount >= 4)
                    {
                        break;
                    }

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

        private static AbilityIndicatorConfig CompileIndicator(JsonObject indicatorObj, string id, string path)
        {
            string shapeValue = indicatorObj["shape"]?.GetValue<string>() ?? "Circle";
            var indicator = new AbilityIndicatorConfig
            {
                Shape = ParseTargetShape(shapeValue, id, path),
                Range = indicatorObj["range"]?.GetValue<float>() ?? 0f,
                Radius = indicatorObj["radius"]?.GetValue<float>() ?? 0f,
                InnerRadius = indicatorObj["innerRadius"]?.GetValue<float>() ?? 0f,
                Angle = indicatorObj["angle"]?.GetValue<float>() ?? 0f,
                ValidColor = ParseColor(indicatorObj["validColor"], new System.Numerics.Vector4(0.20f, 0.85f, 0.45f, 0.35f)),
                InvalidColor = ParseColor(indicatorObj["invalidColor"], new System.Numerics.Vector4(0.95f, 0.30f, 0.25f, 0.35f)),
                RangeCircleColor = ParseColor(indicatorObj["rangeCircleColor"], new System.Numerics.Vector4(0.25f, 0.55f, 0.95f, 0.18f)),
                ShowRangeCircle = indicatorObj["showRangeCircle"]?.GetValue<bool>() ?? false
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

            return indicator;
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

        private static System.Numerics.Vector4 ParseColor(JsonNode? node, System.Numerics.Vector4 fallback)
        {
            if (node is JsonArray arr)
            {
                float r = arr.Count > 0 ? arr[0]?.GetValue<float>() ?? fallback.X : fallback.X;
                float g = arr.Count > 1 ? arr[1]?.GetValue<float>() ?? fallback.Y : fallback.Y;
                float b = arr.Count > 2 ? arr[2]?.GetValue<float>() ?? fallback.Z : fallback.Z;
                float a = arr.Count > 3 ? arr[3]?.GetValue<float>() ?? fallback.W : fallback.W;
                return new System.Numerics.Vector4(r, g, b, a);
            }

            string? hex = node?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(hex))
            {
                return fallback;
            }

            hex = hex.Trim();
            if (hex.StartsWith('#'))
            {
                hex = hex[1..];
            }

            if (hex.Length != 6 && hex.Length != 8)
            {
                return fallback;
            }

            byte rByte = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte gByte = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte bByte = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte aByte = hex.Length == 8
                ? byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : (byte)255;

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

