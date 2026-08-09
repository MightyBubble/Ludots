using System;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Formal integer/SoA obstacle overlay over published walkability.
    /// Blocks walkable spans when the agent occupied half-open vertical interval overlaps an
    /// obstacle vertical interval AND the obstacle XZ footprint conservatively intersects the
    /// span's closed raster cell. Solid-only: does not create walk surfaces on obstacle tops.
    /// Warmed success path allocates 0 managed bytes.
    /// </summary>
    public static class LayeredSpanObstacleOverlayBuilder
    {
        public static void Apply(
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walkability,
            in LayeredSpanRasterGridSpec grid,
            INavObstacleSource obstacles,
            string layerId,
            int agentHeightCm)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (walkability == null) throw new ArgumentNullException(nameof(walkability));
            if (obstacles == null) throw new ArgumentNullException(nameof(obstacles));

            if (!raw.HasPublishedContent)
            {
                throw new InvalidOperationException(
                    "LayeredSpanObstacleOverlayBuilder requires published raw scratch content.");
            }

            if (!walkability.HasPublishedContent || !walkability.WasBuiltFrom(raw))
            {
                throw new InvalidOperationException(
                    "LayeredSpanObstacleOverlayBuilder requires walkability published from the same raw scratch generation.");
            }

            if (string.IsNullOrWhiteSpace(layerId) ||
                !string.Equals(layerId.Trim(), layerId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "LayeredSpanObstacleOverlayBuilder requires a non-empty trimmed nav layer id.");
            }

            if (agentHeightCm <= 0)
            {
                throw new InvalidOperationException(
                    "LayeredSpanObstacleOverlayBuilder.agentHeightCm must be > 0.");
            }

            if (walkability.ColumnCount != raw.ColumnCount ||
                walkability.ClassifiedSpanCount != raw.SpanCount)
            {
                throw new InvalidOperationException(
                    "LayeredSpanObstacleOverlayBuilder walkability column/span counts must match raw scratch.");
            }

            if (raw.ColumnCount != grid.ColumnCount)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanObstacleOverlayBuilder grid.ColumnCount ({grid.ColumnCount}) must equal raw.ColumnCount ({raw.ColumnCount}).");
            }

            int obstacleCount = obstacles.ObstacleCount;
            if (obstacleCount == 0)
            {
                // Deterministic no-op: still republish so downstream provenance sees a post-overlay generation.
                walkability.CommitOverlayRepublish(raw, walkability.WalkableSpanCount);
                return;
            }

            ReadOnlySpan<int> columnOffsets = raw.ColumnSpanOffsets;
            ReadOnlySpan<int> columnCounts = raw.ColumnSpanCounts;
            ReadOnlySpan<int> spanMaxY = raw.SpanMaxYcm;
            Span<LayeredSpanWalkabilityStatus> status = walkability.MutableSpanStatus;
            Span<int> walkableIndices = walkability.MutableWalkableSpanIndices;
            Span<int> walkableCounts = walkability.MutableColumnWalkableCounts;
            Span<int> walkableOffsets = walkability.MutableColumnWalkableOffsets;

            int columnCount = raw.ColumnCount;
            int colCountX = grid.ColumnCountX;
            int originX = grid.OriginXcm;
            int originZ = grid.OriginZcm;
            int cell = grid.CellSizeCm;
            int walkableCapacity = walkability.WalkableSpanCapacity;
            int walkableCount = 0;

            for (int col = 0; col < columnCount; col++)
            {
                int start = columnOffsets[col];
                int count = columnCounts[col];
                int end = start + count;
                int cx = col % colCountX;
                int cz = col / colCountX;
                int cellMinX = originX + (cx * cell);
                int cellMaxX = cellMinX + cell;
                int cellMinZ = originZ + (cz * cell);
                int cellMaxZ = cellMinZ + cell;
                int columnWalkableStart = walkableCount;

                for (int span = start; span < end; span++)
                {
                    if (status[span] != LayeredSpanWalkabilityStatus.Walkable)
                    {
                        continue;
                    }

                    int surfaceY = spanMaxY[span];
                    if (IsBlockedByAnyObstacle(
                            obstacles,
                            obstacleCount,
                            layerId,
                            surfaceY,
                            agentHeightCm,
                            cellMinX,
                            cellMaxX,
                            cellMinZ,
                            cellMaxZ))
                    {
                        status[span] = LayeredSpanWalkabilityStatus.ObstacleBlocked;
                        continue;
                    }

                    if (walkableCount < walkableCapacity)
                    {
                        walkableIndices[walkableCount] = span;
                    }

                    walkableCount++;
                }

                walkableCounts[col] = walkableCount - columnWalkableStart;
            }

            if (walkableCount > walkableCapacity)
            {
                walkability.Reset();
                throw new InvalidOperationException(
                    $"LayeredSpanWalkabilityScratch.walkableSpanCapacity ({walkableCapacity}); required {walkableCount}.");
            }

            int prefix = 0;
            for (int col = 0; col < columnCount; col++)
            {
                walkableOffsets[col] = prefix;
                prefix += walkableCounts[col];
            }

            walkableOffsets[columnCount] = prefix;
            walkability.CommitOverlayRepublish(raw, walkableCount);
        }

        private static bool IsBlockedByAnyObstacle(
            INavObstacleSource obstacles,
            int obstacleCount,
            string layerId,
            int surfaceYcm,
            int agentHeightCm,
            int cellMinX,
            int cellMaxX,
            int cellMinZ,
            int cellMaxZ)
        {
            long agentMaxExclusiveLong = (long)surfaceYcm + agentHeightCm;
            if (agentMaxExclusiveLong > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "LayeredSpanObstacleOverlayBuilder agent occupied vertical interval overflows int centimetres.");
            }

            int agentMaxExclusive = (int)agentMaxExclusiveLong;

            for (int i = 0; i < obstacleCount; i++)
            {
                if (!obstacles.IsEnabled(i))
                {
                    continue;
                }

                if (!obstacles.MatchesLayer(i, layerId))
                {
                    continue;
                }

                obstacles.GetVerticalRange(i, out int omin, out int omax);
                if (omin >= omax)
                {
                    throw new InvalidOperationException(
                        $"INavObstacleSource[{i}].minYcm/maxYcm must author half-open [minYcm,maxYcm) with minYcm < maxYcm.");
                }

                // Half-open overlap: touching-only in Y is not overlap.
                if (surfaceYcm >= omax || omin >= agentMaxExclusive)
                {
                    continue;
                }

                switch (obstacles.GetKind(i))
                {
                    case NavObstacleKind.Circle:
                        obstacles.GetCircle(i, out int centerX, out int centerZ, out int radiusCm);
                        if (CircleIntersectsClosedCell(centerX, centerZ, radiusCm, cellMinX, cellMaxX, cellMinZ, cellMaxZ))
                        {
                            return true;
                        }
                        break;
                    case NavObstacleKind.Polygon:
                        if (PolygonIntersectsClosedCell(obstacles, i, cellMinX, cellMaxX, cellMinZ, cellMaxZ))
                        {
                            return true;
                        }
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"INavObstacleSource[{i}].kind '{obstacles.GetKind(i)}' is not supported by LayeredSpanObstacleOverlayBuilder.");
                }
            }

            return false;
        }

        private static bool CircleIntersectsClosedCell(
            int centerX,
            int centerZ,
            int radiusCm,
            int cellMinX,
            int cellMaxX,
            int cellMinZ,
            int cellMaxZ)
        {
            if (radiusCm <= 0)
            {
                throw new InvalidOperationException("Circle nav obstacle radiusCm must be > 0.");
            }

            int closestX = centerX < cellMinX ? cellMinX : (centerX > cellMaxX ? cellMaxX : centerX);
            int closestZ = centerZ < cellMinZ ? cellMinZ : (centerZ > cellMaxZ ? cellMaxZ : centerZ);
            Int128 dx = (Int128)centerX - closestX;
            Int128 dz = (Int128)centerZ - closestZ;
            Int128 r = radiusCm;
            return (dx * dx) + (dz * dz) <= (r * r);
        }

        private static bool PolygonIntersectsClosedCell(
            INavObstacleSource obstacles,
            int obstacleIndex,
            int cellMinX,
            int cellMaxX,
            int cellMinZ,
            int cellMaxZ)
        {
            int vertexCount = obstacles.GetPolygonVertexCount(obstacleIndex);
            if (vertexCount < 3)
            {
                throw new InvalidOperationException(
                    $"INavObstacleSource[{obstacleIndex}] polygon requires at least 3 points.");
            }

            // Any polygon vertex inside closed cell.
            for (int i = 0; i < vertexCount; i++)
            {
                obstacles.GetPolygonVertex(obstacleIndex, i, out int x, out int z);
                if (x >= cellMinX && x <= cellMaxX && z >= cellMinZ && z <= cellMaxZ)
                {
                    return true;
                }
            }

            // Any cell corner inside polygon (conservative containment).
            if (PointInPolygonExact(cellMinX, cellMinZ, obstacles, obstacleIndex, vertexCount) ||
                PointInPolygonExact(cellMaxX, cellMinZ, obstacles, obstacleIndex, vertexCount) ||
                PointInPolygonExact(cellMaxX, cellMaxZ, obstacles, obstacleIndex, vertexCount) ||
                PointInPolygonExact(cellMinX, cellMaxZ, obstacles, obstacleIndex, vertexCount))
            {
                return true;
            }

            // Any polygon edge intersects any closed cell side (including touching).
            for (int i = 0, j = vertexCount - 1; i < vertexCount; j = i++)
            {
                obstacles.GetPolygonVertex(obstacleIndex, i, out int ax, out int az);
                obstacles.GetPolygonVertex(obstacleIndex, j, out int bx, out int bz);
                if (SegmentsIntersectInclusive(ax, az, bx, bz, cellMinX, cellMinZ, cellMaxX, cellMinZ) ||
                    SegmentsIntersectInclusive(ax, az, bx, bz, cellMaxX, cellMinZ, cellMaxX, cellMaxZ) ||
                    SegmentsIntersectInclusive(ax, az, bx, bz, cellMaxX, cellMaxZ, cellMinX, cellMaxZ) ||
                    SegmentsIntersectInclusive(ax, az, bx, bz, cellMinX, cellMaxZ, cellMinX, cellMinZ))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PointInPolygonExact(
            int xcm,
            int zcm,
            INavObstacleSource obstacles,
            int obstacleIndex,
            int vertexCount)
        {
            // Exact even-odd ray cast using Int128 cross products; no division/float.
            bool inside = false;
            int j = vertexCount - 1;
            for (int i = 0; i < vertexCount; j = i++)
            {
                obstacles.GetPolygonVertex(obstacleIndex, i, out int xi, out int zi);
                obstacles.GetPolygonVertex(obstacleIndex, j, out int xj, out int zj);

                bool ziAbove = zi > zcm;
                bool zjAbove = zj > zcm;
                if (ziAbove == zjAbove)
                {
                    continue;
                }

                // x < xi + (xj - xi) * (zcm - zi) / (zj - zi)
                // <=> (x - xi) * (zj - zi) < (xj - xi) * (zcm - zi)   when (zj - zi) > 0
                // and inequality flips when (zj - zi) < 0.
                Int128 denom = (Int128)zj - zi;
                Int128 lhs = ((Int128)xcm - xi) * denom;
                Int128 rhs = ((Int128)xj - xi) * ((Int128)zcm - zi);
                bool crosses = denom > 0 ? lhs < rhs : lhs > rhs;
                if (crosses)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static bool SegmentsIntersectInclusive(
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz,
            int dx,
            int dz)
        {
            Int128 o1 = Orientation(ax, az, bx, bz, cx, cz);
            Int128 o2 = Orientation(ax, az, bx, bz, dx, dz);
            Int128 o3 = Orientation(cx, cz, dx, dz, ax, az);
            Int128 o4 = Orientation(cx, cz, dx, dz, bx, bz);

            if (((o1 > 0 && o2 < 0) || (o1 < 0 && o2 > 0)) &&
                ((o3 > 0 && o4 < 0) || (o3 < 0 && o4 > 0)))
            {
                return true;
            }

            return (o1 == 0 && OnSegment(ax, az, cx, cz, bx, bz)) ||
                   (o2 == 0 && OnSegment(ax, az, dx, dz, bx, bz)) ||
                   (o3 == 0 && OnSegment(cx, cz, ax, az, dx, dz)) ||
                   (o4 == 0 && OnSegment(cx, cz, bx, bz, dx, dz));
        }

        private static Int128 Orientation(int ax, int az, int bx, int bz, int cx, int cz)
            => ((Int128)bx - ax) * ((Int128)cz - az) - ((Int128)bz - az) * ((Int128)cx - ax);

        private static bool OnSegment(int ax, int az, int px, int pz, int bx, int bz)
            => px >= Math.Min(ax, bx) &&
               px <= Math.Max(ax, bx) &&
               pz >= Math.Min(az, bz) &&
               pz <= Math.Max(az, bz);
    }
}
