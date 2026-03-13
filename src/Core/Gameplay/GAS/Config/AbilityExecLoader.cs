using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
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

            var entry = ConfigPipeline.GetEntryOrDefault(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
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

            // 鈹€鈹€ exec block 鈹€鈹€
            if (obj["exec"] is JsonObject execObj)
            {
                def.ExecSpec = CompileExecSpec(execObj, id, path);
                CompileCallerParamsPool(execObj, id, path, out var pool, out bool hasPool);
                def.ExecCallerParamsPool = pool;
                def.HasExecCallerParamsPool = hasPool;
            }

            // 鈹€鈹€ onActivateEffects 鈹€鈹€
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

            // 鈹€鈹€ blockTags 鈹€鈹€
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

            if (obj["attributePreconditions"] is JsonArray attributePreconditions)
            {
                var preconditions = default(AbilityAttributePreconditions);
                foreach (JsonNode item in attributePreconditions)
                {
                    if (item is not JsonObject condition)
                    {
                        continue;
                    }

                    string attributeName = condition["attribute"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(attributeName))
                    {
                        continue;
                    }

                    if (!preconditions.TryAdd(
                        AttributeRegistry.Register(attributeName),
                        ReadFloat(condition, "value"),
                        ParseAttributeComparison(condition["comparison"]?.GetValue<string>() ?? "GreaterOrEqual"),
                        ParseFailReason(condition["failReason"]?.GetValue<string>() ?? "InsufficientResource")))
                    {
                        throw new InvalidOperationException(
                            $"Ability '{id}' exceeded max {AbilityAttributePreconditions.MAX_ENTRIES} attribute preconditions.");
                    }
                }

                if (preconditions.Count > 0)
                {
                    def.HasAttributePreconditions = true;
                    def.AttributePreconditions = preconditions;
                }
            }

            return def;
        }

        // 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€ ExecSpec 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

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

        // 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€ CallerParamsPool 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

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

        // 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€ Parsing helpers 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

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

        private static AbilityAttributeComparison ParseAttributeComparison(string str)
        {
            return str switch
            {
                "GreaterOrEqual" => AbilityAttributeComparison.GreaterOrEqual,
                "GreaterThan" => AbilityAttributeComparison.GreaterThan,
                "LessOrEqual" => AbilityAttributeComparison.LessOrEqual,
                "LessThan" => AbilityAttributeComparison.LessThan,
                _ => throw new InvalidOperationException(
                    $"Unknown AbilityAttributeComparison '{str}'. Valid values: GreaterOrEqual, GreaterThan, LessOrEqual, LessThan."),
            };
        }

        private static AbilityCastFailReason ParseFailReason(string str)
        {
            return str switch
            {
                "InsufficientResource" => AbilityCastFailReason.InsufficientResource,
                "BlockedByTag" => AbilityCastFailReason.BlockedByTag,
                "OnCooldown" => AbilityCastFailReason.OnCooldown,
                "NoTarget" => AbilityCastFailReason.NoTarget,
                "InvalidSlot" => AbilityCastFailReason.InvalidSlot,
                "NotAlive" => AbilityCastFailReason.NotAlive,
                _ => throw new InvalidOperationException(
                    $"Unknown AbilityCastFailReason '{str}'. Valid values: InsufficientResource, BlockedByTag, OnCooldown, NoTarget, InvalidSlot, NotAlive."),
            };
        }

        private static float ReadFloat(JsonObject obj, string key)
        {
            if (obj[key] is not JsonNode valueNode)
            {
                return 0f;
            }

            JsonElement element = valueNode.GetValue<JsonElement>();
            return element.ValueKind == JsonValueKind.Number
                ? valueNode.GetValue<float>()
                : float.Parse(valueNode.GetValue<string>(), CultureInfo.InvariantCulture);
        }
    }
}


