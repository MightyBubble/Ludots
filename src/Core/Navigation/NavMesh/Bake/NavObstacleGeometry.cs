using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Map.Fields;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    public static class NavObstacleGeometry
    {
        public static int ApplyToWalkMask(
            TriWalkMask mask,
            LogicTerrainField terrain,
            int chunkX,
            int chunkY,
            NavObstacleSet obstacles,
            string layerId)
        {
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));
            if (obstacles?.Obstacles == null || obstacles.Obstacles.Count == 0)
            {
                return 0;
            }

            RequireLayerId(layerId);

            int startC = chunkX * terrain.ChunkSizeCells;
            int startR = chunkY * terrain.ChunkSizeCells;
            int blocked = 0;
            for (int localR = 0; localR < mask.TileHeight; localR++)
            {
                for (int localC = 0; localC < mask.TileWidth; localC++)
                {
                    int globalR = startR + localR;
                    bool isOddRow = terrain.Topology == LogicTerrainTopology.Hex && (globalR & 1) == 1;
                    for (int triIndex = 0; triIndex < 2; triIndex++)
                    {
                        int walkableIndex = (localR * mask.TileWidth + localC) * 2 + triIndex;
                        if (!mask.Walkable[walkableIndex])
                        {
                            continue;
                        }

                        WalkMaskBuilder.GetTriangleVertexOffsets(
                            localC,
                            localR,
                            triIndex,
                            isOddRow,
                            out var va,
                            out var vb,
                            out var vc);

                        GetWorldCm(terrain, startC + localC + va.dc, startR + localR + va.dr, out int ax, out int az);
                        GetWorldCm(terrain, startC + localC + vb.dc, startR + localR + vb.dr, out int bx, out int bz);
                        GetWorldCm(terrain, startC + localC + vc.dc, startR + localR + vc.dr, out int cx, out int cz);

                        if (!IsTriangleBlockedByObstacles(ax, az, bx, bz, cx, cz, obstacles, layerId))
                        {
                            continue;
                        }

                        mask.Walkable[walkableIndex] = false;
                        blocked++;
                    }
                }
            }

            return blocked;
        }

        public static bool IsTriangleBlockedByObstacles(
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz,
            NavObstacleSet obstacles,
            string layerId)
        {
            if (obstacles?.Obstacles == null || obstacles.Obstacles.Count == 0)
            {
                return false;
            }

            RequireLayerId(layerId);
            int mx = (ax + bx + cx) / 3;
            int mz = (az + bz + cz) / 3;

            for (int i = 0; i < obstacles.Obstacles.Count; i++)
            {
                NavObstacle obstacle = obstacles.Obstacles[i]
                    ?? throw new InvalidOperationException($"NavObstacleSet.obstacles[{i}] is null.");
                if (!obstacle.Enabled)
                {
                    continue;
                }

                if (!string.Equals(obstacle.LayerId, layerId, StringComparison.Ordinal))
                {
                    continue;
                }

                switch (obstacle.Kind)
                {
                    case NavObstacleKind.Circle:
                        if (TriangleIntersectsCircle(ax, az, bx, bz, cx, cz, obstacle.Center.Xcm, obstacle.Center.Zcm, obstacle.RadiusCm))
                        {
                            return true;
                        }
                        break;
                    case NavObstacleKind.Polygon:
                        if (TriangleIntersectsPolygon(ax, az, bx, bz, cx, cz, mx, mz, obstacle.Points))
                        {
                            return true;
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"Nav obstacle '{obstacle.Id}' kind '{obstacle.Kind}' is not supported by navmesh bake.");
                }
            }

            return false;
        }

        private static bool TriangleIntersectsCircle(
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz,
            int centerX,
            int centerZ,
            int radiusCm)
        {
            if (radiusCm <= 0)
            {
                throw new InvalidOperationException("Circle nav obstacle radiusCm must be > 0.");
            }

            long r2 = (long)radiusCm * radiusCm;
            if (DistanceSq(ax, az, centerX, centerZ) <= r2 ||
                DistanceSq(bx, bz, centerX, centerZ) <= r2 ||
                DistanceSq(cx, cz, centerX, centerZ) <= r2)
            {
                return true;
            }

            if (PointInTriangle(centerX, centerZ, ax, az, bx, bz, cx, cz))
            {
                return true;
            }

            return DistancePointSegmentSq(centerX, centerZ, ax, az, bx, bz) <= r2 ||
                   DistancePointSegmentSq(centerX, centerZ, bx, bz, cx, cz) <= r2 ||
                   DistancePointSegmentSq(centerX, centerZ, cx, cz, ax, az) <= r2;
        }

        private static bool TriangleIntersectsPolygon(
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz,
            int mx,
            int mz,
            IReadOnlyList<NavPointCm> polygon)
        {
            if (polygon == null || polygon.Count < 3)
            {
                throw new InvalidOperationException("Polygon nav obstacle requires at least 3 points.");
            }

            if (PointInPolygon(mx, mz, polygon) ||
                PointInPolygon(ax, az, polygon) ||
                PointInPolygon(bx, bz, polygon) ||
                PointInPolygon(cx, cz, polygon))
            {
                return true;
            }

            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                int px = polygon[i].Xcm;
                int pz = polygon[i].Zcm;
                if (PointInTriangle(px, pz, ax, az, bx, bz, cx, cz))
                {
                    return true;
                }

                int qx = polygon[j].Xcm;
                int qz = polygon[j].Zcm;
                if (SegmentsIntersect(ax, az, bx, bz, qx, qz, px, pz) ||
                    SegmentsIntersect(bx, bz, cx, cz, qx, qz, px, pz) ||
                    SegmentsIntersect(cx, cz, ax, az, qx, qz, px, pz))
                {
                    return true;
                }
            }

            return false;
        }

        private static void GetWorldCm(LogicTerrainField terrain, int col, int row, out int xcm, out int zcm)
        {
            terrain.GetWorldPositionMeters(col, row, out float xm, out float zm);
            xcm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(xm));
            zcm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(zm));
        }

        private static bool PointInPolygon(int xcm, int zcm, IReadOnlyList<NavPointCm> polygon)
        {
            bool inside = false;
            int j = polygon.Count - 1;
            for (int i = 0; i < polygon.Count; j = i++)
            {
                int xi = polygon[i].Xcm;
                int zi = polygon[i].Zcm;
                int xj = polygon[j].Xcm;
                int zj = polygon[j].Zcm;

                if ((zi > zcm) == (zj > zcm))
                {
                    continue;
                }

                double xIntersect = (double)(xj - xi) * (zcm - zi) / (double)(zj - zi) + xi;
                if (xcm < xIntersect)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static bool PointInTriangle(
            int px,
            int pz,
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz)
        {
            long d1 = Sign(px, pz, ax, az, bx, bz);
            long d2 = Sign(px, pz, bx, bz, cx, cz);
            long d3 = Sign(px, pz, cx, cz, ax, az);
            bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
            bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(hasNeg && hasPos);
        }

        private static bool SegmentsIntersect(
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz,
            int dx,
            int dz)
        {
            long o1 = Orientation(ax, az, bx, bz, cx, cz);
            long o2 = Orientation(ax, az, bx, bz, dx, dz);
            long o3 = Orientation(cx, cz, dx, dz, ax, az);
            long o4 = Orientation(cx, cz, dx, dz, bx, bz);

            if (((o1 > 0 && o2 < 0) || (o1 < 0 && o2 > 0)) &&
                ((o3 > 0 && o4 < 0) || (o3 < 0 && o4 > 0)))
            {
                return true;
            }

            return o1 == 0 && OnSegment(ax, az, cx, cz, bx, bz) ||
                   o2 == 0 && OnSegment(ax, az, dx, dz, bx, bz) ||
                   o3 == 0 && OnSegment(cx, cz, ax, az, dx, dz) ||
                   o4 == 0 && OnSegment(cx, cz, bx, bz, dx, dz);
        }

        private static long Orientation(int ax, int az, int bx, int bz, int cx, int cz)
            => (long)(bx - ax) * (cz - az) - (long)(bz - az) * (cx - ax);

        private static long Sign(int px, int pz, int ax, int az, int bx, int bz)
            => (long)(px - bx) * (az - bz) - (long)(ax - bx) * (pz - bz);

        private static bool OnSegment(int ax, int az, int px, int pz, int bx, int bz)
            => px >= Math.Min(ax, bx) &&
               px <= Math.Max(ax, bx) &&
               pz >= Math.Min(az, bz) &&
               pz <= Math.Max(az, bz);

        private static long DistanceSq(int ax, int az, int bx, int bz)
        {
            long dx = (long)ax - bx;
            long dz = (long)az - bz;
            return dx * dx + dz * dz;
        }

        private static double DistancePointSegmentSq(int px, int pz, int ax, int az, int bx, int bz)
        {
            long dx = (long)bx - ax;
            long dz = (long)bz - az;
            long lenSq = dx * dx + dz * dz;
            if (lenSq == 0)
            {
                return DistanceSq(px, pz, ax, az);
            }

            double t = ((double)(px - ax) * dx + (double)(pz - az) * dz) / lenSq;
            t = Math.Clamp(t, 0d, 1d);
            double closestX = ax + t * dx;
            double closestZ = az + t * dz;
            double ddx = px - closestX;
            double ddz = pz - closestZ;
            return ddx * ddx + ddz * ddz;
        }

        private static void RequireLayerId(string layerId)
        {
            if (string.IsNullOrWhiteSpace(layerId) ||
                !string.Equals(layerId.Trim(), layerId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Nav obstacle filtering requires a non-empty trimmed nav layer id.");
            }
        }
    }
}
