using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Association;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Exchange;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Progression.Registry;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Layers;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Vision;

namespace Ludots.Core.Gameplay.GAS.Config
{
    public sealed class EffectTemplateLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly EffectTemplateRegistry _registry;
        private readonly GasConditionRegistry _conditions;
        private readonly TargetDispatchPresetRegistry _targetDispatchPresets;
        private readonly PresetTypeRegistry? _presetTypes;
        private readonly Ludots.Core.Gameplay.Relationships.RelationshipTypeRegistry? _relationshipTypes;
        private readonly ExchangeOperationRegistry? _exchangeOperations;
        private readonly ScopeKeyRegistry? _progressionScopeKeys;
        private readonly EntityTemplateKeyRegistry? _entityTemplateKeys;
        private readonly FogLayerRegistry? _fogLayers;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            IncludeFields = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        public EffectTemplateLoader(
            ConfigPipeline pipeline,
            EffectTemplateRegistry registry,
            GasConditionRegistry conditions = null,
            TargetDispatchPresetRegistry targetDispatchPresets = null,
            ExchangeOperationRegistry? exchangeOperations = null,
            ScopeKeyRegistry? progressionScopeKeys = null,
            EntityTemplateKeyRegistry? entityTemplateKeys = null,
            Ludots.Core.Gameplay.Relationships.RelationshipTypeRegistry? relationshipTypes = null,
            FogLayerRegistry? fogLayers = null,
            PresetTypeRegistry? presetTypes = null)
        {
            _pipeline = pipeline;
            _registry = registry;
            _conditions = conditions;
            _targetDispatchPresets = targetDispatchPresets;
            _presetTypes = presetTypes;
            _exchangeOperations = exchangeOperations;
            _progressionScopeKeys = progressionScopeKeys;
            _entityTemplateKeys = entityTemplateKeys;
            _relationshipTypes = relationshipTypes;
            _fogLayers = fogLayers;
        }

