using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Technology;
using Ludots.Core.Gameplay.Technology.Registry;
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
        private readonly TargetDispatchPresetRegistry _targetDispatchPresets;
        private readonly TechnologyScopeKeyRegistry? _technologyScopeKeys;
        private readonly TechnologyDefinitionRegistry? _technologyDefinitions;

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
            TechnologyScopeKeyRegistry? technologyScopeKeys = null,
            TechnologyDefinitionRegistry? technologyDefinitions = null)
        {
            _pipeline = pipeline;
            _registry = registry;
            _conditions = conditions;
            _targetDispatchPresets = targetDispatchPresets;
            _technologyScopeKeys = technologyScopeKeys;
            _technologyDefinitions = technologyDefinitions;
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
                durationTicks = RequireInt(cfg.Duration.DurationTicks, cfg.Id, relativePath, "duration.durationTicks");
                periodTicks = RequireInt(cfg.Duration.PeriodTicks, cfg.Id, relativePath, "duration.periodTicks");
                clockId = ParseClockId(RequireString(cfg.Duration.ClockId, cfg.Id, relativePath, "duration.clockId"));
            }
            else
            {
                if (lifetimeKind != EffectLifetimeKind.Instant)
                {
                    throw new InvalidOperationException($"Effect template '{cfg.Id}' in {relativePath}: lifetime '{lifetimeKind}' requires an explicit duration block.");
                }

                durationTicks = 0;
                periodTicks = 0;
            }

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
            var technologyScope = TechnologyScopeSpec.Self;
            var technologyChange = TechnologyLevelChange.Complete;
            int technologyId = 0;

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

            if (cfg.Technology != null && presetType != EffectPresetType.CompleteTechnology)
            {
                throw new InvalidOperationException(
                    $"Effect template '{cfg.Id}' in {relativePath}: 'technology' block is only valid when presetType=CompleteTechnology.");
            }
            if (presetType == EffectPresetType.CompleteTechnology)
            {
                if (lifetimeKind != EffectLifetimeKind.Instant)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType CompleteTechnology requires lifetime=Instant.");
                }
                if (cfg.Technology == null)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: presetType CompleteTechnology requires a 'technology' block.");
                }

                string techName = RequireString(cfg.Technology.Id, cfg.Id, relativePath, "technology.id");
                technologyId = TechnologyIdRegistry.GetId(techName);
                if (technologyId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{cfg.Id}' in {relativePath}: technology.id '{techName}' is not registered.");
                }

                technologyScope = ResolveTechnologyScope(cfg.Technology.Scope, technologyId, cfg.Id, relativePath);
                technologyChange = CompileTechnologyChange(cfg.Technology, cfg.Id, relativePath);
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
                ParticipatesInResponse = RequireBool(cfg.ParticipatesInResponse, cfg.Id, relativePath, "participatesInResponse"),
                Modifiers = modifiers,
                TargetQuery = targetQuery,
                TargetFilter = targetFilter,
                TargetDispatch = targetDispatch,
                Projectile = projectile,
                UnitCreation = unitCreation,
                Displacement = displacement,
                Relation = relation,
                TechnologyScope = technologyScope,
                TechnologyChange = technologyChange,
                TechnologyId = technologyId,
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
            int fixedDirectionDeg = RequireInt(cfg.FixedDirectionDeg, ownerId, relativePath, "displacement.fixedDirectionDeg");
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

        private static RelationDescriptor CompileRelation(RelationConfig cfg, string ownerId, string relativePath)
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

            if (operation == RelationOperation.RemoveParent && snapSubjectToParentPosition)
            {
                throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: relation.snapSubjectToParentPosition is only valid when operation=SetParent.");
            }

            return new RelationDescriptor
            {
                Operation = operation,
                Subject = subject,
                Parent = parent,
                SnapSubjectToParentPosition = snapSubjectToParentPosition
            };
        }

        private static ProjectileDescriptor CompileProjectile(ProjectileConfig cfg, string ownerId, string relativePath)
        {
            if (cfg == null) return default;

            int impactId = 0;
            string impactEffect = RequireString(cfg.ImpactEffect, ownerId, relativePath, "projectile.impactEffect");
            if (!string.IsNullOrWhiteSpace(impactEffect))
            {
                impactId = EffectTemplateIdRegistry.GetId(impactEffect);
                if (impactId <= 0)
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: projectile.impactEffect references unknown effect template '{impactEffect}'.");
                }
            }

            int hitId = 0;
            string hitEffect = RequireString(cfg.HitEffect, ownerId, relativePath, "projectile.hitEffect");
            if (!string.IsNullOrWhiteSpace(hitEffect))
            {
                hitId = EffectTemplateIdRegistry.GetId(hitEffect);
                if (hitId <= 0)
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: projectile.hitEffect references unknown effect template '{hitEffect}'.");
                }
            }

            int presentationId = 0;
            string presentationEffect = RequireString(cfg.PresentationEffect, ownerId, relativePath, "projectile.presentationEffect");
            if (!string.IsNullOrWhiteSpace(presentationEffect))
            {
                presentationId = EffectTemplateIdRegistry.GetId(presentationEffect);
                if (presentationId <= 0)
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: projectile.presentationEffect references unknown effect template '{presentationEffect}'.");
                }
            }

            ProjectileTravelMode travelMode = ParseProjectileTravelMode(cfg.TravelMode);
            ProjectileImpactPolicy impactPolicy = ParseProjectileImpactPolicy(cfg.ImpactPolicy);
            int collisionHalfWidth = RequireInt(cfg.CollisionHalfWidth, ownerId, relativePath, "projectile.collisionHalfWidth");
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
                CollisionRelationFilter = ParseRequiredRelationshipFilter(
                    cfg.CollisionRelationFilter,
                    ownerId,
                    "projectile.collisionRelationFilter",
                    relativePath),
                CollisionExcludeSource = RequireBool(cfg.CollisionExcludeSource, ownerId, relativePath, "projectile.collisionExcludeSource"),
                MaxHitCount = RequireInt(cfg.MaxHitCount, ownerId, relativePath, "projectile.maxHitCount")
            };
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

            return new UnitCreationDescriptor
            {
                PlacementPattern = ParseUnitCreationPlacementPattern(cfg.PlacementPattern),
                FacingPattern = ParseUnitCreationFacingPattern(cfg.FacingPattern),
                UnitTypeId = unitTypeId,
                TemplateId = hasTemplateId ? cfg.TemplateId : string.Empty,
                UseTemplateSpawn = hasTemplateId,
                Count = RequireInt(cfg.Count, ownerId, relativePath, "unitCreation.count"),
                OffsetRadius = RequireInt(cfg.OffsetRadius, ownerId, relativePath, "unitCreation.offsetRadius"),
                PlacementRadiusCm = RequireInt(cfg.PlacementRadiusCm, ownerId, relativePath, "unitCreation.placementRadiusCm"),
                PlacementStartAngleDeg = RequireInt(cfg.PlacementStartAngleDeg, ownerId, relativePath, "unitCreation.placementStartAngleDeg"),
                OnSpawnEffectTemplateId = onSpawnId,
                CopySourcePlayerOwner = RequireBool(cfg.CopySourcePlayerOwner, ownerId, relativePath, "unitCreation.copySourcePlayerOwner"),
                LinkSourceAsParent = RequireBool(cfg.LinkSourceAsParent, ownerId, relativePath, "unitCreation.linkSourceAsParent"),
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
                _ => throw new InvalidOperationException(
                    $"Effect template '{ownerId}' in {relativePath}: unsupported relation.operation '{value}'. Supported: SetParent, RemoveParent.")
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
        private static EffectPresetType ParsePresetType(string? presetType, string ownerId, string relativePath)
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

        private static void CompileConfigParams(
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
                else
                {
                    throw new InvalidOperationException($"Effect template '{ownerId}' in {relativePath}: configParams.{kvp.Key} has unsupported type '{type}'. Supported: Float, Int, EffectTemplate, Attribute.");
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

                int priority = RequireInt(lc.Priority, ownerId, relativePath, $"phaseListeners[{i}].priority");
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

        private TechnologyScopeSpec ResolveTechnologyScope(string? rawValue, int technologyId, string effectId, string path)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return _technologyDefinitions != null &&
                       technologyId > 0 &&
                       _technologyDefinitions.TryGet(technologyId, out var definition)
                    ? definition.DefaultScope
                    : TechnologyScopeSpec.Self;
            }

            return ParseTechnologyScope(rawValue.Trim(), effectId, path);
        }

        private TechnologyScopeSpec ParseTechnologyScope(string value, string effectId, string path)
        {
            if (_technologyScopeKeys == null)
            {
                throw new InvalidOperationException(
                    $"Effect template '{effectId}' in {path}: technology.scope requires TechnologyScopeKeyRegistry.");
            }

            switch (value)
            {
                case "self":
                    return TechnologyScopeSpec.Self;
                case "explicit":
                    return new TechnologyScopeSpec(TechnologyScopeKind.Explicit);
                default:
                    if (_technologyScopeKeys.TryGetId(value, out int scopeKeyId) && scopeKeyId > 0)
                    {
                        return new TechnologyScopeSpec(TechnologyScopeKind.Named, scopeKeyId);
                    }

                    throw new InvalidOperationException(
                        $"Effect template '{effectId}' in {path}: technology.scope '{value}' is not registered by Technology config.");
            }
        }

        private static TechnologyLevelChange CompileTechnologyChange(TechnologyCompletionConfig cfg, string effectId, string path)
        {
            if (cfg.Level.HasValue && cfg.Delta.HasValue)
            {
                throw new InvalidOperationException(
                    $"Effect template '{effectId}' in {path}: technology.level and technology.delta are mutually exclusive.");
            }

            if (cfg.Level.HasValue)
            {
                int level = cfg.Level.Value;
                if (level <= 0)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{effectId}' in {path}: technology.level must be greater than zero.");
                }

                return new TechnologyLevelChange(level, 0);
            }

            if (cfg.Delta.HasValue)
            {
                int delta = cfg.Delta.Value;
                if (delta <= 0)
                {
                    throw new InvalidOperationException(
                        $"Effect template '{effectId}' in {path}: technology.delta must be greater than zero.");
                }

                return new TechnologyLevelChange(0, delta);
            }

            return TechnologyLevelChange.Complete;
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
                desc.Spatial.Shape = ParseSpatialShape(RequireString(cfg.Shape, effectId, path, "targetQuery.shape"), effectId, path);
                desc.Spatial.RadiusCm = RequireInt(cfg.Radius, effectId, path, "targetQuery.radius");
                desc.Spatial.InnerRadiusCm = RequireInt(cfg.InnerRadius, effectId, path, "targetQuery.innerRadius");
                desc.Spatial.HalfAngleDeg = RequireInt(cfg.HalfAngle, effectId, path, "targetQuery.halfAngle");
                desc.Spatial.HalfWidthCm = RequireInt(cfg.HalfWidth, effectId, path, "targetQuery.halfWidth");
                desc.Spatial.HalfHeightCm = RequireInt(cfg.HalfHeight, effectId, path, "targetQuery.halfHeight");
                desc.Spatial.RotationDeg = RequireInt(cfg.Rotation, effectId, path, "targetQuery.rotation");
                desc.Spatial.LengthCm = RequireInt(cfg.Length, effectId, path, "targetQuery.length");
            }
            desc.GraphProgramId = RequireInt(cfg.GraphProgramId, effectId, path, "targetQuery.graphProgramId");
            return desc;
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
            if (!string.IsNullOrWhiteSpace(cfg.PayloadEffect))
            {
                desc.PayloadEffectTemplateId = EffectTemplateIdRegistry.GetId(cfg.PayloadEffect);
                if (desc.PayloadEffectTemplateId <= 0)
                    throw new InvalidOperationException($"Effect template '{effectId}' in {path}: targetDispatch.payloadEffect '{cfg.PayloadEffect}' not found.");
            }

            if (!string.IsNullOrWhiteSpace(cfg.Preset))
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

            if (cfg.ContextMapping != null)
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
                throw new InvalidOperationException(
                    $"Effect template '{effectId}' in {path}: targetDispatch must define either preset or contextMapping.");
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

                int amount = RequireInt(cfg.Amount, effectId, path, $"grantedTags[{i}].amount");
                int baseValue = RequireInt(cfg.Base, effectId, path, $"grantedTags[{i}].base");
                result.Add(new Components.TagContribution
                {
                    TagId = tagId,
                    Formula = formula,
                    Amount = (ushort)System.Math.Clamp(amount, 0, ushort.MaxValue),
                    Base = (ushort)System.Math.Clamp(baseValue, 0, ushort.MaxValue),
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
