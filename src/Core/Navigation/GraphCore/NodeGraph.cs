using System;

namespace Ludots.Core.Navigation.GraphCore
{
    public sealed class NodeGraph
    {
        private readonly int[] _posXcm;
        private readonly int[] _posYcm;
        private readonly ushort[] _nodeTagSetId;
        private readonly TagBits256[] _tagSets;
        private readonly int[] _edgeStart;
        private readonly int[] _edgeTo;
        private readonly float[] _edgeBaseCost;
        private readonly ushort[] _edgeTagSetId;

        public int NodeCount { get; }
        public int EdgeCount { get; }

        public ReadOnlySpan<int> PosXcm => _posXcm;
        public ReadOnlySpan<int> PosYcm => _posYcm;
        public ReadOnlySpan<ushort> NodeTagSetId => _nodeTagSetId;
        public ReadOnlySpan<TagBits256> TagSets => _tagSets;

        public ReadOnlySpan<int> EdgeStart => _edgeStart;
        public ReadOnlySpan<int> EdgeTo => _edgeTo;
        public ReadOnlySpan<float> EdgeBaseCost => _edgeBaseCost;
        public ReadOnlySpan<ushort> EdgeTagSetId => _edgeTagSetId;

        internal int[] PosXcmArray => _posXcm;
        internal int[] PosYcmArray => _posYcm;
        internal ushort[] NodeTagSetIdArray => _nodeTagSetId;
        internal TagBits256[] TagSetsArray => _tagSets;
        internal int[] EdgeStartArray => _edgeStart;
        internal int[] EdgeToArray => _edgeTo;
        internal float[] EdgeBaseCostArray => _edgeBaseCost;
        internal ushort[] EdgeTagSetIdArray => _edgeTagSetId;

        internal NodeGraph(
            int nodeCount,
            int edgeCount,
            int[] posXcm,
            int[] posYcm,
            ushort[] nodeTagSetId,
            TagBits256[] tagSets,
            int[] edgeStart,
            int[] edgeTo,
            float[] edgeBaseCost,
            ushort[] edgeTagSetId)
        {
            NodeCount = nodeCount;
            EdgeCount = edgeCount;
            _posXcm = posXcm ?? throw new ArgumentNullException(nameof(posXcm));
            _posYcm = posYcm ?? throw new ArgumentNullException(nameof(posYcm));
            _nodeTagSetId = nodeTagSetId ?? throw new ArgumentNullException(nameof(nodeTagSetId));
            _tagSets = tagSets ?? throw new ArgumentNullException(nameof(tagSets));
            _edgeStart = edgeStart ?? throw new ArgumentNullException(nameof(edgeStart));
            _edgeTo = edgeTo ?? throw new ArgumentNullException(nameof(edgeTo));
            _edgeBaseCost = edgeBaseCost ?? throw new ArgumentNullException(nameof(edgeBaseCost));
            _edgeTagSetId = edgeTagSetId ?? throw new ArgumentNullException(nameof(edgeTagSetId));
        }

        /// <summary>
        /// Get outgoing edges for a node. Throws if nodeId is out of range.
        /// </summary>
        public EdgeRange GetOutgoingEdges(int nodeId)
        {
            if ((uint)nodeId >= (uint)NodeCount)
                throw new ArgumentOutOfRangeException(nameof(nodeId), nodeId, $"nodeId must be in [0, {NodeCount - 1}].");
            int start = _edgeStart[nodeId];
            int end = _edgeStart[nodeId + 1];
            return new EdgeRange(start, end);
        }

        /// <summary>
        /// Try to get outgoing edges for a node. Returns false if nodeId is out of range.
        /// </summary>
        public bool TryGetOutgoingEdges(int nodeId, out EdgeRange range)
        {
            if ((uint)nodeId >= (uint)NodeCount)
            {
                range = default;
                return false;
            }
            range = new EdgeRange(_edgeStart[nodeId], _edgeStart[nodeId + 1]);
            return true;
        }

        public readonly struct EdgeRange
        {
            public readonly int Start;
            public readonly int EndExclusive;

            public EdgeRange(int start, int endExclusive)
            {
                Start = start;
                EndExclusive = endExclusive;
            }
        }
    }
}
