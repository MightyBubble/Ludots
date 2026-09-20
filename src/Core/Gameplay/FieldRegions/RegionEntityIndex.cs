using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Fields;

namespace Ludots.Core.Gameplay.FieldRegions
{
    /// <summary>
    /// Per-map direct table from (layer, regionId) to the materialized region entity.
    /// Built at materialization time; point queries resolve in O(1) without scanning.
    /// </summary>
    public sealed class RegionEntityIndex
    {
        private readonly Dictionary<long, Entity> _entities = new();

        public int Count => _entities.Count;

        public static long Pack(FieldLayerId layerId, int regionId)
        {
            return ((long)layerId.Value << 32) ^ (uint)regionId;
        }

        public bool TryResolve(FieldLayerId layerId, int regionId, out Entity entity)
        {
            return _entities.TryGetValue(Pack(layerId, regionId), out entity);
        }

        internal void Put(FieldLayerId layerId, int regionId, Entity entity)
        {
            _entities[Pack(layerId, regionId)] = entity;
        }

        internal bool Remove(FieldLayerId layerId, int regionId)
        {
            return _entities.Remove(Pack(layerId, regionId));
        }
    }
}
