using System;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphInvokeCycleTests
    {
        [SetUp]
        public void SetUp()
        {
            GraphIdRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            GraphIdRegistry.Clear();
        }

        [Test]
        public void 自己调自己的图必须被拒绝()
        {
            const string graphKey = "Graph.Cycle.Self";
            int graphId = GraphIdRegistry.Register(graphKey);
            var programs = new GraphProgramRegistry();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                programs.Register(
                    graphId,
                    new[]
                    {
                        new GraphInstruction { Op = (ushort)GraphNodeOp.InvokeScript, Dst = 0, Imm = graphId },
                        new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
                    },
                    GraphKind.Script));

            Assert.That(ex!.Message, Does.Contain("GAS.GRAPH.ERR.InvokeCycle"));
            Assert.That(ex.Message, Does.Contain(graphKey));
            Assert.That(programs.TryGetProgram(graphId, out _), Is.False);
        }

        [Test]
        public void 绕一圈回来也算环()
        {
            int graphA = GraphIdRegistry.Register("Graph.Cycle.A");
            int graphB = GraphIdRegistry.Register("Graph.Cycle.B");
            var programs = new GraphProgramRegistry();

            programs.Register(
                graphA,
                new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.InvokeScript, Dst = 0, Imm = graphB },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
                },
                GraphKind.Script);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                programs.Register(
                    graphB,
                    new[]
                    {
                        new GraphInstruction { Op = (ushort)GraphNodeOp.InvokeScript, Dst = 0, Imm = graphA },
                        new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
                    },
                    GraphKind.Script));

            Assert.That(ex!.Message, Does.Contain("GAS.GRAPH.ERR.InvokeCycle"));
            Assert.That(ex.Message, Does.Contain("Graph.Cycle.A").Or.Contain("Graph.Cycle.B"));
            Assert.That(programs.TryGetProgram(graphA, out _), Is.True);
            Assert.That(programs.TryGetProgram(graphB, out _), Is.False);
        }

        [Test]
        public void 万一漏到了运行期也要报错而不是消失()
        {
            var programs = new GraphProgramRegistry();
            int graphCount = GraphVmLimits.MaxInvokeDepth + 2;
            int[] ids = new int[graphCount];
            for (int i = 0; i < graphCount; i++)
            {
                ids[i] = GraphIdRegistry.Register($"Graph.Depth.{i}");
            }

            for (int i = 0; i < graphCount; i++)
            {
                GraphInstruction[] program = i == graphCount - 1
                    ? new[]
                    {
                        new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
                        new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
                    }
                    : new[]
                    {
                        new GraphInstruction { Op = (ushort)GraphNodeOp.InvokeScript, Dst = 0, Imm = ids[i + 1] },
                        new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
                    };
                programs.Register(ids[i], program, GraphKind.Script);
            }

            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> iRegs = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            e[0] = caster;

            Assert.That(programs.TryGetProgram(ids[0], out ReadOnlySpan<GraphInstruction> root), Is.True);
            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                Programs = programs,
                F = f,
                I = iRegs,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
                CallStack = callStack
            };

            string? message = null;
            try
            {
                GasGraphOpHandlerTable.Execute(ref state, root, GasGraphOpHandlerTable.Instance);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain("GAS.GRAPH.ERR.InvokeDepthExceeded"));
            Assert.That(message, Does.Contain("MaxInvokeDepth"));
        }

        [Test]
        public void Execute_SharedTreeBudget_ThrowsWhenNestedInvokeWouldResetSteps()
        {
            int leafId = GraphIdRegistry.Register("Graph.Budget.Leaf");
            int rootId = GraphIdRegistry.Register("Graph.Budget.Root");
            var programs = new GraphProgramRegistry();
            programs.Register(
                leafId,
                new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = -1 }
                },
                GraphKind.Script);
            programs.Register(
                rootId,
                new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.InvokeScript, Dst = 0, Imm = leafId },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
                },
                GraphKind.Script);

            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> iRegs = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            e[0] = caster;

            Assert.That(programs.TryGetProgram(rootId, out ReadOnlySpan<GraphInstruction> root), Is.True);
            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                Programs = programs,
                F = f,
                I = iRegs,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
                CallStack = callStack,
                TreeSteps = GraphVmLimits.MaxInstructionsPerExecution - 2
            };

            string? message = null;
            try
            {
                GasGraphOpHandlerTable.Execute(ref state, root, GasGraphOpHandlerTable.Instance);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain("MaxInstructionsPerExecution"));
            Assert.That(state.TreeSteps, Is.LessThanOrEqualTo(GraphVmLimits.MaxInstructionsPerExecution));
        }

        [Test]
        public void ReplaceProgram_SelfInvoke_IsRejectedAndRolledBack()
        {
            const string graphKey = "Graph.Cycle.Hot";
            int graphId = GraphIdRegistry.Register(graphKey);
            var programs = new GraphProgramRegistry();
            GraphInstruction[] original =
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 3 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
            };
            programs.Register(graphId, original, GraphKind.Script);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                programs.ReplaceProgram(
                    graphId,
                    new[]
                    {
                        new GraphInstruction { Op = (ushort)GraphNodeOp.InvokeScript, Dst = 0, Imm = graphId },
                        new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
                    },
                    GraphKind.Script,
                    GraphInstructionSourceMap.Empty));

            Assert.That(ex!.Message, Does.Contain("GAS.GRAPH.ERR.InvokeCycle"));
            Assert.That(programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> live), Is.True);
            Assert.That(live[0].Op, Is.EqualTo((ushort)GraphNodeOp.ConstInt));
        }
    }
}
