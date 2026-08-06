using System.Collections.Generic;
using System.Linq;
using Ludots.Core.GraphRuntime;
using NUnit.Framework;

namespace Ludots.Tests.GraphRuntime
{
    [TestFixture]
    public sealed class GraphVmControlFlowTests
    {
        [Test]
        public void CompileRegisterRun_GraphWithExplicitControlFlow_CallsFunctionYieldsAndHalts()
        {
            GraphVmDocument graph = GraphVmTestGraphs.CreateDrinkUntilFullGraph();

            GraphVmCompileResult compiled = GraphVmCompiler.Compile(graph);

            Assert.That(compiled.Succeeded, Is.True, GraphVmTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Program.Select(i => (GraphVmOpcode)i.Op), Does.Contain(GraphVmOpcode.JumpIfFalse));
            Assert.That(compiled.Program.Select(i => (GraphVmOpcode)i.Op), Does.Contain(GraphVmOpcode.Call));
            Assert.That(compiled.Program.Select(i => (GraphVmOpcode)i.Op), Does.Contain(GraphVmOpcode.Return));
            Assert.That(compiled.Program.Select(i => (GraphVmOpcode)i.Op), Does.Contain(GraphVmOpcode.Yield));

            var registry = new GraphProgramRegistry();
            registry.Register(7, compiled.Program, GraphKind.Effect, compiled.SourceMap);

            Assert.That(registry.TryGetProgram(7, out var program), Is.True);
            Assert.That(registry.TryGetKind(7, out GraphKind kind), Is.True);
            Assert.That(kind, Is.EqualTo(GraphKind.Effect));
            Assert.That(registry.RequireKind(7, GraphKind.Effect), Is.EqualTo(GraphKind.Effect));
            Assert.That(registry.TryGetSourceMap(7, out GraphInstructionSourceMap sourceMap), Is.True);

            Span<int> ints = stackalloc int[GraphVmRuntimeLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmRuntimeLimits.MaxBoolRegisters];
            Span<int> callStack = stackalloc int[GraphVmRuntimeLimits.MaxCallStackDepth];
            var cursor = new GraphVmExecutionCursor();
            var trace = new RecordingTraceSink(sourceMap);

            GraphVmExecutionResult first = GraphVmExecutor.ExecuteSlice(program, ints, bools, callStack, ref cursor, 64, trace);
            Assert.That(first.Yielded, Is.True);
            Assert.That(cursor.CallStackCount, Is.EqualTo(1));

            GraphVmExecutionResult second = GraphVmExecutor.ExecuteSlice(program, ints, bools, callStack, ref cursor, 64, trace);
            Assert.That(second.Yielded, Is.True);
            Assert.That(cursor.CallStackCount, Is.EqualTo(1));

            GraphVmExecutionResult third = GraphVmExecutor.ExecuteSlice(program, ints, bools, callStack, ref cursor, 64, trace);
            Assert.That(third.Yielded, Is.True);
            Assert.That(cursor.CallStackCount, Is.EqualTo(1));

            GraphVmExecutionResult fourth = GraphVmExecutor.ExecuteSlice(program, ints, bools, callStack, ref cursor, 64, trace);

            Assert.That(fourth.Halted, Is.True);
            Assert.That(fourth.ReturnInt, Is.EqualTo(3));
            Assert.That(cursor.CallStackCount, Is.EqualTo(0));

            Assert.That(Count(trace.Sources, "callDrink", GraphVmControlPorts.Call), Is.EqualTo(3));
            Assert.That(Count(trace.Sources, "drinkReturn", GraphVmControlPorts.Enter), Is.EqualTo(3));
            Assert.That(Count(trace.Sources, "drinkYield", GraphVmControlPorts.Enter), Is.EqualTo(3));
            Assert.That(Count(trace.Sources, "branchNeedDrink"), Is.EqualTo(7));
            Assert.That(trace.Sources.Last().NodeId, Is.EqualTo("done"));
        }

