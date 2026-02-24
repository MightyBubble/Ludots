namespace Ludots.Core.Navigation.GraphCore
{
    public struct TagAndOverlayTraversalPolicy : INodeGraphTraversalPolicy
    {
        private readonly ushort[] _nodeTagSetId;
        private readonly ushort[] _edgeTagSetId;

        public TagBits256[] TagSets;
        public TagFilter256 NodeFilter;
        public TagFilter256 EdgeFilter;
        public bool UseNodeFilter;
        public bool UseEdgeFilter;

        public GraphEdgeCostOverlay Overlay;
        public bool UseOverlay;

        public bool IsNodeAllowed(int nodeId)
        {
            if (!UseNodeFilter) return true;
            ushort setId = _nodeTagSetId[nodeId];
            return NodeFilter.Matches(in TagSets[setId]);
        }

        public bool IsEdgeAllowed(int edgeIndex, int fromNode, int toNode)
        {
            if (UseOverlay)
            {
                if (Overlay.Blocked[edgeIndex] != 0) return false;
            }

            if (!UseEdgeFilter) return true;
            ushort setId = _edgeTagSetId[edgeIndex];
            return EdgeFilter.Matches(in TagSets[setId]);
        }

        public float GetEdgeCost(int edgeIndex, float baseCost)
        {
            if (!UseOverlay) return baseCost;
            float mul = 1f + Overlay.CostMul[edgeIndex];
            return baseCost * mul + Overlay.CostAdd[edgeIndex];
        }

        public float GetHeuristic(int nodeId, int goalNodeId, int dxCm, int dyCm)
        {
            long dx = dxCm;
            long dy = dyCm;
            double d2 = (double)(dx * dx + dy * dy);
            return (float)System.Math.Sqrt(d2);
        }

        public TagAndOverlayTraversalPolicy(NodeGraph graph)
        {
            TagSets = graph == null ? System.Array.Empty<TagBits256>() : graph.TagSetsArray;
            NodeFilter = default;
            EdgeFilter = default;
            UseNodeFilter = false;
            UseEdgeFilter = false;
            Overlay = null;
            UseOverlay = false;

            _nodeTagSetId = graph == null ? System.Array.Empty<ushort>() : graph.NodeTagSetIdArray;
            _edgeTagSetId = graph == null ? System.Array.Empty<ushort>() : graph.EdgeTagSetIdArray;
        }
    }
}
