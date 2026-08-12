using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphFunctionCatalogLoaderTests
    {
        [Test]
        public void Compile_InvokeScript_ByFunctionName_PatchesToGraphId()
        {
            GraphIdRegistry.Clear();
            var programs = new GraphProgramRegistry();
            var catalog = new GraphFunctionCatalog();

            var calleeDoc = new GraphControlFlowDocument
            {
                Id = "Scripts.Demo.Seven",
                Kind = "Script",
                Entry = "c",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "c", Op = nameof(GraphNodeOp.ConstInt), IntValue = 7 },
                    new GraphControlFlowNode { Id = "h", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = { new GraphControlFlowEdge("c", GraphControlFlowPorts.Next, "h") },
                ValueEdges =
                {
                    new GraphControlFlowValueEdge("c", GraphControlFlowPorts.Value, "h", GraphControlFlowPorts.Value)
                }
            };
            var callee = GraphControlFlowCompiler.CompileWithOutputs(calleeDoc);
            Assert.That(callee.Package.HasValue, Is.True);
            int calleeId = GraphIdRegistry.Register(calleeDoc.Id);
            programs.Register(calleeId, callee.Package!.Value.Program, GraphKind.Script);
            catalog.Register("demo.seven", calleeId, GraphKind.Script);

            var callerDoc = new GraphControlFlowDocument
            {
                Id = "Scripts.Demo.Caller",
                Kind = "Script",
                Entry = "invoke",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "invoke", Op = nameof(GraphNodeOp.InvokeScript), FunctionName = "demo.seven" },
                    new GraphControlFlowNode { Id = "done", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = { new GraphControlFlowEdge("invoke", GraphControlFlowPorts.Next, "done") },
                ValueEdges =
                {
                    new GraphControlFlowValueEdge("invoke", GraphControlFlowPorts.Value, "done", GraphControlFlowPorts.Value)
                }
            };
            var caller = GraphControlFlowCompiler.CompileWithOutputs(callerDoc);
            Assert.That(caller.Package.HasValue, Is.True);
            Assert.That(caller.Diagnostics, Has.Count.EqualTo(0));

            GraphInstruction[] program = caller.Package!.Value.Program;
            string[] symbols = caller.Package.Value.Symbols;
            GraphProgramSymbolPatcher.PatchFuncLib(symbols, program, catalog);

            bool found = false;
            for (int i = 0; i < program.Length; i++)
            {
                if (program[i].Op != (ushort)GraphNodeOp.InvokeScript) continue;
                Assert.That(program[i].Flags & GraphInstructionFlags.FuncLibName, Is.EqualTo(0));
                Assert.That(program[i].Imm, Is.EqualTo(calleeId));
                found = true;
            }

            Assert.That(found, Is.True);
        }

        [Test]
        public void FrontDoor_RejectsLegacyNextChain()
        {
            var obj = JsonNode.Parse("""
                {
                  "kind": "Effect",
                  "entry": "a",
                  "nodes": [ { "id": "a", "op": "ConstInt", "intValue": 1, "next": "b" }, { "id": "b", "op": "HaltReturnInt" } ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """)!.AsObject();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                GraphProgramAuthoringFrontDoor.RequireControlFlowAuthoringShape(obj, "bad.next", GraphKind.Effect));
            Assert.That(ex!.Message, Does.Contain("nodes[].next"));
        }
    }
}