        [Test]
        public void Compile_BranchWithoutFalseEdge_FailsFast()
        {
            var graph = new GraphVmDocument
            {
                Id = "tests.graphvm.missing-false",
                Entry = "one",
                Nodes = new List<GraphVmNode>
                {
                    new() { Id = "one", Op = nameof(GraphVmOpcode.ConstInt), IntValue = 1 },
                    new() { Id = "two", Op = nameof(GraphVmOpcode.ConstInt), IntValue = 2 },
                    new() { Id = "predicate", Op = nameof(GraphVmOpcode.LessThanInt) },
                    new() { Id = "branch", Op = nameof(GraphVmOpcode.BranchBool) },
                    new() { Id = "done", Op = nameof(GraphVmOpcode.ReturnInt) }
                },
                ControlEdges = new List<GraphVmControlEdge>
                {
                    new("one", GraphVmControlPorts.Next, "two"),
                    new("two", GraphVmControlPorts.Next, "predicate"),
                    new("predicate", GraphVmControlPorts.Next, "branch"),
                    new("branch", GraphVmControlPorts.True, "done")
                },
                ValueEdges = new List<GraphVmValueEdge>
                {
                    new("one", GraphVmValuePorts.Value, "predicate", GraphVmValuePorts.A),
                    new("two", GraphVmValuePorts.Value, "predicate", GraphVmValuePorts.B),
                    new("predicate", GraphVmValuePorts.Value, "branch", GraphVmValuePorts.Condition),
                    new("one", GraphVmValuePorts.Value, "done", GraphVmValuePorts.Value)
                }
            };

            GraphVmCompileResult compiled = GraphVmCompiler.Compile(graph);

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphVmDiagnostic>(d =>
                d.Code == GraphVmDiagnosticCodes.MissingControlEdge &&
                d.NodeId == "branch"));
        }

        [Test]
        public void Compile_ValuePinTypeMismatch_FailsFast()
        {
            var graph = new GraphVmDocument
            {
                Id = "tests.graphvm.type-mismatch",
                Entry = "one",
                Nodes = new List<GraphVmNode>
                {
                    new() { Id = "one", Op = nameof(GraphVmOpcode.ConstInt), IntValue = 1 },
                    new() { Id = "branch", Op = nameof(GraphVmOpcode.BranchBool) },
                    new() { Id = "done", Op = nameof(GraphVmOpcode.ReturnInt) }
                },
                ControlEdges = new List<GraphVmControlEdge>
                {
                    new("one", GraphVmControlPorts.Next, "branch"),
                    new("branch", GraphVmControlPorts.True, "done"),
                    new("branch", GraphVmControlPorts.False, "done")
                },
                ValueEdges = new List<GraphVmValueEdge>
                {
                    new("one", GraphVmValuePorts.Value, "branch", GraphVmValuePorts.Condition),
                    new("one", GraphVmValuePorts.Value, "done", GraphVmValuePorts.Value)
                }
            };

            GraphVmCompileResult compiled = GraphVmCompiler.Compile(graph);

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphVmDiagnostic>(d =>
                d.Code == GraphVmDiagnosticCodes.TypeMismatch &&
                d.NodeId == "branch"));
        }

        private static int Count(IReadOnlyList<GraphInstructionSource> values, string nodeId)
        {
            int count = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].NodeId == nodeId)
                {
                    count++;
                }
            }

            return count;
        }

        private static int Count(IReadOnlyList<GraphInstructionSource> values, string nodeId, string controlPort)
        {
            int count = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].NodeId == nodeId && values[i].ControlPort == controlPort)
                {
                    count++;
                }
            }

            return count;
        }

        private sealed class RecordingTraceSink : IGraphVmTraceSink
        {
            private readonly GraphInstructionSourceMap _sourceMap;

            public RecordingTraceSink(GraphInstructionSourceMap sourceMap)
            {
                _sourceMap = sourceMap;
            }

            public List<GraphInstructionSource> Sources { get; } = new();

            public void OnInstruction(in GraphVmTraceEvent traceEvent)
            {
                if (_sourceMap.TryGetSource(traceEvent.InstructionIndex, out GraphInstructionSource source))
                {
                    Sources.Add(source);
                }
            }
        }
    }
}
