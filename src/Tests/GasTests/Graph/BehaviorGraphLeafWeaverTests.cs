using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// BtLeaf / FsmAction portals: outer BT/FSM topology double-clicks into a function Script;
    /// BehaviorGraphLeafWeaver splices before compile (BT strips Halt/Return; FSM keeps Halt).
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class BehaviorGraphLeafWeaverTests
    {
        [SetUp]
        public void SetUp() => GraphIdRegistry.Clear();

        [TearDown]
        public void TearDown() => GraphIdRegistry.Clear();

        [Test]
        public void BtLeaf_SplicesFunctionGraph_AndCompilesAsBtTree()
        {
            var docs = new Dictionary<string, GraphControlFlowDocument>(StringComparer.OrdinalIgnoreCase)
            {
                ["Graph.Leaf.Sense"] = BuildSenseLeaf(),
                ["Graph.Tree.Root"] = BuildTreeWithBtLeaf("Graph.Leaf.Sense"),
            };

            BehaviorGraphLeafWeaver.ExpandDocuments(docs);

            GraphControlFlowDocument host = docs["Graph.Tree.Root"];
            Assert.That(host.Nodes.Any(n => n.Op == GraphAuthoringSugar.BtLeaf), Is.False);
            Assert.That(host.Nodes.Any(n => n.Op == nameof(GraphNodeOp.HaltReturnInt)), Is.False,
                "BtLeaf weave must strip HaltReturnInt so the BT epilogue owns status.");

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(host);
            Assert.That(compiled.Diagnostics.Count(d => d.Severity == GraphDiagnosticSeverity.Error), Is.EqualTo(0),
                string.Join(" | ", compiled.Diagnostics.Select(d => d.Message)));
            Assert.That(compiled.Package, Is.Not.Null);
        }

        [Test]
        public void BtAction_And_BtCondition_UseSameWeavePathAsBtLeaf()
        {
            var docs = new Dictionary<string, GraphControlFlowDocument>(StringComparer.OrdinalIgnoreCase)
            {
                ["Graph.Func.See"] = BuildSenseLeaf(),
                ["Graph.Func.Atk"] = BuildSenseLeaf(),
                ["Graph.BT.Tree.Sample"] = new GraphControlFlowDocument
                {
                    Id = "Graph.BT.Tree.Sample",
                    Kind = "Script",
                    Entry = "root",
                    Nodes =
                    {
                        new GraphControlFlowNode { Id = "root", Op = GraphAuthoringSugar.BtSequence },
                        new GraphControlFlowNode
                        {
                            Id = "see",
                            Op = GraphAuthoringSugar.BtCondition,
                            FunctionName = "Graph.Func.See",
                        },
                        new GraphControlFlowNode
                        {
                            Id = "atk",
                            Op = GraphAuthoringSugar.BtAction,
                            FunctionName = "Graph.Func.Atk",
                        },
                    },
                    ControlEdges =
                    {
                        new GraphControlFlowEdge("root", "child:0", "see"),
                        new GraphControlFlowEdge("root", "child:1", "atk"),
                    },
                },
            };

            BehaviorGraphLeafWeaver.ExpandDocuments(docs);

            GraphControlFlowDocument host = docs["Graph.BT.Tree.Sample"];
            Assert.That(host.Nodes.Any(n => GraphAuthoringSugar.IsBtLeafPortal(n.Op)), Is.False);
            Assert.That(host.Nodes.Any(n => n.Op == nameof(GraphNodeOp.HaltReturnInt)), Is.False);

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(host);
            Assert.That(compiled.Diagnostics.Count(d => d.Severity == GraphDiagnosticSeverity.Error), Is.EqualTo(0),
                string.Join(" | ", compiled.Diagnostics.Select(d => d.Message)));
            Assert.That(compiled.Package, Is.Not.Null);
        }

        [Test]
        public void BtLeaf_UnknownFunctionGraph_FailsClosed()
        {
            var docs = new Dictionary<string, GraphControlFlowDocument>(StringComparer.OrdinalIgnoreCase)
            {
                ["Graph.Tree.Root"] = BuildTreeWithBtLeaf("Graph.Leaf.Missing"),
            };

            var ex = Assert.Throws<InvalidOperationException>(() => BehaviorGraphLeafWeaver.ExpandDocuments(docs));
            Assert.That(ex!.Message, Does.Contain("unknown"));
        }

        [Test]
        public void FsmAction_KeepsHalt_AndCompiles()
        {
            var docs = new Dictionary<string, GraphControlFlowDocument>(StringComparer.OrdinalIgnoreCase)
            {
                ["Graph.Fsm.IdleBody"] = BuildFsmBody(),
                ["Graph.Fsm.Host"] = BuildFsmHostWithAction("Graph.Fsm.IdleBody"),
            };

            BehaviorGraphLeafWeaver.ExpandDocuments(docs);

            GraphControlFlowDocument host = docs["Graph.Fsm.Host"];
            Assert.That(host.Nodes.Any(n => n.Op == GraphAuthoringSugar.FsmAction), Is.False);
            Assert.That(host.Nodes.Any(n => n.Op == nameof(GraphNodeOp.HaltReturnInt)), Is.True);

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(host);
            Assert.That(compiled.Diagnostics.Count(d => d.Severity == GraphDiagnosticSeverity.Error), Is.EqualTo(0),
                string.Join(" | ", compiled.Diagnostics.Select(d => d.Message)));
            Assert.That(compiled.Package, Is.Not.Null);
        }

        [Test]
        public void LeftoverBtLeaf_WithoutExpand_FailsAtCompile()
        {
            GraphControlFlowDocument host = BuildTreeWithBtLeaf("Graph.Leaf.Sense");
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(host);
            Assert.That(compiled.Diagnostics.Any(d =>
                    d.Severity == GraphDiagnosticSeverity.Error &&
                    d.Message.Contains(GraphAuthoringSugar.BtLeaf, StringComparison.Ordinal)),
                Is.True);
        }

        private static GraphControlFlowDocument BuildSenseLeaf()
            => new()
            {
                Id = "Graph.Leaf.Sense",
                Kind = "Script",
                Entry = "yes",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "yes", Op = "ConstInt", IntValue = 1 },
                    new GraphControlFlowNode { Id = "halt", Op = "HaltReturnInt" },
                },
                ControlEdges =
                {
                    new GraphControlFlowEdge("yes", "next", "halt"),
                },
                ValueEdges =
                {
                    new GraphControlFlowValueEdge("yes", "value", "halt", "value"),
                },
            };

        private static GraphControlFlowDocument BuildTreeWithBtLeaf(string leafGraphId)
            => new()
            {
                Id = "Graph.Tree.Root",
                Kind = "Script",
                Entry = "root",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "root", Op = GraphAuthoringSugar.BtSequence },
                    new GraphControlFlowNode
                    {
                        Id = "leaf",
                        Op = GraphAuthoringSugar.BtLeaf,
                        FunctionName = leafGraphId,
                    },
                },
                ControlEdges =
                {
                    new GraphControlFlowEdge("root", "child:0", "leaf"),
                },
            };

        private static GraphControlFlowDocument BuildFsmBody()
            => new()
            {
                Id = "Graph.Fsm.IdleBody",
                Kind = "Script",
                Entry = "code",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "code", Op = "ConstInt", IntValue = 7 },
                    new GraphControlFlowNode { Id = "halt", Op = "HaltReturnInt" },
                },
                ControlEdges =
                {
                    new GraphControlFlowEdge("code", "next", "halt"),
                },
                ValueEdges =
                {
                    new GraphControlFlowValueEdge("code", "value", "halt", "value"),
                },
            };

        private static GraphControlFlowDocument BuildFsmHostWithAction(string bodyGraphId)
            => new()
            {
                Id = "Graph.Fsm.Host",
                Kind = "Script",
                Entry = "go",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "go", Op = "ConstInt", IntValue = 0 },
                    new GraphControlFlowNode
                    {
                        Id = "idle",
                        Op = GraphAuthoringSugar.FsmAction,
                        FunctionName = bodyGraphId,
                    },
                },
                ControlEdges =
                {
                    new GraphControlFlowEdge("go", "next", "idle"),
                },
            };
    }
}
