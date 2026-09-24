using System.Collections.Generic;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public static partial class GraphControlFlowCompiler
    {
        private static void ValidateCalendarNode(
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
                case GraphNodeOp.ReadCalendarEnabled:
                case GraphNodeOp.ReadCalendarDayIndex:
                case GraphNodeOp.ReadCalendarTicksIntoDay:
                case GraphNodeOp.ReadCalendarDayPermille:
                case GraphNodeOp.ReadCalendarDayPhase:
                case GraphNodeOp.ReadCalendarYear:
                    break;
                case GraphNodeOp.ReadCalendarCyclePhase:
                case GraphNodeOp.ReadCalendarCycleDay:
                case GraphNodeOp.ReadCalendarCyclePhaseIndex:
                    RequireNonEmpty(node.Cycle, "cycle", node, graphId, diagnostics);
                    break;
                case GraphNodeOp.ReadCalendarDaysUntilPhase:
                    RequireNonEmpty(node.Cycle, "cycle", node, graphId, diagnostics);
                    RequireNonEmpty(node.Phase, "phase", node, graphId, diagnostics);
                    break;
                case GraphNodeOp.LoadConfigKey:
                    RequireNonEmpty(node.Symbol, "symbol", node, graphId, diagnostics);
                    break;
                case GraphNodeOp.ApplyCalendarStart:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;
                case GraphNodeOp.SetCalendarDayIndex:
                case GraphNodeOp.SetCalendarTicksIntoDay:
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;
            }
        }

        private static void EmitCalendarNode(
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
                case GraphNodeOp.ReadCalendarYear:
                    if (!string.IsNullOrWhiteSpace(node.Calendar))
                    {
                        instruction.Imm = RequireSymbol(node.Calendar, "calendar", node, symbolToIndex, symbols, graphId, diagnostics);
                        instruction.Flags = CalendarOpEncoding.CalendarAuthoredFlag;
                    }
                    else
                    {
                        instruction.Imm = 0;
                        instruction.Flags = 0;
                    }

                    break;
                case GraphNodeOp.ReadCalendarCyclePhase:
                case GraphNodeOp.ReadCalendarCycleDay:
                case GraphNodeOp.ReadCalendarCyclePhaseIndex:
                    EmitCycleAddress(node, ref instruction, symbolToIndex, symbols, graphId, diagnostics);
                    break;
                case GraphNodeOp.ReadCalendarDaysUntilPhase:
                    instruction.ImmF = CalendarOpEncoding.SymbolIndexBits(
                        RequireSymbol(node.Phase, "phase", node, symbolToIndex, symbols, graphId, diagnostics));
                    EmitCycleAddress(node, ref instruction, symbolToIndex, symbols, graphId, diagnostics);
                    break;
                case GraphNodeOp.LoadConfigKey:
                    instruction.Imm = RequireSymbol(node.Symbol, "symbol", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;
                case GraphNodeOp.ApplyCalendarStart:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;
                case GraphNodeOp.SetCalendarDayIndex:
                case GraphNodeOp.SetCalendarTicksIntoDay:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;
            }
        }

        private static void EmitCycleAddress(
            GraphControlFlowNode node,
            ref GraphInstruction instruction,
            Dictionary<string, int> symbolToIndex,
            List<string> symbols,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            instruction.Imm = RequireSymbol(node.Cycle, "cycle", node, symbolToIndex, symbols, graphId, diagnostics);
            if (!string.IsNullOrWhiteSpace(node.Calendar))
            {
                int calendarSymbol = RequireSymbol(node.Calendar, "calendar", node, symbolToIndex, symbols, graphId, diagnostics);
                if ((uint)calendarSymbol > CalendarOpEncoding.MaxKeyId)
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                        $"Node '{node.Id}' calendar symbol index {calendarSymbol} does not fit in 16 bits.", node.Id));
                }
                else
                {
                    // B/C hold the calendar symbol index until patch rewrites Imm and clears them.
                    instruction.Flags = CalendarOpEncoding.CalendarAuthoredFlag;
                    instruction.B = (byte)(calendarSymbol & 0xFF);
                    instruction.C = (byte)((calendarSymbol >> 8) & 0xFF);
                }
            }
            else
            {
                instruction.Flags = 0;
            }
        }
    }
}
