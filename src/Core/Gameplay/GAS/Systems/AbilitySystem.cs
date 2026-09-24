using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public class AbilitySystem : BaseSystem<World, float>
    {
        private readonly EffectRequestQueue _effectRequests;
        private readonly AbilityActivationEligibilityQuery _activationEligibility;

        public AbilitySystem(
            World world,
            EffectRequestQueue effectRequests,
            AbilityDefinitionRegistry abilityDefinitions = null,
            TagOps tagOps = null,
            GraphProgramRegistry graphPrograms = null,
            IGraphRuntimeApi graphApi = null,
            ProgressionRequirementEvaluator progressionRequirements = null,
            AbilityActivationEligibilityQuery activationEligibility = null) : base(world)
        {
            _effectRequests = effectRequests ?? throw new InvalidOperationException(
                "LUDOTS_GAS_ABILITY_EFFECT_QUEUE_REQUIRED: AbilitySystem requires EffectRequestQueue to publish activation effects.");
            TagOps requiredTagOps = tagOps ?? throw new InvalidOperationException(TagOps.MissingTagOpsError);
            _activationEligibility = activationEligibility ?? new AbilityActivationEligibilityQuery(
                world,
                abilityDefinitions,
                requiredTagOps,
                graphPrograms,
                graphApi,
                progressionRequirements);
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
            if (!TryValidateTargets(caster, in args, out Entity validationTarget)) return false;
            var request = new AbilityActivationEligibilityRequest(
                caster,
                slotIndex,
                validationTarget,
                args.TargetContext,
                allowProgressionDeferral: false);
            AbilityActivationEligibilityResult eligibility = _activationEligibility.Evaluate(in request);
            if (!eligibility.IsEligible)
            {
                return false;
            }

            AbilityOnActivateEffects effects = default;
            bool hasEffects;
            if (eligibility.HasDefinition)
            {
                hasEffects = eligibility.Definition.HasOnActivateEffects;
                effects = eligibility.Definition.OnActivateEffects;
            }
            else
            {
                Entity templateEntity = eligibility.TemplateEntity;
                if (!World.Has<AbilityTemplate>(templateEntity))
                {
                    return false;
                }

                hasEffects = World.Has<AbilityOnActivateEffects>(templateEntity);
                if (hasEffects)
                {
                    effects = World.Get<AbilityOnActivateEffects>(templateEntity);
                }
            }

            if (!hasEffects || effects.Count <= 0)
            {
                return true;
            }

            return TryPublishEffects(caster, in args, ref effects);
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

    }
}
