using System.Collections.Generic;
using System.Text;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Tests.Gas.Graph
{
    internal static class GraphScriptTestGraphs
    {
        public static GraphControlFlowDocument CreateDrinkUntilFullGraph(int limit = 3)
        {
            return new GraphControlFlowDocument
            {
                Id = "tests.script.drink-until-full",
                Entry = "zeroWater",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "zeroWater", Op = nameof(GraphNodeOp.ConstInt), IntValue = 0, PinRegister = 0 },
                    new() { Id = "limitValue", Op = nameof(GraphNodeOp.ConstInt), IntValue = limit, PinRegister = 1 },
                    new() { Id = "oneValue", Op = nameof(GraphNodeOp.ConstInt), IntValue = 1, PinRegister = 2 },
                    new() { Id = "readWater", Op = nameof(GraphNodeOp.MoveInt) },
                    new() { Id = "readLimit", Op = nameof(GraphNodeOp.MoveInt) },
                    new() { Id = "waterBelowLimit", Op = nameof(GraphNodeOp.CompareLtInt) },
                    new() { Id = "branchNeedDrink", Op = GraphControlFlowCompiler.BranchBoolOp },
                    new() { Id = "callDrink", Op = nameof(GraphNodeOp.Call) },
                    new() { Id = "readWaterForReturn", Op = nameof(GraphNodeOp.MoveInt) },
                    new() { Id = "done", Op = nameof(GraphNodeOp.HaltReturnInt) },
                    new() { Id = "drinkReadWater", Op = nameof(GraphNodeOp.MoveInt) },
                    new() { Id = "drinkReadOne", Op = nameof(GraphNodeOp.MoveInt) },
                    new() { Id = "drinkAdd", Op = nameof(GraphNodeOp.AddInt), PinRegister = 0 },
                    new() { Id = "drinkYield", Op = nameof(GraphNodeOp.Yield) },
                    new() { Id = "drinkReturn", Op = nameof(GraphNodeOp.Return) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("zeroWater", GraphControlFlowPorts.Next, "limitValue"),
                    new("limitValue", GraphControlFlowPorts.Next, "oneValue"),
                    new("oneValue", GraphControlFlowPorts.Next, "readWater"),
                    new("readWater", GraphControlFlowPorts.Next, "readLimit"),
                    new("readLimit", GraphControlFlowPorts.Next, "waterBelowLimit"),
                    new("waterBelowLimit", GraphControlFlowPorts.Next, "branchNeedDrink"),
                    new("branchNeedDrink", GraphControlFlowPorts.True, "callDrink"),
                    new("branchNeedDrink", GraphControlFlowPorts.False, "readWaterForReturn"),
                    new("callDrink", GraphControlFlowPorts.Call, "drinkReadWater"),
                    new("callDrink", GraphControlFlowPorts.Next, "readWater"),
                    new("readWaterForReturn", GraphControlFlowPorts.Next, "done"),
                    new("drinkReadWater", GraphControlFlowPorts.Next, "drinkReadOne"),
                    new("drinkReadOne", GraphControlFlowPorts.Next, "drinkAdd"),
                    new("drinkAdd", GraphControlFlowPorts.Next, "drinkYield"),
                    new("drinkYield", GraphControlFlowPorts.Next, "drinkReturn")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("zeroWater", GraphControlFlowPorts.Value, "readWater", GraphControlFlowPorts.Value),
                    new("limitValue", GraphControlFlowPorts.Value, "readLimit", GraphControlFlowPorts.Value),
                    new("readWater", GraphControlFlowPorts.Value, "waterBelowLimit", GraphControlFlowPorts.A),
                    new("readLimit", GraphControlFlowPorts.Value, "waterBelowLimit", GraphControlFlowPorts.B),
                    new("waterBelowLimit", GraphControlFlowPorts.Value, "branchNeedDrink", GraphControlFlowPorts.Condition),
                    new("zeroWater", GraphControlFlowPorts.Value, "readWaterForReturn", GraphControlFlowPorts.Value),
                    new("readWaterForReturn", GraphControlFlowPorts.Value, "done", GraphControlFlowPorts.Value),
                    new("zeroWater", GraphControlFlowPorts.Value, "drinkReadWater", GraphControlFlowPorts.Value),
                    new("oneValue", GraphControlFlowPorts.Value, "drinkReadOne", GraphControlFlowPorts.Value),
                    new("drinkReadWater", GraphControlFlowPorts.Value, "drinkAdd", GraphControlFlowPorts.A),
                    new("drinkReadOne", GraphControlFlowPorts.Value, "drinkAdd", GraphControlFlowPorts.B)
                }
            };
        }

        public static GraphControlFlowDocument CreateHaltOnlyScript(int value = 7)
        {
            return new GraphControlFlowDocument
            {
                Id = "tests.script.halt-only",
                Entry = "const",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "const", Op = nameof(GraphNodeOp.ConstInt), IntValue = value },
                    new() { Id = "done", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("const", GraphControlFlowPorts.Next, "done")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("const", GraphControlFlowPorts.Value, "done", GraphControlFlowPorts.Value)
                }
            };
        }

        public static string FormatDiagnostics(IReadOnlyList<GraphDiagnostic> diagnostics)
        {
            if (diagnostics.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(diagnostics[i].Code).Append(':').Append(diagnostics[i].NodeId).Append(':')
                    .Append(diagnostics[i].Message);
            }

            return sb.ToString();
        }
    }
}
