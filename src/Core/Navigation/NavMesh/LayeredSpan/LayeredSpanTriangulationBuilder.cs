using System;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Surface;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Stateless deterministic integer constrained triangulation over published layered-span contour charts.
    /// Per chart: hole bridging, ear clipping, Lawson flips, height sampling, adjacency, and border portals.
    /// Success-path Build after scratch warmup allocates 0 managed bytes.
    /// </summary>
    public static class LayeredSpanTriangulationBuilder
    {
        private const byte ConstrainedEdgeFlagContour = 1;
        private const byte ConstrainedEdgeFlagBridge = 2;

        public static void Build(
            NavTriangleSurfaceSnapshot surface,
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walkability,
            LayeredSpanSurfaceSheetScratch sheets,
            LayeredSpanWalkLinkScratch links,
            LayeredSpanRadiusFieldScratch radius,
            LayeredSpanRegionScratch regions,
            LayeredSpanContourScratch contours,
            in LayeredSpanRasterGridSpec grid,
            in LayeredSpanTriangulationSpec spec,
            LayeredSpanTriangulationScratch output)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (walkability == null) throw new ArgumentNullException(nameof(walkability));
            if (sheets == null) throw new ArgumentNullException(nameof(sheets));
            if (links == null) throw new ArgumentNullException(nameof(links));
            if (radius == null) throw new ArgumentNullException(nameof(radius));
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            if (contours == null) throw new ArgumentNullException(nameof(contours));
            if (output == null) throw new ArgumentNullException(nameof(output));

            output.Reset();

            ValidateProvenance(raw, walkability, sheets, links, radius, regions, contours);

            int columnCount = raw.ColumnCount;
            int spanCount = raw.SpanCount;
            if (columnCount != grid.ColumnCount ||
                columnCount != walkability.ColumnCount ||
                columnCount != sheets.ColumnCount ||
                spanCount != walkability.ClassifiedSpanCount ||
                spanCount != sheets.SpanCount ||
                spanCount != radius.SpanCount ||
                spanCount != regions.SpanCount)
            {
                throw new InvalidOperationException(
                    "LayeredSpanTriangulationBuilder requires consistent column/span counts across the chain and raster grid.");
            }

            output.Prepare();

            int chartCount = contours.ChartCount;
            if (chartCount == 0)
            {
                output.Commit(raw, walkability, sheets, links, radius, regions, contours, surface);
                return;
            }

            bool hasAnyContourHole = HasContourHole(contours.RingKinds);

            ReadOnlySpan<int> contourX = contours.VertexXcm;
            ReadOnlySpan<int> contourZ = contours.VertexZcm;
            ReadOnlySpan<int> contourSpans = contours.VertexSourceSpanIndices;
            ReadOnlySpan<int> ringOffsets = contours.RingOffsets;
            ReadOnlySpan<LayeredSpanContourRingKind> ringKinds = contours.RingKinds;
            ReadOnlySpan<int> chartRingOffsets = contours.ChartRingOffsets;
            ReadOnlySpan<int> chartRegionIds = contours.ChartRegionIds;
            ReadOnlySpan<byte> chartAreaIds = contours.ChartAreaIds;
            ReadOnlySpan<int> spanTriangleIndices = raw.SpanTriangleIndices;
            ReadOnlySpan<int> spanClearance = radius.SpanClearanceCm;
            ReadOnlySpan<int> columnSpanOffsets = raw.ColumnSpanOffsets;

            Span<int> polyX = output.MutablePolyXcm;
            Span<int> polyZ = output.MutablePolyZcm;
            Span<int> polySpan = output.MutablePolySourceSpan;
            Span<int> polyContourVtx = output.MutablePolyContourVertex;
            Span<int> polyNext = output.MutablePolyNext;
            Span<int> polyPrev = output.MutablePolyPrev;
            Span<byte> polyActive = output.MutablePolyActive;
            Span<byte> polyFromHole = output.MutablePolyFromHole;

            Span<int> triA = output.MutableTriA;
            Span<int> triB = output.MutableTriB;
            Span<int> triC = output.MutableTriC;
            Span<int> triChartIds = output.MutableTriChartIds;
            Span<int> triRegionIds = output.MutableTriRegionIds;
            Span<byte> triAreaIds = output.MutableTriAreaIds;

            Span<int> vtxX = output.MutableVertexXcm;
            Span<int> vtxY = output.MutableVertexYcm;
            Span<int> vtxZ = output.MutableVertexZcm;
            Span<int> vtxChart = output.MutableVertexChartIds;
            Span<int> vtxSpan = output.MutableVertexSourceSpanIndices;

            Span<int> cEdgeA = output.MutableConstrainedEdgeA;
            Span<int> cEdgeB = output.MutableConstrainedEdgeB;
            Span<byte> cEdgeFlags = output.MutableConstrainedEdgeFlags;
            Span<int> markA = output.MutableConstraintMarkA;
            Span<int> markB = output.MutableConstraintMarkB;
            Span<byte> tempConstraintFlags = output.MutableTempConstraintFlags;
            Span<int> ringWorkOrder = output.MutableRingWorkOrder;
            Span<int> ringOwnerOuter = output.MutableRingOwnerOuter;

            int globalTriCount = 0;
            int globalVtxCount = 0;
            int globalCEdgeCount = 0;

            for (int chart = 0; chart < chartCount; chart++)
            {
                int ringStart = chartRingOffsets[chart];
                int ringEnd = chartRingOffsets[chart + 1];
                if (ringStart >= ringEnd)
                {
                    Fail(output, $"LayeredSpanTriangulationBuilder chart {chart} has no rings.");
                }

                int chartRingCount = ringEnd - ringStart;
                EnsureRingWorkCapacity(output, chartRingCount);

                AssignHoleOwnersAndOrderOuters(
                    chart,
                    ringStart,
                    ringEnd,
                    ringOffsets,
                    ringKinds,
                    contourX,
                    contourZ,
                    ringWorkOrder,
                    ringOwnerOuter,
                    out int outerCount,
                    output);

                // ringWorkOrder[0..outerCount) holds outer rings in deterministic order.
                // ringOwnerOuter[ring - ringStart] holds owning outer for each hole (-1 for outers).
                for (int oi = 0; oi < outerCount; oi++)
                {
                    int outerRing = ringWorkOrder[oi];
                    int outerStart = ringOffsets[outerRing];
                    int outerEnd = ringOffsets[outerRing + 1];
                    int outerVertexCount = outerEnd - outerStart;
                    if (outerVertexCount < 3)
                    {
                        Fail(output, $"LayeredSpanTriangulationBuilder chart {chart} outer ring has fewer than 3 vertices.");
                    }

                    int polyCount = outerVertexCount;
                    EnsurePolyCapacity(output, polyCount);

                    for (int i = 0; i < outerVertexCount; i++)
                    {
                        int ci = outerStart + i;
                        polyX[i] = contourX[ci];
                        polyZ[i] = contourZ[ci];
                        polySpan[i] = contourSpans[ci];
                        polyContourVtx[i] = ci;
                        polyFromHole[i] = 0;
                        polyActive[i] = 1;
                        polyNext[i] = i + 1 == outerVertexCount ? 0 : i + 1;
                        polyPrev[i] = i == 0 ? outerVertexCount - 1 : i - 1;
                    }

                    int componentConstraintCount = 0;
                    RecordPolyConstraints(
                        outerVertexCount,
                        0,
                        markA,
                        markB,
                        tempConstraintFlags,
                        ref componentConstraintCount,
                        ConstrainedEdgeFlagContour,
                        output);

                    // Collect owned holes into the high half of ringWorkOrder, then sort them.
                    int ownedHoleCount = 0;
                    for (int r = ringStart; r < ringEnd; r++)
                    {
                        if (ringKinds[r] != LayeredSpanContourRingKind.Hole)
                        {
                            continue;
                        }

                        if (ringOwnerOuter[r - ringStart] != outerRing)
                        {
                            continue;
                        }

                        if (outerCount + ownedHoleCount >= output.RingWorkCapacity)
                        {
                            Fail(
                                output,
                                $"LayeredSpanTriangulationScratch.ringWorkCapacity ({output.RingWorkCapacity}); required {outerCount + ownedHoleCount + 1}.");
                        }

                        ringWorkOrder[outerCount + ownedHoleCount] = r;
                        ownedHoleCount++;
                    }

                    if (ownedHoleCount > 0)
                    {
                        SortHoleRingsInPlace(
                            ringWorkOrder.Slice(outerCount, ownedHoleCount),
                            ringOffsets,
                            contourX,
                            contourZ);
                    }

                    for (int hi = 0; hi < ownedHoleCount; hi++)
                    {
                        int holeRing = ringWorkOrder[outerCount + hi];
                        int hStart = ringOffsets[holeRing];
                        int hEnd = ringOffsets[holeRing + 1];
                        int hCount = hEnd - hStart;
                        if (hCount < 3)
                        {
                            Fail(output, $"LayeredSpanTriangulationBuilder chart {chart} hole ring {holeRing} has fewer than 3 vertices.");
                        }

                        int holeBase = polyCount;
                        EnsurePolyCapacity(output, polyCount + hCount);
                        for (int i = 0; i < hCount; i++)
                        {
                            int ci = hStart + i;
                            int pi = holeBase + i;
                            polyX[pi] = contourX[ci];
                            polyZ[pi] = contourZ[ci];
                            polySpan[pi] = contourSpans[ci];
                            polyContourVtx[pi] = ci;
                            polyFromHole[pi] = 1;
                            polyActive[pi] = 0;
                            polyNext[pi] = pi + 1 == holeBase + hCount ? holeBase : pi + 1;
                            polyPrev[pi] = pi == holeBase ? holeBase + hCount - 1 : pi - 1;
                        }

                        polyCount = holeBase + hCount;

                        RecordPolyConstraints(
                            hCount,
                            holeBase,
                            markA,
                            markB,
                            tempConstraintFlags,
                            ref componentConstraintCount,
                            ConstrainedEdgeFlagContour,
                            output);
                        StripFlatRingVertices(
                            holeBase,
                            hCount,
                            polyX,
                            polyZ,
                            polyNext,
                            polyPrev);
                        int liveHoleCount = CompactLiveRing(
                            holeBase,
                            hCount,
                            polyX,
                            polyZ,
                            polySpan,
                            polyContourVtx,
                            polyFromHole,
                            polyNext,
                            polyPrev,
                            polyActive);
                        if (liveHoleCount < 3)
                        {
                            Fail(output, "LayeredSpanTriangulationBuilder hole ring collapsed below 3 vertices after flat strip.");
                        }

                        polyCount = holeBase + liveHoleCount;

                        FindAndSpliceBridge(
                            ref polyCount,
                            holeBase,
                            liveHoleCount,
                            polyX,
                            polyZ,
                            polySpan,
                            polyContourVtx,
                            polyFromHole,
                            polyNext,
                            polyPrev,
                            polyActive,
                            markA,
                            markB,
                            tempConstraintFlags,
                            ref componentConstraintCount,
                            output);
                    }

                    output.SetPolyCount(polyCount);

                    int componentTriStart = globalTriCount;
                    int componentTriCount;
                    if (ownedHoleCount == 0 && !hasAnyContourHole)
                    {
                        EarClipChart(
                            chart,
                            outerRing,
                            ownedHoleCount,
                            in spec,
                            polyCount,
                            polyX,
                            polyZ,
                            polyNext,
                            polyPrev,
                            polyActive,
                            // Mid-segment flat strip is hole-free only. Bridged rings recover from a
                            // no-ear state by stripping a non-duplicate reverse spike.
                            true,
                            triA,
                            triB,
                            triC,
                            ref globalTriCount,
                            output);

                        componentTriCount = globalTriCount - componentTriStart;
                        if (componentTriCount == 0)
                        {
                            Fail(output, $"LayeredSpanTriangulationBuilder chart {chart} outer component produced no triangles.");
                        }

                        LawsonFlipChart(
                            componentTriStart,
                            componentTriCount,
                            polyCount,
                            polyX,
                            polyZ,
                            triA,
                            triB,
                            triC,
                            markA,
                            markB,
                            componentConstraintCount,
                            in spec,
                            output);

                        for (int t = componentTriStart; t < globalTriCount; t++)
                        {
                            triChartIds[t] = chart;
                            triRegionIds[t] = chartRegionIds[chart];
                            triAreaIds[t] = chartAreaIds[chart];
                        }
                    }
                    else
                    {
                        // Hole charts are published from the explicit walkable-cell cover below;
                        // contour vertices/constraints are still emitted here.
                        componentTriCount = 0;
                    }

                    PublishChartVertices(
                        chart,
                        componentTriStart,
                        componentTriCount,
                        polyCount,
                        polyX,
                        polyZ,
                        polySpan,
                        polyContourVtx,
                        triA,
                        triB,
                        triC,
                        markA,
                        markB,
                        componentConstraintCount,
                        vtxX,
                        vtxY,
                        vtxZ,
                        vtxChart,
                        vtxSpan,
                        output.MutableUniqueKeyX,
                        output.MutableUniqueKeyY,
                        output.MutableUniqueKeyZ,
                        output.MutableUniqueKeyChart,
                        output.MutableUniqueKeyIndex,
                        output.MutableContourLocalToPoly,
                        spanTriangleIndices,
                        surface,
                        in spec,
                        ref globalVtxCount,
                        output);

                    PublishChartConstrainedEdges(
                        componentConstraintCount,
                        markA,
                        markB,
                        tempConstraintFlags,
                        output.MutableContourLocalToPoly,
                        cEdgeA,
                        cEdgeB,
                        cEdgeFlags,
                        ref globalCEdgeCount,
                        output);
                }
            }

            // Hole-free charts use compact contour triangulation. Raster-hole charts use an
            // explicit walkable-cell mesh so the annulus stays connected without weakly-simple
            // bridge ears spanning the hole. The cell path is SoA-backed, height sampled, and 0GC.
            if (hasAnyContourHole)
            {
                BuildWalkableCellHoleMesh(
                    raw,
                    walkability,
                    sheets,
                    regions,
                    in grid,
                    in spec,
                    spanTriangleIndices,
                    surface,
                    polyX,
                    polyZ,
                    polySpan,
                    polyContourVtx,
                    triA,
                    triB,
                    triC,
                    triChartIds,
                    triRegionIds,
                    triAreaIds,
                    vtxX,
                    vtxY,
                    vtxZ,
                    vtxChart,
                    vtxSpan,
                    output.MutableUniqueKeyX,
                    output.MutableUniqueKeyY,
                    output.MutableUniqueKeyZ,
                    output.MutableUniqueKeyChart,
                    output.MutableUniqueKeyIndex,
                    output.MutableContourLocalToPoly,
                    ref globalTriCount,
                    ref globalVtxCount,
                    output);
            }

            output.SetVertexCount(globalVtxCount);
            output.SetTriangleCount(globalTriCount);
            output.SetConstrainedEdgeCount(globalCEdgeCount);

            SortTriangles(
                globalTriCount,
                triA,
                triB,
                triC,
                triChartIds,
                triRegionIds,
                triAreaIds,
                vtxX,
                vtxY,
                vtxZ);

            BuildAdjacency(
                globalTriCount,
                globalVtxCount,
                triA,
                triB,
                triC,
                triChartIds,
                vtxX,
                vtxY,
                vtxZ,
                contours,
                raw,
                in grid,
                output);

            BuildBorderPortals(
                surface,
                raw,
                walkability,
                links,
                regions,
                in grid,
                in spec,
                columnSpanOffsets,
                spanClearance,
                spanTriangleIndices,
                output);

            output.Commit(raw, walkability, sheets, links, radius, regions, contours, surface);
        }

        private static bool HasContourHole(ReadOnlySpan<LayeredSpanContourRingKind> ringKinds)
        {
            for (int i = 0; i < ringKinds.Length; i++)
            {
                if (ringKinds[i] == LayeredSpanContourRingKind.Hole)
                {
                    return true;
                }
            }

            return false;
        }

        private static void BuildWalkableCellHoleMesh(
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walkability,
            LayeredSpanSurfaceSheetScratch sheets,
            LayeredSpanRegionScratch regions,
            in LayeredSpanRasterGridSpec grid,
            in LayeredSpanTriangulationSpec spec,
            ReadOnlySpan<int> spanTriangleIndices,
            NavTriangleSurfaceSnapshot surface,
            Span<int> polyX,
            Span<int> polyZ,
            Span<int> polySpan,
            Span<int> polyContourVtx,
            Span<int> triA,
            Span<int> triB,
            Span<int> triC,
            Span<int> triChartIds,
            Span<int> triRegionIds,
            Span<byte> triAreaIds,
            Span<int> vtxX,
            Span<int> vtxY,
            Span<int> vtxZ,
            Span<int> vtxChart,
            Span<int> vtxSpan,
            Span<int> keyX,
            Span<int> keyY,
            Span<int> keyZ,
            Span<int> keyChart,
            Span<int> keyIndex,
            Span<int> polyToGlobal,
            ref int globalTriCount,
            ref int globalVtxCount,
            LayeredSpanTriangulationScratch output)
        {
            int keyCount = globalVtxCount;
            for (int i = 0; i < keyCount; i++)
            {
                keyX[i] = vtxX[i];
                keyY[i] = vtxY[i];
                keyZ[i] = vtxZ[i];
                keyChart[i] = 0;
                keyIndex[i] = i;
            }

            ReadOnlySpan<int> walkableIndices = walkability.WalkableSpanIndices;
            ReadOnlySpan<int> walkableOffsets = walkability.ColumnWalkableOffsets;
            ReadOnlySpan<int> walkableCounts = walkability.ColumnWalkableCounts;
            ReadOnlySpan<byte> spanAreaIds = raw.SpanAreaIds;
            ReadOnlySpan<int> spanSheetIds = sheets.SpanSheetIds;
            ReadOnlySpan<int> spanRegionIds = regions.SpanRegionIds;

            for (int column = 0; column < grid.ColumnCount; column++)
            {
                int columnX = column % grid.ColumnCountX;
                int columnZ = column / grid.ColumnCountX;
                int x0 = grid.ColumnMinXcm(columnX);
                int x1 = grid.ColumnMaxXcm(columnX);
                int z0 = grid.ColumnMinZcm(columnZ);
                int z1 = grid.ColumnMaxZcm(columnZ);
                if (x0 < spec.TargetMinXcm || x1 > spec.TargetMaxXcm ||
                    z0 < spec.TargetMinZcm || z1 > spec.TargetMaxZcm)
                {
                    continue;
                }

                int walkStart = walkableOffsets[column];
                int walkEnd = walkStart + walkableCounts[column];
                for (int wi = walkStart; wi < walkEnd; wi++)
                {
                    int span = walkableIndices[wi];
                    int sheetId = spanSheetIds[span];
                    bool duplicateSheet = false;
                    for (int prior = walkStart; prior < wi; prior++)
                    {
                        if (spanSheetIds[walkableIndices[prior]] == sheetId)
                        {
                            duplicateSheet = true;
                            break;
                        }
                    }

                    if (duplicateSheet)
                    {
                        continue;
                    }

                    polyX[0] = x0;
                    polyZ[0] = z0;
                    polySpan[0] = span;
                    polyContourVtx[0] = -1;
                    polyX[1] = x1;
                    polyZ[1] = z0;
                    polySpan[1] = span;
                    polyContourVtx[1] = -1;
                    polyX[2] = x1;
                    polyZ[2] = z1;
                    polySpan[2] = span;
                    polyContourVtx[2] = -1;
                    polyX[3] = x0;
                    polyZ[3] = z1;
                    polySpan[3] = span;
                    polyContourVtx[3] = -1;

                    if (globalTriCount + 2 > output.TriangleCapacity)
                    {
                        Fail(
                            output,
                            $"LayeredSpanTriangulationScratch.triangleCapacity ({output.TriangleCapacity}); required {globalTriCount + 2}.");
                    }

                    polyToGlobal[0] = -1;
                    polyToGlobal[1] = -1;
                    polyToGlobal[2] = -1;
                    polyToGlobal[3] = -1;
                    int chartId = 0;
                    int regionId = spanRegionIds[span];
                    byte areaId = (byte)spanAreaIds[span];
                    int t0 = globalTriCount++;
                    int t1 = globalTriCount++;
                    triA[t0] = 0;
                    triB[t0] = 1;
                    triC[t0] = 2;
                    triA[t1] = 0;
                    triB[t1] = 2;
                    triC[t1] = 3;
                    triChartIds[t0] = chartId;
                    triChartIds[t1] = chartId;
                    triRegionIds[t0] = regionId;
                    triRegionIds[t1] = regionId;
                    triAreaIds[t0] = areaId;
                    triAreaIds[t1] = areaId;

                    int chartUnique = keyCount;
                    RemapCorner(0, 0, chartId, polyX, polyZ, polySpan, polyContourVtx, spanTriangleIndices, surface, in spec, polyToGlobal, keyX, keyY, keyZ, keyChart, keyIndex, ref chartUnique, vtxX, vtxY, vtxZ, vtxChart, vtxSpan, ref globalVtxCount, output);
                    RemapCorner(0, 1, chartId, polyX, polyZ, polySpan, polyContourVtx, spanTriangleIndices, surface, in spec, polyToGlobal, keyX, keyY, keyZ, keyChart, keyIndex, ref chartUnique, vtxX, vtxY, vtxZ, vtxChart, vtxSpan, ref globalVtxCount, output);
                    RemapCorner(0, 2, chartId, polyX, polyZ, polySpan, polyContourVtx, spanTriangleIndices, surface, in spec, polyToGlobal, keyX, keyY, keyZ, keyChart, keyIndex, ref chartUnique, vtxX, vtxY, vtxZ, vtxChart, vtxSpan, ref globalVtxCount, output);
                    RemapCorner(0, 3, chartId, polyX, polyZ, polySpan, polyContourVtx, spanTriangleIndices, surface, in spec, polyToGlobal, keyX, keyY, keyZ, keyChart, keyIndex, ref chartUnique, vtxX, vtxY, vtxZ, vtxChart, vtxSpan, ref globalVtxCount, output);
                    keyCount = chartUnique;
                    triA[t0] = polyToGlobal[0];
                    triB[t0] = polyToGlobal[1];
                    triC[t0] = polyToGlobal[2];
                    triA[t1] = polyToGlobal[0];
                    triB[t1] = polyToGlobal[2];
                    triC[t1] = polyToGlobal[3];
                }
            }
        }

        private static void ValidateProvenance(
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walkability,
            LayeredSpanSurfaceSheetScratch sheets,
            LayeredSpanWalkLinkScratch links,
            LayeredSpanRadiusFieldScratch radius,
            LayeredSpanRegionScratch regions,
            LayeredSpanContourScratch contours)
        {
            if (!raw.HasPublishedContent)
            {
                throw new InvalidOperationException(
                    "LayeredSpanTriangulationBuilder requires published raw scratch content.");
            }

            if (!walkability.WasBuiltFrom(raw))
            {
                throw new InvalidOperationException(
                    "LayeredSpanTriangulationBuilder requires walkability output that matches the raw scratch identity and content generation.");
            }

            if (!sheets.WasBuiltFrom(raw))
            {
                throw new InvalidOperationException(
                    "LayeredSpanTriangulationBuilder requires surface-sheet output that matches the raw scratch identity and content generation.");
            }

            if (!links.WasBuiltFrom(raw, walkability))
            {
                throw new InvalidOperationException(
                    "LayeredSpanTriangulationBuilder requires walk-link output that matches the raw/walkability scratch identity and content generation.");
            }

            if (!radius.WasBuiltFrom(raw, walkability, sheets, links))
            {
                throw new InvalidOperationException(
                    "LayeredSpanTriangulationBuilder requires radius-field output that matches the raw/walkability/sheets/links scratch identity and content generation.");
            }

            if (!regions.WasBuiltFrom(raw, walkability, sheets, links, radius))
            {
                throw new InvalidOperationException(
                    "LayeredSpanTriangulationBuilder requires region output that matches the raw/walkability/sheets/links/radius scratch identity and content generation.");
            }

            if (!contours.WasBuiltFrom(raw, walkability, sheets, links, radius, regions))
            {
                throw new InvalidOperationException(
                    "LayeredSpanTriangulationBuilder requires contour output that matches the full upstream scratch identity and content generation.");
            }
        }

        private static void EnsureRingWorkCapacity(LayeredSpanTriangulationScratch output, int required)
        {
            if (required > output.RingWorkCapacity)
            {
                Fail(
                    output,
                    $"LayeredSpanTriangulationScratch.ringWorkCapacity ({output.RingWorkCapacity}); required {required}.");
            }
        }

        private static void AssignHoleOwnersAndOrderOuters(
            int chart,
            int ringStart,
            int ringEnd,
            ReadOnlySpan<int> ringOffsets,
            ReadOnlySpan<LayeredSpanContourRingKind> ringKinds,
            ReadOnlySpan<int> x,
            ReadOnlySpan<int> z,
            Span<int> ringWorkOrder,
            Span<int> ringOwnerOuter,
            out int outerCount,
            LayeredSpanTriangulationScratch output)
        {
            outerCount = 0;
            int chartRingCount = ringEnd - ringStart;
            for (int i = 0; i < chartRingCount; i++)
            {
                ringOwnerOuter[i] = -1;
            }

            for (int r = ringStart; r < ringEnd; r++)
            {
                LayeredSpanContourRingKind kind = ringKinds[r];
                if (kind == LayeredSpanContourRingKind.Outer)
                {
                    ringWorkOrder[outerCount++] = r;
                }
                else if (kind != LayeredSpanContourRingKind.Hole)
                {
                    Fail(output, $"LayeredSpanTriangulationBuilder chart {chart} has unknown ring kind.");
                }
            }

            if (outerCount == 0)
            {
                Fail(output, $"LayeredSpanTriangulationBuilder chart {chart} has no outer ring.");
            }

            SortOuterRingsInPlace(ringWorkOrder.Slice(0, outerCount), ringOffsets, x, z);

            for (int h = ringStart; h < ringEnd; h++)
            {
                if (ringKinds[h] != LayeredSpanContourRingKind.Hole)
                {
                    continue;
                }

                int hStart = ringOffsets[h];
                int hEnd = ringOffsets[h + 1];
                int hCount = hEnd - hStart;
                int containing = -1;
                for (int oi = 0; oi < outerCount; oi++)
                {
                    int o = ringWorkOrder[oi];
                    int oStart = ringOffsets[o];
                    int oEnd = ringOffsets[o + 1];
                    if (HoleStrictlyInsideOuter(x, z, hStart, hCount, oStart, oEnd - oStart))
                    {
                        if (containing >= 0)
                        {
                            Fail(
                                output,
                                $"LayeredSpanTriangulationBuilder chart {chart} hole has ambiguous containing outer ring.");
                        }

                        containing = o;
                    }
                }

                if (containing < 0)
                {
                    Fail(output, $"LayeredSpanTriangulationBuilder chart {chart} hole has no containing outer ring.");
                }

                ringOwnerOuter[h - ringStart] = containing;
            }
        }

        private static bool HoleStrictlyInsideOuter(
            ReadOnlySpan<int> x,
            ReadOnlySpan<int> z,
            int hStart,
            int hCount,
            int oStart,
            int oCount)
        {
            for (int i = 0; i < hCount; i++)
            {
                if (!PointInRingStrict(x[hStart + i], z[hStart + i], x, z, oStart, oCount))
                {
                    return false;
                }
            }

            return true;
        }

        private static void SortOuterRingsInPlace(
            Span<int> order,
            ReadOnlySpan<int> ringOffsets,
            ReadOnlySpan<int> x,
            ReadOnlySpan<int> z)
        {
            for (int i = 1; i < order.Length; i++)
            {
                int keyRing = order[i];
                int kStart = ringOffsets[keyRing];
                int kEnd = ringOffsets[keyRing + 1];
                int kMin = MinVertexIndex(x, z, kStart, kEnd - kStart);
                int j = i - 1;
                while (j >= 0)
                {
                    int otherRing = order[j];
                    int oStart = ringOffsets[otherRing];
                    int oEnd = ringOffsets[otherRing + 1];
                    int oMin = MinVertexIndex(x, z, oStart, oEnd - oStart);
                    int cmp = CompareVertexKey(x[kMin], z[kMin], x[oMin], z[oMin]);
                    if (cmp > 0 || (cmp == 0 && keyRing > otherRing))
                    {
                        order[j + 1] = order[j];
                        j--;
                    }
                    else
                    {
                        break;
                    }
                }

                order[j + 1] = keyRing;
            }
        }

        private static void SortHoleRingsInPlace(
            Span<int> order,
            ReadOnlySpan<int> ringOffsets,
            ReadOnlySpan<int> x,
            ReadOnlySpan<int> z)
        {
            for (int i = 1; i < order.Length; i++)
            {
                int keyRing = order[i];
                int kStart = ringOffsets[keyRing];
                int kEnd = ringOffsets[keyRing + 1];
                int kMin = MinVertexIndex(x, z, kStart, kEnd - kStart);
                int j = i - 1;
                while (j >= 0)
                {
                    int otherRing = order[j];
                    int oStart = ringOffsets[otherRing];
                    int oEnd = ringOffsets[otherRing + 1];
                    int oMin = MinVertexIndex(x, z, oStart, oEnd - oStart);
                    int cmp = CompareVertexKey(x[kMin], z[kMin], x[oMin], z[oMin]);
                    if (cmp > 0 || (cmp == 0 && keyRing > otherRing))
                    {
                        order[j + 1] = order[j];
                        j--;
                    }
                    else
                    {
                        break;
                    }
                }

                order[j + 1] = keyRing;
            }
        }

        private static int MinVertexIndex(ReadOnlySpan<int> x, ReadOnlySpan<int> z, int start, int count)
        {
            int best = start;
            for (int i = 1; i < count; i++)
            {
                int idx = start + i;
                if (x[idx] < x[best] || (x[idx] == x[best] && z[idx] < z[best]))
                {
                    best = idx;
                }
            }

            return best;
        }

        private static int CompareVertexKey(int ax, int az, int bx, int bz)
        {
            if (ax != bx)
            {
                return ax < bx ? -1 : 1;
            }

            if (az != bz)
            {
                return az < bz ? -1 : 1;
            }

            return 0;
        }

        private static void RecordPolyConstraints(
            int count,
            int indexOffset,
            Span<int> markA,
            Span<int> markB,
            Span<byte> markFlags,
            ref int markCount,
            byte flag,
            LayeredSpanTriangulationScratch output)
        {
            for (int i = 0; i < count; i++)
            {
                int a = indexOffset + i;
                int b = indexOffset + (i + 1 == count ? 0 : i + 1);
                if (markCount >= output.ConstrainedEdgeCapacity)
                {
                    Fail(
                        output,
                        $"LayeredSpanTriangulationScratch.constrainedEdgeCapacity ({output.ConstrainedEdgeCapacity}); required {markCount + 1}.");
                }

                if (markCount >= output.TemporaryConstraintFlagCapacity)
                {
                    Fail(
                        output,
                        $"LayeredSpanTriangulationScratch.temporaryConstraintFlagCapacity ({output.TemporaryConstraintFlagCapacity}); required {markCount + 1}.");
                }

                markA[markCount] = a;
                markB[markCount] = b;
                markFlags[markCount] = flag;
                markCount++;
            }
        }

        private static void FindAndSpliceBridge(
            ref int polyCount,
            int holeBase,
            int holeCount,
            Span<int> polyX,
            Span<int> polyZ,
            Span<int> polySpan,
            Span<int> polyContourVtx,
            Span<byte> polyFromHole,
            Span<int> polyNext,
            Span<int> polyPrev,
            Span<byte> polyActive,
            Span<int> markA,
            Span<int> markB,
            Span<byte> markFlags,
            ref int markCount,
            LayeredSpanTriangulationScratch output)
        {
            int polyCountBeforeHole = holeBase;
            int candidateCount = 0;
            Span<int> candHole = output.MutableBridgeHoleVertex;
            Span<int> candOuter = output.MutableBridgeOuterVertex;
            Span<long> candDist2 = output.MutableBridgeDist2;

            for (int h = 0; h < holeCount; h++)
            {
                int hi = holeBase + h;
                for (int o = 0; o < polyCountBeforeHole; o++)
                {
                    if (polyActive[o] == 0)
                    {
                        continue;
                    }

                    if (!BridgeVisible(o, hi, polyCount, polyX, polyZ, polyNext, polyPrev, polyActive))
                    {
                        continue;
                    }

                    if (candidateCount >= output.BridgeCandidateCapacity)
                    {
                        Fail(
                            output,
                            $"LayeredSpanTriangulationScratch.bridgeCandidateCapacity ({output.BridgeCandidateCapacity}); required {candidateCount + 1}.");
                    }

                    candHole[candidateCount] = hi;
                    candOuter[candidateCount] = o;
                    candDist2[candidateCount] = Dist2(polyX[o], polyZ[o], polyX[hi], polyZ[hi]);
                    candidateCount++;
                }
            }

            if (candidateCount == 0)
            {
                Fail(output, "LayeredSpanTriangulationBuilder bridge search found no visible candidate.");
            }

            long bestDist2 = long.MaxValue;
            int bestOuter = -1;
            int bestHole = -1;
            int bestCount = 0;
            for (int i = 0; i < candidateCount; i++)
            {
                long d2 = candDist2[i];
                int o = candOuter[i];
                int h = candHole[i];
                if (d2 < bestDist2 ||
                    (d2 == bestDist2 && (o < bestOuter || (o == bestOuter && h < bestHole))))
                {
                    bestDist2 = d2;
                    bestOuter = o;
                    bestHole = h;
                }
            }

            for (int i = 0; i < candidateCount; i++)
            {
                if (candDist2[i] == bestDist2 && candOuter[i] == bestOuter && candHole[i] == bestHole)
                {
                    bestCount++;
                }
            }

            if (bestCount != 1)
            {
                Fail(output, "LayeredSpanTriangulationBuilder bridge search found ambiguous equally-optimal candidates.");
            }

            EnsurePolyCapacity(output, polyCount + 1);
            int outerReturn = polyCount;
            polyX[outerReturn] = polyX[bestOuter];
            polyZ[outerReturn] = polyZ[bestOuter];
            polySpan[outerReturn] = polySpan[bestOuter];
            polyContourVtx[outerReturn] = polyContourVtx[bestOuter];
            polyFromHole[outerReturn] = 0;
            polyActive[outerReturn] = 1;
            polyCount++;

            ReverseRing(holeBase, holeCount, polyNext, polyPrev);
            for (int i = 0; i < holeCount; i++)
            {
                polyActive[holeBase + i] = 1;
            }

            int outerNext = polyNext[bestOuter];
            int holePrev = polyPrev[bestHole];
            polyNext[bestOuter] = bestHole;
            polyPrev[bestHole] = bestOuter;
            polyNext[holePrev] = outerReturn;
            polyPrev[outerReturn] = holePrev;
            polyNext[outerReturn] = outerNext;
            polyPrev[outerNext] = outerReturn;

            if (markCount + 2 > output.ConstrainedEdgeCapacity)
            {
                Fail(
                    output,
                    $"LayeredSpanTriangulationScratch.constrainedEdgeCapacity ({output.ConstrainedEdgeCapacity}); required {markCount + 2}.");
            }

            if (markCount + 2 > output.TemporaryConstraintFlagCapacity)
            {
                Fail(
                    output,
                    $"LayeredSpanTriangulationScratch.temporaryConstraintFlagCapacity ({output.TemporaryConstraintFlagCapacity}); required {markCount + 2}.");
            }

            markA[markCount] = bestOuter;
            markB[markCount] = bestHole;
            markFlags[markCount] = ConstrainedEdgeFlagBridge;
            markCount++;
            markA[markCount] = bestHole;
            markB[markCount] = outerReturn;
            markFlags[markCount] = ConstrainedEdgeFlagBridge;
            markCount++;
        }

        private static void ReverseRing(int start, int count, Span<int> polyNext, Span<int> polyPrev)
        {
            int cur = start;
            for (int i = 0; i < count; i++)
            {
                int n = polyNext[cur];
                int p = polyPrev[cur];
                polyNext[cur] = p;
                polyPrev[cur] = n;
                cur = p;
            }
        }

        private static bool BridgeVisible(
            int outerIdx,
            int holeIdx,
            int polyCount,
            Span<int> polyX,
            Span<int> polyZ,
            Span<int> polyNext,
            Span<int> polyPrev,
            Span<byte> polyActive)
        {
            int ox = polyX[outerIdx];
            int oz = polyZ[outerIdx];
            int hx = polyX[holeIdx];
            int hz = polyZ[holeIdx];

            // Exact open-segment midpoint via doubled Int128 coordinates (never scale/add world int in int).
            Int128 midX2 = (Int128)ox + hx;
            Int128 midZ2 = (Int128)oz + hz;
            if (!PointInActivePolygonStrictDoubled(
                    midX2,
                    midZ2,
                    polyCount,
                    polyX,
                    polyZ,
                    polyNext,
                    polyActive))
            {
                return false;
            }

            // Active polygon ignores the current hole; reject midpoints that lie inside that hole.
            if (PointInRingWalkStrictDoubled(midX2, midZ2, holeIdx, polyX, polyZ, polyNext))
            {
                return false;
            }

            for (int e = 0; e < polyCount; e++)
            {
                int en = polyNext[e];
                if (e == outerIdx || en == outerIdx || e == holeIdx || en == holeIdx)
                {
                    continue;
                }

                if (polyActive[e] == 0)
                {
                    if (!IsOnSameRing(e, holeIdx, polyNext))
                    {
                        continue;
                    }
                }

                if (SegmentsProperIntersect(ox, oz, hx, hz, polyX[e], polyZ[e], polyX[en], polyZ[en]))
                {
                    return false;
                }
            }

            for (int v = 0; v < polyCount; v++)
            {
                if (v == outerIdx || v == holeIdx)
                {
                    continue;
                }

                if (polyActive[v] == 0 && !IsOnSameRing(v, holeIdx, polyNext))
                {
                    continue;
                }

                if (PointOnSegmentInclusive(ox, oz, hx, hz, polyX[v], polyZ[v]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsOnSameRing(int vertex, int ringVertex, ReadOnlySpan<int> polyNext)
        {
            int cur = ringVertex;
            int guard = 0;
            int n = polyNext.Length;
            do
            {
                if (cur == vertex)
                {
                    return true;
                }

                cur = polyNext[cur];
                guard++;
            } while (cur != ringVertex && guard <= n);

            return false;
        }

        private static bool PointInRingWalkStrictDoubled(
            Int128 px2,
            Int128 pz2,
            int ringVertex,
            ReadOnlySpan<int> polyX,
            ReadOnlySpan<int> polyZ,
            ReadOnlySpan<int> polyNext)
        {
            bool inside = false;
            int cur = ringVertex;
            int guard = 0;
            int n = polyNext.Length;
            do
            {
                int j = polyNext[cur];
                Int128 xi2 = (Int128)polyX[cur] * 2;
                Int128 zi2 = (Int128)polyZ[cur] * 2;
                Int128 xj2 = (Int128)polyX[j] * 2;
                Int128 zj2 = (Int128)polyZ[j] * 2;
                if (PointOnSegmentInclusiveWide(xi2, zi2, xj2, zj2, px2, pz2))
                {
                    return true;
                }

                if ((zi2 > pz2) != (zj2 > pz2))
                {
                    Int128 lhs = (pz2 - zi2) * (xj2 - xi2);
                    Int128 rhs = (px2 - xi2) * (zj2 - zi2);
                    bool crossLeft = zj2 > zi2 ? lhs > rhs : lhs < rhs;
                    if (crossLeft)
                    {
                        inside = !inside;
                    }
                }

                cur = j;
                guard++;
            } while (cur != ringVertex && guard <= n);

            return inside;
        }

        private static bool PointInActivePolygonStrictDoubled(
            Int128 px2,
            Int128 pz2,
            int polyCount,
            ReadOnlySpan<int> polyX,
            ReadOnlySpan<int> polyZ,
            ReadOnlySpan<int> polyNext,
            ReadOnlySpan<byte> polyActive)
        {
            for (int i = 0; i < polyCount; i++)
            {
                if (polyActive[i] == 0)
                {
                    continue;
                }

                int j = polyNext[i];
                Int128 xi2 = (Int128)polyX[i] * 2;
                Int128 zi2 = (Int128)polyZ[i] * 2;
                Int128 xj2 = (Int128)polyX[j] * 2;
                Int128 zj2 = (Int128)polyZ[j] * 2;
                if (PointOnSegmentInclusiveWide(xi2, zi2, xj2, zj2, px2, pz2))
                {
                    return false;
                }
            }

            bool inside = false;
            for (int i = 0; i < polyCount; i++)
            {
                if (polyActive[i] == 0)
                {
                    continue;
                }

                int j = polyNext[i];
                Int128 xi2 = (Int128)polyX[i] * 2;
                Int128 zi2 = (Int128)polyZ[i] * 2;
                Int128 xj2 = (Int128)polyX[j] * 2;
                Int128 zj2 = (Int128)polyZ[j] * 2;
                if ((zi2 > pz2) != (zj2 > pz2))
                {
                    Int128 lhs = (pz2 - zi2) * (xj2 - xi2);
                    Int128 rhs = (px2 - xi2) * (zj2 - zi2);
                    bool crossLeft = zj2 > zi2 ? lhs > rhs : lhs < rhs;
                    if (crossLeft)
                    {
                        inside = !inside;
                    }
                }
            }

            return inside;
        }

        private static int CompactLiveRing(
            int rangeBase,
            int rangeCount,
            Span<int> polyX,
            Span<int> polyZ,
            Span<int> polySpan,
            Span<int> polyContourVtx,
            Span<byte> polyFromHole,
            Span<int> polyNext,
            Span<int> polyPrev,
            Span<byte> polyActive)
        {
            if (rangeCount <= 0)
            {
                return 0;
            }

            for (int i = 0; i < rangeCount; i++)
            {
                polyActive[rangeBase + i] = 0;
            }

            int start = -1;
            for (int i = 0; i < rangeCount; i++)
            {
                int candidate = rangeBase + i;
                int guard = 0;
                int cur = candidate;
                bool closed = false;
                do
                {
                    int n = polyNext[cur];
                    if ((uint)n < (uint)rangeBase || (uint)n >= (uint)(rangeBase + rangeCount))
                    {
                        break;
                    }

                    if (n == candidate)
                    {
                        closed = true;
                        break;
                    }

                    cur = n;
                    guard++;
                } while (guard <= rangeCount);

                if (closed)
                {
                    start = candidate;
                    break;
                }
            }

            if (start < 0)
            {
                return 0;
            }

            int tip = start;
            do
            {
                polyActive[tip] = 2; // live mark
                tip = polyNext[tip];
            } while (tip != start);

            int liveCount = 0;
            for (int i = 0; i < rangeCount; i++)
            {
                int src = rangeBase + i;
                if (polyActive[src] != 2)
                {
                    continue;
                }

                int dst = rangeBase + liveCount;
                if (src != dst)
                {
                    polyX[dst] = polyX[src];
                    polyZ[dst] = polyZ[src];
                    polySpan[dst] = polySpan[src];
                    polyContourVtx[dst] = polyContourVtx[src];
                    polyFromHole[dst] = polyFromHole[src];
                }

                polyActive[dst] = 0; // hole verts stay inactive until bridge
                liveCount++;
            }

            for (int i = 0; i < liveCount; i++)
            {
                int dst = rangeBase + i;
                polyNext[dst] = rangeBase + (i + 1 == liveCount ? 0 : i + 1);
                polyPrev[dst] = rangeBase + (i == 0 ? liveCount - 1 : i - 1);
            }

            return liveCount;
        }

        private static void StripFlatRingVertices(
            int rangeBase,
            int rangeCount,
            Span<int> polyX,
            Span<int> polyZ,
            Span<int> polyNext,
            Span<int> polyPrev)
        {
            // For inactive hole rings: walk next/prev only; unlink collinear verts from the ring.
            bool changed = true;
            while (changed)
            {
                changed = false;
                int guard = 0;
                int tip = rangeBase;
                int start = tip;
                do
                {
                    int prev = polyPrev[tip];
                    int next = polyNext[tip];
                    int following = next;
                    if ((polyX[tip] == polyX[prev] && polyZ[tip] == polyZ[prev]) ||
                        (polyX[tip] == polyX[next] && polyZ[tip] == polyZ[next]) ||
                        (polyX[prev] == polyX[next] && polyZ[prev] == polyZ[next]))
                    {
                        tip = following;
                    }
                    else if (Orient2Sign(polyX[prev], polyZ[prev], polyX[tip], polyZ[tip], polyX[next], polyZ[next]) == 0 &&
                             PointOnSegmentInclusive(polyX[prev], polyZ[prev], polyX[next], polyZ[next], polyX[tip], polyZ[tip]))
                    {
                        polyNext[prev] = next;
                        polyPrev[next] = prev;
                        // Keep tip's next/prev stale; it is unreachable from the ring.
                        changed = true;
                        if (tip == start)
                        {
                            start = next;
                        }

                        tip = following;
                    }
                    else
                    {
                        tip = following;
                    }

                    guard++;
                    if (guard > rangeCount * 2)
                    {
                        throw new InvalidOperationException(
                            "LayeredSpanTriangulationBuilder flat ring strip failed to traverse hole ring.");
                    }
                } while (tip != start && guard <= rangeCount * 2);
            }
        }

        private static void EarClipChart(
            int chart,
            int outerRing,
            int ownedHoleCount,
            in LayeredSpanTriangulationSpec spec,
            int polyCount,
            Span<int> polyX,
            Span<int> polyZ,
            Span<int> polyNext,
            Span<int> polyPrev,
            Span<byte> polyActive,
            bool allowMidSegmentFlatStrip,
            Span<int> triA,
            Span<int> triB,
            Span<int> triC,
            ref int triCount,
            LayeredSpanTriangulationScratch output)
        {
            int activeCount = 0;
            for (int i = 0; i < polyCount; i++)
            {
                if (polyActive[i] != 0)
                {
                    activeCount++;
                }
            }

            // Arm mid-segment flat-stripping once for large hole-free rings; keep stripping until
            // no flats remain even after activeCount drops (stopping early reintroduces collapse).
            bool stripMidSegmentFlats = allowMidSegmentFlatStrip && activeCount > 32;

            while (activeCount > 3)
            {
                // Mid-segment flat strip (tip on prev-next): hole-free large rings only.
                int flatTip = -1;
                if (stripMidSegmentFlats)
                {
                    for (int tip = 0; tip < polyCount; tip++)
                    {
                        if (polyActive[tip] == 0)
                        {
                            continue;
                        }

                        int prev = polyPrev[tip];
                        int next = polyNext[tip];
                        if ((polyX[tip] == polyX[prev] && polyZ[tip] == polyZ[prev]) ||
                            (polyX[tip] == polyX[next] && polyZ[tip] == polyZ[next]) ||
                            (polyX[prev] == polyX[next] && polyZ[prev] == polyZ[next]))
                        {
                            continue;
                        }

                        if (HasOtherActiveDuplicateXz(tip, polyCount, polyX, polyZ, polyActive))
                        {
                            continue;
                        }

                        if (Orient2Sign(polyX[prev], polyZ[prev], polyX[tip], polyZ[tip], polyX[next], polyZ[next]) != 0)
                        {
                            continue;
                        }

                        if (!PointOnSegmentInclusive(
                                polyX[prev], polyZ[prev], polyX[next], polyZ[next], polyX[tip], polyZ[tip]))
                        {
                            continue;
                        }

                        // Tips are visited in ascending index order, so this is the same
                        // deterministic winner without scanning the rest of the ring.
                        flatTip = tip;
                        break;
                    }
                }

                if (flatTip >= 0)
                {
                    int flatPrev = polyPrev[flatTip];
                    int flatNext = polyNext[flatTip];
                    polyNext[flatPrev] = flatNext;
                    polyPrev[flatNext] = flatPrev;
                    polyActive[flatTip] = 0;
                    activeCount--;
                    continue;
                }

                int bestEar = -1;
                for (int tip = 0; tip < polyCount; tip++)
                {
                    if (polyActive[tip] == 0)
                    {
                        continue;
                    }

                    int prev = polyPrev[tip];
                    int next = polyNext[tip];
                    if (!IsEar(tip, prev, next, polyCount, polyX, polyZ, polyNext, polyPrev, polyActive))
                    {
                        continue;
                    }

                    // Tips are visited in ascending index order. The first valid ear is
                    // therefore the exact same lowest-index winner as a full ring scan.
                    bestEar = tip;
                    break;
                }

                if (bestEar < 0)
                {
                    // Recovery for bridged-hole rings only: strip one reverse spike when no convex
                    // ear remains. Do not recover on hole-free rings — mid-segment strip already
                    // handles those, and spike recovery can change ear order incorrectly.
                    if (ownedHoleCount > 0)
                    {
                        int spikeTip = FindLowestIndexReverseSpike(
                            polyCount, polyX, polyZ, polyNext, polyPrev, polyActive);
                        if (spikeTip >= 0)
                        {
                            int spikePrev = polyPrev[spikeTip];
                            int spikeNext = polyNext[spikeTip];
                            polyNext[spikePrev] = spikeNext;
                            polyPrev[spikeNext] = spikePrev;
                            polyActive[spikeTip] = 0;
                            activeCount--;
                            continue;
                        }
                    }

                    FailEarClipNoValidEar(
                        chart,
                        outerRing,
                        ownedHoleCount,
                        in spec,
                        polyCount,
                        polyX,
                        polyZ,
                        polyNext,
                        polyPrev,
                        polyActive,
                        allowMidSegmentFlatStrip,
                        activeCount,
                        output);
                }

                int p = polyPrev[bestEar];
                int n = polyNext[bestEar];
                if (triCount >= output.TriangleCapacity)
                {
                    Fail(
                        output,
                        $"LayeredSpanTriangulationScratch.triangleCapacity ({output.TriangleCapacity}); required {triCount + 1}.");
                }

                triA[triCount] = p;
                triB[triCount] = bestEar;
                triC[triCount] = n;
                triCount++;

                polyNext[p] = n;
                polyPrev[n] = p;
                polyActive[bestEar] = 0;
                activeCount--;
            }

            if (activeCount != 3)
            {
                Fail(output, "LayeredSpanTriangulationBuilder ear clipping did not reduce to a triangle.");
            }

            int a = -1;
            for (int i = 0; i < polyCount; i++)
            {
                if (polyActive[i] != 0)
                {
                    a = i;
                    break;
                }
            }

            if (a < 0)
            {
                Fail(output, "LayeredSpanTriangulationBuilder ear clipping lost active vertices.");
            }

            int b = polyNext[a];
            int c = polyNext[b];
            if (triCount >= output.TriangleCapacity)
            {
                Fail(
                    output,
                    $"LayeredSpanTriangulationScratch.triangleCapacity ({output.TriangleCapacity}); required {triCount + 1}.");
            }

            triA[triCount] = a;
            triB[triCount] = b;
            triC[triCount] = c;
            triCount++;
        }

        private static bool IsEar(
            int tip,
            int prev,
            int next,
            int polyCount,
            Span<int> polyX,
            Span<int> polyZ,
            Span<int> polyNext,
            Span<int> polyPrev,
            Span<byte> polyActive)
        {
            if (Orient2Sign(polyX[prev], polyZ[prev], polyX[tip], polyZ[tip], polyX[next], polyZ[next]) <= 0)
            {
                return false;
            }

            for (int v = 0; v < polyCount; v++)
            {
                if (polyActive[v] == 0 || v == tip || v == prev || v == next)
                {
                    continue;
                }

                // Skip Steiner duplicates that share exact XZ with the ear triangle corners.
                if ((polyX[v] == polyX[prev] && polyZ[v] == polyZ[prev]) ||
                    (polyX[v] == polyX[tip] && polyZ[v] == polyZ[tip]) ||
                    (polyX[v] == polyX[next] && polyZ[v] == polyZ[next]))
                {
                    continue;
                }

                if (PointInTriangleStrict(polyX[v], polyZ[v], polyX[prev], polyZ[prev], polyX[tip], polyZ[tip], polyX[next], polyZ[next]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasOtherActiveDuplicateXz(
            int tip,
            int polyCount,
            Span<int> polyX,
            Span<int> polyZ,
            Span<byte> polyActive)
        {
            int tx = polyX[tip];
            int tz = polyZ[tip];
            for (int v = 0; v < polyCount; v++)
            {
                if (v == tip || polyActive[v] == 0)
                {
                    continue;
                }

                if (polyX[v] == tx && polyZ[v] == tz)
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindLowestIndexReverseSpike(
            int polyCount,
            Span<int> polyX,
            Span<int> polyZ,
            Span<int> polyNext,
            Span<int> polyPrev,
            Span<byte> polyActive)
        {
            for (int tip = 0; tip < polyCount; tip++)
            {
                if (polyActive[tip] == 0)
                {
                    continue;
                }

                int prev = polyPrev[tip];
                int next = polyNext[tip];
                if ((polyX[tip] == polyX[prev] && polyZ[tip] == polyZ[prev]) ||
                    (polyX[tip] == polyX[next] && polyZ[tip] == polyZ[next]) ||
                    (polyX[prev] == polyX[next] && polyZ[prev] == polyZ[next]))
                {
                    continue;
                }

                if (HasOtherActiveDuplicateXz(tip, polyCount, polyX, polyZ, polyActive))
                {
                    continue;
                }

                if (Orient2Sign(polyX[prev], polyZ[prev], polyX[tip], polyZ[tip], polyX[next], polyZ[next]) != 0)
                {
                    continue;
                }

                // Reverse spike: collinear but tip is outside segment(prev,next).
                if (PointOnSegmentInclusive(
                        polyX[prev], polyZ[prev], polyX[next], polyZ[next], polyX[tip], polyZ[tip]))
                {
                    continue;
                }

                return tip;
            }

            return -1;
        }

        private static void LawsonFlipChart(
            int triStart,
            int triCount,
            int polyCount,
            Span<int> polyX,
            Span<int> polyZ,
            Span<int> triA,
            Span<int> triB,
            Span<int> triC,
            Span<int> markA,
            Span<int> markB,
            int markCount,
            in LayeredSpanTriangulationSpec spec,
            LayeredSpanTriangulationScratch output)
        {
            int flipCount = 0;
            bool flipped = true;
            while (flipped)
            {
                flipped = false;
                for (int t0 = triStart; t0 < triStart + triCount; t0++)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        int v0 = EdgeVertex(t0, e, triA, triB, triC);
                        int v1 = EdgeVertex(t0, e + 1, triA, triB, triC);
                        if (IsConstrainedPolyEdge(v0, v1, markA, markB, markCount))
                        {
                            continue;
                        }

                        int opp0 = OppositeVertex(t0, v0, v1, triA, triB, triC);
                        int t1 = FindMateTriangle(triStart, triCount, t0, v0, v1, triA, triB, triC);
                        if (t1 < 0)
                        {
                            continue;
                        }

                        int opp1 = OppositeVertex(t1, v0, v1, triA, triB, triC);
                        if (IsConstrainedPolyEdge(opp0, opp1, markA, markB, markCount))
                        {
                            continue;
                        }

                        if (InCircleSign(
                                polyX[v0], polyZ[v0],
                                polyX[v1], polyZ[v1],
                                polyX[opp0], polyZ[opp0],
                                polyX[opp1], polyZ[opp1]) <= 0)
                        {
                            continue;
                        }

                        FlipEdge(t0, t1, v0, v1, opp0, opp1, triA, triB, triC);
                        flipCount++;
                        if (flipCount > spec.MaxLawsonFlipCount)
                        {
                            Fail(
                                output,
                                $"LayeredSpanTriangulationBuilder exceeded maxLawsonFlipCount ({spec.MaxLawsonFlipCount}).");
                        }

                        flipped = true;
                    }
                }
            }
        }

        private static bool IsConstrainedPolyEdge(int a, int b, Span<int> markA, Span<int> markB, int markCount)
        {
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            for (int i = 0; i < markCount; i++)
            {
                int ma = markA[i];
                int mb = markB[i];
                int mlo = ma < mb ? ma : mb;
                int mhi = ma < mb ? mb : ma;
                if (mlo == lo && mhi == hi)
                {
                    return true;
                }
            }

            return false;
        }

        private static int EdgeVertex(int tri, int edge, Span<int> triA, Span<int> triB, Span<int> triC)
        {
            return edge switch
            {
                0 => triA[tri],
                1 => triB[tri],
                _ => triC[tri]
            };
        }

        private static int OppositeVertex(int tri, int v0, int v1, Span<int> triA, Span<int> triB, Span<int> triC)
        {
            int a = triA[tri];
            int b = triB[tri];
            int c = triC[tri];
            if (a != v0 && a != v1) return a;
            if (b != v0 && b != v1) return b;
            return c;
        }

        private static int FindMateTriangle(
            int triStart,
            int triCount,
            int self,
            int v0,
            int v1,
            Span<int> triA,
            Span<int> triB,
            Span<int> triC)
        {
            for (int t = triStart; t < triStart + triCount; t++)
            {
                if (t == self)
                {
                    continue;
                }

                int hits = 0;
                if (triA[t] == v0 || triB[t] == v0 || triC[t] == v0) hits++;
                if (triA[t] == v1 || triB[t] == v1 || triC[t] == v1) hits++;
                if (hits == 2)
                {
                    return t;
                }
            }

            return -1;
        }

        private static void FlipEdge(
            int t0,
            int t1,
            int v0,
            int v1,
            int opp0,
            int opp1,
            Span<int> triA,
            Span<int> triB,
            Span<int> triC)
        {
            SetTriangleCCW(t0, opp0, v0, opp1, triA, triB, triC);
            SetTriangleCCW(t1, opp1, v1, opp0, triA, triB, triC);
        }

        private static void SetTriangleCCW(
            int tri,
            int a,
            int b,
            int c,
            Span<int> triA,
            Span<int> triB,
            Span<int> triC)
        {
            triA[tri] = a;
            triB[tri] = b;
            triC[tri] = c;
        }

        private static void PublishChartVertices(
            int chartId,
            int chartTriStart,
            int chartTriCount,
            int polyCount,
            Span<int> polyX,
            Span<int> polyZ,
            Span<int> polySpan,
            Span<int> polyContourVtx,
            Span<int> triA,
            Span<int> triB,
            Span<int> triC,
            Span<int> markA,
            Span<int> markB,
            int markCount,
            Span<int> vtxX,
            Span<int> vtxY,
            Span<int> vtxZ,
            Span<int> vtxChart,
            Span<int> vtxSpan,
            Span<int> keyX,
            Span<int> keyY,
            Span<int> keyZ,
            Span<int> keyChart,
            Span<int> keyIndex,
            Span<int> polyToGlobal,
            ReadOnlySpan<int> spanTriangleIndices,
            NavTriangleSurfaceSnapshot surface,
            in LayeredSpanTriangulationSpec spec,
            ref int globalVtxCount,
            LayeredSpanTriangulationScratch output)
        {
            for (int i = 0; i < polyCount; i++)
            {
                polyToGlobal[i] = -1;
            }

            int chartUnique = 0;
            for (int t = chartTriStart; t < chartTriStart + chartTriCount; t++)
            {
                RemapCorner(t, triA[t], chartId, polyX, polyZ, polySpan, polyContourVtx, spanTriangleIndices, surface, in spec, polyToGlobal, keyX, keyY, keyZ, keyChart, keyIndex, ref chartUnique, vtxX, vtxY, vtxZ, vtxChart, vtxSpan, ref globalVtxCount, output);
                RemapCorner(t, triB[t], chartId, polyX, polyZ, polySpan, polyContourVtx, spanTriangleIndices, surface, in spec, polyToGlobal, keyX, keyY, keyZ, keyChart, keyIndex, ref chartUnique, vtxX, vtxY, vtxZ, vtxChart, vtxSpan, ref globalVtxCount, output);
                RemapCorner(t, triC[t], chartId, polyX, polyZ, polySpan, polyContourVtx, spanTriangleIndices, surface, in spec, polyToGlobal, keyX, keyY, keyZ, keyChart, keyIndex, ref chartUnique, vtxX, vtxY, vtxZ, vtxChart, vtxSpan, ref globalVtxCount, output);
            }

            // Hole-boundary / bridge constraint endpoints must remain published even if only
            // adjacent to triangles that were stripped from hole interiors.
            for (int m = 0; m < markCount; m++)
            {
                RemapCorner(0, markA[m], chartId, polyX, polyZ, polySpan, polyContourVtx, spanTriangleIndices, surface, in spec, polyToGlobal, keyX, keyY, keyZ, keyChart, keyIndex, ref chartUnique, vtxX, vtxY, vtxZ, vtxChart, vtxSpan, ref globalVtxCount, output);
                RemapCorner(0, markB[m], chartId, polyX, polyZ, polySpan, polyContourVtx, spanTriangleIndices, surface, in spec, polyToGlobal, keyX, keyY, keyZ, keyChart, keyIndex, ref chartUnique, vtxX, vtxY, vtxZ, vtxChart, vtxSpan, ref globalVtxCount, output);
            }

            for (int t = chartTriStart; t < chartTriStart + chartTriCount; t++)
            {
                triA[t] = polyToGlobal[triA[t]];
                triB[t] = polyToGlobal[triB[t]];
                triC[t] = polyToGlobal[triC[t]];
            }
        }

        private static void RemapCorner(
            int tri,
            int polyIdx,
            int chartId,
            Span<int> polyX,
            Span<int> polyZ,
            Span<int> polySpan,
            Span<int> polyContourVtx,
            ReadOnlySpan<int> spanTriangleIndices,
            NavTriangleSurfaceSnapshot surface,
            in LayeredSpanTriangulationSpec spec,
            Span<int> polyToGlobal,
            Span<int> keyX,
            Span<int> keyY,
            Span<int> keyZ,
            Span<int> keyChart,
            Span<int> keyIndex,
            ref int chartUnique,
            Span<int> vtxX,
            Span<int> vtxY,
            Span<int> vtxZ,
            Span<int> vtxChart,
            Span<int> vtxSpan,
            ref int globalVtxCount,
            LayeredSpanTriangulationScratch output)
        {
            if (polyToGlobal[polyIdx] >= 0)
            {
                return;
            }

            int x = polyX[polyIdx];
            int z = polyZ[polyIdx];
            int span = polySpan[polyIdx];
            int y = SampleHeight(surface, span, spanTriangleIndices, x, z, spec.HeightRounding, output);
            for (int k = 0; k < chartUnique; k++)
            {
                if (keyChart[k] == chartId && keyX[k] == x && keyY[k] == y && keyZ[k] == z)
                {
                    polyToGlobal[polyIdx] = keyIndex[k];
                    return;
                }
            }

            if (globalVtxCount >= output.VertexCapacity)
            {
                Fail(
                    output,
                    $"LayeredSpanTriangulationScratch.vertexCapacity ({output.VertexCapacity}); required {globalVtxCount + 1}.");
            }

            keyX[chartUnique] = x;
            keyY[chartUnique] = y;
            keyZ[chartUnique] = z;
            keyChart[chartUnique] = chartId;
            keyIndex[chartUnique] = globalVtxCount;

            vtxX[globalVtxCount] = x;
            vtxY[globalVtxCount] = y;
            vtxZ[globalVtxCount] = z;
            vtxChart[globalVtxCount] = chartId;
            vtxSpan[globalVtxCount] = span;

            polyToGlobal[polyIdx] = globalVtxCount;
            chartUnique++;
            globalVtxCount++;
        }

        private static void PublishChartConstrainedEdges(
            int markCount,
            Span<int> markA,
            Span<int> markB,
            Span<byte> markFlags,
            Span<int> polyToGlobal,
            Span<int> cEdgeA,
            Span<int> cEdgeB,
            Span<byte> cEdgeFlags,
            ref int globalCEdgeCount,
            LayeredSpanTriangulationScratch output)
        {
            for (int i = 0; i < markCount; i++)
            {
                int ga = polyToGlobal[markA[i]];
                int gb = polyToGlobal[markB[i]];
                int lo = ga < gb ? ga : gb;
                int hi = ga < gb ? gb : ga;
                if (globalCEdgeCount >= output.ConstrainedEdgeCapacity)
                {
                    Fail(
                        output,
                        $"LayeredSpanTriangulationScratch.constrainedEdgeCapacity ({output.ConstrainedEdgeCapacity}); required {globalCEdgeCount + 1}.");
                }

                cEdgeA[globalCEdgeCount] = lo;
                cEdgeB[globalCEdgeCount] = hi;
                cEdgeFlags[globalCEdgeCount] = markFlags[i];
                globalCEdgeCount++;
            }
        }

        private static void SortTriangles(
            int triCount,
            Span<int> triA,
            Span<int> triB,
            Span<int> triC,
            Span<int> triChartIds,
            Span<int> triRegionIds,
            Span<byte> triAreaIds,
            ReadOnlySpan<int> vtxX,
            ReadOnlySpan<int> vtxY,
            ReadOnlySpan<int> vtxZ)
        {
            Span<int> order = triChartIds;
            for (int i = 1; i < triCount; i++)
            {
                int ti = i;
                while (ti > 0 && CompareTriangle(ti, ti - 1, triChartIds, triA, triB, triC, vtxX, vtxY, vtxZ) < 0)
                {
                    SwapTriangle(ti, ti - 1, triA, triB, triC, triChartIds, triRegionIds, triAreaIds);
                    ti--;
                }
            }
        }

        private static int CompareTriangle(
            int a,
            int b,
            ReadOnlySpan<int> triChartIds,
            ReadOnlySpan<int> triA,
            ReadOnlySpan<int> triB,
            ReadOnlySpan<int> triC,
            ReadOnlySpan<int> vtxX,
            ReadOnlySpan<int> vtxY,
            ReadOnlySpan<int> vtxZ)
        {
            if (triChartIds[a] != triChartIds[b])
            {
                return triChartIds[a] < triChartIds[b] ? -1 : 1;
            }

            NormalizeTriangleCorners(triA[a], triB[a], triC[a], vtxX, vtxY, vtxZ, out int ax, out int ay, out int az, out int aa, out int ab, out int ac);
            NormalizeTriangleCorners(triA[b], triB[b], triC[b], vtxX, vtxY, vtxZ, out int bx, out int by, out int bz, out int ba, out int bb, out int bc);
            int cmp = CompareVertexKey(ax, az, bx, bz);
            if (cmp != 0) return cmp;
            cmp = ay.CompareTo(by);
            if (cmp != 0) return cmp;
            cmp = aa.CompareTo(ba);
            if (cmp != 0) return cmp;
            cmp = ab.CompareTo(bb);
            if (cmp != 0) return cmp;
            return ac.CompareTo(bc);
        }

        private static void NormalizeTriangleCorners(
            int ia,
            int ib,
            int ic,
            ReadOnlySpan<int> vtxX,
            ReadOnlySpan<int> vtxY,
            ReadOnlySpan<int> vtxZ,
            out int minX,
            out int minY,
            out int minZ,
            out int ca,
            out int cb,
            out int cc)
        {
            int ax = vtxX[ia];
            int ay = vtxY[ia];
            int az = vtxZ[ia];
            int bx = vtxX[ib];
            int by = vtxY[ib];
            int bz = vtxZ[ib];
            int cx = vtxX[ic];
            int cy = vtxY[ic];
            int cz = vtxZ[ic];
            minX = ax;
            minY = ay;
            minZ = az;
            ca = ia;
            cb = ib;
            cc = ic;
            if (CompareVertexKey(bx, bz, minX, minZ) < 0 || (bx == minX && bz == minZ && by < minY))
            {
                minX = bx;
                minY = by;
                minZ = bz;
            }

            if (CompareVertexKey(cx, cz, minX, minZ) < 0 || (cx == minX && cz == minZ && cy < minY))
            {
                minX = cx;
                minY = cy;
                minZ = cz;
            }
        }

        private static void SwapTriangle(
            int a,
            int b,
            Span<int> triA,
            Span<int> triB,
            Span<int> triC,
            Span<int> triChartIds,
            Span<int> triRegionIds,
            Span<byte> triAreaIds)
        {
            (triA[a], triA[b]) = (triA[b], triA[a]);
            (triB[a], triB[b]) = (triB[b], triB[a]);
            (triC[a], triC[b]) = (triC[b], triC[a]);
            (triChartIds[a], triChartIds[b]) = (triChartIds[b], triChartIds[a]);
            (triRegionIds[a], triRegionIds[b]) = (triRegionIds[b], triRegionIds[a]);
            (triAreaIds[a], triAreaIds[b]) = (triAreaIds[b], triAreaIds[a]);
        }

        private static void BuildAdjacency(
            int triCount,
            int vtxCount,
            Span<int> triA,
            Span<int> triB,
            Span<int> triC,
            ReadOnlySpan<int> triChartIds,
            ReadOnlySpan<int> vtxX,
            ReadOnlySpan<int> vtxY,
            ReadOnlySpan<int> vtxZ,
            LayeredSpanContourScratch contours,
            LayeredSpanScratch raw,
            in LayeredSpanRasterGridSpec grid,
            LayeredSpanTriangulationScratch output)
        {
            _ = vtxCount;
            Span<int> n0 = output.MutableN0;
            Span<int> n1 = output.MutableN1;
            Span<int> n2 = output.MutableN2;
            for (int t = 0; t < triCount; t++)
            {
                n0[t] = -1;
                n1[t] = -1;
                n2[t] = -1;
            }

            int edgeCount = checked(triCount * 3);
            if (edgeCount > output.AdjacencyEdgeCapacity)
            {
                Fail(
                    output,
                    $"LayeredSpanTriangulationScratch.adjacencyEdgeCapacity ({output.AdjacencyEdgeCapacity}); required {edgeCount}.");
            }

            Span<int> eA = output.MutableEdgeA;
            Span<int> eB = output.MutableEdgeB;
            Span<int> eTri = output.MutableEdgeTri;
            Span<int> eOpp = output.MutableEdgeOpp;
            Span<int> eOrder = output.MutableEdgeOrder;

            for (int t = 0; t < triCount; t++)
            {
                WriteHalfEdge(t, 0, triA[t], triB[t], eA, eB, eTri, eOpp);
                WriteHalfEdge(t, 1, triB[t], triC[t], eA, eB, eTri, eOpp);
                WriteHalfEdge(t, 2, triC[t], triA[t], eA, eB, eTri, eOpp);
            }

            for (int i = 0; i < edgeCount; i++)
            {
                eOrder[i] = i;
            }

            InsertionSortHalfEdges(eOrder, edgeCount, eA, eB, eTri, eOpp, vtxX, vtxY, vtxZ);

            ReadOnlySpan<int> columnSpanOffsets = raw.ColumnSpanOffsets;
            Span<byte> paired = output.MutableEdgeConstrained;
            paired.Slice(0, edgeCount).Clear();
            for (int i = 0; i < edgeCount; i++)
            {
                int ei = eOrder[i];
                if (paired[ei] != 0)
                {
                    continue;
                }

                int mate = -1;
                for (int j = i + 1; j < edgeCount; j++)
                {
                    int ej = eOrder[j];
                    if (paired[ej] != 0)
                    {
                        continue;
                    }

                    if (!SameUndirectedXyzEdge(ei, ej, eA, eB, vtxX, vtxY, vtxZ))
                    {
                        // Sorted by XYZ key: once keys diverge, no later mate shares this key.
                        break;
                    }

                    if (CanPairHalfEdges(
                            ei,
                            ej,
                            eA,
                            eB,
                            eTri,
                            triChartIds,
                            contours,
                            vtxX,
                            vtxY,
                            vtxZ,
                            columnSpanOffsets,
                            in grid))
                    {
                        mate = ej;
                        break;
                    }
                }

                if (mate < 0)
                {
                    continue;
                }

                int triI = eTri[ei];
                int slotI = eOpp[ei];
                int triJ = eTri[mate];
                int slotJ = eOpp[mate];
                if (NeighborAt(triI, slotI, n0, n1, n2) >= 0 || NeighborAt(triJ, slotJ, n0, n1, n2) >= 0)
                {
                    continue;
                }

                SetNeighbor(triI, slotI, triJ, n0, n1, n2);
                SetNeighbor(triJ, slotJ, triI, n0, n1, n2);
                paired[ei] = 1;
                paired[mate] = 1;
            }
        }

        private static int NeighborAt(int tri, int slot, ReadOnlySpan<int> n0, ReadOnlySpan<int> n1, ReadOnlySpan<int> n2)
            => slot switch
            {
                0 => n0[tri],
                1 => n1[tri],
                _ => n2[tri]
            };

        private static void WriteHalfEdge(
            int tri,
            int slot,
            int v0,
            int v1,
            Span<int> eA,
            Span<int> eB,
            Span<int> eTri,
            Span<int> eOpp)
        {
            int idx = tri * 3 + slot;
            eA[idx] = v0;
            eB[idx] = v1;
            eTri[idx] = tri;
            eOpp[idx] = slot;
        }

        private static bool SameUndirectedXyzEdge(
            int ea,
            int eb,
            ReadOnlySpan<int> eA,
            ReadOnlySpan<int> eB,
            ReadOnlySpan<int> vtxX,
            ReadOnlySpan<int> vtxY,
            ReadOnlySpan<int> vtxZ)
        {
            OrderEndpointKeys(
                eA[ea], eB[ea], vtxX, vtxY, vtxZ,
                out int a0x, out int a0y, out int a0z, out int a1x, out int a1y, out int a1z);
            OrderEndpointKeys(
                eA[eb], eB[eb], vtxX, vtxY, vtxZ,
                out int b0x, out int b0y, out int b0z, out int b1x, out int b1y, out int b1z);
            return a0x == b0x && a0y == b0y && a0z == b0z &&
                   a1x == b1x && a1y == b1y && a1z == b1z;
        }

        private static void OrderEndpointKeys(
            int v0,
            int v1,
            ReadOnlySpan<int> vtxX,
            ReadOnlySpan<int> vtxY,
            ReadOnlySpan<int> vtxZ,
            out int x0,
            out int y0,
            out int z0,
            out int x1,
            out int y1,
            out int z1)
        {
            int ax = vtxX[v0];
            int ay = vtxY[v0];
            int az = vtxZ[v0];
            int bx = vtxX[v1];
            int by = vtxY[v1];
            int bz = vtxZ[v1];
            int cmp = CompareXyz(ax, ay, az, bx, by, bz);
            if (cmp <= 0)
            {
                x0 = ax; y0 = ay; z0 = az;
                x1 = bx; y1 = by; z1 = bz;
            }
            else
            {
                x0 = bx; y0 = by; z0 = bz;
                x1 = ax; y1 = ay; z1 = az;
            }
        }

        private static int CompareXyz(int ax, int ay, int az, int bx, int by, int bz)
        {
            if (ax != bx) return ax < bx ? -1 : 1;
            if (ay != by) return ay < by ? -1 : 1;
            if (az != bz) return az < bz ? -1 : 1;
            return 0;
        }

        private static bool CanPairHalfEdges(
            int ea,
            int eb,
            ReadOnlySpan<int> eA,
            ReadOnlySpan<int> eB,
            ReadOnlySpan<int> eTri,
            ReadOnlySpan<int> triChartIds,
            LayeredSpanContourScratch contours,
            ReadOnlySpan<int> vtxX,
            ReadOnlySpan<int> vtxY,
            ReadOnlySpan<int> vtxZ,
            ReadOnlySpan<int> columnSpanOffsets,
            in LayeredSpanRasterGridSpec grid)
        {
            int triAId = triChartIds[eTri[ea]];
            int triBId = triChartIds[eTri[eb]];
            int va0 = eA[ea];
            int va1 = eB[ea];
            int vb0 = eA[eb];
            int vb1 = eB[eb];

            bool opposite =
                vtxX[va0] == vtxX[vb1] && vtxY[va0] == vtxY[vb1] && vtxZ[va0] == vtxZ[vb1] &&
                vtxX[va1] == vtxX[vb0] && vtxY[va1] == vtxY[vb0] && vtxZ[va1] == vtxZ[vb0];
            if (!opposite)
            {
                return false;
            }

            if (triAId == triBId)
            {
                return true;
            }

            return SeamMatchesExactEdge(
                triAId,
                triBId,
                vtxX[va0],
                vtxY[va0],
                vtxZ[va0],
                vtxX[va1],
                vtxY[va1],
                vtxZ[va1],
                contours,
                columnSpanOffsets,
                in grid);
        }

        private static bool SeamMatchesExactEdge(
            int chartA,
            int chartB,
            int ax,
            int ay,
            int az,
            int bx,
            int by,
            int bz,
            LayeredSpanContourScratch contours,
            ReadOnlySpan<int> columnSpanOffsets,
            in LayeredSpanRasterGridSpec grid)
        {
            _ = ay;
            _ = by;
            ReadOnlySpan<int> seamA = contours.SeamChartA;
            ReadOnlySpan<int> seamB = contours.SeamChartB;
            ReadOnlySpan<LayeredSpanNeighborDirection> seamDirs = contours.SeamDirections;
            ReadOnlySpan<int> seamPortalMin = contours.SeamPortalMinAlongCm;
            ReadOnlySpan<int> seamPortalMax = contours.SeamPortalMaxAlongCm;
            ReadOnlySpan<int> seamSpanA = contours.SeamSpanA;
            ReadOnlySpan<int> seamSpanB = contours.SeamSpanB;

            for (int s = 0; s < contours.SeamCount; s++)
            {
                int ca = seamA[s];
                int cb = seamB[s];
                if (!((ca == chartA && cb == chartB) || (ca == chartB && cb == chartA)))
                {
                    continue;
                }

                int pMin = seamPortalMin[s];
                int pMax = seamPortalMax[s];
                if (pMax <= pMin)
                {
                    continue;
                }

                if (!TrySeamFaceCoordinate(
                        seamDirs[s],
                        seamSpanA[s],
                        seamSpanB[s],
                        columnSpanOffsets,
                        in grid,
                        out int faceX,
                        out int faceZ,
                        out bool verticalFace))
                {
                    continue;
                }

                if (verticalFace)
                {
                    // Shared X face; portal along Z must exactly equal the edge.
                    if (ax != faceX || bx != faceX)
                    {
                        continue;
                    }

                    int zLo = az < bz ? az : bz;
                    int zHi = az < bz ? bz : az;
                    if (zLo == pMin && zHi == pMax)
                    {
                        return true;
                    }
                }
                else
                {
                    if (az != faceZ || bz != faceZ)
                    {
                        continue;
                    }

                    int xLo = ax < bx ? ax : bx;
                    int xHi = ax < bx ? bx : ax;
                    if (xLo == pMin && xHi == pMax)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TrySeamFaceCoordinate(
            LayeredSpanNeighborDirection dir,
            int spanA,
            int spanB,
            ReadOnlySpan<int> columnSpanOffsets,
            in LayeredSpanRasterGridSpec grid,
            out int faceX,
            out int faceZ,
            out bool verticalFace)
        {
            faceX = 0;
            faceZ = 0;
            verticalFace = true;
            int colA = LayeredSpanColumnIndex.FindColumnOfSpan(
                spanA,
                columnSpanOffsets,
                grid.ColumnCount);
            int colB = LayeredSpanColumnIndex.FindColumnOfSpan(
                spanB,
                columnSpanOffsets,
                grid.ColumnCount);
            if (colA < 0 || colB < 0)
            {
                return false;
            }

            int ax = colA % grid.ColumnCountX;
            int az = colA / grid.ColumnCountX;
            int bx = colB % grid.ColumnCountX;
            int bz = colB / grid.ColumnCountX;

            if (az == bz && (ax + 1 == bx || bx + 1 == ax))
            {
                int westX = ax < bx ? ax : bx;
                faceX = grid.ColumnMaxXcm(westX);
                verticalFace = true;
                _ = dir;
                return true;
            }

            if (ax == bx && (az + 1 == bz || bz + 1 == az))
            {
                int northZ = az < bz ? az : bz;
                faceZ = grid.ColumnMaxZcm(northZ);
                verticalFace = false;
                _ = dir;
                return true;
            }

            return false;
        }

        private static void SetNeighbor(int tri, int slot, int neighbor, Span<int> n0, Span<int> n1, Span<int> n2)
        {
            switch (slot)
            {
                case 0: n0[tri] = neighbor; break;
                case 1: n1[tri] = neighbor; break;
                default: n2[tri] = neighbor; break;
            }
        }

        private static void InsertionSortHalfEdges(
            Span<int> order,
            int count,
            ReadOnlySpan<int> eA,
            ReadOnlySpan<int> eB,
            ReadOnlySpan<int> eTri,
            ReadOnlySpan<int> eOpp,
            ReadOnlySpan<int> vtxX,
            ReadOnlySpan<int> vtxY,
            ReadOnlySpan<int> vtxZ)
        {
            for (int i = 1; i < count; i++)
            {
                int key = order[i];
                int j = i - 1;
                while (j >= 0 && CompareHalfEdge(order[j], key, eA, eB, eTri, eOpp, vtxX, vtxY, vtxZ) > 0)
                {
                    order[j + 1] = order[j];
                    j--;
                }

                order[j + 1] = key;
            }
        }

        private static int CompareHalfEdge(
            int a,
            int b,
            ReadOnlySpan<int> eA,
            ReadOnlySpan<int> eB,
            ReadOnlySpan<int> eTri,
            ReadOnlySpan<int> eOpp,
            ReadOnlySpan<int> vtxX,
            ReadOnlySpan<int> vtxY,
            ReadOnlySpan<int> vtxZ)
        {
            OrderEndpointKeys(eA[a], eB[a], vtxX, vtxY, vtxZ, out int a0x, out int a0y, out int a0z, out int a1x, out int a1y, out int a1z);
            OrderEndpointKeys(eA[b], eB[b], vtxX, vtxY, vtxZ, out int b0x, out int b0y, out int b0z, out int b1x, out int b1y, out int b1z);
            int cmp = CompareXyz(a0x, a0y, a0z, b0x, b0y, b0z);
            if (cmp != 0) return cmp;
            cmp = CompareXyz(a1x, a1y, a1z, b1x, b1y, b1z);
            if (cmp != 0) return cmp;
            if (eTri[a] != eTri[b]) return eTri[a] < eTri[b] ? -1 : 1;
            return eOpp[a].CompareTo(eOpp[b]);
        }

        private static void BuildBorderPortals(
            NavTriangleSurfaceSnapshot surface,
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walkability,
            LayeredSpanWalkLinkScratch links,
            LayeredSpanRegionScratch regions,
            in LayeredSpanRasterGridSpec grid,
            in LayeredSpanTriangulationSpec spec,
            ReadOnlySpan<int> columnSpanOffsets,
            ReadOnlySpan<int> spanClearance,
            ReadOnlySpan<int> spanTriangleIndices,
            LayeredSpanTriangulationScratch output)
        {
            _ = raw;
            int portalCount = 0;
            Span<NavPortalSide> sides = output.MutablePortalSides;
            Span<short> u0 = output.MutablePortalU0;
            Span<short> v0 = output.MutablePortalV0;
            Span<short> u1 = output.MutablePortalU1;
            Span<short> v1 = output.MutablePortalV1;
            Span<int> leftX = output.MutablePortalLeftXcm;
            Span<int> leftY = output.MutablePortalLeftYcm;
            Span<int> leftZ = output.MutablePortalLeftZcm;
            Span<int> rightX = output.MutablePortalRightXcm;
            Span<int> rightY = output.MutablePortalRightYcm;
            Span<int> rightZ = output.MutablePortalRightZcm;
            Span<int> clearance = output.MutablePortalClearanceCm;
            Span<int> sourceSpans = output.MutablePortalSourceSpanIndices;
            Span<int> neighborSpans = output.MutablePortalNeighborSpanIndices;

            ReadOnlySpan<int> regionIds = regions.SpanRegionIds;

            CollectWalkLinkBorderPortals(
                surface,
                links,
                walkability,
                columnSpanOffsets,
                regionIds,
                spanTriangleIndices,
                in grid,
                in spec,
                spanClearance,
                sides,
                u0,
                v0,
                u1,
                v1,
                leftX,
                leftY,
                leftZ,
                rightX,
                rightY,
                rightZ,
                clearance,
                sourceSpans,
                neighborSpans,
                ref portalCount,
                output);

            SortPortals(
                portalCount,
                sides,
                u0,
                v0,
                u1,
                v1,
                leftX,
                leftY,
                leftZ,
                rightX,
                rightY,
                rightZ,
                clearance,
                sourceSpans,
                neighborSpans);
            output.SetPortalCount(portalCount);
        }

        private static void CollectWalkLinkBorderPortals(
            NavTriangleSurfaceSnapshot surface,
            LayeredSpanWalkLinkScratch links,
            LayeredSpanWalkabilityScratch walkability,
            ReadOnlySpan<int> columnSpanOffsets,
            ReadOnlySpan<int> regionIds,
            ReadOnlySpan<int> spanTriangleIndices,
            in LayeredSpanRasterGridSpec grid,
            in LayeredSpanTriangulationSpec spec,
            ReadOnlySpan<int> spanClearance,
            Span<NavPortalSide> sides,
            Span<short> u0,
            Span<short> v0,
            Span<short> u1,
            Span<short> v1,
            Span<int> leftX,
            Span<int> leftY,
            Span<int> leftZ,
            Span<int> rightX,
            Span<int> rightY,
            Span<int> rightZ,
            Span<int> clearance,
            Span<int> sourceSpans,
            Span<int> neighborSpansOut,
            ref int portalCount,
            LayeredSpanTriangulationScratch output)
        {
            ReadOnlySpan<int> walkableIndices = walkability.WalkableSpanIndices;
            ReadOnlySpan<int> linkOffsets = links.LinkOffsets;
            ReadOnlySpan<int> neighbors = links.LinkNeighborSpanIndices;
            ReadOnlySpan<LayeredSpanNeighborDirection> dirs = links.LinkNeighborDirections;
            ReadOnlySpan<int> portalMin = links.LinkPortalMinAlongCm;
            ReadOnlySpan<int> portalMax = links.LinkPortalMaxAlongCm;

            int columnCursor = 0;
            for (int w = 0; w < walkability.WalkableSpanCount; w++)
            {
                int span = walkableIndices[w];
                if ((uint)span >= (uint)regionIds.Length || regionIds[span] < 0)
                {
                    continue;
                }

                int col = LayeredSpanColumnIndex.AdvanceToColumnOfSpan(
                    span,
                    columnSpanOffsets,
                    grid.ColumnCount,
                    ref columnCursor);
                if (col < 0)
                {
                    continue;
                }

                int cx = col % grid.ColumnCountX;
                int cz = col / grid.ColumnCountX;
                int cellMinX = grid.ColumnMinXcm(cx);
                int cellMaxX = grid.ColumnMaxXcm(cx);
                int cellMinZ = grid.ColumnMinZcm(cz);
                int cellMaxZ = grid.ColumnMaxZcm(cz);

                for (int li = linkOffsets[w]; li < linkOffsets[w + 1]; li++)
                {
                    int minAlong = portalMin[li];
                    int maxAlong = portalMax[li];
                    if (maxAlong <= minAlong)
                    {
                        continue;
                    }

                    int nSpan = neighbors[li];
                    if ((uint)nSpan >= (uint)regionIds.Length || regionIds[nSpan] < 0)
                    {
                        continue;
                    }

                    LayeredSpanNeighborDirection dir = dirs[li];
                    NavPortalSide side;
                    int leftXcm;
                    int leftZcm;
                    int rightXcm;
                    int rightZcm;
                    switch (dir)
                    {
                        case LayeredSpanNeighborDirection.West when cellMinX == spec.TargetMinXcm:
                            side = NavPortalSide.West;
                            leftXcm = rightXcm = spec.TargetMinXcm;
                            leftZcm = minAlong;
                            rightZcm = maxAlong;
                            break;
                        case LayeredSpanNeighborDirection.East when cellMaxX == spec.TargetMaxXcm:
                            side = NavPortalSide.East;
                            leftXcm = rightXcm = spec.TargetMaxXcm;
                            leftZcm = minAlong;
                            rightZcm = maxAlong;
                            break;
                        case LayeredSpanNeighborDirection.North when cellMinZ == spec.TargetMinZcm:
                            side = NavPortalSide.North;
                            leftZcm = rightZcm = spec.TargetMinZcm;
                            leftXcm = minAlong;
                            rightXcm = maxAlong;
                            break;
                        case LayeredSpanNeighborDirection.South when cellMaxZ == spec.TargetMaxZcm:
                            side = NavPortalSide.South;
                            leftZcm = rightZcm = spec.TargetMaxZcm;
                            leftXcm = minAlong;
                            rightXcm = maxAlong;
                            break;
                        default:
                            continue;
                    }

                    int c = spanClearance[span];
                    if (spanClearance[nSpan] < c)
                    {
                        c = spanClearance[nSpan];
                    }

                    long alongMinL = side == NavPortalSide.West || side == NavPortalSide.East
                        ? (long)leftZcm - spec.TargetMinZcm
                        : (long)leftXcm - spec.TargetMinXcm;
                    long alongMaxL = side == NavPortalSide.West || side == NavPortalSide.East
                        ? (long)rightZcm - spec.TargetMinZcm
                        : (long)rightXcm - spec.TargetMinXcm;
                    if (alongMaxL <= alongMinL)
                    {
                        continue;
                    }

                    int alongMin = checked((int)alongMinL);
                    int alongMax = checked((int)alongMaxL);

                    int leftYcm = SampleHeight(
                        surface, span, spanTriangleIndices, leftXcm, leftZcm, spec.HeightRounding, output);
                    int rightYcm = SampleHeight(
                        surface, span, spanTriangleIndices, rightXcm, rightZcm, spec.HeightRounding, output);

                    // Canonicalize directed/reverse identity: smaller source span first.
                    int srcId = span;
                    int dstId = nSpan;
                    if (dstId < srcId)
                    {
                        (srcId, dstId) = (dstId, srcId);
                    }

                    if (PortalDuplicateExists(
                            portalCount,
                            side,
                            leftXcm,
                            leftYcm,
                            leftZcm,
                            rightXcm,
                            rightYcm,
                            rightZcm,
                            srcId,
                            dstId,
                            c,
                            sides,
                            leftX,
                            leftY,
                            leftZ,
                            rightX,
                            rightY,
                            rightZ,
                            sourceSpans,
                            neighborSpansOut,
                            clearance))
                    {
                        continue;
                    }

                    AddPortalRecord(
                        side,
                        leftXcm,
                        leftYcm,
                        leftZcm,
                        rightXcm,
                        rightYcm,
                        rightZcm,
                        alongMin,
                        alongMax,
                        c,
                        srcId,
                        dstId,
                        in spec,
                        sides,
                        u0,
                        v0,
                        u1,
                        v1,
                        leftX,
                        leftY,
                        leftZ,
                        rightX,
                        rightY,
                        rightZ,
                        clearance,
                        sourceSpans,
                        neighborSpansOut,
                        ref portalCount,
                        output);
                }
            }
        }

        private static bool PortalDuplicateExists(
            int portalCount,
            NavPortalSide side,
            int leftXcm,
            int leftYcm,
            int leftZcm,
            int rightXcm,
            int rightYcm,
            int rightZcm,
            int sourceSpan,
            int neighborSpan,
            int clearanceCm,
            ReadOnlySpan<NavPortalSide> sides,
            ReadOnlySpan<int> leftX,
            ReadOnlySpan<int> leftY,
            ReadOnlySpan<int> leftZ,
            ReadOnlySpan<int> rightX,
            ReadOnlySpan<int> rightY,
            ReadOnlySpan<int> rightZ,
            ReadOnlySpan<int> sourceSpans,
            ReadOnlySpan<int> neighborSpans,
            ReadOnlySpan<int> clearance)
        {
            for (int i = 0; i < portalCount; i++)
            {
                if (sides[i] != side || clearance[i] != clearanceCm)
                {
                    continue;
                }

                if (sourceSpans[i] != sourceSpan || neighborSpans[i] != neighborSpan)
                {
                    continue;
                }

                bool same =
                    leftX[i] == leftXcm && leftY[i] == leftYcm && leftZ[i] == leftZcm &&
                    rightX[i] == rightXcm && rightY[i] == rightYcm && rightZ[i] == rightZcm;
                bool reverse =
                    leftX[i] == rightXcm && leftY[i] == rightYcm && leftZ[i] == rightZcm &&
                    rightX[i] == leftXcm && rightY[i] == leftYcm && rightZ[i] == leftZcm;
                if (same || reverse)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddPortalRecord(
            NavPortalSide side,
            int ax,
            int ay,
            int az,
            int bx,
            int by,
            int bz,
            int alongMin,
            int alongMax,
            int clearanceCm,
            int sourceSpan,
            int neighborSpan,
            in LayeredSpanTriangulationSpec spec,
            Span<NavPortalSide> sides,
            Span<short> u0,
            Span<short> v0,
            Span<short> u1,
            Span<short> v1,
            Span<int> leftX,
            Span<int> leftY,
            Span<int> leftZ,
            Span<int> rightX,
            Span<int> rightY,
            Span<int> rightZ,
            Span<int> clearance,
            Span<int> sourceSpans,
            Span<int> neighborSpans,
            ref int portalCount,
            LayeredSpanTriangulationScratch output)
        {
            if (portalCount >= output.BorderPortalCapacity)
            {
                Fail(
                    output,
                    $"LayeredSpanTriangulationScratch.borderPortalCapacity ({output.BorderPortalCapacity}); required {portalCount + 1}.");
            }

            int tileWidthCm = checked(spec.TargetMaxXcm - spec.TargetMinXcm);
            int tileHeightCm = checked(spec.TargetMaxZcm - spec.TargetMinZcm);
            NavBorderPortalCoordinateContract.RequireTileExtentFitsPortalCoordinates(
                tileWidthCm,
                tileHeightCm,
                "LayeredSpanTriangulationBuilder.AddPortalRecord");

            short su0;
            short su1;
            short sv0;
            short sv1;
            switch (side)
            {
                case NavPortalSide.West:
                    su0 = su1 = 0;
                    sv0 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(alongMin, "LayeredSpan.AddPortalRecord.West.v0");
                    sv1 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(alongMax, "LayeredSpan.AddPortalRecord.West.v1");
                    break;
                case NavPortalSide.East:
                    su0 = su1 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(tileWidthCm, "LayeredSpan.AddPortalRecord.East.u");
                    sv0 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(alongMin, "LayeredSpan.AddPortalRecord.East.v0");
                    sv1 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(alongMax, "LayeredSpan.AddPortalRecord.East.v1");
                    break;
                case NavPortalSide.North:
                    sv0 = sv1 = 0;
                    su0 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(alongMin, "LayeredSpan.AddPortalRecord.North.u0");
                    su1 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(alongMax, "LayeredSpan.AddPortalRecord.North.u1");
                    break;
                default:
                    sv0 = sv1 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(tileHeightCm, "LayeredSpan.AddPortalRecord.South.v");
                    su0 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(alongMin, "LayeredSpan.AddPortalRecord.South.u0");
                    su1 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(alongMax, "LayeredSpan.AddPortalRecord.South.u1");
                    break;
            }

            sides[portalCount] = side;
            u0[portalCount] = su0;
            v0[portalCount] = sv0;
            u1[portalCount] = su1;
            v1[portalCount] = sv1;
            leftX[portalCount] = ax;
            leftY[portalCount] = ay;
            leftZ[portalCount] = az;
            rightX[portalCount] = bx;
            rightY[portalCount] = by;
            rightZ[portalCount] = bz;
            clearance[portalCount] = clearanceCm;
            sourceSpans[portalCount] = sourceSpan;
            neighborSpans[portalCount] = neighborSpan;
            portalCount++;
        }

        private static void SortPortals(
            int count,
            Span<NavPortalSide> sides,
            Span<short> u0,
            Span<short> v0,
            Span<short> u1,
            Span<short> v1,
            Span<int> leftX,
            Span<int> leftY,
            Span<int> leftZ,
            Span<int> rightX,
            Span<int> rightY,
            Span<int> rightZ,
            Span<int> clearance,
            Span<int> sourceSpans,
            Span<int> neighborSpans)
        {
            for (int i = 1; i < count; i++)
            {
                int j = i;
                while (j > 0 &&
                       ComparePortal(
                           j,
                           j - 1,
                           sides,
                           u0,
                           v0,
                           u1,
                           v1,
                           leftX,
                           leftY,
                           leftZ,
                           rightX,
                           rightY,
                           rightZ,
                           clearance,
                           sourceSpans,
                           neighborSpans) < 0)
                {
                    SwapPortal(
                        j,
                        j - 1,
                        sides,
                        u0,
                        v0,
                        u1,
                        v1,
                        leftX,
                        leftY,
                        leftZ,
                        rightX,
                        rightY,
                        rightZ,
                        clearance,
                        sourceSpans,
                        neighborSpans);
                    j--;
                }
            }
        }

        private static int ComparePortal(
            int a,
            int b,
            ReadOnlySpan<NavPortalSide> sides,
            ReadOnlySpan<short> u0,
            ReadOnlySpan<short> v0,
            ReadOnlySpan<short> u1,
            ReadOnlySpan<short> v1,
            ReadOnlySpan<int> leftX,
            ReadOnlySpan<int> leftY,
            ReadOnlySpan<int> leftZ,
            ReadOnlySpan<int> rightX,
            ReadOnlySpan<int> rightY,
            ReadOnlySpan<int> rightZ,
            ReadOnlySpan<int> clearance,
            ReadOnlySpan<int> sourceSpans,
            ReadOnlySpan<int> neighborSpans)
        {
            _ = u0;
            _ = v0;
            _ = u1;
            _ = v1;
            if (sides[a] != sides[b]) return ((byte)sides[a]).CompareTo((byte)sides[b]);
            int cmp = CompareXyz(leftX[a], leftY[a], leftZ[a], leftX[b], leftY[b], leftZ[b]);
            if (cmp != 0) return cmp;
            cmp = CompareXyz(rightX[a], rightY[a], rightZ[a], rightX[b], rightY[b], rightZ[b]);
            if (cmp != 0) return cmp;
            if (sourceSpans[a] != sourceSpans[b]) return sourceSpans[a].CompareTo(sourceSpans[b]);
            if (neighborSpans[a] != neighborSpans[b]) return neighborSpans[a].CompareTo(neighborSpans[b]);
            return clearance[a].CompareTo(clearance[b]);
        }

        private static void SwapPortal(
            int a,
            int b,
            Span<NavPortalSide> sides,
            Span<short> u0,
            Span<short> v0,
            Span<short> u1,
            Span<short> v1,
            Span<int> leftX,
            Span<int> leftY,
            Span<int> leftZ,
            Span<int> rightX,
            Span<int> rightY,
            Span<int> rightZ,
            Span<int> clearance,
            Span<int> sourceSpans,
            Span<int> neighborSpans)
        {
            (sides[a], sides[b]) = (sides[b], sides[a]);
            (u0[a], u0[b]) = (u0[b], u0[a]);
            (v0[a], v0[b]) = (v0[b], v0[a]);
            (u1[a], u1[b]) = (u1[b], u1[a]);
            (v1[a], v1[b]) = (v1[b], v1[a]);
            (leftX[a], leftX[b]) = (leftX[b], leftX[a]);
            (leftY[a], leftY[b]) = (leftY[b], leftY[a]);
            (leftZ[a], leftZ[b]) = (leftZ[b], leftZ[a]);
            (rightX[a], rightX[b]) = (rightX[b], rightX[a]);
            (rightY[a], rightY[b]) = (rightY[b], rightY[a]);
            (rightZ[a], rightZ[b]) = (rightZ[b], rightZ[a]);
            (clearance[a], clearance[b]) = (clearance[b], clearance[a]);
            (sourceSpans[a], sourceSpans[b]) = (sourceSpans[b], sourceSpans[a]);
            (neighborSpans[a], neighborSpans[b]) = (neighborSpans[b], neighborSpans[a]);
        }

        private static int SampleHeight(
            NavTriangleSurfaceSnapshot surface,
            int spanIndex,
            ReadOnlySpan<int> spanTriangleIndices,
            int xcm,
            int zcm,
            LayeredSpanHeightRounding rounding,
            LayeredSpanTriangulationScratch output)
        {
            int tri = spanTriangleIndices[spanIndex];
            int va = surface.TriA[tri];
            int vb = surface.TriB[tri];
            int vc = surface.TriC[tri];
            int ax = surface.VertexXcm[va];
            int ay = surface.VertexYcm[va];
            int az = surface.VertexZcm[va];
            int bx = surface.VertexXcm[vb];
            int by = surface.VertexYcm[vb];
            int bz = surface.VertexZcm[vb];
            int cx = surface.VertexXcm[vc];
            int cy = surface.VertexYcm[vc];
            int cz = surface.VertexZcm[vc];

            Int128 abx = (Int128)bx - ax;
            Int128 aby = (Int128)by - ay;
            Int128 abz = (Int128)bz - az;
            Int128 acx = (Int128)cx - ax;
            Int128 acy = (Int128)cy - ay;
            Int128 acz = (Int128)cz - az;
            Int128 dx = (Int128)xcm - ax;
            Int128 dz = (Int128)zcm - az;
            CheckLocalDelta(abx, "SampleHeight", output);
            CheckLocalDelta(aby, "SampleHeight", output);
            CheckLocalDelta(abz, "SampleHeight", output);
            CheckLocalDelta(acx, "SampleHeight", output);
            CheckLocalDelta(acy, "SampleHeight", output);
            CheckLocalDelta(acz, "SampleHeight", output);
            CheckLocalDelta(dx, "SampleHeight", output);
            CheckLocalDelta(dz, "SampleHeight", output);

            Int128 nx = (aby * acz) - (abz * acy);
            Int128 ny = (abz * acx) - (abx * acz);
            Int128 nz = (abx * acy) - (aby * acx);
            if (ny == 0)
            {
                Fail(output, "LayeredSpanTriangulationBuilder SampleHeight requires non-vertical walk-candidate triangle plane.");
            }

            Int128 numer = (ny * ay) - (nx * dx) - (nz * dz);
            return RoundRationalY(numer, ny, rounding, output);
        }

        private static int RoundRationalY(
            Int128 numer,
            Int128 denom,
            LayeredSpanHeightRounding rounding,
            LayeredSpanTriangulationScratch output)
        {
            if (denom == 0)
            {
                Fail(output, "LayeredSpanTriangulationBuilder SampleHeight denominator is zero.");
            }

            if (denom < 0)
            {
                numer = -numer;
                denom = -denom;
            }

            if (rounding == LayeredSpanHeightRounding.FloorTowardNegativeInfinity)
            {
                return FloorDivInt128(numer, denom, "RoundRationalY.FloorTowardNegativeInfinity", output);
            }

            Int128 q = numer / denom;
            Int128 r = numer % denom;
            if (r == 0)
            {
                return CastInt128ToInt(q, "RoundRationalY.RoundHalfAwayFromZero", output);
            }

            Int128 twiceAbsR = r < 0 ? -r * 2 : r * 2;
            if (twiceAbsR >= denom)
            {
                if (numer >= 0)
                {
                    q++;
                }
                else
                {
                    q--;
                }
            }

            return CastInt128ToInt(q, "RoundRationalY.RoundHalfAwayFromZero", output);
        }

        private static int FloorDivInt128(
            Int128 numer,
            Int128 denom,
            string owner,
            LayeredSpanTriangulationScratch output)
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

            return CastInt128ToInt(q, owner, output);
        }

        private static int CastInt128ToInt(Int128 value, string owner, LayeredSpanTriangulationScratch output)
        {
            if (value < int.MinValue || value > int.MaxValue)
            {
                Fail(
                    output,
                    $"LayeredSpanTriangulationBuilder {owner} result outside int range.");
            }

            return (int)value;
        }

        private static void CheckLocalDelta(Int128 delta, string owner, LayeredSpanTriangulationScratch output)
        {
            Int128 abs = delta < 0 ? -delta : delta;
            if (abs > LayeredSpanTriangulationSpec.DemonstrableLocalAbsDeltaCm)
            {
                Fail(
                    output,
                    $"LayeredSpanTriangulationBuilder predicate local delta exceeds LayeredSpanTriangulationSpec.DemonstrableLocalAbsDeltaCm; owner {owner}.");
            }
        }

        private static void CheckLocalDelta(Int128 delta, string owner)
        {
            Int128 abs = delta < 0 ? -delta : delta;
            if (abs > LayeredSpanTriangulationSpec.DemonstrableLocalAbsDeltaCm)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanTriangulationBuilder predicate local delta exceeds LayeredSpanTriangulationSpec.DemonstrableLocalAbsDeltaCm; owner {owner}.");
            }
        }

        private static Int128 Orient2(int ax, int az, int bx, int bz, int cx, int cz)
        {
            Int128 bxL = (Int128)bx - ax;
            Int128 bzL = (Int128)bz - az;
            Int128 cxL = (Int128)cx - ax;
            Int128 czL = (Int128)cz - az;
            CheckLocalDelta(bxL, "Orient2");
            CheckLocalDelta(bzL, "Orient2");
            CheckLocalDelta(cxL, "Orient2");
            CheckLocalDelta(czL, "Orient2");
            return (bxL * czL) - (bzL * cxL);
        }

        private static int Orient2Sign(int ax, int az, int bx, int bz, int cx, int cz)
        {
            Int128 v = Orient2(ax, az, bx, bz, cx, cz);
            if (v > 0) return 1;
            if (v < 0) return -1;
            return 0;
        }

        private static int InCircleSign(int ax, int az, int bx, int bz, int cx, int cz, int dx, int dz)
        {
            Int128 adx = (Int128)ax - dx;
            Int128 adz = (Int128)az - dz;
            Int128 bdx = (Int128)bx - dx;
            Int128 bdz = (Int128)bz - dz;
            Int128 cdx = (Int128)cx - dx;
            Int128 cdz = (Int128)cz - dz;
            CheckLocalDelta(adx, "InCircle");
            CheckLocalDelta(adz, "InCircle");
            CheckLocalDelta(bdx, "InCircle");
            CheckLocalDelta(bdz, "InCircle");
            CheckLocalDelta(cdx, "InCircle");
            CheckLocalDelta(cdz, "InCircle");

            Int128 abdet = (adx * bdz) - (bdx * adz);
            Int128 bcdet = (bdx * cdz) - (cdx * bdz);
            Int128 cadet = (cdx * adz) - (adx * cdz);
            Int128 alift = (adx * adx) + (adz * adz);
            Int128 blift = (bdx * bdx) + (bdz * bdz);
            Int128 clift = (cdx * cdx) + (cdz * cdz);
            Int128 det = (alift * bcdet) + (blift * cadet) + (clift * abdet);
            if (det > 0) return 1;
            if (det < 0) return -1;
            return 0;
        }

        private static bool PointInTriangleStrict(
            int px,
            int pz,
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz)
        {
            Int128 o1 = Orient2(ax, az, bx, bz, px, pz);
            Int128 o2 = Orient2(bx, bz, cx, cz, px, pz);
            Int128 o3 = Orient2(cx, cz, ax, az, px, pz);
            return o1 > 0 && o2 > 0 && o3 > 0;
        }

        private static bool PointInRingStrict(
            int px,
            int pz,
            ReadOnlySpan<int> x,
            ReadOnlySpan<int> z,
            int start,
            int count)
        {
            for (int i = 0; i < count; i++)
            {
                int j = i + 1 == count ? 0 : i + 1;
                if (PointOnSegmentInclusive(x[start + i], z[start + i], x[start + j], z[start + j], px, pz))
                {
                    return false;
                }
            }

            bool inside = false;
            for (int i = 0; i < count; i++)
            {
                int j = i + 1 == count ? 0 : i + 1;
                int xi = x[start + i];
                int zi = z[start + i];
                int xj = x[start + j];
                int zj = z[start + j];
                if ((zi > pz) != (zj > pz))
                {
                    Int128 lhs = ((Int128)pz - zi) * ((Int128)xj - xi);
                    Int128 rhs = ((Int128)px - xi) * ((Int128)zj - zi);
                    bool crossLeft = zj > zi ? lhs > rhs : lhs < rhs;
                    if (crossLeft)
                    {
                        inside = !inside;
                    }
                }
            }

            return inside;
        }

        private static bool PointInActivePolygonStrict(
            int px,
            int pz,
            int polyCount,
            ReadOnlySpan<int> polyX,
            ReadOnlySpan<int> polyZ,
            ReadOnlySpan<int> polyNext,
            ReadOnlySpan<byte> polyActive)
        {
            for (int i = 0; i < polyCount; i++)
            {
                if (polyActive[i] == 0)
                {
                    continue;
                }

                int j = polyNext[i];
                if (PointOnSegmentInclusive(polyX[i], polyZ[i], polyX[j], polyZ[j], px, pz))
                {
                    return false;
                }
            }

            bool inside = false;
            for (int i = 0; i < polyCount; i++)
            {
                if (polyActive[i] == 0)
                {
                    continue;
                }

                int j = polyNext[i];
                int xi = polyX[i];
                int zi = polyZ[i];
                int xj = polyX[j];
                int zj = polyZ[j];
                if ((zi > pz) != (zj > pz))
                {
                    Int128 lhs = ((Int128)pz - zi) * ((Int128)xj - xi);
                    Int128 rhs = ((Int128)px - xi) * ((Int128)zj - zi);
                    bool crossLeft = zj > zi ? lhs > rhs : lhs < rhs;
                    if (crossLeft)
                    {
                        inside = !inside;
                    }
                }
            }

            return inside;
        }

        private static bool SegmentsProperIntersect(
            int ax, int az, int bx, int bz,
            int cx, int cz, int dx, int dz)
        {
            int o1 = Orient2Sign(ax, az, bx, bz, cx, cz);
            int o2 = Orient2Sign(ax, az, bx, bz, dx, dz);
            int o3 = Orient2Sign(cx, cz, dx, dz, ax, az);
            int o4 = Orient2Sign(cx, cz, dx, dz, bx, bz);
            return o1 != 0 && o2 != 0 && o3 != 0 && o4 != 0 && o1 != o2 && o3 != o4;
        }

        private static bool PointOnSegmentInclusive(int ax, int az, int bx, int bz, int px, int pz)
        {
            if (Orient2Sign(ax, az, bx, bz, px, pz) != 0)
            {
                return false;
            }

            return px >= Math.Min(ax, bx) &&
                   px <= Math.Max(ax, bx) &&
                   pz >= Math.Min(az, bz) &&
                   pz <= Math.Max(az, bz);
        }

        private static bool PointOnSegmentInclusiveWide(
            Int128 ax,
            Int128 az,
            Int128 bx,
            Int128 bz,
            Int128 px,
            Int128 pz)
        {
            Int128 abx = bx - ax;
            Int128 abz = bz - az;
            Int128 apx = px - ax;
            Int128 apz = pz - az;
            Int128 cross = (abx * apz) - (abz * apx);
            if (cross != 0)
            {
                return false;
            }

            Int128 minX = ax < bx ? ax : bx;
            Int128 maxX = ax < bx ? bx : ax;
            Int128 minZ = az < bz ? az : bz;
            Int128 maxZ = az < bz ? bz : az;
            return px >= minX && px <= maxX && pz >= minZ && pz <= maxZ;
        }

        private static long Dist2(int ax, int az, int bx, int bz)
        {
            long dx = (long)bx - ax;
            long dz = (long)bz - az;
            return checked(dx * dx + dz * dz);
        }

        private static void EnsurePolyCapacity(LayeredSpanTriangulationScratch output, int required)
        {
            if (required > output.PolygonVertexCapacity)
            {
                Fail(
                    output,
                    $"LayeredSpanTriangulationScratch.polygonVertexCapacity ({output.PolygonVertexCapacity}); required {required}.");
            }
        }

        private static void FailEarClipNoValidEar(
            int chart,
            int outerRing,
            int ownedHoleCount,
            in LayeredSpanTriangulationSpec spec,
            int polyCount,
            Span<int> polyX,
            Span<int> polyZ,
            Span<int> polyNext,
            Span<int> polyPrev,
            Span<byte> polyActive,
            bool allowMidSegmentFlatStrip,
            int activeCount,
            LayeredSpanTriangulationScratch output)
        {
            int convex = 0;
            int reflex = 0;
            int collinear = 0;
            int midSegmentFlatEligible = 0;
            int reverseSpikeEligible = 0;
            int duplicateNeighbor = 0;
            int bridgeDuplicateXz = 0;
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minZ = int.MaxValue;
            int maxZ = int.MinValue;
            for (int tip = 0; tip < polyCount; tip++)
            {
                if (polyActive[tip] == 0)
                {
                    continue;
                }

                int x = polyX[tip];
                int z = polyZ[tip];
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;

                int prev = polyPrev[tip];
                int next = polyNext[tip];
                if ((polyX[tip] == polyX[prev] && polyZ[tip] == polyZ[prev]) ||
                    (polyX[tip] == polyX[next] && polyZ[tip] == polyZ[next]) ||
                    (polyX[prev] == polyX[next] && polyZ[prev] == polyZ[next]))
                {
                    duplicateNeighbor++;
                    continue;
                }

                if (HasOtherActiveDuplicateXz(tip, polyCount, polyX, polyZ, polyActive))
                {
                    bridgeDuplicateXz++;
                }

                int orient = Orient2Sign(polyX[prev], polyZ[prev], polyX[tip], polyZ[tip], polyX[next], polyZ[next]);
                if (orient > 0)
                {
                    convex++;
                }
                else if (orient < 0)
                {
                    reflex++;
                }
                else
                {
                    collinear++;
                    if (PointOnSegmentInclusive(
                            polyX[prev], polyZ[prev], polyX[next], polyZ[next], polyX[tip], polyZ[tip]))
                    {
                        midSegmentFlatEligible++;
                    }
                    else
                    {
                        reverseSpikeEligible++;
                    }
                }
            }

            Fail(
                output,
                "LayeredSpanTriangulationBuilder ear clipping found no valid ear. " +
                $"chart={chart}; outerRing={outerRing}; ownedHoleCount={ownedHoleCount}; " +
                $"target=[{spec.TargetMinXcm},{spec.TargetMinZcm}]-[{spec.TargetMaxXcm},{spec.TargetMaxZcm}]; " +
                $"activeCount={activeCount}; polyCount={polyCount}; allowMidSegmentFlatStrip={allowMidSegmentFlatStrip}; " +
                $"convex={convex}; reflex={reflex}; collinear={collinear}; midSegmentFlatEligible={midSegmentFlatEligible}; " +
                $"reverseSpikeEligible={reverseSpikeEligible}; duplicateNeighbor={duplicateNeighbor}; " +
                $"bridgeDuplicateXz={bridgeDuplicateXz}; activeBBox=[{minX},{minZ}]-[{maxX},{maxZ}].");
        }

        private static void Fail(LayeredSpanTriangulationScratch output, string message)
        {
            output.Reset();
            throw new InvalidOperationException(message);
        }
    }
}
