using System;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Stateless conservative horizontal edge-clearance builder over walkable layered spans.
    /// Clearance is an agent-radius-independent integer-cm lower bound on the sheet/link graph.
    /// Per adjacent-column hop the lower bound is floor(min(cellSizeXcm, cellSizeZcm) / sqrt(2)),
    /// computed with Q1M constant 707106 and Int128-safe arithmetic (not exact Euclidean clearance).
    /// A same-column surface sheet is one graph node. Boundary seeds require incomplete full-side
    /// portal coverage assembled from deterministically ordered/merged walk-link portals.
    /// Success-path Build after scratch warmup allocates 0 managed bytes.
    /// </summary>
    public static class LayeredSpanRadiusFieldBuilder
    {
        /// <summary>
        /// Q1M encoding of 1/sqrt(2) ≈ 0.707106 used for hop lower-bound arithmetic.
        /// </summary>
        public const int InverseSqrtTwoQ1M = 707_106;

        public const int Q1M = 1_000_000;

        public static void Build(
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walkability,
            LayeredSpanSurfaceSheetScratch sheets,
            LayeredSpanWalkLinkScratch links,
            in LayeredSpanRasterGridSpec grid,
            LayeredSpanRadiusFieldScratch output)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (walkability == null) throw new ArgumentNullException(nameof(walkability));
            if (sheets == null) throw new ArgumentNullException(nameof(sheets));
            if (links == null) throw new ArgumentNullException(nameof(links));
            if (output == null) throw new ArgumentNullException(nameof(output));

            output.Reset();

            if (!raw.HasPublishedContent)
            {
                throw new InvalidOperationException(
                    "LayeredSpanRadiusFieldBuilder requires published raw scratch content.");
            }

            if (!walkability.WasBuiltFrom(raw))
            {
                throw new InvalidOperationException(
                    "LayeredSpanRadiusFieldBuilder requires walkability output that matches the raw scratch identity and content generation.");
            }

            if (!sheets.WasBuiltFrom(raw))
            {
                throw new InvalidOperationException(
                    "LayeredSpanRadiusFieldBuilder requires surface-sheet output that matches the raw scratch identity and content generation.");
            }

            if (!links.WasBuiltFrom(raw, walkability))
            {
                throw new InvalidOperationException(
                    "LayeredSpanRadiusFieldBuilder requires walk-link output that matches the raw/walkability scratch identity and content generation.");
            }

            int columnCount = raw.ColumnCount;
            int spanCount = raw.SpanCount;
            int walkableCount = walkability.WalkableSpanCount;
            int sheetCount = sheets.SheetCount;

            if (columnCount != walkability.ColumnCount ||
                spanCount != walkability.ClassifiedSpanCount)
            {
                throw new InvalidOperationException(
                    "LayeredSpanRadiusFieldBuilder requires walkability output that matches the raw scratch column/span counts.");
            }

            if (columnCount != sheets.ColumnCount || spanCount != sheets.SpanCount)
            {
                throw new InvalidOperationException(
                    "LayeredSpanRadiusFieldBuilder requires surface-sheet output that matches the raw scratch column/span counts.");
            }

            if (walkableCount != links.WalkableSpanCount)
            {
                throw new InvalidOperationException(
                    "LayeredSpanRadiusFieldBuilder requires walk-link output that matches the walkability walkable-span count.");
            }

            if (columnCount != grid.ColumnCount)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanRadiusFieldBuilder grid.ColumnCount ({grid.ColumnCount}) must equal raw.ColumnCount ({columnCount}).");
            }

            int cellSizeXcm = grid.CellSizeCm;
            int cellSizeZcm = grid.CellSizeCm;
            if (cellSizeXcm <= 0 || cellSizeZcm <= 0)
            {
                throw new InvalidOperationException(
                    "LayeredSpanRadiusFieldBuilder requires positive cellSizeXcm and cellSizeZcm from LayeredSpanRasterGridSpec.");
            }

            if (spanCount > output.SpanCapacity)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanRadiusFieldScratch.spanCapacity ({output.SpanCapacity}); required {spanCount}.");
            }

            if (sheetCount > output.SheetCapacity)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanRadiusFieldScratch.sheetCapacity ({output.SheetCapacity}); required {sheetCount}.");
            }

            output.Prepare(spanCount, sheetCount);

            ReadOnlySpan<LayeredSpanWalkabilityStatus> status = walkability.SpanStatus;
            ReadOnlySpan<int> walkableIndices = walkability.WalkableSpanIndices;
            ReadOnlySpan<int> sheetIds = sheets.SpanSheetIds;
            ReadOnlySpan<int> linkOffsets = links.LinkOffsets;
            ReadOnlySpan<int> neighborSpans = links.LinkNeighborSpanIndices;
            ReadOnlySpan<LayeredSpanNeighborDirection> neighborDirs = links.LinkNeighborDirections;
            ReadOnlySpan<int> portalMinAlong = links.LinkPortalMinAlongCm;
            ReadOnlySpan<int> portalMaxAlong = links.LinkPortalMaxAlongCm;
            ReadOnlySpan<int> columnSpanOffsets = raw.ColumnSpanOffsets;

            Span<int> spanClearance = output.MutableSpanClearanceCm;
            Span<int> sheetClearance = output.MutableSheetClearanceCm;
            Span<int> firstBySheet = output.MutableFirstWalkableSpanBySheet;
            Span<int> nextBySheet = output.MutableNextWalkableSpanBySheet;
            Span<int> sheetColumn = output.MutableSheetColumn;
            Span<byte> sheetHasWalkable = output.MutableSheetHasWalkable;
            Span<byte> sheetIsSeed = output.MutableSheetIsBoundarySeed;
            Span<int> bfsQueue = output.MutableBfsQueue;
            Span<int> spanToWalkable = output.MutableSpanToWalkableIndex;
            Span<int> portalMins = output.MutablePortalMinAlongCm;
            Span<int> portalMaxs = output.MutablePortalMaxAlongCm;

            int colCountX = grid.ColumnCountX;
            int hopCm = ComputeAdjacentColumnHopLowerBoundCm(cellSizeXcm, cellSizeZcm);

            int columnCursor = 0;
            for (int w = 0; w < walkableCount; w++)
            {
                int span = walkableIndices[w];
                if ((uint)span >= (uint)spanCount ||
                    status[span] != LayeredSpanWalkabilityStatus.Walkable)
                {
                    output.Reset();
                    throw new InvalidOperationException(
                        "LayeredSpanRadiusFieldBuilder walkable-span index is not marked Walkable.");
                }

                spanToWalkable[span] = w;

                int sheetId = sheetIds[span];
                if (sheetId < 0 || sheetId >= sheetCount)
                {
                    output.Reset();
                    throw new InvalidOperationException(
                        "LayeredSpanRadiusFieldBuilder requires every walkable span to carry a valid surface-sheet id.");
                }

                int col = LayeredSpanColumnIndex.AdvanceToColumnOfSpan(
                    span,
                    columnSpanOffsets,
                    columnCount,
                    ref columnCursor);
                if (col < 0)
                {
                    output.Reset();
                    throw new InvalidOperationException(
                        $"LayeredSpanRadiusFieldBuilder span {span} not found in column offsets.");
                }

                if (sheetHasWalkable[sheetId] == 0)
                {
                    sheetHasWalkable[sheetId] = 1;
                    sheetColumn[sheetId] = col;
                }
                else if (sheetColumn[sheetId] != col)
                {
                    output.Reset();
                    throw new InvalidOperationException(
                        "LayeredSpanRadiusFieldBuilder requires same-column surface sheets.");
                }

                nextBySheet[span] = firstBySheet[sheetId];
                firstBySheet[sheetId] = span;
            }

            // Mark boundary seeds: any of four column sides lacking full portal coverage.
            for (int sheetId = 0; sheetId < sheetCount; sheetId++)
            {
                if (sheetHasWalkable[sheetId] == 0)
                {
                    continue;
                }

                int col = sheetColumn[sheetId];
                int cx = col % colCountX;
                int cz = col / colCountX;
                int sideMinX = grid.ColumnMinXcm(cx);
                int sideMaxX = grid.ColumnMaxXcm(cx);
                int sideMinZ = grid.ColumnMinZcm(cz);
                int sideMaxZ = grid.ColumnMaxZcm(cz);

                if (!SideFullyCoveredByPortals(
                        sheetId,
                        LayeredSpanNeighborDirection.West,
                        sideMinZ,
                        sideMaxZ,
                        firstBySheet,
                        nextBySheet,
                        spanToWalkable,
                        linkOffsets,
                        neighborDirs,
                        portalMinAlong,
                        portalMaxAlong,
                        portalMins,
                        portalMaxs,
                        output) ||
                    !SideFullyCoveredByPortals(
                        sheetId,
                        LayeredSpanNeighborDirection.East,
                        sideMinZ,
                        sideMaxZ,
                        firstBySheet,
                        nextBySheet,
                        spanToWalkable,
                        linkOffsets,
                        neighborDirs,
                        portalMinAlong,
                        portalMaxAlong,
                        portalMins,
                        portalMaxs,
                        output) ||
                    !SideFullyCoveredByPortals(
                        sheetId,
                        LayeredSpanNeighborDirection.North,
                        sideMinX,
                        sideMaxX,
                        firstBySheet,
                        nextBySheet,
                        spanToWalkable,
                        linkOffsets,
                        neighborDirs,
                        portalMinAlong,
                        portalMaxAlong,
                        portalMins,
                        portalMaxs,
                        output) ||
                    !SideFullyCoveredByPortals(
                        sheetId,
                        LayeredSpanNeighborDirection.South,
                        sideMinX,
                        sideMaxX,
                        firstBySheet,
                        nextBySheet,
                        spanToWalkable,
                        linkOffsets,
                        neighborDirs,
                        portalMinAlong,
                        portalMaxAlong,
                        portalMins,
                        portalMaxs,
                        output))
                {
                    sheetIsSeed[sheetId] = 1;
                }
            }

            // Multi-source BFS from boundary seeds through sheet/link adjacency.
            // Unreachable / uninitialized sheets keep MaxValue until written.
            for (int sheetId = 0; sheetId < sheetCount; sheetId++)
            {
                sheetClearance[sheetId] = sheetHasWalkable[sheetId] == 0
                    ? 0
                    : int.MaxValue;
            }

            int queueHead = 0;
            int queueTail = 0;
            for (int sheetId = 0; sheetId < sheetCount; sheetId++)
            {
                if (sheetIsSeed[sheetId] == 0)
                {
                    continue;
                }

                sheetClearance[sheetId] = 0;
                bfsQueue[queueTail++] = sheetId;
            }

            while (queueHead < queueTail)
            {
                int sheetId = bfsQueue[queueHead++];
                int clearance = sheetClearance[sheetId];
                long nextClearanceLong = (long)clearance + hopCm;
                int nextClearance = nextClearanceLong >= int.MaxValue
                    ? int.MaxValue
                    : (int)nextClearanceLong;

                for (int span = firstBySheet[sheetId]; span >= 0; span = nextBySheet[span])
                {
                    int w = spanToWalkable[span];
                    int start = linkOffsets[w];
                    int end = linkOffsets[w + 1];
                    for (int i = start; i < end; i++)
                    {
                        int neighborSpan = neighborSpans[i];
                        if ((uint)neighborSpan >= (uint)spanCount ||
                            status[neighborSpan] != LayeredSpanWalkabilityStatus.Walkable)
                        {
                            output.Reset();
                            throw new InvalidOperationException(
                                "LayeredSpanRadiusFieldBuilder walk-link neighbor is not a walkable raw span.");
                        }

                        int neighborSheet = sheetIds[neighborSpan];
                        if (neighborSheet < 0 || neighborSheet >= sheetCount ||
                            sheetHasWalkable[neighborSheet] == 0)
                        {
                            output.Reset();
                            throw new InvalidOperationException(
                                "LayeredSpanRadiusFieldBuilder walk-link neighbor lacks a walkable surface-sheet id.");
                        }

                        if (neighborSheet == sheetId)
                        {
                            continue;
                        }

                        // Uniform hop cost: first improvement from MaxValue is optimal; queue stays <= sheetCount.
                        if (nextClearance < sheetClearance[neighborSheet])
                        {
                            sheetClearance[neighborSheet] = nextClearance;
                            if (queueTail >= bfsQueue.Length)
                            {
                                output.Reset();
                                throw new InvalidOperationException(
                                    $"LayeredSpanRadiusFieldScratch.sheetCapacity ({output.SheetCapacity}); required {queueTail + 1} (bfsQueue).");
                            }

                            bfsQueue[queueTail++] = neighborSheet;
                        }
                    }
                }
            }

            for (int span = 0; span < spanCount; span++)
            {
                if (status[span] != LayeredSpanWalkabilityStatus.Walkable)
                {
                    spanClearance[span] = 0;
                    continue;
                }

                int sheetId = sheetIds[span];
                int value = sheetClearance[sheetId];
                spanClearance[span] = value == int.MaxValue ? 0 : value;
            }

            output.Commit(raw, walkability, sheets, links);
        }

        /// <summary>
        /// Conservative per-hop lower bound:
        /// floor(min(cellSizeXcm, cellSizeZcm) / sqrt(2))
        /// = floor(min * InverseSqrtTwoQ1M / Q1M) with Int128-safe arithmetic.
        /// </summary>
        public static int ComputeAdjacentColumnHopLowerBoundCm(int cellSizeXcm, int cellSizeZcm)
        {
            if (cellSizeXcm <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellSizeXcm),
                    cellSizeXcm,
                    "cellSizeXcm must be positive.");
            }

            if (cellSizeZcm <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellSizeZcm),
                    cellSizeZcm,
                    "cellSizeZcm must be positive.");
            }

            int minCell = cellSizeXcm < cellSizeZcm ? cellSizeXcm : cellSizeZcm;
            Int128 numer = (Int128)minCell * InverseSqrtTwoQ1M;
            Int128 denom = Q1M;
            return (int)(numer / denom);
        }

        private static bool SideFullyCoveredByPortals(
            int sheetId,
            LayeredSpanNeighborDirection direction,
            int sideMinCm,
            int sideMaxCm,
            Span<int> firstBySheet,
            Span<int> nextBySheet,
            Span<int> spanToWalkable,
            ReadOnlySpan<int> linkOffsets,
            ReadOnlySpan<LayeredSpanNeighborDirection> neighborDirs,
            ReadOnlySpan<int> portalMinAlong,
            ReadOnlySpan<int> portalMaxAlong,
            Span<int> portalMins,
            Span<int> portalMaxs,
            LayeredSpanRadiusFieldScratch output)
        {
            int portalCount = 0;
            for (int span = firstBySheet[sheetId]; span >= 0; span = nextBySheet[span])
            {
                int w = spanToWalkable[span];
                int start = linkOffsets[w];
                int end = linkOffsets[w + 1];
                for (int i = start; i < end; i++)
                {
                    if (neighborDirs[i] != direction)
                    {
                        continue;
                    }

                    long lo = portalMinAlong[i] > sideMinCm ? portalMinAlong[i] : sideMinCm;
                    long hi = portalMaxAlong[i] < sideMaxCm ? portalMaxAlong[i] : sideMaxCm;
                    if (hi <= lo)
                    {
                        continue;
                    }

                    if (portalCount >= output.PortalIntervalCapacity)
                    {
                        output.Reset();
                        throw new InvalidOperationException(
                            $"LayeredSpanRadiusFieldScratch.portalIntervalCapacity ({output.PortalIntervalCapacity}); required {portalCount + 1}.");
                    }

                    portalMins[portalCount] = (int)lo;
                    portalMaxs[portalCount] = (int)hi;
                    portalCount++;
                }
            }

            if (portalCount == 0)
            {
                return false;
            }

            // Deterministic order: ascending min, then ascending max.
            InsertionSortPortals(portalMins, portalMaxs, portalCount);
            MergeTouchingPortals(portalMins, portalMaxs, ref portalCount);

            if (portalMins[0] > sideMinCm)
            {
                return false;
            }

            long coveredTo = portalMaxs[0];
            for (int i = 1; i < portalCount; i++)
            {
                if (portalMins[i] > coveredTo)
                {
                    return false;
                }

                if (portalMaxs[i] > coveredTo)
                {
                    coveredTo = portalMaxs[i];
                }
            }

            return coveredTo >= sideMaxCm;
        }

        private static void InsertionSortPortals(Span<int> mins, Span<int> maxs, int count)
        {
            for (int i = 1; i < count; i++)
            {
                int min = mins[i];
                int max = maxs[i];
                int j = i - 1;
                while (j >= 0 &&
                       (mins[j] > min || (mins[j] == min && maxs[j] > max)))
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

    }
}
