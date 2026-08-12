using System.Collections.Generic;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphScriptDiagnosticTests
    {
        [Test]
        public void GraphCompiler_RejectsScriptKind_OnNextChainPath()
        {
            var cfg = new GraphConfig
            {
                Id = "bad.script.next",
                Kind = "Script",
                Entry = "a",
                Nodes =
                {
                    new GraphNodeConfig
                    {
                        Id = "a",
                        Op = nameof(GraphNodeOp.ConstInt),
                        IntValue = 1
                    }
                }
            };
            var (pkg, _, diags) = GraphCompiler.CompileWithOutputs(cfg);
            Assert.That(pkg.HasValue, Is.False);
            Assert.That(
                diags.Exists(d =>
                    d.Severity == GraphDiagnosticSeverity.Error &&
                    d.Message.Contains("GraphControlFlowCompiler", System.StringComparison.Ordinal)),
                Is.True);
        }

        [Test]
        public void Compile_BranchWithoutFalseEdge_FailsFast()
        {
            var graph = new GraphControlFlowDocument
            {
                Id = "tests.script.missing-false",
                Entry = "one",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "one", Op = nameof(GraphNodeOp.ConstInt), IntValue = 1 },
                    new() { Id = "two", Op = nameof(GraphNodeOp.ConstInt), IntValue = 2 },
                    new() { Id = "predicate", Op = nameof(GraphNodeOp.CompareLtInt) },
                    new() { Id = "branch", Op = GraphControlFlowCompiler.BranchBoolOp },
                    new() { Id = "done", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("one", GraphControlFlowPorts.Next, "two"),
                    new("two", GraphControlFlowPorts.Next, "predicate"),
                    new("predicate", GraphControlFlowPorts.Next, "branch"),
                    new("branch", GraphControlFlowPorts.True, "done")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("one", GraphControlFlowPorts.Value, "predicate", GraphControlFlowPorts.A),
                    new("two", GraphControlFlowPorts.Value, "predicate", GraphControlFlowPorts.B),
                    new("predicate", GraphControlFlowPorts.Value, "branch", GraphControlFlowPorts.Condition),
                    new("one", GraphControlFlowPorts.Value, "done", GraphControlFlowPorts.Value)
                }
            };

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingControlEdge && d.NodeId == "branch"));
        }

        [Test]
        public void Compile_NumericOpString_FailsFast()
        {
            var graph = new GraphControlFlowDocument
            {
                Id = "tests.script.numeric-op",
                Entry = "bad",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "bad", Op = "1" },
                    new() { Id = "done", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("bad", GraphControlFlowPorts.Next, "done")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("bad", GraphControlFlowPorts.Value, "done", GraphControlFlowPorts.Value)
                }
            };

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UnknownNodeOp));
        }

        [Test]
        public void GraphNodeOpParser_RejectsNumericString()
        {
            Assert.That(GraphNodeOpParser.TryParse("1", out _), Is.False);
            Assert.That(GraphNodeOpParser.TryParse("ConstInt", out GraphNodeOp op), Is.True);
            Assert.That(op, Is.EqualTo(GraphNodeOp.ConstInt));
        }

        [Test]
        public void Compile_UnreachableNode_FailsFast()
        {
            var graph = new GraphControlFlowDocument
            {
                Id = "tests.script.unreachable",
                Entry = "one",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "one", Op = nameof(GraphNodeOp.ConstInt), IntValue = 1 },
                    new() { Id = "ghost", Op = nameof(GraphNodeOp.ConstInt), IntValue = 9 },
                    new() { Id = "done", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("one", GraphControlFlowPorts.Next, "done")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("one", GraphControlFlowPorts.Value, "done", GraphControlFlowPorts.Value)
                }
            };

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UnreachableNode && d.NodeId == "ghost"));
        }

        [Test]
        public void Compile_UninitializedRegisterRead_FailsFast()
        {
            // Compile order follows Nodes list; HaltReturnInt is authored before its ConstInt producer.
            var graph = new GraphControlFlowDocument
            {
                Id = "tests.script.uninit-reg",
                Entry = "const",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "done", Op = nameof(GraphNodeOp.HaltReturnInt) },
                    new() { Id = "const", Op = nameof(GraphNodeOp.ConstInt), IntValue = 4 }
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

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UninitializedRegisterRead && d.NodeId == "done"));
        }

        [Test]
        public void Compile_ValuePinTypeMismatch_FailsFast()
        {
            var graph = new GraphControlFlowDocument
            {
                Id = "tests.script.type-mismatch",
                Entry = "one",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "one", Op = nameof(GraphNodeOp.ConstInt), IntValue = 1 },
                    new() { Id = "branch", Op = GraphControlFlowCompiler.BranchBoolOp },
                    new() { Id = "done", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("one", GraphControlFlowPorts.Next, "branch"),
                    new("branch", GraphControlFlowPorts.True, "done"),
                    new("branch", GraphControlFlowPorts.False, "done")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("one", GraphControlFlowPorts.Value, "branch", GraphControlFlowPorts.Condition),
                    new("one", GraphControlFlowPorts.Value, "done", GraphControlFlowPorts.Value)
                }
            };

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.TypeMismatch && d.NodeId == "branch"));
        }
    }
}
