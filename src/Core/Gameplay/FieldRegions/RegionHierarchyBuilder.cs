using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Fields;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Map;

namespace Ludots.Core.Gameplay.FieldRegions
{
    /// <summary>
    /// Lookup of hierarchy group entities by key, plus the roster state the builder
    /// validated. Point queries walk ChildOf edges at read time; no parent-side grid
    /// is ever stored, so there is no projection that could fall out of sync.
    /// </summary>
    public sealed class RegionHierarchyRuntime
    {
        public RegionHierarchyRuntime(Dictionary<string, Entity> groupByKey)
        {
            GroupByKey = groupByKey;
        }

        public Dictionary<string, Entity> GroupByKey { get; }
    }

    /// <summary>
    /// Wires roster entries into ChildOf edges at map load: children must resolve to
    /// materialized region (or group) entities; a child claimed by two different parents
    /// fails the load; cycles are rejected by RelationOps. Group parents without cells of
    /// their own materialize as RegionGroupCm entities.
    /// </summary>
    public static class RegionHierarchyBuilder
    {
        public static RegionHierarchyRuntime Build(World world, MapSession session, List<Fields.Config.FieldHierarchyRoster> rosters)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (rosters == null) throw new ArgumentNullException(nameof(rosters));

            var groupByKey = new Dictionary<string, Entity>(StringComparer.Ordinal);
            if (rosters.Count == 0)
            {
                return new RegionHierarchyRuntime(groupByKey);
            }

            Dictionary<string, Entity> entityByKey = CollectRegionEntities(session);
            var mapTag = new MapEntity { MapId = session.MapId };
            var parentByChild = new Dictionary<string, string>(StringComparer.Ordinal);

            // Two passes: first materialize/resolve every parent, then wire children, so
            // nested rosters (a parent that is itself somebody's child) resolve regardless
            // of asset order.
            foreach (Fields.Config.FieldHierarchyRoster roster in rosters)
            {
                if (!entityByKey.TryGetValue(roster.Parent, out Entity parentEntity))
                {
                    parentEntity = world.Create(mapTag, new RegionGroupCm { GroupKey = roster.Parent });
                    groupByKey[roster.Parent] = parentEntity;
                }
            }

            foreach (Fields.Config.FieldHierarchyRoster roster in rosters)
            {
                Entity parentEntity = ResolveEntity(entityByKey, groupByKey, roster.Parent);
                foreach (string childKey in roster.Children)
                {
                    if (parentByChild.TryGetValue(childKey, out string? existingParent))
                    {
                        if (!string.Equals(existingParent, roster.Parent, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Hierarchy roster for map '{session.MapId.Value}' claims region '{childKey}' for both '{existingParent}' and '{roster.Parent}'.");
                        }

                        continue;
                    }

                    Entity childEntity = ResolveEntity(entityByKey, groupByKey, childKey);
                    RelationOps.SetParent(world, childEntity, parentEntity);
                    parentByChild[childKey] = roster.Parent;
                }
            }

            return new RegionHierarchyRuntime(groupByKey);
        }

        /// <summary>
        /// Point query chain: the region entity followed by every ancestor group key,
        /// finest first. Read-time evaluation over ChildOf edges.
        /// </summary>
        public static bool TryResolveChain(World world, Entity regionEntity, List<string> chain)
        {
            if (!world.IsAlive(regionEntity) || !world.Has<RegionCm>(regionEntity))
            {
                return false;
            }

            chain.Clear();
            chain.Add(world.Get<RegionCm>(regionEntity).RegionKey);
            Entity current = regionEntity;
            int steps = 0;
            while (world.IsAlive(current) && world.Has<ChildOf>(current))
            {
                current = world.Get<ChildOf>(current).Parent;
                if (!world.IsAlive(current))
                {
                    break;
                }

                chain.Add(world.Has<RegionCm>(current)
                    ? world.Get<RegionCm>(current).RegionKey
                    : world.Get<RegionGroupCm>(current).GroupKey);
                steps++;
                if (steps > 64)
                {
                    throw new InvalidOperationException(
                        $"Hierarchy chain from entity {regionEntity.Id} exceeded 64 ancestors.");
                }
            }

            return true;
        }

        /// <summary>Read-time member enumeration of one group (its ChildrenBuffer).</summary>
        public static bool TryEnumerateGroupMembers(World world, Entity groupEntity, List<Entity> members)
        {
            if (!world.IsAlive(groupEntity) || !world.Has<ChildrenBuffer>(groupEntity))
            {
                return false;
            }

            members.Clear();
            ref ChildrenBuffer children = ref world.Get<ChildrenBuffer>(groupEntity);
            for (int i = 0; i < children.Count; i++)
            {
                members.Add(children.Get(i));
            }

            return true;
        }

        private static Dictionary<string, Entity> CollectRegionEntities(MapSession session)
        {
            var entityByKey = new Dictionary<string, Entity>(StringComparer.Ordinal);
            if (session.Fields == null || session.RegionIndex == null)
            {
                return entityByKey;
            }

            foreach (FieldLayerData layer in session.Fields.Layers)
            {
                if (layer is not DiscreteIdFieldLayerData discrete)
                {
                    continue;
                }

                for (int regionId = 1; regionId <= discrete.Regions.Count; regionId++)
                {
                    string key = discrete.Regions.GetName(regionId);
                    if (string.IsNullOrEmpty(key))
                    {
                        continue;
                    }

                    if (!session.RegionIndex.TryResolve(discrete.LayerId, regionId, out Entity entity))
                    {
                        continue;
                    }

                    if (entityByKey.TryGetValue(key, out Entity existing) && !existing.Equals(entity))
                    {
                        throw new InvalidOperationException(
                            $"Map '{session.MapId.Value}' has region key '{key}' on more than one field layer; hierarchy keys must be unique per map.");
                    }

                    entityByKey[key] = entity;
                }
            }

            return entityByKey;
        }

        private static Entity ResolveEntity(
            Dictionary<string, Entity> entityByKey, Dictionary<string, Entity> groupByKey, string key)
        {
            if (entityByKey.TryGetValue(key, out Entity region))
            {
                return region;
            }

            if (groupByKey.TryGetValue(key, out Entity group))
            {
                return group;
            }

            throw new InvalidOperationException(
                $"Hierarchy references '{key}' which is neither a materialized region nor a declared group parent on this map.");
        }
    }
}
