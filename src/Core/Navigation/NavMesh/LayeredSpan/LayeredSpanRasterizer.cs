using System;
using Ludots.Core.Navigation.NavMesh.Surface;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Stateless count → prefix → fill → per-column sort rasterizer.
    /// Success-path rerasterization after scratch warmup allocates 0 managed bytes.
    /// </summary>
    public static class LayeredSpanRasterizer
    {
        public static void Rasterize(
            NavTriangleSurfaceSnapshot surface,
            ReadOnlySpan<int> triangleIndices,
            in LayeredSpanRasterGridSpec spec,
            LayeredSpanScratch scratch)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (scratch == null) throw new ArgumentNullException(nameof(scratch));

            scratch.ResetForRaster();

            int requiredColumns = spec.ColumnCount;
            if (requiredColumns > scratch.ColumnCapacity)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanScratch.columnCapacity ({scratch.ColumnCapacity}); required {requiredColumns}.");
            }

            scratch.PrepareColumns(requiredColumns);
            Span<int> counts = scratch.MutableColumnSpanCounts;

            ReadOnlySpan<int> vertexXcm = surface.VertexXcm;
            ReadOnlySpan<int> vertexYcm = surface.VertexYcm;
            ReadOnlySpan<int> vertexZcm = surface.VertexZcm;
            ReadOnlySpan<int> triA = surface.TriA;
            ReadOnlySpan<int> triB = surface.TriB;
            ReadOnlySpan<int> triC = surface.TriC;
            ReadOnlySpan<int> stableIds = surface.TriStableIds;
            ReadOnlySpan<byte> areaIds = surface.TriAreaIds;
            ReadOnlySpan<NavTriangleSurfaceFlags> triFlags = surface.TriFlags;
            int triangleCount = surface.TriangleCount;
            int originX = spec.OriginXcm;
            int originZ = spec.OriginZcm;
            int cell = spec.CellSizeCm;
            int colCountX = spec.ColumnCountX;
            int colCountZ = spec.ColumnCountZ;

            // Pass 1: count conservative column coverage.
            for (int ti = 0; ti < triangleIndices.Length; ti++)
            {
                int tri = triangleIndices[ti];
                if ((uint)tri >= (uint)triangleCount)
                {
                    scratch.ResetForRaster();
                    throw new ArgumentOutOfRangeException(
                        nameof(triangleIndices),
                        tri,
                        $"Triangle index {tri} is outside surface triangle count {triangleCount}.");
                }

                int ia = triA[tri];
                int ib = triB[tri];
                int ic = triC[tri];
                int ax = vertexXcm[ia];
                int ay = vertexYcm[ia];
                int az = vertexZcm[ia];
                int bx = vertexXcm[ib];
                int by = vertexYcm[ib];
                int bz = vertexZcm[ib];
                int cx = vertexXcm[ic];
                int cy = vertexYcm[ic];
                int cz = vertexZcm[ic];

                if (!TryGetOverlappingColumnRange(
                        ax, az, bx, bz, cx, cz,
                        originX, originZ, cell, colCountX, colCountZ,
                        out int minColX, out int maxColX, out int minColZ, out int maxColZ))
                {
                    continue;
                }

                Int128 xzArea = OrientXZ(ax, az, bx, bz, cx, cz);
                bool requirePositiveXZArea =
                    (triFlags[tri] & NavTriangleSurfaceFlags.WalkCandidate) != 0;
                for (int czCol = minColZ; czCol <= maxColZ; czCol++)
                {
                    int colMinZ = originZ + (czCol * cell);
                    int colMaxZ = colMinZ + cell;
                    int row = czCol * colCountX;
                    for (int cxCol = minColX; cxCol <= maxColX; cxCol++)
                    {
                        int colMinX = originX + (cxCol * cell);
                        int colMaxX = colMinX + cell;
                        if (!TryAcceptColumnSpan(
                                ax, ay, az, bx, by, bz, cx, cy, cz,
                                xzArea,
                                requirePositiveXZArea,
                                colMinX, colMaxX, colMinZ, colMaxZ,
                                out _, out _))
                        {
                            continue;
                        }

                        counts[row + cxCol]++;
                    }
                }
            }

            long totalSpansLong = 0;
            for (int i = 0; i < requiredColumns; i++)
            {
                totalSpansLong += counts[i];
            }

            if (totalSpansLong > scratch.SpanCapacity)
            {
                scratch.ResetForRaster();
                throw new InvalidOperationException(
                    $"LayeredSpanScratch.spanCapacity ({scratch.SpanCapacity}); required {totalSpansLong}.");
            }

            // Pass 2: prefix offsets + fill cursors.
            Span<int> offsets = scratch.MutableColumnSpanOffsets;
            Span<int> cursors = scratch.MutableFillCursor;
            int sum = 0;
            for (int i = 0; i < requiredColumns; i++)
            {
                offsets[i] = sum;
                cursors[i] = sum;
                sum += counts[i];
            }

            offsets[requiredColumns] = sum;

            // Pass 3: fill spans.
            for (int ti = 0; ti < triangleIndices.Length; ti++)
            {
                int tri = triangleIndices[ti];
                int ia = triA[tri];
                int ib = triB[tri];
                int ic = triC[tri];
                int ax = vertexXcm[ia];
                int ay = vertexYcm[ia];
                int az = vertexZcm[ia];
                int bx = vertexXcm[ib];
                int by = vertexYcm[ib];
                int bz = vertexZcm[ib];
                int cx = vertexXcm[ic];
                int cy = vertexYcm[ic];
                int cz = vertexZcm[ic];

                ComputeExactNormal(
                    ax, ay, az, bx, by, bz, cx, cy, cz,
                    out Int128 nx, out Int128 ny, out Int128 nz);
                Int128 xzArea = OrientXZ(ax, az, bx, bz, cx, cz);
                int stableId = stableIds[tri];
                byte areaId = areaIds[tri];
                NavTriangleSurfaceFlags surfaceFlags = triFlags[tri];

                if (!TryGetOverlappingColumnRange(
                        ax, az, bx, bz, cx, cz,
                        originX, originZ, cell, colCountX, colCountZ,
                        out int minColX, out int maxColX, out int minColZ, out int maxColZ))
                {
                    continue;
                }

                bool requirePositiveXZArea =
                    (surfaceFlags & NavTriangleSurfaceFlags.WalkCandidate) != 0;
                for (int czCol = minColZ; czCol <= maxColZ; czCol++)
                {
                    int colMinZ = originZ + (czCol * cell);
                    int colMaxZ = colMinZ + cell;
                    int row = czCol * colCountX;
                    for (int cxCol = minColX; cxCol <= maxColX; cxCol++)
                    {
                        int colMinX = originX + (cxCol * cell);
                        int colMaxX = colMinX + cell;
                        if (!TryAcceptColumnSpan(
                                ax, ay, az, bx, by, bz, cx, cy, cz,
                                xzArea,
                                requirePositiveXZArea,
                                colMinX, colMaxX, colMinZ, colMaxZ,
                                out int minY, out int maxY))
                        {
                            continue;
                        }

                        LayeredSpanBoundaryMask mask = LayeredSpanBoundaryMask.None;
                        int westMin = 0;
                        int westMax = 0;
                        int westMinZ = 0;
                        int westMaxZ = 0;
                        int eastMin = 0;
                        int eastMax = 0;
                        int eastMinZ = 0;
                        int eastMaxZ = 0;
                        int northMin = 0;
                        int northMax = 0;
                        int northMinX = 0;
                        int northMaxX = 0;
                        int southMin = 0;
                        int southMax = 0;
                        int southMinX = 0;
                        int southMaxX = 0;

                        // Exact triangle ∩ closed boundary segment; never copy whole-cell extents.
                        // West/East store along-boundary Z; North/South store along-boundary X.
                        if (TryIntersectBoundaryCoverage(
                                ax, ay, az, bx, by, bz, cx, cy, cz,
                                xzArea,
                                alongIsX: false,
                                fixedCoord: colMinX,
                                alongLo: colMinZ,
                                alongHi: colMaxZ,
                                out westMin, out westMax, out westMinZ, out westMaxZ))
                        {
                            mask |= LayeredSpanBoundaryMask.West;
                        }

                        if (TryIntersectBoundaryCoverage(
                                ax, ay, az, bx, by, bz, cx, cy, cz,
                                xzArea,
                                alongIsX: false,
                                fixedCoord: colMaxX,
                                alongLo: colMinZ,
                                alongHi: colMaxZ,
                                out eastMin, out eastMax, out eastMinZ, out eastMaxZ))
                        {
                            mask |= LayeredSpanBoundaryMask.East;
                        }

                        if (TryIntersectBoundaryCoverage(
                                ax, ay, az, bx, by, bz, cx, cy, cz,
                                xzArea,
                                alongIsX: true,
                                fixedCoord: colMinZ,
                                alongLo: colMinX,
                                alongHi: colMaxX,
                                out northMin, out northMax, out northMinX, out northMaxX))
                        {
                            mask |= LayeredSpanBoundaryMask.North;
                        }

                        if (TryIntersectBoundaryCoverage(
                                ax, ay, az, bx, by, bz, cx, cy, cz,
                                xzArea,
                                alongIsX: true,
                                fixedCoord: colMaxZ,
                                alongLo: colMinX,
                                alongHi: colMaxX,
                                out southMin, out southMax, out southMinX, out southMaxX))
                        {
                            mask |= LayeredSpanBoundaryMask.South;
                        }

                        if ((mask & LayeredSpanBoundaryMask.West) == 0)
                        {
                            westMin = 0;
                            westMax = 0;
                            westMinZ = 0;
                            westMaxZ = 0;
                        }

                        if ((mask & LayeredSpanBoundaryMask.East) == 0)
                        {
                            eastMin = 0;
                            eastMax = 0;
                            eastMinZ = 0;
                            eastMaxZ = 0;
                        }

                        if ((mask & LayeredSpanBoundaryMask.North) == 0)
                        {
                            northMin = 0;
                            northMax = 0;
                            northMinX = 0;
                            northMaxX = 0;
                        }

                        if ((mask & LayeredSpanBoundaryMask.South) == 0)
                        {
                            southMin = 0;
                            southMax = 0;
                            southMinX = 0;
                            southMaxX = 0;
                        }

                        int col = row + cxCol;
                        int spanIndex = cursors[col]++;
                        scratch.WriteSpan(
                            spanIndex,
                            minY,
                            maxY,
                            tri,
                            stableId,
                            areaId,
                            surfaceFlags,
                            nx,
                            ny,
                            nz,
                            mask,
                            westMin,
                            westMax,
                            westMinZ,
                            westMaxZ,
                            eastMin,
                            eastMax,
                            eastMinZ,
                            eastMaxZ,
                            northMin,
                            northMax,
                            northMinX,
                            northMaxX,
                            southMin,
                            southMax,
                            southMinX,
                            southMaxX);
                    }
                }
            }

            // Pass 4: deterministic per-column sort.
            for (int i = 0; i < requiredColumns; i++)
            {
                scratch.SortColumnSpans(offsets[i], counts[i]);
            }

            scratch.CommitSpanCount(sum);
        }

        private static bool TryGetOverlappingColumnRange(
            int ax, int az, int bx, int bz, int cx, int cz,
            int originX, int originZ, int cell, int colCountX, int colCountZ,
            out int minColX, out int maxColX, out int minColZ, out int maxColZ)
        {
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

            // Closed columns [origin+i*cell, origin+(i+1)*cell] share internal boundaries.
            // floor((min-origin-1)/cell) keeps the left column when min lies exactly on a grid line;
            // floor((max-origin)/cell) keeps the right column when max lies exactly on a grid line.
            // Promote before subtract so extreme int-centimeter AABBs cannot overflow.
            long minColXL = FloorDiv((long)minX - originX - 1, cell);
            long maxColXL = FloorDiv((long)maxX - originX, cell);
            long minColZL = FloorDiv((long)minZ - originZ - 1, cell);
            long maxColZL = FloorDiv((long)maxZ - originZ, cell);

            if (maxColXL < 0 || maxColZL < 0 || minColXL >= colCountX || minColZL >= colCountZ)
            {
                minColX = 0;
                maxColX = -1;
                minColZ = 0;
                maxColZ = -1;
                return false;
            }

            minColX = minColXL < 0 ? 0 : (int)minColXL;
            minColZ = minColZL < 0 ? 0 : (int)minColZL;
            maxColX = maxColXL >= colCountX ? colCountX - 1 : (int)maxColXL;
            maxColZ = maxColZL >= colCountZ ? colCountZ - 1 : (int)maxColZL;
            return true;
        }

        private static void ComputeExactNormal(
            int ax, int ay, int az,
            int bx, int by, int bz,
            int cx, int cy, int cz,
            out Int128 nx, out Int128 ny, out Int128 nz)
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
        }

        private static Int128 OrientXZ(int ax, int az, int bx, int bz, int cx, int cz)
            => (((Int128)bx - ax) * ((Int128)cz - az)) - (((Int128)cx - ax) * ((Int128)bz - az));

        private static bool TryAcceptColumnSpan(
            int ax, int ay, int az,
            int bx, int by, int bz,
            int cx, int cy, int cz,
            Int128 xzArea,
            bool requirePositiveXZArea,
            int colMinX, int colMaxX, int colMinZ, int colMaxZ,
            out int minY, out int maxY)
        {
            if (!TryIntersectColumnY(
                    ax, ay, az, bx, by, bz, cx, cy, cz,
                    xzArea,
                    colMinX, colMaxX, colMinZ, colMaxZ,
                    out minY, out maxY))
            {
                return false;
            }

            if (!requirePositiveXZArea)
            {
                return true;
            }

            return HasStrictlyPositiveXZAreaInColumn(
                ax, az, bx, bz, cx, cz,
                xzArea,
                colMinX, colMaxX, colMinZ, colMaxZ);
        }

        private static bool TryIntersectColumnY(
            int ax, int ay, int az,
            int bx, int by, int bz,
            int cx, int cy, int cz,
            Int128 xzArea,
            int colMinX, int colMaxX, int colMinZ, int colMaxZ,
            out int minY, out int maxY)
        {
            return TryIntersectColumnCoverage(
                ax, ay, az, bx, by, bz, cx, cy, cz,
                xzArea,
                colMinX, colMaxX, colMinZ, colMaxZ,
                trackAlong: false,
                alongIsX: false,
                out minY, out maxY, out _, out _);
        }

        /// <summary>
        /// WalkCandidate spans require triangle∩column to have strictly positive XZ area.
        /// Line/point-only closed-boundary contact must not create a walk surface.
        /// Equivalent to: nonzero triangle XZ area and open-column ∩ closed-triangle nonempty.
        /// </summary>
        private static bool HasStrictlyPositiveXZAreaInColumn(
            int ax, int az,
            int bx, int bz,
            int cx, int cz,
            Int128 xzArea,
            int colMinX, int colMaxX, int colMinZ, int colMaxZ)
        {
            if (xzArea == 0)
            {
                return false;
            }

            if (PointInOpenColumn(ax, az, colMinX, colMaxX, colMinZ, colMaxZ) ||
                PointInOpenColumn(bx, bz, colMinX, colMaxX, colMinZ, colMaxZ) ||
                PointInOpenColumn(cx, cz, colMinX, colMaxX, colMinZ, colMaxZ))
            {
                return true;
            }

            if (PointStrictlyInTriangleXZ(colMinX, colMinZ, ax, az, bx, bz, cx, cz, xzArea) ||
                PointStrictlyInTriangleXZ(colMaxX, colMinZ, ax, az, bx, bz, cx, cz, xzArea) ||
                PointStrictlyInTriangleXZ(colMinX, colMaxZ, ax, az, bx, bz, cx, cz, xzArea) ||
                PointStrictlyInTriangleXZ(colMaxX, colMaxZ, ax, az, bx, bz, cx, cz, xzArea))
            {
                return true;
            }

            return SegmentIntersectsOpenColumn(ax, az, bx, bz, colMinX, colMaxX, colMinZ, colMaxZ) ||
                   SegmentIntersectsOpenColumn(bx, bz, cx, cz, colMinX, colMaxX, colMinZ, colMaxZ) ||
                   SegmentIntersectsOpenColumn(cx, cz, ax, az, colMinX, colMaxX, colMinZ, colMaxZ);
        }

        private static bool PointInOpenColumn(int x, int z, int minX, int maxX, int minZ, int maxZ)
            => x > minX && x < maxX && z > minZ && z < maxZ;

        private static bool PointStrictlyInTriangleXZ(
            int px, int pz,
            int ax, int az, int bx, int bz, int cx, int cz,
            Int128 xzArea)
        {
            Int128 o1 = OrientXZ(ax, az, bx, bz, px, pz);
            Int128 o2 = OrientXZ(bx, bz, cx, cz, px, pz);
            Int128 o3 = OrientXZ(cx, cz, ax, az, px, pz);
            if (xzArea > 0)
            {
                return o1 > 0 && o2 > 0 && o3 > 0;
            }

            return o1 < 0 && o2 < 0 && o3 < 0;
        }

        private static bool SegmentIntersectsOpenColumn(
            int x0, int z0, int x1, int z1,
            int colMinX, int colMaxX, int colMinZ, int colMaxZ)
        {
            Int128 tMinNum = 0;
            Int128 tMinDen = 1;
            Int128 tMaxNum = 1;
            Int128 tMaxDen = 1;

            if (!ClipAxisOpen(x0, x1, colMinX, colMaxX, ref tMinNum, ref tMinDen, ref tMaxNum, ref tMaxDen))
            {
                return false;
            }

            if (!ClipAxisOpen(z0, z1, colMinZ, colMaxZ, ref tMinNum, ref tMinDen, ref tMaxNum, ref tMaxDen))
            {
                return false;
            }

            return CompareRational(tMinNum, tMinDen, tMaxNum, tMaxDen) < 0;
        }

        private static bool ClipAxisOpen(
            int p0, int p1,
            int min, int max,
            ref Int128 tMinNum, ref Int128 tMinDen,
            ref Int128 tMaxNum, ref Int128 tMaxDen)
        {
            long dp = (long)p1 - p0;
            if (dp == 0)
            {
                return p0 > min && p0 < max;
            }

            Int128 tAtMinNum = (Int128)min - p0;
            Int128 tAtMinDen = dp;
            Int128 tAtMaxNum = (Int128)max - p0;
            Int128 tAtMaxDen = dp;

            if (dp > 0)
            {
                RaiseTMin(tAtMinNum, tAtMinDen, ref tMinNum, ref tMinDen);
                LowerTMax(tAtMaxNum, tAtMaxDen, ref tMaxNum, ref tMaxDen);
            }
            else
            {
                RaiseTMin(tAtMaxNum, tAtMaxDen, ref tMinNum, ref tMinDen);
                LowerTMax(tAtMinNum, tAtMinDen, ref tMaxNum, ref tMaxDen);
            }

            // Open interval (min,max): nonempty iff enter < exit.
            return CompareRational(tMinNum, tMinDen, tMaxNum, tMaxDen) < 0;
        }

        /// <summary>
        /// Triangle ∩ closed boundary segment. Along axis is Z for West/East (fixed X), X for North/South (fixed Z).
        /// </summary>
        private static bool TryIntersectBoundaryCoverage(
            int ax, int ay, int az,
            int bx, int by, int bz,
            int cx, int cy, int cz,
            Int128 xzArea,
            bool alongIsX,
            int fixedCoord,
            int alongLo,
            int alongHi,
            out int minY,
            out int maxY,
            out int alongMin,
            out int alongMax)
        {
            int colMinX;
            int colMaxX;
            int colMinZ;
            int colMaxZ;
            if (alongIsX)
            {
                colMinX = alongLo;
                colMaxX = alongHi;
                colMinZ = fixedCoord;
                colMaxZ = fixedCoord;
            }
            else
            {
                colMinX = fixedCoord;
                colMaxX = fixedCoord;
                colMinZ = alongLo;
                colMaxZ = alongHi;
            }

            return TryIntersectColumnCoverage(
                ax, ay, az, bx, by, bz, cx, cy, cz,
                xzArea,
                colMinX, colMaxX, colMinZ, colMaxZ,
                trackAlong: true,
                alongIsX,
                out minY, out maxY, out alongMin, out alongMax);
        }

        private static bool TryIntersectColumnCoverage(
            int ax, int ay, int az,
            int bx, int by, int bz,
            int cx, int cy, int cz,
            Int128 xzArea,
            int colMinX, int colMaxX, int colMinZ, int colMaxZ,
            bool trackAlong,
            bool alongIsX,
            out int minY, out int maxY,
            out int alongMin, out int alongMax)
        {
            minY = int.MaxValue;
            maxY = int.MinValue;
            alongMin = int.MaxValue;
            alongMax = int.MinValue;
            bool any = false;

            if (xzArea == 0)
            {
                any |= IncludeSegmentColumnCoverage(
                    ax, ay, az, bx, by, bz, colMinX, colMaxX, colMinZ, colMaxZ,
                    trackAlong, alongIsX, ref minY, ref maxY, ref alongMin, ref alongMax);
                any |= IncludeSegmentColumnCoverage(
                    bx, by, bz, cx, cy, cz, colMinX, colMaxX, colMinZ, colMaxZ,
                    trackAlong, alongIsX, ref minY, ref maxY, ref alongMin, ref alongMax);
                any |= IncludeSegmentColumnCoverage(
                    cx, cy, cz, ax, ay, az, colMinX, colMaxX, colMinZ, colMaxZ,
                    trackAlong, alongIsX, ref minY, ref maxY, ref alongMin, ref alongMax);
                return any;
            }

            if (PointInClosedColumn(ax, az, colMinX, colMaxX, colMinZ, colMaxZ))
            {
                IncludeExactY(ay, ref minY, ref maxY);
                IncludeExactAlong(alongIsX ? ax : az, trackAlong, ref alongMin, ref alongMax);
                any = true;
            }

            if (PointInClosedColumn(bx, bz, colMinX, colMaxX, colMinZ, colMaxZ))
            {
                IncludeExactY(by, ref minY, ref maxY);
                IncludeExactAlong(alongIsX ? bx : bz, trackAlong, ref alongMin, ref alongMax);
                any = true;
            }

            if (PointInClosedColumn(cx, cz, colMinX, colMaxX, colMinZ, colMaxZ))
            {
                IncludeExactY(cy, ref minY, ref maxY);
                IncludeExactAlong(alongIsX ? cx : cz, trackAlong, ref alongMin, ref alongMax);
                any = true;
            }

            any |= IncludeEdgeBoundaryHitsCoverage(
                ax, ay, az, bx, by, bz, colMinX, colMaxX, colMinZ, colMaxZ,
                trackAlong, alongIsX, ref minY, ref maxY, ref alongMin, ref alongMax);
            any |= IncludeEdgeBoundaryHitsCoverage(
                bx, by, bz, cx, cy, cz, colMinX, colMaxX, colMinZ, colMaxZ,
                trackAlong, alongIsX, ref minY, ref maxY, ref alongMin, ref alongMax);
            any |= IncludeEdgeBoundaryHitsCoverage(
                cx, cy, cz, ax, ay, az, colMinX, colMaxX, colMinZ, colMaxZ,
                trackAlong, alongIsX, ref minY, ref maxY, ref alongMin, ref alongMax);

            any |= IncludeCornerIfInTriangleCoverage(
                colMinX, colMinZ, ax, ay, az, bx, by, bz, cx, cy, cz, xzArea,
                trackAlong, alongIsX, ref minY, ref maxY, ref alongMin, ref alongMax);
            any |= IncludeCornerIfInTriangleCoverage(
                colMaxX, colMinZ, ax, ay, az, bx, by, bz, cx, cy, cz, xzArea,
                trackAlong, alongIsX, ref minY, ref maxY, ref alongMin, ref alongMax);
            any |= IncludeCornerIfInTriangleCoverage(
                colMinX, colMaxZ, ax, ay, az, bx, by, bz, cx, cy, cz, xzArea,
                trackAlong, alongIsX, ref minY, ref maxY, ref alongMin, ref alongMax);
            any |= IncludeCornerIfInTriangleCoverage(
                colMaxX, colMaxZ, ax, ay, az, bx, by, bz, cx, cy, cz, xzArea,
                trackAlong, alongIsX, ref minY, ref maxY, ref alongMin, ref alongMax);

            return any;
        }

        private static bool IncludeCornerIfInTriangle(
            int x, int z,
            int ax, int ay, int az,
            int bx, int by, int bz,
            int cx, int cy, int cz,
            Int128 xzArea,
            ref int minY, ref int maxY)
        {
            int alongMin = 0;
            int alongMax = 0;
            return IncludeCornerIfInTriangleCoverage(
                x, z, ax, ay, az, bx, by, bz, cx, cy, cz, xzArea,
                trackAlong: false, alongIsX: false, ref minY, ref maxY, ref alongMin, ref alongMax);
        }

        private static bool IncludeCornerIfInTriangleCoverage(
            int x, int z,
            int ax, int ay, int az,
            int bx, int by, int bz,
            int cx, int cy, int cz,
            Int128 xzArea,
            bool trackAlong,
            bool alongIsX,
            ref int minY, ref int maxY,
            ref int alongMin, ref int alongMax)
        {
            if (!PointInClosedTriangleXZ(x, z, ax, az, bx, bz, cx, cz, xzArea))
            {
                return false;
            }

            IncludePlaneY(x, z, ax, ay, az, bx, by, bz, cx, cy, cz, ref minY, ref maxY);
            IncludeExactAlong(alongIsX ? x : z, trackAlong, ref alongMin, ref alongMax);
            return true;
        }

        private static bool PointInClosedTriangleXZ(
            int px, int pz,
            int ax, int az, int bx, int bz, int cx, int cz,
            Int128 xzArea)
        {
            Int128 o1 = OrientXZ(ax, az, bx, bz, px, pz);
            Int128 o2 = OrientXZ(bx, bz, cx, cz, px, pz);
            Int128 o3 = OrientXZ(cx, cz, ax, az, px, pz);
            if (xzArea > 0)
            {
                return o1 >= 0 && o2 >= 0 && o3 >= 0;
            }

            return o1 <= 0 && o2 <= 0 && o3 <= 0;
        }

        private static void IncludePlaneY(
            int x, int z,
            int ax, int ay, int az,
            int bx, int by, int bz,
            int cx, int cy, int cz,
            ref int minY, ref int maxY)
        {
            ComputeExactNormal(ax, ay, az, bx, by, bz, cx, cy, cz, out Int128 nx, out Int128 ny, out Int128 nz);
            if (ny == 0)
            {
                // Vertical non-degenerate XZ face: Y extrema come from edge/vertex hits already.
                return;
            }

            // ny*y = ny*ay - nx*(x-ax) - nz*(z-az)
            Int128 numer = (ny * ay) - (nx * ((Int128)x - ax)) - (nz * ((Int128)z - az));
            IncludeRationalY(numer, ny, ref minY, ref maxY);
        }

        private static bool IncludeEdgeBoundaryHits(
            int x0, int y0, int z0,
            int x1, int y1, int z1,
            int colMinX, int colMaxX, int colMinZ, int colMaxZ,
            ref int minY, ref int maxY)
        {
            int alongMin = 0;
            int alongMax = 0;
            return IncludeEdgeBoundaryHitsCoverage(
                x0, y0, z0, x1, y1, z1, colMinX, colMaxX, colMinZ, colMaxZ,
                trackAlong: false, alongIsX: false, ref minY, ref maxY, ref alongMin, ref alongMax);
        }

        private static bool IncludeEdgeBoundaryHitsCoverage(
            int x0, int y0, int z0,
            int x1, int y1, int z1,
            int colMinX, int colMaxX, int colMinZ, int colMaxZ,
            bool trackAlong,
            bool alongIsX,
            ref int minY, ref int maxY,
            ref int alongMin, ref int alongMax)
        {
            bool any = false;
            any |= IncludeEdgePlaneHitCoverage(
                x0, y0, z0, x1, y1, z1, axisX: true, plane: colMinX, colMinX, colMaxX, colMinZ, colMaxZ,
                trackAlong, alongIsX, ref minY, ref maxY, ref alongMin, ref alongMax);
            any |= IncludeEdgePlaneHitCoverage(
                x0, y0, z0, x1, y1, z1, axisX: true, plane: colMaxX, colMinX, colMaxX, colMinZ, colMaxZ,
                trackAlong, alongIsX, ref minY, ref maxY, ref alongMin, ref alongMax);
            any |= IncludeEdgePlaneHitCoverage(
                x0, y0, z0, x1, y1, z1, axisX: false, plane: colMinZ, colMinX, colMaxX, colMinZ, colMaxZ,
                trackAlong, alongIsX, ref minY, ref maxY, ref alongMin, ref alongMax);
            any |= IncludeEdgePlaneHitCoverage(
                x0, y0, z0, x1, y1, z1, axisX: false, plane: colMaxZ, colMinX, colMaxX, colMinZ, colMaxZ,
                trackAlong, alongIsX, ref minY, ref maxY, ref alongMin, ref alongMax);
            return any;
        }

        private static bool IncludeEdgePlaneHit(
            int x0, int y0, int z0,
            int x1, int y1, int z1,
            bool axisX,
            int plane,
            int colMinX, int colMaxX, int colMinZ, int colMaxZ,
            ref int minY, ref int maxY)
        {
            int alongMin = 0;
            int alongMax = 0;
            return IncludeEdgePlaneHitCoverage(
                x0, y0, z0, x1, y1, z1, axisX, plane, colMinX, colMaxX, colMinZ, colMaxZ,
                trackAlong: false, alongIsX: false, ref minY, ref maxY, ref alongMin, ref alongMax);
        }

        private static bool IncludeEdgePlaneHitCoverage(
            int x0, int y0, int z0,
            int x1, int y1, int z1,
            bool axisX,
            int plane,
            int colMinX, int colMaxX, int colMinZ, int colMaxZ,
            bool trackAlong,
            bool alongIsX,
            ref int minY, ref int maxY,
            ref int alongMin, ref int alongMax)
        {
            int a0 = axisX ? x0 : z0;
            int a1 = axisX ? x1 : z1;
            long da = (long)a1 - a0;
            if (da == 0)
            {
                return false;
            }

            Int128 numT = (Int128)plane - a0;
            Int128 denT = da;
            if (denT < 0)
            {
                numT = -numT;
                denT = -denT;
            }

            if (numT < 0 || numT > denT)
            {
                return false;
            }

            int b0 = axisX ? z0 : x0;
            int b1 = axisX ? z1 : x1;
            Int128 bNumer = ((Int128)b0 * denT) + ((((Int128)b1 - b0) * numT));
            int bMinAllowed = axisX ? colMinZ : colMinX;
            int bMaxAllowed = axisX ? colMaxZ : colMaxX;
            if (CompareRational(bNumer, denT, bMinAllowed, 1) < 0 ||
                CompareRational(bNumer, denT, bMaxAllowed, 1) > 0)
            {
                return false;
            }

            Int128 yNumer = ((Int128)y0 * denT) + ((((Int128)y1 - y0) * numT));
            IncludeRationalY(yNumer, denT, ref minY, ref maxY);

            if (trackAlong)
            {
                // Along is X when alongIsX; otherwise Z.
                // If the hit plane is the fixed along-orthogonal face, b is the along coordinate.
                // If the hit plane is an along-endpoint face, the fixed coord is plane and along is a0..a1 lerp = plane on the fixed axis...
                // West/East: fixed X, along Z. Hit on X-plane => b=Z=along. Hit on Z-plane => plane=Z=along, b=X=fixed.
                // North/South: fixed Z, along X. Hit on Z-plane => b=X=along. Hit on X-plane => plane=X=along, b=Z=fixed.
                if (alongIsX)
                {
                    if (axisX)
                    {
                        // Hit vertical X=plane: along X is exactly plane.
                        IncludeExactAlong(plane, trackAlong: true, ref alongMin, ref alongMax);
                    }
                    else
                    {
                        // Hit horizontal Z=plane: along X is b.
                        IncludeRationalAlong(bNumer, denT, ref alongMin, ref alongMax);
                    }
                }
                else
                {
                    if (axisX)
                    {
                        // Hit vertical X=plane: along Z is b.
                        IncludeRationalAlong(bNumer, denT, ref alongMin, ref alongMax);
                    }
                    else
                    {
                        // Hit horizontal Z=plane: along Z is exactly plane.
                        IncludeExactAlong(plane, trackAlong: true, ref alongMin, ref alongMax);
                    }
                }
            }

            return true;
        }

        private static bool IncludeSegmentColumnY(
            int x0, int y0, int z0,
            int x1, int y1, int z1,
            int colMinX, int colMaxX, int colMinZ, int colMaxZ,
            ref int minY, ref int maxY)
        {
            int alongMin = 0;
            int alongMax = 0;
            return IncludeSegmentColumnCoverage(
                x0, y0, z0, x1, y1, z1, colMinX, colMaxX, colMinZ, colMaxZ,
                trackAlong: false, alongIsX: false, ref minY, ref maxY, ref alongMin, ref alongMax);
        }

        private static bool IncludeSegmentColumnCoverage(
            int x0, int y0, int z0,
            int x1, int y1, int z1,
            int colMinX, int colMaxX, int colMinZ, int colMaxZ,
            bool trackAlong,
            bool alongIsX,
            ref int minY, ref int maxY,
            ref int alongMin, ref int alongMax)
        {
            // Clip XZ segment to closed column; evaluate Y/along at clipped endpoints.
            Int128 tMinNum = 0;
            Int128 tMinDen = 1;
            Int128 tMaxNum = 1;
            Int128 tMaxDen = 1;

            if (!ClipAxis(x0, x1, colMinX, colMaxX, ref tMinNum, ref tMinDen, ref tMaxNum, ref tMaxDen))
            {
                return false;
            }

            if (!ClipAxis(z0, z1, colMinZ, colMaxZ, ref tMinNum, ref tMinDen, ref tMaxNum, ref tMaxDen))
            {
                return false;
            }

            IncludeLerpY(y0, y1, tMinNum, tMinDen, ref minY, ref maxY);
            IncludeLerpY(y0, y1, tMaxNum, tMaxDen, ref minY, ref maxY);
            if (trackAlong)
            {
                int a0 = alongIsX ? x0 : z0;
                int a1 = alongIsX ? x1 : z1;
                IncludeLerpAlong(a0, a1, tMinNum, tMinDen, ref alongMin, ref alongMax);
                IncludeLerpAlong(a0, a1, tMaxNum, tMaxDen, ref alongMin, ref alongMax);
            }

            return true;
        }

        private static bool ClipAxis(
            int p0, int p1,
            int min, int max,
            ref Int128 tMinNum, ref Int128 tMinDen,
            ref Int128 tMaxNum, ref Int128 tMaxDen)
        {
            long dp = (long)p1 - p0;
            if (dp == 0)
            {
                return p0 >= min && p0 <= max;
            }

            // p(t) >= min and p(t) <= max for t in [tMin, tMax].
            Int128 tAtMinNum = (Int128)min - p0;
            Int128 tAtMinDen = dp;
            Int128 tAtMaxNum = (Int128)max - p0;
            Int128 tAtMaxDen = dp;

            if (dp > 0)
            {
                RaiseTMin(tAtMinNum, tAtMinDen, ref tMinNum, ref tMinDen);
                LowerTMax(tAtMaxNum, tAtMaxDen, ref tMaxNum, ref tMaxDen);
            }
            else
            {
                RaiseTMin(tAtMaxNum, tAtMaxDen, ref tMinNum, ref tMinDen);
                LowerTMax(tAtMinNum, tAtMinDen, ref tMaxNum, ref tMaxDen);
            }

            return CompareRational(tMinNum, tMinDen, tMaxNum, tMaxDen) <= 0;
        }

        private static void RaiseTMin(Int128 num, Int128 den, ref Int128 tMinNum, ref Int128 tMinDen)
        {
            if (CompareRational(num, den, tMinNum, tMinDen) > 0)
            {
                tMinNum = num;
                tMinDen = den;
            }
        }

        private static void LowerTMax(Int128 num, Int128 den, ref Int128 tMaxNum, ref Int128 tMaxDen)
        {
            if (CompareRational(num, den, tMaxNum, tMaxDen) < 0)
            {
                tMaxNum = num;
                tMaxDen = den;
            }
        }

        private static void IncludeLerpY(int y0, int y1, Int128 tNum, Int128 tDen, ref int minY, ref int maxY)
        {
            if (tDen == 0)
            {
                return;
            }

            Int128 yNumer = ((Int128)y0 * tDen) + ((((Int128)y1 - y0) * tNum));
            IncludeRationalY(yNumer, tDen, ref minY, ref maxY);
        }

        private static void IncludeLerpAlong(int a0, int a1, Int128 tNum, Int128 tDen, ref int alongMin, ref int alongMax)
        {
            if (tDen == 0)
            {
                return;
            }

            Int128 aNumer = ((Int128)a0 * tDen) + ((((Int128)a1 - a0) * tNum));
            IncludeRationalAlong(aNumer, tDen, ref alongMin, ref alongMax);
        }

        private static void IncludeRationalY(Int128 numer, Int128 denom, ref int minY, ref int maxY)
        {
            if (denom == 0)
            {
                return;
            }

            int floor = FloorDivInt128(numer, denom);
            int ceil = CeilDivInt128(numer, denom);
            if (floor < minY) minY = floor;
            if (ceil > maxY) maxY = ceil;
        }

        private static void IncludeRationalAlong(Int128 numer, Int128 denom, ref int alongMin, ref int alongMax)
        {
            if (denom == 0)
            {
                return;
            }

            int floor = FloorDivInt128(numer, denom);
            int ceil = CeilDivInt128(numer, denom);
            if (floor < alongMin) alongMin = floor;
            if (ceil > alongMax) alongMax = ceil;
        }

        private static void IncludeExactY(int y, ref int minY, ref int maxY)
        {
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        private static void IncludeExactAlong(int along, bool trackAlong, ref int alongMin, ref int alongMax)
        {
            if (!trackAlong)
            {
                return;
            }

            if (along < alongMin) alongMin = along;
            if (along > alongMax) alongMax = along;
        }

        private static bool PointInClosedColumn(int x, int z, int minX, int maxX, int minZ, int maxZ)
            => x >= minX && x <= maxX && z >= minZ && z <= maxZ;

        private static long FloorDiv(long dividend, int divisor)
        {
            long quotient = dividend / divisor;
            if (dividend < 0 && (dividend % divisor) != 0)
            {
                quotient--;
            }

            return quotient;
        }

        private static int FloorDivInt128(Int128 numer, Int128 denom)
        {
            if (denom < 0)
            {
                numer = -numer;
                denom = -denom;
            }

            Int128 q = numer / denom;
            if (numer < 0 && (numer % denom) != 0)
            {
                q--;
            }

            return (int)q;
        }

        private static int CeilDivInt128(Int128 numer, Int128 denom)
        {
            if (denom < 0)
            {
                numer = -numer;
                denom = -denom;
            }

            Int128 q = numer / denom;
            if (numer > 0 && (numer % denom) != 0)
            {
                q++;
            }

            return (int)q;
        }

        private static int CompareRational(Int128 aNum, Int128 aDen, Int128 bNum, Int128 bDen)
        {
            if (aDen < 0)
            {
                aNum = -aNum;
                aDen = -aDen;
            }

            if (bDen < 0)
            {
                bNum = -bNum;
                bDen = -bDen;
            }

            Int128 left = aNum * bDen;
            Int128 right = bNum * aDen;
            if (left < right) return -1;
            if (left > right) return 1;
            return 0;
        }
    }
}
