using System;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.AI.Utility
{
    public static class UtilityAiGraphSafety
    {
        public static void ValidateScoreProgram(ReadOnlySpan<GraphInstruction> program, string source, int graphId)
        {
            GraphKindOperationPolicy.RequireAllowed(
                GraphKind.Score,
                program,
                GasGraphOpHandlerTable.Instance,
                graphId,
                string.IsNullOrWhiteSpace(source) ? "UtilityGraphScore" : source);
        }
    }
}
