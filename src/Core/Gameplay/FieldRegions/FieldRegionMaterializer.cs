using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Fields;
using Ludots.Core.Map;

namespace Ludots.Core.Gameplay.FieldRegions
{
    /// <summary>
    /// Materializes one entity per registered region of every discrete-id layer enabled
    /// on the map (MapEntity + RegionCm + RegionFootprintCm) and fills the session's
    /// RegionEntityIndex. Bake-phase: runs once at map load.
    /// </summary>
    public static class FieldRegionMaterializer
    {
        public static RegionEntityIndex Materialize(World world, MapSession session)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (session == null) throw new ArgumentNullException(nameof(session));

            var index = new RegionEntityIndex();
            FieldSessionStore? store = session.Fields;
            if (store == null)
            {
                return index;
            }

            var mapTag = new MapEntity { MapId = session.MapId };
            foreach (FieldLayerData layer in store.Layers)
            {
                if (layer is not DiscreteIdFieldLayerData discrete)
                {
                    continue;
                }

                Dictionary<int, int> cellCounts = CountRegionCells(discrete.Field);
                RegionIdRegistry regions = discrete.Regions;
                for (int regionId = 1; regionId <= regions.Count; regionId++)
                {
                    string regionKey = regions.GetName(regionId);
                    if (string.IsNullOrEmpty(regionKey))
                    {
                        throw new InvalidOperationException(
                            $"Field layer '{layer.LayerKey}' has region id {regionId} without a registered key.");
                    }

                    cellCounts.TryGetValue(regionId, out int cellCount);
                    Entity entity = world.Create(
                        mapTag,
                        new RegionCm { LayerId = discrete.LayerId, RegionId = regionId, RegionKey = regionKey },
                        new RegionFootprintCm { CellCount = cellCount });
                    index.Put(discrete.LayerId, regionId, entity);
                }
            }

            return index;
        }

        /// <summary>
        /// Runtime counterpart of bake materialization: creates entities for any
        /// registered region of the layer that has no entity yet (fresh keys born from
        /// a runtime redraw) and is a no-op for already-materialized regions.
        /// </summary>
        public static void EnsureRuntimeRegions(
            World world,
            MapSession session,
            RegionEntityIndex index,
            DiscreteIdFieldLayerData layer,
            IReadOnlyDictionary<int, int> cellCounts)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (layer == null) throw new ArgumentNullException(nameof(layer));

            var mapTag = new MapEntity { MapId = session.MapId };
            for (int regionId = 1; regionId <= layer.Regions.Count; regionId++)
            {
                if (index.TryResolve(layer.LayerId, regionId, out _))
                {
                    continue;
                }

                string regionKey = layer.Regions.GetName(regionId);
                cellCounts.TryGetValue(regionId, out int cellCount);
                Entity entity = world.Create(
                    mapTag,
                    new RegionCm { LayerId = layer.LayerId, RegionId = regionId, RegionKey = regionKey },
                    new RegionFootprintCm { CellCount = cellCount });
                index.Put(layer.LayerId, regionId, entity);
            }
        }

        public static Dictionary<int, int> CountRegionCells(ChunkedField2D<int> field)
        {
            var counts = new Dictionary<int, int>();
            int cellCount = field.Grid.ChunkSizeCells * field.Grid.ChunkSizeCells;
            for (int chunkIndex = 0; chunkIndex < field.ChunkCount; chunkIndex++)
            {
                FieldChunk2D<int> chunk = field.GetChunkAt(chunkIndex);
                for (int local = 0; local < cellCount; local++)
                {
                    int regionId = chunk.Get(local);
                    if (regionId == 0)
                    {
                        continue;
                    }

                    counts.TryGetValue(regionId, out int current);
                    counts[regionId] = current + 1;
                }
            }

            return counts;
        }
    }
}
