using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Relationships;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;

namespace Ludots.Core.Persistence
{
    public static class SaveEntityReferenceValidator
    {
        public static void Validate(World world, SaveEntityInclusionPolicy policy)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (policy == null) throw new ArgumentNullException(nameof(policy));

            ValidateBlackboardEntityBuffer(world, policy);
            ValidateChildrenBuffer(world, policy);
            ValidateActiveEffectContainer(world, policy);
            ValidateAbilityStateBuffer(world, policy);
            ValidateTeamEntityRef(world, policy);
            ValidateRelationshipKeys<RelationshipEdgeSet>(world, policy);
            ValidateRelationshipKeys<InRelationship>(world, policy);
        }

        private static void ValidateBlackboardEntityBuffer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<BlackboardEntityBuffer>();
            world.Query(in query, (Entity owner, ref BlackboardEntityBuffer refs) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                for (int i = 0; i < refs.Count; i++)
                {
                    int key;
                    Entity target;
                    unsafe
                    {
                        key = refs.Keys[i];
                        target = EntityUtil.Reconstruct(refs.EntityIds[i], refs.WorldIds[i], refs.Versions[i]);
                    }

                    ValidateTarget(world, policy, owner, target, nameof(BlackboardEntityBuffer), $"key={key}");
                }
            });
        }

        private static void ValidateChildrenBuffer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<ChildrenBuffer>();
            world.Query(in query, (Entity owner, ref ChildrenBuffer children) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                for (int i = 0; i < children.Count; i++)
                {
                    ValidateTarget(world, policy, owner, children.Get(i), nameof(ChildrenBuffer), $"index={i}");
                }
            });
        }

        private static void ValidateActiveEffectContainer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<ActiveEffectContainer>();
            world.Query(in query, (Entity owner, ref ActiveEffectContainer activeEffects) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                for (int i = 0; i < activeEffects.Count; i++)
                {
                    ValidateTarget(world, policy, owner, activeEffects.GetEntity(i), nameof(ActiveEffectContainer), $"index={i}");
                }
            });
        }

        private static void ValidateAbilityStateBuffer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<AbilityStateBuffer>();
            world.Query(in query, (Entity owner, ref AbilityStateBuffer abilities) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                for (int i = 0; i < abilities.Count; i++)
                {
                    AbilitySlotState slot = abilities.Get(i);
                    if (slot.TemplateEntityId == 0 &&
                        slot.TemplateEntityWorldId == 0 &&
                        slot.TemplateEntityVersion == 0)
                    {
                        continue;
                    }

                    Entity template = EntityUtil.Reconstruct(
                        slot.TemplateEntityId,
                        slot.TemplateEntityWorldId,
                        slot.TemplateEntityVersion);
                    ValidateTarget(world, policy, owner, template, nameof(AbilityStateBuffer), $"slot={i}");
                }
            });
        }

        private static void ValidateTeamEntityRef(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<TeamEntityRef>();
            world.Query(in query, (Entity owner, ref TeamEntityRef teamRef) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, teamRef.Value, nameof(TeamEntityRef), nameof(TeamEntityRef.Value));
            });
        }

        private static void ValidateRelationshipKeys<T>(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<Relationship<T>>();
            world.Query(in query, (Entity owner, ref Relationship<T> relationships) =>
            {
                if (!policy.ShouldInclude(world, owner) || relationships == null)
                {
                    return;
                }

                int index = 0;
                foreach (KeyValuePair<Entity, T> entry in relationships.Elements)
                {
                    ValidateTarget(
                        world,
                        policy,
                        owner,
                        entry.Key,
                        $"Relationship<{typeof(T).Name}>",
                        $"target={index}");
                    index++;
                }
            });
        }

        private static void ValidateTarget(
            World world,
            SaveEntityInclusionPolicy policy,
            Entity owner,
            Entity target,
            string componentName,
            string lane)
        {
            if (target == Entity.Null)
            {
                return;
            }

            if (!world.IsAlive(target))
            {
                throw new SaveContextException(
                    $"Save entity reference validation failed: {componentName} on entity {owner.Id}:{owner.WorldId}:{owner.Version} references missing entity {target.Id}:{target.WorldId}:{target.Version} ({lane}).");
            }

            if (!policy.ShouldInclude(world, target))
            {
                throw new SaveContextException(
                    $"Save entity reference validation failed: {componentName} on entity {owner.Id}:{owner.WorldId}:{owner.Version} references excluded entity {target.Id}:{target.WorldId}:{target.Version} ({lane}).");
            }
        }
    }
}
