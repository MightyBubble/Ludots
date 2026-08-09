using System;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Fixed-capacity SoA scratch for same-column surface-sheet equivalence ids.
    /// All channels allocate only in the constructor; warmed Assign reuses them.
    /// </summary>
    public sealed class LayeredSpanSurfaceSheetScratch
    {
        private readonly int[] _spanSheetIds;
        private readonly int[] _unionParent;
        private readonly int[] _unionRank;
        private readonly int[] _componentMinSpan;
        private readonly int[] _sheetIdByRoot;

        private int _columnCount;
        private int _spanCount;
        private int _sheetCount;
        private ulong _contentGeneration;
        private bool _hasPublishedContent;
        private LayeredSpanScratch? _sourceRaw;
        private ulong _sourceRawContentGeneration;

        public LayeredSpanSurfaceSheetScratch(int columnCapacity, int spanCapacity)
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

            _spanSheetIds = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _unionParent = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _unionRank = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _componentMinSpan = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _sheetIdByRoot = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];

            PreallocatedChannelPayloadBytes = LayeredSpanScratchChannelPayload.Sum(
                LayeredSpanScratchChannelPayload.Of(_spanSheetIds),
                LayeredSpanScratchChannelPayload.Of(_unionParent),
                LayeredSpanScratchChannelPayload.Of(_unionRank),
                LayeredSpanScratchChannelPayload.Of(_componentMinSpan),
                LayeredSpanScratchChannelPayload.Of(_sheetIdByRoot));
        }

        public long PreallocatedChannelPayloadBytes { get; }

        public int ColumnCapacity { get; }

        public int SpanCapacity { get; }

        public int ColumnCount => _columnCount;

        public int SpanCount => _spanCount;

        public int SheetCount => _sheetCount;

        /// <summary>
        /// Monotonic content generation for this scratch instance.
        /// Zero when unpublished/invalid after construction, Reset, or failure.
        /// Provenance only; must not influence deterministic output bytes.
        /// </summary>
        public ulong ContentGeneration => _hasPublishedContent ? _contentGeneration : 0UL;

        public bool HasPublishedContent => _hasPublishedContent;

        /// <summary>
        /// Per-source-span sheet id. Non walk-candidate spans are -1.
        /// </summary>
        public ReadOnlySpan<int> SpanSheetIds => _spanSheetIds.AsSpan(0, _spanCount);

        internal void Reset()
        {
            InvalidatePublishedContent();
            _columnCount = 0;
            _spanCount = 0;
            _sheetCount = 0;
        }

        internal Span<int> MutableSpanSheetIds => _spanSheetIds.AsSpan(0, _spanCount);

        internal Span<int> MutableUnionParent => _unionParent.AsSpan(0, _spanCount);

        internal Span<int> MutableUnionRank => _unionRank.AsSpan(0, _spanCount);

        internal Span<int> MutableComponentMinSpan => _componentMinSpan.AsSpan(0, _spanCount);

        internal Span<int> MutableSheetIdByRoot => _sheetIdByRoot.AsSpan(0, _spanCount);

        internal void Prepare(int columnCount, int spanCount)
        {
            InvalidatePublishedContent();
            _columnCount = columnCount;
            _spanCount = spanCount;
            _sheetCount = 0;
            if (spanCount > 0)
            {
                _spanSheetIds.AsSpan(0, spanCount).Fill(-1);
            }
        }

        internal void CommitSheetCount(LayeredSpanScratch raw, int sheetCount)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (!raw.HasPublishedContent)
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanSurfaceSheetScratch commit requires published raw scratch content.");
            }

            _sourceRaw = raw;
            _sourceRawContentGeneration = raw.ContentGeneration;
            _sheetCount = sheetCount;
            PublishNewContentGeneration();
        }

        /// <summary>
        /// Scratch-ownership check only; must not influence deterministic output bytes.
        /// </summary>
        internal bool WasBuiltFrom(LayeredSpanScratch raw)
        {
            if (!_hasPublishedContent || raw == null || !raw.HasPublishedContent)
            {
                return false;
            }

            return ReferenceEquals(_sourceRaw, raw) &&
                   _sourceRawContentGeneration == raw.ContentGeneration;
        }

        private void InvalidatePublishedContent()
        {
            _hasPublishedContent = false;
            _sourceRaw = null;
            _sourceRawContentGeneration = 0;
        }

        private void PublishNewContentGeneration()
        {
            if (_contentGeneration == ulong.MaxValue)
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanSurfaceSheetScratch.contentGeneration overflow; recreate the scratch instance.");
            }

            _contentGeneration++;
            _hasPublishedContent = true;
        }
    }
}