        public void Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/effects.json")
        {
            _registry.Clear();
            EffectTemplateIdRegistry.Clear();
            UnitTypeRegistry.Clear();

            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var mergedEntries = _pipeline.MergeArrayByIdFromCatalog(in entry, report);

            var merged = new List<(string Id, JsonObject Node)>(mergedEntries.Count);
            for (int i = 0; i < mergedEntries.Count; i++)
            {
                RejectForbiddenFields(mergedEntries[i].Node, relativePath, mergedEntries[i].Id);
                merged.Add((mergedEntries[i].Id, mergedEntries[i].Node));
            }

            merged.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));

            for (int i = 0; i < merged.Count; i++)
            {
                EffectTemplateIdRegistry.Register(merged[i].Id);
            }

            for (int i = 0; i < merged.Count; i++)
            {
                var (id, obj) = merged[i];
                var cfg = obj.Deserialize<EffectTemplateConfig>(JsonOptions);
                if (cfg == null)
                {
                    throw new InvalidOperationException($"Failed to deserialize effect template '{id}' from {relativePath}.");
                }

                if (string.IsNullOrWhiteSpace(cfg.Id))
                {
                    throw new InvalidOperationException($"Effect template '{id}' in {relativePath}: id must be explicitly defined.");
                }

                if (!string.Equals(cfg.Id, id, StringComparison.Ordinal))
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

        private static void RejectForbiddenFields(JsonObject obj, string relativePath, string id)
        {
            // Reject old scalar "duration"/"period" (seconds). New schema uses "duration" as an object block.
            if (obj.ContainsKey("period"))
            {
                throw new InvalidOperationException($"Effect template '{id}' in {relativePath} uses deprecated 'period' field. Use 'duration.periodTicks' instead.");
            }
            // Only reject "duration" if it's a scalar (number/string), not an object block
            if (obj.TryGetPropertyValue("duration", out var durNode))
            {
                if (durNode != null && durNode is not System.Text.Json.Nodes.JsonObject)
                {
                    throw new InvalidOperationException($"Effect template '{id}' in {relativePath} uses scalar 'duration' field. Use 'duration: {{ durationTicks: N }}' object block instead.");
                }
            }

            if (obj.ContainsKey("lifecycleDeploy"))
            {
                throw new InvalidOperationException(
                    $"Effect template '{id}' in {relativePath} uses deprecated 'lifecycleDeploy' block. Use configParams '_ep.targetEntityTemplate' with preset graph composition.");
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
                throw new InvalidOperationException($"Effect template '{cfg.Id}' in {relativePath}: 'lifetime' field is required.");
            }

            lifetimeKind = ParseLifetimeKind(cfg.Lifetime, cfg.Id, relativePath);
            if (cfg.Duration != null)
            {
                if (lifetimeKind == EffectLifetimeKind.Infinite)
                {
                    durationTicks = cfg.Duration.DurationTicks ?? 0;
                    periodTicks = cfg.Duration.PeriodTicks ?? 0;
                    clockId = string.IsNullOrWhiteSpace(cfg.Duration.ClockId)
                        ? GasClockId.FixedFrame
                        : ParseClockId(cfg.Duration.ClockId);
                }
                else
                {
                    durationTicks = RequireInt(cfg.Duration.DurationTicks, cfg.Id, relativePath, "duration.durationTicks");
                    periodTicks = RequireInt(cfg.Duration.PeriodTicks, cfg.Id, relativePath, "duration.periodTicks");
                    clockId = ParseClockId(RequireString(cfg.Duration.ClockId, cfg.Id, relativePath, "duration.clockId"));
                }

                if (lifetimeKind == EffectLifetimeKind.After && durationTicks <= 0)
                {
                    throw new InvalidOperationException($"Effect template '{cfg.Id}' in {relativePath}: lifetime '{lifetimeKind}' requires duration.durationTicks > 0.");
                }
                if (lifetimeKind == EffectLifetimeKind.Instant)
                {
                    throw new InvalidOperationException($"Effect template '{cfg.Id}' in {relativePath}: lifetime '{lifetimeKind}' must omit the duration block.");
                }

                if (lifetimeKind == EffectLifetimeKind.Infinite &&
                    durationTicks == 0 &&
                    periodTicks == 0 &&
                    clockId == GasClockId.FixedFrame)
                {
                    throw new InvalidOperationException($"Effect template '{cfg.Id}' in {relativePath}: lifetime '{lifetimeKind}' must omit the default zero duration block.");
                }
            }
            else if (lifetimeKind == EffectLifetimeKind.After)
            {
                throw new InvalidOperationException($"Effect template '{cfg.Id}' in {relativePath}: lifetime '{lifetimeKind}' requires an explicit duration block.");
            }
            else
            {
                durationTicks = 0;
                periodTicks = 0;
            }

            int tagId = 0;
            if (cfg.Tags != null && cfg.Tags.Count > 0)
            {
                tagId = TagRegistry.Register(cfg.Tags[0]);
            }

            int presetTypeId = ParsePresetTypeId(cfg.PresetType, cfg.Id, relativePath);
            ref readonly PresetTypeDefinition presetDefinition = ref _presetTypes!.Get(presetTypeId);
            if (!presetDefinition.AllowsLifetime(lifetimeKind))
            {
                throw new InvalidOperationException(
                    $"Effect template '{cfg.Id}' in {relativePath}: presetType '{presetDefinition.TypeKey}' does not allow lifetime '{lifetimeKind}'.");
            }

            EffectPresetType presetType = ResolveBuiltinPresetType(presetTypeId);
            int presetAttr0 = 0;
            int presetAttr1 = 0;
            int reserved = 0;
            if (presetType == EffectPresetType.ApplyForce2D)
            {
                if (lifetimeKind != EffectLifetimeKind.Instant)
                {
                    throw new InvalidOperationException($"Effect template '{cfg.Id}' in {relativePath}: presetType ApplyForce2D requires lifetime=Instant.");
                }
                // Force target attributes are specified via configParams with type "Attribute":
                //   "_ep.forceXTargetAttrId": { "type": "Attribute", "value": "Physics.ForceRequestX" }
                //   "_ep.forceYTargetAttrId": { "type": "Attribute", "value": "Physics.ForceRequestY" }
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
                    if (m == null)
                    {
                        throw new InvalidOperationException($"Effect template '{cfg.Id}' in {relativePath}: modifier[{i}] must be an object.");
                    }

                    if (string.IsNullOrWhiteSpace(m.Attribute))
                    {
                        throw new InvalidOperationException($"Effect template '{cfg.Id}' in {relativePath}: modifier[{i}] missing 'attribute' field.");
                    }

                    int attrId = AttributeRegistry.Register(m.Attribute);
                    ModifierOp op = ParseModifierOp(m.Op, cfg.Id, relativePath, modifierIndex: i);
                    float value = RequireFloat(m.Value, cfg.Id, relativePath, $"modifier[{i}].value");
                    modifiers.Add(attrId, op, value);
                }
            }

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
                        "\"_ep.forceXTargetAttrId\" and \"_ep.forceYTargetAttrId\" with type \"Attribute\".");
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
            var displacement = CompileDisplacement(cfg.Displacement, cfg.Id, relativePath);
            var relation = CompileRelation(cfg.Relation, cfg.Id, relativePath);
            var revealArea = default(KnowledgeAreaRevealDescriptor);
            var submitOrderFromBlackboard = CompileSubmitOrderFromBlackboard(cfg.SubmitOrderFromBlackboard, cfg.Id, relativePath);
            var progressionScope = ScopeKey.Self;
            var progressionChange = ProgressionLevelChange.Complete;
            int progressionId = 0;

            if (cfg.Displacement != null && presetType != EffectPresetType.Displacement)
            {
                throw new InvalidOperationException(
                    $"Effect template '{cfg.Id}' in {relativePath}: 'displacement' block is only valid when presetType=Displacement.");
            }
            if (presetType == EffectPresetType.Displacement)
            {
                if (lifetimeKind != EffectLifetimeKind.Instant)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType Displacement requires lifetime=Instant.");
                }
                if (cfg.Displacement == null)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType Displacement requires a 'displacement' block.");
                }
            }

            if (cfg.Relation != null && presetType != EffectPresetType.Relation)
            {
                throw new InvalidOperationException(
                    $"Effect template '{cfg.Id}' in {relativePath}: 'relation' block is only valid when presetType=Relation.");
            }
            if (presetType == EffectPresetType.Relation)
            {
                if (lifetimeKind != EffectLifetimeKind.Instant)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType Relation requires lifetime=Instant.");
                }
                if (cfg.Relation == null)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType Relation requires a 'relation' block.");
                }
            }

            if (cfg.RevealArea != null && presetType != EffectPresetType.RevealArea)
            {
                throw new InvalidOperationException(
                    $"Effect template '{cfg.Id}' in {relativePath}: 'revealArea' block is only valid when presetType=RevealArea.");
            }
            if (presetType == EffectPresetType.RevealArea)
            {
                if (lifetimeKind != EffectLifetimeKind.Instant && lifetimeKind != EffectLifetimeKind.After)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType RevealArea requires lifetime=Instant or lifetime=After.");
                }
                if (lifetimeKind == EffectLifetimeKind.After && periodTicks <= 0)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType RevealArea with lifetime=After requires duration.periodTicks > 0 for refresh.");
                }
                if (cfg.RevealArea == null)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType RevealArea requires a 'revealArea' block.");
                }

                revealArea = CompileRevealArea(cfg.RevealArea, cfg.Id, relativePath);
            }

            if (presetType == EffectPresetType.Exchange)
            {
                if (lifetimeKind != EffectLifetimeKind.Instant)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType Exchange requires lifetime=Instant.");
                }

                if (!configParams.TryGetInt(EffectParamKeys.ExchangeOperationId, out int operationId) || operationId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType Exchange requires configParams \"_ep.exchangeOperationId\" with type \"Int\".");
                }
            }

            if (cfg.Progression != null && presetType != EffectPresetType.CompleteProgression)
            {
                throw new InvalidOperationException(
                    $"Effect template '{cfg.Id}' in {relativePath}: 'progression' block is only valid when presetType=CompleteProgression.");
            }
            if (presetType == EffectPresetType.CompleteProgression)
            {
                if (lifetimeKind != EffectLifetimeKind.Instant)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType CompleteProgression requires lifetime=Instant.");
                }
                if (cfg.Progression == null)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType CompleteProgression requires a 'progression' block.");
                }

                string progressionName = RequireString(cfg.Progression.Id, cfg.Id, relativePath, "progression.id");
                progressionId = ProgressionIdRegistry.GetId(progressionName);
                if (progressionId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: progression.id '{progressionName}' is not registered.");
                }

                progressionScope = ResolveProgressionScope(cfg.Progression.Scope, cfg.Id, relativePath);
                progressionChange = CompileProgressionChange(cfg.Progression, cfg.Id, relativePath);
            }

            if (cfg.Projectile != null && presetType != EffectPresetType.LaunchProjectile)
            {
                throw new InvalidOperationException(
                    $"Effect template '{cfg.Id}' in {relativePath}: 'projectile' block is only valid when presetType=LaunchProjectile.");
            }
            if (presetType == EffectPresetType.LaunchProjectile)
            {
                if (lifetimeKind != EffectLifetimeKind.Instant)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType LaunchProjectile requires lifetime=Instant.");
                }
                if (cfg.Projectile == null)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType LaunchProjectile requires a 'projectile' block.");
                }
            }

            if (cfg.UnitCreation != null && presetType != EffectPresetType.CreateUnit)
            {
                throw new InvalidOperationException(
                    $"Effect template '{cfg.Id}' in {relativePath}: 'unitCreation' block is only valid when presetType=CreateUnit.");
            }
            if (presetType == EffectPresetType.CreateUnit)
            {
                if (lifetimeKind != EffectLifetimeKind.Instant)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType CreateUnit requires lifetime=Instant.");
                }
                if (cfg.UnitCreation == null)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType CreateUnit requires a 'unitCreation' block.");
                }
            }

            if (cfg.SubmitOrderFromBlackboard != null && presetType != EffectPresetType.SubmitOrderFromBlackboard)
            {
                throw new InvalidOperationException(
                    $"Effect template '{cfg.Id}' in {relativePath}: 'submitOrderFromBlackboard' block is only valid when presetType=SubmitOrderFromBlackboard.");
            }
            if (presetType == EffectPresetType.SubmitOrderFromBlackboard)
            {
                if (lifetimeKind != EffectLifetimeKind.Instant)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType SubmitOrderFromBlackboard requires lifetime=Instant.");
                }
                if (cfg.SubmitOrderFromBlackboard == null)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType SubmitOrderFromBlackboard requires a 'submitOrderFromBlackboard' block.");
                }
            }

            if (presetType == EffectPresetType.DeployConsumeSource)
            {
                if (lifetimeKind != EffectLifetimeKind.Instant)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType DeployConsumeSource requires lifetime=Instant.");
                }

                if (!configParams.TryGetInt(EffectParamKeys.TargetEntityTemplateKeyId, out int templateKeyId) || templateKeyId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType DeployConsumeSource requires configParams \"_ep.targetEntityTemplate\" with type \"EntityTemplate\".");
                }

                RequireDeployConsumeSourceLifecycleConfig(in configParams, cfg.Id, relativePath);
            }

            return new EffectTemplateData
            {
                TagId = tagId,
                PresetType = presetType,
                PresetTypeId = presetTypeId,
                PresetAttribute0 = presetAttr0,
                PresetAttribute1 = presetAttr1,
                LifetimeKind = lifetimeKind,
                ClockId = clockId,
                DurationTicks = durationTicks,
                PeriodTicks = periodTicks,
                ExpireCondition = expireCondition,
                ParticipatesInResponse = RequireBool(cfg.ParticipatesInResponse, cfg.Id, relativePath, "participatesInResponse"),
                Modifiers = modifiers,
                TargetQuery = targetQuery,
                TargetFilter = targetFilter,
                TargetDispatch = targetDispatch,
                Projectile = projectile,
                UnitCreation = unitCreation,
                Displacement = displacement,
                Relation = relation,
                RevealArea = revealArea,
                SubmitOrderFromBlackboard = submitOrderFromBlackboard,
                ProgressionScope = progressionScope,
                ProgressionChange = progressionChange,
                ProgressionId = progressionId,
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

        private static DisplacementDescriptor CompileDisplacement(DisplacementConfig cfg, string ownerId, string relativePath)
        {
            if (cfg == null) return default;

            string directionModeValue = RequireString(cfg.DirectionMode, ownerId, relativePath, "displacement.directionMode");
            DisplacementDirectionMode directionMode = directionModeValue switch
            {
                "ToTarget" => DisplacementDirectionMode.ToTarget,
                "AwayFromSource" => DisplacementDirectionMode.AwayFromSource,
                "TowardSource" => DisplacementDirectionMode.TowardSource,
                "Fixed" => DisplacementDirectionMode.Fixed,
                _ => throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: unsupported displacement.directionMode '{directionModeValue}'. " +
                    "Supported: ToTarget, AwayFromSource, TowardSource, Fixed.")
            };

            int totalDistanceCm = RequireInt(cfg.TotalDistanceCm, ownerId, relativePath, "displacement.totalDistanceCm");
            int totalDurationTicks = RequireInt(cfg.TotalDurationTicks, ownerId, relativePath, "displacement.totalDurationTicks");
            int fixedDirectionDeg = directionMode == DisplacementDirectionMode.Fixed
                ? RequireInt(cfg.FixedDirectionDeg, ownerId, relativePath, "displacement.fixedDirectionDeg")
                : cfg.FixedDirectionDeg ?? 0;
            if (directionMode != DisplacementDirectionMode.Fixed)
            {
                RequireAbsent(cfg.FixedDirectionDeg, ownerId, relativePath, "displacement.fixedDirectionDeg", $"directionMode={directionMode}");
            }
            bool overrideNavigation = RequireBool(cfg.OverrideNavigation, ownerId, relativePath, "displacement.overrideNavigation");

            if (totalDistanceCm <= 0)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: displacement.totalDistanceCm must be > 0.");
            }
            if (totalDurationTicks <= 0)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: displacement.totalDurationTicks must be > 0.");
            }

            return new DisplacementDescriptor
            {
                DirectionMode = directionMode,
                FixedDirectionDeg = fixedDirectionDeg,
                TotalDistanceCm = totalDistanceCm,
                TotalDurationTicks = totalDurationTicks,
                OverrideNavigation = overrideNavigation
            };
        }

        private RelationDescriptor CompileRelation(RelationConfig cfg, string ownerId, string relativePath)
        {
            if (cfg == null) return default;

            RelationOperation operation = ParseRelationOperation(cfg.Operation, ownerId, relativePath);
            RelationEntitySlot subject = ParseRelationEntitySlot(
                cfg.Subject,
                ownerId,
                "relation.subject",
                relativePath);
            RelationEntitySlot parent = ParseRelationEntitySlot(
                cfg.Parent,
                ownerId,
                "relation.parent",
                relativePath);
            bool snapSubjectToParentPosition = RequireBool(
                cfg.SnapSubjectToParentPosition,
                ownerId,
                relativePath,
                "relation.snapSubjectToParentPosition");

            if (subject == RelationEntitySlot.None)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: relation.subject cannot be None.");
            }

            if (operation == RelationOperation.SetParent && parent == RelationEntitySlot.None)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: relation.parent cannot be None when operation=SetParent.");
            }

            if (operation == RelationOperation.EnsureLink && parent == RelationEntitySlot.None)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: relation.parent cannot be None when operation=EnsureLink.");
            }

            if (operation != RelationOperation.SetParent && snapSubjectToParentPosition)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: relation.snapSubjectToParentPosition is only valid when operation=SetParent.");
            }

            int relationshipTypeId = 0;
            if (operation == RelationOperation.EnsureLink)
            {
                if (_relationshipTypes == null)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{ownerId}' in {relativePath}: relation.operation=EnsureLink requires RelationshipTypeRegistry.");
                }

                string relationshipType = RequireString(
                    cfg.RelationshipType,
                    ownerId,
                    relativePath,
                    "relation.relationshipType");
                relationshipTypeId = _relationshipTypes.GetId(relationshipType);
                if (relationshipTypeId < 0)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{ownerId}' in {relativePath}: relation.relationshipType '{relationshipType}' is not registered.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(cfg.RelationshipType))
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: relation.relationshipType is only valid when operation=EnsureLink.");
            }

            return new RelationDescriptor
            {
                Operation = operation,
                Subject = subject,
                Parent = parent,
                SnapSubjectToParentPosition = snapSubjectToParentPosition,
                RelationshipTypeId = relationshipTypeId
            };
        }

        private KnowledgeAreaRevealDescriptor CompileRevealArea(RevealAreaConfig cfg, string ownerId, string relativePath)
        {
            if (cfg == null) return default;

            if (_progressionScopeKeys == null)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: revealArea.scope requires ScopeKeyRegistry.");
            }

            if (_fogLayers == null)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: revealArea.layers requires FogLayerRegistry.");
            }

            int radiusCm = RequirePositiveInt(cfg.Radius, ownerId, relativePath, "revealArea.radius");
            string scope = RequireString(cfg.Scope, ownerId, relativePath, "revealArea.scope");
            int scopeKeyId = _progressionScopeKeys.GetId(scope);
            if (scopeKeyId <= 0)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: revealArea.scope '{scope}' is not registered.");
            }

            if (cfg.Layers == null || cfg.Layers.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: revealArea.layers requires at least one layer.");
            }

            if (cfg.Layers.Count > KnowledgeAreaRevealDescriptor.MaxLayers)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: revealArea.layers supports at most {KnowledgeAreaRevealDescriptor.MaxLayers} layers.");
            }

            Span<FogLayerId> layers = stackalloc FogLayerId[KnowledgeAreaRevealDescriptor.MaxLayers];
            for (int i = 0; i < cfg.Layers.Count; i++)
            {
                string layerName = RequireString(cfg.Layers[i], ownerId, relativePath, $"revealArea.layers[{i}]");
                FogLayerId layerId = _fogLayers.GetId(layerName);
                if (layerId.Value <= 0)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{ownerId}' in {relativePath}: revealArea.layers[{i}] '{layerName}' is not registered.");
                }

                layers[i] = layerId;
            }

            int memoryTtlTicks = cfg.MemoryTtlTicks ?? 0;
            if (memoryTtlTicks < 0)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: revealArea.memoryTtlTicks must be >= 0.");
            }

            int detectionStrength = cfg.DetectionStrength ?? 0;
            if ((uint)detectionStrength > byte.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: revealArea.detectionStrength must be in range 0..255.");
            }

            return new KnowledgeAreaRevealDescriptor(
                scopeKeyId,
                radiusCm,
                layers[..cfg.Layers.Count],
                memoryTtlTicks,
                (byte)detectionStrength);
        }

        private static SubmitOrderFromBlackboardDescriptor CompileSubmitOrderFromBlackboard(
            SubmitOrderFromBlackboardConfig? cfg,
            string ownerId,
            string relativePath)
        {
            if (cfg == null)
            {
                return default;
            }

            RelationEntitySlot sourceSlot = string.IsNullOrWhiteSpace(cfg.Source)
                ? RelationEntitySlot.Source
                : ParseRelationEntitySlot(cfg.Source, ownerId, "submitOrderFromBlackboard.source", relativePath);
            RelationEntitySlot targetSlot = string.IsNullOrWhiteSpace(cfg.Target)
                ? RelationEntitySlot.Target
                : ParseRelationEntitySlot(cfg.Target, ownerId, "submitOrderFromBlackboard.target", relativePath);
            BlackboardStoredTargetKeys storedTargetKeys = CompileStoredTargetKeys(
                cfg.StoredTarget,
                ownerId,
                relativePath,
                "submitOrderFromBlackboard.storedTarget");
            string pointMoveOrderTypeKey = RequireString(cfg.PointMoveOrderTypeKey, ownerId, relativePath, "submitOrderFromBlackboard.pointMoveOrderTypeKey");
            string entityOrderTypeKey = RequireString(cfg.EntityOrderTypeKey, ownerId, relativePath, "submitOrderFromBlackboard.entityOrderTypeKey");
            int entityOrderIntArg0 = RequireInt(cfg.EntityOrderIntArg0, ownerId, relativePath, "submitOrderFromBlackboard.entityOrderIntArg0");
            OrderSubmitMode submitMode = ParseOrderSubmitMode(
                RequireString(cfg.SubmitMode, ownerId, relativePath, "submitOrderFromBlackboard.submitMode"),
                ownerId,
                relativePath);

            if (sourceSlot == RelationEntitySlot.None || targetSlot == RelationEntitySlot.None)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: submitOrderFromBlackboard source and target cannot be None.");
            }

            return new SubmitOrderFromBlackboardDescriptor
            {
                SourceSlot = sourceSlot,
                TargetSlot = targetSlot,
                StoredTargetKeys = storedTargetKeys,
                PointMoveOrderTypeKey = pointMoveOrderTypeKey,
                EntityOrderTypeKey = entityOrderTypeKey,
                EntityOrderIntArg0 = entityOrderIntArg0,
                SubmitMode = submitMode,
            };
        }

        private static BlackboardStoredTargetKeys CompileStoredTargetKeys(
            StoredTargetKeysConfig? cfg,
            string ownerId,
            string relativePath,
            string blockName)
        {
            if (cfg == null)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: {blockName} must be defined.");
            }

            return new BlackboardStoredTargetKeys(
                ResolveConfiguredBlackboardKey(RequireString(cfg.TargetKindKey, ownerId, relativePath, $"{blockName}.targetKindKey"), ownerId, relativePath, $"{blockName}.targetKindKey"),
                ResolveConfiguredBlackboardKey(RequireString(cfg.TargetPositionKey, ownerId, relativePath, $"{blockName}.targetPositionKey"), ownerId, relativePath, $"{blockName}.targetPositionKey"),
                ResolveConfiguredBlackboardKey(RequireString(cfg.TargetEntityKey, ownerId, relativePath, $"{blockName}.targetEntityKey"), ownerId, relativePath, $"{blockName}.targetEntityKey"),
                ResolveConfiguredBlackboardKey(RequireString(cfg.HexQKey, ownerId, relativePath, $"{blockName}.hexQKey"), ownerId, relativePath, $"{blockName}.hexQKey"),
                ResolveConfiguredBlackboardKey(RequireString(cfg.HexRKey, ownerId, relativePath, $"{blockName}.hexRKey"), ownerId, relativePath, $"{blockName}.hexRKey"));
        }

        private static int ResolveConfiguredBlackboardKey(string text, string ownerId, string relativePath, string fieldName)
        {
            if (OrderBlackboardKeyRegistry.TryGetId(text, out int blackboardKey))
            {
                return blackboardKey;
            }

            throw new InvalidOperationException(
                $"Effect template '{ownerId}' in {relativePath}: unknown {fieldName} '{text}'.");
        }

        private static OrderSubmitMode ParseOrderSubmitMode(string raw, string ownerId, string relativePath)
        {
            return raw switch
            {
                "Immediate" => OrderSubmitMode.Immediate,
                "Queued" => OrderSubmitMode.Queued,
                _ => throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: unsupported submitOrderFromBlackboard.submitMode '{raw}'. Supported: Immediate, Queued.")
            };
        }

        private static ProjectileDescriptor CompileProjectile(ProjectileConfig cfg, string ownerId, string relativePath)
        {
            if (cfg == null) return default;

            ProjectileTravelMode travelMode = ParseProjectileTravelMode(cfg.TravelMode);
            ProjectileImpactPolicy impactPolicy = ParseProjectileImpactPolicy(cfg.ImpactPolicy);
            bool isLegacyTravel = travelMode == ProjectileTravelMode.Legacy;
            bool isLegacyImpact = impactPolicy == ProjectileImpactPolicy.Legacy;
            if (isLegacyTravel != isLegacyImpact)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: projectile.travelMode and projectile.impactPolicy must both be Legacy or both be non-Legacy.");
            }

            int impactId = ResolveOptionalEffectTemplateId(cfg.ImpactEffect, ownerId, relativePath, "projectile.impactEffect");
            int hitId = ResolveOptionalEffectTemplateId(cfg.HitEffect, ownerId, relativePath, "projectile.hitEffect");
            int presentationId = ResolveOptionalEffectTemplateId(cfg.PresentationEffect, ownerId, relativePath, "projectile.presentationEffect");
            int collisionHalfWidth = cfg.CollisionHalfWidth ?? 0;
            RelationshipFilter collisionRelationFilter = RelationshipFilter.All;
            if (isLegacyImpact)
            {
                RequireAbsent(cfg.HitEffect, ownerId, relativePath, "projectile.hitEffect", "impactPolicy=Legacy");
                RequireAbsent(cfg.CollisionRelationFilter, ownerId, relativePath, "projectile.collisionRelationFilter", "impactPolicy=Legacy");
                RequireAbsent(cfg.CollisionHalfWidth, ownerId, relativePath, "projectile.collisionHalfWidth", "impactPolicy=Legacy");
                RequireAbsent(cfg.CollisionExcludeSource, ownerId, relativePath, "projectile.collisionExcludeSource", "impactPolicy=Legacy");
                RequireAbsent(cfg.MaxHitCount, ownerId, relativePath, "projectile.maxHitCount", "impactPolicy=Legacy");
            }

            if (impactPolicy != ProjectileImpactPolicy.Legacy)
            {
                if (hitId <= 0)
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: projectile.impactPolicy '{impactPolicy}' requires projectile.hitEffect.");
                }

                if (collisionHalfWidth <= 0)
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: projectile.impactPolicy '{impactPolicy}' requires projectile.collisionHalfWidth > 0.");
                }

                collisionRelationFilter = ParseRequiredRelationshipFilter(
                    cfg.CollisionRelationFilter,
                    ownerId,
                    "projectile.collisionRelationFilter",
                    relativePath);
            }
            else if (!string.IsNullOrWhiteSpace(cfg.CollisionRelationFilter))
            {
                collisionRelationFilter = RelationshipFilterUtil.Parse(cfg.CollisionRelationFilter);
            }

            RejectOptionalFalse(cfg.CollisionExcludeSource, ownerId, relativePath, "projectile.collisionExcludeSource");
            if (cfg.MaxHitCount is < 0)
            {
                throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: projectile.maxHitCount must be non-negative.");
            }

            return new ProjectileDescriptor
            {
                Speed = RequireInt(cfg.Speed, ownerId, relativePath, "projectile.speed"),
                Range = RequireInt(cfg.Range, ownerId, relativePath, "projectile.range"),
                ArcHeight = RequireInt(cfg.ArcHeight, ownerId, relativePath, "projectile.arcHeight"),
                ImpactEffectTemplateId = impactId,
                HitEffectTemplateId = hitId,
                PresentationEffectTemplateId = presentationId,
                TravelMode = travelMode,
                ImpactPolicy = impactPolicy,
                CollisionHalfWidthCm = collisionHalfWidth,
                CollisionRelationFilter = collisionRelationFilter,
                CollisionExcludeSource = cfg.CollisionExcludeSource ?? false,
                MaxHitCount = cfg.MaxHitCount ?? 0
            };
        }

        private static int ResolveOptionalEffectTemplateId(string? raw, string effectId, string path, string fieldPath)
        {
            if (raw == null)
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException($"Effect template '{effectId}' in {path}: {fieldPath} must be omitted or a semantic key.");
            }

            int templateId = EffectTemplateIdRegistry.GetId(raw);
            if (templateId <= 0)
            {
                throw new InvalidOperationException($"Effect template '{effectId}' in {path}: {fieldPath} references unknown effect template '{raw}'.");
            }

            return templateId;
        }

        private static ProjectileTravelMode ParseProjectileTravelMode(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException("projectile.travelMode is required.");
            }

            return raw switch
            {
                "Legacy" => ProjectileTravelMode.Legacy,
                "Direction" => ProjectileTravelMode.Direction,
                "TrackTarget" => ProjectileTravelMode.TrackTarget,
                _ => throw new InvalidOperationException($"Unsupported projectile.travelMode '{raw}'.")
            };
        }

        private static ProjectileImpactPolicy ParseProjectileImpactPolicy(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException("projectile.impactPolicy is required.");
            }

            return raw switch
            {
                "Legacy" => ProjectileImpactPolicy.Legacy,
                "DestroyOnFirstHit" => ProjectileImpactPolicy.DestroyOnFirstHit,
                "ContinueOnHit" => ProjectileImpactPolicy.ContinueOnHit,
                _ => throw new InvalidOperationException($"Unsupported projectile.impactPolicy '{raw}'.")
            };
        }

        private static UnitCreationDescriptor CompileUnitCreation(UnitCreationConfig cfg, string ownerId, string relativePath)
        {
            if (cfg == null) return default;

            bool hasUnitType = !string.IsNullOrWhiteSpace(cfg.UnitType);
            bool hasTemplateId = !string.IsNullOrWhiteSpace(cfg.TemplateId);
            if (hasUnitType == hasTemplateId)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: unitCreation must declare exactly one of unitType or templateId.");
            }

            int unitTypeId = 0;
            if (hasUnitType)
            {
                unitTypeId = UnitTypeRegistry.Register(cfg.UnitType);
                if (unitTypeId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{ownerId}' in {relativePath}: unitCreation.unitType is required.");
                }
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

            UnitCreationPlacementPattern placementPattern = ParseUnitCreationPlacementPattern(cfg.PlacementPattern);
            UnitCreationFacingPattern facingPattern = string.IsNullOrWhiteSpace(cfg.FacingPattern)
                ? UnitCreationFacingPattern.PreserveTemplate
                : ParseUnitCreationFacingPattern(cfg.FacingPattern);
            int offsetRadius = cfg.OffsetRadius ?? 0;
            int placementRadiusCm = cfg.PlacementRadiusCm ?? 0;
            int placementStartAngleDeg = cfg.PlacementStartAngleDeg ?? 0;

            if (placementPattern == UnitCreationPlacementPattern.Scatter)
            {
                RequireAbsent(cfg.FacingPattern, ownerId, relativePath, "unitCreation.facingPattern", "placementPattern=Scatter");
                RequireAbsent(cfg.PlacementRadiusCm, ownerId, relativePath, "unitCreation.placementRadiusCm", "placementPattern=Scatter");
                RequireAbsent(cfg.PlacementStartAngleDeg, ownerId, relativePath, "unitCreation.placementStartAngleDeg", "placementPattern=Scatter");
                offsetRadius = cfg.OffsetRadius ?? 0;
                if (offsetRadius < 0)
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: unitCreation.offsetRadius must be non-negative.");
                }
            }
            else if (placementPattern == UnitCreationPlacementPattern.Circle)
            {
                RequireAbsent(cfg.OffsetRadius, ownerId, relativePath, "unitCreation.offsetRadius", "placementPattern=Circle");
                placementRadiusCm = RequireInt(cfg.PlacementRadiusCm, ownerId, relativePath, "unitCreation.placementRadiusCm");
                placementStartAngleDeg = RequireInt(cfg.PlacementStartAngleDeg, ownerId, relativePath, "unitCreation.placementStartAngleDeg");
                if (placementRadiusCm <= 0)
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: unitCreation.placementRadiusCm must be > 0 when placementPattern=Circle.");
                }
            }

            RejectOptionalFalse(cfg.CopySourcePlayerOwner, ownerId, relativePath, "unitCreation.copySourcePlayerOwner");
            RejectOptionalFalse(cfg.LinkSourceAsParent, ownerId, relativePath, "unitCreation.linkSourceAsParent");

            return new UnitCreationDescriptor
            {
                PlacementPattern = placementPattern,
                FacingPattern = facingPattern,
                UnitTypeId = unitTypeId,
                TemplateId = hasTemplateId ? cfg.TemplateId : string.Empty,
                UseTemplateSpawn = hasTemplateId,
                Count = RequireInt(cfg.Count, ownerId, relativePath, "unitCreation.count"),
                OffsetRadius = offsetRadius,
                PlacementRadiusCm = placementRadiusCm,
                PlacementStartAngleDeg = placementStartAngleDeg,
                OnSpawnEffectTemplateId = onSpawnId,
                CopySourcePlayerOwner = cfg.CopySourcePlayerOwner ?? false,
                LinkSourceAsParent = cfg.LinkSourceAsParent ?? false,
            };
        }

        private static UnitCreationPlacementPattern ParseUnitCreationPlacementPattern(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException("unitCreation.placementPattern is required.");
            }

            return raw switch
            {
                "Scatter" => UnitCreationPlacementPattern.Scatter,
                "Circle" => UnitCreationPlacementPattern.Circle,
                _ => throw new InvalidOperationException($"Unsupported unitCreation.placementPattern '{raw}'.")
            };
        }

        private static UnitCreationFacingPattern ParseUnitCreationFacingPattern(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException("unitCreation.facingPattern is required.");
            }

            return raw switch
            {
                "PreserveTemplate" => UnitCreationFacingPattern.PreserveTemplate,
                "RadialOutward" => UnitCreationFacingPattern.RadialOutward,
                "TangentClockwise" => UnitCreationFacingPattern.TangentClockwise,
                "TangentCounterClockwise" => UnitCreationFacingPattern.TangentCounterClockwise,
                _ => throw new InvalidOperationException($"Unsupported unitCreation.facingPattern '{raw}'.")
            };
        }

        private static RelationOperation ParseRelationOperation(string? value, string ownerId, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: relation.operation is required.");
            }

            return value switch
            {
                "SetParent" => RelationOperation.SetParent,
                "RemoveParent" => RelationOperation.RemoveParent,
                "EnsureLink" => RelationOperation.EnsureLink,
                _ => throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: unsupported relation.operation '{value}'. Supported: SetParent, RemoveParent, EnsureLink.")
            };
        }

        private static ModifierOp ParseModifierOp(string? op, string ownerId, string relativePath, int modifierIndex)
        {
            if (string.IsNullOrWhiteSpace(op))
            {
                throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: modifier[{modifierIndex}].op is required.");
            }

            if (op == "Add") return ModifierOp.Add;
            if (op == "Multiply") return ModifierOp.Multiply;
            if (op == "Override") return ModifierOp.Override;

            throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: modifier[{modifierIndex}] unsupported op '{op}'. Supported: Add, Multiply, Override.");
        }

        private static SpatialShape ParseSpatialShape(string shape, string ownerId, string relativePath)
        {
            if (shape == "Circle") return SpatialShape.Circle;
            if (shape == "Cone") return SpatialShape.Cone;
            if (shape == "Rectangle") return SpatialShape.Rectangle;
            if (shape == "Line") return SpatialShape.Line;
            if (shape == "Ring") return SpatialShape.Ring;
            throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: unsupported targetResolver.shape '{shape}'.");
        }

        private static RelationEntitySlot ParseRelationEntitySlot(
            string? slot,
            string ownerId,
            string fieldPath,
            string relativePath)
        {
            if (string.IsNullOrWhiteSpace(slot))
            {
                throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: {fieldPath} is required.");
            }

            if (slot == "None") return RelationEntitySlot.None;
            if (slot == "Source") return RelationEntitySlot.Source;
            if (slot == "Target") return RelationEntitySlot.Target;
            if (slot == "TargetContext") return RelationEntitySlot.TargetContext;

            throw new InvalidOperationException(
                $"Effect template '{ownerId}' in {relativePath}: {fieldPath} uses unsupported entity slot '{slot}'. Supported: None, Source, Target, TargetContext.");
        }
        private int ParsePresetTypeId(string? presetType, string ownerId, string relativePath)
        {
            string context = $"Effect template '{ownerId}' in {relativePath}";
            if (string.IsNullOrWhiteSpace(presetType))
            {
                throw new InvalidOperationException($"{context}: presetType must be explicitly defined.");
            }

            if (_presetTypes != null && _presetTypes.TryGetId(presetType, out int typeId) && _presetTypes.IsRegistered(typeId))
            {
                return typeId;
            }

            throw new InvalidOperationException(
                $"{context}: presetType '{presetType}' is not registered in preset_types.json or a loaded mod extension.");
        }

        private static EffectPresetType ResolveBuiltinPresetType(int presetTypeId)
        {
            if ((uint)presetTypeId <= byte.MaxValue &&
                Enum.IsDefined(typeof(EffectPresetType), (byte)presetTypeId))
            {
                return (EffectPresetType)(byte)presetTypeId;
            }

            return EffectPresetType.None;
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
                if (phaseCfg == null)
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: phaseGraphs.{kvp.Key} must be an object.");
                }

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

        private void CompileConfigParams(
            Dictionary<string, ConfigParamConfig> configParams,
            ref EffectConfigParams result,
            string ownerId,
            string relativePath)
        {
            foreach (var kvp in configParams)
            {
                var paramCfg = kvp.Value;
                if (paramCfg == null)
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams.{kvp.Key} must be an object.");
                }

                // Use a deterministic key ID from the config key name.
                int keyId = ConfigKeyRegistry.Register(kvp.Key);

                string type = RequireString(paramCfg.Type, ownerId, relativePath, $"configParams.{kvp.Key}.type");
                if (paramCfg.Value == null)
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams.{kvp.Key}.value is required.");
                }

                if (type == "Float")
                {
                    float val = paramCfg.Value is JsonElement jf ? jf.GetSingle() : Convert.ToSingle(paramCfg.Value, CultureInfo.InvariantCulture);
                    if (!result.TryAddFloat(keyId, val))
                    {
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams exceeded capacity ({EffectConfigParams.MAX_PARAMS}).");
                    }
                }
                else if (type == "Int")
                {
                    int val = paramCfg.Value is JsonElement ji ? ji.GetInt32() : Convert.ToInt32(paramCfg.Value, CultureInfo.InvariantCulture);
                    if (!result.TryAddInt(keyId, val))
                    {
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams exceeded capacity ({EffectConfigParams.MAX_PARAMS}).");
                    }
                }
                else if (type == "EffectTemplate")
                {
                    string templateName = paramCfg.Value.ToString()
                        ?? throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams.{kvp.Key}.value must convert to a string.");
                    if (string.IsNullOrWhiteSpace(templateName))
                    {
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams.{kvp.Key} effectTemplate type requires a non-empty effect template id.");
                    }

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
                else if (type == "Attribute")
                {
                    string attrName = paramCfg.Value.ToString()
                        ?? throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams.{kvp.Key}.value must convert to a string.");
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
                else if (type == "ExchangeOperation")
                {
                    string operationName = paramCfg.Value.ToString()
                        ?? throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams.{kvp.Key}.value must convert to a string.");
                    if (string.IsNullOrWhiteSpace(operationName))
                    {
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams.{kvp.Key} exchange operation type requires a non-empty operation id.");
                    }

                    if (_exchangeOperations == null)
                    {
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: ExchangeOperation config param requires ExchangeOperationRegistry.");
                    }

                    int operationId = _exchangeOperations.GetId(operationName);
                    if (operationId <= 0)
                    {
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams.{kvp.Key} references unknown exchange operation '{operationName}'.");
                    }

                    if (!result.TryAddInt(keyId, operationId))
                    {
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams exceeded capacity ({EffectConfigParams.MAX_PARAMS}).");
                    }
                }
                else if (type == "EntityTemplate")
                {
                    string templateName = paramCfg.Value.ToString()
                        ?? throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams.{kvp.Key}.value must convert to a string.");
                    if (string.IsNullOrWhiteSpace(templateName))
                    {
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams.{kvp.Key} entity template type requires a non-empty template id.");
                    }

                    if (_entityTemplateKeys == null)
                    {
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: EntityTemplate config param requires EntityTemplateKeyRegistry.");
                    }

                    int templateKeyId = _entityTemplateKeys.Register(templateName);
                    if (!result.TryAddEntityTemplateKeyId(keyId, templateKeyId))
                    {
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams exceeded capacity ({EffectConfigParams.MAX_PARAMS}).");
                    }
                }
                else if (type == "LifecycleAttributeValueSource")
                {
                    string sourceName = paramCfg.Value.ToString()
                        ?? throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams.{kvp.Key}.value must convert to a string.");
                    int sourceValue = ParseLifecycleAttributeValueSource(
                        sourceName,
                        ownerId,
                        relativePath,
                        $"configParams.{kvp.Key}.value");

                    if (!result.TryAddLifecycleAttributeValueSource(keyId, sourceValue))
                    {
                        throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams exceeded capacity ({EffectConfigParams.MAX_PARAMS}).");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams.{kvp.Key} has unsupported type '{type}'. Supported: Float, Int, EffectTemplate, Attribute, ExchangeOperation, EntityTemplate, LifecycleAttributeValueSource.");
                }
            }
        }

        private static void RequireDeployConsumeSourceLifecycleConfig(
            in EffectConfigParams configParams,
            string effectId,
            string relativePath)
        {
            if (!configParams.TryGetLifecycleAttributeValueSource(EffectParamKeys.LifecycleAttributeValueSource, out int rawSource) ||
                (rawSource != (int)LifecycleAttributeValueSource.Base &&
                 rawSource != (int)LifecycleAttributeValueSource.Current))
            {
                throw new InvalidOperationException(
                    $"Effect template '{effectId}' in {relativePath}: presetType DeployConsumeSource requires configParams \"_ep.lifecycleAttributeValueSource\" with type \"LifecycleAttributeValueSource\".");
            }

            for (int i = 0; i < EffectParamKeys.LifecycleAttributeCapacity; i++)
            {
                int keyId = EffectParamKeys.GetLifecycleAttributeKey(i);
                if (configParams.TryGetAttributeIdStrict(keyId, out int attributeId) && attributeId >= 0)
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                $"Effect template '{effectId}' in {relativePath}: presetType DeployConsumeSource requires at least one configParams \"_ep.lifecycleAttributeN\" entry with type \"Attribute\".");
        }

        private static int ParseLifecycleAttributeValueSource(
            string raw,
            string effectId,
            string relativePath,
            string fieldPath)
        {
            return raw switch
            {
                "Base" => (int)LifecycleAttributeValueSource.Base,
                "Current" => (int)LifecycleAttributeValueSource.Current,
                _ => throw new InvalidOperationException(
                    $"Effect template '{effectId}' in {relativePath}: {fieldPath} has unsupported lifecycle attribute value source '{raw}'. Supported: Base, Current."),
            };
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
                if (lc == null)
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: phaseListeners[{i}] must be an object.");
                }

                // Phase
                if (string.IsNullOrWhiteSpace(lc.Phase) || !GasEnumParser.TryParsePhaseId(lc.Phase, out var phaseId))
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: phaseListeners[{i}] has invalid phase '{lc.Phase}'.");
                }

                // Scope
                PhaseListenerScope scope;
                if (string.IsNullOrWhiteSpace(lc.Scope))
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: phaseListeners[{i}].scope is required.");
                }
                if (lc.Scope == "Source")
                    scope = PhaseListenerScope.Source;
                else if (lc.Scope == "Target")
                    scope = PhaseListenerScope.Target;
                else
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: phaseListeners[{i}] has unknown scope '{lc.Scope}'. Supported: Source, Target.");

                // Action
                PhaseListenerActionFlags flags;
                if (string.IsNullOrWhiteSpace(lc.Action))
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: phaseListeners[{i}].action is required.");
                }
                if (lc.Action == "Graph")
                    flags = PhaseListenerActionFlags.ExecuteGraph;
                else if (lc.Action == "Event")
                    flags = PhaseListenerActionFlags.PublishEvent;
                else if (lc.Action == "Both")
                    flags = PhaseListenerActionFlags.Both;
                else
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: phaseListeners[{i}] has unknown action '{lc.Action}'. Supported: Graph, Event, Both.");

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

                int priority = lc.Priority ?? 0;
                if (!result.TryAddTemplate(listenTagId, listenEffectId, phaseId, scope, flags, graphProgramId, eventTagId, priority))
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: phaseListeners exceeded capacity ({EffectPhaseListenerBuffer.CAPACITY}).");
                }
            }
        }

        // ── New schema parse helpers ──

        private static EffectLifetimeKind ParseLifetimeKind(string? value, string effectId, string path)
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

        private ScopeKey ResolveProgressionScope(string? rawValue, string effectId, string path)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                throw new InvalidOperationException(
                    $"Effect template '{effectId}' in {path}: progression.scope is required. Use 'self', 'explicit', or a named scope declared in Progression/scopes.json.");
            }

            return ParseProgressionScope(rawValue.Trim(), effectId, path);
        }

        private ScopeKey ParseProgressionScope(string value, string effectId, string path)
        {
            if (_progressionScopeKeys == null)
            {
                throw new InvalidOperationException(
                    $"Effect template '{effectId}' in {path}: progression.scope requires ScopeKeyRegistry.");
            }

            switch (value)
            {
                case "self":
                    return ScopeKey.Self;
                case "explicit":
                    return new ScopeKey(ScopeKind.Explicit);
                default:
                    if (_progressionScopeKeys.TryGetId(value, out int scopeKeyId) && scopeKeyId > 0)
                    {
                        return new ScopeKey(ScopeKind.Named, scopeKeyId);
                    }

                    throw new InvalidOperationException(
                        $"Effect template '{effectId}' in {path}: progression.scope '{value}' is not registered by Progression config.");
            }
        }

        private static ProgressionLevelChange CompileProgressionChange(ProgressionCompletionConfig cfg, string effectId, string path)
        {
            if (cfg.Level.HasValue && cfg.Delta.HasValue)
            {
                throw new InvalidOperationException(
                    $"Effect template '{effectId}' in {path}: progression.level and progression.delta are mutually exclusive.");
            }

            if (cfg.Level.HasValue)
            {
                int level = cfg.Level.Value;
                if (level <= 0)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{effectId}' in {path}: progression.level must be greater than zero.");
                }

                return new ProgressionLevelChange(level, 0);
            }

            if (cfg.Delta.HasValue)
            {
                int delta = cfg.Delta.Value;
                if (delta <= 0)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{effectId}' in {path}: progression.delta must be greater than zero.");
                }

                return new ProgressionLevelChange(0, delta);
            }

            return ProgressionLevelChange.Complete;
        }

        private static TargetQueryDescriptor CompileTargetQuery(TargetQueryConfig cfg, string effectId, string path)
        {
            var desc = default(TargetQueryDescriptor);
            string kind = RequireString(cfg.Kind, effectId, path, "targetQuery.kind");
            desc.Kind = kind switch
            {
                "BuiltinSpatial" => TargetResolverKind.BuiltinSpatial,
                "GraphProgram" => TargetResolverKind.GraphProgram,
                _ => throw new InvalidOperationException($"Effect template '{effectId}' in {path}: targetQuery.kind has unsupported value '{kind}'.")
            };

            if (desc.Kind == TargetResolverKind.BuiltinSpatial)
            {
                RequireAbsent(cfg.GraphProgramId, effectId, path, "targetQuery.graphProgramId", "kind=BuiltinSpatial");
                desc.Spatial = CompileBuiltinSpatialQuery(cfg, effectId, path);
            }
            else
            {
                RequireAbsent(cfg.Shape, effectId, path, "targetQuery.shape", "kind=GraphProgram");
                RequireAbsent(cfg.Radius, effectId, path, "targetQuery.radius", "kind=GraphProgram");
                RequireAbsent(cfg.InnerRadius, effectId, path, "targetQuery.innerRadius", "kind=GraphProgram");
                RequireAbsent(cfg.HalfAngle, effectId, path, "targetQuery.halfAngle", "kind=GraphProgram");
                RequireAbsent(cfg.HalfWidth, effectId, path, "targetQuery.halfWidth", "kind=GraphProgram");
                RequireAbsent(cfg.HalfHeight, effectId, path, "targetQuery.halfHeight", "kind=GraphProgram");
                RequireAbsent(cfg.Rotation, effectId, path, "targetQuery.rotation", "kind=GraphProgram");
                RequireAbsent(cfg.Length, effectId, path, "targetQuery.length", "kind=GraphProgram");
                desc.GraphProgramId = RequireInt(cfg.GraphProgramId, effectId, path, "targetQuery.graphProgramId");
                if (desc.GraphProgramId <= 0)
                {
                    throw new InvalidOperationException($"Effect template '{effectId}' in {path}: targetQuery.graphProgramId must be > 0 when kind=GraphProgram.");
                }
            }

            return desc;
        }

        private static BuiltinSpatialDescriptor CompileBuiltinSpatialQuery(TargetQueryConfig cfg, string effectId, string path)
        {
            var spatial = default(BuiltinSpatialDescriptor);
            spatial.Shape = ParseSpatialShape(RequireString(cfg.Shape, effectId, path, "targetQuery.shape"), effectId, path);

            switch (spatial.Shape)
            {
                case SpatialShape.Circle:
                    RequireAbsent(cfg.InnerRadius, effectId, path, "targetQuery.innerRadius", "shape=Circle");
                    RequireAbsent(cfg.HalfAngle, effectId, path, "targetQuery.halfAngle", "shape=Circle");
                    RequireAbsent(cfg.HalfWidth, effectId, path, "targetQuery.halfWidth", "shape=Circle");
                    RequireAbsent(cfg.HalfHeight, effectId, path, "targetQuery.halfHeight", "shape=Circle");
                    RequireAbsent(cfg.Rotation, effectId, path, "targetQuery.rotation", "shape=Circle");
                    RequireAbsent(cfg.Length, effectId, path, "targetQuery.length", "shape=Circle");
                    spatial.RadiusCm = RequirePositiveInt(cfg.Radius, effectId, path, "targetQuery.radius");
                    break;
                case SpatialShape.Ring:
                    RequireAbsent(cfg.HalfAngle, effectId, path, "targetQuery.halfAngle", "shape=Ring");
                    RequireAbsent(cfg.HalfWidth, effectId, path, "targetQuery.halfWidth", "shape=Ring");
                    RequireAbsent(cfg.HalfHeight, effectId, path, "targetQuery.halfHeight", "shape=Ring");
                    RequireAbsent(cfg.Rotation, effectId, path, "targetQuery.rotation", "shape=Ring");
                    RequireAbsent(cfg.Length, effectId, path, "targetQuery.length", "shape=Ring");
                    spatial.RadiusCm = RequirePositiveInt(cfg.Radius, effectId, path, "targetQuery.radius");
                    spatial.InnerRadiusCm = RequireInt(cfg.InnerRadius, effectId, path, "targetQuery.innerRadius");
                    if (spatial.InnerRadiusCm < 0 || spatial.InnerRadiusCm >= spatial.RadiusCm)
                    {
                        throw new InvalidOperationException($"Effect template '{effectId}' in {path}: targetQuery.innerRadius must be >= 0 and < targetQuery.radius for shape=Ring.");
                    }
                    break;
                case SpatialShape.Cone:
                    RequireAbsent(cfg.InnerRadius, effectId, path, "targetQuery.innerRadius", "shape=Cone");
                    RequireAbsent(cfg.HalfWidth, effectId, path, "targetQuery.halfWidth", "shape=Cone");
                    RequireAbsent(cfg.HalfHeight, effectId, path, "targetQuery.halfHeight", "shape=Cone");
                    RequireAbsent(cfg.Rotation, effectId, path, "targetQuery.rotation", "shape=Cone");
                    RequireAbsent(cfg.Length, effectId, path, "targetQuery.length", "shape=Cone");
                    spatial.RadiusCm = RequirePositiveInt(cfg.Radius, effectId, path, "targetQuery.radius");
                    spatial.HalfAngleDeg = RequirePositiveInt(cfg.HalfAngle, effectId, path, "targetQuery.halfAngle");
                    break;
                case SpatialShape.Line:
                    RequireAbsent(cfg.Radius, effectId, path, "targetQuery.radius", "shape=Line");
                    RequireAbsent(cfg.InnerRadius, effectId, path, "targetQuery.innerRadius", "shape=Line");
                    RequireAbsent(cfg.HalfAngle, effectId, path, "targetQuery.halfAngle", "shape=Line");
                    RequireAbsent(cfg.HalfHeight, effectId, path, "targetQuery.halfHeight", "shape=Line");
                    RequireAbsent(cfg.Rotation, effectId, path, "targetQuery.rotation", "shape=Line");
                    spatial.LengthCm = RequirePositiveInt(cfg.Length, effectId, path, "targetQuery.length");
                    spatial.HalfWidthCm = RequirePositiveInt(cfg.HalfWidth, effectId, path, "targetQuery.halfWidth");
                    break;
                case SpatialShape.Rectangle:
                    RequireAbsent(cfg.Radius, effectId, path, "targetQuery.radius", "shape=Rectangle");
                    RequireAbsent(cfg.InnerRadius, effectId, path, "targetQuery.innerRadius", "shape=Rectangle");
                    RequireAbsent(cfg.HalfAngle, effectId, path, "targetQuery.halfAngle", "shape=Rectangle");
                    RequireAbsent(cfg.Length, effectId, path, "targetQuery.length", "shape=Rectangle");
                    spatial.HalfWidthCm = RequirePositiveInt(cfg.HalfWidth, effectId, path, "targetQuery.halfWidth");
                    spatial.HalfHeightCm = RequirePositiveInt(cfg.HalfHeight, effectId, path, "targetQuery.halfHeight");
                    spatial.RotationDeg = cfg.Rotation ?? 0;
                    break;
            }

            return spatial;
        }

        private static TargetFilterDescriptor CompileTargetFilter(TargetFilterConfig cfg, string effectId, string path)
        {
            var desc = default(TargetFilterDescriptor);
            desc.ExcludeSource = RequireBool(cfg.ExcludeSource, effectId, path, "targetFilter.excludeSource");
            desc.MaxTargets = RequireInt(cfg.MaxTargets, effectId, path, "targetFilter.maxTargets");
            desc.RelationFilter = ParseRequiredRelationshipFilter(
                cfg.RelationFilter,
                effectId,
                "targetFilter.relationFilter",
                path);
            if (cfg.LayerMask != null)
                desc.LayerMask = ParseLayerMask(cfg.LayerMask);
            return desc;
        }

        private static RelationshipFilter ParseRequiredRelationshipFilter(
            string? raw,
            string effectId,
            string fieldPath,
            string path)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException($"Effect template '{effectId}' in {path}: {fieldPath} is required.");
            }

            return RelationshipFilterUtil.Parse(raw);
        }

        private static string RequireString(string? raw, string effectId, string path, string fieldPath)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException($"Effect template '{effectId}' in {path}: {fieldPath} is required.");
            }

            return raw;
        }

        private static int RequireInt(int? value, string effectId, string path, string fieldPath)
        {
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Effect template '{effectId}' in {path}: {fieldPath} is required.");
            }

            return value.Value;
        }

        private static int RequirePositiveInt(int? value, string effectId, string path, string fieldPath)
        {
            int result = RequireInt(value, effectId, path, fieldPath);
            if (result <= 0)
            {
                throw new InvalidOperationException($"Effect template '{effectId}' in {path}: {fieldPath} must be > 0.");
            }

            return result;
        }

        private static void RequireAbsent(int? value, string effectId, string path, string fieldPath, string when)
        {
            if (value.HasValue)
            {
                throw new InvalidOperationException($"Effect template '{effectId}' in {path}: {fieldPath} is not valid when {when}.");
            }
        }

        private static void RequireAbsent(bool? value, string effectId, string path, string fieldPath, string when)
        {
            if (value.HasValue)
            {
                throw new InvalidOperationException($"Effect template '{effectId}' in {path}: {fieldPath} is not valid when {when}.");
            }
        }

        private static void RequireAbsent(string? value, string effectId, string path, string fieldPath, string when)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Effect template '{effectId}' in {path}: {fieldPath} is not valid when {when}.");
            }
        }

        private static void RejectOptionalFalse(bool? value, string effectId, string path, string fieldPath)
        {
            if (value.HasValue && !value.Value)
            {
                throw new InvalidOperationException($"Effect template '{effectId}' in {path}: {fieldPath}=false must be omitted.");
            }
        }

        private static void RejectOptionalZero(int? value, string effectId, string path, string fieldPath)
        {
            if (value.HasValue && value.Value == 0)
            {
                throw new InvalidOperationException($"Effect template '{effectId}' in {path}: {fieldPath}=0 must be omitted.");
            }
        }

        private static float RequireFloat(float? value, string effectId, string path, string fieldPath)
        {
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Effect template '{effectId}' in {path}: {fieldPath} is required.");
            }

            return value.Value;
        }

        private static bool RequireBool(bool? value, string effectId, string path, string fieldPath)
        {
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Effect template '{effectId}' in {path}: {fieldPath} is required.");
            }

            return value.Value;
        }

        private TargetDispatchDescriptor CompileTargetDispatch(TargetDispatchConfig cfg, string effectId, string path)
        {
            var desc = default(TargetDispatchDescriptor);
            bool hasPreset = !string.IsNullOrWhiteSpace(cfg.Preset);
            bool hasContextMapping = cfg.ContextMapping != null;

            if (!string.IsNullOrWhiteSpace(cfg.PayloadEffect))
            {
                desc.PayloadEffectTemplateId = EffectTemplateIdRegistry.GetId(cfg.PayloadEffect);
                if (desc.PayloadEffectTemplateId <= 0)
                    throw new InvalidOperationException($"Effect template '{effectId}' in {path}: targetDispatch.payloadEffect '{cfg.PayloadEffect}' not found.");
            }

            if (hasPreset && hasContextMapping)
            {
                throw new InvalidOperationException(
                    $"Effect template '{effectId}' in {path}: targetDispatch must define either preset or contextMapping, not both.");
            }

            if (hasPreset)
            {
                if (_targetDispatchPresets == null)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{effectId}' in {path}: targetDispatch.preset requires TargetDispatchPresetRegistry.");
                }

                int presetId = _targetDispatchPresets.GetId(cfg.Preset);
                desc.ContextMapping = _targetDispatchPresets.Get(presetId);
                return desc;
            }

            if (hasContextMapping)
            {
                desc.ContextMapping = new TargetResolverContextMapping
                {
                    PayloadSource = TargetDispatchPresetLoader.ParseContextSlotStrict(
                        RequireString(cfg.ContextMapping.PayloadSource, effectId, path, "targetDispatch.contextMapping.payloadSource"),
                        effectId,
                        "targetDispatch.contextMapping.payloadSource",
                        path),
                    PayloadTarget = TargetDispatchPresetLoader.ParseContextSlotStrict(
                        RequireString(cfg.ContextMapping.PayloadTarget, effectId, path, "targetDispatch.contextMapping.payloadTarget"),
                        effectId,
                        "targetDispatch.contextMapping.payloadTarget",
                        path),
                    PayloadTargetContext = TargetDispatchPresetLoader.ParseContextSlotStrict(
                        RequireString(cfg.ContextMapping.PayloadTargetContext, effectId, path, "targetDispatch.contextMapping.payloadTargetContext"),
                        effectId,
                        "targetDispatch.contextMapping.payloadTargetContext",
                        path),
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

            TagSense sense;
            string senseValue = RequireString(cfg.Sense, effectId, path, "expireCondition.sense");
            sense = senseValue switch
            {
                "Raw" => TagSense.Present,
                "Effective" => TagSense.Effective,
                _ => throw new InvalidOperationException($"Effect template '{effectId}' in {path}: unknown tag sense '{senseValue}'."),
            };

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
                string formulaValue = RequireString(cfg.Formula, effectId, path, $"grantedTags[{i}].formula");
                var formula = formulaValue switch
                {
                    "Fixed" => Components.TagContributionFormula.Fixed,
                    "Linear" => Components.TagContributionFormula.Linear,
                    "LinearPlusBase" => Components.TagContributionFormula.LinearPlusBase,
                    "GraphProgram" => Components.TagContributionFormula.GraphProgram,
                    _ => throw new InvalidOperationException($"Effect template '{effectId}' in {path}: grantedTags[{i}] unknown formula '{formulaValue}'."),
                };

                if (formula == Components.TagContributionFormula.GraphProgram)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{effectId}' in {path}: grantedTags[{i}] formula=GraphProgram is not supported until a tag contribution graph evaluator is wired.");
                }

                int amount = formula == Components.TagContributionFormula.GraphProgram
                    ? cfg.Amount ?? 0
                    : RequireInt(cfg.Amount, effectId, path, $"grantedTags[{i}].amount");
                int baseValue = formula == Components.TagContributionFormula.LinearPlusBase
                    ? RequireInt(cfg.Base, effectId, path, $"grantedTags[{i}].base")
                    : cfg.Base ?? 0;
                int graphProgramId = 0;
                switch (formula)
                {
                    case Components.TagContributionFormula.Fixed:
                    case Components.TagContributionFormula.Linear:
                        RequireAbsent(cfg.Base, effectId, path, $"grantedTags[{i}].base", $"formula={formula}");
                        RequireAbsent(cfg.GraphProgram, effectId, path, $"grantedTags[{i}].graphProgram", $"formula={formula}");
                        break;
                    case Components.TagContributionFormula.LinearPlusBase:
                        RequireAbsent(cfg.GraphProgram, effectId, path, $"grantedTags[{i}].graphProgram", "formula=LinearPlusBase");
                        break;
                    case Components.TagContributionFormula.GraphProgram:
                        RequireAbsent(cfg.Amount, effectId, path, $"grantedTags[{i}].amount", "formula=GraphProgram");
                        RequireAbsent(cfg.Base, effectId, path, $"grantedTags[{i}].base", "formula=GraphProgram");
                        break;
                }

                if (formula == Components.TagContributionFormula.GraphProgram)
                {
                    graphProgramId = ResolveGraphProgram(
                        RequireString(cfg.GraphProgram, effectId, path, $"grantedTags[{i}].graphProgram"),
                        effectId,
                        $"grantedTags[{i}].graphProgram",
                        path);
                }

                result.Add(new Components.TagContribution
                {
                    TagId = tagId,
                    Formula = formula,
                    Amount = (ushort)System.Math.Clamp(amount, 0, ushort.MaxValue),
                    Base = (ushort)System.Math.Clamp(baseValue, 0, ushort.MaxValue),
                    GraphProgramId = graphProgramId,
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
            stackLimit = RequireInt(cfg.Limit, effectId, path, "stack.limit");

            string policy = RequireString(cfg.Policy, effectId, path, "stack.policy");
            stackPolicy = policy switch
            {
                "RefreshDuration" => Components.StackPolicy.RefreshDuration,
                "AddDuration" => Components.StackPolicy.AddDuration,
                "KeepDuration" => Components.StackPolicy.KeepDuration,
                _ => throw new InvalidOperationException($"Effect template '{effectId}' in {path}: unknown stack policy '{policy}'."),
            };

            string overflowPolicy = RequireString(cfg.OverflowPolicy, effectId, path, "stack.overflowPolicy");
            stackOverflowPolicy = overflowPolicy switch
            {
                "RejectNew" => Components.StackOverflowPolicy.RejectNew,
                "RemoveOldest" => Components.StackOverflowPolicy.RemoveOldest,
                _ => throw new InvalidOperationException($"Effect template '{effectId}' in {path}: unknown stack overflow policy '{overflowPolicy}'."),
            };
        }
    }
}
