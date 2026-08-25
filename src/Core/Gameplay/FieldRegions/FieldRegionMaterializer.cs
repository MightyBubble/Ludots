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
                        new RegionCm { LayerId = discrete.LayerId, RegionId = regionId },
                        new RegionFootprintCm { CellCount = cellCount });
                    index.Put(discrete.LayerId, regionId, entity);
                }
            }

            return index;
        }

        private static Dictionary<int, int> CountRegionCells(ChunkedField2D<int> field)
        {
            var counts = new Dictionary<int, int>();
            var buffer = new FieldCellValue2D<int>[field.NonDefaultCount];
            field.CopyNonDefaultCells(buffer);
            foreach (FieldCellValue2D<int> cell in buffer)
            {
                counts.TryGetValue(cell.Value, out int current);
                counts[cell.Value] = current + 1;
            }

            return counts;
        }
    }
}
