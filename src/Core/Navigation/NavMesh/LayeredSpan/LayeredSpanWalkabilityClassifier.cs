using System;
using Ludots.Core.Navigation.NavMesh.Surface;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Stateless fixed-capacity slope and vertical-clearance classifier over rasterized layered spans.
    /// Success-path Classify after scratch warmup allocates 0 managed bytes.
    /// </summary>
    public static class LayeredSpanWalkabilityClassifier
    {
        private static readonly Int128 MaxNormalComponentBeforeSquare = (Int128)1 << 40;

        public static void Classify(
            LayeredSpanScratch raw,
            in LayeredSpanWalkabilitySpec spec,
            LayeredSpanWalkabilityScratch output)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (output == null) throw new ArgumentNullException(nameof(output));

            output.Reset();

            if (!raw.HasPublishedContent)
            {
                throw new InvalidOperationException(
                    "LayeredSpanWalkabilityClassifier requires published raw scratch content.");
            }

            int columnCount = raw.ColumnCount;
            int spanCount = raw.SpanCount;

            if (columnCount > output.ColumnCapacity)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanWalkabilityScratch.columnCapacity ({output.ColumnCapacity}); required {columnCount}.");
            }

            if (spanCount > output.ClassifiedSpanCapacity)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanWalkabilityScratch.classifiedSpanCapacity ({output.ClassifiedSpanCapacity}); required {spanCount}.");
            }

            output.Prepare(columnCount, spanCount);

            ReadOnlySpan<int> columnCounts = raw.ColumnSpanCounts;
            ReadOnlySpan<int> columnOffsets = raw.ColumnSpanOffsets;
            ReadOnlySpan<int> minY = raw.SpanMinYcm;
            ReadOnlySpan<int> maxY = raw.SpanMaxYcm;
            ReadOnlySpan<NavTriangleSurfaceFlags> flags = raw.SpanSurfaceFlags;
            ReadOnlySpan<Int128> nx = raw.SpanNormalX;
            ReadOnlySpan<Int128> ny = raw.SpanNormalY;
            ReadOnlySpan<Int128> nz = raw.SpanNormalZ;

            Span<LayeredSpanWalkabilityStatus> status = output.MutableSpanStatus;
            Span<int> clearance = output.MutableSpanClearanceCm;
            Span<int> prefixMax = output.MutablePrefixMaxMaxYcm;
            Span<int> walkableIndices = output.MutableWalkableSpanIndices;
            Span<int> walkableCounts = output.MutableColumnWalkableCounts;
            Span<int> walkableOffsets = output.MutableColumnWalkableOffsets;

            int walkableCapacity = output.WalkableSpanCapacity;
            int walkableCount = 0;
            int agentHeight = spec.AgentHeightCm;
            int tolerance = spec.SameSurfaceToleranceCm;
            int minUpDotQ1M = spec.MinWalkableUpDotQ1M;
            Int128 thresholdSq = (Int128)minUpDotQ1M * minUpDotQ1M;
            Int128 q1MSq = (Int128)LayeredSpanWalkabilitySpec.UpDotQ1M * LayeredSpanWalkabilitySpec.UpDotQ1M;

            for (int col = 0; col < columnCount; col++)
            {
                int start = columnOffsets[col];
                int count = columnCounts[col];
                int end = start + count;

                BuildSolidPrefixMaxMaxY(maxY, flags, start, end, prefixMax);

                int columnWalkableStart = walkableCount;
                for (int span = start; span < end; span++)
                {
                    int top = maxY[span];
                    int spanClearance = ComputeClearanceCm(
                        minY,
                        maxY,
                        flags,
                        prefixMax,
                        start,
                        end,
                        top,
                        tolerance);
                    clearance[span] = spanClearance;

                    NavTriangleSurfaceFlags surfaceFlags = flags[span];
                    if ((surfaceFlags & NavTriangleSurfaceFlags.WalkCandidate) == 0)
                    {
                        status[span] = LayeredSpanWalkabilityStatus.SolidOnly;
                        continue;
                    }

                    if (!TryAcceptSlope(nx[span], ny[span], nz[span], thresholdSq, q1MSq, out bool degenerate))
                    {
                        status[span] = degenerate
                            ? LayeredSpanWalkabilityStatus.DegenerateNormal
                            : LayeredSpanWalkabilityStatus.SlopeRejected;
                        continue;
                    }

                    if (spanClearance < agentHeight)
                    {
                        status[span] = LayeredSpanWalkabilityStatus.ClearanceRejected;
                        continue;
                    }

                    status[span] = LayeredSpanWalkabilityStatus.Walkable;
                    if (walkableCount < walkableCapacity)
                    {
                        walkableIndices[walkableCount] = span;
                    }

                    walkableCount++;
                }

                walkableCounts[col] = walkableCount - columnWalkableStart;
            }

            if (walkableCount > walkableCapacity)
            {
                output.Reset();
                throw new InvalidOperationException(
                    $"LayeredSpanWalkabilityScratch.walkableSpanCapacity ({walkableCapacity}); required {walkableCount}.");
            }

            int prefix = 0;
            for (int col = 0; col < columnCount; col++)
            {
                walkableOffsets[col] = prefix;
                prefix += walkableCounts[col];
            }

            walkableOffsets[columnCount] = prefix;
            output.CommitWalkableSpanCount(raw, walkableCount);
        }

        private static void BuildSolidPrefixMaxMaxY(
            ReadOnlySpan<int> maxY,
            ReadOnlySpan<NavTriangleSurfaceFlags> flags,
            int start,
            int end,
            Span<int> prefixMax)
        {
            int running = int.MinValue;
            for (int i = start; i < end; i++)
            {
                if ((flags[i] & NavTriangleSurfaceFlags.Solid) != 0)
                {
                    int y = maxY[i];
                    if (y > running)
                    {
                        running = y;
                    }
                }

                prefixMax[i] = running;
            }
        }

        private static int ComputeClearanceCm(
            ReadOnlySpan<int> minY,
            ReadOnlySpan<int> maxY,
            ReadOnlySpan<NavTriangleSurfaceFlags> flags,
            ReadOnlySpan<int> prefixMax,
            int start,
            int end,
            int top,
            int tolerance)
        {
            long headThresholdLong = (long)top + tolerance;
            if (headThresholdLong > int.MaxValue)
            {
                // No solid span minY/maxY can exceed int.MaxValue, so nothing crosses or ceilings above.
                return int.MaxValue;
            }

            int headThreshold = (int)headThresholdLong;

            int lastAtOrBelow = FindLastMinYAtOrBelow(minY, start, end, headThreshold);
            if (lastAtOrBelow >= start)
            {
                int solidMaxThrough = prefixMax[lastAtOrBelow];
                if (solidMaxThrough > headThreshold)
                {
                    return 0;
                }
            }

            int firstAbove = FindFirstMinYAbove(minY, start, end, headThreshold);
            for (int i = firstAbove; i < end; i++)
            {
                if ((flags[i] & NavTriangleSurfaceFlags.Solid) == 0)
                {
                    continue;
                }

                long clearanceLong = (long)minY[i] - top;
                if (clearanceLong >= int.MaxValue)
                {
                    return int.MaxValue;
                }

                if (clearanceLong <= 0)
                {
                    return 0;
                }

                return (int)clearanceLong;
            }

            return int.MaxValue;
        }

        private static int FindLastMinYAtOrBelow(ReadOnlySpan<int> minY, int start, int end, int threshold)
        {
            int lo = start;
            int hi = end;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) / 2);
                if (minY[mid] <= threshold)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo - 1;
        }

        private static int FindFirstMinYAbove(ReadOnlySpan<int> minY, int start, int end, int threshold)
        {
            int lo = start;
            int hi = end;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) / 2);
                if (minY[mid] <= threshold)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }

        private static bool TryAcceptSlope(
            Int128 normalX,
            Int128 normalY,
            Int128 normalZ,
            Int128 thresholdSq,
            Int128 q1MSq,
            out bool degenerate)
        {
            if (normalX == 0 && normalY == 0 && normalZ == 0)
            {
                degenerate = true;
                return false;
            }

            ScaleNormalsForSquare(ref normalX, ref normalY, ref normalZ);

            if (normalX == 0 && normalY == 0 && normalZ == 0)
            {
                // Deterministic right-shift of extreme equal-magnitude components can collapse to zero.
                degenerate = true;
                return false;
            }

            Int128 absNy = normalY < 0 ? -normalY : normalY;
            Int128 lenSq = (normalX * normalX) + (normalY * normalY) + (normalZ * normalZ);
            // abs(Ny)/|N| >= t/Q1M  <=>  abs(Ny)^2 * Q1M^2 >= t^2 * |N|^2
            bool accepted = (absNy * absNy * q1MSq) >= (thresholdSq * lenSq);
            degenerate = false;
            return accepted;
        }

        private static void ScaleNormalsForSquare(ref Int128 nx, ref Int128 ny, ref Int128 nz)
        {
            // Shift absolute magnitudes by a shared count, then restore signs.
            // Arithmetic right-shift on signed Int128 is not a strict mirror for odd components.
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
