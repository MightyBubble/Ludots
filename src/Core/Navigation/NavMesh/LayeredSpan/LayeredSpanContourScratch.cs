using System;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Fixed-capacity SoA scratch for layered-span contour charts, closed rings, and chart seams.
    /// Constructor owns every public and builder-private channel; warmed Build reuses them.
    /// </summary>
    public sealed class LayeredSpanContourScratch
    {
        // ---- Published chart / ring / vertex / seam channels ----
        private readonly int[] _chartMinSpanIndices;
        private readonly int[] _chartRegionIds;
        private readonly byte[] _chartAreaIds;
        private readonly int[] _chartRingOffsets;

        private readonly int[] _ringOffsets;
        private readonly int[] _ringChartIds;
        private readonly int[] _ringRegionIds;
        private readonly byte[] _ringAreaIds;
        private readonly Int128[] _ringSignedArea2;
        private readonly LayeredSpanContourRingKind[] _ringKinds;

        private readonly int[] _vertexXcm;
        private readonly int[] _vertexZcm;
        private readonly int[] _vertexSourceSpanIndices;
        private readonly byte[] _vertexMandatory;

        private readonly int[] _seamChartA;
        private readonly int[] _seamChartB;
        private readonly LayeredSpanNeighborDirection[] _seamDirections;
        private readonly int[] _seamPortalMinAlongCm;
        private readonly int[] _seamPortalMaxAlongCm;
        private readonly int[] _seamSpanA;
        private readonly int[] _seamSpanB;

        // ---- Builder private scratch ----
        private readonly int[] _spanToWalkableIndex;
        private readonly int[] _sheetColumn;
        private readonly int[] _sheetRegionIds;
        private readonly byte[] _sheetAreaIds;
        private readonly int[] _sheetMinSpanIndices;
        private readonly byte[] _sheetEligible;
        private readonly int[] _firstEligibleSpanBySheet;
        private readonly int[] _nextEligibleSpanBySheet;

        private readonly int[] _sheetChartUnionParent;
        private readonly int[] _sheetChartUnionRank;
        private readonly int[] _sheetChartComponentMinSpan;
        private readonly int[] _sheetChartIdByRoot;
        private readonly int[] _sheetToChart;
        private readonly int[] _chartColumnScratch;
        private readonly int[] _chartColumnMarks;
        private int _chartColumnMarkStamp;

        // Column-indexed / component-member scratch (authored columnCapacity ≠ sheetCapacity).
        private readonly int[] _columnSheetFirst;
        private readonly int[] _columnSheetNext;
        private readonly int[] _componentMemberFirst;
        private readonly int[] _componentMemberNext;
        private readonly int[] _componentMemberLast;
        private readonly int[] _componentSize;

        // Chart-id assignment: span-keyed bucket heads + sheet-keyed singly-linked next.
        // Capacities match their index domains; never alias across span/sheet/column contracts.
        private readonly int[] _chartMinSpanBucketFirst;
        private readonly int[] _chartMinSpanBucketNext;

        private readonly int[] _canonicalLinkSheetA;
        private readonly int[] _canonicalLinkSheetB;
        private readonly int[] _canonicalLinkSpanA;
        private readonly int[] _canonicalLinkSpanB;
        private readonly LayeredSpanNeighborDirection[] _canonicalLinkDirections;
        private readonly int[] _canonicalLinkPortalMinAlongCm;
        private readonly int[] _canonicalLinkPortalMaxAlongCm;
        private readonly byte[] _canonicalLinkAcceptedUnion;
        private readonly int[] _sheetCanonicalLinkOffsets;
        private readonly int[] _sheetCanonicalLinkIndices;
        private readonly int[] _sheetCanonicalLinkCounts;

        private readonly int[] _portalMinAlongCm;
        private readonly int[] _portalMaxAlongCm;

        private readonly int[] _edgeFromXcm;
        private readonly int[] _edgeFromZcm;
        private readonly int[] _edgeToXcm;
        private readonly int[] _edgeToZcm;
        private readonly int[] _edgeChartIds;
        private readonly int[] _edgeSourceSpanIndices;
        private readonly byte[] _edgeFromMandatory;
        private readonly byte[] _edgeToMandatory;
        private readonly byte[] _edgeUsed;

        private readonly int[] _splitAlongCm;
        private readonly byte[] _splitMandatory;

        private readonly int[] _adjEdgeIndices;
        private readonly int[] _adjOffsets;
        private readonly int[] _vertexKeyXcm;
        private readonly int[] _vertexKeyZcm;
        private readonly int[] _vertexKeyFirstEdge;
        private readonly int[] _vertexKeyNextEdge;

        private readonly int[] _traceXcm;
        private readonly int[] _traceZcm;
        private readonly int[] _traceSourceSpan;
        private readonly byte[] _traceMandatory;

        private readonly int[] _simplifyKeep;
        private readonly int[] _ringOrder;
        private readonly Int128[] _ringReorderSignedArea2;

        private int _chartCount;
        private int _ringCount;
        private int _vertexCount;
        private int _seamCount;
        private int _edgeCount;
        private int _canonicalLinkCount;
        private int _vertexKeyCount;

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
        private LayeredSpanRegionScratch? _sourceRegions;
        private ulong _sourceRegionsContentGeneration;

        public LayeredSpanContourScratch(
            int columnCapacity,
            int spanCapacity,
            int sheetCapacity,
            int chartCapacity,
            int ringCapacity,
            int vertexCapacity,
            int edgeCapacity,
            int seamCapacity,
            int portalIntervalCapacity,
            int canonicalLinkCapacity,
            int splitPointCapacity)
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
                throw new ArgumentOutOfRangeException(nameof(spanCapacity), spanCapacity, "spanCapacity must be nonnegative.");
            }

            if (sheetCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sheetCapacity), sheetCapacity, "sheetCapacity must be nonnegative.");
            }

            if (chartCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chartCapacity), chartCapacity, "chartCapacity must be nonnegative.");
            }

            if (ringCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ringCapacity), ringCapacity, "ringCapacity must be nonnegative.");
            }

            if (vertexCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexCapacity), vertexCapacity, "vertexCapacity must be nonnegative.");
            }

            if (edgeCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(edgeCapacity), edgeCapacity, "edgeCapacity must be nonnegative.");
            }

            if (seamCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seamCapacity), seamCapacity, "seamCapacity must be nonnegative.");
            }

            if (portalIntervalCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(portalIntervalCapacity),
                    portalIntervalCapacity,
                    "portalIntervalCapacity must be nonnegative.");
            }

            if (canonicalLinkCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(canonicalLinkCapacity),
                    canonicalLinkCapacity,
                    "canonicalLinkCapacity must be nonnegative.");
            }

            if (splitPointCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(splitPointCapacity),
                    splitPointCapacity,
                    "splitPointCapacity must be nonnegative.");
            }

            ColumnCapacity = columnCapacity;
            SpanCapacity = spanCapacity;
            SheetCapacity = sheetCapacity;
            ChartCapacity = chartCapacity;
            RingCapacity = ringCapacity;
            VertexCapacity = vertexCapacity;
            EdgeCapacity = edgeCapacity;
            SeamCapacity = seamCapacity;
            PortalIntervalCapacity = portalIntervalCapacity;
            CanonicalLinkCapacity = canonicalLinkCapacity;
            SplitPointCapacity = splitPointCapacity;

            _chartMinSpanIndices = EmptyInts(chartCapacity);
            _chartRegionIds = EmptyInts(chartCapacity);
            _chartAreaIds = EmptyBytes(chartCapacity);
            _chartRingOffsets = new int[checked(chartCapacity + 1)];

            _ringOffsets = new int[checked(ringCapacity + 1)];
            _ringChartIds = EmptyInts(ringCapacity);
            _ringRegionIds = EmptyInts(ringCapacity);
            _ringAreaIds = EmptyBytes(ringCapacity);
            _ringSignedArea2 = EmptyInt128s(ringCapacity);
            _ringKinds = ringCapacity == 0
                ? Array.Empty<LayeredSpanContourRingKind>()
                : new LayeredSpanContourRingKind[ringCapacity];

            _vertexXcm = EmptyInts(vertexCapacity);
            _vertexZcm = EmptyInts(vertexCapacity);
            _vertexSourceSpanIndices = EmptyInts(vertexCapacity);
            _vertexMandatory = EmptyBytes(vertexCapacity);

            _seamChartA = EmptyInts(seamCapacity);
            _seamChartB = EmptyInts(seamCapacity);
            _seamDirections = seamCapacity == 0
                ? Array.Empty<LayeredSpanNeighborDirection>()
                : new LayeredSpanNeighborDirection[seamCapacity];
            _seamPortalMinAlongCm = EmptyInts(seamCapacity);
            _seamPortalMaxAlongCm = EmptyInts(seamCapacity);
            _seamSpanA = EmptyInts(seamCapacity);
            _seamSpanB = EmptyInts(seamCapacity);

            _spanToWalkableIndex = EmptyInts(spanCapacity);
            _sheetColumn = EmptyInts(sheetCapacity);
            _sheetRegionIds = EmptyInts(sheetCapacity);
            _sheetAreaIds = EmptyBytes(sheetCapacity);
            _sheetMinSpanIndices = EmptyInts(sheetCapacity);
            _sheetEligible = EmptyBytes(sheetCapacity);
            _firstEligibleSpanBySheet = EmptyInts(sheetCapacity);
            _nextEligibleSpanBySheet = EmptyInts(spanCapacity);

            _sheetChartUnionParent = EmptyInts(sheetCapacity);
            _sheetChartUnionRank = EmptyInts(sheetCapacity);
            _sheetChartComponentMinSpan = EmptyInts(sheetCapacity);
            _sheetChartIdByRoot = EmptyInts(sheetCapacity);
            _sheetToChart = EmptyInts(sheetCapacity);
            _chartColumnScratch = EmptyInts(sheetCapacity);
            _chartColumnMarks = EmptyInts(sheetCapacity);

            _columnSheetFirst = EmptyInts(columnCapacity);
            _columnSheetNext = EmptyInts(sheetCapacity);
            _componentMemberFirst = EmptyInts(sheetCapacity);
            _componentMemberNext = EmptyInts(sheetCapacity);
            _componentMemberLast = EmptyInts(sheetCapacity);
            _componentSize = EmptyInts(sheetCapacity);

            _chartMinSpanBucketFirst = EmptyInts(spanCapacity);
            _chartMinSpanBucketNext = EmptyInts(sheetCapacity);

            _canonicalLinkSheetA = EmptyInts(canonicalLinkCapacity);
            _canonicalLinkSheetB = EmptyInts(canonicalLinkCapacity);
            _canonicalLinkSpanA = EmptyInts(canonicalLinkCapacity);
            _canonicalLinkSpanB = EmptyInts(canonicalLinkCapacity);
            _canonicalLinkDirections = canonicalLinkCapacity == 0
                ? Array.Empty<LayeredSpanNeighborDirection>()
                : new LayeredSpanNeighborDirection[canonicalLinkCapacity];
            _canonicalLinkPortalMinAlongCm = EmptyInts(canonicalLinkCapacity);
            _canonicalLinkPortalMaxAlongCm = EmptyInts(canonicalLinkCapacity);
            _canonicalLinkAcceptedUnion = EmptyBytes(canonicalLinkCapacity);
            _sheetCanonicalLinkOffsets = new int[checked(sheetCapacity + 1)];
            _sheetCanonicalLinkIndices = EmptyInts(checked(canonicalLinkCapacity * 2));
            _sheetCanonicalLinkCounts = EmptyInts(sheetCapacity);

            _portalMinAlongCm = EmptyInts(portalIntervalCapacity);
            _portalMaxAlongCm = EmptyInts(portalIntervalCapacity);

            _edgeFromXcm = EmptyInts(edgeCapacity);
            _edgeFromZcm = EmptyInts(edgeCapacity);
            _edgeToXcm = EmptyInts(edgeCapacity);
            _edgeToZcm = EmptyInts(edgeCapacity);
            _edgeChartIds = EmptyInts(edgeCapacity);
            _edgeSourceSpanIndices = EmptyInts(edgeCapacity);
            _edgeFromMandatory = EmptyBytes(edgeCapacity);
            _edgeToMandatory = EmptyBytes(edgeCapacity);
            _edgeUsed = EmptyBytes(edgeCapacity);

            _splitAlongCm = EmptyInts(splitPointCapacity);
            _splitMandatory = EmptyBytes(splitPointCapacity);

            _adjEdgeIndices = EmptyInts(edgeCapacity);
            _adjOffsets = new int[checked(edgeCapacity + 1)];
            _vertexKeyXcm = EmptyInts(edgeCapacity);
            _vertexKeyZcm = EmptyInts(edgeCapacity);
            _vertexKeyFirstEdge = EmptyInts(edgeCapacity);
            _vertexKeyNextEdge = EmptyInts(edgeCapacity);

            _traceXcm = EmptyInts(vertexCapacity);
            _traceZcm = EmptyInts(vertexCapacity);
            _traceSourceSpan = EmptyInts(vertexCapacity);
            _traceMandatory = EmptyBytes(vertexCapacity);

            _simplifyKeep = EmptyInts(vertexCapacity);
            _ringOrder = EmptyInts(ringCapacity);
            _ringReorderSignedArea2 = EmptyInt128s(ringCapacity);

            PreallocatedChannelPayloadBytes = LayeredSpanScratchChannelPayload.Sum(
                LayeredSpanScratchChannelPayload.Of(_chartMinSpanIndices),
                LayeredSpanScratchChannelPayload.Of(_chartRegionIds),
                LayeredSpanScratchChannelPayload.Of(_chartAreaIds),
                LayeredSpanScratchChannelPayload.Of(_chartRingOffsets),
                LayeredSpanScratchChannelPayload.Of(_ringOffsets),
                LayeredSpanScratchChannelPayload.Of(_ringChartIds),
                LayeredSpanScratchChannelPayload.Of(_ringRegionIds),
                LayeredSpanScratchChannelPayload.Of(_ringAreaIds),
                LayeredSpanScratchChannelPayload.Of(_ringSignedArea2),
                LayeredSpanScratchChannelPayload.Of(_ringKinds),
                LayeredSpanScratchChannelPayload.Of(_vertexXcm),
                LayeredSpanScratchChannelPayload.Of(_vertexZcm),
                LayeredSpanScratchChannelPayload.Of(_vertexSourceSpanIndices),
                LayeredSpanScratchChannelPayload.Of(_vertexMandatory),
                LayeredSpanScratchChannelPayload.Of(_seamChartA),
                LayeredSpanScratchChannelPayload.Of(_seamChartB),
                LayeredSpanScratchChannelPayload.Of(_seamDirections),
                LayeredSpanScratchChannelPayload.Of(_seamPortalMinAlongCm),
                LayeredSpanScratchChannelPayload.Of(_seamPortalMaxAlongCm),
                LayeredSpanScratchChannelPayload.Of(_seamSpanA),
                LayeredSpanScratchChannelPayload.Of(_seamSpanB),
                LayeredSpanScratchChannelPayload.Of(_spanToWalkableIndex),
                LayeredSpanScratchChannelPayload.Of(_sheetColumn),
                LayeredSpanScratchChannelPayload.Of(_sheetRegionIds),
                LayeredSpanScratchChannelPayload.Of(_sheetAreaIds),
                LayeredSpanScratchChannelPayload.Of(_sheetMinSpanIndices),
                LayeredSpanScratchChannelPayload.Of(_sheetEligible),
                LayeredSpanScratchChannelPayload.Of(_firstEligibleSpanBySheet),
                LayeredSpanScratchChannelPayload.Of(_nextEligibleSpanBySheet),
                LayeredSpanScratchChannelPayload.Of(_sheetChartUnionParent),
                LayeredSpanScratchChannelPayload.Of(_sheetChartUnionRank),
                LayeredSpanScratchChannelPayload.Of(_sheetChartComponentMinSpan),
                LayeredSpanScratchChannelPayload.Of(_sheetChartIdByRoot),
                LayeredSpanScratchChannelPayload.Of(_sheetToChart),
                LayeredSpanScratchChannelPayload.Of(_chartColumnScratch),
                LayeredSpanScratchChannelPayload.Of(_chartColumnMarks),
                LayeredSpanScratchChannelPayload.Of(_columnSheetFirst),
                LayeredSpanScratchChannelPayload.Of(_columnSheetNext),
                LayeredSpanScratchChannelPayload.Of(_componentMemberFirst),
                LayeredSpanScratchChannelPayload.Of(_componentMemberNext),
                LayeredSpanScratchChannelPayload.Of(_componentMemberLast),
                LayeredSpanScratchChannelPayload.Of(_componentSize),
                LayeredSpanScratchChannelPayload.Of(_chartMinSpanBucketFirst),
                LayeredSpanScratchChannelPayload.Of(_chartMinSpanBucketNext),
                LayeredSpanScratchChannelPayload.Of(_canonicalLinkSheetA),
                LayeredSpanScratchChannelPayload.Of(_canonicalLinkSheetB),
                LayeredSpanScratchChannelPayload.Of(_canonicalLinkSpanA),
                LayeredSpanScratchChannelPayload.Of(_canonicalLinkSpanB),
                LayeredSpanScratchChannelPayload.Of(_canonicalLinkDirections),
                LayeredSpanScratchChannelPayload.Of(_canonicalLinkPortalMinAlongCm),
                LayeredSpanScratchChannelPayload.Of(_canonicalLinkPortalMaxAlongCm),
                LayeredSpanScratchChannelPayload.Of(_canonicalLinkAcceptedUnion),
                LayeredSpanScratchChannelPayload.Of(_sheetCanonicalLinkOffsets),
                LayeredSpanScratchChannelPayload.Of(_sheetCanonicalLinkIndices),
                LayeredSpanScratchChannelPayload.Of(_sheetCanonicalLinkCounts),
                LayeredSpanScratchChannelPayload.Of(_portalMinAlongCm),
                LayeredSpanScratchChannelPayload.Of(_portalMaxAlongCm),
                LayeredSpanScratchChannelPayload.Of(_edgeFromXcm),
                LayeredSpanScratchChannelPayload.Of(_edgeFromZcm),
                LayeredSpanScratchChannelPayload.Of(_edgeToXcm),
                LayeredSpanScratchChannelPayload.Of(_edgeToZcm),
                LayeredSpanScratchChannelPayload.Of(_edgeChartIds),
                LayeredSpanScratchChannelPayload.Of(_edgeSourceSpanIndices),
                LayeredSpanScratchChannelPayload.Of(_edgeFromMandatory),
                LayeredSpanScratchChannelPayload.Of(_edgeToMandatory),
                LayeredSpanScratchChannelPayload.Of(_edgeUsed),
                LayeredSpanScratchChannelPayload.Of(_splitAlongCm),
                LayeredSpanScratchChannelPayload.Of(_splitMandatory),
                LayeredSpanScratchChannelPayload.Of(_adjEdgeIndices),
                LayeredSpanScratchChannelPayload.Of(_adjOffsets),
                LayeredSpanScratchChannelPayload.Of(_vertexKeyXcm),
                LayeredSpanScratchChannelPayload.Of(_vertexKeyZcm),
                LayeredSpanScratchChannelPayload.Of(_vertexKeyFirstEdge),
                LayeredSpanScratchChannelPayload.Of(_vertexKeyNextEdge),
                LayeredSpanScratchChannelPayload.Of(_traceXcm),
                LayeredSpanScratchChannelPayload.Of(_traceZcm),
                LayeredSpanScratchChannelPayload.Of(_traceSourceSpan),
                LayeredSpanScratchChannelPayload.Of(_traceMandatory),
                LayeredSpanScratchChannelPayload.Of(_simplifyKeep),
                LayeredSpanScratchChannelPayload.Of(_ringOrder),
                LayeredSpanScratchChannelPayload.Of(_ringReorderSignedArea2));
        }

        public long PreallocatedChannelPayloadBytes { get; }

        public int ColumnCapacity { get; }
        public int SpanCapacity { get; }
        public int SheetCapacity { get; }
        public int ChartCapacity { get; }
        public int RingCapacity { get; }
        public int VertexCapacity { get; }
        public int EdgeCapacity { get; }
        public int SeamCapacity { get; }
        public int PortalIntervalCapacity { get; }
        public int CanonicalLinkCapacity { get; }
        public int SplitPointCapacity { get; }

        public int ChartCount => _chartCount;
        public int RingCount => _ringCount;
        public int VertexCount => _vertexCount;
        public int SeamCount => _seamCount;

        /// <summary>
        /// Monotonic content generation for this scratch instance.
        /// Zero when unpublished/invalid after construction, Reset, or failure.
        /// Provenance only; must not influence deterministic output bytes.
        /// </summary>
        public ulong ContentGeneration => _hasPublishedContent ? _contentGeneration : 0UL;

        public bool HasPublishedContent => _hasPublishedContent;

        public ReadOnlySpan<int> ChartMinSpanIndices => _chartMinSpanIndices.AsSpan(0, _chartCount);
        public ReadOnlySpan<int> ChartRegionIds => _chartRegionIds.AsSpan(0, _chartCount);
        public ReadOnlySpan<byte> ChartAreaIds => _chartAreaIds.AsSpan(0, _chartCount);
        public ReadOnlySpan<int> ChartRingOffsets => _chartRingOffsets.AsSpan(0, _chartCount + 1);

        public ReadOnlySpan<int> RingOffsets => _ringOffsets.AsSpan(0, _ringCount + 1);
        public ReadOnlySpan<int> RingChartIds => _ringChartIds.AsSpan(0, _ringCount);
        public ReadOnlySpan<int> RingRegionIds => _ringRegionIds.AsSpan(0, _ringCount);
        public ReadOnlySpan<byte> RingAreaIds => _ringAreaIds.AsSpan(0, _ringCount);
        public ReadOnlySpan<Int128> RingSignedArea2 => _ringSignedArea2.AsSpan(0, _ringCount);
        public ReadOnlySpan<LayeredSpanContourRingKind> RingKinds => _ringKinds.AsSpan(0, _ringCount);

        public ReadOnlySpan<int> VertexXcm => _vertexXcm.AsSpan(0, _vertexCount);
        public ReadOnlySpan<int> VertexZcm => _vertexZcm.AsSpan(0, _vertexCount);
        public ReadOnlySpan<int> VertexSourceSpanIndices => _vertexSourceSpanIndices.AsSpan(0, _vertexCount);
        public ReadOnlySpan<byte> VertexMandatory => _vertexMandatory.AsSpan(0, _vertexCount);

        public ReadOnlySpan<int> SeamChartA => _seamChartA.AsSpan(0, _seamCount);
        public ReadOnlySpan<int> SeamChartB => _seamChartB.AsSpan(0, _seamCount);
        public ReadOnlySpan<LayeredSpanNeighborDirection> SeamDirections => _seamDirections.AsSpan(0, _seamCount);
        public ReadOnlySpan<int> SeamPortalMinAlongCm => _seamPortalMinAlongCm.AsSpan(0, _seamCount);
        public ReadOnlySpan<int> SeamPortalMaxAlongCm => _seamPortalMaxAlongCm.AsSpan(0, _seamCount);
        public ReadOnlySpan<int> SeamSpanA => _seamSpanA.AsSpan(0, _seamCount);
        public ReadOnlySpan<int> SeamSpanB => _seamSpanB.AsSpan(0, _seamCount);

        internal void Reset()
        {
            InvalidatePublishedContent();
            _chartCount = 0;
            _ringCount = 0;
            _vertexCount = 0;
            _seamCount = 0;
            _edgeCount = 0;
            _canonicalLinkCount = 0;
            _vertexKeyCount = 0;
        }

        internal int EdgeCount => _edgeCount;
        internal int CanonicalLinkCount => _canonicalLinkCount;
        internal int VertexKeyCount => _vertexKeyCount;
        internal int ChartColumnMarkStamp => _chartColumnMarkStamp;

        internal Span<int> MutableChartMinSpanIndices => _chartMinSpanIndices.AsSpan(0, ChartCapacity);
        internal Span<int> MutableChartRegionIds => _chartRegionIds.AsSpan(0, ChartCapacity);
        internal Span<byte> MutableChartAreaIds => _chartAreaIds.AsSpan(0, ChartCapacity);
        internal Span<int> MutableChartRingOffsets => _chartRingOffsets.AsSpan(0, ChartCapacity + 1);

        internal Span<int> MutableRingOffsets => _ringOffsets.AsSpan(0, RingCapacity + 1);
        internal Span<int> MutableRingChartIds => _ringChartIds.AsSpan(0, RingCapacity);
        internal Span<int> MutableRingRegionIds => _ringRegionIds.AsSpan(0, RingCapacity);
        internal Span<byte> MutableRingAreaIds => _ringAreaIds.AsSpan(0, RingCapacity);
        internal Span<Int128> MutableRingSignedArea2 => _ringSignedArea2.AsSpan(0, RingCapacity);
        internal Span<LayeredSpanContourRingKind> MutableRingKinds => _ringKinds.AsSpan(0, RingCapacity);

        internal Span<int> MutableVertexXcm => _vertexXcm.AsSpan(0, VertexCapacity);
        internal Span<int> MutableVertexZcm => _vertexZcm.AsSpan(0, VertexCapacity);
        internal Span<int> MutableVertexSourceSpanIndices => _vertexSourceSpanIndices.AsSpan(0, VertexCapacity);
        internal Span<byte> MutableVertexMandatory => _vertexMandatory.AsSpan(0, VertexCapacity);

        internal Span<int> MutableSeamChartA => _seamChartA.AsSpan(0, SeamCapacity);
        internal Span<int> MutableSeamChartB => _seamChartB.AsSpan(0, SeamCapacity);
        internal Span<LayeredSpanNeighborDirection> MutableSeamDirections => _seamDirections.AsSpan(0, SeamCapacity);
        internal Span<int> MutableSeamPortalMinAlongCm => _seamPortalMinAlongCm.AsSpan(0, SeamCapacity);
        internal Span<int> MutableSeamPortalMaxAlongCm => _seamPortalMaxAlongCm.AsSpan(0, SeamCapacity);
        internal Span<int> MutableSeamSpanA => _seamSpanA.AsSpan(0, SeamCapacity);
        internal Span<int> MutableSeamSpanB => _seamSpanB.AsSpan(0, SeamCapacity);

        internal Span<int> MutableSpanToWalkableIndex => _spanToWalkableIndex.AsSpan(0, SpanCapacity);
        internal Span<int> MutableSheetColumn => _sheetColumn.AsSpan(0, SheetCapacity);
        internal Span<int> MutableSheetRegionIds => _sheetRegionIds.AsSpan(0, SheetCapacity);
        internal Span<byte> MutableSheetAreaIds => _sheetAreaIds.AsSpan(0, SheetCapacity);
        internal Span<int> MutableSheetMinSpanIndices => _sheetMinSpanIndices.AsSpan(0, SheetCapacity);
        internal Span<byte> MutableSheetEligible => _sheetEligible.AsSpan(0, SheetCapacity);
        internal Span<int> MutableFirstEligibleSpanBySheet => _firstEligibleSpanBySheet.AsSpan(0, SheetCapacity);
        internal Span<int> MutableNextEligibleSpanBySheet => _nextEligibleSpanBySheet.AsSpan(0, SpanCapacity);

        internal Span<int> MutableSheetChartUnionParent => _sheetChartUnionParent.AsSpan(0, SheetCapacity);
        internal Span<int> MutableSheetChartUnionRank => _sheetChartUnionRank.AsSpan(0, SheetCapacity);
        internal Span<int> MutableSheetChartComponentMinSpan => _sheetChartComponentMinSpan.AsSpan(0, SheetCapacity);
        internal Span<int> MutableSheetChartIdByRoot => _sheetChartIdByRoot.AsSpan(0, SheetCapacity);
        internal Span<int> MutableSheetToChart => _sheetToChart.AsSpan(0, SheetCapacity);
        internal Span<int> MutableChartColumnScratch => _chartColumnScratch.AsSpan(0, SheetCapacity);
        internal Span<int> MutableChartColumnMarks => _chartColumnMarks.AsSpan(0, SheetCapacity);

        internal Span<int> MutableColumnSheetFirst => _columnSheetFirst.AsSpan(0, ColumnCapacity);
        internal Span<int> MutableColumnSheetNext => _columnSheetNext.AsSpan(0, SheetCapacity);
        internal Span<int> MutableComponentMemberFirst => _componentMemberFirst.AsSpan(0, SheetCapacity);
        internal Span<int> MutableComponentMemberNext => _componentMemberNext.AsSpan(0, SheetCapacity);
        internal Span<int> MutableComponentMemberLast => _componentMemberLast.AsSpan(0, SheetCapacity);
        internal Span<int> MutableComponentSize => _componentSize.AsSpan(0, SheetCapacity);

        internal Span<int> MutableChartMinSpanBucketFirst => _chartMinSpanBucketFirst.AsSpan(0, SpanCapacity);
        internal Span<int> MutableChartMinSpanBucketNext => _chartMinSpanBucketNext.AsSpan(0, SheetCapacity);

        internal Span<int> MutableCanonicalLinkSheetA => _canonicalLinkSheetA.AsSpan(0, CanonicalLinkCapacity);
        internal Span<int> MutableCanonicalLinkSheetB => _canonicalLinkSheetB.AsSpan(0, CanonicalLinkCapacity);
        internal Span<int> MutableCanonicalLinkSpanA => _canonicalLinkSpanA.AsSpan(0, CanonicalLinkCapacity);
        internal Span<int> MutableCanonicalLinkSpanB => _canonicalLinkSpanB.AsSpan(0, CanonicalLinkCapacity);
        internal Span<LayeredSpanNeighborDirection> MutableCanonicalLinkDirections
            => _canonicalLinkDirections.AsSpan(0, CanonicalLinkCapacity);
        internal Span<int> MutableCanonicalLinkPortalMinAlongCm
            => _canonicalLinkPortalMinAlongCm.AsSpan(0, CanonicalLinkCapacity);
        internal Span<int> MutableCanonicalLinkPortalMaxAlongCm
            => _canonicalLinkPortalMaxAlongCm.AsSpan(0, CanonicalLinkCapacity);
        internal Span<byte> MutableCanonicalLinkAcceptedUnion => _canonicalLinkAcceptedUnion.AsSpan(0, CanonicalLinkCapacity);
        internal Span<int> MutableSheetCanonicalLinkOffsets => _sheetCanonicalLinkOffsets.AsSpan(0, SheetCapacity + 1);
        internal Span<int> MutableSheetCanonicalLinkIndices => _sheetCanonicalLinkIndices.AsSpan(0, CanonicalLinkCapacity * 2);
        internal Span<int> MutableSheetCanonicalLinkCounts => _sheetCanonicalLinkCounts.AsSpan(0, SheetCapacity);

        internal Span<int> MutablePortalMinAlongCm => _portalMinAlongCm.AsSpan(0, PortalIntervalCapacity);
        internal Span<int> MutablePortalMaxAlongCm => _portalMaxAlongCm.AsSpan(0, PortalIntervalCapacity);

        internal Span<int> MutableEdgeFromXcm => _edgeFromXcm.AsSpan(0, EdgeCapacity);
        internal Span<int> MutableEdgeFromZcm => _edgeFromZcm.AsSpan(0, EdgeCapacity);
        internal Span<int> MutableEdgeToXcm => _edgeToXcm.AsSpan(0, EdgeCapacity);
        internal Span<int> MutableEdgeToZcm => _edgeToZcm.AsSpan(0, EdgeCapacity);
        internal Span<int> MutableEdgeChartIds => _edgeChartIds.AsSpan(0, EdgeCapacity);
        internal Span<int> MutableEdgeSourceSpanIndices => _edgeSourceSpanIndices.AsSpan(0, EdgeCapacity);
        internal Span<byte> MutableEdgeFromMandatory => _edgeFromMandatory.AsSpan(0, EdgeCapacity);
        internal Span<byte> MutableEdgeToMandatory => _edgeToMandatory.AsSpan(0, EdgeCapacity);
        internal Span<byte> MutableEdgeUsed => _edgeUsed.AsSpan(0, EdgeCapacity);

        internal Span<int> MutableSplitAlongCm => _splitAlongCm.AsSpan(0, SplitPointCapacity);
        internal Span<byte> MutableSplitMandatory => _splitMandatory.AsSpan(0, SplitPointCapacity);

        internal Span<int> MutableAdjEdgeIndices => _adjEdgeIndices.AsSpan(0, EdgeCapacity);
        internal Span<int> MutableAdjOffsets => _adjOffsets.AsSpan(0, EdgeCapacity + 1);
        internal Span<int> MutableVertexKeyXcm => _vertexKeyXcm.AsSpan(0, EdgeCapacity);
        internal Span<int> MutableVertexKeyZcm => _vertexKeyZcm.AsSpan(0, EdgeCapacity);
        internal Span<int> MutableVertexKeyFirstEdge => _vertexKeyFirstEdge.AsSpan(0, EdgeCapacity);
        internal Span<int> MutableVertexKeyNextEdge => _vertexKeyNextEdge.AsSpan(0, EdgeCapacity);

        internal Span<int> MutableTraceXcm => _traceXcm.AsSpan(0, VertexCapacity);
        internal Span<int> MutableTraceZcm => _traceZcm.AsSpan(0, VertexCapacity);
        internal Span<int> MutableTraceSourceSpan => _traceSourceSpan.AsSpan(0, VertexCapacity);
        internal Span<byte> MutableTraceMandatory => _traceMandatory.AsSpan(0, VertexCapacity);

        internal Span<int> MutableSimplifyKeep => _simplifyKeep.AsSpan(0, VertexCapacity);
        internal Span<int> MutableRingOrder => _ringOrder.AsSpan(0, RingCapacity);
        internal Span<Int128> MutableRingReorderSignedArea2 => _ringReorderSignedArea2.AsSpan(0, RingCapacity);

        internal void Prepare()
        {
            InvalidatePublishedContent();
            _chartCount = 0;
            _ringCount = 0;
            _vertexCount = 0;
            _seamCount = 0;
            _edgeCount = 0;
            _canonicalLinkCount = 0;
            _vertexKeyCount = 0;
            _chartColumnMarkStamp = 0;
        }

        internal void SetChartCount(int chartCount) => _chartCount = chartCount;
        internal void SetRingCount(int ringCount) => _ringCount = ringCount;
        internal void SetVertexCount(int vertexCount) => _vertexCount = vertexCount;
        internal void SetSeamCount(int seamCount) => _seamCount = seamCount;
        internal void SetEdgeCount(int edgeCount) => _edgeCount = edgeCount;
        internal void SetCanonicalLinkCount(int count) => _canonicalLinkCount = count;
        internal void SetVertexKeyCount(int count) => _vertexKeyCount = count;

        internal void BumpChartColumnMarkStamp()
        {
            if (_chartColumnMarkStamp == int.MaxValue)
            {
                _chartColumnMarks.AsSpan(0, SheetCapacity).Clear();
                _chartColumnMarkStamp = 1;
            }
            else
            {
                _chartColumnMarkStamp++;
            }
        }

        internal void Commit(
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walkability,
            LayeredSpanSurfaceSheetScratch sheets,
            LayeredSpanWalkLinkScratch links,
            LayeredSpanRadiusFieldScratch radius,
            LayeredSpanRegionScratch regions)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (walkability == null) throw new ArgumentNullException(nameof(walkability));
            if (sheets == null) throw new ArgumentNullException(nameof(sheets));
            if (links == null) throw new ArgumentNullException(nameof(links));
            if (radius == null) throw new ArgumentNullException(nameof(radius));
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            if (!raw.HasPublishedContent ||
                !walkability.HasPublishedContent ||
                !sheets.HasPublishedContent ||
                !links.HasPublishedContent ||
                !radius.HasPublishedContent ||
                !regions.HasPublishedContent)
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanContourScratch commit requires published raw/walkability/sheets/links/radius/regions content.");
            }

            if (!walkability.WasBuiltFrom(raw) ||
                !sheets.WasBuiltFrom(raw) ||
                !links.WasBuiltFrom(raw, walkability) ||
                !radius.WasBuiltFrom(raw, walkability, sheets, links) ||
                !regions.WasBuiltFrom(raw, walkability, sheets, links, radius))
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanContourScratch commit requires all inputs published from the same scratch identity and content generation chain.");
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
            _sourceRegions = regions;
            _sourceRegionsContentGeneration = regions.ContentGeneration;
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
            LayeredSpanRadiusFieldScratch radius,
            LayeredSpanRegionScratch regions)
        {
            if (!_hasPublishedContent ||
                raw == null ||
                walkability == null ||
                sheets == null ||
                links == null ||
                radius == null ||
                regions == null ||
                !raw.HasPublishedContent ||
                !walkability.HasPublishedContent ||
                !sheets.HasPublishedContent ||
                !links.HasPublishedContent ||
                !radius.HasPublishedContent ||
                !regions.HasPublishedContent)
            {
                return false;
            }

            return ReferenceEquals(_sourceRaw, raw) &&
                   ReferenceEquals(_sourceWalkability, walkability) &&
                   ReferenceEquals(_sourceSheets, sheets) &&
                   ReferenceEquals(_sourceLinks, links) &&
                   ReferenceEquals(_sourceRadius, radius) &&
                   ReferenceEquals(_sourceRegions, regions) &&
                   _sourceRawContentGeneration == raw.ContentGeneration &&
                   _sourceWalkabilityContentGeneration == walkability.ContentGeneration &&
                   _sourceSheetsContentGeneration == sheets.ContentGeneration &&
                   _sourceLinksContentGeneration == links.ContentGeneration &&
                   _sourceRadiusContentGeneration == radius.ContentGeneration &&
                   _sourceRegionsContentGeneration == regions.ContentGeneration;
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
            _sourceRegions = null;
            _sourceRegionsContentGeneration = 0;
        }

        private void PublishNewContentGeneration()
        {
            if (_contentGeneration == ulong.MaxValue)
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanContourScratch.contentGeneration overflow; recreate the scratch instance.");
            }

            _contentGeneration++;
            _hasPublishedContent = true;
        }

        private static int[] EmptyInts(int n) => n == 0 ? Array.Empty<int>() : new int[n];
        private static byte[] EmptyBytes(int n) => n == 0 ? Array.Empty<byte>() : new byte[n];
        private static Int128[] EmptyInt128s(int n) => n == 0 ? Array.Empty<Int128>() : new Int128[n];
    }
}
