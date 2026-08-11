using System;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Stateless four-neighbor walk-link CSR builder over classified layered spans.
    /// Links require positive-length shared along-boundary coverage plus height overlap/delta within maxClimbCm.
    /// Every accepted directed link stores the portal interval [minAlongCm, maxAlongCm] used to accept it.
    /// Success-path Build after scratch warmup allocates 0 managed bytes.
    /// </summary>
    public static class LayeredSpanWalkLinkBuilder
    {
        public static void Build(
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walkability,
            in LayeredSpanRasterGridSpec grid,
            in LayeredSpanWalkLinkSpec linkSpec,
            LayeredSpanWalkLinkScratch output)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (walkability == null) throw new ArgumentNullException(nameof(walkability));
            if (output == null) throw new ArgumentNullException(nameof(output));

            output.Reset();

            if (!raw.HasPublishedContent)
            {
                throw new InvalidOperationException(
                    "LayeredSpanWalkLinkBuilder requires published raw scratch content.");
            }

            if (!walkability.WasBuiltFrom(raw))
            {
                throw new InvalidOperationException(
                    "LayeredSpanWalkLinkBuilder requires walkability output that matches the raw scratch identity and content generation.");
            }

            int columnCount = raw.ColumnCount;
            int spanCount = raw.SpanCount;
            int walkableCount = walkability.WalkableSpanCount;

            if (columnCount != walkability.ColumnCount || spanCount != walkability.ClassifiedSpanCount)
            {
                throw new InvalidOperationException(
                    "LayeredSpanWalkLinkBuilder requires walkability output that matches the raw scratch column/span counts.");
            }

            if (columnCount != grid.ColumnCount)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanWalkLinkBuilder grid.ColumnCount ({grid.ColumnCount}) must equal raw.ColumnCount ({columnCount}).");
            }

            if (walkableCount > output.WalkableSpanCapacity)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanWalkLinkScratch.walkableSpanCapacity ({output.WalkableSpanCapacity}); required {walkableCount}.");
            }

            ReadOnlySpan<int> walkableIndices = walkability.WalkableSpanIndices;
            ReadOnlySpan<LayeredSpanWalkabilityStatus> status = walkability.SpanStatus;
            ReadOnlySpan<int> columnWalkableCounts = walkability.ColumnWalkableCounts;
            ReadOnlySpan<int> columnWalkableOffsets = walkability.ColumnWalkableOffsets;
            ReadOnlySpan<LayeredSpanBoundaryMask> masks = raw.SpanBoundaryMask;
            ReadOnlySpan<int> westMin = raw.SpanWestMinYcm;
            ReadOnlySpan<int> westMax = raw.SpanWestMaxYcm;
            ReadOnlySpan<int> westAlongMin = raw.SpanWestMinZcm;
            ReadOnlySpan<int> westAlongMax = raw.SpanWestMaxZcm;
            ReadOnlySpan<int> eastMin = raw.SpanEastMinYcm;
            ReadOnlySpan<int> eastMax = raw.SpanEastMaxYcm;
            ReadOnlySpan<int> eastAlongMin = raw.SpanEastMinZcm;
            ReadOnlySpan<int> eastAlongMax = raw.SpanEastMaxZcm;
            ReadOnlySpan<int> northMin = raw.SpanNorthMinYcm;
            ReadOnlySpan<int> northMax = raw.SpanNorthMaxYcm;
            ReadOnlySpan<int> northAlongMin = raw.SpanNorthMinXcm;
            ReadOnlySpan<int> northAlongMax = raw.SpanNorthMaxXcm;
            ReadOnlySpan<int> southMin = raw.SpanSouthMinYcm;
            ReadOnlySpan<int> southMax = raw.SpanSouthMaxYcm;
            ReadOnlySpan<int> southAlongMin = raw.SpanSouthMinXcm;
            ReadOnlySpan<int> southAlongMax = raw.SpanSouthMaxXcm;

            Span<int> linkCounts = output.MutableLinkCounts;
            Span<int> linkOffsets = output.MutableLinkOffsets;
            Span<int> neighborSpans = output.MutableLinkNeighborSpanIndices;
            Span<LayeredSpanNeighborDirection> neighborDirs = output.MutableLinkNeighborDirections;
            Span<int> portalMinAlong = output.MutableLinkPortalMinAlongCm;
            Span<int> portalMaxAlong = output.MutableLinkPortalMaxAlongCm;

            int maxClimb = linkSpec.MaxClimbCm;
            int colCountX = grid.ColumnCountX;
            int colCountZ = grid.ColumnCountZ;

            // Pass 1: count links per walkable in source walkable-span order.
            // Use a wide accumulator so required capacity cannot wrap before validation.
            long totalLinksLong = 0;
            for (int col = 0; col < columnCount; col++)
            {
                int wStart = columnWalkableOffsets[col];
                int wEnd = columnWalkableOffsets[col + 1];
                for (int w = wStart; w < wEnd; w++)
                {
                    int span = walkableIndices[w];
                    int count = CountLinksForSpan(
                        span,
                        col,
                        colCountX,
                        colCountZ,
                        maxClimb,
                        masks,
                        westMin,
                        westMax,
                        westAlongMin,
                        westAlongMax,
                        eastMin,
                        eastMax,
                        eastAlongMin,
                        eastAlongMax,
                        northMin,
                        northMax,
                        northAlongMin,
                        northAlongMax,
                        southMin,
                        southMax,
                        southAlongMin,
                        southAlongMax,
                        status,
                        columnWalkableCounts,
                        columnWalkableOffsets,
                        walkableIndices);
                    linkCounts[w] = count;
                    totalLinksLong += count;
                }
            }

            if (totalLinksLong > output.LinkCapacity)
            {
                output.Reset();
                throw new InvalidOperationException(
                    $"LayeredSpanWalkLinkScratch.linkCapacity ({output.LinkCapacity}); required {totalLinksLong}.");
            }

            int totalLinks = (int)totalLinksLong;
            int prefix = 0;
            for (int w = 0; w < walkableCount; w++)
            {
                linkOffsets[w] = prefix;
                prefix += linkCounts[w];
            }

            linkOffsets[walkableCount] = prefix;

            // Pass 2: fill CSR neighbors.
            for (int col = 0; col < columnCount; col++)
            {
                int wStart = columnWalkableOffsets[col];
                int wEnd = columnWalkableOffsets[col + 1];
                for (int w = wStart; w < wEnd; w++)
                {
                    int span = walkableIndices[w];
                    int cursor = linkOffsets[w];
                    cursor = FillLinksForSpan(
                        span,
                        col,
                        colCountX,
                        colCountZ,
                        maxClimb,
                        masks,
                        westMin,
                        westMax,
                        westAlongMin,
                        westAlongMax,
                        eastMin,
                        eastMax,
                        eastAlongMin,
                        eastAlongMax,
                        northMin,
                        northMax,
                        northAlongMin,
                        northAlongMax,
                        southMin,
                        southMax,
                        southAlongMin,
                        southAlongMax,
                        status,
                        columnWalkableCounts,
                        columnWalkableOffsets,
                        walkableIndices,
                        neighborSpans,
                        neighborDirs,
                        portalMinAlong,
                        portalMaxAlong,
                        cursor);
                    if (cursor != linkOffsets[w] + linkCounts[w])
                    {
                        output.Reset();
                        throw new InvalidOperationException(
                            "LayeredSpanWalkLinkBuilder fill/count mismatch.");
                    }
                }
            }

            output.Commit(raw, walkability, walkableCount, totalLinks);
        }

        private static int CountLinksForSpan(
            int span,
            int col,
            int colCountX,
            int colCountZ,
            int maxClimb,
            ReadOnlySpan<LayeredSpanBoundaryMask> masks,
            ReadOnlySpan<int> westMin,
            ReadOnlySpan<int> westMax,
            ReadOnlySpan<int> westAlongMin,
            ReadOnlySpan<int> westAlongMax,
            ReadOnlySpan<int> eastMin,
            ReadOnlySpan<int> eastMax,
            ReadOnlySpan<int> eastAlongMin,
            ReadOnlySpan<int> eastAlongMax,
            ReadOnlySpan<int> northMin,
            ReadOnlySpan<int> northMax,
            ReadOnlySpan<int> northAlongMin,
            ReadOnlySpan<int> northAlongMax,
            ReadOnlySpan<int> southMin,
            ReadOnlySpan<int> southMax,
            ReadOnlySpan<int> southAlongMin,
            ReadOnlySpan<int> southAlongMax,
            ReadOnlySpan<LayeredSpanWalkabilityStatus> status,
            ReadOnlySpan<int> columnWalkableCounts,
            ReadOnlySpan<int> columnWalkableOffsets,
            ReadOnlySpan<int> walkableIndices)
        {
            int count = 0;
            count += CountDirection(
                span, col, LayeredSpanNeighborDirection.West, colCountX, colCountZ, maxClimb,
                masks, westMin, westMax, westAlongMin, westAlongMax,
                eastMin, eastMax, eastAlongMin, eastAlongMax,
                northMin, northMax, northAlongMin, northAlongMax,
                southMin, southMax, southAlongMin, southAlongMax,
                status, columnWalkableCounts, columnWalkableOffsets, walkableIndices);
            count += CountDirection(
                span, col, LayeredSpanNeighborDirection.East, colCountX, colCountZ, maxClimb,
                masks, westMin, westMax, westAlongMin, westAlongMax,
                eastMin, eastMax, eastAlongMin, eastAlongMax,
                northMin, northMax, northAlongMin, northAlongMax,
                southMin, southMax, southAlongMin, southAlongMax,
                status, columnWalkableCounts, columnWalkableOffsets, walkableIndices);
            count += CountDirection(
                span, col, LayeredSpanNeighborDirection.North, colCountX, colCountZ, maxClimb,
                masks, westMin, westMax, westAlongMin, westAlongMax,
                eastMin, eastMax, eastAlongMin, eastAlongMax,
                northMin, northMax, northAlongMin, northAlongMax,
                southMin, southMax, southAlongMin, southAlongMax,
                status, columnWalkableCounts, columnWalkableOffsets, walkableIndices);
            count += CountDirection(
                span, col, LayeredSpanNeighborDirection.South, colCountX, colCountZ, maxClimb,
                masks, westMin, westMax, westAlongMin, westAlongMax,
                eastMin, eastMax, eastAlongMin, eastAlongMax,
                northMin, northMax, northAlongMin, northAlongMax,
                southMin, southMax, southAlongMin, southAlongMax,
                status, columnWalkableCounts, columnWalkableOffsets, walkableIndices);
            return count;
        }

        private static int FillLinksForSpan(
            int span,
            int col,
            int colCountX,
            int colCountZ,
            int maxClimb,
            ReadOnlySpan<LayeredSpanBoundaryMask> masks,
            ReadOnlySpan<int> westMin,
            ReadOnlySpan<int> westMax,
            ReadOnlySpan<int> westAlongMin,
            ReadOnlySpan<int> westAlongMax,
            ReadOnlySpan<int> eastMin,
            ReadOnlySpan<int> eastMax,
            ReadOnlySpan<int> eastAlongMin,
            ReadOnlySpan<int> eastAlongMax,
            ReadOnlySpan<int> northMin,
            ReadOnlySpan<int> northMax,
            ReadOnlySpan<int> northAlongMin,
            ReadOnlySpan<int> northAlongMax,
            ReadOnlySpan<int> southMin,
            ReadOnlySpan<int> southMax,
            ReadOnlySpan<int> southAlongMin,
            ReadOnlySpan<int> southAlongMax,
            ReadOnlySpan<LayeredSpanWalkabilityStatus> status,
            ReadOnlySpan<int> columnWalkableCounts,
            ReadOnlySpan<int> columnWalkableOffsets,
            ReadOnlySpan<int> walkableIndices,
            Span<int> neighborSpans,
            Span<LayeredSpanNeighborDirection> neighborDirs,
            Span<int> portalMinAlong,
            Span<int> portalMaxAlong,
            int cursor)
        {
            cursor = FillDirection(
                span, col, LayeredSpanNeighborDirection.West, colCountX, colCountZ, maxClimb,
                masks, westMin, westMax, westAlongMin, westAlongMax,
                eastMin, eastMax, eastAlongMin, eastAlongMax,
                northMin, northMax, northAlongMin, northAlongMax,
                southMin, southMax, southAlongMin, southAlongMax,
                status, columnWalkableCounts, columnWalkableOffsets, walkableIndices,
                neighborSpans, neighborDirs, portalMinAlong, portalMaxAlong, cursor);
            cursor = FillDirection(
                span, col, LayeredSpanNeighborDirection.East, colCountX, colCountZ, maxClimb,
                masks, westMin, westMax, westAlongMin, westAlongMax,
                eastMin, eastMax, eastAlongMin, eastAlongMax,
                northMin, northMax, northAlongMin, northAlongMax,
                southMin, southMax, southAlongMin, southAlongMax,
                status, columnWalkableCounts, columnWalkableOffsets, walkableIndices,
                neighborSpans, neighborDirs, portalMinAlong, portalMaxAlong, cursor);
            cursor = FillDirection(
                span, col, LayeredSpanNeighborDirection.North, colCountX, colCountZ, maxClimb,
                masks, westMin, westMax, westAlongMin, westAlongMax,
                eastMin, eastMax, eastAlongMin, eastAlongMax,
                northMin, northMax, northAlongMin, northAlongMax,
                southMin, southMax, southAlongMin, southAlongMax,
                status, columnWalkableCounts, columnWalkableOffsets, walkableIndices,
                neighborSpans, neighborDirs, portalMinAlong, portalMaxAlong, cursor);
            cursor = FillDirection(
                span, col, LayeredSpanNeighborDirection.South, colCountX, colCountZ, maxClimb,
                masks, westMin, westMax, westAlongMin, westAlongMax,
                eastMin, eastMax, eastAlongMin, eastAlongMax,
                northMin, northMax, northAlongMin, northAlongMax,
                southMin, southMax, southAlongMin, southAlongMax,
                status, columnWalkableCounts, columnWalkableOffsets, walkableIndices,
                neighborSpans, neighborDirs, portalMinAlong, portalMaxAlong, cursor);
            return cursor;
        }

        private static int CountDirection(
            int span,
            int col,
            LayeredSpanNeighborDirection direction,
            int colCountX,
            int colCountZ,
            int maxClimb,
            ReadOnlySpan<LayeredSpanBoundaryMask> masks,
            ReadOnlySpan<int> westMin,
            ReadOnlySpan<int> westMax,
            ReadOnlySpan<int> westAlongMin,
            ReadOnlySpan<int> westAlongMax,
            ReadOnlySpan<int> eastMin,
            ReadOnlySpan<int> eastMax,
            ReadOnlySpan<int> eastAlongMin,
            ReadOnlySpan<int> eastAlongMax,
            ReadOnlySpan<int> northMin,
            ReadOnlySpan<int> northMax,
            ReadOnlySpan<int> northAlongMin,
            ReadOnlySpan<int> northAlongMax,
            ReadOnlySpan<int> southMin,
            ReadOnlySpan<int> southMax,
            ReadOnlySpan<int> southAlongMin,
            ReadOnlySpan<int> southAlongMax,
            ReadOnlySpan<LayeredSpanWalkabilityStatus> status,
            ReadOnlySpan<int> columnWalkableCounts,
            ReadOnlySpan<int> columnWalkableOffsets,
            ReadOnlySpan<int> walkableIndices)
        {
            if (!TryGetNeighborColumn(col, direction, colCountX, colCountZ, out int neighborCol))
            {
                return 0;
            }

            if (!TryGetBoundaryCoverage(
                    span,
                    direction,
                    masks,
                    westMin, westMax, westAlongMin, westAlongMax,
                    eastMin, eastMax, eastAlongMin, eastAlongMax,
                    northMin, northMax, northAlongMin, northAlongMax,
                    southMin, southMax, southAlongMin, southAlongMax,
                    out int srcMin, out int srcMax, out int srcAlongMin, out int srcAlongMax))
            {
                return 0;
            }

            LayeredSpanNeighborDirection opposite = Opposite(direction);
            int start = columnWalkableOffsets[neighborCol];
            int end = start + columnWalkableCounts[neighborCol];
            int count = 0;
            for (int i = start; i < end; i++)
            {
                int neighborSpan = walkableIndices[i];
                if (status[neighborSpan] != LayeredSpanWalkabilityStatus.Walkable)
                {
                    continue;
                }

                if (!TryGetBoundaryCoverage(
                        neighborSpan,
                        opposite,
                        masks,
                        westMin, westMax, westAlongMin, westAlongMax,
                        eastMin, eastMax, eastAlongMin, eastAlongMax,
                        northMin, northMax, northAlongMin, northAlongMax,
                        southMin, southMax, southAlongMin, southAlongMax,
                        out int dstMin, out int dstMax, out int dstAlongMin, out int dstAlongMax))
                {
                    continue;
                }

                if (TryAcceptWalkLink(
                        srcMin, srcMax, srcAlongMin, srcAlongMax,
                        dstMin, dstMax, dstAlongMin, dstAlongMax,
                        maxClimb,
                        out _,
                        out _))
                {
                    count++;
                }
            }

            return count;
        }

        private static int FillDirection(
            int span,
            int col,
            LayeredSpanNeighborDirection direction,
            int colCountX,
            int colCountZ,
            int maxClimb,
            ReadOnlySpan<LayeredSpanBoundaryMask> masks,
            ReadOnlySpan<int> westMin,
            ReadOnlySpan<int> westMax,
            ReadOnlySpan<int> westAlongMin,
            ReadOnlySpan<int> westAlongMax,
            ReadOnlySpan<int> eastMin,
            ReadOnlySpan<int> eastMax,
            ReadOnlySpan<int> eastAlongMin,
            ReadOnlySpan<int> eastAlongMax,
            ReadOnlySpan<int> northMin,
            ReadOnlySpan<int> northMax,
            ReadOnlySpan<int> northAlongMin,
            ReadOnlySpan<int> northAlongMax,
            ReadOnlySpan<int> southMin,
            ReadOnlySpan<int> southMax,
            ReadOnlySpan<int> southAlongMin,
            ReadOnlySpan<int> southAlongMax,
            ReadOnlySpan<LayeredSpanWalkabilityStatus> status,
            ReadOnlySpan<int> columnWalkableCounts,
            ReadOnlySpan<int> columnWalkableOffsets,
            ReadOnlySpan<int> walkableIndices,
            Span<int> neighborSpans,
            Span<LayeredSpanNeighborDirection> neighborDirs,
            Span<int> portalMinAlong,
            Span<int> portalMaxAlong,
            int cursor)
        {
            if (!TryGetNeighborColumn(col, direction, colCountX, colCountZ, out int neighborCol))
            {
                return cursor;
            }

            if (!TryGetBoundaryCoverage(
                    span,
                    direction,
                    masks,
                    westMin, westMax, westAlongMin, westAlongMax,
                    eastMin, eastMax, eastAlongMin, eastAlongMax,
                    northMin, northMax, northAlongMin, northAlongMax,
                    southMin, southMax, southAlongMin, southAlongMax,
                    out int srcMin, out int srcMax, out int srcAlongMin, out int srcAlongMax))
            {
                return cursor;
            }

            LayeredSpanNeighborDirection opposite = Opposite(direction);
            int start = columnWalkableOffsets[neighborCol];
            int end = start + columnWalkableCounts[neighborCol];
            for (int i = start; i < end; i++)
            {
                int neighborSpan = walkableIndices[i];
                if (status[neighborSpan] != LayeredSpanWalkabilityStatus.Walkable)
                {
                    continue;
                }

                if (!TryGetBoundaryCoverage(
                        neighborSpan,
                        opposite,
                        masks,
                        westMin, westMax, westAlongMin, westAlongMax,
                        eastMin, eastMax, eastAlongMin, eastAlongMax,
                        northMin, northMax, northAlongMin, northAlongMax,
                        southMin, southMax, southAlongMin, southAlongMax,
                        out int dstMin, out int dstMax, out int dstAlongMin, out int dstAlongMax))
                {
                    continue;
                }

                if (!TryAcceptWalkLink(
                        srcMin, srcMax, srcAlongMin, srcAlongMax,
                        dstMin, dstMax, dstAlongMin, dstAlongMax,
                        maxClimb,
                        out int portalMin,
                        out int portalMax))
                {
                    continue;
                }

                neighborSpans[cursor] = neighborSpan;
                neighborDirs[cursor] = direction;
                portalMinAlong[cursor] = portalMin;
                portalMaxAlong[cursor] = portalMax;
                cursor++;
            }

            return cursor;
        }

        private static bool TryGetNeighborColumn(
            int col,
            LayeredSpanNeighborDirection direction,
            int colCountX,
            int colCountZ,
            out int neighborCol)
        {
            int cx = col % colCountX;
            int cz = col / colCountX;
            switch (direction)
            {
                case LayeredSpanNeighborDirection.West:
                    if (cx <= 0)
                    {
                        neighborCol = 0;
                        return false;
                    }

                    neighborCol = col - 1;
                    return true;
                case LayeredSpanNeighborDirection.East:
                    if (cx + 1 >= colCountX)
                    {
                        neighborCol = 0;
                        return false;
                    }

                    neighborCol = col + 1;
                    return true;
                case LayeredSpanNeighborDirection.North:
                    if (cz <= 0)
                    {
                        neighborCol = 0;
                        return false;
                    }

                    neighborCol = col - colCountX;
                    return true;
                case LayeredSpanNeighborDirection.South:
                    if (cz + 1 >= colCountZ)
                    {
                        neighborCol = 0;
                        return false;
                    }

                    neighborCol = col + colCountX;
                    return true;
                default:
                    neighborCol = 0;
                    return false;
            }
        }

        private static LayeredSpanNeighborDirection Opposite(LayeredSpanNeighborDirection direction)
            => direction switch
            {
                LayeredSpanNeighborDirection.West => LayeredSpanNeighborDirection.East,
                LayeredSpanNeighborDirection.East => LayeredSpanNeighborDirection.West,
                LayeredSpanNeighborDirection.North => LayeredSpanNeighborDirection.South,
                LayeredSpanNeighborDirection.South => LayeredSpanNeighborDirection.North,
                _ => direction
            };

        private static bool TryGetBoundaryCoverage(
            int span,
            LayeredSpanNeighborDirection direction,
            ReadOnlySpan<LayeredSpanBoundaryMask> masks,
            ReadOnlySpan<int> westMin,
            ReadOnlySpan<int> westMax,
            ReadOnlySpan<int> westAlongMin,
            ReadOnlySpan<int> westAlongMax,
            ReadOnlySpan<int> eastMin,
            ReadOnlySpan<int> eastMax,
            ReadOnlySpan<int> eastAlongMin,
            ReadOnlySpan<int> eastAlongMax,
            ReadOnlySpan<int> northMin,
            ReadOnlySpan<int> northMax,
            ReadOnlySpan<int> northAlongMin,
            ReadOnlySpan<int> northAlongMax,
            ReadOnlySpan<int> southMin,
            ReadOnlySpan<int> southMax,
            ReadOnlySpan<int> southAlongMin,
            ReadOnlySpan<int> southAlongMax,
            out int minY,
            out int maxY,
            out int alongMin,
            out int alongMax)
        {
            LayeredSpanBoundaryMask bit;
            switch (direction)
            {
                case LayeredSpanNeighborDirection.West:
                    bit = LayeredSpanBoundaryMask.West;
                    minY = westMin[span];
                    maxY = westMax[span];
                    alongMin = westAlongMin[span];
                    alongMax = westAlongMax[span];
                    break;
                case LayeredSpanNeighborDirection.East:
                    bit = LayeredSpanBoundaryMask.East;
                    minY = eastMin[span];
                    maxY = eastMax[span];
                    alongMin = eastAlongMin[span];
                    alongMax = eastAlongMax[span];
                    break;
                case LayeredSpanNeighborDirection.North:
                    bit = LayeredSpanBoundaryMask.North;
                    minY = northMin[span];
                    maxY = northMax[span];
                    alongMin = northAlongMin[span];
                    alongMax = northAlongMax[span];
                    break;
                case LayeredSpanNeighborDirection.South:
                    bit = LayeredSpanBoundaryMask.South;
                    minY = southMin[span];
                    maxY = southMax[span];
                    alongMin = southAlongMin[span];
                    alongMax = southAlongMax[span];
                    break;
                default:
                    minY = 0;
                    maxY = 0;
                    alongMin = 0;
                    alongMax = 0;
                    return false;
            }

            return (masks[span] & bit) != 0;
        }

        private static bool TryAcceptWalkLink(
            int aMinY,
            int aMaxY,
            int aAlongMin,
            int aAlongMax,
            int bMinY,
            int bMaxY,
            int bAlongMin,
            int bAlongMax,
            int maxClimb,
            out int portalMinAlongCm,
            out int portalMaxAlongCm)
        {
            // Point/corner-only along contact is not a traversable portal.
            if (!TryPositiveLengthAlongOverlap(
                    aAlongMin,
                    aAlongMax,
                    bAlongMin,
                    bAlongMax,
                    out portalMinAlongCm,
                    out portalMaxAlongCm))
            {
                return false;
            }

            if (aMinY <= bMaxY && bMinY <= aMaxY)
            {
                return true;
            }

            if (aMaxY < bMinY)
            {
                return ((long)bMinY - aMaxY) <= maxClimb;
            }

            return ((long)aMinY - bMaxY) <= maxClimb;
        }

        private static bool TryPositiveLengthAlongOverlap(
            int aMin,
            int aMax,
            int bMin,
            int bMax,
            out int portalMinAlongCm,
            out int portalMaxAlongCm)
        {
            long lo = aMin > bMin ? aMin : bMin;
            long hi = aMax < bMax ? aMax : bMax;
            if (hi > lo)
            {
                portalMinAlongCm = (int)lo;
                portalMaxAlongCm = (int)hi;
                return true;
            }

            portalMinAlongCm = 0;
            portalMaxAlongCm = 0;
            return false;
        }
    }
}
