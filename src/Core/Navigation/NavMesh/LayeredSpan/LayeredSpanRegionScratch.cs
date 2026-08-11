using System;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Fixed-capacity SoA scratch for deterministic layered-span connected regions.
    /// Per-span region ids, per-region min/representative span, and member counts allocate only
    /// in the constructor; warmed Build reuses them.
    /// </summary>
    public sealed class LayeredSpanRegionScratch
    {
        private readonly int[] _spanRegionIds;
        private readonly int[] _regionMinSpanIndices;
        private readonly int[] _regionMemberCounts;
        private readonly int[] _unionParent;
        private readonly int[] _unionRank;
        private readonly int[] _componentMinSpan;
        private readonly int[] _regionIdByRoot;
        private readonly int[] _firstWalkableSpanBySheet;

        private int _spanCount;
        private int _regionCount;
        private ulong _contentGeneration;
        private bool _hasPublishedContent;
        private LayeredSpanScratch? _sourceRaw;
        private ulong _sourceRawContentGeneration;
        private LayeredSpanWalkabilityScratch? _sourceWalkability;
        private ulong _sourceWalkabilityContentGeneration;
        private LayeredSpanSurfaceSheetScratch? _sourceSheets;
        private ulong _sourceSheetsContentGeneration;
        private LayeredSpanWalkLinkScratch? _sourceLinks;
        private ulong _sourceLinksContentGeneration;
        private LayeredSpanRadiusFieldScratch? _sourceRadius;
        private ulong _sourceRadiusContentGeneration;

        public LayeredSpanRegionScratch(int spanCapacity, int regionCapacity)
        {
            if (spanCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(spanCapacity),
                    spanCapacity,
                    "spanCapacity must be nonnegative.");
            }

            if (regionCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(regionCapacity),
                    regionCapacity,
                    "regionCapacity must be nonnegative.");
            }

            SpanCapacity = spanCapacity;
            RegionCapacity = regionCapacity;

            _spanRegionIds = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _regionMinSpanIndices = regionCapacity == 0 ? Array.Empty<int>() : new int[regionCapacity];
            _regionMemberCounts = regionCapacity == 0 ? Array.Empty<int>() : new int[regionCapacity];
            _unionParent = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _unionRank = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _componentMinSpan = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _regionIdByRoot = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _firstWalkableSpanBySheet = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];

            PreallocatedChannelPayloadBytes = LayeredSpanScratchChannelPayload.Sum(
                LayeredSpanScratchChannelPayload.Of(_spanRegionIds),
                LayeredSpanScratchChannelPayload.Of(_regionMinSpanIndices),
                LayeredSpanScratchChannelPayload.Of(_regionMemberCounts),
                LayeredSpanScratchChannelPayload.Of(_unionParent),
                LayeredSpanScratchChannelPayload.Of(_unionRank),
                LayeredSpanScratchChannelPayload.Of(_componentMinSpan),
                LayeredSpanScratchChannelPayload.Of(_regionIdByRoot),
                LayeredSpanScratchChannelPayload.Of(_firstWalkableSpanBySheet));
        }

        public long PreallocatedChannelPayloadBytes { get; }

        public int SpanCapacity { get; }

        public int RegionCapacity { get; }

        public int SpanCount => _spanCount;

        public int RegionCount => _regionCount;

        /// <summary>
        /// Monotonic content generation for this scratch instance.
        /// Zero when unpublished/invalid after construction, Reset, or failure.
        /// Provenance only; must not influence deterministic output bytes.
        /// </summary>
        public ulong ContentGeneration => _hasPublishedContent ? _contentGeneration : 0UL;

        public bool HasPublishedContent => _hasPublishedContent;

        /// <summary>
        /// Per-source-span region id. Non-eligible spans are -1.
        /// </summary>
        public ReadOnlySpan<int> SpanRegionIds => _spanRegionIds.AsSpan(0, _spanCount);

        /// <summary>
        /// Minimum source raw-span index (representative) for each compact region id.
        /// </summary>
        public ReadOnlySpan<int> RegionMinSpanIndices => _regionMinSpanIndices.AsSpan(0, _regionCount);

        /// <summary>
        /// Radius-eligible member count for each compact region id.
        /// </summary>
        public ReadOnlySpan<int> RegionMemberCounts => _regionMemberCounts.AsSpan(0, _regionCount);

        internal void Reset()
        {
            InvalidatePublishedContent();
            _spanCount = 0;
            _regionCount = 0;
        }

        internal Span<int> MutableSpanRegionIds => _spanRegionIds.AsSpan(0, _spanCount);

        internal Span<int> MutableRegionMinSpanIndices => _regionMinSpanIndices.AsSpan(0, RegionCapacity);

        internal Span<int> MutableRegionMemberCounts => _regionMemberCounts.AsSpan(0, RegionCapacity);

        internal Span<int> MutableUnionParent => _unionParent.AsSpan(0, _spanCount);

        internal Span<int> MutableUnionRank => _unionRank.AsSpan(0, _spanCount);

        internal Span<int> MutableComponentMinSpan => _componentMinSpan.AsSpan(0, _spanCount);

        internal Span<int> MutableRegionIdByRoot => _regionIdByRoot.AsSpan(0, _spanCount);

        /// <summary>
        /// Scratch indexed by sheet id (sheetCount &lt;= spanCapacity).
        /// </summary>
        internal Span<int> MutableFirstWalkableSpanBySheet => _firstWalkableSpanBySheet.AsSpan(0, SpanCapacity);

        internal void Prepare(int spanCount)
        {
            InvalidatePublishedContent();
            _spanCount = spanCount;
            _regionCount = 0;
            if (spanCount > 0)
            {
                _spanRegionIds.AsSpan(0, spanCount).Fill(-1);
            }
        }

        internal void CommitRegionCount(
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walkability,
            LayeredSpanSurfaceSheetScratch sheets,
            LayeredSpanWalkLinkScratch links,
            LayeredSpanRadiusFieldScratch radius,
            int regionCount)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (walkability == null) throw new ArgumentNullException(nameof(walkability));
            if (sheets == null) throw new ArgumentNullException(nameof(sheets));
            if (links == null) throw new ArgumentNullException(nameof(links));
            if (radius == null) throw new ArgumentNullException(nameof(radius));
            if (!raw.HasPublishedContent ||
                !walkability.HasPublishedContent ||
                !sheets.HasPublishedContent ||
                !links.HasPublishedContent ||
                !radius.HasPublishedContent)
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanRegionScratch commit requires published raw/walkability/sheets/links/radius content.");
            }

            if (!walkability.WasBuiltFrom(raw) ||
                !sheets.WasBuiltFrom(raw) ||
                !links.WasBuiltFrom(raw, walkability) ||
                !radius.WasBuiltFrom(raw, walkability, sheets, links))
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanRegionScratch commit requires all inputs published from the same scratch identity and content generation chain.");
            }

            _sourceRaw = raw;
            _sourceRawContentGeneration = raw.ContentGeneration;
            _sourceWalkability = walkability;
            _sourceWalkabilityContentGeneration = walkability.ContentGeneration;
            _sourceSheets = sheets;
            _sourceSheetsContentGeneration = sheets.ContentGeneration;
            _sourceLinks = links;
            _sourceLinksContentGeneration = links.ContentGeneration;
            _sourceRadius = radius;
            _sourceRadiusContentGeneration = radius.ContentGeneration;
            _regionCount = regionCount;
            PublishNewContentGeneration();
        }

        /// <summary>
        /// Scratch-ownership check only; must not influence deterministic output bytes.
        /// </summary>
        internal bool WasBuiltFrom(
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walkability,
            LayeredSpanSurfaceSheetScratch sheets,
            LayeredSpanWalkLinkScratch links,
            LayeredSpanRadiusFieldScratch radius)
        {
            if (!_hasPublishedContent ||
                raw == null ||
                walkability == null ||
                sheets == null ||
                links == null ||
                radius == null ||
                !raw.HasPublishedContent ||
                !walkability.HasPublishedContent ||
                !sheets.HasPublishedContent ||
                !links.HasPublishedContent ||
                !radius.HasPublishedContent)
            {
                return false;
            }

            return ReferenceEquals(_sourceRaw, raw) &&
                   ReferenceEquals(_sourceWalkability, walkability) &&
                   ReferenceEquals(_sourceSheets, sheets) &&
                   ReferenceEquals(_sourceLinks, links) &&
                   ReferenceEquals(_sourceRadius, radius) &&
                   _sourceRawContentGeneration == raw.ContentGeneration &&
                   _sourceWalkabilityContentGeneration == walkability.ContentGeneration &&
                   _sourceSheetsContentGeneration == sheets.ContentGeneration &&
                   _sourceLinksContentGeneration == links.ContentGeneration &&
                   _sourceRadiusContentGeneration == radius.ContentGeneration;
        }

        private void InvalidatePublishedContent()
        {
            _hasPublishedContent = false;
            _sourceRaw = null;
            _sourceRawContentGeneration = 0;
            _sourceWalkability = null;
            _sourceWalkabilityContentGeneration = 0;
            _sourceSheets = null;
            _sourceSheetsContentGeneration = 0;
            _sourceLinks = null;
            _sourceLinksContentGeneration = 0;
            _sourceRadius = null;
            _sourceRadiusContentGeneration = 0;
        }

        private void PublishNewContentGeneration()
        {
            if (_contentGeneration == ulong.MaxValue)
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanRegionScratch.contentGeneration overflow; recreate the scratch instance.");
            }

            _contentGeneration++;
            _hasPublishedContent = true;
        }
    }
}
