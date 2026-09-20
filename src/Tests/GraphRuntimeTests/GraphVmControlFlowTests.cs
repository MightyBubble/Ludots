using System.Collections.Generic;
using System.Linq;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
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
            var registeredProgram = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt }
            };
            var registeredSourceMap = new GraphInstructionSourceMap(
                graph.Id,
                new[] { new GraphInstructionSource(graph.Id, "done", nameof(GraphNodeOp.HaltReturnInt)) });
            registry.Register(7, registeredProgram, GraphKind.Effect, registeredSourceMap);

            Assert.That(registry.TryGetProgram(7, out var registeredView), Is.True);
            Assert.That(registry.TryGetKind(7, out GraphKind kind), Is.True);
            Assert.That(kind, Is.EqualTo(GraphKind.Effect));
            Assert.That(registry.RequireKind(7, GraphKind.Effect), Is.EqualTo(GraphKind.Effect));
            Assert.That(registry.TryGetSourceMap(7, out GraphInstructionSourceMap registeredMap), Is.True);
            Assert.That(registeredView.Length, Is.EqualTo(registeredProgram.Length));
            Assert.That(registeredMap.GraphId, Is.EqualTo(graph.Id));

            Span<int> ints = stackalloc int[GraphVmRuntimeLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmRuntimeLimits.MaxBoolRegisters];
            Span<int> callStack = stackalloc int[GraphVmRuntimeLimits.MaxCallStackDepth];
            var cursor = new GraphVmExecutionCursor();
            var trace = new RecordingTraceSink(compiled.SourceMap);

            GraphVmExecutionResult first = GraphVmExecutor.ExecuteSlice(compiled.Program, ints, bools, callStack, ref cursor, 64, trace);
            Assert.That(first.Yielded, Is.True);
            Assert.That(cursor.CallStackCount, Is.EqualTo(1));

            GraphVmExecutionResult second = GraphVmExecutor.ExecuteSlice(compiled.Program, ints, bools, callStack, ref cursor, 64, trace);
            Assert.That(second.Yielded, Is.True);
            Assert.That(cursor.CallStackCount, Is.EqualTo(1));

            GraphVmExecutionResult third = GraphVmExecutor.ExecuteSlice(compiled.Program, ints, bools, callStack, ref cursor, 64, trace);
            Assert.That(third.Yielded, Is.True);
            Assert.That(cursor.CallStackCount, Is.EqualTo(1));

            GraphVmExecutionResult fourth = GraphVmExecutor.ExecuteSlice(compiled.Program, ints, bools, callStack, ref cursor, 64, trace);

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
        public void ExecuteSlice_NonTerminatingGraph_ExceedsBetweenYieldsBudget_FailsClosed()
        {
            var graph = new GraphVmDocument
            {
                Id = "tests.graphvm.non-terminating",
                Entry = "loop",
                Nodes = new List<GraphVmNode>
                {
                    new() { Id = "loop", Op = nameof(GraphVmOpcode.Jump) }
                },
                ControlEdges = new List<GraphVmControlEdge>
                {
                    new("loop", GraphVmControlPorts.Target, "loop")
                }
            };

            GraphVmCompileResult compiled = GraphVmCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.True, GraphVmTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            Span<int> ints = stackalloc int[GraphVmRuntimeLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmRuntimeLimits.MaxBoolRegisters];
            Span<int> callStack = stackalloc int[GraphVmRuntimeLimits.MaxCallStackDepth];
            var cursor = new GraphVmExecutionCursor();

            InvalidOperationException? budgetFailure = null;
            try
            {
                GraphVmExecutionResult result;
                do
                {
                    result = GraphVmExecutor.ExecuteSlice(
                        compiled.Program,
                        ints,
                        bools,
                        callStack,
                        ref cursor,
                        GraphVmRuntimeLimits.MaxInstructionsPerExecution);
                }
                while (!result.Halted);
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains(nameof(GraphVmRuntimeLimits.MaxInstructionsBetweenYields)))
            {
                budgetFailure = ex;
            }

            Assert.That(budgetFailure, Is.Not.Null,
                "a non-terminating segment must fail closed instead of being resumed forever.");
            Assert.That(cursor.Steps, Is.EqualTo(GraphVmRuntimeLimits.MaxInstructionsBetweenYields),
                "the lifetime counter must stop exactly at the budget that failed.");
            Assert.That(cursor.StepsSinceYield, Is.EqualTo(GraphVmRuntimeLimits.MaxInstructionsBetweenYields),
                "without any Yield the segment counter tracks the lifetime counter exactly.");
        }

        [Test]
        public void ExecuteSlice_LongLivedYieldLoop_TotalStepsExceedBetweenYieldsBudget_EveryResumeYields()
        {
            // pump (Yield) -> back (Jump -> pump): the program yields once per
            // scheduler slice and never halts. This is a legitimate long-lived
            // coroutine: total executed steps across resumptions exceed
            // MaxInstructionsBetweenYields many times over, yet every resume
            // must continue yielding instead of being killed as non-terminating.
            var graph = new GraphVmDocument
            {
                Id = "tests.graphvm.long-lived-yield",
                Entry = "pump",
                Nodes = new List<GraphVmNode>
                {
                    new() { Id = "pump", Op = nameof(GraphVmOpcode.Yield) },
                    new() { Id = "back", Op = nameof(GraphVmOpcode.Jump) }
                },
                ControlEdges = new List<GraphVmControlEdge>
                {
                    new("pump", GraphVmControlPorts.Next, "back"),
                    new("back", GraphVmControlPorts.Target, "pump")
                }
            };

            GraphVmCompileResult compiled = GraphVmCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.True, GraphVmTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            Span<int> ints = stackalloc int[GraphVmRuntimeLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmRuntimeLimits.MaxBoolRegisters];
            Span<int> callStack = stackalloc int[GraphVmRuntimeLimits.MaxCallStackDepth];
            var cursor = new GraphVmExecutionCursor();

            int resumptions = GraphVmRuntimeLimits.MaxInstructionsBetweenYields + 64;
            int previousSteps = 0;
            for (int i = 0; i < resumptions; i++)
            {
                GraphVmExecutionResult result = GraphVmExecutor.ExecuteSlice(
                    compiled.Program,
                    ints,
                    bools,
                    callStack,
                    ref cursor,
                    GraphVmRuntimeLimits.MaxInstructionsPerExecution);

                Assert.That(result.Yielded, Is.True, $"resumption {i} must yield and continue.");
                Assert.That(result.Steps, Is.GreaterThan(previousSteps),
                    $"resumption {i} must report a cumulative, monotonic lifetime step count.");
                previousSteps = result.Steps;
            }

            // Every resume yields once, yet the lifetime counter exceeds the
            // between-yields budget many times over: the segment budget must
            // never cap a coroutine that yields every slice.
            Assert.That(previousSteps, Is.GreaterThan(GraphVmRuntimeLimits.MaxInstructionsBetweenYields),
                "the lifetime counter must exceed the between-yields budget while every resume yields.");
            Assert.That(cursor.Steps, Is.EqualTo(previousSteps),
                "the lifetime counter must match the last reported result steps.");
            Assert.That(cursor.StepsSinceYield, Is.EqualTo(0),
                "the between-yields budget counter must reset at every Yield.");
        }

        [Test]
        public void ExecuteSlice_YieldResumeHalt_ResultStepsAndTraceStepsAreCumulativeAndMonotonic()
        {
            GraphVmDocument graph = GraphVmTestGraphs.CreateDrinkUntilFullGraph();

            GraphVmCompileResult compiled = GraphVmCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.True, GraphVmTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            Span<int> ints = stackalloc int[GraphVmRuntimeLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmRuntimeLimits.MaxBoolRegisters];
            Span<int> callStack = stackalloc int[GraphVmRuntimeLimits.MaxCallStackDepth];
            var cursor = new GraphVmExecutionCursor();
            var trace = new RecordingTraceSink(GraphInstructionSourceMap.Empty);

            GraphVmExecutionResult first = GraphVmExecutor.ExecuteSlice(compiled.Program, ints, bools, callStack, ref cursor, 64, trace);
            Assert.That(first.Yielded, Is.True);

            GraphVmExecutionResult second = GraphVmExecutor.ExecuteSlice(compiled.Program, ints, bools, callStack, ref cursor, 64, trace);
            Assert.That(second.Yielded, Is.True);

            GraphVmExecutionResult third = GraphVmExecutor.ExecuteSlice(compiled.Program, ints, bools, callStack, ref cursor, 64, trace);
            Assert.That(third.Yielded, Is.True);

            GraphVmExecutionResult fourth = GraphVmExecutor.ExecuteSlice(compiled.Program, ints, bools, callStack, ref cursor, 64, trace);
            Assert.That(fourth.Halted, Is.True);

            // Result steps are lifetime-cumulative and strictly monotonic
            // across yield/resume/halt.
            Assert.That(first.Steps, Is.LessThan(second.Steps));
            Assert.That(second.Steps, Is.LessThan(third.Steps));
            Assert.That(third.Steps, Is.LessThan(fourth.Steps));
            Assert.That(cursor.Steps, Is.EqualTo(fourth.Steps),
                "the halt result must report the full lifetime cumulative step count.");

            // Every executed instruction is traced exactly once, and trace
            // steps are cumulative and strictly increasing across slices.
            Assert.That(trace.Steps.Count, Is.EqualTo(fourth.Steps),
                "each executed instruction must be traced exactly once.");
            for (int i = 1; i < trace.Steps.Count; i++)
            {
                Assert.That(trace.Steps[i], Is.GreaterThan(trace.Steps[i - 1]),
                    $"trace step {i} must be strictly increasing (cumulative).");
            }
        }

        [Test]
        public void Execute_ProgramThatYields_ThrowsWithExecuteSliceGuidance()
        {
            var graph = new GraphVmDocument
            {
                Id = "tests.graphvm.execute-yield",
                Entry = "one",
                Nodes = new List<GraphVmNode>
                {
                    new() { Id = "one", Op = nameof(GraphVmOpcode.ConstInt), IntValue = 1 },
                    new() { Id = "pump", Op = nameof(GraphVmOpcode.Yield) },
                    new() { Id = "done", Op = nameof(GraphVmOpcode.ReturnInt) }
                },
                ControlEdges = new List<GraphVmControlEdge>
                {
                    new("one", GraphVmControlPorts.Next, "pump"),
                    new("pump", GraphVmControlPorts.Next, "done")
                },
                ValueEdges = new List<GraphVmValueEdge>
                {
                    new("one", GraphVmValuePorts.Value, "done", GraphVmValuePorts.Value)
                }
            };

            GraphVmCompileResult compiled = GraphVmCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.True, GraphVmTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            Assert.That(
                () => GraphVmExecutor.Execute(compiled.Program),
                Throws.InvalidOperationException.With.Message.Contains(nameof(GraphVmExecutor.ExecuteSlice)));
        }

        [Test]
        public void ExecuteSlice_CorruptedReturnAddressOnCallStack_FailsClosed()
        {
            // A Return pops its target straight off the caller-provided call
            // stack. A corrupted slot (e.g. wrong entity's stack, stale memory)
            // must be validated like any control target and fail closed instead
            // of silently running with an out-of-bounds pc.
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphVmOpcode.Return }
            };

            Span<int> ints = stackalloc int[GraphVmRuntimeLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmRuntimeLimits.MaxBoolRegisters];
            Span<int> callStack = stackalloc int[GraphVmRuntimeLimits.MaxCallStackDepth];
            callStack[0] = int.MaxValue;
            var cursor = new GraphVmExecutionCursor { CallStackCount = 1 };

            InvalidOperationException? failure = null;
            try
            {
                GraphVmExecutor.ExecuteSlice(program, ints, bools, callStack, ref cursor, 64);
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains("jump target out of range"))
            {
                failure = ex;
            }

            Assert.That(failure, Is.Not.Null,
                "a corrupted return address must fail closed instead of running with an out-of-bounds pc.");
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

            /// <summary>Every trace event's cumulative step, regardless of source-map resolution.</summary>
            public List<int> Steps { get; } = new();

            public void OnInstruction(in GraphVmTraceEvent traceEvent)
            {
                Steps.Add(traceEvent.Step);
                if (_sourceMap.TryGetSource(traceEvent.InstructionIndex, out GraphInstructionSource source))
                {
                    Sources.Add(source);
                }
            }
        }
    }
}
