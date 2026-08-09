using System;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Fixed-capacity SoA scratch for four-neighbor layered-span walk-link CSR.
    /// Each directed link stores the positive-length shared along-boundary portal interval
    /// [minAlongCm, maxAlongCm] used to accept it. All channels allocate only in the constructor;
    /// warmed Build reuses them.
    /// </summary>
    public sealed class LayeredSpanWalkLinkScratch
    {
        private readonly int[] _linkOffsets;
        private readonly int[] _linkNeighborSpanIndices;
        private readonly LayeredSpanNeighborDirection[] _linkNeighborDirections;
        private readonly int[] _linkPortalMinAlongCm;
        private readonly int[] _linkPortalMaxAlongCm;
        private readonly int[] _linkCounts;

        private int _walkableSpanCount;
        private int _linkCount;
        private ulong _contentGeneration;
        private bool _hasPublishedContent;
        private LayeredSpanScratch? _sourceRaw;
        private ulong _sourceRawContentGeneration;
        private LayeredSpanWalkabilityScratch? _sourceWalkability;
        private ulong _sourceWalkabilityContentGeneration;

        public LayeredSpanWalkLinkScratch(int walkableSpanCapacity, int linkCapacity)
        {
            if (walkableSpanCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(walkableSpanCapacity),
                    walkableSpanCapacity,
                    "walkableSpanCapacity must be nonnegative.");
            }

            if (linkCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(linkCapacity),
                    linkCapacity,
                    "linkCapacity must be nonnegative.");
            }

            WalkableSpanCapacity = walkableSpanCapacity;
            LinkCapacity = linkCapacity;

            _linkOffsets = new int[checked(walkableSpanCapacity + 1)];
            _linkNeighborSpanIndices = linkCapacity == 0 ? Array.Empty<int>() : new int[linkCapacity];
            _linkNeighborDirections = linkCapacity == 0
                ? Array.Empty<LayeredSpanNeighborDirection>()
                : new LayeredSpanNeighborDirection[linkCapacity];
            _linkPortalMinAlongCm = linkCapacity == 0 ? Array.Empty<int>() : new int[linkCapacity];
            _linkPortalMaxAlongCm = linkCapacity == 0 ? Array.Empty<int>() : new int[linkCapacity];
            _linkCounts = walkableSpanCapacity == 0 ? Array.Empty<int>() : new int[walkableSpanCapacity];

            PreallocatedChannelPayloadBytes = LayeredSpanScratchChannelPayload.Sum(
                LayeredSpanScratchChannelPayload.Of(_linkOffsets),
                LayeredSpanScratchChannelPayload.Of(_linkNeighborSpanIndices),
                LayeredSpanScratchChannelPayload.Of(_linkNeighborDirections),
                LayeredSpanScratchChannelPayload.Of(_linkPortalMinAlongCm),
                LayeredSpanScratchChannelPayload.Of(_linkPortalMaxAlongCm),
                LayeredSpanScratchChannelPayload.Of(_linkCounts));
        }

        public long PreallocatedChannelPayloadBytes { get; }

        public int WalkableSpanCapacity { get; }

        public int LinkCapacity { get; }

        public int WalkableSpanCount => _walkableSpanCount;

        public int LinkCount => _linkCount;

        /// <summary>
        /// Monotonic content generation for this scratch instance.
        /// Zero when unpublished/invalid after construction, Reset, or failure.
        /// Provenance only; must not influence deterministic output bytes.
        /// </summary>
        public ulong ContentGeneration => _hasPublishedContent ? _contentGeneration : 0UL;

        public bool HasPublishedContent => _hasPublishedContent;

        public ReadOnlySpan<int> LinkOffsets => _linkOffsets.AsSpan(0, _walkableSpanCount + 1);

        public ReadOnlySpan<int> LinkNeighborSpanIndices => _linkNeighborSpanIndices.AsSpan(0, _linkCount);

        public ReadOnlySpan<LayeredSpanNeighborDirection> LinkNeighborDirections
            => _linkNeighborDirections.AsSpan(0, _linkCount);

        /// <summary>
        /// Inclusive lower bound of the accepted shared along-boundary portal interval (cm).
        /// West/East portals use Z; North/South portals use X.
        /// </summary>
        public ReadOnlySpan<int> LinkPortalMinAlongCm => _linkPortalMinAlongCm.AsSpan(0, _linkCount);

        /// <summary>
        /// Exclusive-of-degenerate upper bound of the accepted shared along-boundary portal interval (cm).
        /// Interval length is strictly positive: maxAlongCm &gt; minAlongCm.
        /// </summary>
        public ReadOnlySpan<int> LinkPortalMaxAlongCm => _linkPortalMaxAlongCm.AsSpan(0, _linkCount);

        internal void Reset()
        {
            InvalidatePublishedContent();
            _walkableSpanCount = 0;
            _linkCount = 0;
        }

        internal Span<int> MutableLinkOffsets => _linkOffsets.AsSpan(0, WalkableSpanCapacity + 1);

        internal Span<int> MutableLinkNeighborSpanIndices => _linkNeighborSpanIndices.AsSpan(0, LinkCapacity);

        internal Span<LayeredSpanNeighborDirection> MutableLinkNeighborDirections
            => _linkNeighborDirections.AsSpan(0, LinkCapacity);

        internal Span<int> MutableLinkPortalMinAlongCm => _linkPortalMinAlongCm.AsSpan(0, LinkCapacity);

        internal Span<int> MutableLinkPortalMaxAlongCm => _linkPortalMaxAlongCm.AsSpan(0, LinkCapacity);

        internal Span<int> MutableLinkCounts => _linkCounts.AsSpan(0, WalkableSpanCapacity);

        internal void Commit(
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walkability,
            int walkableSpanCount,
            int linkCount)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (walkability == null) throw new ArgumentNullException(nameof(walkability));
            if (!raw.HasPublishedContent || !walkability.HasPublishedContent)
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanWalkLinkScratch commit requires published raw and walkability scratch content.");
            }

            if (!walkability.WasBuiltFrom(raw))
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanWalkLinkScratch commit requires walkability published from the same raw scratch content generation.");
            }

            _sourceRaw = raw;
            _sourceRawContentGeneration = raw.ContentGeneration;
            _sourceWalkability = walkability;
            _sourceWalkabilityContentGeneration = walkability.ContentGeneration;
            _walkableSpanCount = walkableSpanCount;
            _linkCount = linkCount;
            PublishNewContentGeneration();
        }

        /// <summary>
        /// Scratch-ownership check only; must not influence deterministic output bytes.
        /// </summary>
        internal bool WasBuiltFrom(LayeredSpanScratch raw, LayeredSpanWalkabilityScratch walkability)
        {
            if (!_hasPublishedContent ||
                raw == null ||
                walkability == null ||
                !raw.HasPublishedContent ||
                !walkability.HasPublishedContent)
            {
                return false;
            }

            return ReferenceEquals(_sourceRaw, raw) &&
                   ReferenceEquals(_sourceWalkability, walkability) &&
                   _sourceRawContentGeneration == raw.ContentGeneration &&
                   _sourceWalkabilityContentGeneration == walkability.ContentGeneration;
        }

        private void InvalidatePublishedContent()
        {
            _hasPublishedContent = false;
            _sourceRaw = null;
            _sourceRawContentGeneration = 0;
            _sourceWalkability = null;
            _sourceWalkabilityContentGeneration = 0;
        }

        private void PublishNewContentGeneration()
        {
            if (_contentGeneration == ulong.MaxValue)
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanWalkLinkScratch.contentGeneration overflow; recreate the scratch instance.");
            }

            _contentGeneration++;
            _hasPublishedContent = true;
        }
    }
}
