namespace Ludots.Core.Navigation.GraphCore
{
    public interface INodeGraphTraversalPolicy
    {
        bool IsNodeAllowed(int nodeId);
        bool IsEdgeAllowed(int edgeIndex, int fromNode, int toNode);
        float GetEdgeCost(int edgeIndex, float baseCost);
        float GetHeuristic(int nodeId, int goalNodeId, int dxCm, int dyCm);
    }

    public struct DefaultTraversalPolicy : INodeGraphTraversalPolicy
    {
        public bool IsNodeAllowed(int nodeId) => true;
        public bool IsEdgeAllowed(int edgeIndex, int fromNode, int toNode) => true;
        public float GetEdgeCost(int edgeIndex, float baseCost) => baseCost;

        public float GetHeuristic(int nodeId, int goalNodeId, int dxCm, int dyCm)
        {
            long dx = dxCm;
            long dy = dyCm;
            double d2 = (double)(dx * dx + dy * dy);
            return (float)System.Math.Sqrt(d2);
        }
    }
}

