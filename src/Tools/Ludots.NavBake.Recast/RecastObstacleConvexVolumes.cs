using System;
using System.Collections.Generic;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Geometry;

namespace Ludots.NavBake.Recast
{
    /// <summary>
    /// Maps authored nav obstacles onto DotRecast convex volumes marked as <see cref="RcRecast.RC_NULL_AREA"/>.
    /// Walkable ground triangles stay in the input mesh; holes come from Recast area marking, not triangle deletion.
    /// DotRecast marks convex volumes after walkable-radius erosion, so footprints must expand by agent radius here
    /// (polygon interior + edge capsules + vertex circles). Vertical range covers the agent occupied interval
    /// [minY - agentHeight, maxY].
    /// </summary>
    internal static class RecastObstacleConvexVolumes
    {
        /// <summary>
        /// Hard capacity for circle tessellation verts. Exceeding this fails fast — never silent clamp.
        /// Sized for cell-derived tessellation of obstacles that do not fully cover the tile+halo query.
        /// Circles that fully contain the query emit an exact AABB volume instead of tessellating.
        /// </summary>
        public const int MaxCircleConvexVolumeSegments = 256;

        /// <summary>
        /// Geometric minimum verts for a closed polygon volume.
        /// </summary>
        public const int MinCircleConvexVolumeSegments = 3;

        private static readonly RcAreaModification NullArea =
            new RcAreaModification(RcRecast.RC_NULL_AREA);

