using System;
using Ludots.Core.GraphRuntime;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Graph;

[TestFixture]
public sealed class GraphRuntimeExecutorTests
{
    [Test]
    public void Execute_RunsRegisteredHandlers()
    {
        var state = new TestState();
        var program = new[]
        {
            Instruction(TestOp.Set, imm: 2),
            Instruction(TestOp.Add, imm: 3),
            default,
            Instruction(TestOp.Add, imm: 5)
        };

        GraphExecutor.Execute(ref state, program, TestHandlerTable.Instance);

        Assert.That(state.Value, Is.EqualTo(10));
        Assert.That(state.Hits, Is.EqualTo(3));
    }

    [Test]
    public void Execute_AllowsHandlerControlledJump()
    {
        var state = new TestState();
        var program = new[]
        {
            Instruction(TestOp.Set, imm: 1),
            Instruction(TestOp.Jump, imm: 1),
            Instruction(TestOp.Set, imm: 100),
            Instruction(TestOp.Add, imm: 2)
        };

        GraphExecutor.Execute(ref state, program, TestHandlerTable.Instance);

        Assert.That(state.Value, Is.EqualTo(3));
        Assert.That(state.Hits, Is.EqualTo(3));
    }

    [Test]
    public void Execute_FailsFastOnMissingHandler()
    {
        var state = new TestState();
        var program = new[] { Instruction(TestOp.Unregistered) };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => GraphExecutor.Execute(ref state, program, TestHandlerTable.Instance))!;

        Assert.That(ex.Message, Does.Contain("No handler registered"));
    }

    [Test]
    public void Execute_FailsFastOnRunawayProgram()
    {
        var state = new TestState();
        var program = new[] { Instruction(TestOp.Jump, imm: -1) };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => GraphExecutor.Execute(ref state, program, TestHandlerTable.Instance, maxInstructions: 4))!;

        Assert.That(ex.Message, Does.Contain("exceeded MaxInstructionsPerExecution"));
    }

    [Test]
    public void Execute_FailsFastWhenHandlerMovesPcOutOfRange()
    {
        var state = new TestState();
        var program = new[] { Instruction(TestOp.Jump, imm: -2) };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => GraphExecutor.Execute(ref state, program, TestHandlerTable.Instance))!;

        Assert.That(ex.Message, Does.Contain("outside 0.."));
    }

    private static GraphInstruction Instruction(TestOp op, int imm = 0) =>
        new()
        {
            Op = (ushort)op,
            Imm = imm
        };

    private enum TestOp : ushort
    {
        Set = 1,
        Add = 2,
        Jump = 3,
        Unregistered = 4
    }

    private struct TestState
    {
        public int Value;
        public int Hits;
    }

    private sealed class TestHandlerTable : IOpHandlerTable<TestState>
    {
        public static readonly TestHandlerTable Instance = new();

        public GraphOpHandler<TestState>[] Handlers { get; }

        private TestHandlerTable()
        {
            var handlers = new GraphOpHandler<TestState>[8];
            handlers[(ushort)TestOp.Set] = Set;
            handlers[(ushort)TestOp.Add] = Add;
            handlers[(ushort)TestOp.Jump] = Jump;
            Handlers = handlers;
        }

        private static void Set(ref TestState state, in GraphInstruction instruction, ref int pc)
        {
            state.Value = instruction.Imm;
            state.Hits++;
        }

        private static void Add(ref TestState state, in GraphInstruction instruction, ref int pc)
        {
            state.Value += instruction.Imm;
            state.Hits++;
        }

        private static void Jump(ref TestState state, in GraphInstruction instruction, ref int pc)
        {
            pc += instruction.Imm;
            state.Hits++;
        }
    }
}
