using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Gameplay.GAS.Components;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public class AbilitySystem : BaseSystem<World, float>
    {
        private readonly EffectRequestQueue _effectRequests;
        private readonly AbilityDefinitionRegistry _abilityDefinitions;
        private readonly TagOps _tagOps;

        public AbilitySystem(World world, EffectRequestQueue effectRequests = null, AbilityDefinitionRegistry abilityDefinitions = null, TagOps tagOps = null) : base(world) 
        {
            _effectRequests = effectRequests;
            _abilityDefinitions = abilityDefinitions;
            _tagOps = tagOps ?? new TagOps();
        }

        public override void Update(in float dt) { }

        public readonly ref struct AbilityActivationArgs
        {
            public readonly Entity ExplicitTarget;
            public readonly ReadOnlySpan<Entity> TargetEntities;
            public readonly Entity TargetContext;

            public AbilityActivationArgs(Entity explicitTarget)
            {
                ExplicitTarget = explicitTarget;
                TargetEntities = ReadOnlySpan<Entity>.Empty;
                TargetContext = default;
            }

            public AbilityActivationArgs(ReadOnlySpan<Entity> targetEntities)
            {
                ExplicitTarget = default;
                TargetEntities = targetEntities;
                TargetContext = default;
            }

            public AbilityActivationArgs(Entity explicitTarget, ReadOnlySpan<Entity> targetEntities, Entity targetContext)
            {
                ExplicitTarget = explicitTarget;
                TargetEntities = targetEntities;
                TargetContext = targetContext;
            }
        }

        public bool TryActivateAbility(Entity caster, int slotIndex, Entity explicitTarget = default)
        {
            return TryActivateAbility(caster, slotIndex, new AbilityActivationArgs(explicitTarget));
        }

        public bool TryActivateAbility(Entity caster, int slotIndex, in AbilityActivationArgs args)
        {
            if (!World.IsAlive(caster)) return false;

            ref var buffer = ref World.TryGetRef<AbilityStateBuffer>(caster, out bool hasAbilityBuffer);
            if (!hasAbilityBuffer) return false;
            var slot = buffer.Get(slotIndex);

            if (slot.AbilityId > 0 && _abilityDefinitions != null && _abilityDefinitions.TryGet(slot.AbilityId, out var def))
            {
                if (def.HasActivationBlockTags)
                {
                    var blockTags = def.ActivationBlockTags;
                    ref var casterTags = ref World.TryGetRef<GameplayTagContainer>(caster, out bool hasCasterTags);
                    if (!hasCasterTags)
                    {
                        if (!blockTags.RequiredAll.IsEmpty) return false;
                    }
                    else
                    {
                        if (_tagOps.Intersects(ref casterTags, in blockTags.BlockedAny, TagSense.Effective)) return false;
                        if (!_tagOps.ContainsAll(ref casterTags, in blockTags.RequiredAll, TagSense.Effective)) return false;
                    }
                }

                if (_effectRequests == null) return true;
                if (!def.HasOnActivateEffects || def.OnActivateEffects.Count <= 0) return true;

                var effects = def.OnActivateEffects;
                if (args.TargetEntities.Length > 0)
                {
                    for (int ti = 0; ti < args.TargetEntities.Length; ti++)
                    {
                        var target = args.TargetEntities[ti];
                        if (!World.IsAlive(target)) continue;
                        PublishEffects(caster, target, args.TargetContext, ref effects);
                    }
                }
                else if (World.IsAlive(args.ExplicitTarget))
                {
                    PublishEffects(caster, args.ExplicitTarget, args.TargetContext, ref effects);
                }
                else
                {
                    PublishEffects(caster, caster, args.TargetContext, ref effects);
                }

                return true;
            }

            if (slot.TemplateEntityId <= 0) return false;

            Entity templateEntity = ReconstructEntity(slot.TemplateEntityId, slot.TemplateEntityWorldId, slot.TemplateEntityVersion);
            if (!World.IsAlive(templateEntity)) return false;
            World.TryGetRef<AbilityTemplate>(templateEntity, out bool hasTemplate);
            if (!hasTemplate) return false;

            ref var blockTagsEntity = ref World.TryGetRef<AbilityActivationBlockTags>(templateEntity, out bool hasBlockTagsEntity);
            if (hasBlockTagsEntity)
            {
                ref var casterTags = ref World.TryGetRef<GameplayTagContainer>(caster, out bool hasCasterTags);
                if (!hasCasterTags)
                {
                    if (!blockTagsEntity.RequiredAll.IsEmpty) return false;
                }
                else
                {
                    if (_tagOps.Intersects(ref casterTags, in blockTagsEntity.BlockedAny, TagSense.Effective)) return false;
                    if (!_tagOps.ContainsAll(ref casterTags, in blockTagsEntity.RequiredAll, TagSense.Effective)) return false;
                }
            }

            if (_effectRequests == null) return true;

            ref var effectsEntity = ref World.TryGetRef<AbilityOnActivateEffects>(templateEntity, out bool hasOnActivateEntity);
            if (hasOnActivateEntity)
            {
                if (effectsEntity.Count <= 0) return true;

                if (args.TargetEntities.Length > 0)
                {
                    for (int ti = 0; ti < args.TargetEntities.Length; ti++)
                    {
                        var target = args.TargetEntities[ti];
                        if (!World.IsAlive(target)) continue;
                        PublishEffects(caster, target, args.TargetContext, ref effectsEntity);
                    }
                }
                else if (World.IsAlive(args.ExplicitTarget))
                {
                    PublishEffects(caster, args.ExplicitTarget, args.TargetContext, ref effectsEntity);
                }
                else
                {
                    PublishEffects(caster, caster, args.TargetContext, ref effectsEntity);
                }
            }

            return true;
        }

        private unsafe void PublishEffects(Entity source, Entity target, Entity targetContext, ref AbilityOnActivateEffects effects)
        {
            fixed (int* ids = effects.TemplateIds)
            {
                for (int i = 0; i < effects.Count; i++)
                {
                    int templateId = ids[i];
                    if (templateId <= 0) continue;
                    _effectRequests.Publish(new EffectRequest
                    {
                        Source = source,
                        Target = target,
                        TargetContext = targetContext,
                        TemplateId = templateId
                    });
                }
            }
        }

        private static Entity ReconstructEntity(int id, int worldId, int version)
            => EntityUtil.Reconstruct(id, worldId, version);
    }
}
