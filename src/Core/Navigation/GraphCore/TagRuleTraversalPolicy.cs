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

        public TagBits256[] TagSets;
        public TagFilter256 EdgeFilter;
        public bool UseEdgeFilter;
        public TagRule[] EdgeRules;

        public TagRuleTraversalPolicy(NodeGraph graph)
        {
            TagSets = graph == null ? Array.Empty<TagBits256>() : graph.TagSetsArray;
            EdgeFilter = default;
            UseEdgeFilter = false;
            EdgeRules = Array.Empty<TagRule>();
            _edgeTagSetId = graph == null ? Array.Empty<ushort>() : graph.EdgeTagSetIdArray;
        }

        public bool IsNodeAllowed(int nodeId) => true;

        public bool IsEdgeAllowed(int edgeIndex, int fromNode, int toNode)
        {
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
            if (EdgeRules == null || EdgeRules.Length == 0) return baseCost;
            ushort setId = _edgeTagSetId[edgeIndex];
            var tags = TagSets[setId];

            float c = baseCost;
            for (int i = 0; i < EdgeRules.Length; i++)
            {
                ref readonly var r = ref EdgeRules[i];
                if (!tags.Intersects(in r.MatchAny)) continue;
                c = c * r.CostMul + r.CostAdd;
            }
            return c;
        }

        public float GetHeuristic(int nodeId, int goalNodeId, int dxCm, int dyCm)
        {
            long dx = dxCm;
            long dy = dyCm;
            double d2 = (double)(dx * dx + dy * dy);
            return (float)Math.Sqrt(d2);
        }
    }
}
