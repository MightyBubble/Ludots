using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Layers;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Core.Gameplay.GAS.Config
{
    public sealed class EffectTemplateLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly EffectTemplateRegistry _registry;
        private readonly GasConditionRegistry _conditions;

        public EffectTemplateLoader(ConfigPipeline pipeline, EffectTemplateRegistry registry, GasConditionRegistry conditions = null)
        {
            _pipeline = pipeline;
            _registry = registry;
            _conditions = conditions;
        }

        public void Load(string relativePath = "GAS/effects.json")
        {
            _registry.Clear();
            EffectTemplateIdRegistry.Clear();
            UnitTypeRegistry.Clear();

            var arrays = _pipeline.CollectJsonArrays(relativePath);
            var mergedNodes = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var array in arrays)
            {
                foreach (var node in array)
                {
                    if (node is not JsonObject obj) continue;

                    if (!TryGetId(obj, out var id))
                    {
                        throw new InvalidOperationException($"Effect template entry missing 'id' in {relativePath}.");
                    }

                    if (mergedNodes.TryGetValue(id, out var existingNode))
                    {
                        JsonMerger.Merge(existingNode, node);
                    }
                    else
                    {
                        mergedNodes[id] = node.DeepClone();
                    }
                }
            }

            var merged = new List<(string Id, JsonObject Node)>(mergedNodes.Count);
            foreach (var kvp in mergedNodes)
            {
                if (kvp.Value is not JsonObject obj)
                {
                    throw new InvalidOperationException($"Effect template '{kvp.Key}' in {relativePath} is not an object.");
                }

                RejectForbiddenFields(obj, relativePath, kvp.Key);
                merged.Add((kvp.Key, obj));
            }

            merged.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id));

            for (int i = 0; i < merged.Count; i++)
            {
                EffectTemplateIdRegistry.Register(merged[i].Id);
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };

            for (int i = 0; i < merged.Count; i++)
            {
                var (id, obj) = merged[i];
                var cfg = obj.Deserialize<EffectTemplateConfig>(options);
                if (cfg == null)
                {
                    throw new InvalidOperationException($"Failed to deserialize effect template '{id}' from {relativePath}.");
                }

                if (string.IsNullOrWhiteSpace(cfg.Id))
                {
                    cfg.Id = id;
                }

                if (!string.Equals(cfg.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Effect template id mismatch in {relativePath}: '{id}' vs '{cfg.Id}'.");
                }

                int templateId = EffectTemplateIdRegistry.GetId(id);
                if (templateId <= 0)
                {
                    throw new InvalidOperationException($"Internal error: failed to allocate templateId for '{id}'.");
                }

                var data = Compile(cfg, relativePath);
                _registry.Register(templateId, data);
            }
        }

        private static bool TryGetId(JsonObject obj, out string id)
        {
            if (obj.TryGetPropertyValue("Id", out var idNode) || obj.TryGetPropertyValue("id", out idNode))
            {
                id = idNode?.ToString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(id);
            }

            foreach (var kvp in obj)
            {
                if (string.Equals(kvp.Key, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    id = kvp.Value?.ToString() ?? string.Empty;
                    return !string.IsNullOrWhiteSpace(id);
                }
            }

            id = string.Empty;
            return false;
        }

        private static void RejectForbiddenFields(JsonObject obj, string relativePath, string id)
        {
            // Reject old scalar "duration"/"period" (seconds). New schema uses "duration" as an object block.
            if (obj.ContainsKey("period") || obj.ContainsKey("Period"))
            {
                throw new InvalidOperationException($"Effect template '{id}' in {relativePath} uses deprecated 'period' field. Use 'duration.periodTicks' instead.");
            }
            // Only reject "duration" if it's a scalar (number/string), not an object block
            if (obj.TryGetPropertyValue("duration", out var durNode) || obj.TryGetPropertyValue("Duration", out durNode))
            {
                if (durNode != null && durNode is not System.Text.Json.Nodes.JsonObject)
                {
                    throw new InvalidOperationException($"Effect template '{id}' in {relativePath} uses scalar 'duration' field. Use 'duration: {{ durationTicks: N }}' object block instead.");
                }
            }
        }

        private EffectTemplateData Compile(EffectTemplateConfig cfg, string relativePath)
        {
            // ── Lifetime + Duration resolution ──
            EffectLifetimeKind lifetimeKind;
            int durationTicks;
            int periodTicks;
            GasClockId clockId = GasClockId.FixedFrame;

            if (string.IsNullOrWhiteSpace(cfg.Lifetime))
            {
                throw new InvalidOperationException($"Effect template '{cfg.Id}' in {relativePath}: 'lifetime' field is required. Legacy 'durationType' schema is no longer supported.");
            }

            // New schema: explicit lifetime field + Duration block
            lifetimeKind = ParseLifetimeKind(cfg.Lifetime, cfg.Id, relativePath);
            durationTicks = cfg.Duration?.DurationTicks ?? 0;
            periodTicks = cfg.Duration?.PeriodTicks ?? 0;
            if (cfg.Duration != null && !string.IsNullOrWhiteSpace(cfg.Duration.ClockId))
                clockId = ParseClockId(cfg.Duration.ClockId);

            int tagId = 0;
            if (cfg.Tags != null && cfg.Tags.Count > 0)
            {
                tagId = TagRegistry.Register(cfg.Tags[0]);
            }

            EffectPresetType presetType = ParsePresetType(cfg.PresetType, cfg.Id, relativePath);
            int presetAttr0 = 0;
            int presetAttr1 = 0;
            int reserved = 0;
            if (presetType == EffectPresetType.ApplyForce2D)
            {
                if (lifetimeKind != EffectLifetimeKind.Instant)
                {
                    throw new InvalidOperationException($"Effect template '{cfg.Id}' in {relativePath}: presetType ApplyForce2D requires lifetime=Instant.");
                }
                // Force target attributes are specified via configParams with type "attribute":
                //   "_ep.forceXTargetAttrId": { "type": "attribute", "value": "Physics.ForceRequestX" }
                //   "_ep.forceYTargetAttrId": { "type": "attribute", "value": "Physics.ForceRequestY" }
                // They are resolved below after configParams compilation.
                reserved = 2;
            }

            var modifiers = default(EffectModifiers);
            if (cfg.Modifiers != null && cfg.Modifiers.Count > 0)
            {
                if (cfg.Modifiers.Count > EffectModifiers.CAPACITY - reserved)
                {
                    throw new InvalidOperationException($"Effect template '{cfg.Id}' in {relativePath}: modifiers count exceeds capacity {EffectModifiers.CAPACITY - reserved} (reserved={reserved} for presetType={presetType}).");
                }

                for (int i = 0; i < cfg.Modifiers.Count; i++)
                {
                    var m = cfg.Modifiers[i];
                    if (m == null) continue;

                    if (string.IsNullOrWhiteSpace(m.Attribute))
                    {
                        throw new InvalidOperationException($"Effect template '{cfg.Id}' in {relativePath}: modifier[{i}] missing 'attribute' field.");
                    }

                    int attrId = AttributeRegistry.Register(m.Attribute);
                    ModifierOp op = ParseModifierOp(m.Op, cfg.Id, relativePath, modifierIndex: i);
                    modifiers.Add(attrId, op, m.Value);
                }
            }

            // Legacy callback fields (onApplyEffect, etc.) have been removed from EffectTemplateConfig.
            // If old JSON configs contain them, System.Text.Json will silently ignore unknown properties.

            // ── Phase Graph bindings ──
            var behaviorTemplate = default(EffectPhaseGraphBindings);
            if (cfg.PhaseGraphs != null)
            {
                CompilePhaseGraphs(cfg.PhaseGraphs, ref behaviorTemplate, cfg.Id, relativePath);
            }

            // ── Config Params ──
            var configParams = default(EffectConfigParams);
            if (cfg.ConfigParams != null)
            {
                CompileConfigParams(cfg.ConfigParams, ref configParams, cfg.Id, relativePath);
            }

            // ── ApplyForce2D: resolve target attribute IDs from configParams ──
            if (presetType == EffectPresetType.ApplyForce2D)
            {
                if (!configParams.TryGetAttributeId(EffectParamKeys.ForceXTargetAttrId, out int fxAttrId) ||
                    !configParams.TryGetAttributeId(EffectParamKeys.ForceYTargetAttrId, out int fyAttrId) ||
                    fxAttrId < 0 || fyAttrId < 0)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: ApplyForce2D requires configParams " +
                        "\"_ep.forceXTargetAttrId\" and \"_ep.forceYTargetAttrId\" with type \"attribute\".");
                }
                presetAttr0 = fxAttrId;
                presetAttr1 = fyAttrId;
            }

            // ── Phase Listeners ──
            var listenerSetup = default(EffectPhaseListenerBuffer);
            if (cfg.PhaseListeners != null)
            {
                CompilePhaseListeners(cfg.PhaseListeners, ref listenerSetup, cfg.Id, relativePath);
            }

            // ── Three-layer target resolution (new schema) ──
            var targetQuery = default(TargetQueryDescriptor);
            var targetFilter = default(TargetFilterDescriptor);
            var targetDispatch = default(TargetDispatchDescriptor);
            if (cfg.TargetQuery != null)
            {
                targetQuery = CompileTargetQuery(cfg.TargetQuery, cfg.Id, relativePath);
            }
            if (cfg.TargetFilter != null)
            {
                targetFilter = CompileTargetFilter(cfg.TargetFilter, cfg.Id, relativePath);
            }
            if (cfg.TargetDispatch != null)
            {
                targetDispatch = CompileTargetDispatch(cfg.TargetDispatch, cfg.Id, relativePath);
            }

            var expireCondition = CompileExpireCondition(cfg.ExpireCondition, cfg.Id, relativePath);
            var grantedTags = CompileGrantedTags(cfg.GrantedTags, cfg.Id, relativePath);
            CompileStackConfig(cfg.Stack, cfg.Id, relativePath,
                out bool hasStackPolicy, out Components.StackPolicy stackPolicy,
                out Components.StackOverflowPolicy stackOverflowPolicy, out int stackLimit);

            var projectile = CompileProjectile(cfg.Projectile, cfg.Id, relativePath);
            var unitCreation = CompileUnitCreation(cfg.UnitCreation, cfg.Id, relativePath);

            return new EffectTemplateData
            {
                TagId = tagId,
                PresetType = presetType,
                PresetAttribute0 = presetAttr0,
                PresetAttribute1 = presetAttr1,
                LifetimeKind = lifetimeKind,
                ClockId = clockId,
                DurationTicks = durationTicks,
                PeriodTicks = periodTicks,
                ExpireCondition = expireCondition,
                ParticipatesInResponse = cfg.ParticipatesInResponse,
                Modifiers = modifiers,
                TargetQuery = targetQuery,
                TargetFilter = targetFilter,
                TargetDispatch = targetDispatch,
                Projectile = projectile,
                UnitCreation = unitCreation,
                PhaseGraphBindings = behaviorTemplate,
                ConfigParams = configParams,
                ListenerSetup = listenerSetup,
                GrantedTags = grantedTags,
                HasStackPolicy = hasStackPolicy,
                StackPolicy = stackPolicy,
                StackOverflowPolicy = stackOverflowPolicy,
                StackLimit = stackLimit,
            };
        }

        private static ProjectileDescriptor CompileProjectile(ProjectileConfig cfg, string ownerId, string relativePath)
        {
            if (cfg == null) return default;

            int impactId = 0;
            if (!string.IsNullOrWhiteSpace(cfg.ImpactEffect))
            {
                impactId = EffectTemplateIdRegistry.GetId(cfg.ImpactEffect);
                if (impactId <= 0)
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: projectile.impactEffect references unknown effect template '{cfg.ImpactEffect}'.");
                }
            }

            return new ProjectileDescriptor
            {
                Speed = cfg.Speed,
                Range = cfg.Range,
                ArcHeight = cfg.ArcHeight,
                ImpactEffectTemplateId = impactId
            };
        }

        private static UnitCreationDescriptor CompileUnitCreation(UnitCreationConfig cfg, string ownerId, string relativePath)
        {
            if (cfg == null) return default;

            int unitTypeId = UnitTypeRegistry.Register(cfg.UnitType);
            if (unitTypeId <= 0)
            {
                throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: unitCreation.unitType is required.");
            }

            int onSpawnId = 0;
            if (!string.IsNullOrWhiteSpace(cfg.OnSpawnEffect))
            {
                onSpawnId = EffectTemplateIdRegistry.GetId(cfg.OnSpawnEffect);
                if (onSpawnId <= 0)
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: unitCreation.onSpawnEffect references unknown effect template '{cfg.OnSpawnEffect}'.");
                }
            }

            return new UnitCreationDescriptor
            {
                UnitTypeId = unitTypeId,
                Count = cfg.Count,
                OffsetRadius = cfg.OffsetRadius,
                OnSpawnEffectTemplateId = onSpawnId
            };
        }

        private static ModifierOp ParseModifierOp(string op, string ownerId, string relativePath, int modifierIndex)
        {
            if (string.IsNullOrWhiteSpace(op)) return ModifierOp.Add;

            if (string.Equals(op, "Add", StringComparison.OrdinalIgnoreCase)) return ModifierOp.Add;
            if (string.Equals(op, "Multiply", StringComparison.OrdinalIgnoreCase)) return ModifierOp.Multiply;
            if (string.Equals(op, "Override", StringComparison.OrdinalIgnoreCase)) return ModifierOp.Override;

            throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: modifier[{modifierIndex}] unsupported op '{op}'. Supported: Add, Multiply, Override.");
        }

        private static SpatialShape ParseSpatialShape(string shape, string ownerId, string relativePath)
        {
            if (string.Equals(shape, "Circle", StringComparison.OrdinalIgnoreCase)) return SpatialShape.Circle;
            if (string.Equals(shape, "Cone", StringComparison.OrdinalIgnoreCase)) return SpatialShape.Cone;
            if (string.Equals(shape, "Rectangle", StringComparison.OrdinalIgnoreCase)) return SpatialShape.Rectangle;
            if (string.Equals(shape, "Line", StringComparison.OrdinalIgnoreCase)) return SpatialShape.Line;
            if (string.Equals(shape, "Ring", StringComparison.OrdinalIgnoreCase)) return SpatialShape.Ring;
            throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: unsupported targetResolver.shape '{shape}'.");
        }

        // ── Legacy TeamFilter → RelationshipFilter migration ──
        // Mapping table: old teamFilter vocabulary → canonical RelationshipFilter names.
        // Lives in the Loader (migration boundary), NOT in RelationshipFilterUtil (clean API).
        private static ContextSlot ParseContextSlot(string slot, ContextSlot defaultValue)
        {
            if (string.IsNullOrWhiteSpace(slot)) return defaultValue;
            if (string.Equals(slot, "OriginalSource", StringComparison.OrdinalIgnoreCase)) return ContextSlot.OriginalSource;
            if (string.Equals(slot, "OriginalTarget", StringComparison.OrdinalIgnoreCase)) return ContextSlot.OriginalTarget;
            if (string.Equals(slot, "OriginalTargetContext", StringComparison.OrdinalIgnoreCase)) return ContextSlot.OriginalTargetContext;
            if (string.Equals(slot, "ResolvedEntity", StringComparison.OrdinalIgnoreCase)) return ContextSlot.ResolvedEntity;
            return defaultValue;
        }

        private static EffectPresetType ParsePresetType(string presetType, string ownerId, string relativePath)
        {
            return GasEnumParser.ParsePresetTypeStrict(presetType, $"Effect template '{ownerId}' in {relativePath}");
        }

        // ── Phase Graph compilation ──

        // Phase name map delegated to GasEnumParser.TryParsePhaseId (single source of truth)

        private static void CompilePhaseGraphs(
            Dictionary<string, PhaseGraphConfig> phaseGraphs,
            ref EffectPhaseGraphBindings behavior,
            string ownerId,
            string relativePath)
        {
            foreach (var kvp in phaseGraphs)
            {
                if (!GasEnumParser.TryParsePhaseId(kvp.Key, out var phaseId))
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: unknown phaseGraph key '{kvp.Key}'.");
                }

                var phaseCfg = kvp.Value;
                if (phaseCfg == null) continue;

                if (!string.IsNullOrWhiteSpace(phaseCfg.Pre))
                {
                    int graphId = ResolveGraphProgram(phaseCfg.Pre, ownerId, $"phaseGraphs.{kvp.Key}.pre", relativePath);
                    if (graphId > 0)
                    {
                        if (!behavior.TryAddStep(phaseId, PhaseSlot.Pre, graphId))
                        {
                            throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: exceeded max phase steps ({EffectPhaseGraphBindings.MAX_STEPS}).");
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(phaseCfg.Post))
                {
                    int graphId = ResolveGraphProgram(phaseCfg.Post, ownerId, $"phaseGraphs.{kvp.Key}.post", relativePath);
                    if (graphId > 0)
                    {
                        if (!behavior.TryAddStep(phaseId, PhaseSlot.Post, graphId))
                        {
                            throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: exceeded max phase steps ({EffectPhaseGraphBindings.MAX_STEPS}).");
                        }
                    }
                }

                if (phaseCfg.SkipMain)
                {
                    behavior.SetSkipMain(phaseId);
                }
            }
        }

        private static int ResolveGraphProgram(string name, string ownerId, string fieldPath, string relativePath)
        {
            int id = GraphIdRegistry.GetId(name);
            if (id <= 0)
            {
                throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: {fieldPath} references unknown graph program '{name}'.");
            }
            return id;
        }

        // ── Config Params compilation ──

        private static void CompileConfigParams(
            Dictionary<string, ConfigParamConfig> configParams,
            ref EffectConfigParams result,
            string ownerId,
            string relativePath)
        {
            foreach (var kvp in configParams)
            {
                var paramCfg = kvp.Value;
                if (paramCfg == null) continue;

                // Use a deterministic key ID from the config key name.
                int keyId = ConfigKeyRegistry.Register(kvp.Key);

                string type = paramCfg.Type ?? "float";
                if (string.Equals(type, "float", StringComparison.OrdinalIgnoreCase))
                {
                    float val = paramCfg.Value is JsonElement jf ? jf.GetSingle() : Convert.ToSingle(paramCfg.Value, CultureInfo.InvariantCulture);
                    if (!result.TryAddFloat(keyId, val))
                    {
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams exceeded capacity ({EffectConfigParams.MAX_PARAMS}).");
                    }
                }
                else if (string.Equals(type, "int", StringComparison.OrdinalIgnoreCase))
                {
                    int val = paramCfg.Value is JsonElement ji ? ji.GetInt32() : Convert.ToInt32(paramCfg.Value, CultureInfo.InvariantCulture);
                    if (!result.TryAddInt(keyId, val))
                    {
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams exceeded capacity ({EffectConfigParams.MAX_PARAMS}).");
                    }
                }
                else if (string.Equals(type, "effectTemplate", StringComparison.OrdinalIgnoreCase))
                {
                    string templateName = paramCfg.Value?.ToString() ?? "";
                    int templateId = EffectTemplateIdRegistry.GetId(templateName);
                    if (templateId <= 0)
                    {
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams.{kvp.Key} references unknown effect template '{templateName}'.");
                    }
                    if (!result.TryAddEffectTemplateId(keyId, templateId))
                    {
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams exceeded capacity ({EffectConfigParams.MAX_PARAMS}).");
                    }
                }
                else if (string.Equals(type, "attribute", StringComparison.OrdinalIgnoreCase))
                {
                    string attrName = paramCfg.Value?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(attrName))
                    {
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams.{kvp.Key} attribute type requires a non-empty attribute name.");
                    }
                    int attrId = AttributeRegistry.Register(attrName);
                    if (!result.TryAddAttributeId(keyId, attrId))
                    {
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams exceeded capacity ({EffectConfigParams.MAX_PARAMS}).");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams.{kvp.Key} has unsupported type '{type}'. Supported: float, int, effectTemplate, attribute.");
                }
            }
        }

        // ── Phase Listeners compilation ──

        private static void CompilePhaseListeners(
            List<PhaseListenerConfig> listeners,
            ref EffectPhaseListenerBuffer result,
            string ownerId,
            string relativePath)
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                var lc = listeners[i];
                if (lc == null) continue;

                // Phase
                if (string.IsNullOrWhiteSpace(lc.Phase) || !GasEnumParser.TryParsePhaseId(lc.Phase, out var phaseId))
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: phaseListeners[{i}] has invalid phase '{lc.Phase}'.");
                }

                // Scope
                PhaseListenerScope scope = PhaseListenerScope.Target;
                if (!string.IsNullOrWhiteSpace(lc.Scope))
                {
                    if (string.Equals(lc.Scope, "source", StringComparison.OrdinalIgnoreCase))
                        scope = PhaseListenerScope.Source;
                    else if (!string.Equals(lc.Scope, "target", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: phaseListeners[{i}] has unknown scope '{lc.Scope}'. Supported: source, target.");
                }

                // Action
                PhaseListenerActionFlags flags = PhaseListenerActionFlags.ExecuteGraph;
                if (!string.IsNullOrWhiteSpace(lc.Action))
                {
                    if (string.Equals(lc.Action, "graph", StringComparison.OrdinalIgnoreCase))
                        flags = PhaseListenerActionFlags.ExecuteGraph;
                    else if (string.Equals(lc.Action, "event", StringComparison.OrdinalIgnoreCase))
                        flags = PhaseListenerActionFlags.PublishEvent;
                    else if (string.Equals(lc.Action, "both", StringComparison.OrdinalIgnoreCase))
                        flags = PhaseListenerActionFlags.Both;
                    else
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: phaseListeners[{i}] has unknown action '{lc.Action}'. Supported: graph, event, both.");
                }

                // Listen tag
                int listenTagId = 0;
                if (!string.IsNullOrWhiteSpace(lc.ListenTag))
                    listenTagId = TagRegistry.Register(lc.ListenTag);

                // Listen effect template id
                int listenEffectId = 0;
                if (!string.IsNullOrWhiteSpace(lc.ListenEffectId))
                {
                    listenEffectId = EffectTemplateIdRegistry.GetId(lc.ListenEffectId);
                    if (listenEffectId <= 0)
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: phaseListeners[{i}].listenEffectId '{lc.ListenEffectId}' not found.");
                }

                // Graph program
                int graphProgramId = 0;
                if ((flags & PhaseListenerActionFlags.ExecuteGraph) != 0 && !string.IsNullOrWhiteSpace(lc.GraphProgram))
                    graphProgramId = ResolveGraphProgram(lc.GraphProgram, ownerId, $"phaseListeners[{i}].graphProgram", relativePath);

                // Event tag
                int eventTagId = 0;
                if ((flags & PhaseListenerActionFlags.PublishEvent) != 0 && !string.IsNullOrWhiteSpace(lc.EventTag))
                    eventTagId = TagRegistry.Register(lc.EventTag);

                if (!result.TryAddTemplate(listenTagId, listenEffectId, phaseId, scope, flags, graphProgramId, eventTagId, lc.Priority))
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: phaseListeners exceeded capacity ({EffectPhaseListenerBuffer.CAPACITY}).");
                }
            }
        }

        // ── New schema parse helpers ──

        private static EffectLifetimeKind ParseLifetimeKind(string value, string effectId, string path)
        {
            return GasEnumParser.ParseLifetimeKindStrict(value, $"Effect template '{effectId}' in {path}");
        }

        private static GasClockId ParseClockId(string value) => value switch
        {
            "FixedFrame" => GasClockId.FixedFrame,
            "Step" => GasClockId.Step,
            "Turn" => GasClockId.Turn,
            _ => throw new InvalidOperationException($"Unknown GasClockId '{value}'. Supported: FixedFrame, Step, Turn."),
        };

        private static TargetQueryDescriptor CompileTargetQuery(TargetQueryConfig cfg, string effectId, string path)
        {
            var desc = default(TargetQueryDescriptor);
            if (!string.IsNullOrWhiteSpace(cfg.Kind))
            {
                desc.Kind = cfg.Kind switch
                {
                    "BuiltinSpatial" => TargetResolverKind.BuiltinSpatial,
                    "GraphProgram" => TargetResolverKind.GraphProgram,
                    _ => TargetResolverKind.None,
                };
            }
            if (desc.Kind == TargetResolverKind.BuiltinSpatial)
            {
                desc.Spatial.Shape = ParseSpatialShape(cfg.Shape, effectId, path);
                desc.Spatial.RadiusCm = cfg.Radius;
                desc.Spatial.InnerRadiusCm = cfg.InnerRadius;
                desc.Spatial.HalfAngleDeg = cfg.HalfAngle;
                desc.Spatial.HalfWidthCm = cfg.HalfWidth;
                desc.Spatial.HalfHeightCm = cfg.HalfHeight;
                desc.Spatial.RotationDeg = cfg.Rotation;
                desc.Spatial.LengthCm = cfg.Length;
            }
            desc.GraphProgramId = cfg.GraphProgramId;
            return desc;
        }

        private static TargetFilterDescriptor CompileTargetFilter(TargetFilterConfig cfg, string effectId, string path)
        {
            var desc = default(TargetFilterDescriptor);
            desc.ExcludeSource = cfg.ExcludeSource;
            desc.MaxTargets = cfg.MaxTargets;
            if (!string.IsNullOrWhiteSpace(cfg.RelationFilter))
                desc.RelationFilter = Teams.RelationshipFilterUtil.Parse(cfg.RelationFilter);
            if (cfg.LayerMask != null)
                desc.LayerMask = ParseLayerMask(cfg.LayerMask);
            return desc;
        }

        private static TargetDispatchDescriptor CompileTargetDispatch(TargetDispatchConfig cfg, string effectId, string path)
        {
            var desc = default(TargetDispatchDescriptor);
            if (!string.IsNullOrWhiteSpace(cfg.PayloadEffect))
            {
                desc.PayloadEffectTemplateId = EffectTemplateIdRegistry.GetId(cfg.PayloadEffect);
                if (desc.PayloadEffectTemplateId <= 0)
                    throw new InvalidOperationException($"Effect template '{effectId}' in {path}: targetDispatch.payloadEffect '{cfg.PayloadEffect}' not found.");
            }
            if (cfg.ContextMapping != null)
            {
                desc.ContextMapping = new TargetResolverContextMapping
                {
                    PayloadSource = ParseContextSlot(cfg.ContextMapping.PayloadSource, ContextSlot.OriginalSource),
                    PayloadTarget = ParseContextSlot(cfg.ContextMapping.PayloadTarget, ContextSlot.ResolvedEntity),
                    PayloadTargetContext = ParseContextSlot(cfg.ContextMapping.PayloadTargetContext, ContextSlot.OriginalTarget),
                };
            }
            else
            {
                desc.ContextMapping = TargetResolverContextMapping.Default;
            }
            return desc;
        }

        private static uint ParseLayerMask(List<string> layers)
        {
            throw new NotImplementedException("LayerMask parsing not yet implemented. Layer name to bit mapping requires a layer registry.");
        }

        private GasConditionHandle CompileExpireCondition(ExpireConditionConfig cfg, string effectId, string path)
        {
            if (cfg == null) return default;

            var kind = cfg.Kind switch
            {
                "TagPresent" => GasConditionKind.TagPresent,
                "TagAbsent" => GasConditionKind.TagAbsent,
                _ => throw new InvalidOperationException($"Effect template '{effectId}' in {path}: unknown expire condition kind '{cfg.Kind}'."),
            };

            if (string.IsNullOrWhiteSpace(cfg.Tag))
                throw new InvalidOperationException($"Effect template '{effectId}' in {path}: expireCondition requires a 'tag' field.");

            int tagId = TagRegistry.Register(cfg.Tag);

            var sense = TagSense.Effective;
            if (!string.IsNullOrWhiteSpace(cfg.Sense))
            {
                sense = cfg.Sense switch
                {
                    "Raw" => TagSense.Present,
                    "Effective" => TagSense.Effective,
                    _ => throw new InvalidOperationException($"Effect template '{effectId}' in {path}: unknown tag sense '{cfg.Sense}'."),
                };
            }

            if (_conditions == null)
                throw new InvalidOperationException($"Effect template '{effectId}' in {path}: expireCondition requires GasConditionRegistry to be provided to the loader.");

            return _conditions.Register(new GasCondition(kind, tagId, sense));
        }

        private static Components.EffectGrantedTags CompileGrantedTags(List<GrantedTagConfig> cfgs, string effectId, string path)
        {
            var result = new Components.EffectGrantedTags();
            if (cfgs == null || cfgs.Count == 0) return result;

            for (int i = 0; i < cfgs.Count; i++)
            {
                if (i >= Components.EffectGrantedTags.MAX_GRANTS)
                {
                    throw new InvalidOperationException($"Effect template '{effectId}' in {path}: grantedTags exceeds max {Components.EffectGrantedTags.MAX_GRANTS}.");
                }

                var cfg = cfgs[i];
                if (string.IsNullOrWhiteSpace(cfg.Tag))
                    throw new InvalidOperationException($"Effect template '{effectId}' in {path}: grantedTags[{i}] requires a 'tag' field.");

                int tagId = TagRegistry.Register(cfg.Tag);
                var formula = (cfg.Formula ?? "Fixed") switch
                {
                    "Fixed" => Components.TagContributionFormula.Fixed,
                    "Linear" => Components.TagContributionFormula.Linear,
                    "LinearPlusBase" => Components.TagContributionFormula.LinearPlusBase,
                    "GraphProgram" => Components.TagContributionFormula.GraphProgram,
                    _ => throw new InvalidOperationException($"Effect template '{effectId}' in {path}: grantedTags[{i}] unknown formula '{cfg.Formula}'."),
                };

                result.Add(new Components.TagContribution
                {
                    TagId = tagId,
                    Formula = formula,
                    Amount = (ushort)System.Math.Clamp(cfg.Amount, 0, ushort.MaxValue),
                    Base = (ushort)System.Math.Clamp(cfg.Base, 0, ushort.MaxValue),
                    GraphProgramId = 0, // Resolved later if needed
                });
            }
            return result;
        }

        private static void CompileStackConfig(StackConfig cfg, string effectId, string path,
            out bool hasStackPolicy, out Components.StackPolicy stackPolicy,
            out Components.StackOverflowPolicy stackOverflowPolicy, out int stackLimit)
        {
            if (cfg == null)
            {
                hasStackPolicy = false;
                stackPolicy = default;
                stackOverflowPolicy = default;
                stackLimit = 0;
                return;
            }

            hasStackPolicy = true;
            stackLimit = cfg.Limit;

            stackPolicy = (cfg.Policy ?? "RefreshDuration") switch
            {
                "RefreshDuration" => Components.StackPolicy.RefreshDuration,
                "AddDuration" => Components.StackPolicy.AddDuration,
                "KeepDuration" => Components.StackPolicy.KeepDuration,
                _ => throw new InvalidOperationException($"Effect template '{effectId}' in {path}: unknown stack policy '{cfg.Policy}'."),
            };

            stackOverflowPolicy = (cfg.OverflowPolicy ?? "RejectNew") switch
            {
                "RejectNew" => Components.StackOverflowPolicy.RejectNew,
                "RemoveOldest" => Components.StackOverflowPolicy.RemoveOldest,
                _ => throw new InvalidOperationException($"Effect template '{effectId}' in {path}: unknown stack overflow policy '{cfg.OverflowPolicy}'."),
            };
        }
    }
}
