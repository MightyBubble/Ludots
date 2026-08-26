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
    /// Lookup of hierarchy groups and baked readonly leaf-to-ancestor visual remaps.
    /// Point queries still walk ChildOf edges; the remaps are presentation-derived and
    /// never become an authoring surface.
    /// </summary>
    public sealed class RegionHierarchyRuntime
    {
        private readonly Dictionary<FieldLayerId, LayerProjectionTable> _projectionByLayer = new();
        private readonly Dictionary<string, int> _projectionIdByKey = new(StringComparer.Ordinal);
        private int _nextProjectionId = 1;

        internal RegionHierarchyRuntime(
            Dictionary<string, Entity> groupByKey,
            World world,
            MapSession session)
        {
            GroupByKey = groupByKey;
            RebuildRemaps(world, session, markDirty: false);
        }

        public IReadOnlyDictionary<string, Entity> GroupByKey { get; }
        public int Revision { get; private set; }

        public void RebuildRemaps(World world, MapSession session)
        {
            RebuildRemaps(world, session, markDirty: true);
        }

        public int GetProjectionRevision(FieldLayerId layerId)
        {
            return RequireProjection(layerId).Revision;
        }

        public int GetMaxProjectedId(FieldLayerId layerId, in FieldDiscreteVisualMapMode mode)
        {
            LayerProjectionTable projection = RequireProjection(layerId);
            int max = 0;
            for (int regionId = 1; regionId < projection.Chains.Length; regionId++)
            {
                int projectedId = projection.Resolve(regionId, in mode);
                if (projectedId > max)
                {
                    max = projectedId;
                }
            }

            return max;
        }

        public int ResolveProjectedId(
            FieldLayerId layerId,
            int leafRegionId,
            in FieldDiscreteVisualMapMode mode)
        {
            LayerProjectionTable projection = RequireProjection(layerId);
            if ((uint)leafRegionId >= (uint)projection.Chains.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(leafRegionId));
            }

            return projection.Resolve(leafRegionId, in mode);
        }

        private void RebuildRemaps(World world, MapSession session, bool markDirty)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(session);

            if (session.Fields == null || session.RegionIndex == null)
            {
                throw new InvalidOperationException(
                    $"Map '{session.MapId.Value}' must materialize field regions before hierarchy remaps are built.");
            }

            var rebuilt = new Dictionary<FieldLayerId, LayerProjectionTable>();
            foreach (FieldLayerData layerData in session.Fields.Layers)
            {
                if (layerData is not DiscreteIdFieldLayerData layer)
                {
                    continue;
                }

                string[][] chains = BuildChains(world, session, layer);
                _projectionByLayer.TryGetValue(layer.LayerId, out LayerProjectionTable? previous);
                bool[] changedRegions = FindChangedRegions(previous?.Chains, chains);
                int revision = previous == null
                    ? 1
                    : HasChanges(changedRegions) ? NextRevision(previous.Revision) : previous.Revision;
                rebuilt.Add(layer.LayerId, BuildProjectionTable(chains, revision));

                if (markDirty && HasChanges(changedRegions))
                {
                    MarkChangedRegionsDirty(layer.Field, changedRegions);
                }
            }

            _projectionByLayer.Clear();
            foreach (KeyValuePair<FieldLayerId, LayerProjectionTable> entry in rebuilt)
            {
                _projectionByLayer.Add(entry.Key, entry.Value);
            }

            Revision = NextRevision(Revision);
        }

        private string[][] BuildChains(World world, MapSession session, DiscreteIdFieldLayerData layer)
        {
            var chains = new string[layer.Regions.Count + 1][];
            chains[0] = Array.Empty<string>();
            var keys = new List<string>(8);
            for (int regionId = 1; regionId <= layer.Regions.Count; regionId++)
            {
                if (!session.RegionIndex!.TryResolve(layer.LayerId, regionId, out Entity regionEntity))
                {
                    throw new InvalidOperationException(
                        $"Map '{session.MapId.Value}' has no materialized entity for field layer '{layer.LayerKey}' region {regionId}.");
                }

                keys.Clear();
                Entity current = regionEntity;
                for (int depth = 0; depth <= 64; depth++)
                {
                    keys.Add(ResolveHierarchyKey(world, current));
                    if (!world.Has<ChildOf>(current))
                    {
                        break;
                    }

                    current = world.Get<ChildOf>(current).Parent;
                    if (!world.IsAlive(current))
                    {
                        throw new InvalidOperationException(
                            $"Hierarchy chain for field layer '{layer.LayerKey}' region {regionId} references a dead parent.");
                    }

                    if (depth == 64)
                    {
                        throw new InvalidOperationException(
                            $"Hierarchy chain for field layer '{layer.LayerKey}' region {regionId} exceeded 64 ancestors.");
                    }
                }

                chains[regionId] = keys.ToArray();
            }

            return chains;
        }

        private LayerProjectionTable BuildProjectionTable(string[][] chains, int revision)
        {
            int maxDepth = 0;
            for (int regionId = 1; regionId < chains.Length; regionId++)
            {
                maxDepth = Math.Max(maxDepth, chains[regionId].Length - 1);
            }

            var depthRemaps = new int[maxDepth][];
            for (int depth = 0; depth < maxDepth; depth++)
            {
                depthRemaps[depth] = new int[chains.Length];
            }

            var groupRemaps = new Dictionary<string, int[]>(StringComparer.Ordinal);
            for (int regionId = 1; regionId < chains.Length; regionId++)
            {
                string[] chain = chains[regionId];
                for (int chainIndex = 0; chainIndex < chain.Length; chainIndex++)
                {
                    string key = chain[chainIndex];
                    int projectedId = GetOrCreateProjectionId(key);
                    if (!groupRemaps.TryGetValue(key, out int[]? groupRemap))
                    {
                        groupRemap = new int[chains.Length];
                        groupRemaps.Add(key, groupRemap);
                    }

                    groupRemap[regionId] = projectedId;
                    if (chainIndex > 0)
                    {
                        depthRemaps[chainIndex - 1][regionId] = projectedId;
                    }
                }
            }

            return new LayerProjectionTable(chains, depthRemaps, groupRemaps, revision);
        }

        private int GetOrCreateProjectionId(string key)
        {
            if (_projectionIdByKey.TryGetValue(key, out int projectionId))
            {
                return projectionId;
            }

            projectionId = _nextProjectionId++;
            _projectionIdByKey.Add(key, projectionId);
            return projectionId;
        }

        private LayerProjectionTable RequireProjection(FieldLayerId layerId)
        {
            if (!_projectionByLayer.TryGetValue(layerId, out LayerProjectionTable? projection))
            {
                throw new InvalidOperationException(
                    $"Hierarchy visual projection has no discrete field layer id {layerId.Value}.");
            }

            return projection;
        }

        private static string ResolveHierarchyKey(World world, Entity entity)
        {
            if (!world.IsAlive(entity))
            {
                throw new InvalidOperationException("Hierarchy visual projection encountered a dead entity.");
            }

            if (world.Has<RegionCm>(entity))
            {
                return world.Get<RegionCm>(entity).RegionKey;
            }

            if (world.Has<RegionGroupCm>(entity))
            {
                return world.Get<RegionGroupCm>(entity).GroupKey;
            }

            throw new InvalidOperationException(
                $"Hierarchy entity {entity.Id} has neither RegionCm nor RegionGroupCm.");
        }

        private static bool[] FindChangedRegions(string[][]? previous, string[][] current)
        {
            var changed = new bool[current.Length];
            for (int regionId = 1; regionId < current.Length; regionId++)
            {
                changed[regionId] =
                    previous == null ||
                    regionId >= previous.Length ||
                    !ChainsEqual(previous[regionId], current[regionId]);
            }

            return changed;
        }

        private static bool ChainsEqual(string[] left, string[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasChanges(bool[] changed)
        {
            for (int i = 1; i < changed.Length; i++)
            {
                if (changed[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static void MarkChangedRegionsDirty(ChunkedField2D<int> field, bool[] changedRegions)
        {
            for (int chunkIndex = 0; chunkIndex < field.ChunkCount; chunkIndex++)
            {
                FieldChunk2D<int> chunk = field.GetChunkAt(chunkIndex);
                for (int local = 0; local < chunk.CellCount; local++)
                {
                    int regionId = chunk.Get(local);
                    if ((uint)regionId >= (uint)changedRegions.Length || !changedRegions[regionId])
                    {
                        continue;
                    }

                    field.MarkDirty(field.Grid.CellFromChunkLocal(chunk.ChunkX, chunk.ChunkY, local));
                }
            }
        }

        private static int NextRevision(int revision) => revision == int.MaxValue ? 1 : revision + 1;

        private sealed class LayerProjectionTable
        {
            public LayerProjectionTable(
                string[][] chains,
                int[][] depthRemaps,
                Dictionary<string, int[]> groupRemaps,
                int revision)
            {
                Chains = chains;
                DepthRemaps = depthRemaps;
                GroupRemaps = groupRemaps;
                Revision = revision;
            }

            public string[][] Chains { get; }
            public int[][] DepthRemaps { get; }
            public Dictionary<string, int[]> GroupRemaps { get; }
            public int Revision { get; }

            public int Resolve(int leafRegionId, in FieldDiscreteVisualMapMode mode)
            {
                return mode.Kind switch
                {
                    FieldDiscreteVisualMapModeKind.Leaf => leafRegionId,
                    FieldDiscreteVisualMapModeKind.AncestorDepth =>
                        mode.Depth <= DepthRemaps.Length ? DepthRemaps[mode.Depth - 1][leafRegionId] : 0,
                    FieldDiscreteVisualMapModeKind.GroupKey =>
                        GroupRemaps.TryGetValue(mode.GroupKey, out int[]? remap)
                            ? remap[leafRegionId]
                            : throw new InvalidOperationException(
                                $"Hierarchy visual projection group key '{mode.GroupKey}' is not present in this field layer."),
                    _ => throw new ArgumentOutOfRangeException(nameof(mode)),
                };
            }
        }
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
                return new RegionHierarchyRuntime(groupByKey, world, session);
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

            return new RegionHierarchyRuntime(groupByKey, world, session);
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
