using System;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphExecuteSliceBudgetTests
    {
        [Test]
        public void Execute_RunToHalt_ThrowsWhenBudgetExceeded()
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = -1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
            };

            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            e[0] = caster;

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
                CallStack = callStack
            };

            string? message = null;
            try
            {
                GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain("MaxInstructionsPerExecution"));
        }

        [Test]
        public void ExecuteSlice_ReturnsRunningWhenBudgetExhausted_WithoutThrowing()
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = -1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
            };

            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            e[0] = caster;

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
                CallStack = callStack
            };
            var cursor = new GraphExecutionCursor();

            GraphSliceResult result = GasGraphOpHandlerTable.ExecuteSlice(
                ref state,
                program,
                GasGraphOpHandlerTable.Instance,
                ref cursor,
                budgetSteps: 8);

            Assert.That(result.BudgetSuspended, Is.True);
            Assert.That(result.Halted, Is.False);
            Assert.That(cursor.Status, Is.EqualTo(GraphExecutionStatus.BudgetSuspended));
            Assert.That(cursor.Steps, Is.EqualTo(8));
        }

        [Test]
        public void ExecuteSlice_SelfMoveFastPathRejectsInvalidRegister()
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.MoveInt, Dst = byte.MaxValue, A = byte.MaxValue },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
            };

            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            e[0] = caster;

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
                CallStack = callStack
            };
            var cursor = new GraphExecutionCursor();

            string? message = null;
            try
            {
                GasGraphOpHandlerTable.ExecuteSlice(
                    ref state,
                    program,
                    GasGraphOpHandlerTable.Instance,
                    ref cursor,
                    budgetSteps: 8);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Does.Contain("MoveInt"));
            Assert.That(cursor.Steps, Is.EqualTo(1));
            Assert.That(state.TreeSteps, Is.EqualTo(1));
        }

        [Test]
        public void ExecuteSlice_PcOutOfRangePersistsFastPathSteps()
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 }
            };

            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            e[0] = caster;

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
                CallStack = callStack
            };
            var cursor = new GraphExecutionCursor();

            string? message = null;
            try
            {
                GasGraphOpHandlerTable.ExecuteSlice(
                    ref state,
                    program,
                    GasGraphOpHandlerTable.Instance,
                    ref cursor,
                    budgetSteps: 8);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Does.Contain(GraphKindOperationPolicy.PcOutOfRangeError));
            Assert.That(cursor.Pc, Is.EqualTo(1));
            Assert.That(cursor.Steps, Is.EqualTo(1));
            Assert.That(state.TreeSteps, Is.EqualTo(1));
        }

        [Test]
        public void ExecuteSlice_SelfMoveFastPathConsumesBudgetAndResumes()
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 9 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.MoveInt, Dst = 0, A = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.MoveInt, Dst = 0, A = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.MoveInt, Dst = 0, A = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
            };

            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            e[0] = caster;

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
                CallStack = callStack
            };
            var cursor = new GraphExecutionCursor();

            GraphSliceResult first = GasGraphOpHandlerTable.ExecuteSlice(
                ref state,
                program,
                GasGraphOpHandlerTable.Instance,
                ref cursor,
                budgetSteps: 3);

            Assert.That(first.BudgetSuspended, Is.True);
            Assert.That(cursor.Pc, Is.EqualTo(3));
            Assert.That(cursor.Steps, Is.EqualTo(3));

            GraphSliceResult second = GasGraphOpHandlerTable.ExecuteSlice(
                ref state,
                program,
                GasGraphOpHandlerTable.Instance,
                ref cursor,
                budgetSteps: 8);

            Assert.That(second.Halted, Is.True);
            Assert.That(second.ReturnInt, Is.EqualTo(9));
            Assert.That(cursor.Pc, Is.EqualTo(program.Length));
            Assert.That(cursor.Steps, Is.EqualTo(5));
        }

        [Test]
        public void ExecuteScriptSlice_ResumeKeepsResolvedGroundPoint()
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 4, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadTargetPosX, Dst = 5 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadTargetPosY, Dst = 6 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 5 }
            };

            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var cursor = new GraphExecutionCursor();
            var ground = new IntVector2(1960, 680);

            GraphSliceResult first = GraphExecutor.ExecuteScriptSlice(
                world, caster, Entity.Null, ground, program, api: null!, programs: null,
                floats, ints, bools, entities, targets, callStack, ref cursor, budgetSteps: 1);
            Assert.That(first.BudgetSuspended, Is.True);
            Assert.That(cursor.TargetPosCm, Is.EqualTo(ground));

            GraphSliceResult second = GraphExecutor.ExecuteScriptSlice(
                world, caster, Entity.Null, default, program, api: null!, programs: null,
                floats, ints, bools, entities, targets, callStack, ref cursor, budgetSteps: 8);
            Assert.That(second.Halted, Is.True);
            Assert.That(second.ReturnInt, Is.EqualTo(1960));
            Assert.That(ints[5], Is.EqualTo(1960));
            Assert.That(ints[6], Is.EqualTo(680));
        }

        [Test]
        public void ExecuteSlice_LinearPath_SliceStepsEqualCountLongest()
        {
            GraphInstruction[] program = Linear(4);
            GraphSliceResult result = RunToHalt(program);
            Assert.That(result.Halted, Is.True);
            Assert.That(result.SliceSteps, Is.EqualTo(TriggerGraphSliceBudget.CountLongest(program, 0)));
            Assert.That(result.SliceSteps, Is.EqualTo(result.Steps));
        }

        [Test]
        public void ExecuteSlice_JumpIfFalse_LongerArmMatchesCountLongest()
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.JumpIfFalse, A = 0, Imm = 2 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };

            int longest = TriggerGraphSliceBudget.CountLongest(program, 0);
            GraphSliceResult taken = RunToHalt(program, jumpTrue: true);
            GraphSliceResult skipped = RunToHalt(program, jumpTrue: false);
            Assert.That(taken.SliceSteps, Is.EqualTo(3));
            Assert.That(skipped.SliceSteps, Is.EqualTo(2));
            Assert.That(longest, Is.EqualTo(taken.SliceSteps));
            Assert.That(SliceStepProbe.Measure(program, 0), Is.EqualTo(longest));
        }

        [Test]
        public void ExecuteSlice_CallAndReturn_StayInsideTheSlice()
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.Call, Imm = 2 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 7 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.Return },
            };

            GraphSliceResult result = RunToHalt(program);
            Assert.That(result.Halted, Is.True);
            Assert.That(result.ReturnInt, Is.EqualTo(7));
            Assert.That(result.SliceSteps, Is.EqualTo(4));
            Assert.That(result.SliceSteps, Is.EqualTo(TriggerGraphSliceBudget.CountLongest(program, 0)));
            Assert.That(result.Steps, Is.EqualTo(result.SliceSteps));
        }

        [Test]
        public void ExecuteSlice_InvokeGraph_ChargesOneSliceStep()
        {
            const int childSteps = 11;
            var child = Linear(childSteps - 1);
            var programs = new GraphProgramRegistry();
            int childId = GraphIdRegistry.Register("graph.probe.slice.invoke.child");
            programs.Register(
                childId,
                child,
                GraphKind.TriggerGraph,
                GraphInstructionSourceMap.Empty,
                Array.Empty<string>(),
                new[] { new TriggerGraphEntry("main", "MapLoaded", 0, false) });

            var host = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 1, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.InvokeGraph, Dst = 0, Imm = childId },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };

            GraphSliceResult result = RunToHalt(host, programs: programs);
            int longest = TriggerGraphSliceBudget.CountLongest(host, 0);
            Assert.That(result.Halted, Is.True);
            Assert.That(longest, Is.EqualTo(3));
            Assert.That(result.SliceSteps, Is.EqualTo(longest));
            Assert.That(result.Steps - result.SliceSteps, Is.EqualTo(childSteps));
            Assert.That(SliceStepProbe.Measure(host, 0), Is.EqualTo(longest));
        }

        [Test]
        public void SingleSlice_AtBudget_Registers_OverBudget_NamesGraphEntryCountAndBudget()
        {
            var within = new GraphProgramRegistry();
            int withinId = GraphIdRegistry.Register("graph.probe.slice.within");
            GraphInstruction[] withinProgram = Linear(TriggerGraphSliceBudget.SingleSliceSteps - 1);
            within.Register(
                withinId,
                withinProgram,
                GraphKind.TriggerGraph,
                GraphInstructionSourceMap.Empty,
                Array.Empty<string>(),
                new[] { SingleSliceEntry("within") });
            Assert.That(
                TriggerGraphSliceBudget.CountLongest(withinProgram, 0),
                Is.EqualTo(TriggerGraphSliceBudget.SingleSliceSteps));

            var over = new GraphProgramRegistry();
            int overId = GraphIdRegistry.Register("graph.probe.slice.over");
            GraphInstruction[] overProgram = Linear(TriggerGraphSliceBudget.SingleSliceSteps);
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => over.Register(
                overId,
                overProgram,
                GraphKind.TriggerGraph,
                GraphInstructionSourceMap.Empty,
                Array.Empty<string>(),
                new[] { SingleSliceEntry("over") }));
            Assert.That(error!.Message, Does.Contain("LUDOTS_TRIGGER_GRAPH_SINGLE_SLICE"));
            Assert.That(error.Message, Does.Contain("graph.probe.slice.over"));
            Assert.That(error.Message, Does.Contain("over"));
            Assert.That(error.Message, Does.Contain((TriggerGraphSliceBudget.SingleSliceSteps + 1).ToString()));
            Assert.That(error.Message, Does.Contain(TriggerGraphEntry.BudgetSingleSlice));
            Assert.That(error.Message, Does.Contain(TriggerGraphSliceBudget.SingleSliceSteps.ToString()));
        }

        [Test]
        public void Compile_UnknownEntryBudget_FailsClosed()
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "graph.probe.slice.budget.token",
                Kind = "TriggerGraph",
                Entries = new List<TriggerGraphEntryConfig>
                {
                    new() { Label = "go", Event = "MapLoaded", Start = "halt", Budget = "two-ticks" },
                },
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "halt", Op = "HaltReturnInt" },
                },
            };

            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc);
            Assert.That(
                result.Diagnostics.Any(d => d.Severity == GraphDiagnosticSeverity.Error && d.Message.Contains("budget", StringComparison.Ordinal)),
                Is.True,
                () => string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        }

        private static GraphInstruction[] Linear(int constCount)
        {
            var program = new GraphInstruction[constCount + 1];
            for (int i = 0; i < constCount; i++)
            {
                program[i] = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = i };
            }

            program[constCount] = new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 };
            return program;
        }

        private static TriggerGraphEntry SingleSliceEntry(string label)
        {
            return new TriggerGraphEntry(
                label,
                "MapLoaded",
                0,
                false,
                default,
                TriggerGraphEntry.RefireIgnore,
                budget: TriggerGraphEntry.BudgetSingleSlice);
        }

        private static GraphSliceResult RunToHalt(
            GraphInstruction[] program,
            GraphProgramRegistry? programs = null,
            bool? jumpTrue = null)
        {
            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            e[0] = caster;
            if (jumpTrue.HasValue)
            {
                b[0] = (byte)(jumpTrue.Value ? 1 : 0);
            }

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                Programs = programs,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
                CallStack = callStack,
            };
            var cursor = new GraphExecutionCursor();
            return GasGraphOpHandlerTable.ExecuteSlice(
                ref state,
                program,
                GasGraphOpHandlerTable.Instance,
                ref cursor,
                budgetSteps: TriggerGraphSliceBudget.SingleSliceSteps);
        }
    }
}
