using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.GraphCore;

namespace Ludots.Core.Navigation.GraphWorld
{
    public sealed class LoadedGraphView
    {
        public NodeGraph Graph { get; }
        public GraphNodeKey[] NodeKeys { get; }
        public IReadOnlyDictionary<GraphNodeKey, int> NodeIdByKey => _nodeIdByKey;

        private readonly Dictionary<GraphNodeKey, int> _nodeIdByKey;

        internal LoadedGraphView(NodeGraph graph, GraphNodeKey[] nodeKeys, Dictionary<GraphNodeKey, int> nodeIdByKey)
        {
            Graph = graph ?? throw new ArgumentNullException(nameof(graph));
            NodeKeys = nodeKeys ?? throw new ArgumentNullException(nameof(nodeKeys));
            _nodeIdByKey = nodeIdByKey ?? throw new ArgumentNullException(nameof(nodeIdByKey));
        }

        public bool TryGetNodeId(in GraphNodeKey key, out int nodeId)
        {
            return _nodeIdByKey.TryGetValue(key, out nodeId);
        }
    }
}

