using System.Collections.Generic;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public static partial class GraphControlFlowCompiler
    {
        private static void ValidateTimeFlowNode(
            GraphControlFlowNode node,
            GraphNodeOp op,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            GraphValueType[] outputTypes,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            switch (op)
            {
                case GraphNodeOp.ReadTimeFlowPaused:
                case GraphNodeOp.ReadTimeFlowScalePermille:
                case GraphNodeOp.AcquireTimeFlowPause:
                    RequireNonEmpty(node.Domain, "domain", node, graphId, diagnostics);
                    break;
                case GraphNodeOp.AcquireTimeFlowScale:
                    RequireNonEmpty(node.Domain, "domain", node, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;
                case GraphNodeOp.ReleaseTimeFlowToken:
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;
            }
        }

        private static void EmitTimeFlowNode(
            GraphControlFlowNode node,
            GraphNodeOp op,
            ref GraphInstruction instruction,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            GraphValueType[] outputTypes,
            byte[] outputRegisters,
            byte[] boolScratches,
            byte[] droppedRegisters,
            bool[] definedInts,
            bool[] definedBools,
            Dictionary<string, int> symbolToIndex,
            List<string> symbols,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            switch (op)
            {
                case GraphNodeOp.ReadTimeFlowPaused:
                case GraphNodeOp.ReadTimeFlowScalePermille:
                case GraphNodeOp.AcquireTimeFlowPause:
                    instruction.Imm = RequireSymbol(node.Domain, "domain", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;
                case GraphNodeOp.AcquireTimeFlowScale:
                    instruction.Imm = RequireSymbol(node.Domain, "domain", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;
                case GraphNodeOp.ReleaseTimeFlowToken:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;
            }
        }
    }
}
