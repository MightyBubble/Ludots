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


        public static GraphControlFlowDocument CreateWaitOnceThenHaltGraph(int value = 9)
        {
            return new GraphControlFlowDocument
            {
                Id = "tests.script.wait-once",
                Kind = "Script",
                Entry = "const",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "const", Op = nameof(GraphNodeOp.ConstInt), IntValue = value },
                    new() { Id = "wait", Op = GraphControlFlowCompiler.WaitOp },
                    new() { Id = "done", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("const", GraphControlFlowPorts.Next, "wait"),
                    new("wait", GraphControlFlowPorts.Next, "done")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("const", GraphControlFlowPorts.Value, "done", GraphControlFlowPorts.Value)
                }
            };
        }

        public static GraphControlFlowDocument CreateCountWhileGraph(int limit = 3)
        {
            // while (counter < limit) counter += 1; return counter
            return new GraphControlFlowDocument
            {
                Id = "tests.script.count-while",
                Kind = "Script",
                Entry = "zero",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "zero", Op = nameof(GraphNodeOp.ConstInt), IntValue = 0, PinRegister = 0 },
                    new() { Id = "limit", Op = nameof(GraphNodeOp.ConstInt), IntValue = limit, PinRegister = 1 },
                    new() { Id = "one", Op = nameof(GraphNodeOp.ConstInt), IntValue = 1, PinRegister = 2 },
                    new() { Id = "readCounter", Op = nameof(GraphNodeOp.MoveInt) },
                    new() { Id = "readLimit", Op = nameof(GraphNodeOp.MoveInt) },
                    new() { Id = "pred", Op = nameof(GraphNodeOp.CompareLtInt) },
                    new() { Id = "loop", Op = GraphControlFlowCompiler.WhileOp },
                    new() { Id = "bodyReadCounter", Op = nameof(GraphNodeOp.MoveInt) },
                    new() { Id = "bodyReadOne", Op = nameof(GraphNodeOp.MoveInt) },
                    new() { Id = "bodyAdd", Op = nameof(GraphNodeOp.AddInt), PinRegister = 0 },
                    new() { Id = "readReturn", Op = nameof(GraphNodeOp.MoveInt) },
                    new() { Id = "done", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("zero", GraphControlFlowPorts.Next, "limit"),
                    new("limit", GraphControlFlowPorts.Next, "one"),
                    new("one", GraphControlFlowPorts.Next, "readCounter"),
                    new("readCounter", GraphControlFlowPorts.Next, "readLimit"),
                    new("readLimit", GraphControlFlowPorts.Next, "pred"),
                    new("pred", GraphControlFlowPorts.Next, "loop"),
                    new("loop", GraphControlFlowPorts.Body, "bodyReadCounter"),
                    new("loop", GraphControlFlowPorts.Next, "readReturn"),
                    new("bodyReadCounter", GraphControlFlowPorts.Next, "bodyReadOne"),
                    new("bodyReadOne", GraphControlFlowPorts.Next, "bodyAdd"),
                    new("bodyAdd", GraphControlFlowPorts.Next, "readCounter"),
                    new("readReturn", GraphControlFlowPorts.Next, "done")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("zero", GraphControlFlowPorts.Value, "readCounter", GraphControlFlowPorts.Value),
                    new("limit", GraphControlFlowPorts.Value, "readLimit", GraphControlFlowPorts.Value),
                    new("readCounter", GraphControlFlowPorts.Value, "pred", GraphControlFlowPorts.A),
                    new("readLimit", GraphControlFlowPorts.Value, "pred", GraphControlFlowPorts.B),
                    new("pred", GraphControlFlowPorts.Value, "loop", GraphControlFlowPorts.Condition),
                    new("zero", GraphControlFlowPorts.Value, "bodyReadCounter", GraphControlFlowPorts.Value),
                    new("one", GraphControlFlowPorts.Value, "bodyReadOne", GraphControlFlowPorts.Value),
                    new("bodyReadCounter", GraphControlFlowPorts.Value, "bodyAdd", GraphControlFlowPorts.A),
                    new("bodyReadOne", GraphControlFlowPorts.Value, "bodyAdd", GraphControlFlowPorts.B),
                    new("zero", GraphControlFlowPorts.Value, "readReturn", GraphControlFlowPorts.Value),
                    new("readReturn", GraphControlFlowPorts.Value, "done", GraphControlFlowPorts.Value)
                }
            };
        }

        public static GraphControlFlowDocument CreateCountUntilGraph(int limit = 3)
        {
            // until (limit < counter) counter += 1; return counter
            // exits when counter becomes > limit
            return new GraphControlFlowDocument
            {
                Id = "tests.script.count-until",
                Kind = "Script",
                Entry = "zero",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "zero", Op = nameof(GraphNodeOp.ConstInt), IntValue = 0, PinRegister = 0 },
                    new() { Id = "limit", Op = nameof(GraphNodeOp.ConstInt), IntValue = limit, PinRegister = 1 },
                    new() { Id = "one", Op = nameof(GraphNodeOp.ConstInt), IntValue = 1, PinRegister = 2 },
                    new() { Id = "readCounter", Op = nameof(GraphNodeOp.MoveInt) },
                    new() { Id = "readLimit", Op = nameof(GraphNodeOp.MoveInt) },
                    new() { Id = "pred", Op = nameof(GraphNodeOp.CompareLtInt) },
                    new() { Id = "loop", Op = GraphControlFlowCompiler.UntilOp },
                    new() { Id = "bodyReadCounter", Op = nameof(GraphNodeOp.MoveInt) },
                    new() { Id = "bodyReadOne", Op = nameof(GraphNodeOp.MoveInt) },
                    new() { Id = "bodyAdd", Op = nameof(GraphNodeOp.AddInt), PinRegister = 0 },
                    new() { Id = "readReturn", Op = nameof(GraphNodeOp.MoveInt) },
                    new() { Id = "done", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("zero", GraphControlFlowPorts.Next, "limit"),
                    new("limit", GraphControlFlowPorts.Next, "one"),
                    new("one", GraphControlFlowPorts.Next, "readCounter"),
                    new("readCounter", GraphControlFlowPorts.Next, "readLimit"),
                    new("readLimit", GraphControlFlowPorts.Next, "pred"),
                    new("pred", GraphControlFlowPorts.Next, "loop"),
                    new("loop", GraphControlFlowPorts.Body, "bodyReadCounter"),
                    new("loop", GraphControlFlowPorts.Next, "readReturn"),
                    new("bodyReadCounter", GraphControlFlowPorts.Next, "bodyReadOne"),
                    new("bodyReadOne", GraphControlFlowPorts.Next, "bodyAdd"),
                    new("bodyAdd", GraphControlFlowPorts.Next, "readCounter"),
                    new("readReturn", GraphControlFlowPorts.Next, "done")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    // pred = limit < counter  (CompareLtInt A=limit, B=counter)
                    new("limit", GraphControlFlowPorts.Value, "readLimit", GraphControlFlowPorts.Value),
                    new("zero", GraphControlFlowPorts.Value, "readCounter", GraphControlFlowPorts.Value),
                    new("readLimit", GraphControlFlowPorts.Value, "pred", GraphControlFlowPorts.A),
                    new("readCounter", GraphControlFlowPorts.Value, "pred", GraphControlFlowPorts.B),
                    new("pred", GraphControlFlowPorts.Value, "loop", GraphControlFlowPorts.Condition),
                    new("zero", GraphControlFlowPorts.Value, "bodyReadCounter", GraphControlFlowPorts.Value),
                    new("one", GraphControlFlowPorts.Value, "bodyReadOne", GraphControlFlowPorts.Value),
                    new("bodyReadCounter", GraphControlFlowPorts.Value, "bodyAdd", GraphControlFlowPorts.A),
                    new("bodyReadOne", GraphControlFlowPorts.Value, "bodyAdd", GraphControlFlowPorts.B),
                    new("zero", GraphControlFlowPorts.Value, "readReturn", GraphControlFlowPorts.Value),
                    new("readReturn", GraphControlFlowPorts.Value, "done", GraphControlFlowPorts.Value)
                }
            };
        }

        public static GraphControlFlowDocument CreateInfiniteWhileGraph()
        {
            return new GraphControlFlowDocument
            {
                Id = "tests.script.infinite-while",
                Kind = "Script",
                Entry = "zero",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "zero", Op = nameof(GraphNodeOp.ConstInt), IntValue = 0 },
                    new() { Id = "one", Op = nameof(GraphNodeOp.ConstInt), IntValue = 1 },
                    new() { Id = "pred", Op = nameof(GraphNodeOp.CompareLtInt) },
                    new() { Id = "loop", Op = GraphControlFlowCompiler.WhileOp },
                    new() { Id = "body", Op = nameof(GraphNodeOp.ConstInt), IntValue = 0 },
                    new() { Id = "done", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("zero", GraphControlFlowPorts.Next, "one"),
                    new("one", GraphControlFlowPorts.Next, "pred"),
                    new("pred", GraphControlFlowPorts.Next, "loop"),
                    new("loop", GraphControlFlowPorts.Body, "body"),
                    new("loop", GraphControlFlowPorts.Next, "done"),
                    new("body", GraphControlFlowPorts.Next, "loop")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("zero", GraphControlFlowPorts.Value, "pred", GraphControlFlowPorts.A),
                    new("one", GraphControlFlowPorts.Value, "pred", GraphControlFlowPorts.B),
                    new("pred", GraphControlFlowPorts.Value, "loop", GraphControlFlowPorts.Condition),
                    new("zero", GraphControlFlowPorts.Value, "done", GraphControlFlowPorts.Value)
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
