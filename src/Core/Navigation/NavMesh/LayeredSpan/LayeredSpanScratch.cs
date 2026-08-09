using System;
using Ludots.Core.Navigation.NavMesh.Surface;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Fixed-capacity SoA scratch for layered-span rasterization.
    /// All channels and fill/sort scratch allocate only in the constructor; warmed Rasterize writes reuse them.
    /// </summary>
    public sealed class LayeredSpanScratch
    {
        private readonly int[] _columnSpanCounts;
        private readonly int[] _columnSpanOffsets;
        private readonly int[] _fillCursor;
        private readonly int[] _spanMinYcm;
        private readonly int[] _spanMaxYcm;
        private readonly int[] _spanTriangleIndices;
        private readonly int[] _spanStableTriangleIds;
        private readonly byte[] _spanAreaIds;
        private readonly NavTriangleSurfaceFlags[] _spanSurfaceFlags;
        private readonly Int128[] _spanNormalX;
        private readonly Int128[] _spanNormalY;
        private readonly Int128[] _spanNormalZ;
        private readonly LayeredSpanBoundaryMask[] _spanBoundaryMask;
        private readonly int[] _spanWestMinYcm;
        private readonly int[] _spanWestMaxYcm;
        private readonly int[] _spanWestMinZcm;
        private readonly int[] _spanWestMaxZcm;
        private readonly int[] _spanEastMinYcm;
        private readonly int[] _spanEastMaxYcm;
        private readonly int[] _spanEastMinZcm;
        private readonly int[] _spanEastMaxZcm;
        private readonly int[] _spanNorthMinYcm;
        private readonly int[] _spanNorthMaxYcm;
        private readonly int[] _spanNorthMinXcm;
        private readonly int[] _spanNorthMaxXcm;
        private readonly int[] _spanSouthMinYcm;
        private readonly int[] _spanSouthMaxYcm;
        private readonly int[] _spanSouthMinXcm;
        private readonly int[] _spanSouthMaxXcm;

        private int _columnCount;
        private int _spanCount;
        private ulong _contentGeneration;
        private bool _hasPublishedContent;

        public LayeredSpanScratch(int columnCapacity, int spanCapacity)
        {
            if (columnCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(columnCapacity),
                    columnCapacity,
                    "columnCapacity must be nonnegative.");
            }

            if (spanCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(spanCapacity),
                    spanCapacity,
                    "spanCapacity must be nonnegative.");
            }

            ColumnCapacity = columnCapacity;
            SpanCapacity = spanCapacity;

            _columnSpanCounts = columnCapacity == 0 ? Array.Empty<int>() : new int[columnCapacity];
            _columnSpanOffsets = new int[checked(columnCapacity + 1)];
            _fillCursor = columnCapacity == 0 ? Array.Empty<int>() : new int[columnCapacity];
            _spanMinYcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanMaxYcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanTriangleIndices = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanStableTriangleIds = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanAreaIds = spanCapacity == 0 ? Array.Empty<byte>() : new byte[spanCapacity];
            _spanSurfaceFlags = spanCapacity == 0
                ? Array.Empty<NavTriangleSurfaceFlags>()
                : new NavTriangleSurfaceFlags[spanCapacity];
            _spanNormalX = spanCapacity == 0 ? Array.Empty<Int128>() : new Int128[spanCapacity];
            _spanNormalY = spanCapacity == 0 ? Array.Empty<Int128>() : new Int128[spanCapacity];
            _spanNormalZ = spanCapacity == 0 ? Array.Empty<Int128>() : new Int128[spanCapacity];
            _spanBoundaryMask = spanCapacity == 0
                ? Array.Empty<LayeredSpanBoundaryMask>()
                : new LayeredSpanBoundaryMask[spanCapacity];
            _spanWestMinYcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanWestMaxYcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanWestMinZcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanWestMaxZcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanEastMinYcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanEastMaxYcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanEastMinZcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanEastMaxZcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanNorthMinYcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanNorthMaxYcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanNorthMinXcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanNorthMaxXcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanSouthMinYcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanSouthMaxYcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanSouthMinXcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _spanSouthMaxXcm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];

            PreallocatedChannelPayloadBytes = LayeredSpanScratchChannelPayload.Sum(
                LayeredSpanScratchChannelPayload.Of(_columnSpanCounts),
                LayeredSpanScratchChannelPayload.Of(_columnSpanOffsets),
                LayeredSpanScratchChannelPayload.Of(_fillCursor),
                LayeredSpanScratchChannelPayload.Of(_spanMinYcm),
                LayeredSpanScratchChannelPayload.Of(_spanMaxYcm),
                LayeredSpanScratchChannelPayload.Of(_spanTriangleIndices),
                LayeredSpanScratchChannelPayload.Of(_spanStableTriangleIds),
                LayeredSpanScratchChannelPayload.Of(_spanAreaIds),
                LayeredSpanScratchChannelPayload.Of(_spanSurfaceFlags),
                LayeredSpanScratchChannelPayload.Of(_spanNormalX),
                LayeredSpanScratchChannelPayload.Of(_spanNormalY),
                LayeredSpanScratchChannelPayload.Of(_spanNormalZ),
                LayeredSpanScratchChannelPayload.Of(_spanBoundaryMask),
                LayeredSpanScratchChannelPayload.Of(_spanWestMinYcm),
                LayeredSpanScratchChannelPayload.Of(_spanWestMaxYcm),
                LayeredSpanScratchChannelPayload.Of(_spanWestMinZcm),
                LayeredSpanScratchChannelPayload.Of(_spanWestMaxZcm),
                LayeredSpanScratchChannelPayload.Of(_spanEastMinYcm),
                LayeredSpanScratchChannelPayload.Of(_spanEastMaxYcm),
                LayeredSpanScratchChannelPayload.Of(_spanEastMinZcm),
                LayeredSpanScratchChannelPayload.Of(_spanEastMaxZcm),
                LayeredSpanScratchChannelPayload.Of(_spanNorthMinYcm),
                LayeredSpanScratchChannelPayload.Of(_spanNorthMaxYcm),
                LayeredSpanScratchChannelPayload.Of(_spanNorthMinXcm),
                LayeredSpanScratchChannelPayload.Of(_spanNorthMaxXcm),
                LayeredSpanScratchChannelPayload.Of(_spanSouthMinYcm),
                LayeredSpanScratchChannelPayload.Of(_spanSouthMaxYcm),
                LayeredSpanScratchChannelPayload.Of(_spanSouthMinXcm),
                LayeredSpanScratchChannelPayload.Of(_spanSouthMaxXcm));
        }

        public long PreallocatedChannelPayloadBytes { get; }

        public int ColumnCapacity { get; }

        public int SpanCapacity { get; }

        public int ColumnCount => _columnCount;

        public int SpanCount => _spanCount;

        /// <summary>
        /// Monotonic content generation for this scratch instance.
        /// Zero when unpublished/invalid after construction, Reset, or failure.
        /// Provenance only; must not influence deterministic output bytes.
        /// </summary>
        public ulong ContentGeneration => _hasPublishedContent ? _contentGeneration : 0UL;

        public bool HasPublishedContent => _hasPublishedContent;

        public ReadOnlySpan<int> ColumnSpanCounts => _columnSpanCounts.AsSpan(0, _columnCount);

        public ReadOnlySpan<int> ColumnSpanOffsets => _columnSpanOffsets.AsSpan(0, _columnCount + 1);

        public ReadOnlySpan<int> SpanMinYcm => _spanMinYcm.AsSpan(0, _spanCount);

        public ReadOnlySpan<int> SpanMaxYcm => _spanMaxYcm.AsSpan(0, _spanCount);

        public ReadOnlySpan<int> SpanTriangleIndices => _spanTriangleIndices.AsSpan(0, _spanCount);

        public ReadOnlySpan<int> SpanStableTriangleIds => _spanStableTriangleIds.AsSpan(0, _spanCount);

        public ReadOnlySpan<byte> SpanAreaIds => _spanAreaIds.AsSpan(0, _spanCount);

        /// <summary>
        /// Typed per-span surface flags copied from the source triangle (byte-backed enum; no cast allocation).
        /// </summary>
        public ReadOnlySpan<NavTriangleSurfaceFlags> SpanSurfaceFlags => _spanSurfaceFlags.AsSpan(0, _spanCount);

        public ReadOnlySpan<Int128> SpanNormalX => _spanNormalX.AsSpan(0, _spanCount);

        public ReadOnlySpan<Int128> SpanNormalY => _spanNormalY.AsSpan(0, _spanCount);

        public ReadOnlySpan<Int128> SpanNormalZ => _spanNormalZ.AsSpan(0, _spanCount);

        public ReadOnlySpan<LayeredSpanBoundaryMask> SpanBoundaryMask => _spanBoundaryMask.AsSpan(0, _spanCount);

        public ReadOnlySpan<int> SpanWestMinYcm => _spanWestMinYcm.AsSpan(0, _spanCount);

        public ReadOnlySpan<int> SpanWestMaxYcm => _spanWestMaxYcm.AsSpan(0, _spanCount);

        /// <summary>Along-boundary Z coverage on the west face (triangle ∩ closed west segment).</summary>
        public ReadOnlySpan<int> SpanWestMinZcm => _spanWestMinZcm.AsSpan(0, _spanCount);

        public ReadOnlySpan<int> SpanWestMaxZcm => _spanWestMaxZcm.AsSpan(0, _spanCount);

        public ReadOnlySpan<int> SpanEastMinYcm => _spanEastMinYcm.AsSpan(0, _spanCount);

        public ReadOnlySpan<int> SpanEastMaxYcm => _spanEastMaxYcm.AsSpan(0, _spanCount);

        /// <summary>Along-boundary Z coverage on the east face (triangle ∩ closed east segment).</summary>
        public ReadOnlySpan<int> SpanEastMinZcm => _spanEastMinZcm.AsSpan(0, _spanCount);

        public ReadOnlySpan<int> SpanEastMaxZcm => _spanEastMaxZcm.AsSpan(0, _spanCount);

        public ReadOnlySpan<int> SpanNorthMinYcm => _spanNorthMinYcm.AsSpan(0, _spanCount);

        public ReadOnlySpan<int> SpanNorthMaxYcm => _spanNorthMaxYcm.AsSpan(0, _spanCount);

        /// <summary>Along-boundary X coverage on the north face (triangle ∩ closed north segment).</summary>
        public ReadOnlySpan<int> SpanNorthMinXcm => _spanNorthMinXcm.AsSpan(0, _spanCount);

        public ReadOnlySpan<int> SpanNorthMaxXcm => _spanNorthMaxXcm.AsSpan(0, _spanCount);

        public ReadOnlySpan<int> SpanSouthMinYcm => _spanSouthMinYcm.AsSpan(0, _spanCount);

        public ReadOnlySpan<int> SpanSouthMaxYcm => _spanSouthMaxYcm.AsSpan(0, _spanCount);

        /// <summary>Along-boundary X coverage on the south face (triangle ∩ closed south segment).</summary>
        public ReadOnlySpan<int> SpanSouthMinXcm => _spanSouthMinXcm.AsSpan(0, _spanCount);

        public ReadOnlySpan<int> SpanSouthMaxXcm => _spanSouthMaxXcm.AsSpan(0, _spanCount);

        internal void ResetForRaster()
        {
            InvalidatePublishedContent();
            _columnCount = 0;
            _spanCount = 0;
        }

        internal void PrepareColumns(int columnCount)
        {
            InvalidatePublishedContent();
            _columnCount = columnCount;
            _spanCount = 0;
            if (columnCount > 0)
            {
                _columnSpanCounts.AsSpan(0, columnCount).Clear();
            }
        }

        internal Span<int> MutableColumnSpanCounts => _columnSpanCounts.AsSpan(0, _columnCount);

        internal Span<int> MutableColumnSpanOffsets => _columnSpanOffsets.AsSpan(0, _columnCount + 1);

        internal Span<int> MutableFillCursor => _fillCursor.AsSpan(0, _columnCount);

        internal void CommitSpanCount(int spanCount)
        {
            _spanCount = spanCount;
            PublishNewContentGeneration();
        }

        private void InvalidatePublishedContent()
        {
            _hasPublishedContent = false;
        }

        private void PublishNewContentGeneration()
        {
            if (_contentGeneration == ulong.MaxValue)
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanScratch.contentGeneration overflow; recreate the scratch instance.");
            }

            _contentGeneration++;
            _hasPublishedContent = true;
        }

        internal void WriteSpan(
            int spanIndex,
            int minYcm,
            int maxYcm,
            int triangleIndex,
            int stableTriangleId,
            byte areaId,
            NavTriangleSurfaceFlags surfaceFlags,
            Int128 normalX,
            Int128 normalY,
            Int128 normalZ,
            LayeredSpanBoundaryMask boundaryMask,
            int westMinYcm,
            int westMaxYcm,
            int westMinZcm,
            int westMaxZcm,
            int eastMinYcm,
            int eastMaxYcm,
            int eastMinZcm,
            int eastMaxZcm,
            int northMinYcm,
            int northMaxYcm,
            int northMinXcm,
            int northMaxXcm,
            int southMinYcm,
            int southMaxYcm,
            int southMinXcm,
            int southMaxXcm)
        {
            _spanMinYcm[spanIndex] = minYcm;
            _spanMaxYcm[spanIndex] = maxYcm;
            _spanTriangleIndices[spanIndex] = triangleIndex;
            _spanStableTriangleIds[spanIndex] = stableTriangleId;
            _spanAreaIds[spanIndex] = areaId;
            _spanSurfaceFlags[spanIndex] = surfaceFlags;
            _spanNormalX[spanIndex] = normalX;
            _spanNormalY[spanIndex] = normalY;
            _spanNormalZ[spanIndex] = normalZ;
            _spanBoundaryMask[spanIndex] = boundaryMask;
            _spanWestMinYcm[spanIndex] = westMinYcm;
            _spanWestMaxYcm[spanIndex] = westMaxYcm;
            _spanWestMinZcm[spanIndex] = westMinZcm;
            _spanWestMaxZcm[spanIndex] = westMaxZcm;
            _spanEastMinYcm[spanIndex] = eastMinYcm;
            _spanEastMaxYcm[spanIndex] = eastMaxYcm;
            _spanEastMinZcm[spanIndex] = eastMinZcm;
            _spanEastMaxZcm[spanIndex] = eastMaxZcm;
            _spanNorthMinYcm[spanIndex] = northMinYcm;
            _spanNorthMaxYcm[spanIndex] = northMaxYcm;
            _spanNorthMinXcm[spanIndex] = northMinXcm;
            _spanNorthMaxXcm[spanIndex] = northMaxXcm;
            _spanSouthMinYcm[spanIndex] = southMinYcm;
            _spanSouthMaxYcm[spanIndex] = southMaxYcm;
            _spanSouthMinXcm[spanIndex] = southMinXcm;
            _spanSouthMaxXcm[spanIndex] = southMaxXcm;
        }

        internal void SortColumnSpans(int start, int count)
        {
            if (count <= 1)
            {
                return;
            }

            HeapSort(start, count);
        }

        private void HeapSort(int start, int count)
        {
            for (int i = (count / 2) - 1; i >= 0; i--)
            {
                SiftDown(start, count, i);
            }

            for (int end = count - 1; end > 0; end--)
            {
                SwapRows(start, 0, end);
                SiftDown(start, end, 0);
            }
        }

        private void SiftDown(int start, int heapSize, int root)
        {
            while (true)
            {
                int largest = root;
                int left = (root * 2) + 1;
                int right = left + 1;
                if (left < heapSize && CompareSpans(start + left, start + largest) > 0)
                {
                    largest = left;
                }

                if (right < heapSize && CompareSpans(start + right, start + largest) > 0)
                {
                    largest = right;
                }

                if (largest == root)
                {
                    return;
                }

                SwapRows(start, root, largest);
                root = largest;
            }
        }

        private int CompareSpans(int left, int right)
        {
            int cmp = _spanMinYcm[left].CompareTo(_spanMinYcm[right]);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = _spanMaxYcm[left].CompareTo(_spanMaxYcm[right]);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = _spanStableTriangleIds[left].CompareTo(_spanStableTriangleIds[right]);
            if (cmp != 0)
            {
                return cmp;
            }

            return _spanTriangleIndices[left].CompareTo(_spanTriangleIndices[right]);
        }

        private void SwapRows(int start, int leftOffset, int rightOffset)
        {
            int left = start + leftOffset;
            int right = start + rightOffset;

            Swap(ref _spanMinYcm[left], ref _spanMinYcm[right]);
            Swap(ref _spanMaxYcm[left], ref _spanMaxYcm[right]);
            Swap(ref _spanTriangleIndices[left], ref _spanTriangleIndices[right]);
            Swap(ref _spanStableTriangleIds[left], ref _spanStableTriangleIds[right]);
            Swap(ref _spanAreaIds[left], ref _spanAreaIds[right]);
            Swap(ref _spanSurfaceFlags[left], ref _spanSurfaceFlags[right]);
            Swap(ref _spanNormalX[left], ref _spanNormalX[right]);
            Swap(ref _spanNormalY[left], ref _spanNormalY[right]);
            Swap(ref _spanNormalZ[left], ref _spanNormalZ[right]);
            Swap(ref _spanBoundaryMask[left], ref _spanBoundaryMask[right]);
            Swap(ref _spanWestMinYcm[left], ref _spanWestMinYcm[right]);
            Swap(ref _spanWestMaxYcm[left], ref _spanWestMaxYcm[right]);
            Swap(ref _spanWestMinZcm[left], ref _spanWestMinZcm[right]);
            Swap(ref _spanWestMaxZcm[left], ref _spanWestMaxZcm[right]);
            Swap(ref _spanEastMinYcm[left], ref _spanEastMinYcm[right]);
            Swap(ref _spanEastMaxYcm[left], ref _spanEastMaxYcm[right]);
            Swap(ref _spanEastMinZcm[left], ref _spanEastMinZcm[right]);
            Swap(ref _spanEastMaxZcm[left], ref _spanEastMaxZcm[right]);
            Swap(ref _spanNorthMinYcm[left], ref _spanNorthMinYcm[right]);
            Swap(ref _spanNorthMaxYcm[left], ref _spanNorthMaxYcm[right]);
            Swap(ref _spanNorthMinXcm[left], ref _spanNorthMinXcm[right]);
            Swap(ref _spanNorthMaxXcm[left], ref _spanNorthMaxXcm[right]);
            Swap(ref _spanSouthMinYcm[left], ref _spanSouthMinYcm[right]);
            Swap(ref _spanSouthMaxYcm[left], ref _spanSouthMaxYcm[right]);
            Swap(ref _spanSouthMinXcm[left], ref _spanSouthMinXcm[right]);
            Swap(ref _spanSouthMaxXcm[left], ref _spanSouthMaxXcm[right]);
        }

        private static void Swap<T>(ref T left, ref T right)
        {
            T tmp = left;
            left = right;
            right = tmp;
        }
    }
}
