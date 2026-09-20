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
    /// InlineGraph compile-time macro: AwaitCallback may live inside a reusable fragment
    /// after splice; runtime InvokeGraph remains sync-only (ContainsYield ban unchanged).
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class TriggerGraphInlineGraphTests
    {
        [SetUp]
        public void SetUp() => GraphIdRegistry.Clear();

        [TearDown]
        public void TearDown() => GraphIdRegistry.Clear();

        [Test]
        public void InlineGraph_WithAwaitCallback_ExpandsIntoHostProgram_WithoutInvokeGraph()
        {
            var docs = new Dictionary<string, GraphControlFlowDocument>(StringComparer.OrdinalIgnoreCase)
            {
                ["Graph.Macro.AskConfirm"] = BuildMacroWithAwait(),
                ["Graph.Host.InlineAsk"] = BuildHostInlining("Graph.Macro.AskConfirm")
            };

            TriggerGraphInlineWeaver.ExpandDocuments(docs);

            GraphControlFlowDocument host = docs["Graph.Host.InlineAsk"];
            Assert.That(host.Nodes.Any(n => n.Op == GraphAuthoringSugar.InlineGraph), Is.False);
            Assert.That(host.Nodes.Any(n => n.Op == nameof(GraphNodeOp.AwaitCallback)), Is.True);

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(host);
            Assert.That(compiled.Diagnostics.Count(d => d.Severity == GraphDiagnosticSeverity.Error), Is.EqualTo(0),
                string.Join(" | ", compiled.Diagnostics.Select(d => d.Message)));
            Assert.That(compiled.Package, Is.Not.Null);

            GraphInstruction[] program = compiled.Package!.Value.Program;
            Assert.That(program.Any(i => i.Op == (ushort)GraphNodeOp.AwaitCallback), Is.True);
            Assert.That(program.Any(i => i.Op == (ushort)GraphNodeOp.InvokeGraph), Is.False,
                "InlineGraph must splice instructions, not emit InvokeGraph");
        }

        [Test]
        public void InlineGraph_UnknownMacro_FailsClosed()
        {
            var docs = new Dictionary<string, GraphControlFlowDocument>(StringComparer.OrdinalIgnoreCase)
            {
                ["Graph.Host.InlineAsk"] = BuildHostInlining("Graph.Macro.Missing")
            };

            var ex = Assert.Throws<InvalidOperationException>(() => TriggerGraphInlineWeaver.ExpandDocuments(docs));
            Assert.That(ex!.Message, Does.Contain("unknown macro"));
        }

        [Test]
        public void InlineGraph_Cycle_FailsClosed()
        {
            var a = BuildHostInlining("Graph.B");
            a.Id = "Graph.A";
            var b = BuildHostInlining("Graph.A");
            b.Id = "Graph.B";
            // Give each a trivial next halt so site shape is valid before cycle trips.
            a.Nodes.Add(new GraphControlFlowNode { Id = "done", Op = "HaltReturnInt" });
            b.Nodes.Add(new GraphControlFlowNode { Id = "done", Op = "HaltReturnInt" });
            a.ControlEdges.Add(new GraphControlFlowEdge("inline", "then", "done"));
            b.ControlEdges.Add(new GraphControlFlowEdge("inline", "then", "done"));

            var docs = new Dictionary<string, GraphControlFlowDocument>(StringComparer.OrdinalIgnoreCase)
            {
                ["Graph.A"] = a,
                ["Graph.B"] = b
            };

            var ex = Assert.Throws<InvalidOperationException>(() => TriggerGraphInlineWeaver.ExpandDocuments(docs));
            Assert.That(ex!.Message, Does.Contain(TriggerGraphInlineWeaver.InlineCycleError));
        }

        [Test]
        public void LeftoverInlineGraph_WithoutExpand_FailsAtCompile()
        {
            GraphControlFlowDocument host = BuildHostInlining("Graph.Macro.AskConfirm");
            host.Nodes.Add(new GraphControlFlowNode { Id = "done", Op = "HaltReturnInt" });
            host.ControlEdges.Add(new GraphControlFlowEdge("inline", "then", "done"));

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(host);
            Assert.That(
                compiled.Diagnostics.Any(d =>
                    d.Severity == GraphDiagnosticSeverity.Error &&
                    d.Message.Contains(GraphAuthoringSugar.InlineGraph, StringComparison.Ordinal)),
                Is.True);
        }

        private static GraphControlFlowDocument BuildMacroWithAwait()
        {
            return new GraphControlFlowDocument
            {
                Id = "Graph.Macro.AskConfirm",
                Kind = nameof(GraphKind.TriggerGraph),
                Entries =
                {
                    new TriggerGraphEntryConfig
                    {
                        Label = "main",
                        Event = "Story.ManualInvoke",
                        Start = "ask"
                    }
                },
                Nodes =
                {
                    new GraphControlFlowNode
                    {
                        Id = "ask",
                        Op = nameof(GraphNodeOp.AwaitCallback),
                        CallbackType = "DialogConfirm"
                    },
                    new GraphControlFlowNode { Id = "halt", Op = "HaltReturnInt" }
                },
                ControlEdges =
                {
                    new GraphControlFlowEdge("ask", "next", "halt")
                }
            };
        }

        private static GraphControlFlowDocument BuildHostInlining(string macroId)
        {
            return new GraphControlFlowDocument
            {
                Id = "Graph.Host.InlineAsk",
                Kind = nameof(GraphKind.TriggerGraph),
                Entries =
                {
                    new TriggerGraphEntryConfig
                    {
                        Label = "boot",
                        Event = "MapLoaded",
                        Start = "inline"
                    }
                },
                Nodes =
                {
                    new GraphControlFlowNode
                    {
                        Id = "inline",
                        Op = GraphAuthoringSugar.InlineGraph,
                        FunctionName = macroId,
                        EntryLabel = "main"
                    },
                    new GraphControlFlowNode { Id = "done", Op = "HaltReturnInt" }
                },
                ControlEdges =
                {
                    new GraphControlFlowEdge("inline", "then", "done")
                }
            };
        }
    }
}
