using System;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;

namespace Ludots.Core.Navigation.NavMesh.Geometry
{
    /// <summary>
    /// Exact integer walkability predicates shared by direct triangle-surface bakers.
    /// </summary>
    public static class TriangleSurfaceWalkability
    {
        private static readonly Int128 MaxNormalComponentBeforeSquare = (Int128)1 << 40;

        public static bool TryComputeFaceNormal(
            int ax,
            int ay,
            int az,
            int bx,
            int by,
            int bz,
            int cx,
            int cy,
            int cz,
            out Int128 nx,
            out Int128 ny,
            out Int128 nz,
            out bool degenerate)
        {
            Int128 abx = (Int128)bx - ax;
            Int128 aby = (Int128)by - ay;
            Int128 abz = (Int128)bz - az;
            Int128 acx = (Int128)cx - ax;
            Int128 acy = (Int128)cy - ay;
            Int128 acz = (Int128)cz - az;
            nx = (aby * acz) - (abz * acy);
            ny = (abz * acx) - (abx * acz);
            nz = (abx * acy) - (aby * acx);
            if (nx == 0 && ny == 0 && nz == 0)
            {
                degenerate = true;
                return false;
            }

            degenerate = false;
            return true;
        }

        public static bool TryAcceptSlope(
            Int128 normalX,
            Int128 normalY,
            Int128 normalZ,
            int minWalkableUpDotQ1M,
            out bool degenerate)
        {
            Int128 thresholdSq = (Int128)minWalkableUpDotQ1M * minWalkableUpDotQ1M;
            Int128 q1MSq = (Int128)LayeredSpanWalkabilitySpec.UpDotQ1M * LayeredSpanWalkabilitySpec.UpDotQ1M;

            if (normalX == 0 && normalY == 0 && normalZ == 0)
            {
                degenerate = true;
                return false;
            }

            ScaleNormalsForSquare(ref normalX, ref normalY, ref normalZ);
            if (normalX == 0 && normalY == 0 && normalZ == 0)
            {
                degenerate = true;
                return false;
            }

            Int128 absNy = normalY < 0 ? -normalY : normalY;
            Int128 lenSq = (normalX * normalX) + (normalY * normalY) + (normalZ * normalZ);
            bool accepted = (absNy * absNy * q1MSq) >= (thresholdSq * lenSq);
            degenerate = false;
            return accepted;
        }

        /// <summary>
        /// Conservative vertical clearance from walkable top to the next solid surface above in overlapping XZ.
        /// Candidates must be a spatially local triangle index set (tile CSR / overlapping tiles).
        /// Scanning the full world surface is forbidden — that would make open-world local bakes O(world).
        /// </summary>
        public static int ComputeVerticalClearanceCm(
            int walkTopYcm,
            ReadOnlySpan<int> vertexXcm,
            ReadOnlySpan<int> vertexYcm,
            ReadOnlySpan<int> vertexZcm,
            ReadOnlySpan<int> triA,
            ReadOnlySpan<int> triB,
            ReadOnlySpan<int> triC,
            ReadOnlySpan<NavTriangleSurfaceFlags> triFlags,
            int walkTriIndex,
            int walkMinXcm,
            int walkMaxXcm,
            int walkMinZcm,
            int walkMaxZcm,
            ReadOnlySpan<int> clearanceCandidateTriIndices)
        {
            if (clearanceCandidateTriIndices.Length <= 0)
            {
                throw new ArgumentException(
                    "Vertical clearance requires a non-empty local candidate triangle span; " +
                    "full-surface scans are forbidden for open-world locality.",
                    nameof(clearanceCandidateTriIndices));
            }

            int clearance = int.MaxValue;
            for (int c = 0; c < clearanceCandidateTriIndices.Length; c++)
            {
                int i = clearanceCandidateTriIndices[c];
                if ((uint)i >= (uint)triA.Length)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(clearanceCandidateTriIndices),
                        $"Clearance candidate triangle index {i} is outside surface triangle count {triA.Length}.");
                }

                if (i == walkTriIndex)
                {
                    continue;
                }

                if ((triFlags[i] & NavTriangleSurfaceFlags.Solid) == 0)
                {
                    continue;
                }

                int ia = triA[i];
                int ib = triB[i];
                int ic = triC[i];
                int sMinX = Min3(vertexXcm[ia], vertexXcm[ib], vertexXcm[ic]);
                int sMaxX = Max3(vertexXcm[ia], vertexXcm[ib], vertexXcm[ic]);
                int sMinZ = Min3(vertexZcm[ia], vertexZcm[ib], vertexZcm[ic]);
                int sMaxZ = Max3(vertexZcm[ia], vertexZcm[ib], vertexZcm[ic]);
                if (sMaxX < walkMinXcm || sMinX > walkMaxXcm || sMaxZ < walkMinZcm || sMinZ > walkMaxZcm)
                {
                    continue;
                }

                int solidMinY = Min3(vertexYcm[ia], vertexYcm[ib], vertexYcm[ic]);
                if (solidMinY <= walkTopYcm)
                {
                    continue;
                }

                long gap = (long)solidMinY - walkTopYcm;
                if (gap < clearance)
                {
                    if (gap >= int.MaxValue)
                    {
                        return int.MaxValue;
                    }

                    clearance = (int)gap;
                }
            }

