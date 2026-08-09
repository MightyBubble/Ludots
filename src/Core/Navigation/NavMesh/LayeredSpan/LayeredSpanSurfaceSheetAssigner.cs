using System;
using Ludots.Core.Navigation.NavMesh.Surface;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Stateless same-column surface-sheet equivalence over walk-candidate spans.
    /// Sheets require positive-length geometric contact inside the closed cell; point-only contact is rejected.
    /// Faceted adjacent triangles that share a non-zero projected edge merge even when normals differ.
    /// Success-path Assign after scratch warmup allocates 0 managed bytes.
    /// </summary>
    public static class LayeredSpanSurfaceSheetAssigner
    {
        public static void Assign(
            NavTriangleSurfaceSnapshot surface,
            LayeredSpanScratch raw,
            in LayeredSpanRasterGridSpec grid,
            in LayeredSpanWalkabilitySpec walkabilitySpec,
            LayeredSpanSurfaceSheetScratch output)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (output == null) throw new ArgumentNullException(nameof(output));

            output.Reset();

            if (!raw.HasPublishedContent)
            {
                throw new InvalidOperationException(
                    "LayeredSpanSurfaceSheetAssigner requires published raw scratch content.");
            }

            int columnCount = raw.ColumnCount;
            int spanCount = raw.SpanCount;

            if (columnCount != grid.ColumnCount)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanSurfaceSheetAssigner grid.ColumnCount ({grid.ColumnCount}) must equal raw.ColumnCount ({columnCount}).");
            }

            if (columnCount > output.ColumnCapacity)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanSurfaceSheetScratch.columnCapacity ({output.ColumnCapacity}); required {columnCount}.");
            }

            if (spanCount > output.SpanCapacity)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanSurfaceSheetScratch.spanCapacity ({output.SpanCapacity}); required {spanCount}.");
            }

            output.Prepare(columnCount, spanCount);

            ReadOnlySpan<int> columnCounts = raw.ColumnSpanCounts;
            ReadOnlySpan<int> columnOffsets = raw.ColumnSpanOffsets;
            ReadOnlySpan<int> minY = raw.SpanMinYcm;
            ReadOnlySpan<int> maxY = raw.SpanMaxYcm;
            ReadOnlySpan<int> triIndices = raw.SpanTriangleIndices;
            ReadOnlySpan<NavTriangleSurfaceFlags> flags = raw.SpanSurfaceFlags;
            ReadOnlySpan<Int128> nx = raw.SpanNormalX;
            ReadOnlySpan<Int128> ny = raw.SpanNormalY;
            ReadOnlySpan<Int128> nz = raw.SpanNormalZ;

            Span<int> sheetIds = output.MutableSpanSheetIds;
            Span<int> parent = output.MutableUnionParent;
            Span<int> rank = output.MutableUnionRank;
            Span<int> componentMin = output.MutableComponentMinSpan;
            Span<int> sheetByRoot = output.MutableSheetIdByRoot;

            ReadOnlySpan<int> vertexXcm = surface.VertexXcm;
            ReadOnlySpan<int> vertexYcm = surface.VertexYcm;
            ReadOnlySpan<int> vertexZcm = surface.VertexZcm;
            ReadOnlySpan<int> triA = surface.TriA;
            ReadOnlySpan<int> triB = surface.TriB;
            ReadOnlySpan<int> triC = surface.TriC;
            int tolerance = walkabilitySpec.SameSurfaceToleranceCm;
            int colCountX = grid.ColumnCountX;
            int originX = grid.OriginXcm;
            int originZ = grid.OriginZcm;
            int cell = grid.CellSizeCm;

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

                for (int span = start; span < end; span++)
                {
                    parent[span] = span;
                    rank[span] = 0;
                    componentMin[span] = span;
                }

                for (int i = start; i < end; i++)
                {
                    if ((flags[i] & NavTriangleSurfaceFlags.WalkCandidate) == 0)
                    {
                        continue;
                    }

                    for (int j = i + 1; j < end; j++)
                    {
                        if ((flags[j] & NavTriangleSurfaceFlags.WalkCandidate) == 0)
                        {
                            continue;
                        }

                        if (raw.SpanAreaIds[i] != raw.SpanAreaIds[j])
                        {
                            continue;
                        }

                        if (!AreSameSurfaceSheet(
                                i,
                                j,
                                minY,
                                maxY,
                                nx,
                                ny,
                                nz,
                                triIndices,
                                vertexXcm,
                                vertexYcm,
                                vertexZcm,
                                triA,
                                triB,
                                triC,
                                cellMinX,
                                cellMaxX,
                                cellMinZ,
                                cellMaxZ,
                                tolerance))
                        {
                            continue;
                        }

                        Union(i, j, parent, rank, componentMin);
                    }
                }
            }

            // Deterministic sheet ids: ascending by minimum source-span index in each component.
            int sheetCount = 0;
            for (int span = 0; span < spanCount; span++)
            {
                if ((flags[span] & NavTriangleSurfaceFlags.WalkCandidate) == 0)
                {
                    sheetIds[span] = -1;
                    continue;
                }

                int root = Find(span, parent);
                if (componentMin[root] != span)
                {
                    continue;
                }

                sheetByRoot[root] = sheetCount++;
            }

            for (int span = 0; span < spanCount; span++)
            {
                if ((flags[span] & NavTriangleSurfaceFlags.WalkCandidate) == 0)
                {
                    sheetIds[span] = -1;
                    continue;
                }

                int root = Find(span, parent);
                sheetIds[span] = sheetByRoot[root];
            }

            output.CommitSheetCount(raw, sheetCount);
        }

        private static bool AreSameSurfaceSheet(
            int left,
            int right,
            ReadOnlySpan<int> minY,
            ReadOnlySpan<int> maxY,
            ReadOnlySpan<Int128> nx,
            ReadOnlySpan<Int128> ny,
            ReadOnlySpan<Int128> nz,
            ReadOnlySpan<int> triIndices,
            ReadOnlySpan<int> vertexXcm,
            ReadOnlySpan<int> vertexYcm,
            ReadOnlySpan<int> vertexZcm,
            ReadOnlySpan<int> triA,
            ReadOnlySpan<int> triB,
            ReadOnlySpan<int> triC,
            int cellMinX,
            int cellMaxX,
            int cellMinZ,
            int cellMaxZ,
            int tolerance)
        {
            if (!YIntervalsWithinTolerance(minY[left], maxY[left], minY[right], maxY[right], tolerance))
            {
                return false;
            }

            int triL = triIndices[left];
            int triR = triIndices[right];
            if (triL == triR)
            {
                return true;
            }

            // Faceted but continuous: shared mesh edge with non-zero XZ projection inside the closed cell.
            if (SharePositiveLengthProjectedMeshEdgeInCell(
                    triL,
                    triR,
                    vertexXcm,
                    vertexYcm,
                    vertexZcm,
                    triA,
                    triB,
                    triC,
                    cellMinX,
                    cellMaxX,
                    cellMinZ,
                    cellMaxZ))
            {
                return true;
            }

            // Coplanar path: one nondegenerate raw normal + zero signed distance for all three
            // other-triangle vertices. Avoids normal×normal products that can overflow Int128.
            // Still requires real geometric contact inside the cell (not Y/plane alone).
            if (!AreExactCoplanarBySignedPlaneDistances(
                    triL,
                    triR,
                    nx[left],
                    ny[left],
                    nz[left],
                    nx[right],
                    ny[right],
                    nz[right],
                    vertexXcm,
                    vertexYcm,
                    vertexZcm,
                    triA,
                    triB,
                    triC))
            {
                return false;
            }

            return HavePositiveLengthXzContactInCell(
                triL,
                triR,
                vertexXcm,
                vertexZcm,
                triA,
                triB,
                triC,
                cellMinX,
                cellMaxX,
                cellMinZ,
                cellMaxZ);
        }

        private static bool AreExactCoplanarBySignedPlaneDistances(
            int triL,
            int triR,
            Int128 nxL,
            Int128 nyL,
            Int128 nzL,
            Int128 nxR,
            Int128 nyR,
            Int128 nzR,
            ReadOnlySpan<int> vertexXcm,
            ReadOnlySpan<int> vertexYcm,
            ReadOnlySpan<int> vertexZcm,
            ReadOnlySpan<int> triA,
            ReadOnlySpan<int> triB,
            ReadOnlySpan<int> triC)
        {
            // Prefer the right triangle's raw normal when nondegenerate; otherwise the left.
            if (IsNonZeroNormal(nxR, nyR, nzR))
            {
                return AllVerticesHaveZeroSignedPlaneDistance(
                    triL,
                    nxR,
                    nyR,
                    nzR,
                    triA[triR],
                    vertexXcm,
                    vertexYcm,
                    vertexZcm,
                    triA,
                    triB,
                    triC);
            }

            if (IsNonZeroNormal(nxL, nyL, nzL))
            {
                return AllVerticesHaveZeroSignedPlaneDistance(
                    triR,
                    nxL,
                    nyL,
                    nzL,
                    triA[triL],
                    vertexXcm,
                    vertexYcm,
                    vertexZcm,
                    triA,
                    triB,
                    triC);
            }

            return false;
        }

        private static bool IsNonZeroNormal(Int128 nx, Int128 ny, Int128 nz)
            => nx != 0 || ny != 0 || nz != 0;

        private static bool AllVerticesHaveZeroSignedPlaneDistance(
            int otherTri,
            Int128 planeNx,
            Int128 planeNy,
            Int128 planeNz,
            int planePointVertex,
            ReadOnlySpan<int> vertexXcm,
            ReadOnlySpan<int> vertexYcm,
            ReadOnlySpan<int> vertexZcm,
            ReadOnlySpan<int> triA,
            ReadOnlySpan<int> triB,
            ReadOnlySpan<int> triC)
        {
            int px = vertexXcm[planePointVertex];
            int py = vertexYcm[planePointVertex];
            int pz = vertexZcm[planePointVertex];
            return HasZeroSignedPlaneDistance(triA[otherTri], planeNx, planeNy, planeNz, px, py, pz, vertexXcm, vertexYcm, vertexZcm) &&
                   HasZeroSignedPlaneDistance(triB[otherTri], planeNx, planeNy, planeNz, px, py, pz, vertexXcm, vertexYcm, vertexZcm) &&
                   HasZeroSignedPlaneDistance(triC[otherTri], planeNx, planeNy, planeNz, px, py, pz, vertexXcm, vertexYcm, vertexZcm);
        }

        private static bool HasZeroSignedPlaneDistance(
            int vertex,
            Int128 planeNx,
            Int128 planeNy,
            Int128 planeNz,
            int planePointX,
            int planePointY,
            int planePointZ,
            ReadOnlySpan<int> vertexXcm,
            ReadOnlySpan<int> vertexYcm,
            ReadOnlySpan<int> vertexZcm)
        {
            // Promote before subtraction so extreme int centimeters cannot overflow.
            Int128 dx = (Int128)vertexXcm[vertex] - planePointX;
            Int128 dy = (Int128)vertexYcm[vertex] - planePointY;
            Int128 dz = (Int128)vertexZcm[vertex] - planePointZ;
            return (planeNx * dx) + (planeNy * dy) + (planeNz * dz) == 0;
        }

        private static bool SharePositiveLengthProjectedMeshEdgeInCell(
            int triL,
            int triR,
            ReadOnlySpan<int> vertexXcm,
            ReadOnlySpan<int> vertexYcm,
            ReadOnlySpan<int> vertexZcm,
            ReadOnlySpan<int> triA,
            ReadOnlySpan<int> triB,
            ReadOnlySpan<int> triC,
            int cellMinX,
            int cellMaxX,
            int cellMinZ,
            int cellMaxZ)
        {
            int la = triA[triL];
            int lb = triB[triL];
            int lc = triC[triL];
            int ra = triA[triR];
            int rb = triB[triR];
            int rc = triC[triR];

            return EdgePairPositiveInCell(la, lb, ra, rb, rc, vertexXcm, vertexYcm, vertexZcm, cellMinX, cellMaxX, cellMinZ, cellMaxZ) ||
                   EdgePairPositiveInCell(lb, lc, ra, rb, rc, vertexXcm, vertexYcm, vertexZcm, cellMinX, cellMaxX, cellMinZ, cellMaxZ) ||
                   EdgePairPositiveInCell(lc, la, ra, rb, rc, vertexXcm, vertexYcm, vertexZcm, cellMinX, cellMaxX, cellMinZ, cellMaxZ);
        }

        private static bool EdgePairPositiveInCell(
            int e0,
            int e1,
            int ra,
            int rb,
            int rc,
            ReadOnlySpan<int> vertexXcm,
            ReadOnlySpan<int> vertexYcm,
            ReadOnlySpan<int> vertexZcm,
            int cellMinX,
            int cellMaxX,
            int cellMinZ,
            int cellMaxZ)
        {
            if (!TryMatchUndirectedEdge(e0, e1, ra, rb, rc, out int m0, out int m1))
            {
                // Geometric coincidence: identical endpoint coordinates (duplicated verts).
                if (!TryMatchGeometricEdge(e0, e1, ra, rb, rc, vertexXcm, vertexYcm, vertexZcm, out m0, out m1))
                {
                    return false;
                }
            }

            return SegmentHasPositiveProjectedLengthInClosedCell(
                vertexXcm[m0], vertexZcm[m0],
                vertexXcm[m1], vertexZcm[m1],
                cellMinX, cellMaxX, cellMinZ, cellMaxZ);
        }

        private static bool TryMatchUndirectedEdge(
            int e0,
            int e1,
            int ra,
            int rb,
            int rc,
            out int m0,
            out int m1)
        {
            if (MatchesEdge(e0, e1, ra, rb) || MatchesEdge(e0, e1, rb, rc) || MatchesEdge(e0, e1, rc, ra))
            {
                m0 = e0;
                m1 = e1;
                return true;
            }

            m0 = 0;
            m1 = 0;
            return false;
        }

        private static bool MatchesEdge(int a0, int a1, int b0, int b1)
            => (a0 == b0 && a1 == b1) || (a0 == b1 && a1 == b0);

        private static bool TryMatchGeometricEdge(
            int e0,
            int e1,
            int ra,
            int rb,
            int rc,
            ReadOnlySpan<int> vertexXcm,
            ReadOnlySpan<int> vertexYcm,
            ReadOnlySpan<int> vertexZcm,
            out int m0,
            out int m1)
        {
            if (GeometricMatchesEdge(e0, e1, ra, rb, vertexXcm, vertexYcm, vertexZcm) ||
                GeometricMatchesEdge(e0, e1, rb, rc, vertexXcm, vertexYcm, vertexZcm) ||
                GeometricMatchesEdge(e0, e1, rc, ra, vertexXcm, vertexYcm, vertexZcm))
            {
                m0 = e0;
                m1 = e1;
                return true;
            }

            m0 = 0;
            m1 = 0;
            return false;
        }

        private static bool GeometricMatchesEdge(
            int a0,
            int a1,
            int b0,
            int b1,
            ReadOnlySpan<int> vertexXcm,
            ReadOnlySpan<int> vertexYcm,
            ReadOnlySpan<int> vertexZcm)
        {
            return (SamePoint(a0, b0, vertexXcm, vertexYcm, vertexZcm) &&
                    SamePoint(a1, b1, vertexXcm, vertexYcm, vertexZcm)) ||
                   (SamePoint(a0, b1, vertexXcm, vertexYcm, vertexZcm) &&
                    SamePoint(a1, b0, vertexXcm, vertexYcm, vertexZcm));
        }

        private static bool SamePoint(
            int a,
            int b,
            ReadOnlySpan<int> vertexXcm,
            ReadOnlySpan<int> vertexYcm,
            ReadOnlySpan<int> vertexZcm)
            => vertexXcm[a] == vertexXcm[b] &&
               vertexYcm[a] == vertexYcm[b] &&
               vertexZcm[a] == vertexZcm[b];

        private static bool SegmentHasPositiveProjectedLengthInClosedCell(
            int x0,
            int z0,
            int x1,
            int z1,
            int cellMinX,
            int cellMaxX,
            int cellMinZ,
            int cellMaxZ)
        {
            // Zero XZ projection cannot form a traversable sheet portal edge.
            if (x0 == x1 && z0 == z1)
            {
                return false;
            }

            Int128 tMinNum = 0;
            Int128 tMinDen = 1;
            Int128 tMaxNum = 1;
            Int128 tMaxDen = 1;
            if (!ClipAxis(x0, x1, cellMinX, cellMaxX, ref tMinNum, ref tMinDen, ref tMaxNum, ref tMaxDen))
            {
                return false;
            }

            if (!ClipAxis(z0, z1, cellMinZ, cellMaxZ, ref tMinNum, ref tMinDen, ref tMaxNum, ref tMaxDen))
            {
                return false;
            }

            // Strict inequality: point-only clipped contact is rejected.
            return CompareRational(tMinNum, tMinDen, tMaxNum, tMaxDen) < 0;
        }

        private static bool HavePositiveLengthXzContactInCell(
            int triL,
            int triR,
            ReadOnlySpan<int> vertexXcm,
            ReadOnlySpan<int> vertexZcm,
            ReadOnlySpan<int> triA,
            ReadOnlySpan<int> triB,
            ReadOnlySpan<int> triC,
            int cellMinX,
            int cellMaxX,
            int cellMinZ,
            int cellMaxZ)
        {
            int la = triA[triL];
            int lb = triB[triL];
            int lc = triC[triL];
            int ra = triA[triR];
            int rb = triB[triR];
            int rc = triC[triR];

            Int128 areaL = OrientXZ(
                vertexXcm[la], vertexZcm[la],
                vertexXcm[lb], vertexZcm[lb],
                vertexXcm[lc], vertexZcm[lc]);
            Int128 areaR = OrientXZ(
                vertexXcm[ra], vertexZcm[ra],
                vertexXcm[rb], vertexZcm[rb],
                vertexXcm[rc], vertexZcm[rc]);

            // Strict interior vertex of one triangle inside the other (within cell) => positive area contact.
            if (areaL != 0)
            {
                if (VertexStrictlyInsideTriangleInCell(
                        vertexXcm[ra], vertexZcm[ra],
                        vertexXcm[la], vertexZcm[la],
                        vertexXcm[lb], vertexZcm[lb],
                        vertexXcm[lc], vertexZcm[lc],
                        areaL, cellMinX, cellMaxX, cellMinZ, cellMaxZ) ||
                    VertexStrictlyInsideTriangleInCell(
                        vertexXcm[rb], vertexZcm[rb],
                        vertexXcm[la], vertexZcm[la],
                        vertexXcm[lb], vertexZcm[lb],
                        vertexXcm[lc], vertexZcm[lc],
                        areaL, cellMinX, cellMaxX, cellMinZ, cellMaxZ) ||
                    VertexStrictlyInsideTriangleInCell(
                        vertexXcm[rc], vertexZcm[rc],
                        vertexXcm[la], vertexZcm[la],
                        vertexXcm[lb], vertexZcm[lb],
                        vertexXcm[lc], vertexZcm[lc],
                        areaL, cellMinX, cellMaxX, cellMinZ, cellMaxZ))
                {
                    return true;
                }
            }

            if (areaR != 0)
            {
                if (VertexStrictlyInsideTriangleInCell(
                        vertexXcm[la], vertexZcm[la],
                        vertexXcm[ra], vertexZcm[ra],
                        vertexXcm[rb], vertexZcm[rb],
                        vertexXcm[rc], vertexZcm[rc],
                        areaR, cellMinX, cellMaxX, cellMinZ, cellMaxZ) ||
                    VertexStrictlyInsideTriangleInCell(
                        vertexXcm[lb], vertexZcm[lb],
                        vertexXcm[ra], vertexZcm[ra],
                        vertexXcm[rb], vertexZcm[rb],
                        vertexXcm[rc], vertexZcm[rc],
                        areaR, cellMinX, cellMaxX, cellMinZ, cellMaxZ) ||
                    VertexStrictlyInsideTriangleInCell(
                        vertexXcm[lc], vertexZcm[lc],
                        vertexXcm[ra], vertexZcm[ra],
                        vertexXcm[rb], vertexZcm[rb],
                        vertexXcm[rc], vertexZcm[rc],
                        areaR, cellMinX, cellMaxX, cellMinZ, cellMaxZ))
                {
                    return true;
                }
            }

            // Positive-length collinear overlap or proper edge crossing inside the closed cell.
            return EdgesHavePositiveContactInCell(
                       vertexXcm[la], vertexZcm[la], vertexXcm[lb], vertexZcm[lb],
                       vertexXcm[ra], vertexZcm[ra], vertexXcm[rb], vertexZcm[rb],
                       cellMinX, cellMaxX, cellMinZ, cellMaxZ) ||
                   EdgesHavePositiveContactInCell(
                       vertexXcm[la], vertexZcm[la], vertexXcm[lb], vertexZcm[lb],
                       vertexXcm[rb], vertexZcm[rb], vertexXcm[rc], vertexZcm[rc],
                       cellMinX, cellMaxX, cellMinZ, cellMaxZ) ||
                   EdgesHavePositiveContactInCell(
                       vertexXcm[la], vertexZcm[la], vertexXcm[lb], vertexZcm[lb],
                       vertexXcm[rc], vertexZcm[rc], vertexXcm[ra], vertexZcm[ra],
                       cellMinX, cellMaxX, cellMinZ, cellMaxZ) ||
                   EdgesHavePositiveContactInCell(
                       vertexXcm[lb], vertexZcm[lb], vertexXcm[lc], vertexZcm[lc],
                       vertexXcm[ra], vertexZcm[ra], vertexXcm[rb], vertexZcm[rb],
                       cellMinX, cellMaxX, cellMinZ, cellMaxZ) ||
                   EdgesHavePositiveContactInCell(
                       vertexXcm[lb], vertexZcm[lb], vertexXcm[lc], vertexZcm[lc],
                       vertexXcm[rb], vertexZcm[rb], vertexXcm[rc], vertexZcm[rc],
                       cellMinX, cellMaxX, cellMinZ, cellMaxZ) ||
                   EdgesHavePositiveContactInCell(
                       vertexXcm[lb], vertexZcm[lb], vertexXcm[lc], vertexZcm[lc],
                       vertexXcm[rc], vertexZcm[rc], vertexXcm[ra], vertexZcm[ra],
                       cellMinX, cellMaxX, cellMinZ, cellMaxZ) ||
                   EdgesHavePositiveContactInCell(
                       vertexXcm[lc], vertexZcm[lc], vertexXcm[la], vertexZcm[la],
                       vertexXcm[ra], vertexZcm[ra], vertexXcm[rb], vertexZcm[rb],
                       cellMinX, cellMaxX, cellMinZ, cellMaxZ) ||
                   EdgesHavePositiveContactInCell(
                       vertexXcm[lc], vertexZcm[lc], vertexXcm[la], vertexZcm[la],
                       vertexXcm[rb], vertexZcm[rb], vertexXcm[rc], vertexZcm[rc],
                       cellMinX, cellMaxX, cellMinZ, cellMaxZ) ||
                   EdgesHavePositiveContactInCell(
                       vertexXcm[lc], vertexZcm[lc], vertexXcm[la], vertexZcm[la],
                       vertexXcm[rc], vertexZcm[rc], vertexXcm[ra], vertexZcm[ra],
                       cellMinX, cellMaxX, cellMinZ, cellMaxZ);
        }

        private static bool VertexStrictlyInsideTriangleInCell(
            int px,
            int pz,
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz,
            Int128 xzArea,
            int cellMinX,
            int cellMaxX,
            int cellMinZ,
            int cellMaxZ)
        {
            if (px < cellMinX || px > cellMaxX || pz < cellMinZ || pz > cellMaxZ)
            {
                return false;
            }

            Int128 o1 = OrientXZ(ax, az, bx, bz, px, pz);
            Int128 o2 = OrientXZ(bx, bz, cx, cz, px, pz);
            Int128 o3 = OrientXZ(cx, cz, ax, az, px, pz);
            if (xzArea > 0)
            {
                return o1 > 0 && o2 > 0 && o3 > 0;
            }

            return o1 < 0 && o2 < 0 && o3 < 0;
        }

        private static bool EdgesHavePositiveContactInCell(
            int ax, int az, int bx, int bz,
            int cx, int cz, int dx, int dz,
            int cellMinX, int cellMaxX, int cellMinZ, int cellMaxZ)
        {
            Int128 abx = (Int128)bx - ax;
            Int128 abz = (Int128)bz - az;
            Int128 cdx = (Int128)dx - cx;
            Int128 cdz = (Int128)dz - cz;
            Int128 acx = (Int128)cx - ax;
            Int128 acz = (Int128)cz - az;

            Int128 cross = (abx * cdz) - (abz * cdx);
            if (cross == 0)
            {
                // Collinear: require positive-length overlap of both segments clipped to the cell.
                if (!CollinearOverlapOnLine(ax, az, bx, bz, cx, cz, dx, dz))
                {
                    return false;
                }

                return CollinearSegmentsHavePositiveOverlapInCell(
                    ax, az, bx, bz, cx, cz, dx, dz,
                    cellMinX, cellMaxX, cellMinZ, cellMaxZ);
            }

            // Proper intersection in open parameter interval (0,1) for both segments.
            Int128 tNum = (acx * cdz) - (acz * cdx);
            Int128 uNum = (acx * abz) - (acz * abx);
            if (cross < 0)
            {
                cross = -cross;
                tNum = -tNum;
                uNum = -uNum;
            }

            if (tNum <= 0 || tNum >= cross || uNum <= 0 || uNum >= cross)
            {
                return false;
            }

            // Intersection point must lie in the closed cell.
            Int128 ixNumer = ((Int128)ax * cross) + (abx * tNum);
            Int128 izNumer = ((Int128)az * cross) + (abz * tNum);
            return CompareRational(ixNumer, cross, cellMinX, 1) >= 0 &&
                   CompareRational(ixNumer, cross, cellMaxX, 1) <= 0 &&
                   CompareRational(izNumer, cross, cellMinZ, 1) >= 0 &&
                   CompareRational(izNumer, cross, cellMaxZ, 1) <= 0;
        }

        private static bool CollinearOverlapOnLine(
            int ax, int az, int bx, int bz,
            int cx, int cz, int dx, int dz)
        {
            // A,B,C collinear and A,B,D collinear.
            Int128 abx = (Int128)bx - ax;
            Int128 abz = (Int128)bz - az;
            Int128 acx = (Int128)cx - ax;
            Int128 acz = (Int128)cz - az;
            Int128 adx = (Int128)dx - ax;
            Int128 adz = (Int128)dz - az;
            return ((abx * acz) - (abz * acx)) == 0 &&
                   ((abx * adz) - (abz * adx)) == 0;
        }

        private static bool CollinearSegmentsHavePositiveOverlapInCell(
            int ax, int az, int bx, int bz,
            int cx, int cz, int dx, int dz,
            int cellMinX, int cellMaxX, int cellMinZ, int cellMaxZ)
        {
            // Project onto the dominant axis for exact 1D overlap, then require clipped overlap length > 0.
            long abx = (long)bx - ax;
            long abz = (long)bz - az;
            bool useX = System.Math.Abs(abx) >= System.Math.Abs(abz);
            long a0 = useX ? ax : az;
            long a1 = useX ? bx : bz;
            long b0 = useX ? cx : cz;
            long b1 = useX ? dx : dz;
            if (a0 > a1)
            {
                long tmp = a0;
                a0 = a1;
                a1 = tmp;
            }

            if (b0 > b1)
            {
                long tmp = b0;
                b0 = b1;
                b1 = tmp;
            }

            long overlapLo = a0 > b0 ? a0 : b0;
            long overlapHi = a1 < b1 ? a1 : b1;
            if (overlapHi <= overlapLo)
            {
                return false;
            }

            // Map the 1D overlap interval back through either endpoint pair and clip to cell.
            // Sufficient exact check: the overlapping parameter range on AB clipped to cell has positive length.
            return SegmentHasPositiveProjectedLengthInClosedCell(
                       ax, az, bx, bz, cellMinX, cellMaxX, cellMinZ, cellMaxZ) &&
                   SegmentHasPositiveProjectedLengthInClosedCell(
                       cx, cz, dx, dz, cellMinX, cellMaxX, cellMinZ, cellMaxZ) &&
                   OneDIntervalsOverlapPositivelyInsideCellProjection(
                       ax, az, bx, bz, cx, cz, dx, dz,
                       cellMinX, cellMaxX, cellMinZ, cellMaxZ,
                       useX);
        }

        private static bool OneDIntervalsOverlapPositivelyInsideCellProjection(
            int ax, int az, int bx, int bz,
            int cx, int cz, int dx, int dz,
            int cellMinX, int cellMaxX, int cellMinZ, int cellMaxZ,
            bool useX)
        {
            // Clip each segment to cell, then require positive 1D overlap of clipped ranges.
            if (!TryClipSegmentToCell(ax, az, bx, bz, cellMinX, cellMaxX, cellMinZ, cellMaxZ, out int ax2, out int az2, out int bx2, out int bz2))
            {
                return false;
            }

            if (!TryClipSegmentToCell(cx, cz, dx, dz, cellMinX, cellMaxX, cellMinZ, cellMaxZ, out int cx2, out int cz2, out int dx2, out int dz2))
            {
                return false;
            }

            long a0 = useX ? ax2 : az2;
            long a1 = useX ? bx2 : bz2;
            long b0 = useX ? cx2 : cz2;
            long b1 = useX ? dx2 : dz2;
            if (a0 > a1)
            {
                long tmp = a0;
                a0 = a1;
                a1 = tmp;
            }

            if (b0 > b1)
            {
                long tmp = b0;
                b0 = b1;
                b1 = tmp;
            }

            long lo = a0 > b0 ? a0 : b0;
            long hi = a1 < b1 ? a1 : b1;
            return hi > lo;
        }

        private static bool TryClipSegmentToCell(
            int x0, int z0, int x1, int z1,
            int cellMinX, int cellMaxX, int cellMinZ, int cellMaxZ,
            out int ox0, out int oz0, out int ox1, out int oz1)
        {
            Int128 tMinNum = 0;
            Int128 tMinDen = 1;
            Int128 tMaxNum = 1;
            Int128 tMaxDen = 1;
            if (!ClipAxis(x0, x1, cellMinX, cellMaxX, ref tMinNum, ref tMinDen, ref tMaxNum, ref tMaxDen) ||
                !ClipAxis(z0, z1, cellMinZ, cellMaxZ, ref tMinNum, ref tMinDen, ref tMaxNum, ref tMaxDen) ||
                CompareRational(tMinNum, tMinDen, tMaxNum, tMaxDen) > 0)
            {
                ox0 = 0;
                oz0 = 0;
                ox1 = 0;
                oz1 = 0;
                return false;
            }

            // Evaluate endpoints with floor for min-ish and ceil for max-ish is unnecessary for 1D overlap
            // when we only need positive length; use exact rational compare path via integer lerp at t=0/1 extremes.
            // For clipped endpoints that are rational, snap to floor of each coordinate for conservative contact:
            // positive-length 1D overlap of floored endpoints can miss a thin rational overlap, so use
            // the parameter interval length instead: tMin < tMax already proven for positive projected length.
            if (CompareRational(tMinNum, tMinDen, tMaxNum, tMaxDen) >= 0)
            {
                ox0 = 0;
                oz0 = 0;
                ox1 = 0;
                oz1 = 0;
                return false;
            }

            ox0 = LerpInt(x0, x1, tMinNum, tMinDen);
            oz0 = LerpInt(z0, z1, tMinNum, tMinDen);
            ox1 = LerpInt(x0, x1, tMaxNum, tMaxDen);
            oz1 = LerpInt(z0, z1, tMaxNum, tMaxDen);
            return true;
        }

        private static int LerpInt(int a0, int a1, Int128 tNum, Int128 tDen)
        {
            if (tDen == 0)
            {
                return a0;
            }

            Int128 numer = ((Int128)a0 * tDen) + ((((Int128)a1 - a0) * tNum));
            // Nearest toward -inf for stable interval endpoints is fine for overlap length checks.
            return FloorDivInt128(numer, tDen);
        }

        private static bool YIntervalsWithinTolerance(
            int aMin,
            int aMax,
            int bMin,
            int bMax,
            int tolerance)
        {
            long aLo = (long)aMin - tolerance;
            long aHi = (long)aMax + tolerance;
            return aHi >= bMin && (long)bMax >= aLo;
        }

        private static Int128 OrientXZ(int ax, int az, int bx, int bz, int cx, int cz)
            => (((Int128)bx - ax) * ((Int128)cz - az)) - (((Int128)cx - ax) * ((Int128)bz - az));

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

        private static int Find(int span, Span<int> parent)
        {
            int root = span;
            while (parent[root] != root)
            {
                root = parent[root];
            }

            while (parent[span] != root)
            {
                int next = parent[span];
                parent[span] = root;
                span = next;
            }

            return root;
        }

        private static void Union(int left, int right, Span<int> parent, Span<int> rank, Span<int> componentMin)
        {
            int rootL = Find(left, parent);
            int rootR = Find(right, parent);
            if (rootL == rootR)
            {
                return;
            }

            if (rank[rootL] < rank[rootR])
            {
                int tmp = rootL;
                rootL = rootR;
                rootR = tmp;
            }

            parent[rootR] = rootL;
            if (componentMin[rootR] < componentMin[rootL])
            {
                componentMin[rootL] = componentMin[rootR];
            }

            if (rank[rootL] == rank[rootR])
            {
                rank[rootL]++;
            }
        }
    }
}
