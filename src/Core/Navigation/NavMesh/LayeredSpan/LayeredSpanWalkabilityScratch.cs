using System;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Fixed-capacity SoA scratch for layered-span slope and vertical-clearance classification.
    /// All channels allocate only in the constructor; warmed Classify reuses them.
    /// </summary>
    public sealed class LayeredSpanWalkabilityScratch
    {
        private readonly LayeredSpanWalkabilityStatus[] _spanStatus;
        private readonly int[] _spanClearanceCm;
        private readonly int[] _walkableSpanIndices;
        private readonly int[] _columnWalkableCounts;
        private readonly int[] _columnWalkableOffsets;
        private readonly int[] _prefixMaxMaxYcm;

        private int _columnCount;
        private int _classifiedSpanCount;
        private int _walkableSpanCount;
        private ulong _contentGeneration;
        private bool _hasPublishedContent;
        private LayeredSpanScratch? _sourceRaw;
        private ulong _sourceRawContentGeneration;

        public LayeredSpanWalkabilityScratch(
            int columnCapacity,
            int classifiedSpanCapacity,
            int walkableSpanCapacity)
        {
            if (columnCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(columnCapacity),
                    columnCapacity,
                    "columnCapacity must be nonnegative.");
            }

            if (classifiedSpanCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(classifiedSpanCapacity),
                    classifiedSpanCapacity,
                    "classifiedSpanCapacity must be nonnegative.");
            }

            if (walkableSpanCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(walkableSpanCapacity),
                    walkableSpanCapacity,
                    "walkableSpanCapacity must be nonnegative.");
            }

            ColumnCapacity = columnCapacity;
            ClassifiedSpanCapacity = classifiedSpanCapacity;
            WalkableSpanCapacity = walkableSpanCapacity;

            _spanStatus = classifiedSpanCapacity == 0
                ? Array.Empty<LayeredSpanWalkabilityStatus>()
                : new LayeredSpanWalkabilityStatus[classifiedSpanCapacity];
            _spanClearanceCm = classifiedSpanCapacity == 0
                ? Array.Empty<int>()
                : new int[classifiedSpanCapacity];
            _walkableSpanIndices = walkableSpanCapacity == 0
                ? Array.Empty<int>()
                : new int[walkableSpanCapacity];
            _columnWalkableCounts = columnCapacity == 0
                ? Array.Empty<int>()
                : new int[columnCapacity];
            _columnWalkableOffsets = new int[checked(columnCapacity + 1)];
            _prefixMaxMaxYcm = classifiedSpanCapacity == 0
                ? Array.Empty<int>()
                : new int[classifiedSpanCapacity];

            PreallocatedChannelPayloadBytes = LayeredSpanScratchChannelPayload.Sum(
                LayeredSpanScratchChannelPayload.Of(_spanStatus),
                LayeredSpanScratchChannelPayload.Of(_spanClearanceCm),
                LayeredSpanScratchChannelPayload.Of(_walkableSpanIndices),
                LayeredSpanScratchChannelPayload.Of(_columnWalkableCounts),
                LayeredSpanScratchChannelPayload.Of(_columnWalkableOffsets),
                LayeredSpanScratchChannelPayload.Of(_prefixMaxMaxYcm));
        }

        public long PreallocatedChannelPayloadBytes { get; }

        public int ColumnCapacity { get; }

        public int ClassifiedSpanCapacity { get; }

        public int WalkableSpanCapacity { get; }

        public int ColumnCount => _columnCount;

        public int ClassifiedSpanCount => _classifiedSpanCount;

        public int WalkableSpanCount => _walkableSpanCount;

        /// <summary>
        /// Monotonic content generation for this scratch instance.
        /// Zero when unpublished/invalid after construction, Reset, or failure.
        /// Provenance only; must not influence deterministic output bytes.
        /// </summary>
        public ulong ContentGeneration => _hasPublishedContent ? _contentGeneration : 0UL;

        public bool HasPublishedContent => _hasPublishedContent;

        public ReadOnlySpan<LayeredSpanWalkabilityStatus> SpanStatus
            => _spanStatus.AsSpan(0, _classifiedSpanCount);

        public ReadOnlySpan<int> SpanClearanceCm
            => _spanClearanceCm.AsSpan(0, _classifiedSpanCount);

        public ReadOnlySpan<int> WalkableSpanIndices
            => _walkableSpanIndices.AsSpan(0, _walkableSpanCount);

        public ReadOnlySpan<int> ColumnWalkableCounts
            => _columnWalkableCounts.AsSpan(0, _columnCount);

        public ReadOnlySpan<int> ColumnWalkableOffsets
            => _columnWalkableOffsets.AsSpan(0, _columnCount + 1);

        internal void Reset()
        {
            InvalidatePublishedContent();
            _columnCount = 0;
            _classifiedSpanCount = 0;
            _walkableSpanCount = 0;
        }

        internal Span<LayeredSpanWalkabilityStatus> MutableSpanStatus
            => _spanStatus.AsSpan(0, _classifiedSpanCount);

        internal Span<int> MutableSpanClearanceCm
            => _spanClearanceCm.AsSpan(0, _classifiedSpanCount);

        internal Span<int> MutableWalkableSpanIndices
            => _walkableSpanIndices.AsSpan(0, WalkableSpanCapacity);

        internal Span<int> MutableColumnWalkableCounts
            => _columnWalkableCounts.AsSpan(0, _columnCount);

        internal Span<int> MutableColumnWalkableOffsets
            => _columnWalkableOffsets.AsSpan(0, _columnCount + 1);

        internal Span<int> MutablePrefixMaxMaxYcm
            => _prefixMaxMaxYcm.AsSpan(0, _classifiedSpanCount);

        internal void Prepare(int columnCount, int classifiedSpanCount)
        {
            InvalidatePublishedContent();
            _columnCount = columnCount;
            _classifiedSpanCount = classifiedSpanCount;
            _walkableSpanCount = 0;
            if (columnCount > 0)
            {
                _columnWalkableCounts.AsSpan(0, columnCount).Clear();
            }
        }

        internal void CommitWalkableSpanCount(LayeredSpanScratch raw, int walkableSpanCount)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (!raw.HasPublishedContent)
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanWalkabilityScratch commit requires published raw scratch content.");
            }

            _sourceRaw = raw;
            _sourceRawContentGeneration = raw.ContentGeneration;
            _walkableSpanCount = walkableSpanCount;
            PublishNewContentGeneration();
        }

        /// <summary>
        /// Republish after in-place obstacle overlay mutation while keeping the same raw provenance.
        /// </summary>
        internal void CommitOverlayRepublish(LayeredSpanScratch raw, int walkableSpanCount)
            => CommitWalkableSpanCount(raw, walkableSpanCount);

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
                    "LayeredSpanWalkabilityScratch.contentGeneration overflow; recreate the scratch instance.");
            }

            _contentGeneration++;
            _hasPublishedContent = true;
        }
    }
}
