using System;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
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
    }
}
