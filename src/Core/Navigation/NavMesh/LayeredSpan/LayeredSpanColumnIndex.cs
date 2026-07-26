using System;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Zero-allocation lookups over the raw layered-span column CSR offsets.
    /// </summary>
    internal static class LayeredSpanColumnIndex
    {
        public static int FindColumnOfSpan(
            int span,
            ReadOnlySpan<int> columnSpanOffsets,
            int columnCount)
        {
            ValidateShape(columnSpanOffsets, columnCount);
            if (columnCount == 0 ||
                span < columnSpanOffsets[0] ||
                span >= columnSpanOffsets[columnCount])
            {
                return -1;
            }

            // upper_bound(span) - 1 selects the non-empty owner after any repeated
            // offsets introduced by empty columns.
            int lo = 0;
            int hi = columnCount + 1;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (columnSpanOffsets[mid] <= span)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            int column = lo - 1;
            return column >= 0 &&
                   column < columnCount &&
                   span < columnSpanOffsets[column + 1]
                ? column
                : -1;
        }

        public static int AdvanceToColumnOfSpan(
            int span,
            ReadOnlySpan<int> columnSpanOffsets,
            int columnCount,
            ref int columnCursor)
        {
            ValidateShape(columnSpanOffsets, columnCount);
            if (columnCursor < 0 || columnCursor > columnCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(columnCursor),
                    columnCursor,
                    "columnCursor must be within the column CSR range.");
            }

            while (columnCursor < columnCount && span >= columnSpanOffsets[columnCursor + 1])
            {
                columnCursor++;
            }

            return columnCursor < columnCount &&
                   span >= columnSpanOffsets[columnCursor] &&
                   span < columnSpanOffsets[columnCursor + 1]
                ? columnCursor
                : -1;
        }

        private static void ValidateShape(ReadOnlySpan<int> columnSpanOffsets, int columnCount)
        {
            if (columnCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(columnCount),
                    columnCount,
                    "columnCount must be nonnegative.");
            }

            if (columnSpanOffsets.Length <= columnCount)
            {
                throw new ArgumentException(
                    "columnSpanOffsets must contain columnCount + 1 CSR offsets.",
                    nameof(columnSpanOffsets));
            }
        }
    }
}
