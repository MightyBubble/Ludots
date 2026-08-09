using System;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Surface;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Fixed-capacity SoA scratch for layered-span constrained triangulation output and builder work.
    /// Constructor owns every public and builder-private channel; warmed Build reuses them.
    /// </summary>
    public sealed class LayeredSpanTriangulationScratch
    {
        // ---- Published channels ----
        private readonly int[] _vertexXcm;
        private readonly int[] _vertexYcm;
        private readonly int[] _vertexZcm;
        private readonly int[] _vertexChartIds;
        private readonly int[] _vertexSourceSpanIndices;

        private readonly int[] _triA;
        private readonly int[] _triB;
        private readonly int[] _triC;
        private readonly int[] _triChartIds;
        private readonly int[] _triRegionIds;
        private readonly byte[] _triAreaIds;
        private readonly int[] _n0;
        private readonly int[] _n1;
        private readonly int[] _n2;

        private readonly int[] _constrainedEdgeA;
        private readonly int[] _constrainedEdgeB;
        private readonly byte[] _constrainedEdgeFlags;

        private readonly NavPortalSide[] _portalSides;
        private readonly short[] _portalU0;
        private readonly short[] _portalV0;
        private readonly short[] _portalU1;
        private readonly short[] _portalV1;
        private readonly int[] _portalLeftXcm;
        private readonly int[] _portalLeftYcm;
        private readonly int[] _portalLeftZcm;
        private readonly int[] _portalRightXcm;
        private readonly int[] _portalRightYcm;
        private readonly int[] _portalRightZcm;
        private readonly int[] _portalClearanceCm;
        private readonly int[] _portalSourceSpanIndices;
        private readonly int[] _portalNeighborSpanIndices;

        // ---- Builder private ----
        private readonly int[] _polyXcm;
        private readonly int[] _polyZcm;
        private readonly int[] _polySourceSpan;
        private readonly int[] _polyContourVertex;
        private readonly int[] _polyNext;
        private readonly int[] _polyPrev;
        private readonly byte[] _polyActive;
        private readonly byte[] _polyFromHole;

        private readonly int[] _bridgeHoleVertex;
        private readonly int[] _bridgeOuterVertex;
        private readonly long[] _bridgeDist2;

        private readonly int[] _edgeA;
        private readonly int[] _edgeB;
        private readonly int[] _edgeTri;
        private readonly int[] _edgeOpp;
        private readonly byte[] _edgeConstrained;
        private readonly int[] _edgeOrder;

        private readonly int[] _uniqueKeyX;
        private readonly int[] _uniqueKeyY;
        private readonly int[] _uniqueKeyZ;
        private readonly int[] _uniqueKeyChart;
        private readonly int[] _uniqueKeyIndex;

        private readonly int[] _contourLocalToPoly;
        private readonly int[] _constraintMarkA;
        private readonly int[] _constraintMarkB;
        private readonly byte[] _tempConstraintFlags;

        private readonly int[] _ringWorkOrder;
        private readonly int[] _ringOwnerOuter;

        private int _vertexCount;
        private int _triangleCount;
        private int _constrainedEdgeCount;
        private int _portalCount;
        private int _polyCount;

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
        private LayeredSpanContourScratch? _sourceContours;
        private ulong _sourceContoursContentGeneration;
        private NavTriangleSurfaceSnapshot? _sourceSurface;

        public LayeredSpanTriangulationScratch(
            int vertexCapacity,
            int triangleCapacity,
            int constrainedEdgeCapacity,
            int borderPortalCapacity,
            int polygonVertexCapacity,
            int adjacencyEdgeCapacity,
            int bridgeCandidateCapacity,
            int ringWorkCapacity,
            int temporaryConstraintFlagCapacity)
        {
            if (vertexCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexCapacity), vertexCapacity, "vertexCapacity must be nonnegative.");
            }

            if (triangleCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(triangleCapacity), triangleCapacity, "triangleCapacity must be nonnegative.");
            }

            if (constrainedEdgeCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(constrainedEdgeCapacity),
                    constrainedEdgeCapacity,
                    "constrainedEdgeCapacity must be nonnegative.");
            }

            if (borderPortalCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(borderPortalCapacity),
                    borderPortalCapacity,
                    "borderPortalCapacity must be nonnegative.");
            }

            if (polygonVertexCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(polygonVertexCapacity),
                    polygonVertexCapacity,
                    "polygonVertexCapacity must be nonnegative.");
            }

            if (adjacencyEdgeCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(adjacencyEdgeCapacity),
                    adjacencyEdgeCapacity,
                    "adjacencyEdgeCapacity must be nonnegative.");
            }

            if (bridgeCandidateCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bridgeCandidateCapacity),
                    bridgeCandidateCapacity,
                    "bridgeCandidateCapacity must be nonnegative.");
            }

            if (ringWorkCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ringWorkCapacity),
                    ringWorkCapacity,
                    "ringWorkCapacity must be nonnegative.");
            }

            if (temporaryConstraintFlagCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(temporaryConstraintFlagCapacity),
                    temporaryConstraintFlagCapacity,
                    "temporaryConstraintFlagCapacity must be nonnegative.");
            }

            VertexCapacity = vertexCapacity;
            TriangleCapacity = triangleCapacity;
            ConstrainedEdgeCapacity = constrainedEdgeCapacity;
            BorderPortalCapacity = borderPortalCapacity;
            PolygonVertexCapacity = polygonVertexCapacity;
            AdjacencyEdgeCapacity = adjacencyEdgeCapacity;
            BridgeCandidateCapacity = bridgeCandidateCapacity;
            RingWorkCapacity = ringWorkCapacity;
            TemporaryConstraintFlagCapacity = temporaryConstraintFlagCapacity;

            _vertexXcm = EmptyInts(vertexCapacity);
            _vertexYcm = EmptyInts(vertexCapacity);
            _vertexZcm = EmptyInts(vertexCapacity);
            _vertexChartIds = EmptyInts(vertexCapacity);
            _vertexSourceSpanIndices = EmptyInts(vertexCapacity);

            _triA = EmptyInts(triangleCapacity);
            _triB = EmptyInts(triangleCapacity);
            _triC = EmptyInts(triangleCapacity);
            _triChartIds = EmptyInts(triangleCapacity);
            _triRegionIds = EmptyInts(triangleCapacity);
            _triAreaIds = EmptyBytes(triangleCapacity);
            _n0 = EmptyInts(triangleCapacity);
            _n1 = EmptyInts(triangleCapacity);
            _n2 = EmptyInts(triangleCapacity);

            _constrainedEdgeA = EmptyInts(constrainedEdgeCapacity);
            _constrainedEdgeB = EmptyInts(constrainedEdgeCapacity);
            _constrainedEdgeFlags = EmptyBytes(constrainedEdgeCapacity);

            _portalSides = borderPortalCapacity == 0
                ? Array.Empty<NavPortalSide>()
                : new NavPortalSide[borderPortalCapacity];
            _portalU0 = EmptyShorts(borderPortalCapacity);
            _portalV0 = EmptyShorts(borderPortalCapacity);
            _portalU1 = EmptyShorts(borderPortalCapacity);
            _portalV1 = EmptyShorts(borderPortalCapacity);
            _portalLeftXcm = EmptyInts(borderPortalCapacity);
            _portalLeftYcm = EmptyInts(borderPortalCapacity);
            _portalLeftZcm = EmptyInts(borderPortalCapacity);
            _portalRightXcm = EmptyInts(borderPortalCapacity);
            _portalRightYcm = EmptyInts(borderPortalCapacity);
            _portalRightZcm = EmptyInts(borderPortalCapacity);
            _portalClearanceCm = EmptyInts(borderPortalCapacity);
            _portalSourceSpanIndices = EmptyInts(borderPortalCapacity);
            _portalNeighborSpanIndices = EmptyInts(borderPortalCapacity);

            _polyXcm = EmptyInts(polygonVertexCapacity);
            _polyZcm = EmptyInts(polygonVertexCapacity);
            _polySourceSpan = EmptyInts(polygonVertexCapacity);
            _polyContourVertex = EmptyInts(polygonVertexCapacity);
            _polyNext = EmptyInts(polygonVertexCapacity);
            _polyPrev = EmptyInts(polygonVertexCapacity);
            _polyActive = EmptyBytes(polygonVertexCapacity);
            _polyFromHole = EmptyBytes(polygonVertexCapacity);

            _bridgeHoleVertex = EmptyInts(bridgeCandidateCapacity);
            _bridgeOuterVertex = EmptyInts(bridgeCandidateCapacity);
            _bridgeDist2 = bridgeCandidateCapacity == 0 ? Array.Empty<long>() : new long[bridgeCandidateCapacity];

            _edgeA = EmptyInts(adjacencyEdgeCapacity);
            _edgeB = EmptyInts(adjacencyEdgeCapacity);
            _edgeTri = EmptyInts(adjacencyEdgeCapacity);
            _edgeOpp = EmptyInts(adjacencyEdgeCapacity);
            _edgeConstrained = EmptyBytes(adjacencyEdgeCapacity);
            _edgeOrder = EmptyInts(adjacencyEdgeCapacity);

            _uniqueKeyX = EmptyInts(vertexCapacity);
            _uniqueKeyY = EmptyInts(vertexCapacity);
            _uniqueKeyZ = EmptyInts(vertexCapacity);
            _uniqueKeyChart = EmptyInts(vertexCapacity);
            _uniqueKeyIndex = EmptyInts(vertexCapacity);

            _contourLocalToPoly = EmptyInts(polygonVertexCapacity);
            _constraintMarkA = EmptyInts(constrainedEdgeCapacity);
            _constraintMarkB = EmptyInts(constrainedEdgeCapacity);
            _tempConstraintFlags = EmptyBytes(temporaryConstraintFlagCapacity);

            _ringWorkOrder = EmptyInts(ringWorkCapacity);
            _ringOwnerOuter = EmptyInts(ringWorkCapacity);

            PreallocatedChannelPayloadBytes = LayeredSpanScratchChannelPayload.Sum(
                LayeredSpanScratchChannelPayload.Of(_vertexXcm),
                LayeredSpanScratchChannelPayload.Of(_vertexYcm),
                LayeredSpanScratchChannelPayload.Of(_vertexZcm),
                LayeredSpanScratchChannelPayload.Of(_vertexChartIds),
                LayeredSpanScratchChannelPayload.Of(_vertexSourceSpanIndices),
                LayeredSpanScratchChannelPayload.Of(_triA),
                LayeredSpanScratchChannelPayload.Of(_triB),
                LayeredSpanScratchChannelPayload.Of(_triC),
                LayeredSpanScratchChannelPayload.Of(_triChartIds),
                LayeredSpanScratchChannelPayload.Of(_triRegionIds),
                LayeredSpanScratchChannelPayload.Of(_triAreaIds),
                LayeredSpanScratchChannelPayload.Of(_n0),
                LayeredSpanScratchChannelPayload.Of(_n1),
                LayeredSpanScratchChannelPayload.Of(_n2),
                LayeredSpanScratchChannelPayload.Of(_constrainedEdgeA),
                LayeredSpanScratchChannelPayload.Of(_constrainedEdgeB),
                LayeredSpanScratchChannelPayload.Of(_constrainedEdgeFlags),
                LayeredSpanScratchChannelPayload.Of(_portalSides),
                LayeredSpanScratchChannelPayload.Of(_portalU0),
                LayeredSpanScratchChannelPayload.Of(_portalV0),
                LayeredSpanScratchChannelPayload.Of(_portalU1),
                LayeredSpanScratchChannelPayload.Of(_portalV1),
                LayeredSpanScratchChannelPayload.Of(_portalLeftXcm),
                LayeredSpanScratchChannelPayload.Of(_portalLeftYcm),
                LayeredSpanScratchChannelPayload.Of(_portalLeftZcm),
                LayeredSpanScratchChannelPayload.Of(_portalRightXcm),
                LayeredSpanScratchChannelPayload.Of(_portalRightYcm),
                LayeredSpanScratchChannelPayload.Of(_portalRightZcm),
                LayeredSpanScratchChannelPayload.Of(_portalClearanceCm),
                LayeredSpanScratchChannelPayload.Of(_portalSourceSpanIndices),
                LayeredSpanScratchChannelPayload.Of(_portalNeighborSpanIndices),
                LayeredSpanScratchChannelPayload.Of(_polyXcm),
                LayeredSpanScratchChannelPayload.Of(_polyZcm),
                LayeredSpanScratchChannelPayload.Of(_polySourceSpan),
                LayeredSpanScratchChannelPayload.Of(_polyContourVertex),
                LayeredSpanScratchChannelPayload.Of(_polyNext),
                LayeredSpanScratchChannelPayload.Of(_polyPrev),
                LayeredSpanScratchChannelPayload.Of(_polyActive),
                LayeredSpanScratchChannelPayload.Of(_polyFromHole),
                LayeredSpanScratchChannelPayload.Of(_bridgeHoleVertex),
                LayeredSpanScratchChannelPayload.Of(_bridgeOuterVertex),
                LayeredSpanScratchChannelPayload.Of(_bridgeDist2),
                LayeredSpanScratchChannelPayload.Of(_edgeA),
                LayeredSpanScratchChannelPayload.Of(_edgeB),
                LayeredSpanScratchChannelPayload.Of(_edgeTri),
                LayeredSpanScratchChannelPayload.Of(_edgeOpp),
                LayeredSpanScratchChannelPayload.Of(_edgeConstrained),
                LayeredSpanScratchChannelPayload.Of(_edgeOrder),
                LayeredSpanScratchChannelPayload.Of(_uniqueKeyX),
                LayeredSpanScratchChannelPayload.Of(_uniqueKeyY),
                LayeredSpanScratchChannelPayload.Of(_uniqueKeyZ),
                LayeredSpanScratchChannelPayload.Of(_uniqueKeyChart),
                LayeredSpanScratchChannelPayload.Of(_uniqueKeyIndex),
                LayeredSpanScratchChannelPayload.Of(_contourLocalToPoly),
                LayeredSpanScratchChannelPayload.Of(_constraintMarkA),
                LayeredSpanScratchChannelPayload.Of(_constraintMarkB),
                LayeredSpanScratchChannelPayload.Of(_tempConstraintFlags),
                LayeredSpanScratchChannelPayload.Of(_ringWorkOrder),
                LayeredSpanScratchChannelPayload.Of(_ringOwnerOuter));
        }

        public long PreallocatedChannelPayloadBytes { get; }

        public int VertexCapacity { get; }
        public int TriangleCapacity { get; }
        public int ConstrainedEdgeCapacity { get; }
        public int BorderPortalCapacity { get; }
        public int PolygonVertexCapacity { get; }
        public int AdjacencyEdgeCapacity { get; }
        public int BridgeCandidateCapacity { get; }
        public int RingWorkCapacity { get; }
        public int TemporaryConstraintFlagCapacity { get; }

        public int VertexCount => _vertexCount;
        public int TriangleCount => _triangleCount;
        public int ConstrainedEdgeCount => _constrainedEdgeCount;
        public int PortalCount => _portalCount;

        /// <summary>
        /// Monotonic content generation for this scratch instance.
        /// Zero when unpublished/invalid after construction, Reset, or failure.
        /// Provenance only; must not influence deterministic output bytes.
        /// </summary>
        public ulong ContentGeneration => _hasPublishedContent ? _contentGeneration : 0UL;

        public bool HasPublishedContent => _hasPublishedContent;

        public ReadOnlySpan<int> VertexXcm => _vertexXcm.AsSpan(0, _vertexCount);
        public ReadOnlySpan<int> VertexYcm => _vertexYcm.AsSpan(0, _vertexCount);
        public ReadOnlySpan<int> VertexZcm => _vertexZcm.AsSpan(0, _vertexCount);
        public ReadOnlySpan<int> VertexChartIds => _vertexChartIds.AsSpan(0, _vertexCount);
        public ReadOnlySpan<int> VertexSourceSpanIndices => _vertexSourceSpanIndices.AsSpan(0, _vertexCount);

        public ReadOnlySpan<int> TriA => _triA.AsSpan(0, _triangleCount);
        public ReadOnlySpan<int> TriB => _triB.AsSpan(0, _triangleCount);
        public ReadOnlySpan<int> TriC => _triC.AsSpan(0, _triangleCount);
        public ReadOnlySpan<int> TriChartIds => _triChartIds.AsSpan(0, _triangleCount);
        public ReadOnlySpan<int> TriRegionIds => _triRegionIds.AsSpan(0, _triangleCount);
        public ReadOnlySpan<byte> TriAreaIds => _triAreaIds.AsSpan(0, _triangleCount);
        public ReadOnlySpan<int> N0 => _n0.AsSpan(0, _triangleCount);
        public ReadOnlySpan<int> N1 => _n1.AsSpan(0, _triangleCount);
        public ReadOnlySpan<int> N2 => _n2.AsSpan(0, _triangleCount);

        public ReadOnlySpan<int> ConstrainedEdgeA => _constrainedEdgeA.AsSpan(0, _constrainedEdgeCount);
        public ReadOnlySpan<int> ConstrainedEdgeB => _constrainedEdgeB.AsSpan(0, _constrainedEdgeCount);
        public ReadOnlySpan<byte> ConstrainedEdgeFlags => _constrainedEdgeFlags.AsSpan(0, _constrainedEdgeCount);

        public ReadOnlySpan<NavPortalSide> PortalSides => _portalSides.AsSpan(0, _portalCount);
        public ReadOnlySpan<short> PortalU0 => _portalU0.AsSpan(0, _portalCount);
        public ReadOnlySpan<short> PortalV0 => _portalV0.AsSpan(0, _portalCount);
        public ReadOnlySpan<short> PortalU1 => _portalU1.AsSpan(0, _portalCount);
        public ReadOnlySpan<short> PortalV1 => _portalV1.AsSpan(0, _portalCount);
        public ReadOnlySpan<int> PortalLeftXcm => _portalLeftXcm.AsSpan(0, _portalCount);
        public ReadOnlySpan<int> PortalLeftYcm => _portalLeftYcm.AsSpan(0, _portalCount);
        public ReadOnlySpan<int> PortalLeftZcm => _portalLeftZcm.AsSpan(0, _portalCount);
        public ReadOnlySpan<int> PortalRightXcm => _portalRightXcm.AsSpan(0, _portalCount);
        public ReadOnlySpan<int> PortalRightYcm => _portalRightYcm.AsSpan(0, _portalCount);
        public ReadOnlySpan<int> PortalRightZcm => _portalRightZcm.AsSpan(0, _portalCount);
        public ReadOnlySpan<int> PortalClearanceCm => _portalClearanceCm.AsSpan(0, _portalCount);
        public ReadOnlySpan<int> PortalSourceSpanIndices => _portalSourceSpanIndices.AsSpan(0, _portalCount);
        public ReadOnlySpan<int> PortalNeighborSpanIndices => _portalNeighborSpanIndices.AsSpan(0, _portalCount);

        internal void Reset()
        {
            InvalidatePublishedContent();
            ClearCounts();
        }

        internal void Prepare()
        {
            InvalidatePublishedContent();
            ClearCounts();
        }

        internal Span<int> MutableVertexXcm => _vertexXcm.AsSpan(0, VertexCapacity);
        internal Span<int> MutableVertexYcm => _vertexYcm.AsSpan(0, VertexCapacity);
        internal Span<int> MutableVertexZcm => _vertexZcm.AsSpan(0, VertexCapacity);
        internal Span<int> MutableVertexChartIds => _vertexChartIds.AsSpan(0, VertexCapacity);
        internal Span<int> MutableVertexSourceSpanIndices => _vertexSourceSpanIndices.AsSpan(0, VertexCapacity);

        internal Span<int> MutableTriA => _triA.AsSpan(0, TriangleCapacity);
        internal Span<int> MutableTriB => _triB.AsSpan(0, TriangleCapacity);
        internal Span<int> MutableTriC => _triC.AsSpan(0, TriangleCapacity);
        internal Span<int> MutableTriChartIds => _triChartIds.AsSpan(0, TriangleCapacity);
        internal Span<int> MutableTriRegionIds => _triRegionIds.AsSpan(0, TriangleCapacity);
        internal Span<byte> MutableTriAreaIds => _triAreaIds.AsSpan(0, TriangleCapacity);
        internal Span<int> MutableN0 => _n0.AsSpan(0, TriangleCapacity);
        internal Span<int> MutableN1 => _n1.AsSpan(0, TriangleCapacity);
        internal Span<int> MutableN2 => _n2.AsSpan(0, TriangleCapacity);

        internal Span<int> MutableConstrainedEdgeA => _constrainedEdgeA.AsSpan(0, ConstrainedEdgeCapacity);
        internal Span<int> MutableConstrainedEdgeB => _constrainedEdgeB.AsSpan(0, ConstrainedEdgeCapacity);
        internal Span<byte> MutableConstrainedEdgeFlags => _constrainedEdgeFlags.AsSpan(0, ConstrainedEdgeCapacity);

        internal Span<NavPortalSide> MutablePortalSides => _portalSides.AsSpan(0, BorderPortalCapacity);
        internal Span<short> MutablePortalU0 => _portalU0.AsSpan(0, BorderPortalCapacity);
        internal Span<short> MutablePortalV0 => _portalV0.AsSpan(0, BorderPortalCapacity);
        internal Span<short> MutablePortalU1 => _portalU1.AsSpan(0, BorderPortalCapacity);
        internal Span<short> MutablePortalV1 => _portalV1.AsSpan(0, BorderPortalCapacity);
        internal Span<int> MutablePortalLeftXcm => _portalLeftXcm.AsSpan(0, BorderPortalCapacity);
        internal Span<int> MutablePortalLeftYcm => _portalLeftYcm.AsSpan(0, BorderPortalCapacity);
        internal Span<int> MutablePortalLeftZcm => _portalLeftZcm.AsSpan(0, BorderPortalCapacity);
        internal Span<int> MutablePortalRightXcm => _portalRightXcm.AsSpan(0, BorderPortalCapacity);
        internal Span<int> MutablePortalRightYcm => _portalRightYcm.AsSpan(0, BorderPortalCapacity);
        internal Span<int> MutablePortalRightZcm => _portalRightZcm.AsSpan(0, BorderPortalCapacity);
        internal Span<int> MutablePortalClearanceCm => _portalClearanceCm.AsSpan(0, BorderPortalCapacity);
        internal Span<int> MutablePortalSourceSpanIndices => _portalSourceSpanIndices.AsSpan(0, BorderPortalCapacity);
        internal Span<int> MutablePortalNeighborSpanIndices => _portalNeighborSpanIndices.AsSpan(0, BorderPortalCapacity);

        internal Span<int> MutablePolyXcm => _polyXcm.AsSpan(0, PolygonVertexCapacity);
        internal Span<int> MutablePolyZcm => _polyZcm.AsSpan(0, PolygonVertexCapacity);
        internal Span<int> MutablePolySourceSpan => _polySourceSpan.AsSpan(0, PolygonVertexCapacity);
        internal Span<int> MutablePolyContourVertex => _polyContourVertex.AsSpan(0, PolygonVertexCapacity);
        internal Span<int> MutablePolyNext => _polyNext.AsSpan(0, PolygonVertexCapacity);
        internal Span<int> MutablePolyPrev => _polyPrev.AsSpan(0, PolygonVertexCapacity);
        internal Span<byte> MutablePolyActive => _polyActive.AsSpan(0, PolygonVertexCapacity);
        internal Span<byte> MutablePolyFromHole => _polyFromHole.AsSpan(0, PolygonVertexCapacity);

        internal Span<int> MutableBridgeHoleVertex => _bridgeHoleVertex.AsSpan(0, BridgeCandidateCapacity);
        internal Span<int> MutableBridgeOuterVertex => _bridgeOuterVertex.AsSpan(0, BridgeCandidateCapacity);
        internal Span<long> MutableBridgeDist2 => _bridgeDist2.AsSpan(0, BridgeCandidateCapacity);

        internal Span<int> MutableEdgeA => _edgeA.AsSpan(0, AdjacencyEdgeCapacity);
        internal Span<int> MutableEdgeB => _edgeB.AsSpan(0, AdjacencyEdgeCapacity);
        internal Span<int> MutableEdgeTri => _edgeTri.AsSpan(0, AdjacencyEdgeCapacity);
        internal Span<int> MutableEdgeOpp => _edgeOpp.AsSpan(0, AdjacencyEdgeCapacity);
        internal Span<byte> MutableEdgeConstrained => _edgeConstrained.AsSpan(0, AdjacencyEdgeCapacity);
        internal Span<int> MutableEdgeOrder => _edgeOrder.AsSpan(0, AdjacencyEdgeCapacity);

        internal Span<int> MutableUniqueKeyX => _uniqueKeyX.AsSpan(0, VertexCapacity);
        internal Span<int> MutableUniqueKeyY => _uniqueKeyY.AsSpan(0, VertexCapacity);
        internal Span<int> MutableUniqueKeyZ => _uniqueKeyZ.AsSpan(0, VertexCapacity);
        internal Span<int> MutableUniqueKeyChart => _uniqueKeyChart.AsSpan(0, VertexCapacity);
        internal Span<int> MutableUniqueKeyIndex => _uniqueKeyIndex.AsSpan(0, VertexCapacity);

        internal Span<int> MutableContourLocalToPoly => _contourLocalToPoly.AsSpan(0, PolygonVertexCapacity);
        internal Span<int> MutableConstraintMarkA => _constraintMarkA.AsSpan(0, ConstrainedEdgeCapacity);
        internal Span<int> MutableConstraintMarkB => _constraintMarkB.AsSpan(0, ConstrainedEdgeCapacity);
        internal Span<byte> MutableTempConstraintFlags => _tempConstraintFlags.AsSpan(0, TemporaryConstraintFlagCapacity);

        internal Span<int> MutableRingWorkOrder => _ringWorkOrder.AsSpan(0, RingWorkCapacity);
        internal Span<int> MutableRingOwnerOuter => _ringOwnerOuter.AsSpan(0, RingWorkCapacity);

        internal int PolyCount => _polyCount;

        internal void SetVertexCount(int count) => _vertexCount = count;
        internal void SetTriangleCount(int count) => _triangleCount = count;
        internal void SetConstrainedEdgeCount(int count) => _constrainedEdgeCount = count;
        internal void SetPortalCount(int count) => _portalCount = count;
        internal void SetPolyCount(int count) => _polyCount = count;

        internal void Commit(
            LayeredSpanScratch raw,
            LayeredSpanWalkabilityScratch walkability,
            LayeredSpanSurfaceSheetScratch sheets,
            LayeredSpanWalkLinkScratch links,
            LayeredSpanRadiusFieldScratch radius,
            LayeredSpanRegionScratch regions,
            LayeredSpanContourScratch contours,
            NavTriangleSurfaceSnapshot surface)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (walkability == null) throw new ArgumentNullException(nameof(walkability));
            if (sheets == null) throw new ArgumentNullException(nameof(sheets));
            if (links == null) throw new ArgumentNullException(nameof(links));
            if (radius == null) throw new ArgumentNullException(nameof(radius));
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            if (contours == null) throw new ArgumentNullException(nameof(contours));
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            if (!raw.HasPublishedContent ||
                !walkability.HasPublishedContent ||
                !sheets.HasPublishedContent ||
                !links.HasPublishedContent ||
                !radius.HasPublishedContent ||
                !regions.HasPublishedContent ||
                !contours.HasPublishedContent)
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanTriangulationScratch commit requires published raw/walkability/sheets/links/radius/regions/contour content.");
            }

            if (!walkability.WasBuiltFrom(raw) ||
                !sheets.WasBuiltFrom(raw) ||
                !links.WasBuiltFrom(raw, walkability) ||
                !radius.WasBuiltFrom(raw, walkability, sheets, links) ||
                !regions.WasBuiltFrom(raw, walkability, sheets, links, radius) ||
                !contours.WasBuiltFrom(raw, walkability, sheets, links, radius, regions))
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanTriangulationScratch commit requires all inputs published from the same scratch identity and content generation chain.");
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
            _sourceContours = contours;
            _sourceContoursContentGeneration = contours.ContentGeneration;
            _sourceSurface = surface;
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
            LayeredSpanRegionScratch regions,
            LayeredSpanContourScratch contours,
            NavTriangleSurfaceSnapshot surface)
        {
            if (!_hasPublishedContent ||
                raw == null ||
                walkability == null ||
                sheets == null ||
                links == null ||
                radius == null ||
                regions == null ||
                contours == null ||
                surface == null ||
                !raw.HasPublishedContent ||
                !walkability.HasPublishedContent ||
                !sheets.HasPublishedContent ||
                !links.HasPublishedContent ||
                !radius.HasPublishedContent ||
                !regions.HasPublishedContent ||
                !contours.HasPublishedContent)
            {
                return false;
            }

            return ReferenceEquals(_sourceRaw, raw) &&
                   ReferenceEquals(_sourceWalkability, walkability) &&
                   ReferenceEquals(_sourceSheets, sheets) &&
                   ReferenceEquals(_sourceLinks, links) &&
                   ReferenceEquals(_sourceRadius, radius) &&
                   ReferenceEquals(_sourceRegions, regions) &&
                   ReferenceEquals(_sourceContours, contours) &&
                   ReferenceEquals(_sourceSurface, surface) &&
                   _sourceRawContentGeneration == raw.ContentGeneration &&
                   _sourceWalkabilityContentGeneration == walkability.ContentGeneration &&
                   _sourceSheetsContentGeneration == sheets.ContentGeneration &&
                   _sourceLinksContentGeneration == links.ContentGeneration &&
                   _sourceRadiusContentGeneration == radius.ContentGeneration &&
                   _sourceRegionsContentGeneration == regions.ContentGeneration &&
                   _sourceContoursContentGeneration == contours.ContentGeneration;
        }

        private void ClearCounts()
        {
            _vertexCount = 0;
            _triangleCount = 0;
            _constrainedEdgeCount = 0;
            _portalCount = 0;
            _polyCount = 0;
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
            _sourceContours = null;
            _sourceContoursContentGeneration = 0;
            _sourceSurface = null;
        }

        private void PublishNewContentGeneration()
        {
            if (_contentGeneration == ulong.MaxValue)
            {
                InvalidatePublishedContent();
                throw new InvalidOperationException(
                    "LayeredSpanTriangulationScratch.contentGeneration overflow; recreate the scratch instance.");
            }

            _contentGeneration++;
            _hasPublishedContent = true;
        }

        private static int[] EmptyInts(int n) => n == 0 ? Array.Empty<int>() : new int[n];
        private static byte[] EmptyBytes(int n) => n == 0 ? Array.Empty<byte>() : new byte[n];
        private static short[] EmptyShorts(int n) => n == 0 ? Array.Empty<short>() : new short[n];
    }
}
