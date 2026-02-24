using System;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Physics;

namespace Ludots.Core.Spatial
{
    public sealed class PhysicsWorldSpatialBackend : ISpatialQueryBackend
    {
        private readonly PhysicsWorld _world;
        private readonly ISpatialCoordinateConverter _coords;

        public PhysicsWorldSpatialBackend(PhysicsWorld world, ISpatialCoordinateConverter coords)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _coords = coords ?? throw new ArgumentNullException(nameof(coords));
        }

        public int QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer, out int dropped)
        {
            IntRect rect = MathUtil.WorldAabbToCellRect(in bounds, _coords.GridCellSizeCm);
            return _world.Query(rect, buffer, out dropped);
        }

        public int QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer, out int dropped)
        {
            WorldAabbCm bounds = WorldAabbCm.FromCenterRadius(center, radiusCm);
            return QueryAabb(in bounds, buffer, out dropped);
        }
    }
}
