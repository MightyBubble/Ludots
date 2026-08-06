using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public class AbilitySystem : BaseSystem<World, float>
    {
        private readonly EffectRequestQueue _effectRequests;
        private readonly AbilityDefinitionRegistry _abilityDefinitions;
        private readonly TagOps _tagOps;
        private readonly GraphProgramRegistry _graphPrograms;
        private readonly IGraphRuntimeApi _graphApi;
        private readonly ProgressionRequirementEvaluator _progressionRequirements;

        public AbilitySystem(
            World world,
            EffectRequestQueue effectRequests,
            AbilityDefinitionRegistry abilityDefinitions = null,
            TagOps tagOps = null,
            GraphProgramRegistry graphPrograms = null,
            IGraphRuntimeApi graphApi = null,
            ProgressionRequirementEvaluator progressionRequirements = null) : base(world)
        {
            _effectRequests = effectRequests ?? throw new InvalidOperationException(
                "LUDOTS_GAS_ABILITY_EFFECT_QUEUE_REQUIRED: AbilitySystem requires EffectRequestQueue to publish activation effects.");
            _abilityDefinitions = abilityDefinitions;
            _tagOps = tagOps ?? throw new InvalidOperationException(TagOps.MissingTagOpsError);
            _graphPrograms = graphPrograms;
            _graphApi = graphApi;
            _progressionRequirements = progressionRequirements;
        }

        public override void Update(in float dt) { }

        public readonly ref struct AbilityActivationArgs
        {
            public readonly Entity ExplicitTarget;
            public readonly ReadOnlySpan<Entity> TargetEntities;
            public readonly Entity TargetContext;
            public readonly bool UsesTargetCollection;
            public readonly bool HasExplicitTarget;

            public AbilityActivationArgs(Entity explicitTarget)
            {
                ExplicitTarget = explicitTarget;
                TargetEntities = ReadOnlySpan<Entity>.Empty;
                TargetContext = default;
                UsesTargetCollection = false;
                HasExplicitTarget = IsSpecified(explicitTarget);
            }

            public AbilityActivationArgs(ReadOnlySpan<Entity> targetEntities)
            {
                ExplicitTarget = default;
                TargetEntities = targetEntities;
                TargetContext = default;
                UsesTargetCollection = true;
                HasExplicitTarget = false;
            }

            public AbilityActivationArgs(
                Entity explicitTarget,
                ReadOnlySpan<Entity> targetEntities,
                Entity targetContext)
            {
                ExplicitTarget = explicitTarget;
                TargetEntities = targetEntities;
                TargetContext = targetContext;
                UsesTargetCollection = !targetEntities.IsEmpty;
                HasExplicitTarget = IsSpecified(explicitTarget);
            }

            public AbilityActivationArgs(
                Entity explicitTarget,
                ReadOnlySpan<Entity> targetEntities,
                Entity targetContext,
                bool usesTargetCollection)
            {
                ExplicitTarget = explicitTarget;
                TargetEntities = targetEntities;
                TargetContext = targetContext;
                UsesTargetCollection = usesTargetCollection;
                HasExplicitTarget = IsSpecified(explicitTarget);
            }

            private static bool IsSpecified(Entity entity)
                => entity != Entity.Null && entity != default(Entity);
        }

        public bool TryActivateAbility(Entity caster, int slotIndex, Entity explicitTarget = default)
        {
            return TryActivateAbility(caster, slotIndex, new AbilityActivationArgs(explicitTarget));
        }

        public bool TryActivateAbility(Entity caster, int slotIndex, in AbilityActivationArgs args)
        {
            if (!World.IsAlive(caster)) return false;

            World.TryGetRef<AbilityStateBuffer>(caster, out bool hasAbilityBuffer);
            if (!hasAbilityBuffer) return false;
            if (!TryValidateTargets(caster, in args, out Entity validationTarget)) return false;
            if (!AbilitySlotResolver.TryResolve(World, caster, slotIndex, out AbilitySlotState slot)) return false;

            if (slot.AbilityId > 0 && _abilityDefinitions != null && _abilityDefinitions.TryGet(slot.AbilityId, out var def))
            {
                if (def.HasActivationBlockTags)
                {
                    var blockTags = def.ActivationBlockTags;
                    if (!AbilityActivationBlockTagEvaluator.Passes(World, caster, _tagOps, in blockTags)) return false;
                }

                if (def.HasActivationPrecondition)
                {
                    if (!AbilityActivationPreconditionEvaluator.Evaluate(
                            World,
                            caster,
                            validationTarget,
                            default,
                            slot.AbilityId,
                            in def.ActivationPrecondition,
                            _graphPrograms,
                            _graphApi))
                    {
                        return false;
                    }
                }

                if (!EvaluateProgressionUseRequirement(caster, validationTarget, args.TargetContext, in def))
                {
                    return false;
                }

                if (!def.HasOnActivateEffects || def.OnActivateEffects.Count <= 0) return true;

                var effects = def.OnActivateEffects;
                return TryPublishEffects(caster, in args, ref effects);
            }

            if (slot.TemplateEntityId <= 0) return false;

            Entity templateEntity = ReconstructEntity(slot.TemplateEntityId, slot.TemplateEntityWorldId, slot.TemplateEntityVersion);
            if (!World.IsAlive(templateEntity)) return false;
            World.TryGetRef<AbilityTemplate>(templateEntity, out bool hasTemplate);
            if (!hasTemplate) return false;

            ref var blockTagsEntity = ref World.TryGetRef<AbilityActivationBlockTags>(templateEntity, out bool hasBlockTagsEntity);
            if (hasBlockTagsEntity)
            {
                if (!AbilityActivationBlockTagEvaluator.Passes(World, caster, _tagOps, in blockTagsEntity)) return false;
            }

            ref var activationPreconditionEntity = ref World.TryGetRef<AbilityActivationPrecondition>(templateEntity, out bool hasActivationPreconditionEntity);
            if (hasActivationPreconditionEntity)
            {
                int activationId = slot.AbilityId > 0 ? slot.AbilityId : slot.TemplateEntityId;
                if (!AbilityActivationPreconditionEvaluator.Evaluate(
                        World,
                        caster,
                        validationTarget,
                        default,
                        activationId,
                        in activationPreconditionEntity,
                        _graphPrograms,
                        _graphApi))
                {
                    return false;
                }
            }

            ref var progressionRequirementsEntity = ref World.TryGetRef<AbilityProgressionRequirements>(templateEntity, out bool hasProgressionRequirementsEntity);
            if (hasProgressionRequirementsEntity &&
                !EvaluateProgressionUseRequirement(caster, validationTarget, args.TargetContext, in progressionRequirementsEntity))
            {
                return false;
            }

            ref var effectsEntity = ref World.TryGetRef<AbilityOnActivateEffects>(templateEntity, out bool hasOnActivateEntity);
            if (hasOnActivateEntity)
            {
                if (effectsEntity.Count <= 0) return true;
                return TryPublishEffects(caster, in args, ref effectsEntity);
            }

            return true;
        }

        private unsafe bool TryPublishEffects(
            Entity source,
            in AbilityActivationArgs args,
            ref AbilityOnActivateEffects effects)
        {
            if (effects.Count <= 0 || effects.Count > AbilityOnActivateEffects.CAPACITY)
            {
                return false;
            }

            int targetCount = args.UsesTargetCollection ? args.TargetEntities.Length : 1;
            if (targetCount > _effectRequests.AvailableCapacity / effects.Count)
            {
                return false;
            }

            fixed (int* ids = effects.TemplateIds)
            {
                for (int i = 0; i < effects.Count; i++)
                {
                    if (ids[i] <= 0)
                    {
                        return false;
                    }
                }

                if (args.UsesTargetCollection)
                {
                    for (int targetIndex = 0; targetIndex < args.TargetEntities.Length; targetIndex++)
                    {
                        PublishEffectsToTarget(source, args.TargetEntities[targetIndex], args.TargetContext, ids, effects.Count);
                    }
                }
                else
                {
                    Entity target = args.HasExplicitTarget ? args.ExplicitTarget : source;
                    PublishEffectsToTarget(source, target, args.TargetContext, ids, effects.Count);
                }
            }

            return true;
        }

        private unsafe void PublishEffectsToTarget(
            Entity source,
            Entity target,
            Entity targetContext,
            int* templateIds,
            int effectCount)
        {
            for (int i = 0; i < effectCount; i++)
            {
                _effectRequests.Publish(new EffectRequest
                {
                    Source = source,
                    Target = target,
                    TargetContext = targetContext,
                    TemplateId = templateIds[i]
                });
            }
        }

        private bool TryValidateTargets(
            Entity caster,
            in AbilityActivationArgs args,
            out Entity validationTarget)
        {
            validationTarget = caster;
            if (args.TargetContext != Entity.Null &&
                args.TargetContext != default(Entity) &&
                !World.IsAlive(args.TargetContext))
            {
                return false;
            }
            if (args.HasExplicitTarget)
            {
                if (!World.IsAlive(args.ExplicitTarget))
                {
                    return false;
                }
                validationTarget = args.ExplicitTarget;
            }

            if (!args.UsesTargetCollection)
            {
                return true;
            }
            if (args.TargetEntities.IsEmpty)
            {
                return false;
            }
            for (int i = 0; i < args.TargetEntities.Length; i++)
            {
                if (!World.IsAlive(args.TargetEntities[i]))
                {
                    return false;
                }
            }
            if (!args.HasExplicitTarget)
            {
                validationTarget = args.TargetEntities[0];
            }
            return true;
        }

        private bool EvaluateProgressionUseRequirement(Entity caster, Entity subject, Entity explicitScopeHost, in AbilityDefinition definition)
        {
            if (!definition.HasUseProgressionRequirement)
            {
                return true;
            }

            return EvaluateProgressionRequirement(caster, subject, explicitScopeHost, definition.UseProgressionRequirementId);
        }

        private bool EvaluateProgressionUseRequirement(Entity caster, Entity subject, Entity explicitScopeHost, in AbilityProgressionRequirements requirements)
        {
            if (requirements.UseRequirementId <= 0)
            {
                return true;
            }

            return EvaluateProgressionRequirement(caster, subject, explicitScopeHost, requirements.UseRequirementId);
        }

        private bool EvaluateProgressionRequirement(Entity caster, Entity subject, Entity explicitScopeHost, int requirementId)
        {
            if (_progressionRequirements == null)
            {
                throw new InvalidOperationException("Ability progression requirement is configured, but ProgressionRequirementEvaluator is not registered.");
            }

            Entity resolvedSubject = World.IsAlive(subject) ? subject : caster;
            Entity resolvedExplicitScopeHost = World.IsAlive(explicitScopeHost)
                ? explicitScopeHost
                : default;
            var context = new RoleResolverContext(
                actor: caster,
                subject: resolvedSubject,
                explicitScopeHost: resolvedExplicitScopeHost);
            return _progressionRequirements.Evaluate(requirementId, in context);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Entity ReconstructEntity(int id, int worldId, int version)
            => EntityUtil.Reconstruct(id, worldId, version);
    }
}
