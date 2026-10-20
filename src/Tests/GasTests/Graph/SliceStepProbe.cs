using System;
using System.Collections.Generic;
using System.Text;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// Longest host path measured by <see cref="GasGraphOpHandlerTable.ExecuteSlice"/>.
    /// Gameplay ops become ConstInt: a handler cannot add to the slice counter, so each
    /// of them costs one step the same way. InvokeGraph stays an invoke, aimed at a
    /// halt stub, because the callee's steps land on the lifetime counter only.
    /// </summary>
    public static class SliceStepProbe
    {
        private const int StubInstructionCount = 4;

        public static int Measure(GraphInstruction[] program, int startPc)
        {
            if (program == null) throw new ArgumentNullException(nameof(program));
            GraphInstruction[] rewritten = (GraphInstruction[])program.Clone();
            var programs = new GraphProgramRegistry();
            int stubId = RegisterStub(programs);
            for (int i = 0; i < rewritten.Length; i++)
            {
                GraphNodeOp op = (GraphNodeOp)rewritten[i].Op;
                if (IsControl(op))
                {
                    continue;
                }

                if (op == GraphNodeOp.InvokeGraph)
                {
                    rewritten[i] = new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.InvokeGraph,
                        Dst = 0,
                        Imm = stubId,
                    };
                    continue;
                }

                rewritten[i] = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 0 };
            }

            using var world = World.Create();
            Entity caster = world.Create();
            var returns = new int[GraphVmLimits.MaxCallStackDepth];
            var memo = new Dictionary<string, int>(StringComparer.Ordinal);
            var stack = new HashSet<string>(StringComparer.Ordinal);
            var key = new StringBuilder();
            return Walk(startPc, 0);

            int Walk(int pc, int depth)
            {
                if ((uint)pc >= (uint)rewritten.Length)
                {
                    return 0;
                }

                string id = Key(pc, depth);
                if (memo.TryGetValue(id, out int cached))
                {
                    return cached;
                }

                if (!stack.Add(id))
                {
                    throw new InvalidOperationException($"slice probe cycles at pc {pc}.");
                }

                GraphInstruction ins = rewritten[pc];
                GraphNodeOp op = (GraphNodeOp)ins.Op;
                int rest = op switch
                {
                    GraphNodeOp.HaltReturnInt => Halt(pc, depth),
                    GraphNodeOp.Fail => Fail(pc, depth),
                    GraphNodeOp.Jump => Jump(pc, ins.Imm, depth),
                    GraphNodeOp.JumpIfFalse => Branch(pc, ins, depth),
                    GraphNodeOp.Call => Call(pc, ins.Imm, depth),
                    GraphNodeOp.Return => Return(pc, depth),
                    GraphNodeOp.Yield or GraphNodeOp.AwaitCallback =>
                        throw new InvalidOperationException($"slice probe reaches {op} at pc {pc}."),
                    GraphNodeOp.InvokeGraph => Invoke(pc, depth),
                    _ => FallThrough(pc, depth),
                };
                int total = 1 + rest;
                stack.Remove(id);
                memo[id] = total;
                return total;
            }

            int Halt(int pc, int depth)
            {
                Step step = Once(pc, depth, 0);
                Assert.That(step.Halted, Is.True, $"halt at pc {pc}");
                Assert.That(step.SliceSteps, Is.EqualTo(1), $"halt at pc {pc}");
                return 0;
            }

            int Fail(int pc, int depth)
            {
                InvalidOperationException? error = null;
                try
                {
                    Once(pc, depth, 0);
                }
                catch (InvalidOperationException ex)
                {
                    error = ex;
                }

                Assert.That(error, Is.Not.Null, $"Fail at pc {pc} must end the slice");
                return 0;
            }

            int Jump(int pc, int imm, int depth)
            {
                Step step = Once(pc, depth, 0);
                Assert.That(step.SliceSteps, Is.EqualTo(1));
                Assert.That(step.Pc, Is.EqualTo(pc + 1 + imm), $"jump at pc {pc}");
                return Walk(pc + 1 + imm, depth);
            }

            int Branch(int pc, GraphInstruction ins, int depth)
            {
                Step whenTrue = Once(pc, depth, 1);
                Step whenFalse = Once(pc, depth, 0);
                Assert.That(whenTrue.SliceSteps, Is.EqualTo(1));
                Assert.That(whenFalse.SliceSteps, Is.EqualTo(1));
                Assert.That(whenTrue.Pc, Is.EqualTo(pc + 1), $"JumpIfFalse true arm at pc {pc}");
                Assert.That(whenFalse.Pc, Is.EqualTo(pc + 1 + ins.Imm), $"JumpIfFalse false arm at pc {pc}");
                return Math.Max(Walk(pc + 1, depth), Walk(pc + 1 + ins.Imm, depth));
            }

            int Call(int pc, int target, int depth)
            {
                Step step = Once(pc, depth, 0);
                Assert.That(step.SliceSteps, Is.EqualTo(1));
                Assert.That(step.Pc, Is.EqualTo(target), $"call at pc {pc}");
                Assert.That(step.CallStackCount, Is.EqualTo(depth + 1));
                Assert.That(step.PushedReturn, Is.EqualTo(pc + 1));
                returns[depth] = pc + 1;
                return Walk(target, depth + 1);
            }

            int Return(int pc, int depth)
            {
                Assert.That(depth, Is.GreaterThan(0), $"return at pc {pc}");
                Step step = Once(pc, depth, 0);
                Assert.That(step.SliceSteps, Is.EqualTo(1));
                Assert.That(step.Pc, Is.EqualTo(returns[depth - 1]));
                Assert.That(step.CallStackCount, Is.EqualTo(depth - 1));
                return Walk(returns[depth - 1], depth - 1);
            }

            int Invoke(int pc, int depth)
            {
                Step step = Once(pc, depth, 0);
                Assert.That(step.SliceSteps, Is.EqualTo(1), $"invoke at pc {pc}");
                Assert.That(step.Pc, Is.EqualTo(pc + 1));
                Assert.That(step.Steps - step.SliceSteps, Is.EqualTo(StubInstructionCount), $"invoke at pc {pc}");
                return Walk(pc + 1, depth);
            }

            int FallThrough(int pc, int depth)
            {
                Step step = Once(pc, depth, 0);
                Assert.That(step.SliceSteps, Is.EqualTo(1), $"pc {pc}");
                Assert.That(step.Halted, Is.False);
                Assert.That(step.Pc, Is.EqualTo(pc + 1));
                return Walk(pc + 1, depth);
            }

            Step Once(int pc, int depth, byte jumpBool)
            {
                Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
                Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
                Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
                Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
                Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
                Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
                e[0] = caster;
                for (int n = 0; n < depth; n++)
                {
                    callStack[n] = returns[n];
                }

                GraphInstruction ins = rewritten[pc];
                if ((GraphNodeOp)ins.Op == GraphNodeOp.JumpIfFalse)
                {
                    b[ins.A] = jumpBool;
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
                var cursor = new GraphExecutionCursor(pc)
                {
                    CallStackCount = depth,
                };
                GraphSliceResult result = GasGraphOpHandlerTable.ExecuteSlice(
                    ref state,
                    rewritten,
                    GasGraphOpHandlerTable.Instance,
                    ref cursor,
                    budgetSteps: 1);
                int pushed = depth < callStack.Length ? callStack[depth] : 0;
                return new Step(result.SliceSteps, cursor.Steps, cursor.Pc, cursor.CallStackCount, pushed, result.Halted);
            }

            string Key(int pc, int depth)
            {
                key.Clear();
                key.Append(pc);
                for (int n = 0; n < depth; n++)
                {
                    key.Append(',').Append(returns[n]);
                }

                return key.ToString();
            }
        }

        private static int RegisterStub(GraphProgramRegistry programs)
        {
            var stub = new GraphInstruction[StubInstructionCount];
            for (int i = 0; i < StubInstructionCount - 1; i++)
            {
                stub[i] = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = i };
            }

            stub[StubInstructionCount - 1] = new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 };
            int stubId = GraphIdRegistry.Register("graph.probe.slice.invoke.stub");
            programs.Register(
                stubId,
                stub,
                GraphKind.TriggerGraph,
                GraphInstructionSourceMap.Empty,
                Array.Empty<string>(),
                new[] { new TriggerGraphEntry("main", "MapLoaded", 0, false) });
            return stubId;
        }

        private static bool IsControl(GraphNodeOp op)
        {
            return op is GraphNodeOp.Jump
                or GraphNodeOp.JumpIfFalse
                or GraphNodeOp.Call
                or GraphNodeOp.Return
                or GraphNodeOp.HaltReturnInt
                or GraphNodeOp.Fail
                or GraphNodeOp.Yield
                or GraphNodeOp.AwaitCallback;
        }

        private readonly record struct Step(int SliceSteps, int Steps, int Pc, int CallStackCount, int PushedReturn, bool Halted);
    }
}
