using System;
using Arch.Core;
using Arch.Relationships;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Tasks;

namespace Ludots.Core.Persistence
{
    public static class SaveEntityWorldIdNormalizer
    {
        public static void Normalize(World world)
        {
            Normalize(world, world.Id);
        }

        public static void Normalize(World world, int canonicalWorldId)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            NormalizeBlackboardEntityBuffer(world, canonicalWorldId);
            NormalizeChildrenBuffer(world, canonicalWorldId);
            NormalizeActiveEffectContainer(world, canonicalWorldId);
            NormalizeAbilityStateBuffer(world, canonicalWorldId);
            NormalizeTeamEntityRef(world, canonicalWorldId);
            NormalizeActivityInstances(world, canonicalWorldId);
            NormalizeTaskInstances(world, canonicalWorldId);
            NormalizeRelationshipInstances(world, canonicalWorldId);
            NormalizeRelationshipKeys<RelationshipEdgeSet>(world, canonicalWorldId);
            NormalizeRelationshipKeys<InRelationship>(world, canonicalWorldId);
        }

        private static void NormalizeBlackboardEntityBuffer(World world, int worldId)
        {
            var query = new QueryDescription().WithAll<BlackboardEntityBuffer>();
            world.Query(in query, (ref BlackboardEntityBuffer refs) =>
            {
                unsafe
                {
                    for (int i = 0; i < refs.Count; i++)
                    {
                        refs.WorldIds[i] = worldId;
                    }
                }
            });
        }

        private static void NormalizeChildrenBuffer(World world, int worldId)
        {
            var query = new QueryDescription().WithAll<ChildrenBuffer>();
            world.Query(in query, (ref ChildrenBuffer children) =>
            {
                unsafe
                {
                    for (int i = 0; i < children.Count; i++)
                    {
                        children.ChildWorldIds[i] = worldId;
                    }
                }
            });
        }

        private static void NormalizeActiveEffectContainer(World world, int worldId)
        {
            var query = new QueryDescription().WithAll<ActiveEffectContainer>();
            world.Query(in query, (ref ActiveEffectContainer activeEffects) =>
            {
                unsafe
                {
                    for (int i = 0; i < activeEffects.Count; i++)
                    {
                        activeEffects.WorldIds[i] = worldId;
                    }
                }
            });
        }

        private static void NormalizeAbilityStateBuffer(World world, int worldId)
        {
            var query = new QueryDescription().WithAll<AbilityStateBuffer>();
            world.Query(in query, (ref AbilityStateBuffer abilities) =>
            {
                unsafe
                {
                    for (int i = 0; i < abilities.Count; i++)
                    {
                        if (abilities.TemplateIds[i] != 0 || abilities.TemplateVersions[i] != 0)
                        {
                            abilities.TemplateWorldIds[i] = worldId;
                        }
                    }
                }
            });
        }

        private static void NormalizeTeamEntityRef(World world, int worldId)
        {
            var query = new QueryDescription().WithAll<TeamEntityRef>();
            world.Query(in query, (ref TeamEntityRef teamRef) =>
            {
                Entity value = teamRef.Value;
                if (value != Entity.Null)
                {
                    teamRef.Value = EntityUtil.Reconstruct(value.Id, worldId, value.Version);
                }
            });
        }

        private static void NormalizeActivityInstances(World world, int worldId)
        {
            var query = new QueryDescription().WithAll<ActivityInstanceCm>();
            world.Query(in query, (ref ActivityInstanceCm activity) =>
            {
                Entity scopeHost = NormalizeOptionalEntity(activity.ScopeHost);
                if (scopeHost != Entity.Null)
                {
                    activity.ScopeHost = EntityUtil.Reconstruct(
                        scopeHost.Id,
                        worldId,
                        scopeHost.Version);
                    return;
                }

                activity.ScopeHost = Entity.Null;
            });
        }

        private static void NormalizeTaskInstances(World world, int worldId)
        {
            var query = new QueryDescription().WithAll<TaskInstanceCm>();
            world.Query(in query, (ref TaskInstanceCm task) =>
            {
                Entity scopeHost = NormalizeOptionalEntity(task.ScopeHost);
                if (scopeHost != Entity.Null)
                {
                    task.ScopeHost = EntityUtil.Reconstruct(
                        scopeHost.Id,
                        worldId,
                        scopeHost.Version);
                    return;
                }

                task.ScopeHost = Entity.Null;
            });
        }

        private static void NormalizeRelationshipInstances(World world, int worldId)
        {
            var query = new QueryDescription().WithAll<RelationshipInstanceCm>();
            world.Query(in query, (ref RelationshipInstanceCm relationship) =>
            {
                if (relationship.Source != Entity.Null)
                {
                    relationship.Source = EntityUtil.Reconstruct(
                        relationship.Source.Id,
                        worldId,
                        relationship.Source.Version);
                }

                if (relationship.Target != Entity.Null)
                {
                    relationship.Target = EntityUtil.Reconstruct(
                        relationship.Target.Id,
                        worldId,
                        relationship.Target.Version);
                }
            });
        }

        private static void NormalizeRelationshipKeys<T>(World world, int worldId)
        {
            var query = new QueryDescription().WithAll<Relationship<T>>();
            world.Query(in query, (ref Relationship<T> relationships) =>
            {
                if (relationships == null || relationships.Elements.Count == 0)
                {
                    return;
                }

                var normalized = new SortedList<Entity, T>(relationships.Elements.Count);
                foreach (KeyValuePair<Entity, T> entry in relationships.Elements)
                {
                    Entity target = entry.Key;
                    Entity normalizedTarget = target == Entity.Null
                        ? Entity.Null
                        : EntityUtil.Reconstruct(target.Id, worldId, target.Version);
                    normalized.Add(normalizedTarget, entry.Value);
                }

                relationships.Elements.Clear();
                foreach (KeyValuePair<Entity, T> entry in normalized)
                {
                    relationships.Elements.Add(entry.Key, entry.Value);
                }
            });
        }

        private static Entity NormalizeOptionalEntity(Entity entity)
        {
            return entity.Equals(default(Entity)) || entity.Equals(Entity.Null)
                ? Entity.Null
                : entity;
        }
    }
}
