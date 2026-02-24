using System;
using Ludots.Core.Collections;

namespace Ludots.Core.Navigation.GraphCore
{
    public sealed class NodeGraphPathScratch
    {
        private int _stamp;

        private int[] _seenStamp = Array.Empty<int>();
        private int[] _closedStamp = Array.Empty<int>();
        private float[] _gScore = Array.Empty<float>();
        private int[] _parentNode = Array.Empty<int>();
        private int[] _parentEdge = Array.Empty<int>();
        private PriorityQueue<int> _open = new PriorityQueue<int>(64);

        public void EnsureCapacity(int nodeCount)
        {
            if (nodeCount <= _gScore.Length) return;
            int newCap = _gScore.Length == 0 ? 4 : _gScore.Length * 2;
            if (newCap < nodeCount) newCap = nodeCount;
            Array.Resize(ref _seenStamp, newCap);
            Array.Resize(ref _closedStamp, newCap);
            Array.Resize(ref _gScore, newCap);
            Array.Resize(ref _parentNode, newCap);
            Array.Resize(ref _parentEdge, newCap);
        }

        public int Begin()
        {
            int s = ++_stamp;
            if (s == int.MaxValue)
            {
                Array.Clear(_seenStamp, 0, _seenStamp.Length);
                Array.Clear(_closedStamp, 0, _closedStamp.Length);
                _stamp = 1;
                s = 1;
            }

            _open.Clear();
            return s;
        }

        public bool IsSeen(int nodeId, int stamp) => _seenStamp[nodeId] == stamp;
        public bool IsClosed(int nodeId, int stamp) => _closedStamp[nodeId] == stamp;
        public void MarkClosed(int nodeId, int stamp) => _closedStamp[nodeId] = stamp;

        public float GetG(int nodeId) => _gScore[nodeId];

        public void SetStart(int nodeId, int stamp)
        {
            _seenStamp[nodeId] = stamp;
            _closedStamp[nodeId] = 0;
            _gScore[nodeId] = 0f;
            _parentNode[nodeId] = -1;
            _parentEdge[nodeId] = -1;
        }

        public void Relax(int nodeId, int stamp, float g, int parentNode, int parentEdge)
        {
            _seenStamp[nodeId] = stamp;
            _gScore[nodeId] = g;
            _parentNode[nodeId] = parentNode;
            _parentEdge[nodeId] = parentEdge;
        }

        public int GetParentNode(int nodeId) => _parentNode[nodeId];
        public int GetParentEdge(int nodeId) => _parentEdge[nodeId];

        public void EnqueueOpen(int nodeId, float priority)
        {
            _open.Enqueue(nodeId, priority);
        }

        public bool TryDequeueOpen(out int nodeId, out float priority)
        {
            return _open.TryDequeue(out nodeId, out priority);
        }
    }
}

