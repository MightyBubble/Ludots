using System;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Shared Core integer triangle-vs-obstacle predicate for CDT input prep and Recast border-portal evidence.
    /// Recast walkable holes use DotRecast convex volumes (RC_NULL_AREA), not whole-triangle deletion.
    /// Vertical overlap is half-open and uses the agent occupied volume [surfaceMinY, surfaceMinY+agentHeight)
    /// conservatively against obstacle [minY,maxY). AreaId is not reinterpreted; obstacles remain blocking.
    /// Border-portal evidence must clip along the world boundary interval: a corner obstacle on a coarse
    /// tile triangle must not reject free side-route intervals on the same shared border.
    /// </summary>
    public static class NavTriangleObstaclePredicate
    {
        /// <summary>
        /// True when every centimetre of <paramref name="alongMinCm"/>..<paramref name="alongMaxCm"/>
        /// on the exact world boundary plane is covered by at least one vertically overlapping obstacle
        /// expanded by <paramref name="agentRadiusCm"/>. Point-length intervals are treated as blocked.
        /// </summary>
        public static bool IsBoundaryAlongIntervalFullyBlocked(
            NavPortalSide side,
            int boundaryWorldCm,
            int alongMinCm,
            int alongMaxCm,
            int surfaceMinYcm,
            INavObstacleSource obstacles,
            string layerId,
            int agentHeightCm,
            int agentRadiusCm)
        {
            if (obstacles == null) throw new ArgumentNullException(nameof(obstacles));
            if (alongMaxCm <= alongMinCm)
            {
                return true;
            }

            RequireLayerId(layerId);
            if (agentHeightCm <= 0)
            {
                throw new InvalidOperationException("NavTriangleObstaclePredicate.agentHeightCm must be > 0.");
            }

            if (agentRadiusCm < 0)
            {
                throw new InvalidOperationException("NavTriangleObstaclePredicate.agentRadiusCm must be >= 0.");
            }

            long agentMaxExclusiveLong = (long)surfaceMinYcm + agentHeightCm;
            if (agentMaxExclusiveLong > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "NavTriangleObstaclePredicate agent occupied vertical interval overflows int centimetres.");
            }

            int agentMaxExclusive = (int)agentMaxExclusiveLong;
            // Repeated greedy extension: each pass rescans the source in order and extends the
            // frontier from coveredThrough without allocating interval scratch. A pass always
            // finishes so later matching entries still validate even when coverage already reaches
            // alongMaxCm mid-scan.
            int coveredThrough = alongMinCm;
            while (coveredThrough < alongMaxCm)
            {
                int furthest = coveredThrough;
                for (int i = 0; i < obstacles.ObstacleCount; i++)
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

                    if (surfaceMinYcm >= omax || omin >= agentMaxExclusive)
                    {
                        continue;
                    }

                    if (!TryGetBoundaryBlockedAlong(
                            side,
                            boundaryWorldCm,
                            alongMinCm,
                            alongMaxCm,
                            obstacles,
                            i,
                            agentRadiusCm,
                            out int bMin,
                            out int bMax))
                    {
                        continue;
                    }

                    if (bMin <= coveredThrough && bMax > furthest)
                    {
                        furthest = bMax;
                    }
                }

                if (furthest == coveredThrough)
                {
                    return false;
                }

                if (furthest >= alongMaxCm)
                {
                    return true;
                }

                coveredThrough = furthest;
            }

            return true;
        }

        private static bool TryGetBoundaryBlockedAlong(
            NavPortalSide side,
            int boundaryWorldCm,
            int alongMinCm,
            int alongMaxCm,
            INavObstacleSource obstacles,
            int index,
            int agentRadiusCm,
            out int blockedMin,
            out int blockedMax)
        {
            blockedMin = 0;
            blockedMax = 0;
            switch (obstacles.GetKind(index))
            {
                case NavObstacleKind.Circle:
                {
                    obstacles.GetCircle(index, out int centerX, out int centerZ, out int radiusCm);
                    if (radiusCm <= 0)
                    {
                        throw new InvalidOperationException(
                            $"INavObstacleSource[{index}] circle radiusCm must be > 0.");
                    }

                    int inflatedRadius = checked(radiusCm + agentRadiusCm);
                    int axisDelta;
                    int centerAlong;
                    if (side is NavPortalSide.West or NavPortalSide.East)
                    {
                        axisDelta = boundaryWorldCm - centerX;
                        centerAlong = centerZ;
                    }
                    else
                    {
                        axisDelta = boundaryWorldCm - centerZ;
                        centerAlong = centerX;
                    }

                    Int128 axisAbs = axisDelta < 0 ? -(Int128)axisDelta : (Int128)axisDelta;
                    Int128 r = inflatedRadius;
                    if (axisAbs > r)
                    {
                        return false;
                    }

                    Int128 halfSq = (r * r) - (axisAbs * axisAbs);
                    int half = SqrtFloor(halfSq);
                    blockedMin = checked(centerAlong - half);
                    blockedMax = checked(centerAlong + half);
                    break;
                }
                case NavObstacleKind.Polygon:
                {
                    // Conservative AABB projection onto the boundary plane (inflated by agent radius).
                    int vertexCount = obstacles.GetPolygonVertexCount(index);
                    if (vertexCount < 3)
                    {
                        throw new InvalidOperationException(
                            $"INavObstacleSource[{index}] polygon requires at least 3 points.");
                    }

                    obstacles.GetPolygonVertex(index, 0, out int minX, out int minZ);
                    int maxX = minX;
                    int maxZ = minZ;
                    for (int v = 1; v < vertexCount; v++)
                    {
                        obstacles.GetPolygonVertex(index, v, out int x, out int z);
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (z < minZ) minZ = z;
                        if (z > maxZ) maxZ = z;
                    }

                    minX = checked(minX - agentRadiusCm);
                    maxX = checked(maxX + agentRadiusCm);
                    minZ = checked(minZ - agentRadiusCm);
                    maxZ = checked(maxZ + agentRadiusCm);
                    if (side is NavPortalSide.West or NavPortalSide.East)
                    {
                        if (boundaryWorldCm < minX || boundaryWorldCm > maxX)
                        {
                            return false;
                        }

                        blockedMin = minZ;
                        blockedMax = maxZ;
                    }
                    else
                    {
                        if (boundaryWorldCm < minZ || boundaryWorldCm > maxZ)
                        {
                            return false;
                        }

                        blockedMin = minX;
                        blockedMax = maxX;
                    }

                    break;
                }
                default:
                    throw new InvalidOperationException(
                        $"INavObstacleSource[{index}].kind '{obstacles.GetKind(index)}' is not supported by boundary portal clipping.");
            }

            if (blockedMin < alongMinCm) blockedMin = alongMinCm;
            if (blockedMax > alongMaxCm) blockedMax = alongMaxCm;
            return blockedMax > blockedMin;
        }

        private static int SqrtFloor(Int128 value)
        {
            if (value < 0)
            {
                throw new InvalidOperationException("SqrtFloor requires a non-negative radicand.");
            }

            if (value == 0)
            {
                return 0;
            }

            // Integer sqrt via Newton; result fits int because callers clamp to centimetre radii.
            Int128 x = value;
            Int128 y = (x + 1) / 2;
            while (y < x)
            {
                x = y;
                y = (x + (value / x)) / 2;
            }

            if (x > int.MaxValue)
            {
                throw new OverflowException("SqrtFloor result overflows int centimetres.");
            }

            return (int)x;
        }

        public static bool IsTriangleBlocked(
            int ax,
            int ay,
            int az,
            int bx,
            int by,
            int bz,
            int cx,
            int cy,
            int cz,
            INavObstacleSource obstacles,
            string layerId,
            int agentHeightCm,
            int agentRadiusCm)
        {
            if (obstacles == null) throw new ArgumentNullException(nameof(obstacles));
            if (obstacles.ObstacleCount == 0)
            {
                return false;
            }

            RequireLayerId(layerId);
            if (agentHeightCm <= 0)
            {
                throw new InvalidOperationException("NavTriangleObstaclePredicate.agentHeightCm must be > 0.");
            }

            if (agentRadiusCm < 0)
            {
                throw new InvalidOperationException("NavTriangleObstaclePredicate.agentRadiusCm must be >= 0.");
            }

            int surfaceMinY = ay;
            if (by < surfaceMinY) surfaceMinY = by;
            if (cy < surfaceMinY) surfaceMinY = cy;

            long agentMaxExclusiveLong = (long)surfaceMinY + agentHeightCm;
            if (agentMaxExclusiveLong > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "NavTriangleObstaclePredicate agent occupied vertical interval overflows int centimetres.");
            }

            int agentMaxExclusive = (int)agentMaxExclusiveLong;

            // Inflate triangle XZ AABB by agent radius for conservative blocking.
            int minX = ax;
            if (bx < minX) minX = bx;
            if (cx < minX) minX = cx;
            int maxX = ax;
            if (bx > maxX) maxX = bx;
            if (cx > maxX) maxX = cx;
            int minZ = az;
            if (bz < minZ) minZ = bz;
            if (cz < minZ) minZ = cz;
            int maxZ = az;
            if (bz > maxZ) maxZ = bz;
            if (cz > maxZ) maxZ = cz;

            int expandedMinX = checked(minX - agentRadiusCm);
            int expandedMaxX = checked(maxX + agentRadiusCm);
            int expandedMinZ = checked(minZ - agentRadiusCm);
            int expandedMaxZ = checked(maxZ + agentRadiusCm);

            int mx = (ax + bx + cx) / 3;
            int mz = (az + bz + cz) / 3;

            for (int i = 0; i < obstacles.ObstacleCount; i++)
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

                // Half-open overlap: endpoint-only touch is not overlap.
                if (surfaceMinY >= omax || omin >= agentMaxExclusive)
                {
                    continue;
                }

                switch (obstacles.GetKind(i))
                {
                    case NavObstacleKind.Circle:
                    {
                        obstacles.GetCircle(i, out int centerX, out int centerZ, out int radiusCm);
                        int inflatedRadius = checked(radiusCm + agentRadiusCm);
                        if (TriangleIntersectsCircle(
                                ax, az, bx, bz, cx, cz,
                                expandedMinX, expandedMaxX, expandedMinZ, expandedMaxZ,
                                centerX, centerZ, inflatedRadius))
                        {
                            return true;
                        }

                        break;
                    }
                    case NavObstacleKind.Polygon:
                        if (TriangleIntersectsPolygon(
                                ax, az, bx, bz, cx, cz,
                                mx, mz,
                                expandedMinX, expandedMaxX, expandedMinZ, expandedMaxZ,
                                obstacles, i))
                        {
                            return true;
                        }

                        break;
                    default:
                        throw new InvalidOperationException(
                            $"INavObstacleSource[{i}].kind '{obstacles.GetKind(i)}' is not supported by NavTriangleObstaclePredicate.");
                }
            }

            return false;
        }

        private static void RequireLayerId(string layerId)
        {
            if (string.IsNullOrWhiteSpace(layerId) ||
                !string.Equals(layerId.Trim(), layerId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "NavTriangleObstaclePredicate requires a non-empty trimmed nav layer id.");
            }
        }

        private static bool TriangleIntersectsCircle(
            int ax, int az, int bx, int bz, int cx, int cz,
            int aabbMinX, int aabbMaxX, int aabbMinZ, int aabbMaxZ,
            int centerX, int centerZ, int radiusCm)
        {
            if (radiusCm <= 0)
            {
                throw new InvalidOperationException("Circle nav obstacle radiusCm must be > 0.");
            }

            // Quick reject against inflated triangle AABB.
            int closestX = centerX < aabbMinX ? aabbMinX : (centerX > aabbMaxX ? aabbMaxX : centerX);
            int closestZ = centerZ < aabbMinZ ? aabbMinZ : (centerZ > aabbMaxZ ? aabbMaxZ : centerZ);
            Int128 dx = (Int128)centerX - closestX;
            Int128 dz = (Int128)centerZ - closestZ;
            Int128 r = radiusCm;
            if ((dx * dx) + (dz * dz) > (r * r))
            {
                return false;
            }

            if (PointInTriangleXZ(centerX, centerZ, ax, az, bx, bz, cx, cz))
            {
                return true;
            }

            return SegmentDistanceSq(ax, az, bx, bz, centerX, centerZ) <= (r * r) ||
                   SegmentDistanceSq(bx, bz, cx, cz, centerX, centerZ) <= (r * r) ||
                   SegmentDistanceSq(cx, cz, ax, az, centerX, centerZ) <= (r * r);
        }

        private static bool TriangleIntersectsPolygon(
            int ax, int az, int bx, int bz, int cx, int cz,
            int mx, int mz,
            int aabbMinX, int aabbMaxX, int aabbMinZ, int aabbMaxZ,
            INavObstacleSource obstacles,
            int obstacleIndex)
        {
            int vertexCount = obstacles.GetPolygonVertexCount(obstacleIndex);
            if (vertexCount < 3)
            {
                throw new InvalidOperationException(
                    $"INavObstacleSource[{obstacleIndex}] polygon requires at least 3 points.");
            }

            // Any triangle vertex inside polygon.
            if (PointInPolygonExact(ax, az, obstacles, obstacleIndex, vertexCount) ||
                PointInPolygonExact(bx, bz, obstacles, obstacleIndex, vertexCount) ||
                PointInPolygonExact(cx, cz, obstacles, obstacleIndex, vertexCount) ||
                PointInPolygonExact(mx, mz, obstacles, obstacleIndex, vertexCount))
            {
                return true;
            }

            // Any polygon vertex inside triangle or inflated AABB containment + edge touches.
            for (int i = 0; i < vertexCount; i++)
            {
                obstacles.GetPolygonVertex(obstacleIndex, i, out int x, out int z);
                if (x >= aabbMinX && x <= aabbMaxX && z >= aabbMinZ && z <= aabbMaxZ &&
                    PointInTriangleXZ(x, z, ax, az, bx, bz, cx, cz))
                {
                    return true;
                }
            }

            for (int i = 0, j = vertexCount - 1; i < vertexCount; j = i++)
            {
                obstacles.GetPolygonVertex(obstacleIndex, i, out int px, out int pz);
                obstacles.GetPolygonVertex(obstacleIndex, j, out int qx, out int qz);
                if (SegmentsIntersectInclusive(ax, az, bx, bz, px, pz, qx, qz) ||
                    SegmentsIntersectInclusive(bx, bz, cx, cz, px, pz, qx, qz) ||
                    SegmentsIntersectInclusive(cx, cz, ax, az, px, pz, qx, qz))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PointInTriangleXZ(
            int px, int pz,
            int ax, int az,
            int bx, int bz,
            int cx, int cz)
        {
            Int128 o1 = Orientation(ax, az, bx, bz, px, pz);
            Int128 o2 = Orientation(bx, bz, cx, cz, px, pz);
            Int128 o3 = Orientation(cx, cz, ax, az, px, pz);
            bool hasNeg = o1 < 0 || o2 < 0 || o3 < 0;
            bool hasPos = o1 > 0 || o2 > 0 || o3 > 0;
            return !(hasNeg && hasPos);
        }

        private static Int128 SegmentDistanceSq(int ax, int az, int bx, int bz, int px, int pz)
        {
            Int128 abx = (Int128)bx - ax;
            Int128 abz = (Int128)bz - az;
            Int128 apx = (Int128)px - ax;
            Int128 apz = (Int128)pz - az;
            Int128 abLenSq = (abx * abx) + (abz * abz);
            if (abLenSq == 0)
            {
                return (apx * apx) + (apz * apz);
            }

            Int128 dot = (apx * abx) + (apz * abz);
            if (dot <= 0)
            {
                return (apx * apx) + (apz * apz);
            }

            if (dot >= abLenSq)
            {
                Int128 bpx = (Int128)px - bx;
                Int128 bpz = (Int128)pz - bz;
                return (bpx * bpx) + (bpz * bpz);
            }

            // Distance^2 from point to segment via cross product / |AB|.
            // ((APxABz - APzABx)^2) / |AB|^2  — compare without division: cross^2 <= r^2 * |AB|^2 handled by caller.
            Int128 cross = (apx * abz) - (apz * abx);
            return (cross * cross) / abLenSq;
        }

        private static bool PointInPolygonExact(
            int xcm,
            int zcm,
            INavObstacleSource obstacles,
            int obstacleIndex,
            int vertexCount)
        {
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
            int ax, int az, int bx, int bz,
            int cx, int cz, int dx, int dz)
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
