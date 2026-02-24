using System;
using Ludots.Core.Navigation.GraphCore;

namespace Ludots.Core.Navigation.GraphWorld
{
    public sealed class GraphChunkData
    {
        public NodeGraph Graph { get; }
        public GraphCrossEdge[] CrossEdges { get; }

        public GraphChunkData(NodeGraph graph, GraphCrossEdge[] crossEdges)
        {
            Graph = graph ?? throw new ArgumentNullException(nameof(graph));
            CrossEdges = crossEdges ?? Array.Empty<GraphCrossEdge>();
        }
    }
}

