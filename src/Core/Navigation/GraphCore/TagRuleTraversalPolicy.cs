using System;

namespace Ludots.Core.Navigation.GraphCore
{
    public struct TagRuleTraversalPolicy : INodeGraphTraversalPolicy
    {
        public readonly struct TagRule
        {
            public readonly TagBits256 MatchAny;
            public readonly float CostMul;
            public readonly float CostAdd;
            public readonly bool Block;

            public TagRule(in TagBits256 matchAny, float costMul, float costAdd, bool block)
            {
                MatchAny = matchAny;
                CostMul = costMul;
                CostAdd = costAdd;
                Block = block;
            }
        }

        private readonly ushort[] _edgeTagSetId;
        private readonly int[] _edgeDepthCm;
        private readonly int[] _edgeWidthCm;

        public TagBits256[] TagSets;
        public TagFilter256 EdgeFilter;
        public bool UseEdgeFilter;
        public TagRule[] EdgeRules;
        public GraphEdgeCostOverlay? Overlay;
        public bool UseOverlay;
        public float AgentDraftCm;
        public float AgentBeamCm;

        public TagRuleTraversalPolicy(NodeGraph graph)
        {
            TagSets = graph == null ? Array.Empty<TagBits256>() : graph.TagSetsArray;
            EdgeFilter = default;
            UseEdgeFilter = false;
            EdgeRules = Array.Empty<TagRule>();
            Overlay = null;
            UseOverlay = false;
            AgentDraftCm = 0f;
            AgentBeamCm = 0f;
            _edgeTagSetId = graph == null ? Array.Empty<ushort>() : graph.EdgeTagSetIdArray;
            _edgeDepthCm = graph == null ? Array.Empty<int>() : graph.EdgeDepthCmArray;
            _edgeWidthCm = graph == null ? Array.Empty<int>() : graph.EdgeWidthCmArray;
        }

        public bool IsNodeAllowed(int nodeId) => true;

        public bool IsEdgeAllowed(int edgeIndex, int fromNode, int toNode)
        {
            if (UseOverlay && Overlay != null && (uint)edgeIndex < (uint)Overlay.Blocked.Length && Overlay.Blocked[edgeIndex] != 0)
            {
                return false;
            }

            if (AgentDraftCm > 0f && (uint)edgeIndex < (uint)_edgeDepthCm.Length)
            {
                int depthCm = _edgeDepthCm[edgeIndex];
                if (depthCm > 0 && AgentDraftCm > depthCm)
                {
                    return false;
                }
            }

            if (AgentBeamCm > 0f && (uint)edgeIndex < (uint)_edgeWidthCm.Length)
            {
                int widthCm = _edgeWidthCm[edgeIndex];
                if (widthCm > 0 && AgentBeamCm > widthCm)
                {
                    return false;
                }
            }

            ushort setId = _edgeTagSetId[edgeIndex];
            var tags = TagSets[setId];

            if (UseEdgeFilter && !EdgeFilter.Matches(in tags)) return false;

            if (EdgeRules != null)
            {
                for (int i = 0; i < EdgeRules.Length; i++)
                {
                    if (!tags.Intersects(in EdgeRules[i].MatchAny)) continue;
                    if (EdgeRules[i].Block) return false;
                }
            }

            return true;
        }

        public float GetEdgeCost(int edgeIndex, float baseCost)
        {
            if (EdgeRules == null || EdgeRules.Length == 0)
            {
                return ApplyOverlay(edgeIndex, baseCost);
            }

            ushort setId = _edgeTagSetId[edgeIndex];
            var tags = TagSets[setId];

            float c = baseCost;
            for (int i = 0; i < EdgeRules.Length; i++)
            {
                ref readonly var r = ref EdgeRules[i];
                if (!tags.Intersects(in r.MatchAny)) continue;
                c = c * r.CostMul + r.CostAdd;
            }
            return ApplyOverlay(edgeIndex, c);
        }

        public float GetHeuristic(int nodeId, int goalNodeId, int dxCm, int dyCm)
        {
            long dx = dxCm;
            long dy = dyCm;
            double d2 = (double)(dx * dx + dy * dy);
            return (float)Math.Sqrt(d2);
        }

        private float ApplyOverlay(int edgeIndex, float staticCost)
        {
            if (!UseOverlay || Overlay == null || (uint)edgeIndex >= (uint)Overlay.CostAdd.Length)
            {
                return staticCost;
            }

            return staticCost * (1f + Overlay.CostMul[edgeIndex]) + Overlay.CostAdd[edgeIndex];
        }
    }
}
