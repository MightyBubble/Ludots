using System;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Stateless deterministic connected-region builder over radius-eligible layered spans.
    /// Connectivity unions same-column surface-sheet ids and undirected walk-link adjacency,
    /// but only among vertically-walkable spans whose horizontal clearance &gt;= agentRadiusCm.
    /// Links only connect two radius-eligible spans. Compact region ids ascend by minimum
    /// source raw-span index. Success-path Build after scratch warmup allocates 0 managed bytes.
    /// </summary>
    public static class LayeredSpanRegionBuilder
    {
        public static void Build(
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walkability,
            LayeredSpanSurfaceSheetScratch sheets,
            LayeredSpanWalkLinkScratch links,
            LayeredSpanRadiusFieldScratch radius,
            int agentRadiusCm,
            LayeredSpanRegionScratch output)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (walkability == null) throw new ArgumentNullException(nameof(walkability));
            if (sheets == null) throw new ArgumentNullException(nameof(sheets));
            if (links == null) throw new ArgumentNullException(nameof(links));
            if (radius == null) throw new ArgumentNullException(nameof(radius));
            if (output == null) throw new ArgumentNullException(nameof(output));

            output.Reset();

            if (agentRadiusCm < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(agentRadiusCm),
                    agentRadiusCm,
                    "agentRadiusCm must be nonnegative.");
            }

            if (!raw.HasPublishedContent)
            {
                throw new InvalidOperationException(
                    "LayeredSpanRegionBuilder requires published raw scratch content.");
            }

            if (!walkability.WasBuiltFrom(raw))
            {
                throw new InvalidOperationException(
                    "LayeredSpanRegionBuilder requires walkability output that matches the raw scratch identity and content generation.");
            }

            if (!sheets.WasBuiltFrom(raw))
            {
                throw new InvalidOperationException(
                    "LayeredSpanRegionBuilder requires surface-sheet output that matches the raw scratch identity and content generation.");
            }

            if (!links.WasBuiltFrom(raw, walkability))
            {
                throw new InvalidOperationException(
                    "LayeredSpanRegionBuilder requires walk-link output that matches the raw/walkability scratch identity and content generation.");
            }

            if (!radius.WasBuiltFrom(raw, walkability, sheets, links))
            {
                throw new InvalidOperationException(
                    "LayeredSpanRegionBuilder requires radius-field output that matches the raw/walkability/sheets/links scratch identity and content generation.");
            }

            int columnCount = raw.ColumnCount;
            int spanCount = raw.SpanCount;
            int walkableCount = walkability.WalkableSpanCount;
            int sheetCount = sheets.SheetCount;

            if (columnCount != walkability.ColumnCount ||
                spanCount != walkability.ClassifiedSpanCount)
            {
                throw new InvalidOperationException(
                    "LayeredSpanRegionBuilder requires walkability output that matches the raw scratch column/span counts.");
            }

            if (columnCount != sheets.ColumnCount || spanCount != sheets.SpanCount)
            {
                throw new InvalidOperationException(
                    "LayeredSpanRegionBuilder requires surface-sheet output that matches the raw scratch column/span counts.");
            }

            if (walkableCount != links.WalkableSpanCount)
            {
                throw new InvalidOperationException(
                    "LayeredSpanRegionBuilder requires walk-link output that matches the walkability walkable-span count.");
            }

            if (spanCount != radius.SpanCount || sheetCount != radius.SheetCount)
            {
                throw new InvalidOperationException(
                    "LayeredSpanRegionBuilder requires radius-field output that matches the raw/sheet span counts.");
            }

            if (spanCount > output.SpanCapacity)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanRegionScratch.spanCapacity ({output.SpanCapacity}); required {spanCount}.");
            }

            // Sheet ids index first-eligible scratch; sheetCount cannot exceed spanCapacity.
            if (sheetCount > output.SpanCapacity)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanRegionScratch.spanCapacity ({output.SpanCapacity}); required {sheetCount} (sheetCount).");
            }

            output.Prepare(spanCount);

            ReadOnlySpan<LayeredSpanWalkabilityStatus> status = walkability.SpanStatus;
            ReadOnlySpan<int> walkableIndices = walkability.WalkableSpanIndices;
            ReadOnlySpan<int> sheetIds = sheets.SpanSheetIds;
            ReadOnlySpan<int> linkOffsets = links.LinkOffsets;
            ReadOnlySpan<int> neighborSpans = links.LinkNeighborSpanIndices;
            ReadOnlySpan<int> clearanceCm = radius.SpanClearanceCm;

            Span<int> regionIds = output.MutableSpanRegionIds;
            Span<int> parent = output.MutableUnionParent;
            Span<int> rank = output.MutableUnionRank;
            Span<int> componentMin = output.MutableComponentMinSpan;
            Span<int> regionIdByRoot = output.MutableRegionIdByRoot;
            Span<int> firstBySheet = output.MutableFirstWalkableSpanBySheet;
            Span<int> regionMinSpans = output.MutableRegionMinSpanIndices;
            Span<int> regionMemberCounts = output.MutableRegionMemberCounts;

            for (int span = 0; span < spanCount; span++)
            {
                parent[span] = span;
                rank[span] = 0;
                componentMin[span] = span;
            }

            if (sheetCount > 0)
            {
                firstBySheet.Slice(0, sheetCount).Fill(-1);
            }

            for (int w = 0; w < walkableCount; w++)
            {
                int span = walkableIndices[w];
                if (status[span] != LayeredSpanWalkabilityStatus.Walkable)
                {
                    output.Reset();
                    throw new InvalidOperationException(
                        "LayeredSpanRegionBuilder walkable-span index is not marked Walkable.");
                }

                if (clearanceCm[span] < agentRadiusCm)
                {
                    continue;
                }

                int sheetId = sheetIds[span];
                if (sheetId < 0 || sheetId >= sheetCount)
                {
                    output.Reset();
                    throw new InvalidOperationException(
                        "LayeredSpanRegionBuilder requires every radius-eligible span to carry a valid surface-sheet id.");
                }

                int first = firstBySheet[sheetId];
                if (first < 0)
                {
                    firstBySheet[sheetId] = span;
                }
                else
                {
                    Union(first, span, parent, rank, componentMin);
                }
            }

            for (int w = 0; w < walkableCount; w++)
            {
                int src = walkableIndices[w];
                if (clearanceCm[src] < agentRadiusCm)
                {
                    continue;
                }

                int start = linkOffsets[w];
                int end = linkOffsets[w + 1];
                for (int i = start; i < end; i++)
                {
                    int neighbor = neighborSpans[i];
                    if ((uint)neighbor >= (uint)spanCount ||
                        status[neighbor] != LayeredSpanWalkabilityStatus.Walkable)
                    {
                        output.Reset();
                        throw new InvalidOperationException(
                            "LayeredSpanRegionBuilder walk-link neighbor is not a walkable raw span.");
                    }

                    if (clearanceCm[neighbor] < agentRadiusCm)
                    {
                        continue;
                    }

                    // CSR stores both directions; connectivity is undirected.
                    Union(src, neighbor, parent, rank, componentMin);
                }
            }

            int regionCount = 0;
            for (int span = 0; span < spanCount; span++)
            {
                if (status[span] != LayeredSpanWalkabilityStatus.Walkable ||
                    clearanceCm[span] < agentRadiusCm)
                {
                    continue;
                }

                int root = Find(span, parent);
                if (componentMin[root] != span)
                {
                    continue;
                }

                regionCount++;
            }

            if (regionCount > output.RegionCapacity)
            {
                output.Reset();
                throw new InvalidOperationException(
                    $"LayeredSpanRegionScratch.regionCapacity ({output.RegionCapacity}); required {regionCount}.");
            }

            int nextId = 0;
            for (int span = 0; span < spanCount; span++)
            {
                if (status[span] != LayeredSpanWalkabilityStatus.Walkable ||
                    clearanceCm[span] < agentRadiusCm)
                {
                    regionIds[span] = -1;
                    continue;
                }

                int root = Find(span, parent);
                if (componentMin[root] != span)
                {
                    continue;
                }

                int id = nextId++;
                regionIdByRoot[root] = id;
                regionMinSpans[id] = span;
                regionMemberCounts[id] = 0;
            }

            for (int span = 0; span < spanCount; span++)
            {
                if (status[span] != LayeredSpanWalkabilityStatus.Walkable ||
                    clearanceCm[span] < agentRadiusCm)
                {
                    regionIds[span] = -1;
                    continue;
                }

                int root = Find(span, parent);
                int id = regionIdByRoot[root];
                regionIds[span] = id;
                regionMemberCounts[id]++;
            }

            output.CommitRegionCount(raw, walkability, sheets, links, radius, regionCount);
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
