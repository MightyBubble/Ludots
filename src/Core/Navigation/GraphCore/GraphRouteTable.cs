using System;

namespace Ludots.Core.Navigation.GraphCore
{
    public sealed class GraphRouteTable
    {
        public int NodeCount { get; }

        private readonly ushort[] _nextHop;
        private readonly float[] _cost;

        public GraphRouteTable(int nodeCount, ushort[] nextHop, float[] cost)
        {
            NodeCount = nodeCount;
            _nextHop = nextHop ?? throw new ArgumentNullException(nameof(nextHop));
            _cost = cost ?? throw new ArgumentNullException(nameof(cost));
            if (_nextHop.Length != nodeCount * nodeCount) throw new ArgumentException("nextHop size mismatch.", nameof(nextHop));
            if (_cost.Length != nodeCount * nodeCount) throw new ArgumentException("cost size mismatch.", nameof(cost));
        }

        public bool TryGetCost(int srcNodeId, int dstNodeId, out float cost)
        {
            if ((uint)srcNodeId >= (uint)NodeCount || (uint)dstNodeId >= (uint)NodeCount)
            {
                cost = 0f;
                return false;
            }
            cost = _cost[srcNodeId * NodeCount + dstNodeId];
            return !float.IsPositiveInfinity(cost);
        }

        public bool TryGetNextHop(int srcNodeId, int dstNodeId, out int nextHopNodeId)
        {
            if ((uint)srcNodeId >= (uint)NodeCount || (uint)dstNodeId >= (uint)NodeCount)
            {
                nextHopNodeId = -1;
                return false;
            }
            ushort hop = _nextHop[srcNodeId * NodeCount + dstNodeId];
            if (hop == ushort.MaxValue)
            {
                nextHopNodeId = -1;
                return false;
            }
            nextHopNodeId = hop;
            return true;
        }

        public GraphPathResult ReconstructPath(int srcNodeId, int dstNodeId, Span<int> outPath)
        {
            if ((uint)srcNodeId >= (uint)NodeCount || (uint)dstNodeId >= (uint)NodeCount)
            {
                return new GraphPathResult(GraphPathStatus.InvalidInput, 0, 0, 0);
            }

            if (srcNodeId == dstNodeId)
            {
                if (outPath.Length < 1) return new GraphPathResult(GraphPathStatus.BufferTooSmall, 0, 1, 0);
                outPath[0] = srcNodeId;
                return new GraphPathResult(GraphPathStatus.Success, 1, 1, 0);
            }

            if (!TryGetNextHop(srcNodeId, dstNodeId, out _))
            {
                return new GraphPathResult(GraphPathStatus.NotFound, 0, 0, 0);
            }

            int written = 0;
            int cur = srcNodeId;
            outPath[written++] = cur;

            int guard = NodeCount + 1;
            while (cur != dstNodeId && guard-- > 0)
            {
                if (!TryGetNextHop(cur, dstNodeId, out int hop))
                {
                    return new GraphPathResult(GraphPathStatus.NotFound, 0, 0, 0);
                }

                if (written >= outPath.Length)
                {
                    return new GraphPathResult(GraphPathStatus.BufferTooSmall, 0, written + 1, 0);
                }

                cur = hop;
                outPath[written++] = cur;
            }

            if (cur != dstNodeId) return new GraphPathResult(GraphPathStatus.NotFound, 0, 0, 0);
            return new GraphPathResult(GraphPathStatus.Success, written, written, 0);
        }
    }
}

