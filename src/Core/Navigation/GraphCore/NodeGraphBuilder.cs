using System;

namespace Ludots.Core.Navigation.GraphCore
{
    public sealed class NodeGraphBuilder
    {
        private int[] _posXcm = Array.Empty<int>();
        private int[] _posYcm = Array.Empty<int>();
        private ushort[] _nodeTagSetId = Array.Empty<ushort>();

        private int[] _edgeFrom = Array.Empty<int>();
        private int[] _edgeTo = Array.Empty<int>();
        private float[] _edgeBaseCost = Array.Empty<float>();
        private ushort[] _edgeTagSetId = Array.Empty<ushort>();

        private int _nodeCount;
        private int _edgeCount;

        public int NodeCount => _nodeCount;
        public int EdgeCount => _edgeCount;

        public NodeGraphBuilder(int initialNodeCapacity = 1024, int initialEdgeCapacity = 4096)
        {
            if (initialNodeCapacity < 0) throw new ArgumentOutOfRangeException(nameof(initialNodeCapacity));
            if (initialEdgeCapacity < 0) throw new ArgumentOutOfRangeException(nameof(initialEdgeCapacity));

            _posXcm = initialNodeCapacity == 0 ? Array.Empty<int>() : new int[initialNodeCapacity];
            _posYcm = initialNodeCapacity == 0 ? Array.Empty<int>() : new int[initialNodeCapacity];
            _nodeTagSetId = initialNodeCapacity == 0 ? Array.Empty<ushort>() : new ushort[initialNodeCapacity];

            _edgeFrom = initialEdgeCapacity == 0 ? Array.Empty<int>() : new int[initialEdgeCapacity];
            _edgeTo = initialEdgeCapacity == 0 ? Array.Empty<int>() : new int[initialEdgeCapacity];
            _edgeBaseCost = initialEdgeCapacity == 0 ? Array.Empty<float>() : new float[initialEdgeCapacity];
            _edgeTagSetId = initialEdgeCapacity == 0 ? Array.Empty<ushort>() : new ushort[initialEdgeCapacity];
        }

        public void Reset()
        {
            _nodeCount = 0;
            _edgeCount = 0;
        }

        public int AddNode(int xCm, int yCm, ushort tagSetId = 0)
        {
            EnsureNodeCapacity(_nodeCount + 1);
            int id = _nodeCount++;
            _posXcm[id] = xCm;
            _posYcm[id] = yCm;
            _nodeTagSetId[id] = tagSetId;
            return id;
        }

        public int AddEdge(int fromNodeId, int toNodeId, float baseCost, ushort tagSetId = 0)
        {
            if ((uint)fromNodeId >= (uint)_nodeCount) throw new ArgumentOutOfRangeException(nameof(fromNodeId));
            if ((uint)toNodeId >= (uint)_nodeCount) throw new ArgumentOutOfRangeException(nameof(toNodeId));
            if (float.IsNaN(baseCost) || baseCost < 0f) throw new ArgumentOutOfRangeException(nameof(baseCost));

            EnsureEdgeCapacity(_edgeCount + 1);
            int id = _edgeCount++;
            _edgeFrom[id] = fromNodeId;
            _edgeTo[id] = toNodeId;
            _edgeBaseCost[id] = baseCost;
            _edgeTagSetId[id] = tagSetId;
            return id;
        }

        public NodeGraph Build()
        {
            var nodeCount = _nodeCount;
            var edgeCount = _edgeCount;

            var outDegree = nodeCount == 0 ? Array.Empty<int>() : new int[nodeCount];
            for (int i = 0; i < edgeCount; i++)
            {
                outDegree[_edgeFrom[i]]++;
            }

            var edgeStart = new int[nodeCount + 1];
            int sum = 0;
            for (int n = 0; n < nodeCount; n++)
            {
                edgeStart[n] = sum;
                sum += outDegree[n];
            }
            edgeStart[nodeCount] = sum;

            var cursor = new int[nodeCount];
            Array.Copy(edgeStart, cursor, nodeCount);

            var edgeTo = edgeCount == 0 ? Array.Empty<int>() : new int[edgeCount];
            var edgeBaseCost = edgeCount == 0 ? Array.Empty<float>() : new float[edgeCount];
            var edgeTagSetId = edgeCount == 0 ? Array.Empty<ushort>() : new ushort[edgeCount];

            for (int i = 0; i < edgeCount; i++)
            {
                int from = _edgeFrom[i];
                int dst = cursor[from]++;
                edgeTo[dst] = _edgeTo[i];
                edgeBaseCost[dst] = _edgeBaseCost[i];
                edgeTagSetId[dst] = _edgeTagSetId[i];
            }

            var posXcm = nodeCount == 0 ? Array.Empty<int>() : new int[nodeCount];
            var posYcm = nodeCount == 0 ? Array.Empty<int>() : new int[nodeCount];
            var nodeTagSetId = nodeCount == 0 ? Array.Empty<ushort>() : new ushort[nodeCount];
            Array.Copy(_posXcm, posXcm, nodeCount);
            Array.Copy(_posYcm, posYcm, nodeCount);
            Array.Copy(_nodeTagSetId, nodeTagSetId, nodeCount);

            var tagSets = new[] { default(TagBits256) };

            return new NodeGraph(
                nodeCount,
                edgeCount,
                posXcm,
                posYcm,
                nodeTagSetId,
                tagSets,
                edgeStart,
                edgeTo,
                edgeBaseCost,
                edgeTagSetId);
        }

        private void EnsureNodeCapacity(int required)
        {
            if (required <= _posXcm.Length) return;
            int newCap = _posXcm.Length == 0 ? 4 : _posXcm.Length * 2;
            if (newCap < required) newCap = required;
            Array.Resize(ref _posXcm, newCap);
            Array.Resize(ref _posYcm, newCap);
            Array.Resize(ref _nodeTagSetId, newCap);
        }

        private void EnsureEdgeCapacity(int required)
        {
            if (required <= _edgeFrom.Length) return;
            int newCap = _edgeFrom.Length == 0 ? 4 : _edgeFrom.Length * 2;
            if (newCap < required) newCap = required;
            Array.Resize(ref _edgeFrom, newCap);
            Array.Resize(ref _edgeTo, newCap);
            Array.Resize(ref _edgeBaseCost, newCap);
            Array.Resize(ref _edgeTagSetId, newCap);
        }
    }
}