        public static void AddNullAreaVolumes(
            RcSampleInputGeomProvider geom,
            INavObstacleSource obstacles,
            string layerId,
            float cellSizeMeters,
            int agentHeightCm,
            int agentRadiusCm,
            float tileMinX,
            float tileMinZ,
            float tileMaxX,
            float tileMaxZ,
            float borderWorldMeters)
        {
            if (geom == null) throw new ArgumentNullException(nameof(geom));
            if (obstacles == null) throw new ArgumentNullException(nameof(obstacles));
            RequireLayerId(layerId);
            if (!(cellSizeMeters > 0f) || float.IsNaN(cellSizeMeters) || float.IsInfinity(cellSizeMeters))
            {
                throw new InvalidOperationException(
                    "RecastObstacleConvexVolumes.cellSizeMeters must be finite and > 0.");
            }

            if (agentHeightCm <= 0)
            {
                throw new InvalidOperationException(
                    "RecastObstacleConvexVolumes.agentHeightCm must be > 0.");
            }

            if (agentRadiusCm < 0)
            {
                throw new InvalidOperationException(
                    "RecastObstacleConvexVolumes.agentRadiusCm must be >= 0.");
            }

            if (!(borderWorldMeters >= 0f) || float.IsNaN(borderWorldMeters) || float.IsInfinity(borderWorldMeters))
            {
                throw new InvalidOperationException(
                    "RecastObstacleConvexVolumes.borderWorldMeters must be finite and >= 0.");
            }

            float queryMinX = tileMinX - borderWorldMeters;
            float queryMinZ = tileMinZ - borderWorldMeters;
            float queryMaxX = tileMaxX + borderWorldMeters;
            float queryMaxZ = tileMaxZ + borderWorldMeters;

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

                obstacles.GetVerticalRange(i, out int minYcm, out int maxYcm);
                if (minYcm >= maxYcm)
                {
                    throw new InvalidOperationException(
                        $"INavObstacleSource[{i}].minYcm/maxYcm must author half-open [minYcm,maxYcm) with minYcm < maxYcm.");
                }

                // Agent standing on surface Y overlaps obstacle when surfaceY ∈ (minY-agentHeight, maxY).
                long hminCmLong = (long)minYcm - agentHeightCm;
                if (hminCmLong < int.MinValue || hminCmLong > int.MaxValue)
                {
                    throw new InvalidOperationException(
                        $"INavObstacleSource[{i}] agent-occupied vertical range overflows int centimetres.");
                }

                float hmin = hminCmLong / 100f;
                float hmax = maxYcm / 100f;

                switch (obstacles.GetKind(i))
                {
                    case NavObstacleKind.Circle:
                        AddCircleVolumes(
                            geom,
                            obstacles,
                            i,
                            cellSizeMeters,
                            agentRadiusCm,
                            hmin,
                            hmax,
                            queryMinX,
                            queryMinZ,
                            queryMaxX,
                            queryMaxZ);
                        break;
                    case NavObstacleKind.Polygon:
                        AddPolygonVolumes(
                            geom,
                            obstacles,
                            i,
                            cellSizeMeters,
                            agentRadiusCm,
                            hmin,
                            hmax,
                            queryMinX,
                            queryMinZ,
                            queryMaxX,
                            queryMaxZ);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"INavObstacleSource[{i}].kind '{obstacles.GetKind(i)}' is not supported by RecastObstacleConvexVolumes.");
                }
            }
        }

        private static void AddCircleVolumes(
            RcSampleInputGeomProvider geom,
            INavObstacleSource obstacles,
            int index,
            float cellSizeMeters,
            int agentRadiusCm,
            float hmin,
            float hmax,
            float queryMinX,
            float queryMinZ,
            float queryMaxX,
            float queryMaxZ)
        {
            obstacles.GetCircle(index, out int centerXcm, out int centerZcm, out int radiusCm);
            if (radiusCm <= 0)
            {
                throw new InvalidOperationException(
                    $"INavObstacleSource[{index}] circle radiusCm must be > 0.");
            }

            int inflatedRadiusCm = checked(radiusCm + agentRadiusCm);
            float cx = centerXcm / 100f;
            float cz = centerZcm / 100f;
            float radiusM = inflatedRadiusCm / 100f;
            if (!AabbOverlapsCircle(queryMinX, queryMinZ, queryMaxX, queryMaxZ, cx, cz, radiusM))
            {
                return;
            }

            AddCircleConvexVolume(
                geom,
                index,
                cx,
                cz,
                radiusM,
                cellSizeMeters,
                hmin,
                hmax,
                queryMinX,
                queryMinZ,
                queryMaxX,
                queryMaxZ);
        }

        private static void AddPolygonVolumes(
            RcSampleInputGeomProvider geom,
            INavObstacleSource obstacles,
            int index,
            float cellSizeMeters,
            int agentRadiusCm,
            float hmin,
            float hmax,
            float queryMinX,
            float queryMinZ,
            float queryMaxX,
            float queryMaxZ)
        {
            int vertexCount = obstacles.GetPolygonVertexCount(index);
            if (vertexCount < 3)
            {
                throw new InvalidOperationException(
                    $"INavObstacleSource[{index}] polygon requires at least 3 points.");
            }

            var xs = new int[vertexCount];
            var zs = new int[vertexCount];
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minZ = int.MaxValue;
            int maxZ = int.MinValue;
            for (int v = 0; v < vertexCount; v++)
            {
                obstacles.GetPolygonVertex(index, v, out int xcm, out int zcm);
                xs[v] = xcm;
                zs[v] = zcm;
                if (xcm < minX) minX = xcm;
                if (xcm > maxX) maxX = xcm;
                if (zcm < minZ) minZ = zcm;
                if (zcm > maxZ) maxZ = zcm;
            }

            int expandedMinXcm = checked(minX - agentRadiusCm);
            int expandedMaxXcm = checked(maxX + agentRadiusCm);
            int expandedMinZcm = checked(minZ - agentRadiusCm);
            int expandedMaxZcm = checked(maxZ + agentRadiusCm);
            if (!AabbOverlapsAabb(
                    queryMinX,
                    queryMinZ,
                    queryMaxX,
                    queryMaxZ,
                    expandedMinXcm / 100f,
                    expandedMinZcm / 100f,
                    expandedMaxXcm / 100f,
                    expandedMaxZcm / 100f))
            {
                return;
            }

            EmitPolygonInteriorVolumes(geom, index, xs, zs, hmin, hmax);

            if (agentRadiusCm == 0)
            {
                return;
            }

            EmitPolygonRadiusExpansionVolumes(
                geom,
                index,
                xs,
                zs,
                agentRadiusCm,
                cellSizeMeters,
                hmin,
                hmax,
                queryMinX,
                queryMinZ,
                queryMaxX,
                queryMaxZ);
        }

        private static void EmitPolygonRadiusExpansionVolumes(
            RcSampleInputGeomProvider geom,
            int index,
            int[] xs,
            int[] zs,
            int agentRadiusCm,
            float cellSizeMeters,
            float hmin,
            float hmax,
            float queryMinX,
            float queryMinZ,
            float queryMaxX,
            float queryMaxZ)
        {
            float expandM = agentRadiusCm / 100f;
            int vertexCount = xs.Length;

            for (int v = 0; v < vertexCount; v++)
            {
                float vx = xs[v] / 100f;
                float vz = zs[v] / 100f;
                if (!AabbOverlapsCircle(queryMinX, queryMinZ, queryMaxX, queryMaxZ, vx, vz, expandM))
                {
                    continue;
                }

                AddCircleConvexVolume(
                    geom,
                    index,
                    vx,
                    vz,
                    expandM,
                    cellSizeMeters,
                    hmin,
                    hmax,
                    queryMinX,
                    queryMinZ,
                    queryMaxX,
                    queryMaxZ);
            }

            for (int i = 0, j = vertexCount - 1; i < vertexCount; j = i++)
            {
                AddEdgeCapsuleVolume(
                    geom,
                    index,
                    xs[j],
                    zs[j],
                    xs[i],
                    zs[i],
                    agentRadiusCm,
                    hmin,
                    hmax,
                    queryMinX,
                    queryMinZ,
                    queryMaxX,
                    queryMaxZ);
            }
        }

        private static void EmitPolygonInteriorVolumes(
            RcSampleInputGeomProvider geom,
            int index,
            int[] xs,
            int[] zs,
            float hmin,
            float hmax)
        {
            if (IsConvexPolygon(xs, zs))
            {
                geom.AddConvexVolume(BuildVolumeVerts(xs, zs, hmin), hmin, hmax, NullArea);
                return;
            }

            var triA = new List<int>(xs.Length - 2);
            var triB = new List<int>(xs.Length - 2);
            var triC = new List<int>(xs.Length - 2);
            int maxFlips = checked(xs.Length * xs.Length * 4);
            if (maxFlips < 16)
            {
                maxFlips = 16;
            }

            ExactConstrainedTriangulation.TriangulatePolygon(
                xs,
                zs,
                ReadOnlySpan<int>.Empty,
                ReadOnlySpan<int>.Empty,
                maxFlips,
                triA,
                triB,
                triC);

            int emitted = 0;
            for (int t = 0; t < triA.Count; t++)
            {
                int ia = triA[t];
                int ib = triB[t];
                int ic = triC[t];
                if (ExactPredicates2D.Orient2Sign(xs[ia], zs[ia], xs[ib], zs[ib], xs[ic], zs[ic]) == 0)
                {
                    continue;
                }

                float[] verts =
                {
                    xs[ia] / 100f, hmin, zs[ia] / 100f,
                    xs[ib] / 100f, hmin, zs[ib] / 100f,
                    xs[ic] / 100f, hmin, zs[ic] / 100f
                };
                geom.AddConvexVolume(verts, hmin, hmax, NullArea);
                emitted++;
            }

            if (emitted == 0)
            {
                throw new InvalidOperationException(
                    $"INavObstacleSource[{index}] concave polygon triangulation produced no non-degenerate " +
                    "convex volumes. Unsupported or degenerate authored polygon.");
            }
        }

        private static void AddEdgeCapsuleVolume(
            RcSampleInputGeomProvider geom,
            int obstacleIndex,
            int axCm,
            int azCm,
            int bxCm,
            int bzCm,
            int agentRadiusCm,
            float hmin,
            float hmax,
            float queryMinX,
            float queryMinZ,
            float queryMaxX,
            float queryMaxZ)
        {
            long dx = (long)bxCm - axCm;
            long dz = (long)bzCm - azCm;
            double len = Math.Sqrt((dx * dx) + (dz * dz));
            if (!(len > 0d))
            {
                throw new InvalidOperationException(
                    $"INavObstacleSource[{obstacleIndex}] polygon has a zero-length edge; " +
                    "edge capsules require positive-length segments.");
            }

            int minX = axCm < bxCm ? axCm : bxCm;
            int maxX = axCm > bxCm ? axCm : bxCm;
            int minZ = azCm < bzCm ? azCm : bzCm;
            int maxZ = azCm > bzCm ? azCm : bzCm;
            int expandedMinX = checked(minX - agentRadiusCm);
            int expandedMaxX = checked(maxX + agentRadiusCm);
            int expandedMinZ = checked(minZ - agentRadiusCm);
            int expandedMaxZ = checked(maxZ + agentRadiusCm);
            if (!AabbOverlapsAabb(
                    queryMinX,
                    queryMinZ,
                    queryMaxX,
                    queryMaxZ,
                    expandedMinX / 100f,
                    expandedMinZ / 100f,
                    expandedMaxX / 100f,
                    expandedMaxZ / 100f))
            {
                return;
            }

            double invLen = 1.0 / len;
            // Leftward unit normal of AB on XZ, scaled to agent radius (metres).
            float nx = (float)((-dz * invLen) * (agentRadiusCm / 100.0));
            float nz = (float)((dx * invLen) * (agentRadiusCm / 100.0));
            float ax = axCm / 100f;
            float az = azCm / 100f;
            float bx = bxCm / 100f;
            float bz = bzCm / 100f;

            // CCW rectangle: A+n, B+n, B-n, A-n.
            float[] verts =
            {
                ax + nx, hmin, az + nz,
                bx + nx, hmin, bz + nz,
                bx - nx, hmin, bz - nz,
                ax - nx, hmin, az - nz
            };
            geom.AddConvexVolume(verts, hmin, hmax, NullArea);
        }

        private static void AddCircleConvexVolume(
            RcSampleInputGeomProvider geom,
            int obstacleIndex,
            float cx,
            float cz,
            float radiusM,
            float cellSizeMeters,
            float hmin,
            float hmax,
            float queryMinX,
            float queryMinZ,
            float queryMaxX,
            float queryMaxZ)
        {
            if (!(radiusM > 0f) || float.IsNaN(radiusM) || float.IsInfinity(radiusM))
            {
                throw new InvalidOperationException(
                    $"INavObstacleSource[{obstacleIndex}] circle convex volume radius must be finite and > 0.");
            }

            // Exact specialization: when the circle swallows the whole tile+halo query, a convex box
            // volume is geometrically equivalent and avoids unbounded tessellation cost.
            if (CircleContainsAabb(cx, cz, radiusM, queryMinX, queryMinZ, queryMaxX, queryMaxZ))
            {
                float[] box =
                {
                    queryMinX, hmin, queryMinZ,
                    queryMaxX, hmin, queryMinZ,
                    queryMaxX, hmin, queryMaxZ,
                    queryMinX, hmin, queryMaxZ
                };
                geom.AddConvexVolume(box, hmin, hmax, NullArea);
                return;
            }

            int segments = ComputeCircleSegmentCount(radiusM, cellSizeMeters, obstacleIndex);
            float[] verts = new float[segments * 3];
            for (int s = 0; s < segments; s++)
            {
                // Deterministic CCW tessellation from +X; angle derived only from segment index.
                double angle = (Math.PI * 2.0 * s) / segments;
                float x = cx + (float)(Math.Cos(angle) * radiusM);
                float z = cz + (float)(Math.Sin(angle) * radiusM);
                int o = s * 3;
                verts[o] = x;
                verts[o + 1] = hmin;
                verts[o + 2] = z;
            }

            geom.AddConvexVolume(verts, hmin, hmax, NullArea);
        }

        private static int ComputeCircleSegmentCount(float radiusMeters, float cellSizeMeters, int obstacleIndex)
        {
            // Chord length targets Recast cell size so the rasterized hole follows configured resolution.
            double circumference = Math.PI * 2.0 * radiusMeters;
            int segments = (int)Math.Ceiling(circumference / cellSizeMeters);
            if (segments < MinCircleConvexVolumeSegments)
            {
                segments = MinCircleConvexVolumeSegments;
            }

            if (segments > MaxCircleConvexVolumeSegments)
            {
                throw new InvalidOperationException(
                    $"INavObstacleSource[{obstacleIndex}] circle tessellation requires {segments} segments " +
                    $"(circumference={circumference:0.###}m, cellSize={cellSizeMeters:0.###}m) which exceeds " +
                    $"MaxCircleConvexVolumeSegments={MaxCircleConvexVolumeSegments}. " +
                    "Increase Recast cell size or reduce circle radius; silent clamping is forbidden.");
            }

            return segments;
        }

        private static float[] BuildVolumeVerts(int[] xs, int[] zs, float y)
        {
            float[] verts = new float[xs.Length * 3];
            for (int i = 0; i < xs.Length; i++)
            {
                int o = i * 3;
                verts[o] = xs[i] / 100f;
                verts[o + 1] = y;
                verts[o + 2] = zs[i] / 100f;
            }

            return verts;
        }

        private static bool IsConvexPolygon(ReadOnlySpan<int> xs, ReadOnlySpan<int> zs)
        {
            int n = xs.Length;
            int sign = 0;
            for (int i = 0; i < n; i++)
            {
                int a = i;
                int b = i + 1 == n ? 0 : i + 1;
                int c = i + 2 >= n ? i + 2 - n : i + 2;
                int o = ExactPredicates2D.Orient2Sign(xs[a], zs[a], xs[b], zs[b], xs[c], zs[c]);
                if (o == 0)
                {
                    continue;
                }

                if (sign == 0)
                {
                    sign = o;
                }
                else if (o != sign)
                {
                    return false;
                }
            }

            return sign != 0;
        }

        private static bool AabbOverlapsCircle(
            float minX,
            float minZ,
            float maxX,
            float maxZ,
            float cx,
            float cz,
            float radius)
        {
            float closestX = cx < minX ? minX : (cx > maxX ? maxX : cx);
            float closestZ = cz < minZ ? minZ : (cz > maxZ ? maxZ : cz);
            float dx = cx - closestX;
            float dz = cz - closestZ;
            return (dx * dx) + (dz * dz) <= radius * radius;
        }

        private static bool CircleContainsAabb(
            float cx,
            float cz,
            float radius,
            float minX,
            float minZ,
            float maxX,
            float maxZ)
        {
            float r2 = radius * radius;
            return PointInCircle(minX, minZ, cx, cz, r2) &&
                   PointInCircle(maxX, minZ, cx, cz, r2) &&
                   PointInCircle(maxX, maxZ, cx, cz, r2) &&
                   PointInCircle(minX, maxZ, cx, cz, r2);
        }

        private static bool PointInCircle(float x, float z, float cx, float cz, float radiusSq)
        {
            float dx = x - cx;
            float dz = z - cz;
            return (dx * dx) + (dz * dz) <= radiusSq;
        }

        private static bool AabbOverlapsAabb(
            float aMinX,
            float aMinZ,
            float aMaxX,
            float aMaxZ,
            float bMinX,
            float bMinZ,
            float bMaxX,
            float bMaxZ)
            => aMinX <= bMaxX && aMaxX >= bMinX && aMinZ <= bMaxZ && aMaxZ >= bMinZ;

        private static void RequireLayerId(string layerId)
        {
            if (string.IsNullOrWhiteSpace(layerId) ||
                !string.Equals(layerId.Trim(), layerId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "RecastObstacleConvexVolumes requires a non-empty trimmed nav layer id.");
            }
        }
    }
}
