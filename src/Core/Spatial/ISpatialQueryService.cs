using System;
using Arch.Core;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial
{
    public interface ISpatialQueryService
    {
        SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer);

        SpatialQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer);

        /// <summary>Cone / sector query. <paramref name="directionDeg"/> is heading (0=+X, 90=+Y), <paramref name="halfAngleDeg"/> is half-aperture.</summary>
        SpatialQueryResult QueryCone(WorldCmInt2 origin, int directionDeg, int halfAngleDeg, int rangeCm, Span<Entity> buffer);

        /// <summary>Oriented rectangle (OBB). Center, half-extents, rotation in degrees.</summary>
        SpatialQueryResult QueryRectangle(WorldCmInt2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer);

        /// <summary>Line / capsule query from origin along direction with half-width.</summary>
        SpatialQueryResult QueryLine(WorldCmInt2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer);

        /// <summary>Query all entities within hex distance &lt;= <paramref name="hexRadius"/> from center.</summary>
        SpatialQueryResult QueryHexRange(HexCoordinates center, int hexRadius, Span<Entity> buffer);

        /// <summary>Query all entities on the hex ring at exactly <paramref name="hexRadius"/> steps from center.</summary>
        SpatialQueryResult QueryHexRing(HexCoordinates center, int hexRadius, Span<Entity> buffer);
    }
}