            return clearance;
        }

        public static bool IsWalkableTriangleIgnoringObstacles(
            int triIndex,
            ReadOnlySpan<int> vertexXcm,
            ReadOnlySpan<int> vertexYcm,
            ReadOnlySpan<int> vertexZcm,
            ReadOnlySpan<int> triA,
            ReadOnlySpan<int> triB,
            ReadOnlySpan<int> triC,
            ReadOnlySpan<NavTriangleSurfaceFlags> triFlags,
            int minWalkableUpDotQ1M,
            int agentHeightCm,
            ReadOnlySpan<int> clearanceCandidateTriIndices)
        {
            return IsWalkableTriangleCore(
                triIndex,
                vertexXcm,
                vertexYcm,
                vertexZcm,
                triA,
                triB,
                triC,
                triFlags,
                minWalkableUpDotQ1M,
                agentHeightCm,
                obstacles: null,
                layerId: null,
                agentRadiusCm: 0,
                clearanceCandidateTriIndices,
                applyObstacles: false);
        }

        public static bool IsWalkableTriangle(
            int triIndex,
            ReadOnlySpan<int> vertexXcm,
            ReadOnlySpan<int> vertexYcm,
            ReadOnlySpan<int> vertexZcm,
            ReadOnlySpan<int> triA,
            ReadOnlySpan<int> triB,
            ReadOnlySpan<int> triC,
            ReadOnlySpan<NavTriangleSurfaceFlags> triFlags,
            int minWalkableUpDotQ1M,
            int agentHeightCm,
            INavObstacleSource obstacles,
            string layerId,
            int agentRadiusCm,
            ReadOnlySpan<int> clearanceCandidateTriIndices)
        {
            return IsWalkableTriangleCore(
                triIndex,
                vertexXcm,
                vertexYcm,
                vertexZcm,
                triA,
                triB,
                triC,
                triFlags,
                minWalkableUpDotQ1M,
                agentHeightCm,
                obstacles,
                layerId,
                agentRadiusCm,
                clearanceCandidateTriIndices,
                applyObstacles: true);
        }

        private static bool IsWalkableTriangleCore(
            int triIndex,
            ReadOnlySpan<int> vertexXcm,
            ReadOnlySpan<int> vertexYcm,
            ReadOnlySpan<int> vertexZcm,
            ReadOnlySpan<int> triA,
            ReadOnlySpan<int> triB,
            ReadOnlySpan<int> triC,
            ReadOnlySpan<NavTriangleSurfaceFlags> triFlags,
            int minWalkableUpDotQ1M,
            int agentHeightCm,
            INavObstacleSource? obstacles,
            string? layerId,
            int agentRadiusCm,
            ReadOnlySpan<int> clearanceCandidateTriIndices,
            bool applyObstacles)
        {
            if ((triFlags[triIndex] & NavTriangleSurfaceFlags.WalkCandidate) == 0)
            {
                return false;
            }

            int ia = triA[triIndex];
            int ib = triB[triIndex];
            int ic = triC[triIndex];
            int ax = vertexXcm[ia];
            int ay = vertexYcm[ia];
            int az = vertexZcm[ia];
            int bx = vertexXcm[ib];
            int by = vertexYcm[ib];
            int bz = vertexZcm[ib];
            int cx = vertexXcm[ic];
            int cy = vertexYcm[ic];
            int cz = vertexZcm[ic];

            if (!TryComputeFaceNormal(ax, ay, az, bx, by, bz, cx, cy, cz, out Int128 nx, out Int128 ny, out Int128 nz, out bool degenerate))
            {
                return !degenerate && false;
            }

            if (!TryAcceptSlope(nx, ny, nz, minWalkableUpDotQ1M, out degenerate))
            {
                return false;
            }

            int topY = Max3(ay, by, cy);
            int minX = Min3(ax, bx, cx);
            int maxX = Max3(ax, bx, cx);
            int minZ = Min3(az, bz, cz);
            int maxZ = Max3(az, bz, cz);
            int clearance = ComputeVerticalClearanceCm(
                topY,
                vertexXcm,
                vertexYcm,
                vertexZcm,
                triA,
                triB,
                triC,
                triFlags,
                triIndex,
                minX,
                maxX,
                minZ,
                maxZ,
                clearanceCandidateTriIndices);
            if (clearance < agentHeightCm)
            {
                return false;
            }

            if (!applyObstacles)
            {
                return true;
            }

            return !NavTriangleObstaclePredicate.IsTriangleBlocked(
                ax, ay, az, bx, by, bz, cx, cy, cz,
                obstacles!,
                layerId!,
                agentHeightCm,
                agentRadiusCm);
        }

        private static int Min3(int a, int b, int c)
        {
            int m = a;
            if (b < m) m = b;
            if (c < m) m = c;
            return m;
        }

        private static int Max3(int a, int b, int c)
        {
            int m = a;
            if (b > m) m = b;
            if (c > m) m = c;
            return m;
        }

        private static void ScaleNormalsForSquare(ref Int128 nx, ref Int128 ny, ref Int128 nz)
        {
            Int128 ax = Abs(nx);
            Int128 ay = Abs(ny);
            Int128 az = Abs(nz);
            Int128 maxMag = ax;
            if (ay > maxMag) maxMag = ay;
            if (az > maxMag) maxMag = az;

            int shift = 0;
            while (maxMag > MaxNormalComponentBeforeSquare)
            {
                maxMag >>= 1;
                shift++;
            }

            if (shift == 0)
            {
                return;
            }

            ax >>= shift;
            ay >>= shift;
            az >>= shift;
            nx = nx < 0 ? -ax : ax;
            ny = ny < 0 ? -ay : ay;
            nz = nz < 0 ? -az : az;
        }

        private static Int128 Abs(Int128 value) => value < 0 ? -value : value;
    }
}
