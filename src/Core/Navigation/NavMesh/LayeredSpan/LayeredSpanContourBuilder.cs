using System;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Stateless deterministic contour chart/ring builder over region-eligible layered spans.
    /// Charts are region+area partitions that never place two sheets in one XZ column.
    /// Boundary edges are conservative full cell sides unless same-chart portals cover the side;
    /// cross-chart portals become seam records and mandatory split endpoints.
    /// Success-path Build after scratch warmup allocates 0 managed bytes.
    /// </summary>
    public static class LayeredSpanContourBuilder
    {
        public static void Build(
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walkability,
            LayeredSpanSurfaceSheetScratch sheets,
            LayeredSpanWalkLinkScratch links,
            LayeredSpanRadiusFieldScratch radius,
            LayeredSpanRegionScratch regions,
            in LayeredSpanRasterGridSpec grid,
            in LayeredSpanContourSpec spec,
            LayeredSpanContourScratch output)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (walkability == null) throw new ArgumentNullException(nameof(walkability));
            if (sheets == null) throw new ArgumentNullException(nameof(sheets));
            if (links == null) throw new ArgumentNullException(nameof(links));
            if (radius == null) throw new ArgumentNullException(nameof(radius));
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            if (output == null) throw new ArgumentNullException(nameof(output));

            output.Reset();

            if (!raw.HasPublishedContent)
            {
                throw new InvalidOperationException(
                    "LayeredSpanContourBuilder requires published raw scratch content.");
            }

            if (!walkability.WasBuiltFrom(raw))
            {
                throw new InvalidOperationException(
                    "LayeredSpanContourBuilder requires walkability output that matches the raw scratch identity and content generation.");
            }

            if (!sheets.WasBuiltFrom(raw))
            {
                throw new InvalidOperationException(
                    "LayeredSpanContourBuilder requires surface-sheet output that matches the raw scratch identity and content generation.");
            }

            if (!links.WasBuiltFrom(raw, walkability))
            {
                throw new InvalidOperationException(
                    "LayeredSpanContourBuilder requires walk-link output that matches the raw/walkability scratch identity and content generation.");
            }

            if (!radius.WasBuiltFrom(raw, walkability, sheets, links))
            {
                throw new InvalidOperationException(
                    "LayeredSpanContourBuilder requires radius-field output that matches the raw/walkability/sheets/links scratch identity and content generation.");
            }

            if (!regions.WasBuiltFrom(raw, walkability, sheets, links, radius))
            {
                throw new InvalidOperationException(
                    "LayeredSpanContourBuilder requires region output that matches the raw/walkability/sheets/links/radius scratch identity and content generation.");
            }

            int columnCount = raw.ColumnCount;
            int spanCount = raw.SpanCount;
            int walkableCount = walkability.WalkableSpanCount;
            int sheetCount = sheets.SheetCount;

            if (columnCount != walkability.ColumnCount ||
                spanCount != walkability.ClassifiedSpanCount ||
                columnCount != sheets.ColumnCount ||
                spanCount != sheets.SpanCount ||
                walkableCount != links.WalkableSpanCount ||
                spanCount != radius.SpanCount ||
                sheetCount != radius.SheetCount ||
                spanCount != regions.SpanCount ||
                columnCount != grid.ColumnCount)
            {
                throw new InvalidOperationException(
                    "LayeredSpanContourBuilder requires consistent column/span/sheet/walkable counts across the six-stage chain and raster grid.");
            }

            if (spanCount > output.SpanCapacity)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanContourScratch.spanCapacity ({output.SpanCapacity}); required {spanCount}.");
            }

            if (sheetCount > output.SheetCapacity)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanContourScratch.sheetCapacity ({output.SheetCapacity}); required {sheetCount}.");
            }

            if (columnCount > output.ColumnCapacity)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanContourScratch.columnCapacity ({output.ColumnCapacity}); required {columnCount}.");
            }

            output.Prepare();

            ReadOnlySpan<int> regionIds = regions.SpanRegionIds;
            ReadOnlySpan<int> sheetIds = sheets.SpanSheetIds;
            ReadOnlySpan<byte> areaIds = raw.SpanAreaIds;
            ReadOnlySpan<int> columnSpanOffsets = raw.ColumnSpanOffsets;
            ReadOnlySpan<int> walkableIndices = walkability.WalkableSpanIndices;
            ReadOnlySpan<int> linkOffsets = links.LinkOffsets;
            ReadOnlySpan<int> neighborSpans = links.LinkNeighborSpanIndices;
            ReadOnlySpan<LayeredSpanNeighborDirection> neighborDirs = links.LinkNeighborDirections;
            ReadOnlySpan<int> portalMinAlong = links.LinkPortalMinAlongCm;
            ReadOnlySpan<int> portalMaxAlong = links.LinkPortalMaxAlongCm;

            Span<int> spanToWalkable = output.MutableSpanToWalkableIndex;
            Span<int> sheetColumn = output.MutableSheetColumn;
            Span<int> sheetRegion = output.MutableSheetRegionIds;
            Span<byte> sheetArea = output.MutableSheetAreaIds;
            Span<int> sheetMinSpan = output.MutableSheetMinSpanIndices;
            Span<byte> sheetEligible = output.MutableSheetEligible;
            Span<int> firstBySheet = output.MutableFirstEligibleSpanBySheet;
            Span<int> nextBySheet = output.MutableNextEligibleSpanBySheet;

            if (spanCount > 0)
            {
                spanToWalkable.Slice(0, spanCount).Fill(-1);
                nextBySheet.Slice(0, spanCount).Fill(-1);
            }

            if (sheetCount > 0)
            {
                sheetColumn.Slice(0, sheetCount).Fill(-1);
                sheetRegion.Slice(0, sheetCount).Fill(-1);
                sheetArea.Slice(0, sheetCount).Clear();
                sheetMinSpan.Slice(0, sheetCount).Fill(int.MaxValue);
                sheetEligible.Slice(0, sheetCount).Clear();
                firstBySheet.Slice(0, sheetCount).Fill(-1);
            }

            for (int w = 0; w < walkableCount; w++)
            {
                spanToWalkable[walkableIndices[w]] = w;
            }

            // Column-range CSR walk: O(columnCount + spanCount). Preserves ascending span order
            // (column offsets are a partition of [0, spanCount)) without ColumnOfSpan scans.
            for (int col = 0; col < columnCount; col++)
            {
                int spanBegin = columnSpanOffsets[col];
                int spanEnd = columnSpanOffsets[col + 1];
                if (spanBegin >= spanEnd)
                {
                    continue;
                }

                if ((uint)spanBegin > (uint)spanCount || (uint)spanEnd > (uint)spanCount || spanBegin < 0)
                {
                    Fail(
                        output,
                        $"LayeredSpanContourBuilder column {col} span range [{spanBegin}, {spanEnd}) is outside spanCount {spanCount}.");
                }

                int cx = col % grid.ColumnCountX;
                int cz = col / grid.ColumnCountX;
                int cellMinX = grid.ColumnMinXcm(cx);
                int cellMaxX = grid.ColumnMaxXcm(cx);
                int cellMinZ = grid.ColumnMinZcm(cz);
                int cellMaxZ = grid.ColumnMaxZcm(cz);
                if (!CellIntersectsTarget(cellMinX, cellMaxX, cellMinZ, cellMaxZ, in spec))
                {
                    // Halo / out-of-target columns never enter contour charts.
                    continue;
                }

                for (int span = spanBegin; span < spanEnd; span++)
                {
                    int regionId = regionIds[span];
                    if (regionId < 0)
                    {
                        continue;
                    }

                    int sheetId = sheetIds[span];
                    if (sheetId < 0 || sheetId >= sheetCount)
                    {
                        Fail(output, "LayeredSpanContourBuilder requires every region-eligible span to carry a valid surface-sheet id.");
                    }

                    if (sheetEligible[sheetId] == 0)
                    {
                        sheetEligible[sheetId] = 1;
                        sheetColumn[sheetId] = col;
                        sheetRegion[sheetId] = regionId;
                        sheetArea[sheetId] = areaIds[span];
                        sheetMinSpan[sheetId] = span;
                    }
                    else
                    {
                        if (sheetColumn[sheetId] != col)
                        {
                            Fail(output, "LayeredSpanContourBuilder LayeredSpanSurfaceSheetScratch requires same-column eligible members.");
                        }

                        if (sheetRegion[sheetId] != regionId)
                        {
                            Fail(
                                output,
                                $"LayeredSpanContourBuilder LayeredSpanRegionScratch.SpanRegionIds disagree inside sheet {sheetId}.");
                        }

                        if (sheetArea[sheetId] != areaIds[span])
                        {
                            Fail(
                                output,
                                $"LayeredSpanContourBuilder LayeredSpanScratch.SpanAreaIds disagree inside sheet {sheetId}.");
                        }

                        if (span < sheetMinSpan[sheetId])
                        {
                            sheetMinSpan[sheetId] = span;
                        }
                    }

                    nextBySheet[span] = firstBySheet[sheetId];
                    firstBySheet[sheetId] = span;
                }
            }

            BuildCanonicalLinksAndCharts(
                sheetCount,
                spanCount,
                walkableCount,
                walkableIndices,
                sheetIds,
                sheetEligible,
                sheetColumn,
                sheetRegion,
                sheetArea,
                sheetMinSpan,
                firstBySheet,
                nextBySheet,
                raw.SpanMinYcm,
                raw.SpanMaxYcm,
                linkOffsets,
                neighborSpans,
                neighborDirs,
                portalMinAlong,
                portalMaxAlong,
                regionIds,
                areaIds,
                output);

            EmitSeams(output);
            EmitBoundaryEdges(raw, grid, in spec, sheetCount, output);
            TraceRingsAndSimplify(in spec, output);

            output.Commit(raw, walkability, sheets, links, radius, regions);
        }

        private static void BuildCanonicalLinksAndCharts(
            int sheetCount,
            int spanCount,
            int walkableCount,
            ReadOnlySpan<int> walkableIndices,
            ReadOnlySpan<int> sheetIds,
            Span<byte> sheetEligible,
            Span<int> sheetColumn,
            Span<int> sheetRegion,
            Span<byte> sheetArea,
            Span<int> sheetMinSpan,
            Span<int> firstBySheet,
            Span<int> nextBySheet,
            ReadOnlySpan<int> spanMinY,
            ReadOnlySpan<int> spanMaxY,
            ReadOnlySpan<int> linkOffsets,
            ReadOnlySpan<int> neighborSpans,
            ReadOnlySpan<LayeredSpanNeighborDirection> neighborDirs,
            ReadOnlySpan<int> portalMinAlong,
            ReadOnlySpan<int> portalMaxAlong,
            ReadOnlySpan<int> regionIds,
            ReadOnlySpan<byte> areaIds,
            LayeredSpanContourScratch output)
        {
            // Contour-local merge of same-column/region/area sheets whose Y ranges overlap.
            // Collapses closed-boundary bleed fragments without merging vertically stacked floors.
            Span<int> parent = output.MutableSheetChartUnionParent;
            Span<int> rank = output.MutableSheetChartUnionRank;
            Span<int> componentMin = output.MutableSheetChartComponentMinSpan;
            for (int s = 0; s < sheetCount; s++)
            {
                parent[s] = s;
                rank[s] = 0;
                componentMin[s] = sheetEligible[s] != 0 ? sheetMinSpan[s] : int.MaxValue;
            }

            // Column -> eligible sheet singly-linked lists (O(sheets)). Bleed merge and later
            // duplicate-column probes only walk same-column chains — never sheetCount^2.
            Span<int> columnSheetFirst = output.MutableColumnSheetFirst;
            Span<int> columnSheetNext = output.MutableColumnSheetNext;
            for (int s = 0; s < sheetCount; s++)
            {
                if (sheetEligible[s] == 0)
                {
                    continue;
                }

                int col = sheetColumn[s];
                if ((uint)col >= (uint)columnSheetFirst.Length)
                {
                    Fail(
                        output,
                        $"LayeredSpanContourBuilder sheet column {col} exceeds columnCapacity ({columnSheetFirst.Length}).");
                }

                columnSheetFirst[col] = -1;
            }

            for (int s = 0; s < sheetCount; s++)
            {
                if (sheetEligible[s] == 0)
                {
                    columnSheetNext[s] = -1;
                    continue;
                }

                int col = sheetColumn[s];
                columnSheetNext[s] = columnSheetFirst[col];
                columnSheetFirst[col] = s;
            }

            for (int a = 0; a < sheetCount; a++)
            {
                if (sheetEligible[a] == 0)
                {
                    continue;
                }

                int col = sheetColumn[a];
                SheetYRange(a, firstBySheet, nextBySheet, spanMinY, spanMaxY, out int aMinY, out int aMaxY);
                for (int b = columnSheetFirst[col]; b >= 0; b = columnSheetNext[b])
                {
                    // Preserve original a < b pair order (each unordered pair once).
                    if (b <= a)
                    {
                        continue;
                    }

                    if (sheetRegion[a] != sheetRegion[b] || sheetArea[a] != sheetArea[b])
                    {
                        continue;
                    }

                    SheetYRange(b, firstBySheet, nextBySheet, spanMinY, spanMaxY, out int bMinY, out int bMaxY);
                    if (aMaxY < bMinY || bMaxY < aMinY)
                    {
                        continue;
                    }

                    Union(Find(a, parent), Find(b, parent), parent, rank, componentMin);
                }
            }

            // Remap each eligible sheet to its bleed-merged representative (min span sheet).
            // O(sheets): one pass records root->rep, second pass assigns sheetRep.
            Span<int> rootRep = output.MutableSheetChartIdByRoot;
            for (int s = 0; s < sheetCount; s++)
            {
                rootRep[s] = -1;
            }

            for (int s = 0; s < sheetCount; s++)
            {
                if (sheetEligible[s] == 0)
                {
                    continue;
                }

                int root = Find(s, parent);
                if (sheetMinSpan[s] == componentMin[root])
                {
                    rootRep[root] = s;
                }
            }

            Span<int> sheetRep = output.MutableChartColumnMarks;
            for (int s = 0; s < sheetCount; s++)
            {
                if (sheetEligible[s] == 0)
                {
                    sheetRep[s] = -1;
                    continue;
                }

                sheetRep[s] = rootRep[Find(s, parent)];
            }

            Span<int> linkSheetA = output.MutableCanonicalLinkSheetA;
            Span<int> linkSheetB = output.MutableCanonicalLinkSheetB;
            Span<int> linkSpanA = output.MutableCanonicalLinkSpanA;
            Span<int> linkSpanB = output.MutableCanonicalLinkSpanB;
            Span<LayeredSpanNeighborDirection> linkDirs = output.MutableCanonicalLinkDirections;
            Span<int> linkPortalMin = output.MutableCanonicalLinkPortalMinAlongCm;
            Span<int> linkPortalMax = output.MutableCanonicalLinkPortalMaxAlongCm;
            int linkCount = 0;

            for (int w = 0; w < walkableCount; w++)
            {
                int src = walkableIndices[w];
                if (regionIds[src] < 0)
                {
                    continue;
                }

                int srcSheet = sheetIds[src];
                if ((uint)srcSheet >= (uint)sheetCount || sheetRep[srcSheet] < 0)
                {
                    continue;
                }

                srcSheet = sheetRep[srcSheet];
                int start = linkOffsets[w];
                int end = linkOffsets[w + 1];
                for (int i = start; i < end; i++)
                {
                    int dst = neighborSpans[i];
                    if ((uint)dst >= (uint)spanCount || regionIds[dst] < 0)
                    {
                        continue;
                    }

                    int dstSheet = sheetIds[dst];
                    if ((uint)dstSheet >= (uint)sheetCount || sheetRep[dstSheet] < 0)
                    {
                        continue;
                    }

                    dstSheet = sheetRep[dstSheet];
                    if (srcSheet == dstSheet)
                    {
                        continue;
                    }

                    int aSheet = srcSheet;
                    int bSheet = dstSheet;
                    int aSpan = src;
                    int bSpan = dst;
                    LayeredSpanNeighborDirection dir = neighborDirs[i];
                    if (bSheet < aSheet || (bSheet == aSheet && bSpan < aSpan))
                    {
                        aSheet = dstSheet;
                        bSheet = srcSheet;
                        aSpan = dst;
                        bSpan = src;
                        dir = Opposite(dir);
                    }

                    if (src > dst)
                    {
                        continue;
                    }

                    if (linkCount >= output.CanonicalLinkCapacity)
                    {
                        Fail(
                            output,
                            $"LayeredSpanContourScratch.canonicalLinkCapacity ({output.CanonicalLinkCapacity}); required {linkCount + 1}.");
                    }

                    linkSheetA[linkCount] = aSheet;
                    linkSheetB[linkCount] = bSheet;
                    linkSpanA[linkCount] = aSpan;
                    linkSpanB[linkCount] = bSpan;
                    linkDirs[linkCount] = dir;
                    linkPortalMin[linkCount] = portalMinAlong[i];
                    linkPortalMax[linkCount] = portalMaxAlong[i];
                    linkCount++;
                }
            }

            // Deterministic O(n log n) canonical link order (published seams/edges re-sort locally,
            // but boundary emit / ring adjacency still need stable link visitation order).
            HeapSortCanonicalLinks(
                linkSheetA,
                linkSheetB,
                linkSpanA,
                linkSpanB,
                linkDirs,
                linkPortalMin,
                linkPortalMax,
                linkCount);
            output.SetCanonicalLinkCount(linkCount);
            BuildSheetCanonicalLinkCsr(sheetCount, linkCount, linkSheetA, linkSheetB, output);

            // Dedicated SoA: component size / member chains / column->sheet (no idle-channel aliases).
            Span<int> componentSize = output.MutableComponentSize;
            Span<int> memberFirst = output.MutableComponentMemberFirst;
            Span<int> memberNext = output.MutableComponentMemberNext;
            Span<int> memberLast = output.MutableComponentMemberLast;

            // Reset union-by-size member chains over representative sheets only.
            for (int s = 0; s < sheetCount; s++)
            {
                parent[s] = s;
                if (sheetEligible[s] != 0)
                {
                    componentSize[s] = 1;
                    componentMin[s] = sheetMinSpan[s];
                    memberFirst[s] = s;
                    memberNext[s] = -1;
                    memberLast[s] = s;
                }
                else
                {
                    componentSize[s] = 0;
                    componentMin[s] = int.MaxValue;
                    memberFirst[s] = -1;
                    memberNext[s] = -1;
                    memberLast[s] = -1;
                }
            }

            // Keep bleed merges as already-united chart seeds: union each sheet to its representative.
            for (int s = 0; s < sheetCount; s++)
            {
                int rep = sheetRep[s];
                if (rep < 0 || rep == s)
                {
                    continue;
                }

                UnionBySize(
                    Find(s, parent),
                    Find(rep, parent),
                    parent,
                    componentSize,
                    componentMin,
                    memberFirst,
                    memberNext,
                    memberLast);
            }

            Span<byte> acceptedUnion = output.MutableCanonicalLinkAcceptedUnion;
            if (linkCount > 0)
            {
                acceptedUnion.Slice(0, linkCount).Clear();
            }

            for (int i = 0; i < linkCount; i++)
            {
                int a = linkSheetA[i];
                int b = linkSheetB[i];
                if (sheetEligible[a] == 0 || sheetEligible[b] == 0)
                {
                    continue;
                }

                if (sheetRegion[a] != sheetRegion[b] || sheetArea[a] != sheetArea[b])
                {
                    continue;
                }

                int rootA = Find(a, parent);
                int rootB = Find(b, parent);
                if (rootA == rootB)
                {
                    acceptedUnion[i] = 1;
                    continue;
                }

                if (ComponentsHaveDuplicateColumn(
                        rootA,
                        rootB,
                        parent,
                        componentSize,
                        memberFirst,
                        memberNext,
                        sheetColumn,
                        columnSheetFirst,
                        columnSheetNext,
                        output))
                {
                    continue;
                }

                UnionBySize(
                    rootA,
                    rootB,
                    parent,
                    componentSize,
                    componentMin,
                    memberFirst,
                    memberNext,
                    memberLast);
                acceptedUnion[i] = 1;
            }

            Span<int> chartIdByRoot = output.MutableSheetChartIdByRoot;
            Span<int> sheetToChart = output.MutableSheetToChart;
            Span<int> chartMin = output.MutableChartMinSpanIndices;
            Span<int> chartRegion = output.MutableChartRegionIds;
            Span<byte> chartArea = output.MutableChartAreaIds;

            int chartCount = 0;
            for (int s = 0; s < sheetCount; s++)
            {
                if (sheetEligible[s] == 0)
                {
                    continue;
                }

                int root = Find(s, parent);
                if (componentMin[root] != sheetMinSpan[s])
                {
                    continue;
                }

                chartCount++;
            }

            if (chartCount > output.ChartCapacity)
            {
                Fail(
                    output,
                    $"LayeredSpanContourScratch.chartCapacity ({output.ChartCapacity}); required {chartCount}.");
            }

            // O(spanCount + sheetCount) chart ids by increasing componentMinSpan.
            // Dedicated span-bucket + sheet-next (no capacity-mismatched aliases).
            // Reverse sheet scan + prepend ⇒ walking each bucket yields ascending sheet index.
            Span<int> chartMinBucketFirst = output.MutableChartMinSpanBucketFirst;
            Span<int> chartMinBucketNext = output.MutableChartMinSpanBucketNext;
            if (spanCount > 0)
            {
                chartMinBucketFirst.Slice(0, spanCount).Fill(-1);
            }

            for (int s = sheetCount - 1; s >= 0; s--)
            {
                if (sheetEligible[s] == 0)
                {
                    chartMinBucketNext[s] = -1;
                    continue;
                }

                int minSpan = sheetMinSpan[s];
                int root = Find(s, parent);
                if (componentMin[root] != minSpan)
                {
                    chartMinBucketNext[s] = -1;
                    continue;
                }

                if ((uint)minSpan >= (uint)spanCount)
                {
                    Fail(
                        output,
                        $"LayeredSpanContourBuilder sheet {s} min-span {minSpan} exceeds spanCount {spanCount}.");
                }

                chartMinBucketNext[s] = chartMinBucketFirst[minSpan];
                chartMinBucketFirst[minSpan] = s;
            }

            int nextId = 0;
            for (int spanProbe = 0; spanProbe < spanCount && nextId < chartCount; spanProbe++)
            {
                for (int s = chartMinBucketFirst[spanProbe]; s >= 0; s = chartMinBucketNext[s])
                {
                    int root = Find(s, parent);
                    int id = nextId++;
                    chartIdByRoot[root] = id;
                    chartMin[id] = spanProbe;
                    chartRegion[id] = sheetRegion[s];
                    chartArea[id] = sheetArea[s];
                }
            }

            for (int s = 0; s < sheetCount; s++)
            {
                if (sheetEligible[s] == 0)
                {
                    sheetToChart[s] = -1;
                    continue;
                }

                int root = Find(s, parent);
                sheetToChart[s] = chartIdByRoot[root];
            }

            output.SetChartCount(chartCount);
            _ = areaIds;
        }

        private static void SheetYRange(
            int sheetId,
            Span<int> firstBySheet,
            Span<int> nextBySheet,
            ReadOnlySpan<int> spanMinY,
            ReadOnlySpan<int> spanMaxY,
            out int minY,
            out int maxY)
        {
            minY = int.MaxValue;
            maxY = int.MinValue;
            for (int span = firstBySheet[sheetId]; span >= 0; span = nextBySheet[span])
            {
                if (spanMinY[span] < minY)
                {
                    minY = spanMinY[span];
                }

                if (spanMaxY[span] > maxY)
                {
                    maxY = spanMaxY[span];
                }
            }

            if (minY == int.MaxValue)
            {
                minY = 0;
                maxY = 0;
            }
        }

        private static bool ComponentsHaveDuplicateColumn(
            int rootA,
            int rootB,
            Span<int> parent,
            Span<int> componentSize,
            Span<int> memberFirst,
            Span<int> memberNext,
            Span<int> sheetColumn,
            Span<int> columnSheetFirst,
            Span<int> columnSheetNext,
            LayeredSpanContourScratch output)
        {
            // True iff both components already own the same column. Multiple sheets inside one
            // component that share a column are bleed-merged fragments and are expected.
            // Scan only the smaller component; probe same-column sheets via the prebuilt index.
            int smaller = rootA;
            int larger = rootB;
            if (componentSize[rootA] > componentSize[rootB] ||
                (componentSize[rootA] == componentSize[rootB] && rootA > rootB))
            {
                smaller = rootB;
                larger = rootA;
            }

            for (int sheet = memberFirst[smaller]; sheet >= 0; sheet = memberNext[sheet])
            {
                int col = sheetColumn[sheet];
                if ((uint)col >= (uint)columnSheetFirst.Length)
                {
                    Fail(
                        output,
                        $"LayeredSpanContourBuilder sheet column {col} exceeds columnCapacity ({columnSheetFirst.Length}).");
                }

                for (int other = columnSheetFirst[col]; other >= 0; other = columnSheetNext[other])
                {
                    if (other == sheet)
                    {
                        continue;
                    }

                    if (Find(other, parent) == larger)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void EmitSeams(LayeredSpanContourScratch output)
        {
            int linkCount = output.CanonicalLinkCount;
            int chartCount = output.ChartCount;
            ReadOnlySpan<int> sheetToChart = output.MutableSheetToChart;
            ReadOnlySpan<byte> acceptedUnion = output.MutableCanonicalLinkAcceptedUnion;
            ReadOnlySpan<int> linkSheetA = output.MutableCanonicalLinkSheetA;
            ReadOnlySpan<int> linkSheetB = output.MutableCanonicalLinkSheetB;
            ReadOnlySpan<int> linkSpanA = output.MutableCanonicalLinkSpanA;
            ReadOnlySpan<int> linkSpanB = output.MutableCanonicalLinkSpanB;
            ReadOnlySpan<LayeredSpanNeighborDirection> linkDirs = output.MutableCanonicalLinkDirections;
            ReadOnlySpan<int> linkPortalMin = output.MutableCanonicalLinkPortalMinAlongCm;
            ReadOnlySpan<int> linkPortalMax = output.MutableCanonicalLinkPortalMaxAlongCm;

            Span<int> seamChartA = output.MutableSeamChartA;
            Span<int> seamChartB = output.MutableSeamChartB;
            Span<LayeredSpanNeighborDirection> seamDirs = output.MutableSeamDirections;
            Span<int> seamPortalMin = output.MutableSeamPortalMinAlongCm;
            Span<int> seamPortalMax = output.MutableSeamPortalMaxAlongCm;
            Span<int> seamSpanA = output.MutableSeamSpanA;
            Span<int> seamSpanB = output.MutableSeamSpanB;
            int seamCount = 0;

            for (int i = 0; i < linkCount; i++)
            {
                int aSheet = linkSheetA[i];
                int bSheet = linkSheetB[i];
                int chartA = sheetToChart[aSheet];
                int chartB = sheetToChart[bSheet];
                if (chartA < 0 || chartB < 0 || chartA == chartB)
                {
                    continue;
                }

                // Cross-chart accepted walk portals are seams whether or not union was rejected for columns.
                _ = acceptedUnion;
                int cA = chartA;
                int cB = chartB;
                int sA = linkSpanA[i];
                int sB = linkSpanB[i];
                LayeredSpanNeighborDirection dir = linkDirs[i];
                if (cB < cA || (cB == cA && sB < sA))
                {
                    cA = chartB;
                    cB = chartA;
                    sA = linkSpanB[i];
                    sB = linkSpanA[i];
                    dir = Opposite(dir);
                }

                if (seamCount >= output.SeamCapacity)
                {
                    Fail(
                        output,
                        $"LayeredSpanContourScratch.seamCapacity ({output.SeamCapacity}); required {seamCount + 1}.");
                }

                // Dedup identical seam keys.
                bool dup = false;
                for (int prev = 0; prev < seamCount; prev++)
                {
                    if (seamChartA[prev] == cA &&
                        seamChartB[prev] == cB &&
                        seamDirs[prev] == dir &&
                        seamPortalMin[prev] == linkPortalMin[i] &&
                        seamPortalMax[prev] == linkPortalMax[i] &&
                        seamSpanA[prev] == sA &&
                        seamSpanB[prev] == sB)
                    {
                        dup = true;
                        break;
                    }
                }

                if (dup)
                {
                    continue;
                }

                seamChartA[seamCount] = cA;
                seamChartB[seamCount] = cB;
                seamDirs[seamCount] = dir;
                seamPortalMin[seamCount] = linkPortalMin[i];
                seamPortalMax[seamCount] = linkPortalMax[i];
                seamSpanA[seamCount] = sA;
                seamSpanB[seamCount] = sB;
                seamCount++;
            }

            InsertionSortSeams(
                seamChartA,
                seamChartB,
                seamDirs,
                seamPortalMin,
                seamPortalMax,
                seamSpanA,
                seamSpanB,
                seamCount);
            output.SetSeamCount(seamCount);
            _ = chartCount;
        }

        private static void EmitBoundaryEdges(
            LayeredSpanScratch raw,
            in LayeredSpanRasterGridSpec grid,
            in LayeredSpanContourSpec spec,
            int sheetCount,
            LayeredSpanContourScratch output)
        {
            _ = raw;
            ReadOnlySpan<byte> sheetEligible = output.MutableSheetEligible;
            ReadOnlySpan<int> sheetColumn = output.MutableSheetColumn;
            ReadOnlySpan<int> sheetToChart = output.MutableSheetToChart;
            ReadOnlySpan<int> firstBySheet = output.MutableFirstEligibleSpanBySheet;

            int colCountX = grid.ColumnCountX;
            Span<int> portalMins = output.MutablePortalMinAlongCm;
            Span<int> portalMaxs = output.MutablePortalMaxAlongCm;
            int edgeCount = 0;

            ReadOnlySpan<int> cSheetA = output.MutableCanonicalLinkSheetA;
            ReadOnlySpan<int> cSheetB = output.MutableCanonicalLinkSheetB;
            ReadOnlySpan<LayeredSpanNeighborDirection> cDirs = output.MutableCanonicalLinkDirections;
            ReadOnlySpan<int> cPortalMin = output.MutableCanonicalLinkPortalMinAlongCm;
            ReadOnlySpan<int> cPortalMax = output.MutableCanonicalLinkPortalMaxAlongCm;
            ReadOnlySpan<int> cSpanA = output.MutableCanonicalLinkSpanA;
            ReadOnlySpan<int> cSpanB = output.MutableCanonicalLinkSpanB;
            int canonicalCount = output.CanonicalLinkCount;
            ReadOnlySpan<int> sheetLinkOffsets = output.MutableSheetCanonicalLinkOffsets;
            ReadOnlySpan<int> sheetLinkIndices = output.MutableSheetCanonicalLinkIndices;

            for (int sheetId = 0; sheetId < sheetCount; sheetId++)
            {
                if (sheetEligible[sheetId] == 0)
                {
                    continue;
                }

                int chartId = sheetToChart[sheetId];
                if (chartId < 0)
                {
                    continue;
                }

                // Emit once per bleed-merged representative sheet.
                if (output.MutableChartColumnMarks[sheetId] != sheetId)
                {
                    continue;
                }

                int column = sheetColumn[sheetId];
                int cx = column % colCountX;
                int cz = column / colCountX;
                int minX = grid.ColumnMinXcm(cx);
                int maxX = grid.ColumnMaxXcm(cx);
                int minZ = grid.ColumnMinZcm(cz);
                int maxZ = grid.ColumnMaxZcm(cz);
                if (!CellIntersectsTarget(minX, maxX, minZ, maxZ, in spec))
                {
                    continue;
                }

                int sourceSpan = firstBySheet[sheetId];
                int linkStart = sheetLinkOffsets[sheetId];
                int linkEnd = sheetLinkOffsets[sheetId + 1];

                EmitSideIfBoundary(
                    sheetId,
                    chartId,
                    sourceSpan,
                    LayeredSpanNeighborDirection.West,
                    minX,
                    minZ,
                    maxZ,
                    minX,
                    maxX,
                    minZ,
                    maxZ,
                    in spec,
                    cSheetA,
                    cSheetB,
                    cDirs,
                    cPortalMin,
                    cPortalMax,
                    cSpanA,
                    cSpanB,
                    sheetToChart,
                    sheetLinkIndices,
                    linkStart,
                    linkEnd,
                    portalMins,
                    portalMaxs,
                    ref edgeCount,
                    output);
                EmitSideIfBoundary(
                    sheetId,
                    chartId,
                    sourceSpan,
                    LayeredSpanNeighborDirection.East,
                    maxX,
                    minZ,
                    maxZ,
                    minX,
                    maxX,
                    minZ,
                    maxZ,
                    in spec,
                    cSheetA,
                    cSheetB,
                    cDirs,
                    cPortalMin,
                    cPortalMax,
                    cSpanA,
                    cSpanB,
                    sheetToChart,
                    sheetLinkIndices,
                    linkStart,
                    linkEnd,
                    portalMins,
                    portalMaxs,
                    ref edgeCount,
                    output);
                EmitSideIfBoundary(
                    sheetId,
                    chartId,
                    sourceSpan,
                    LayeredSpanNeighborDirection.North,
                    minZ,
                    minX,
                    maxX,
                    minX,
                    maxX,
                    minZ,
                    maxZ,
                    in spec,
                    cSheetA,
                    cSheetB,
                    cDirs,
                    cPortalMin,
                    cPortalMax,
                    cSpanA,
                    cSpanB,
                    sheetToChart,
                    sheetLinkIndices,
                    linkStart,
                    linkEnd,
                    portalMins,
                    portalMaxs,
                    ref edgeCount,
                    output);
                EmitSideIfBoundary(
                    sheetId,
                    chartId,
                    sourceSpan,
                    LayeredSpanNeighborDirection.South,
                    maxZ,
                    minX,
                    maxX,
                    minX,
                    maxX,
                    minZ,
                    maxZ,
                    in spec,
                    cSheetA,
                    cSheetB,
                    cDirs,
                    cPortalMin,
                    cPortalMax,
                    cSpanA,
                    cSpanB,
                    sheetToChart,
                    sheetLinkIndices,
                    linkStart,
                    linkEnd,
                    portalMins,
                    portalMaxs,
                    ref edgeCount,
                    output);
            }

            output.SetEdgeCount(edgeCount);
            _ = canonicalCount;
        }

        private static void BuildSheetCanonicalLinkCsr(
            int sheetCount,
            int linkCount,
            ReadOnlySpan<int> linkSheetA,
            ReadOnlySpan<int> linkSheetB,
            LayeredSpanContourScratch output)
        {
            Span<int> counts = output.MutableSheetCanonicalLinkCounts;
            Span<int> offsets = output.MutableSheetCanonicalLinkOffsets;
            Span<int> indices = output.MutableSheetCanonicalLinkIndices;
            counts.Clear();
            for (int i = 0; i < linkCount; i++)
            {
                int a = linkSheetA[i];
                int b = linkSheetB[i];
                if ((uint)a >= (uint)sheetCount || (uint)b >= (uint)sheetCount)
                {
                    Fail(
                        output,
                        $"LayeredSpanContourBuilder canonical link sheet out of range (a={a}, b={b}, sheetCount={sheetCount}).");
                }

                counts[a]++;
                if (a != b)
                {
                    counts[b]++;
                }
            }

            int cursor = 0;
            for (int s = 0; s < sheetCount; s++)
            {
                offsets[s] = cursor;
                cursor = checked(cursor + counts[s]);
                counts[s] = 0;
            }

            offsets[sheetCount] = cursor;
            if (cursor > indices.Length)
            {
                Fail(
                    output,
                    $"LayeredSpanContourScratch sheetCanonicalLinkIndices capacity ({indices.Length}); required {cursor}.");
            }

            for (int i = 0; i < linkCount; i++)
            {
                int a = linkSheetA[i];
                int b = linkSheetB[i];
                int slotA = offsets[a] + counts[a];
                indices[slotA] = i;
                counts[a]++;
                if (a != b)
                {
                    int slotB = offsets[b] + counts[b];
                    indices[slotB] = i;
                    counts[b]++;
                }
            }

            // Counts are only a CSR build scratch; clear so a later reader never sees stale tallies.
            counts.Clear();
        }

        private static void EmitSideIfBoundary(
            int sheetId,
            int chartId,
            int sourceSpan,
            LayeredSpanNeighborDirection direction,
            int fixedCoord,
            int sideMin,
            int sideMax,
            int cellMinX,
            int cellMaxX,
            int cellMinZ,
            int cellMaxZ,
            in LayeredSpanContourSpec spec,
            ReadOnlySpan<int> cSheetA,
            ReadOnlySpan<int> cSheetB,
            ReadOnlySpan<LayeredSpanNeighborDirection> cDirs,
            ReadOnlySpan<int> cPortalMin,
            ReadOnlySpan<int> cPortalMax,
            ReadOnlySpan<int> cSpanA,
            ReadOnlySpan<int> cSpanB,
            ReadOnlySpan<int> sheetToChart,
            ReadOnlySpan<int> sheetLinkIndices,
            int sheetLinkStart,
            int sheetLinkEnd,
            Span<int> portalMins,
            Span<int> portalMaxs,
            ref int edgeCount,
            LayeredSpanContourScratch output)
        {
            int portalCount = 0;
            for (int slot = sheetLinkStart; slot < sheetLinkEnd; slot++)
            {
                int i = sheetLinkIndices[slot];
                bool matchA = cSheetA[i] == sheetId && SameDirOrOpposite(cDirs[i], direction, fromA: true);
                bool matchB = cSheetB[i] == sheetId && SameDirOrOpposite(cDirs[i], direction, fromA: false);
                if (!matchA && !matchB)
                {
                    continue;
                }

                int otherSheet = matchA ? cSheetB[i] : cSheetA[i];
                int otherChart = sheetToChart[otherSheet];
                if (otherChart != chartId)
                {
                    continue;
                }

                long lo = cPortalMin[i];
                long hi = cPortalMax[i];
                if (lo > hi)
                {
                    long tmp = lo;
                    lo = hi;
                    hi = tmp;
                }

                if (hi <= sideMin || lo >= sideMax)
                {
                    continue;
                }

                if (lo < sideMin)
                {
                    lo = sideMin;
                }

                if (hi > sideMax)
                {
                    hi = sideMax;
                }

                if (hi <= lo)
                {
                    continue;
                }

                if (portalCount >= portalMins.Length)
                {
                    Fail(
                        output,
                        $"LayeredSpanContourScratch.portalIntervalCapacity ({portalMins.Length}); required {portalCount + 1}.");
                }

                portalMins[portalCount] = (int)lo;
                portalMaxs[portalCount] = (int)hi;
                portalCount++;
            }

            if (portalCount > 0)
            {
                InsertionSortPortals(portalMins, portalMaxs, portalCount);
                MergeTouchingPortals(portalMins, portalMaxs, ref portalCount);
                if (portalMins[0] <= sideMin)
                {
                    long coveredTo = portalMaxs[0];
                    bool gap = false;
                    for (int i = 1; i < portalCount; i++)
                    {
                        if (portalMins[i] > coveredTo)
                        {
                            gap = true;
                            break;
                        }

                        if (portalMaxs[i] > coveredTo)
                        {
                            coveredTo = portalMaxs[i];
                        }
                    }

                    if (!gap && coveredTo >= sideMax)
                    {
                        return;
                    }
                }
            }

            // Collect split points: side endpoints + portal endpoints (incident links) + target-border intersections.
            Span<int> splitAlong = output.MutableSplitAlongCm;
            Span<byte> splitMandatory = output.MutableSplitMandatory;
            int splitCount = 0;
            AddSplit(sideMin, mandatory: false, splitAlong, splitMandatory, ref splitCount, output);
            AddSplit(sideMax, mandatory: false, splitAlong, splitMandatory, ref splitCount, output);

            for (int slot = sheetLinkStart; slot < sheetLinkEnd; slot++)
            {
                int i = sheetLinkIndices[slot];
                bool involves = cSheetA[i] == sheetId || cSheetB[i] == sheetId;
                if (!involves)
                {
                    continue;
                }

                LayeredSpanNeighborDirection dirFromSheet =
                    cSheetA[i] == sheetId ? cDirs[i] : Opposite(cDirs[i]);
                if (dirFromSheet != direction)
                {
                    continue;
                }

                AddSplit(cPortalMin[i], mandatory: true, splitAlong, splitMandatory, ref splitCount, output);
                AddSplit(cPortalMax[i], mandatory: true, splitAlong, splitMandatory, ref splitCount, output);
            }

            // Target-border intersections on this side.
            if (direction == LayeredSpanNeighborDirection.West || direction == LayeredSpanNeighborDirection.East)
            {
                if (fixedCoord == spec.TargetMinXcm || fixedCoord == spec.TargetMaxXcm)
                {
                    if (sideMin < spec.TargetMaxZcm && sideMax > spec.TargetMinZcm)
                    {
                        int lo = sideMin > spec.TargetMinZcm ? sideMin : spec.TargetMinZcm;
                        int hi = sideMax < spec.TargetMaxZcm ? sideMax : spec.TargetMaxZcm;
                        AddSplit(lo, mandatory: true, splitAlong, splitMandatory, ref splitCount, output);
                        AddSplit(hi, mandatory: true, splitAlong, splitMandatory, ref splitCount, output);
                    }
                }

                if (spec.TargetMinZcm > sideMin && spec.TargetMinZcm < sideMax &&
                    fixedCoord >= spec.TargetMinXcm && fixedCoord <= spec.TargetMaxXcm)
                {
                    AddSplit(spec.TargetMinZcm, mandatory: true, splitAlong, splitMandatory, ref splitCount, output);
                }

                if (spec.TargetMaxZcm > sideMin && spec.TargetMaxZcm < sideMax &&
                    fixedCoord >= spec.TargetMinXcm && fixedCoord <= spec.TargetMaxXcm)
                {
                    AddSplit(spec.TargetMaxZcm, mandatory: true, splitAlong, splitMandatory, ref splitCount, output);
                }
            }
            else
            {
                if (fixedCoord == spec.TargetMinZcm || fixedCoord == spec.TargetMaxZcm)
                {
                    if (sideMin < spec.TargetMaxXcm && sideMax > spec.TargetMinXcm)
                    {
                        int lo = sideMin > spec.TargetMinXcm ? sideMin : spec.TargetMinXcm;
                        int hi = sideMax < spec.TargetMaxXcm ? sideMax : spec.TargetMaxXcm;
                        AddSplit(lo, mandatory: true, splitAlong, splitMandatory, ref splitCount, output);
                        AddSplit(hi, mandatory: true, splitAlong, splitMandatory, ref splitCount, output);
                    }
                }

                if (spec.TargetMinXcm > sideMin && spec.TargetMinXcm < sideMax &&
                    fixedCoord >= spec.TargetMinZcm && fixedCoord <= spec.TargetMaxZcm)
                {
                    AddSplit(spec.TargetMinXcm, mandatory: true, splitAlong, splitMandatory, ref splitCount, output);
                }

                if (spec.TargetMaxXcm > sideMin && spec.TargetMaxXcm < sideMax &&
                    fixedCoord >= spec.TargetMinZcm && fixedCoord <= spec.TargetMaxZcm)
                {
                    AddSplit(spec.TargetMaxXcm, mandatory: true, splitAlong, splitMandatory, ref splitCount, output);
                }
            }

            InsertionSortSplits(splitAlong, splitMandatory, splitCount);

            for (int i = 0; i + 1 < splitCount; i++)
            {
                int a = splitAlong[i];
                int b = splitAlong[i + 1];
                if (b <= a)
                {
                    continue;
                }

                // Clip segment to target rectangle.
                if (!TryClipSideSegment(
                        direction,
                        fixedCoord,
                        a,
                        b,
                        in spec,
                        out int fromX,
                        out int fromZ,
                        out int toX,
                        out int toZ,
                        out bool fromMand,
                        out bool toMand))
                {
                    continue;
                }

                // Directed edge keeps chart interior on the mathematical-CCW left for Z-down grids:
                // West: S->N, North: W->E, East: N->S, South: E->W.
                OrientBoundaryEdge(direction, ref fromX, ref fromZ, ref toX, ref toZ, ref fromMand, ref toMand);

                bool segFromMand = fromMand || splitMandatory[i] != 0 || IsOnTargetBorder(fromX, fromZ, in spec);
                bool segToMand = toMand || splitMandatory[i + 1] != 0 || IsOnTargetBorder(toX, toZ, in spec);

                if (edgeCount >= output.EdgeCapacity)
                {
                    Fail(
                        output,
                        $"LayeredSpanContourScratch.edgeCapacity ({output.EdgeCapacity}); required {edgeCount + 1}.");
                }

                output.MutableEdgeFromXcm[edgeCount] = fromX;
                output.MutableEdgeFromZcm[edgeCount] = fromZ;
                output.MutableEdgeToXcm[edgeCount] = toX;
                output.MutableEdgeToZcm[edgeCount] = toZ;
                output.MutableEdgeChartIds[edgeCount] = chartId;
                output.MutableEdgeSourceSpanIndices[edgeCount] = sourceSpan;
                output.MutableEdgeFromMandatory[edgeCount] = (byte)(segFromMand ? 1 : 0);
                output.MutableEdgeToMandatory[edgeCount] = (byte)(segToMand ? 1 : 0);
                edgeCount++;
            }

            _ = cellMinX;
            _ = cellMaxX;
            _ = cellMinZ;
            _ = cellMaxZ;
            _ = cSpanA;
            _ = cSpanB;
        }

        private static void TraceRingsAndSimplify(in LayeredSpanContourSpec spec, LayeredSpanContourScratch output)
        {
            int edgeCount = output.EdgeCount;
            if (edgeCount == 0)
            {
                output.MutableChartRingOffsets[0] = 0;
                output.MutableRingOffsets[0] = 0;
                output.SetRingCount(0);
                output.SetVertexCount(0);
                return;
            }

            Span<byte> edgeUsed = output.MutableEdgeUsed;
            edgeUsed.Slice(0, edgeCount).Clear();

            // Build per-chart vertex-key adjacency of outgoing edges.
            BuildEdgeAdjacency(edgeCount, output);

            Span<int> ringOffsets = output.MutableRingOffsets;
            Span<int> ringChartIds = output.MutableRingChartIds;
            Span<int> ringRegionIds = output.MutableRingRegionIds;
            Span<byte> ringAreaIds = output.MutableRingAreaIds;
            Span<Int128> ringSignedArea2 = output.MutableRingSignedArea2;
            Span<LayeredSpanContourRingKind> ringKinds = output.MutableRingKinds;
            Span<int> outX = output.MutableVertexXcm;
            Span<int> outZ = output.MutableVertexZcm;
            Span<int> outSpan = output.MutableVertexSourceSpanIndices;
            Span<byte> outMand = output.MutableVertexMandatory;
            Span<int> chartRegion = output.MutableChartRegionIds;
            Span<byte> chartArea = output.MutableChartAreaIds;
            Span<int> chartRingOffsets = output.MutableChartRingOffsets;
            Span<int> ringOrder = output.MutableRingOrder;

            int ringCount = 0;
            int vertexCount = 0;
            int chartCount = output.ChartCount;
            chartRingOffsets[0] = 0;

            // Trace rings chart-by-chart in chart id order.
            for (int chartId = 0; chartId < chartCount; chartId++)
            {
                int chartRingStart = ringCount;
                for (int startEdge = 0; startEdge < edgeCount; startEdge++)
                {
                    if (edgeUsed[startEdge] != 0 || output.MutableEdgeChartIds[startEdge] != chartId)
                    {
                        continue;
                    }

                    TraceOneRing(
                        startEdge,
                        chartId,
                        in spec,
                        ref ringCount,
                        ref vertexCount,
                        ringOffsets,
                        ringChartIds,
                        ringRegionIds,
                        ringAreaIds,
                        ringSignedArea2,
                        ringKinds,
                        outX,
                        outZ,
                        outSpan,
                        outMand,
                        chartRegion,
                        chartArea,
                        edgeUsed,
                        output);
                }

                // Finalize CSR end so ring sort can read offsets[ring+1].
                ringOffsets[ringCount] = vertexCount;

                // Sort rings of this chart: outer before hole, then min vertex key.
                int chartRingCount = ringCount - chartRingStart;
                for (int i = 0; i < chartRingCount; i++)
                {
                    ringOrder[i] = chartRingStart + i;
                }

                for (int i = 1; i < chartRingCount; i++)
                {
                    int key = ringOrder[i];
                    int j = i - 1;
                    while (j >= 0 && RingSortLess(key, ringOrder[j], ringKinds, ringOffsets, outX, outZ))
                    {
                        ringOrder[j + 1] = ringOrder[j];
                        j--;
                    }

                    ringOrder[j + 1] = key;
                }

                if (chartRingCount > 1)
                {
                    ReorderChartRings(
                        chartRingStart,
                        chartRingCount,
                        vertexCount,
                        ringOrder,
                        ringOffsets,
                        ringChartIds,
                        ringRegionIds,
                        ringAreaIds,
                        ringSignedArea2,
                        ringKinds,
                        outX,
                        outZ,
                        outSpan,
                        outMand,
                        output);
                }

                chartRingOffsets[chartId + 1] = ringCount;

                ValidateChartContainment(
                    chartRingStart,
                    ringCount,
                    vertexCount,
                    ringOffsets,
                    ringKinds,
                    outX,
                    outZ,
                    output);
                ValidateChartRingPairEdges(
                    chartRingStart,
                    ringCount,
                    vertexCount,
                    ringOffsets,
                    outX,
                    outZ,
                    output);
            }

            ringOffsets[ringCount] = vertexCount;
            output.SetRingCount(ringCount);
            output.SetVertexCount(vertexCount);
        }

        private static void TraceOneRing(
            int startEdge,
            int chartId,
            in LayeredSpanContourSpec spec,
            ref int ringCount,
            ref int vertexCount,
            Span<int> ringOffsets,
            Span<int> ringChartIds,
            Span<int> ringRegionIds,
            Span<byte> ringAreaIds,
            Span<Int128> ringSignedArea2,
            Span<LayeredSpanContourRingKind> ringKinds,
            Span<int> outX,
            Span<int> outZ,
            Span<int> outSpan,
            Span<byte> outMand,
            Span<int> chartRegion,
            Span<byte> chartArea,
            Span<byte> edgeUsed,
            LayeredSpanContourScratch output)
        {
            Span<int> traceX = output.MutableTraceXcm;
            Span<int> traceZ = output.MutableTraceZcm;
            Span<int> traceSpan = output.MutableTraceSourceSpan;
            Span<byte> traceMand = output.MutableTraceMandatory;

            int edgeCount = output.EdgeCount;
            int curEdge = startEdge;
            int startX = output.MutableEdgeFromXcm[startEdge];
            int startZ = output.MutableEdgeFromZcm[startEdge];
            int tCount = 0;

            while (true)
            {
                if (edgeUsed[curEdge] != 0)
                {
                    Fail(output, "LayeredSpanContourBuilder malformed topology: edge reused during ring trace.");
                }

                edgeUsed[curEdge] = 1;
                int fromX = output.MutableEdgeFromXcm[curEdge];
                int fromZ = output.MutableEdgeFromZcm[curEdge];
                int toX = output.MutableEdgeToXcm[curEdge];
                int toZ = output.MutableEdgeToZcm[curEdge];
                if (tCount >= output.VertexCapacity)
                {
                    Fail(
                        output,
                        $"LayeredSpanContourScratch.vertexCapacity ({output.VertexCapacity}); required {tCount + 1}.");
                }

                traceX[tCount] = fromX;
                traceZ[tCount] = fromZ;
                traceSpan[tCount] = output.MutableEdgeSourceSpanIndices[curEdge];
                traceMand[tCount] = output.MutableEdgeFromMandatory[curEdge];
                tCount++;

                if (toX == startX && toZ == startZ)
                {
                    // Closed. Carry mandatory onto start if needed.
                    if (output.MutableEdgeToMandatory[curEdge] != 0)
                    {
                        traceMand[0] = 1;
                    }

                    break;
                }

                int next = ChooseNextEdge(curEdge, fromX, fromZ, toX, toZ, chartId, edgeUsed, output);
                if (next < 0)
                {
                    Fail(
                        output,
                        "LayeredSpanContourBuilder malformed topology: dead-end during closed ring trace.");
                }

                // Propagate mandatory at shared vertex.
                if (output.MutableEdgeToMandatory[curEdge] != 0)
                {
                    output.MutableEdgeFromMandatory[next] = 1;
                }

                curEdge = next;
                if (tCount > edgeCount + 1)
                {
                    Fail(
                        output,
                        "LayeredSpanContourBuilder malformed topology: ring trace exceeded edge count without closing.");
                }
            }

            if (tCount < 3)
            {
                Fail(output, "LayeredSpanContourBuilder ring has fewer than 3 vertices.");
            }

            // Exact duplicate / collinear simplification, then optional nonzero-error.
            int kept = SimplifyRing(
                traceX,
                traceZ,
                traceSpan,
                traceMand,
                tCount,
                in spec,
                output);

            Int128 area2 = ComputeSignedArea2(traceX, traceZ, kept);
            if (area2 == 0)
            {
                Fail(output, "LayeredSpanContourBuilder ring has zero signed area after simplification.");
            }

            if (HasSelfIntersection(traceX, traceZ, kept))
            {
                Fail(output, "LayeredSpanContourBuilder ring self-intersects after simplification.");
            }

            if (ringCount >= output.RingCapacity)
            {
                Fail(
                    output,
                    $"LayeredSpanContourScratch.ringCapacity ({output.RingCapacity}); required {ringCount + 1}.");
            }

            if (vertexCount + kept > output.VertexCapacity)
            {
                Fail(
                    output,
                    $"LayeredSpanContourScratch.vertexCapacity ({output.VertexCapacity}); required {vertexCount + kept}.");
            }

            ringOffsets[ringCount] = vertexCount;
            for (int i = 0; i < kept; i++)
            {
                outX[vertexCount + i] = traceX[i];
                outZ[vertexCount + i] = traceZ[i];
                outSpan[vertexCount + i] = traceSpan[i];
                outMand[vertexCount + i] = traceMand[i];
            }

            vertexCount += kept;
            ringChartIds[ringCount] = chartId;
            ringRegionIds[ringCount] = chartRegion[chartId];
            ringAreaIds[ringCount] = chartArea[chartId];
            ringSignedArea2[ringCount] = area2;
            ringKinds[ringCount] = area2 > 0
                ? LayeredSpanContourRingKind.Outer
                : LayeredSpanContourRingKind.Hole;
            ringCount++;
        }

        private static int SimplifyRing(
            Span<int> x,
            Span<int> z,
            Span<int> spans,
            Span<byte> mand,
            int count,
            in LayeredSpanContourSpec spec,
            LayeredSpanContourScratch output)
        {
            // Pass 1: remove exact duplicate consecutive vertices (preserve mandatory by OR).
            int w = 0;
            for (int i = 0; i < count; i++)
            {
                if (w > 0 && x[w - 1] == x[i] && z[w - 1] == z[i])
                {
                    if (mand[i] != 0)
                    {
                        mand[w - 1] = 1;
                    }

                    continue;
                }

                x[w] = x[i];
                z[w] = z[i];
                spans[w] = spans[i];
                mand[w] = mand[i];
                w++;
            }

            // Close-duplicate against first.
            if (w > 1 && x[0] == x[w - 1] && z[0] == z[w - 1])
            {
                if (mand[w - 1] != 0)
                {
                    mand[0] = 1;
                }

                w--;
            }

            if (w < 3)
            {
                Fail(output, "LayeredSpanContourBuilder ring collapsed below 3 vertices during duplicate removal.");
            }

            // Pass 2: exact collinear removal (safe).
            w = RemoveExactCollinear(x, z, spans, mand, w, output);

            if (spec.MaxSimplificationErrorCm > 0)
            {
                w = RemoveWithinError(
                    x,
                    z,
                    spans,
                    mand,
                    w,
                    spec.MaxSimplificationErrorCm,
                    output);
            }

            return w;
        }

        private static int RemoveExactCollinear(
            Span<int> x,
            Span<int> z,
            Span<int> spans,
            Span<byte> mand,
            int count,
            LayeredSpanContourScratch output)
        {
            Span<int> keep = output.MutableSimplifyKeep;
            for (int i = 0; i < count; i++)
            {
                keep[i] = 1;
            }

            bool changed = true;
            while (changed)
            {
                changed = false;
                int alive = 0;
                for (int i = 0; i < count; i++)
                {
                    if (keep[i] != 0)
                    {
                        alive++;
                    }
                }

                if (alive < 3)
                {
                    Fail(output, "LayeredSpanContourBuilder ring collapsed below 3 vertices during collinear removal.");
                }

                for (int i = 0; i < count; i++)
                {
                    if (keep[i] == 0 || mand[i] != 0)
                    {
                        continue;
                    }

                    int prev = PrevKept(keep, count, i);
                    int next = NextKept(keep, count, i);
                    if (Orientation(x[prev], z[prev], x[i], z[i], x[next], z[next]) != 0)
                    {
                        continue;
                    }

                    // Exact collinear and i between prev/next on the segment.
                    if (!PointOnSegmentInclusive(x[prev], z[prev], x[next], z[next], x[i], z[i]))
                    {
                        continue;
                    }

                    Int128 areaBefore = ComputeSignedArea2Kept(x, z, keep, count);
                    keep[i] = 0;
                    Int128 areaAfter = ComputeSignedArea2Kept(x, z, keep, count);
                    if (areaAfter == 0 || Sign(areaAfter) != Sign(areaBefore))
                    {
                        keep[i] = 1;
                        continue;
                    }

                    changed = true;
                }
            }

            return CompactKept(x, z, spans, mand, keep, count);
        }

        private static int RemoveWithinError(
            Span<int> x,
            Span<int> z,
            Span<int> spans,
            Span<byte> mand,
            int count,
            int maxErrorCm,
            LayeredSpanContourScratch output)
        {
            Span<int> keep = output.MutableSimplifyKeep;
            for (int i = 0; i < count; i++)
            {
                keep[i] = 1;
            }

            Int128 maxErr2 = (Int128)maxErrorCm * maxErrorCm;
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < count; i++)
                {
                    if (keep[i] == 0 || mand[i] != 0)
                    {
                        continue;
                    }

                    int prev = PrevKept(keep, count, i);
                    int next = NextKept(keep, count, i);
                    if (!PointWithinSegmentError(
                            x[i], z[i], x[prev], z[prev], x[next], z[next], maxErr2))
                    {
                        continue;
                    }

                    // Chord must not intersect non-adjacent kept edges.
                    if (ChordIntersectsNonAdjacent(x, z, keep, count, prev, next))
                    {
                        continue;
                    }

                    Int128 areaBefore = ComputeSignedArea2Kept(x, z, keep, count);
                    keep[i] = 0;
                    Int128 areaAfter = ComputeSignedArea2Kept(x, z, keep, count);
                    if (areaAfter == 0 || Sign(areaAfter) != Sign(areaBefore))
                    {
                        keep[i] = 1;
                        continue;
                    }

                    // Containment relative to other rings is validated after all rings of the chart exist;
                    // per-ring winding/area preserved here.
                    changed = true;
                }
            }

            int w = CompactKept(x, z, spans, mand, keep, count);
            if (w < 3)
            {
                Fail(output, "LayeredSpanContourBuilder ring collapsed below 3 vertices during error simplification.");
            }

            return w;
        }

        private static void ValidateChartContainment(
            int ringStart,
            int ringEnd,
            int vertexCount,
            Span<int> ringOffsets,
            Span<LayeredSpanContourRingKind> ringKinds,
            Span<int> x,
            Span<int> z,
            LayeredSpanContourScratch output)
        {
            for (int h = ringStart; h < ringEnd; h++)
            {
                if (ringKinds[h] != LayeredSpanContourRingKind.Hole)
                {
                    continue;
                }

                int hStart = ringOffsets[h];
                int hEnd = h + 1 < ringEnd ? ringOffsets[h + 1] : vertexCount;
                int hCount = hEnd - hStart;
                int containing = -1;
                for (int o = ringStart; o < ringEnd; o++)
                {
                    if (ringKinds[o] != LayeredSpanContourRingKind.Outer)
                    {
                        continue;
                    }

                    int oStart = ringOffsets[o];
                    int oEnd = o + 1 < ringEnd ? ringOffsets[o + 1] : vertexCount;
                    if (HoleStrictlyInsideOuter(x, z, hStart, hCount, oStart, oEnd - oStart))
                    {
                        if (containing >= 0)
                        {
                            Fail(output, "LayeredSpanContourBuilder hole has ambiguous containing outer ring.");
                        }

                        containing = o;
                    }
                }

                if (containing < 0)
                {
                    Fail(output, "LayeredSpanContourBuilder hole has no containing outer ring.");
                }
            }
        }

        private static void ValidateChartRingPairEdges(
            int ringStart,
            int ringEnd,
            int vertexCount,
            Span<int> ringOffsets,
            Span<int> x,
            Span<int> z,
            LayeredSpanContourScratch output)
        {
            for (int a = ringStart; a < ringEnd; a++)
            {
                int aStart = ringOffsets[a];
                int aEnd = a + 1 < ringEnd ? ringOffsets[a + 1] : vertexCount;
                int aCount = aEnd - aStart;
                for (int b = a + 1; b < ringEnd; b++)
                {
                    int bStart = ringOffsets[b];
                    int bEnd = b + 1 < ringEnd ? ringOffsets[b + 1] : vertexCount;
                    int bCount = bEnd - bStart;
                    for (int i = 0; i < aCount; i++)
                    {
                        int i2 = i + 1 == aCount ? 0 : i + 1;
                        int ax0 = x[aStart + i];
                        int az0 = z[aStart + i];
                        int ax1 = x[aStart + i2];
                        int az1 = z[aStart + i2];
                        for (int j = 0; j < bCount; j++)
                        {
                            int j2 = j + 1 == bCount ? 0 : j + 1;
                            if (SegmentsIntersectInclusive(
                                    ax0, az0, ax1, az1,
                                    x[bStart + j], z[bStart + j],
                                    x[bStart + j2], z[bStart + j2]))
                            {
                                Fail(
                                    output,
                                    "LayeredSpanContourBuilder chart rings improperly intersect or touch.");
                            }
                        }
                    }
                }
            }
        }

        private static bool HoleStrictlyInsideOuter(
            Span<int> x,
            Span<int> z,
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

        private static void BuildEdgeAdjacency(int edgeCount, LayeredSpanContourScratch output)
        {
            Span<int> keyX = output.MutableVertexKeyXcm;
            Span<int> keyZ = output.MutableVertexKeyZcm;
            Span<int> first = output.MutableVertexKeyFirstEdge;
            Span<int> next = output.MutableVertexKeyNextEdge;
            int keyCount = 0;

            for (int e = 0; e < edgeCount; e++)
            {
                next[e] = -1;
            }

            for (int e = 0; e < edgeCount; e++)
            {
                int x = output.MutableEdgeFromXcm[e];
                int z = output.MutableEdgeFromZcm[e];
                int key = -1;
                for (int k = 0; k < keyCount; k++)
                {
                    if (keyX[k] == x && keyZ[k] == z)
                    {
                        key = k;
                        break;
                    }
                }

                if (key < 0)
                {
                    if (keyCount >= output.EdgeCapacity)
                    {
                        Fail(
                            output,
                            $"LayeredSpanContourScratch.edgeCapacity ({output.EdgeCapacity}); required {keyCount + 1} (vertexKeys).");
                    }

                    key = keyCount++;
                    keyX[key] = x;
                    keyZ[key] = z;
                    first[key] = e;
                }
                else
                {
                    next[e] = first[key];
                    first[key] = e;
                }
            }

            output.SetVertexKeyCount(keyCount);
        }

        private static int ChooseNextEdge(
            int curEdge,
            int prevX,
            int prevZ,
            int curX,
            int curZ,
            int chartId,
            Span<byte> edgeUsed,
            LayeredSpanContourScratch output)
        {
            int keyCount = output.VertexKeyCount;
            ReadOnlySpan<int> keyX = output.MutableVertexKeyXcm;
            ReadOnlySpan<int> keyZ = output.MutableVertexKeyZcm;
            ReadOnlySpan<int> first = output.MutableVertexKeyFirstEdge;
            ReadOnlySpan<int> next = output.MutableVertexKeyNextEdge;

            int key = -1;
            for (int k = 0; k < keyCount; k++)
            {
                if (keyX[k] == curX && keyZ[k] == curZ)
                {
                    key = k;
                    break;
                }
            }

            if (key < 0)
            {
                return -1;
            }

            Int128 inDx = (Int128)curX - prevX;
            Int128 inDz = (Int128)curZ - prevZ;
            int best = -1;
            for (int e = first[key]; e >= 0; e = next[e])
            {
                if (edgeUsed[e] != 0 || output.MutableEdgeChartIds[e] != chartId || e == curEdge)
                {
                    continue;
                }

                Int128 outDx = (Int128)output.MutableEdgeToXcm[e] - curX;
                Int128 outDz = (Int128)output.MutableEdgeToZcm[e] - curZ;
                if (outDx == 0 && outDz == 0)
                {
                    continue;
                }

                // Skip immediate reverse unless it is the only option (degenerate).
                if (outDx == -inDx && outDz == -inDz)
                {
                    if (best >= 0)
                    {
                        continue;
                    }
                }

                if (best < 0 || PreferLeftTurn(
                        inDx,
                        inDz,
                        outDx,
                        outDz,
                        (Int128)output.MutableEdgeToXcm[best] - curX,
                        (Int128)output.MutableEdgeToZcm[best] - curZ))
                {
                    best = e;
                }
            }

            return best;
        }

        private static bool PreferLeftTurn(
            Int128 inDx, Int128 inDz, Int128 aDx, Int128 aDz, Int128 bDx, Int128 bDz)
        {
            Int128 crossA = (inDx * aDz) - (inDz * aDx);
            Int128 crossB = (inDx * bDz) - (inDz * bDx);
            if (crossA != crossB)
            {
                // Larger cross => more left turn in X/Z with Z-down matching the contour min-angle convention.
                return crossA > crossB;
            }

            Int128 dotA = (inDx * aDx) + (inDz * aDz);
            Int128 dotB = (inDx * bDx) + (inDz * bDz);
            if (dotA != dotB)
            {
                return dotA > dotB;
            }

            // Deterministic tie-break on outgoing vector.
            if (aDx != bDx)
            {
                return aDx < bDx;
            }

            return aDz < bDz;
        }

        private static void ReorderChartRings(
            int ringStart,
            int chartRingCount,
            int vertexCount,
            Span<int> order,
            Span<int> ringOffsets,
            Span<int> ringChartIds,
            Span<int> ringRegionIds,
            Span<byte> ringAreaIds,
            Span<Int128> ringSignedArea2,
            Span<LayeredSpanContourRingKind> ringKinds,
            Span<int> outX,
            Span<int> outZ,
            Span<int> outSpan,
            Span<byte> outMand,
            LayeredSpanContourScratch output)
        {
            int vertexBase = ringOffsets[ringStart];
            int totalVerts = vertexCount - vertexBase;
            if (totalVerts > output.VertexCapacity)
            {
                Fail(output, "LayeredSpanContourBuilder reorder exceeds vertex capacity.");
            }

            if (chartRingCount + 1 > output.EdgeCapacity)
            {
                Fail(output, "LayeredSpanContourBuilder reorder temporary offset storage exceeds edge capacity.");
            }

            // Snapshot source ring ends (last ring ends at vertexCount).
            Span<int> srcEnds = output.MutableAdjOffsets;
            for (int i = 0; i < chartRingCount; i++)
            {
                int ring = ringStart + i;
                srcEnds[i] = i + 1 < chartRingCount ? ringOffsets[ring + 1] : vertexCount;
            }

            Span<int> tmpX = output.MutableTraceXcm;
            Span<int> tmpZ = output.MutableTraceZcm;
            Span<int> tmpSpan = output.MutableTraceSourceSpan;
            Span<byte> tmpMand = output.MutableTraceMandatory;
            Span<int> newOffsets = output.MutableAdjEdgeIndices;

            int write = 0;
            for (int i = 0; i < chartRingCount; i++)
            {
                int srcRing = order[i];
                int local = srcRing - ringStart;
                int s = ringOffsets[srcRing];
                int e = srcEnds[local];
                newOffsets[i] = write;
                for (int v = s; v < e; v++)
                {
                    tmpX[write] = outX[v];
                    tmpZ[write] = outZ[v];
                    tmpSpan[write] = outSpan[v];
                    tmpMand[write] = outMand[v];
                    write++;
                }
            }

            newOffsets[chartRingCount] = write;
            for (int i = 0; i < write; i++)
            {
                outX[vertexBase + i] = tmpX[i];
                outZ[vertexBase + i] = tmpZ[i];
                outSpan[vertexBase + i] = tmpSpan[i];
                outMand[vertexBase + i] = tmpMand[i];
            }

            // Apply metadata permutation via temporary slots in portal scratch (int pairs).
            if (chartRingCount > output.PortalIntervalCapacity)
            {
                Fail(
                    output,
                    $"LayeredSpanContourScratch.portalIntervalCapacity ({output.PortalIntervalCapacity}); required {chartRingCount} (ringReorder).");
            }

            Span<int> tmpRegion = output.MutablePortalMinAlongCm;
            Span<int> tmpAreaAsInt = output.MutablePortalMaxAlongCm;
            for (int i = 0; i < chartRingCount; i++)
            {
                int src = order[i];
                tmpRegion[i] = ringRegionIds[src];
                tmpAreaAsInt[i] = ringAreaIds[src];
            }

            // kind + signed area2 + chart id via simplifyKeep / chart column scratch.
            Span<int> tmpChart = output.MutableChartColumnScratch;
            Span<int> tmpKind = output.MutableSimplifyKeep;
            for (int i = 0; i < chartRingCount; i++)
            {
                int src = order[i];
                tmpChart[i] = ringChartIds[src];
                tmpKind[i] = (int)ringKinds[src];
            }

            // signed area2 via dedicated Int128 reorder scratch (no long narrowing).
            Span<Int128> areaTmp = output.MutableRingReorderSignedArea2;
            for (int i = 0; i < chartRingCount; i++)
            {
                areaTmp[i] = ringSignedArea2[order[i]];
            }

            for (int i = 0; i < chartRingCount; i++)
            {
                ringChartIds[ringStart + i] = tmpChart[i];
                ringRegionIds[ringStart + i] = tmpRegion[i];
                ringAreaIds[ringStart + i] = (byte)tmpAreaAsInt[i];
                ringKinds[ringStart + i] = (LayeredSpanContourRingKind)tmpKind[i];
                ringSignedArea2[ringStart + i] = areaTmp[i];
                ringOffsets[ringStart + i] = vertexBase + newOffsets[i];
            }

            ringOffsets[ringStart + chartRingCount] = vertexBase + newOffsets[chartRingCount];
        }

        private static bool RingSortLess(
            int left,
            int right,
            Span<LayeredSpanContourRingKind> kinds,
            Span<int> offsets,
            Span<int> x,
            Span<int> z)
        {
            // Outer before hole.
            if (kinds[left] != kinds[right])
            {
                return kinds[left] == LayeredSpanContourRingKind.Outer;
            }

            int lStart = offsets[left];
            int rStart = offsets[right];
            int lCount = offsets[left + 1] - lStart;
            int rCount = offsets[right + 1] - rStart;
            int lMin = MinVertexIndex(x, z, lStart, lCount);
            int rMin = MinVertexIndex(x, z, rStart, rCount);
            int lx = x[lMin];
            int lz = z[lMin];
            int rx = x[rMin];
            int rz = z[rMin];
            if (lx != rx)
            {
                return lx < rx;
            }

            if (lz != rz)
            {
                return lz < rz;
            }

            return left < right;
        }

        private static int MinVertexIndex(Span<int> x, Span<int> z, int start, int count)
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

        private static void Fail(LayeredSpanContourScratch output, string message)
        {
            output.Reset();
            throw new InvalidOperationException(message);
        }

        private static int Find(int sheet, Span<int> parent)
        {
            int root = sheet;
            while (parent[root] != root)
            {
                root = parent[root];
            }

            while (parent[sheet] != root)
            {
                int n = parent[sheet];
                parent[sheet] = root;
                sheet = n;
            }

            return root;
        }

        private static void Union(int rootL, int rootR, Span<int> parent, Span<int> rank, Span<int> componentMin)
        {
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

        /// <summary>
        /// Union-by-size with deterministic equal-size tie (lower root index wins) and O(1) member-list
        /// concat via dedicated first/last pointers. Used only for chart partitioning conflict checks.
        /// </summary>
        private static void UnionBySize(
            int rootL,
            int rootR,
            Span<int> parent,
            Span<int> componentSize,
            Span<int> componentMin,
            Span<int> memberFirst,
            Span<int> memberNext,
            Span<int> memberLast)
        {
            if (rootL == rootR)
            {
                return;
            }

            // Prefer larger component as surviving root; equal size -> lower index (deterministic).
            if (componentSize[rootL] < componentSize[rootR] ||
                (componentSize[rootL] == componentSize[rootR] && rootL > rootR))
            {
                int tmp = rootL;
                rootL = rootR;
                rootR = tmp;
            }

            int headR = memberFirst[rootR];
            if (headR >= 0)
            {
                int lastR = memberLast[rootR];
                memberNext[lastR] = memberFirst[rootL];
                if (memberFirst[rootL] < 0)
                {
                    memberLast[rootL] = lastR;
                }

                memberFirst[rootL] = headR;
                memberFirst[rootR] = -1;
                memberLast[rootR] = -1;
            }

            parent[rootR] = rootL;
            componentSize[rootL] += componentSize[rootR];
            if (componentMin[rootR] < componentMin[rootL])
            {
                componentMin[rootL] = componentMin[rootR];
            }
        }

        private static LayeredSpanNeighborDirection Opposite(LayeredSpanNeighborDirection d)
            => d switch
            {
                LayeredSpanNeighborDirection.West => LayeredSpanNeighborDirection.East,
                LayeredSpanNeighborDirection.East => LayeredSpanNeighborDirection.West,
                LayeredSpanNeighborDirection.North => LayeredSpanNeighborDirection.South,
                LayeredSpanNeighborDirection.South => LayeredSpanNeighborDirection.North,
                _ => d
            };

        private static bool SameDirOrOpposite(LayeredSpanNeighborDirection stored, LayeredSpanNeighborDirection want, bool fromA)
        {
            LayeredSpanNeighborDirection d = fromA ? stored : Opposite(stored);
            return d == want;
        }

        private static bool CellIntersectsTarget(
            int minX,
            int maxX,
            int minZ,
            int maxZ,
            in LayeredSpanContourSpec spec)
            => minX < spec.TargetMaxXcm &&
               maxX > spec.TargetMinXcm &&
               minZ < spec.TargetMaxZcm &&
               maxZ > spec.TargetMinZcm;

        private static bool IsOnTargetBorder(int x, int z, in LayeredSpanContourSpec spec)
            => x == spec.TargetMinXcm ||
               x == spec.TargetMaxXcm ||
               z == spec.TargetMinZcm ||
               z == spec.TargetMaxZcm;

        private static void AddSplit(
            int along,
            bool mandatory,
            Span<int> splitAlong,
            Span<byte> splitMandatory,
            ref int splitCount,
            LayeredSpanContourScratch output)
        {
            for (int i = 0; i < splitCount; i++)
            {
                if (splitAlong[i] == along)
                {
                    if (mandatory)
                    {
                        splitMandatory[i] = 1;
                    }

                    return;
                }
            }

            if (splitCount >= output.SplitPointCapacity)
            {
                Fail(
                    output,
                    $"LayeredSpanContourScratch.splitPointCapacity ({output.SplitPointCapacity}); required {splitCount + 1}.");
            }

            splitAlong[splitCount] = along;
            splitMandatory[splitCount] = (byte)(mandatory ? 1 : 0);
            splitCount++;
        }

        private static void OrientBoundaryEdge(
            LayeredSpanNeighborDirection direction,
            ref int fromX,
            ref int fromZ,
            ref int toX,
            ref int toZ,
            ref bool fromMand,
            ref bool toMand)
        {
            switch (direction)
            {
                case LayeredSpanNeighborDirection.West:
                    // S -> N
                    if (fromZ < toZ)
                    {
                        Swap(ref fromX, ref fromZ, ref toX, ref toZ, ref fromMand, ref toMand);
                    }

                    return;
                case LayeredSpanNeighborDirection.East:
                    // N -> S
                    if (fromZ > toZ)
                    {
                        Swap(ref fromX, ref fromZ, ref toX, ref toZ, ref fromMand, ref toMand);
                    }

                    return;
                case LayeredSpanNeighborDirection.North:
                    // W -> E
                    if (fromX > toX)
                    {
                        Swap(ref fromX, ref fromZ, ref toX, ref toZ, ref fromMand, ref toMand);
                    }

                    return;
                case LayeredSpanNeighborDirection.South:
                    // E -> W
                    if (fromX < toX)
                    {
                        Swap(ref fromX, ref fromZ, ref toX, ref toZ, ref fromMand, ref toMand);
                    }

                    return;
            }
        }

        private static void Swap(ref int fromX, ref int fromZ, ref int toX, ref int toZ, ref bool fromMand, ref bool toMand)
        {
            int tx = fromX;
            int tz = fromZ;
            bool tm = fromMand;
            fromX = toX;
            fromZ = toZ;
            fromMand = toMand;
            toX = tx;
            toZ = tz;
            toMand = tm;
        }

        private static bool TryClipSideSegment(
            LayeredSpanNeighborDirection direction,
            int fixedCoord,
            int a,
            int b,
            in LayeredSpanContourSpec spec,
            out int fromX,
            out int fromZ,
            out int toX,
            out int toZ,
            out bool fromMand,
            out bool toMand)
        {
            fromX = fromZ = toX = toZ = 0;
            fromMand = toMand = false;
            if (direction == LayeredSpanNeighborDirection.West || direction == LayeredSpanNeighborDirection.East)
            {
                if (fixedCoord < spec.TargetMinXcm || fixedCoord > spec.TargetMaxXcm)
                {
                    return false;
                }

                int lo = a > spec.TargetMinZcm ? a : spec.TargetMinZcm;
                int hi = b < spec.TargetMaxZcm ? b : spec.TargetMaxZcm;
                if (hi <= lo)
                {
                    return false;
                }

                fromX = fixedCoord;
                toX = fixedCoord;
                fromZ = lo;
                toZ = hi;
                fromMand = lo == spec.TargetMinZcm || lo == spec.TargetMaxZcm || fixedCoord == spec.TargetMinXcm || fixedCoord == spec.TargetMaxXcm;
                toMand = hi == spec.TargetMinZcm || hi == spec.TargetMaxZcm || fixedCoord == spec.TargetMinXcm || fixedCoord == spec.TargetMaxXcm;
                return true;
            }
            else
            {
                if (fixedCoord < spec.TargetMinZcm || fixedCoord > spec.TargetMaxZcm)
                {
                    return false;
                }

                int lo = a > spec.TargetMinXcm ? a : spec.TargetMinXcm;
                int hi = b < spec.TargetMaxXcm ? b : spec.TargetMaxXcm;
                if (hi <= lo)
                {
                    return false;
                }

                fromZ = fixedCoord;
                toZ = fixedCoord;
                fromX = lo;
                toX = hi;
                fromMand = lo == spec.TargetMinXcm || lo == spec.TargetMaxXcm || fixedCoord == spec.TargetMinZcm || fixedCoord == spec.TargetMaxZcm;
                toMand = hi == spec.TargetMinXcm || hi == spec.TargetMaxXcm || fixedCoord == spec.TargetMinZcm || fixedCoord == spec.TargetMaxZcm;
                return true;
            }
        }

        private static Int128 ComputeSignedArea2(Span<int> x, Span<int> z, int count)
        {
            Int128 area2 = 0;
            for (int i = 0; i < count; i++)
            {
                int j = i + 1 == count ? 0 : i + 1;
                area2 += ((Int128)x[i] * z[j]) - ((Int128)x[j] * z[i]);
            }

            return area2;
        }

        private static Int128 ComputeSignedArea2Kept(Span<int> x, Span<int> z, Span<int> keep, int count)
        {
            Int128 area2 = 0;
            int first = -1;
            int prev = -1;
            for (int i = 0; i < count; i++)
            {
                if (keep[i] == 0)
                {
                    continue;
                }

                if (first < 0)
                {
                    first = i;
                    prev = i;
                    continue;
                }

                area2 += ((Int128)x[prev] * z[i]) - ((Int128)x[i] * z[prev]);
                prev = i;
            }

            if (first >= 0 && prev >= 0 && first != prev)
            {
                area2 += ((Int128)x[prev] * z[first]) - ((Int128)x[first] * z[prev]);
            }

            return area2;
        }

        private static int Orientation(int ax, int az, int bx, int bz, int cx, int cz)
        {
            Int128 v = (((Int128)bx - ax) * ((Int128)cz - az)) - (((Int128)bz - az) * ((Int128)cx - ax));
            if (v > 0)
            {
                return 1;
            }

            if (v < 0)
            {
                return -1;
            }

            return 0;
        }

        private static bool PointOnSegmentInclusive(int ax, int az, int bx, int bz, int px, int pz)
        {
            if (Orientation(ax, az, bx, bz, px, pz) != 0)
            {
                return false;
            }

            return px >= Math.Min(ax, bx) &&
                   px <= Math.Max(ax, bx) &&
                   pz >= Math.Min(az, bz) &&
                   pz <= Math.Max(az, bz);
        }

        /// <summary>
        /// Exact chord-error predicate: endpoint cases compare squared distances in Int128;
        /// interior projection compares cross虏 against maxError虏 * |ab|虏. No division/rounding.
        /// </summary>
        private static bool PointWithinSegmentError(
            int px, int pz, int ax, int az, int bx, int bz, Int128 maxErr2)
        {
            Int128 abx = (Int128)bx - ax;
            Int128 abz = (Int128)bz - az;
            Int128 apx = (Int128)px - ax;
            Int128 apz = (Int128)pz - az;
            Int128 ab2 = (abx * abx) + (abz * abz);
            Int128 ap2 = (apx * apx) + (apz * apz);
            if (ab2 == 0)
            {
                return ap2 <= maxErr2;
            }

            Int128 dot = (apx * abx) + (apz * abz);
            if (dot <= 0)
            {
                return ap2 <= maxErr2;
            }

            if (dot >= ab2)
            {
                Int128 bpx = (Int128)px - bx;
                Int128 bpz = (Int128)pz - bz;
                return ((bpx * bpx) + (bpz * bpz)) <= maxErr2;
            }

            Int128 cross = (apx * abz) - (apz * abx);
            return (cross * cross) <= (maxErr2 * ab2);
        }

        private static bool ChordIntersectsNonAdjacent(
            Span<int> x,
            Span<int> z,
            Span<int> keep,
            int count,
            int prev,
            int next)
        {
            for (int i = 0; i < count; i++)
            {
                if (keep[i] == 0)
                {
                    continue;
                }

                int j = NextKept(keep, count, i);
                if (i == prev && j == next)
                {
                    continue;
                }

                // Adjacent to chord endpoints share a vertex 鈥?allow touching at endpoints only.
                if (i == prev || j == prev || i == next || j == next)
                {
                    if (SegmentsProperIntersect(x[prev], z[prev], x[next], z[next], x[i], z[i], x[j], z[j]))
                    {
                        return true;
                    }

                    continue;
                }

                if (SegmentsIntersectInclusive(x[prev], z[prev], x[next], z[next], x[i], z[i], x[j], z[j]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SegmentsProperIntersect(
            int ax, int az, int bx, int bz,
            int cx, int cz, int dx, int dz)
        {
            int o1 = Orientation(ax, az, bx, bz, cx, cz);
            int o2 = Orientation(ax, az, bx, bz, dx, dz);
            int o3 = Orientation(cx, cz, dx, dz, ax, az);
            int o4 = Orientation(cx, cz, dx, dz, bx, bz);
            return o1 != 0 && o2 != 0 && o3 != 0 && o4 != 0 && o1 != o2 && o3 != o4;
        }

        private static bool SegmentsIntersectInclusive(
            int ax, int az, int bx, int bz,
            int cx, int cz, int dx, int dz)
        {
            int o1 = Orientation(ax, az, bx, bz, cx, cz);
            int o2 = Orientation(ax, az, bx, bz, dx, dz);
            int o3 = Orientation(cx, cz, dx, dz, ax, az);
            int o4 = Orientation(cx, cz, dx, dz, bx, bz);
            if (o1 != o2 && o3 != o4)
            {
                return true;
            }

            if (o1 == 0 && PointOnSegmentInclusive(ax, az, bx, bz, cx, cz)) return true;
            if (o2 == 0 && PointOnSegmentInclusive(ax, az, bx, bz, dx, dz)) return true;
            if (o3 == 0 && PointOnSegmentInclusive(cx, cz, dx, dz, ax, az)) return true;
            if (o4 == 0 && PointOnSegmentInclusive(cx, cz, dx, dz, bx, bz)) return true;
            return false;
        }

        private static bool HasSelfIntersection(Span<int> x, Span<int> z, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int i2 = i + 1 == count ? 0 : i + 1;
                for (int j = i + 1; j < count; j++)
                {
                    int j2 = j + 1 == count ? 0 : j + 1;

                    // Skip adjacent edges sharing a vertex.
                    if (i2 == j || j2 == i)
                    {
                        continue;
                    }

                    // Also skip the pair that closes the ring adjacent to edge 0.
                    if ((i == 0 && j2 == 0) || (j == 0 && i2 == 0))
                    {
                        continue;
                    }

                    if (SegmentsIntersectInclusive(x[i], z[i], x[i2], z[i2], x[j], z[j], x[j2], z[j2]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Test hook: inclusive nonadjacent self-intersection / collinear-overlap rejection used by the formal validator.
        /// </summary>
        internal static bool RingHasInclusiveSelfIntersectionForTests(ReadOnlySpan<int> xcm, ReadOnlySpan<int> zcm)
        {
            if (xcm.Length != zcm.Length)
            {
                throw new ArgumentException("xcm and zcm lengths must match.", nameof(zcm));
            }

            Span<int> x = stackalloc int[xcm.Length];
            Span<int> z = stackalloc int[zcm.Length];
            xcm.CopyTo(x);
            zcm.CopyTo(z);
            return HasSelfIntersection(x, z, xcm.Length);
        }

        /// <summary>
        /// Test hook: hole-touch / containment / pairwise edge validation clears output via Fail.
        /// </summary>
        internal static void ValidateChartTopologyForTests(
            LayeredSpanContourScratch output,
            int ringStart,
            int ringEnd,
            int vertexCount)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            ValidateChartContainment(
                ringStart,
                ringEnd,
                vertexCount,
                output.MutableRingOffsets,
                output.MutableRingKinds,
                output.MutableVertexXcm,
                output.MutableVertexZcm,
                output);
            ValidateChartRingPairEdges(
                ringStart,
                ringEnd,
                vertexCount,
                output.MutableRingOffsets,
                output.MutableVertexXcm,
                output.MutableVertexZcm,
                output);
        }

        private static bool PointInRingStrict(int px, int pz, Span<int> x, Span<int> z, int start, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int j = i + 1 == count ? 0 : i + 1;
                if (PointOnSegmentInclusive(
                        x[start + i], z[start + i], x[start + j], z[start + j], px, pz))
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
                    // Exact: px < xi + (xj-xi)*(pz-zi)/(zj-zi) without division/overflow.
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

        private static int PrevKept(Span<int> keep, int count, int i)
        {
            int j = i;
            do
            {
                j = j == 0 ? count - 1 : j - 1;
            }
            while (keep[j] == 0);

            return j;
        }

        private static int NextKept(Span<int> keep, int count, int i)
        {
            int j = i;
            do
            {
                j = j + 1 == count ? 0 : j + 1;
            }
            while (keep[j] == 0);

            return j;
        }

        private static int CompactKept(
            Span<int> x,
            Span<int> z,
            Span<int> spans,
            Span<byte> mand,
            Span<int> keep,
            int count)
        {
            int w = 0;
            for (int i = 0; i < count; i++)
            {
                if (keep[i] == 0)
                {
                    continue;
                }

                x[w] = x[i];
                z[w] = z[i];
                spans[w] = spans[i];
                mand[w] = mand[i];
                w++;
            }

            return w;
        }

        private static int Sign(Int128 v) => v > 0 ? 1 : v < 0 ? -1 : 0;

        private static void InsertionSortPortals(Span<int> mins, Span<int> maxs, int count)
        {
            for (int i = 1; i < count; i++)
            {
                int min = mins[i];
                int max = maxs[i];
                int j = i - 1;
                while (j >= 0 && (mins[j] > min || (mins[j] == min && maxs[j] > max)))
                {
                    mins[j + 1] = mins[j];
                    maxs[j + 1] = maxs[j];
                    j--;
                }

                mins[j + 1] = min;
                maxs[j + 1] = max;
            }
        }

        private static void MergeTouchingPortals(Span<int> mins, Span<int> maxs, ref int count)
        {
            if (count <= 1)
            {
                return;
            }

            int write = 0;
            for (int read = 1; read < count; read++)
            {
                if (mins[read] <= maxs[write])
                {
                    if (maxs[read] > maxs[write])
                    {
                        maxs[write] = maxs[read];
                    }
                }
                else
                {
                    write++;
                    mins[write] = mins[read];
                    maxs[write] = maxs[read];
                }
            }

            count = write + 1;
        }

        private static void InsertionSortSplits(Span<int> along, Span<byte> mandatory, int count)
        {
            for (int i = 1; i < count; i++)
            {
                int a = along[i];
                byte m = mandatory[i];
                int j = i - 1;
                while (j >= 0 && along[j] > a)
                {
                    along[j + 1] = along[j];
                    mandatory[j + 1] = mandatory[j];
                    j--;
                }

                along[j + 1] = a;
                mandatory[j + 1] = m;
            }
        }

        private static void HeapSortCanonicalLinks(
            Span<int> sheetA,
            Span<int> sheetB,
            Span<int> spanA,
            Span<int> spanB,
            Span<LayeredSpanNeighborDirection> dirs,
            Span<int> portalMin,
            Span<int> portalMax,
            int count)
        {
            for (int start = (count / 2) - 1; start >= 0; start--)
            {
                SiftDownCanonicalLink(sheetA, sheetB, spanA, spanB, dirs, portalMin, portalMax, start, count);
            }

            for (int end = count - 1; end > 0; end--)
            {
                SwapCanonicalLink(sheetA, sheetB, spanA, spanB, dirs, portalMin, portalMax, 0, end);
                SiftDownCanonicalLink(sheetA, sheetB, spanA, spanB, dirs, portalMin, portalMax, 0, end);
            }
        }

        private static void SiftDownCanonicalLink(
            Span<int> sheetA,
            Span<int> sheetB,
            Span<int> spanA,
            Span<int> spanB,
            Span<LayeredSpanNeighborDirection> dirs,
            Span<int> portalMin,
            Span<int> portalMax,
            int root,
            int count)
        {
            while (true)
            {
                int left = (2 * root) + 1;
                if (left >= count)
                {
                    return;
                }

                int right = left + 1;
                int greatest = left;
                if (right < count &&
                    CanonicalLinkGreater(
                        sheetA[right], sheetB[right], spanA[right], spanB[right], dirs[right], portalMin[right], portalMax[right],
                        sheetA[left], sheetB[left], spanA[left], spanB[left], dirs[left], portalMin[left], portalMax[left]))
                {
                    greatest = right;
                }

                if (!CanonicalLinkGreater(
                        sheetA[greatest], sheetB[greatest], spanA[greatest], spanB[greatest], dirs[greatest], portalMin[greatest], portalMax[greatest],
                        sheetA[root], sheetB[root], spanA[root], spanB[root], dirs[root], portalMin[root], portalMax[root]))
                {
                    return;
                }

                SwapCanonicalLink(sheetA, sheetB, spanA, spanB, dirs, portalMin, portalMax, root, greatest);
                root = greatest;
            }
        }

        private static void SwapCanonicalLink(
            Span<int> sheetA,
            Span<int> sheetB,
            Span<int> spanA,
            Span<int> spanB,
            Span<LayeredSpanNeighborDirection> dirs,
            Span<int> portalMin,
            Span<int> portalMax,
            int i,
            int j)
        {
            int tsa = sheetA[i]; sheetA[i] = sheetA[j]; sheetA[j] = tsa;
            int tsb = sheetB[i]; sheetB[i] = sheetB[j]; sheetB[j] = tsb;
            int tpa = spanA[i]; spanA[i] = spanA[j]; spanA[j] = tpa;
            int tpb = spanB[i]; spanB[i] = spanB[j]; spanB[j] = tpb;
            LayeredSpanNeighborDirection td = dirs[i]; dirs[i] = dirs[j]; dirs[j] = td;
            int tmin = portalMin[i]; portalMin[i] = portalMin[j]; portalMin[j] = tmin;
            int tmax = portalMax[i]; portalMax[i] = portalMax[j]; portalMax[j] = tmax;
        }

        private static void InsertionSortCanonicalLinks(
            Span<int> sheetA,
            Span<int> sheetB,
            Span<int> spanA,
            Span<int> spanB,
            Span<LayeredSpanNeighborDirection> dirs,
            Span<int> portalMin,
            Span<int> portalMax,
            int count)
        {
            HeapSortCanonicalLinks(sheetA, sheetB, spanA, spanB, dirs, portalMin, portalMax, count);
        }

        private static bool CanonicalLinkGreater(
            int aSheetA, int aSheetB, int aSpanA, int aSpanB, LayeredSpanNeighborDirection aDir, int aMin, int aMax,
            int bSheetA, int bSheetB, int bSpanA, int bSpanB, LayeredSpanNeighborDirection bDir, int bMin, int bMax)
        {
            if (aSheetA != bSheetA) return aSheetA > bSheetA;
            if (aSheetB != bSheetB) return aSheetB > bSheetB;
            if (aSpanA != bSpanA) return aSpanA > bSpanA;
            if (aSpanB != bSpanB) return aSpanB > bSpanB;
            if (aDir != bDir) return (byte)aDir > (byte)bDir;
            if (aMin != bMin) return aMin > bMin;
            return aMax > bMax;
        }

        private static void InsertionSortSeams(
            Span<int> chartA,
            Span<int> chartB,
            Span<LayeredSpanNeighborDirection> dirs,
            Span<int> portalMin,
            Span<int> portalMax,
            Span<int> spanA,
            Span<int> spanB,
            int count)
        {
            for (int i = 1; i < count; i++)
            {
                int ca = chartA[i];
                int cb = chartB[i];
                LayeredSpanNeighborDirection d = dirs[i];
                int pmin = portalMin[i];
                int pmax = portalMax[i];
                int sa = spanA[i];
                int sb = spanB[i];
                int j = i - 1;
                while (j >= 0 && SeamGreater(chartA[j], chartB[j], dirs[j], portalMin[j], portalMax[j], spanA[j], spanB[j], ca, cb, d, pmin, pmax, sa, sb))
                {
                    chartA[j + 1] = chartA[j];
                    chartB[j + 1] = chartB[j];
                    dirs[j + 1] = dirs[j];
                    portalMin[j + 1] = portalMin[j];
                    portalMax[j + 1] = portalMax[j];
                    spanA[j + 1] = spanA[j];
                    spanB[j + 1] = spanB[j];
                    j--;
                }

                chartA[j + 1] = ca;
                chartB[j + 1] = cb;
                dirs[j + 1] = d;
                portalMin[j + 1] = pmin;
                portalMax[j + 1] = pmax;
                spanA[j + 1] = sa;
                spanB[j + 1] = sb;
            }
        }

        private static bool SeamGreater(
            int aCA, int aCB, LayeredSpanNeighborDirection aDir, int aMin, int aMax, int aSA, int aSB,
            int bCA, int bCB, LayeredSpanNeighborDirection bDir, int bMin, int bMax, int bSA, int bSB)
        {
            if (aCA != bCA) return aCA > bCA;
            if (aCB != bCB) return aCB > bCB;
            if (aDir != bDir) return (byte)aDir > (byte)bDir;
            if (aMin != bMin) return aMin > bMin;
            if (aMax != bMax) return aMax > bMax;
            if (aSA != bSA) return aSA > bSA;
            return aSB > bSB;
        }
    }
}
