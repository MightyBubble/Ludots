using System;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphCore;

namespace Ludots.Core.Spatial.Eqs.Adapters
{
    /// <summary>
    /// Adapts a NodeGraph + INodeGraphSpatialIndex (transport network / nav graph) to IEqsNodeSource.
    /// Reuses the existing spatial index radius query; does not reimplement node search.
    /// </summary>
    public sealed class NodeGraphEqsNodeSource : IEqsNodeSource
    {
        private readonly NodeGraph _graph;
        private readonly INodeGraphSpatialIndex _index;

        public NodeGraphEqsNodeSource(NodeGraph graph, INodeGraphSpatialIndex index)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _index = index ?? throw new ArgumentNullException(nameof(index));
        }

        public int QueryNodePositions(WorldCmInt2 center, int radiusCm, Span<WorldCmInt2> positions)
        {
            if (positions.IsEmpty)
            {
                return 0;
            }

            Span<int> nodeIds = positions.Length <= 256
                ? stackalloc int[positions.Length]
                : new int[positions.Length];

            GraphQueryResult result = _index.QueryRadius(center, radiusCm, nodeIds);
            int count = result.Count;

            ReadOnlySpan<int> posX = _graph.PosXcm;
            ReadOnlySpan<int> posY = _graph.PosYcm;

            int written = 0;
            for (int i = 0; i < count && written < positions.Length; i++)
            {
                int nodeId = nodeIds[i];
                if ((uint)nodeId >= (uint)_graph.NodeCount)
                {
                    continue;
                }

                positions[written++] = new WorldCmInt2(posX[nodeId], posY[nodeId]);
            }

            return written;
        }
    }
}
