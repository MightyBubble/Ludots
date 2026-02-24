using System;
using Arch.Core;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial
{
    /// <summary>
    /// Base class for spatial query backends that delegate to an ISpatialPartitionWorld.
    /// Converts world-space AABB queries to cell-space IntRect queries.
    /// </summary>
    public abstract class SpatialPartitionBackendBase : ISpatialQueryBackend
    {
        private readonly ISpatialPartitionWorld _world;
        private readonly int _cellSizeCm;

        protected SpatialPartitionBackendBase(ISpatialPartitionWorld world, int cellSizeCm)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            if (cellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeCm));
            _cellSizeCm = cellSizeCm;
        }

        public int QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer, out int dropped)
        {
            IntRect rect = MathUtil.WorldAabbToCellRect(in bounds, _cellSizeCm);
            return _world.Query(in rect, buffer, out dropped);
        }

        public int QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer, out int dropped)
        {
            WorldAabbCm bounds = WorldAabbCm.FromCenterRadius(center, radiusCm);
            return QueryAabb(in bounds, buffer, out dropped);
        }
    }
}
