using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphSemantics.GAS;

namespace Ludots.Core.Navigation.Pathing.Config
{
    public static class PathingNodeGraphPolicyCompiler
    {
        public static TagFilter256 CompileEdgeFilter(PathingNodeGraphConfig cfg)
        {
            if (cfg == null) return default;
            var req = CompileTagIds(cfg.RequiredTagsAll);
            var forb = CompileTagIds(cfg.ForbiddenTagsAny);
            return GraphTagFilterBuilder.Compile(req, forb);
        }

        public static TagRuleTraversalPolicy.TagRule[] CompileEdgeRules(PathingNodeGraphConfig cfg)
        {
            if (cfg?.TagCostRules == null || cfg.TagCostRules.Count == 0) return Array.Empty<TagRuleTraversalPolicy.TagRule>();
            var rules = new TagRuleTraversalPolicy.TagRule[cfg.TagCostRules.Count];
            for (int i = 0; i < cfg.TagCostRules.Count; i++)
            {
                var r = cfg.TagCostRules[i];
                if (r == null) throw new InvalidOperationException("PathingNodeGraphConfig.tagCostRules contains null.");
                if (string.IsNullOrWhiteSpace(r.Tag)) throw new InvalidOperationException("PathingNodeGraphConfig.tagCostRules.tag is required.");

                int tagId = TagRegistry.GetId(r.Tag);
                if (tagId == TagRegistry.InvalidId)
                {
                    tagId = TagRegistry.Register(r.Tag);
                }

                var bits = GraphTagSetRegistry.TagBitsFromIds(new[] { tagId });
                float mul = r.CostMul;
                if (float.IsNaN(mul) || mul <= 0f) throw new InvalidOperationException($"Invalid costMul for tag '{r.Tag}'.");
                float add = r.CostAdd;
                if (float.IsNaN(add)) throw new InvalidOperationException($"Invalid costAdd for tag '{r.Tag}'.");

                rules[i] = new TagRuleTraversalPolicy.TagRule(in bits, mul, add, r.Block);
            }
            return rules;
        }

        private static int[] CompileTagIds(List<string> tags)
        {
            if (tags == null || tags.Count == 0) return Array.Empty<int>();
            var ids = new int[tags.Count];
            int count = 0;
            for (int i = 0; i < tags.Count; i++)
            {
                var t = tags[i];
                if (string.IsNullOrWhiteSpace(t)) continue;
                int id = TagRegistry.GetId(t);
                if (id == TagRegistry.InvalidId)
                {
                    id = TagRegistry.Register(t);
                }
                ids[count++] = id;
            }
            if (count == ids.Length) return ids;
            if (count == 0) return Array.Empty<int>();
            Array.Resize(ref ids, count);
            return ids;
        }
    }
}

