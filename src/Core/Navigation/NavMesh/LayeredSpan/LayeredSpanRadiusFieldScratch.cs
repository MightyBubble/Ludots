using System;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Fixed-capacity SoA scratch for conservative horizontal edge-clearance (integer centimeters).
    /// Values are agent-radius-independent lower bounds; non-walkable spans are 0.
    /// Same-column surface-sheet members share one clearance. All channels allocate only in the
    /// constructor; warmed Build reuses them.
    /// </summary>
    public sealed class LayeredSpanRadiusFieldScratch
    {
        private readonly int[] _spanClearanceCm;
        private readonly int[] _sheetClearanceCm;
        private readonly int[] _firstWalkableSpanBySheet;
        private readonly int[] _nextWalkableSpanBySheet;
        private readonly int[] _sheetColumn;
        private readonly byte[] _sheetHasWalkable;
        private readonly byte[] _sheetIsBoundarySeed;
        private readonly int[] _bfsQueue;
        private readonly int[] _spanToWalkableIndex;
        private readonly int[] _portalMinAlongCm;
        private readonly int[] _portalMaxAlongCm;

        private int _spanCount;
        private int _sheetCount;
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

        public LayeredSpanRadiusFieldScratch(int spanCapacity, int sheetCapacity, int portalIntervalCapacity)
        {
            if (spanCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(spanCapacity),
                    spanCapacity,
                    "spanCapacity must be nonnegative.");
            }

            if (sheetCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sheetCapacity),
                    sheetCapacity,
                    "sheetCapacity must be nonnegative.");
            }

            if (portalIntervalCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(portalIntervalCapacity),
                    portalIntervalCapacity,
                    "portalIntervalCapacity must be nonnegative.");
            }

            SpanCapacity = spanCapacity;
            SheetCapacity = sheetCapacity;
            PortalIntervalCapacity = portalIntervalCapacity;

            _spanClearanceCm = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _sheetClearanceCm = sheetCapacity == 0 ? Array.Empty<int>() : new int[sheetCapacity];
            _firstWalkableSpanBySheet = sheetCapacity == 0 ? Array.Empty<int>() : new int[sheetCapacity];
            _nextWalkableSpanBySheet = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _sheetColumn = sheetCapacity == 0 ? Array.Empty<int>() : new int[sheetCapacity];
            _sheetHasWalkable = sheetCapacity == 0 ? Array.Empty<byte>() : new byte[sheetCapacity];
            _sheetIsBoundarySeed = sheetCapacity == 0 ? Array.Empty<byte>() : new byte[sheetCapacity];
            _bfsQueue = sheetCapacity == 0 ? Array.Empty<int>() : new int[sheetCapacity];
            _spanToWalkableIndex = spanCapacity == 0 ? Array.Empty<int>() : new int[spanCapacity];
            _portalMinAlongCm = portalIntervalCapacity == 0
                ? Array.Empty<int>()
                : new int[portalIntervalCapacity];
            _portalMaxAlongCm = portalIntervalCapacity == 0
                ? Array.Empty<int>()
                : new int[portalIntervalCapacity];

            PreallocatedChannelPayloadBytes = LayeredSpanScratchChannelPayload.Sum(
                LayeredSpanScratchChannelPayload.Of(_spanClearanceCm),
                LayeredSpanScratchChannelPayload.Of(_sheetClearanceCm),
                LayeredSpanScratchChannelPayload.Of(_firstWalkableSpanBySheet),
                LayeredSpanScratchChannelPayload.Of(_nextWalkableSpanBySheet),
                LayeredSpanScratchChannelPayload.Of(_sheetColumn),
                LayeredSpanScratchChannelPayload.Of(_sheetHasWalkable),
                LayeredSpanScratchChannelPayload.Of(_sheetIsBoundarySeed),
                LayeredSpanScratchChannelPayload.Of(_bfsQueue),
                LayeredSpanScratchChannelPayload.Of(_spanToWalkableIndex),
                LayeredSpanScratchChannelPayload.Of(_portalMinAlongCm),
                LayeredSpanScratchChannelPayload.Of(_portalMaxAlongCm));
        }

        public long PreallocatedChannelPayloadBytes { get; }

        public int SpanCapacity { get; }

        public int SheetCapacity { get; }

        public int PortalIntervalCapacity { get; }

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
        /// Conservative horizontal clearance lower bound in integer centimeters.
        /// Non-walkable spans are 0. Same-sheet walkable members share one value.
        /// </summary>
        public ReadOnlySpan<int> SpanClearanceCm => _spanClearanceCm.AsSpan(0, _spanCount);

        internal void Reset()
        {
            InvalidatePublishedContent();
            _spanCount = 0;
            _sheetCount = 0;
        }

        internal Span<int> MutableSpanClearanceCm => _spanClearanceCm.AsSpan(0, _spanCount);

        internal Span<int> MutableSheetClearanceCm => _sheetClearanceCm.AsSpan(0, SheetCapacity);

        internal Span<int> MutableFirstWalkableSpanBySheet => _firstWalkableSpanBySheet.AsSpan(0, SheetCapacity);

        internal Span<int> MutableNextWalkableSpanBySheet => _nextWalkableSpanBySheet.AsSpan(0, SpanCapacity);

        internal Span<int> MutableSheetColumn => _sheetColumn.AsSpan(0, SheetCapacity);

        internal Span<byte> MutableSheetHasWalkable => _sheetHasWalkable.AsSpan(0, SheetCapacity);

        internal Span<byte> MutableSheetIsBoundarySeed => _sheetIsBoundarySeed.AsSpan(0, SheetCapacity);

        internal Span<int> MutableBfsQueue => _bfsQueue.AsSpan(0, SheetCapacity);

        internal Span<int> MutableSpanToWalkableIndex => _spanToWalkableIndex.AsSpan(0, SpanCapacity);

        internal Span<int> MutablePortalMinAlongCm => _portalMinAlongCm.AsSpan(0, PortalIntervalCapacity);

        internal Span<int> MutablePortalMaxAlongCm => _portalMaxAlongCm.AsSpan(0, PortalIntervalCapacity);

        internal void Prepare(int spanCount, int sheetCount)
        {
            InvalidatePublishedContent();
            _spanCount = spanCount;
            _sheetCount = sheetCount;
            if (spanCount > 0)
            {
                _spanClearanceCm.AsSpan(0, spanCount).Clear();
                _nextWalkableSpanBySheet.AsSpan(0, spanCount).Fill(-1);
                _spanToWalkableIndex.AsSpan(0, spanCount).Fill(-1);
            }

            if (sheetCount > 0)
            {
                _sheetClearanceCm.AsSpan(0, sheetCount).Clear();
                _firstWalkableSpanBySheet.AsSpan(0, sheetCount).Fill(-1);
                _sheetColumn.AsSpan(0, sheetCount).Fill(-1);
                _sheetHasWalkable.AsSpan(0, sheetCount).Clear();
                _sheetIsBoundarySeed.AsSpan(0, sheetCount).Clear();
            }
        }

        internal void Commit(
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walkability,
            LayeredSpanSurfaceSheetScratch sheets,
            LayeredSpanWalkLinkScratch links)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (walkability == null) throw new ArgumentNullException(nameof(walkability));
            if (sheets == null) throw new ArgumentNullException(nameof(sheets));
            if (links == null) throw new ArgumentNullException(nameof(links));
            if (!raw.HasPublishedContent ||
                !walkability.HasPublishedContent ||
                !sheets.HasPublishedContent ||
                !links.HasPublishedContent)
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanRadiusFieldScratch commit requires published raw/walkability/sheets/links content.");
            }

            if (!walkability.WasBuiltFrom(raw) ||
                !sheets.WasBuiltFrom(raw) ||
                !links.WasBuiltFrom(raw, walkability))
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanRadiusFieldScratch commit requires all inputs published from the same scratch identity and content generation chain.");
            }

            _sourceRaw = raw;
            _sourceRawContentGeneration = raw.ContentGeneration;
            _sourceWalkability = walkability;
            _sourceWalkabilityContentGeneration = walkability.ContentGeneration;
            _sourceSheets = sheets;
            _sourceSheetsContentGeneration = sheets.ContentGeneration;
            _sourceLinks = links;
            _sourceLinksContentGeneration = links.ContentGeneration;
            PublishNewContentGeneration();
        }

        /// <summary>
        /// Scratch-ownership check only; must not influence deterministic output bytes.
        /// </summary>
        internal bool WasBuiltFrom(
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walkability,
            LayeredSpanSurfaceSheetScratch sheets,
            LayeredSpanWalkLinkScratch links)
        {
            if (!_hasPublishedContent ||
                raw == null ||
                walkability == null ||
                sheets == null ||
                links == null ||
                !raw.HasPublishedContent ||
                !walkability.HasPublishedContent ||
                !sheets.HasPublishedContent ||
                !links.HasPublishedContent)
            {
                return false;
            }

            return ReferenceEquals(_sourceRaw, raw) &&
                   ReferenceEquals(_sourceWalkability, walkability) &&
                   ReferenceEquals(_sourceSheets, sheets) &&
                   ReferenceEquals(_sourceLinks, links) &&
                   _sourceRawContentGeneration == raw.ContentGeneration &&
                   _sourceWalkabilityContentGeneration == walkability.ContentGeneration &&
                   _sourceSheetsContentGeneration == sheets.ContentGeneration &&
                   _sourceLinksContentGeneration == links.ContentGeneration;
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
        }

        private void PublishNewContentGeneration()
        {
            if (_contentGeneration == ulong.MaxValue)
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanRadiusFieldScratch.contentGeneration overflow; recreate the scratch instance.");
            }

            _contentGeneration++;
            _hasPublishedContent = true;
        }
    }
}
