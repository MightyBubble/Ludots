using System;
using System.Linq;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// DoOnce compile-time sugar shape and fail-closed gates (#1467). The lowered
    /// chain mirrors a handwritten ReadMapVarInt→ConstInt→CompareEqInt→BranchBool→
    /// WriteMapVarInt gate; runtime behavior is proven end-to-end by
    /// RegionVolumeTextbookAcceptanceTests, whose ambush graph authors this sugar.
    /// </summary>
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphDoOnceSugarTests
    {
        [Test]
        public void DoOnce_LowersToReadWriteLatchChain()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(CreateDoOnceGraph());

            Assert.That(compiled.Succeeded, Is.True, FormatDiagnostics(compiled.Diagnostics));
            var ops = compiled.Program.Select(i => (GraphNodeOp)i.Op).ToArray();

            Assert.That(ops, Does.Contain(GraphNodeOp.ReadMapVarInt));
            Assert.That(ops, Does.Contain(GraphNodeOp.WriteMapVarInt));
            Assert.That(ops, Does.Contain(GraphNodeOp.CompareEqInt));
            Assert.That(ops, Does.Contain(GraphNodeOp.JumpIfFalse));
            Assert.That(ops, Does.Contain(GraphNodeOp.Jump));

            // Latch order: read and compare precede the branch; the write follows it.
            int read = Array.IndexOf(ops, GraphNodeOp.ReadMapVarInt);
            int compare = Array.IndexOf(ops, GraphNodeOp.CompareEqInt);
            int branch = Array.IndexOf(ops, GraphNodeOp.JumpIfFalse);
            int write = Array.IndexOf(ops, GraphNodeOp.WriteMapVarInt);
            Assert.That(read, Is.LessThan(compare));
            Assert.That(compare, Is.LessThan(branch));
            Assert.That(branch, Is.LessThan(write));
        }

        [Test]
        public void DoOnce_InQueryGraph_Rejected()
        {
            var doc = CreateDoOnceGraph();
            doc.Kind = "Query";
            Assert.That(GraphControlFlowCompiler.Compile(doc).Succeeded, Is.False,
                "DoOnce is Script/TriggerGraph sugar only.");
        }

        [Test]
        public void DoOnce_WithoutFalseArm_Rejected()
        {
            var doc = CreateDoOnceGraph();
            doc.ControlEdges.RemoveAll(e => e.From == "gate" && e.FromPort == "false");
            Assert.That(GraphControlFlowCompiler.Compile(doc).Succeeded, Is.False,
                "Both true and false arms are required (BranchBool contract).");
        }

        [Test]
        public void DoOnce_WithoutVar_Rejected()
        {
            var doc = CreateDoOnceGraph();
            doc.Nodes.First(n => n.Id == "gate").Var = null;
            Assert.That(GraphControlFlowCompiler.Compile(doc).Succeeded, Is.False,
                "The latch variable is required.");
        }

        private static string FormatDiagnostics(System.Collections.Generic.List<GraphDiagnostic> diagnostics)
            => string.Join("; ", diagnostics.Select(d => $"{d.Code}:{d.Message}"));

        private static GraphControlFlowDocument CreateDoOnceGraph()
        {
            return new GraphControlFlowDocument
            {
                Id = "script.do_once.probe",
                Kind = "Script",
                Entry = "gate",
                Nodes = new()
                {
                    new GraphControlFlowNode { Id = "gate", Op = GraphAuthoringSugar.DoOnce, Var = "probe.fired" },
                    new GraphControlFlowNode { Id = "first_const", Op = "ConstInt", IntValue = 11 },
                    new GraphControlFlowNode { Id = "first_halt", Op = "HaltReturnInt" },
                    new GraphControlFlowNode { Id = "later_const", Op = "ConstInt", IntValue = 22 },
                    new GraphControlFlowNode { Id = "later_halt", Op = "HaltReturnInt" },
                },
                ControlEdges = new()
                {
                    new GraphControlFlowEdge { From = "gate", FromPort = "true", To = "first_const" },
                    new GraphControlFlowEdge { From = "gate", FromPort = "false", To = "later_const" },
                    new GraphControlFlowEdge { From = "first_const", FromPort = "next", To = "first_halt" },
                    new GraphControlFlowEdge { From = "later_const", FromPort = "next", To = "later_halt" },
                },
            };
        }
    }
}
