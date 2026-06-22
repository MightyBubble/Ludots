using System;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.AI.Utility
{
    public static class UtilityAiGraphSafety
    {
        public static void ValidateScoreProgram(ReadOnlySpan<GraphInstruction> program, string source, int graphId)
        {
            for (int i = 0; i < program.Length; i++)
            {
                var op = (GraphNodeOp)program[i].Op;
                if (IsScoreWriteOp(op))
                {
                    throw new InvalidOperationException(
                        $"{source}: AI GraphScore graph {graphId} contains write/effect op '{op}' at instruction {i}.");
                }
            }
        }

        private static bool IsScoreWriteOp(GraphNodeOp op)
        {
            switch (op)
            {
                case GraphNodeOp.ApplyEffectTemplate:
                case GraphNodeOp.FanOutApplyEffect:
                case GraphNodeOp.ApplyEffectDynamic:
                case GraphNodeOp.FanOutApplyEffectDynamic:
                case GraphNodeOp.RemoveEffectTemplate:
                case GraphNodeOp.FanOutDispatchEffect:
                case GraphNodeOp.FanOutDispatchEffectDynamic:
                case GraphNodeOp.ModifyAttributeAdd:
                case GraphNodeOp.SendEvent:
                case GraphNodeOp.WriteBlackboardFloat:
                case GraphNodeOp.WriteBlackboardInt:
                case GraphNodeOp.WriteBlackboardEntity:
                case GraphNodeOp.WriteSelfAttribute:
                case GraphNodeOp.RelationshipEnsureLink:
                case GraphNodeOp.RelationshipRemoveLink:
                case GraphNodeOp.RelationshipSetMetric:
                case GraphNodeOp.RelationshipAddMetric:
                case GraphNodeOp.RelationshipSetFlag:
                    return true;
                default:
                    return false;
            }
        }
    }
}
