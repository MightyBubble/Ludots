using System;
using Arch.Core;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.AI
{
    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class GraphFrameFrontDoorTests
    {
        [Test]
        public void 效果图绑到行为树叶子必须失败并说明种类不对()
        {
            var programs = new GraphProgramRegistry();
            const int effectGraphId = 9101;
            programs.Register(
                effectGraphId,
                new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 1f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
                },
                GraphKind.Effect);

            var tree = new BehaviorTreeDefinition(
                "bt.effect-on-leaf",
                new[]
                {
                    new BehaviorTreeNode(
                        BehaviorTreeNodeKind.Action,
                        0,
                        0,
                        BehaviorTreeLeafBinding.ScriptSlice,
                        effectGraphId)
                },
                rootIndex: 0);
            var world = new BehaviorTreeWorld(tree, 1);
            world.AddAgent();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                world.TickAll(programs, 32, sensors: null));
            Assert.That(ex!.Message, Does.Contain(GraphKindOperationPolicy.KindMismatchError));
            Assert.That(ex.Message, Does.Contain("Effect"));
            Assert.That(ex.Message, Does.Contain("行为树叶子"));
            Assert.That(ex.Message, Does.Contain("Script"));
        }

        [Test]
        public void 跳到程序外的跳转登记必须失败并指出这个跳转()
        {
            var programs = new GraphProgramRegistry();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                programs.Register(
                    9102,
                    new[]
                    {
                        new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 8 },
                        new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
                    },
                    GraphKind.Script));

            Assert.That(ex!.Message, Does.Contain(GraphKindOperationPolicy.JumpOutOfRangeError));
            Assert.That(ex.Message, Does.Contain("instructionIndex=0"));
            Assert.That(ex.Message, Does.Contain("跳到了程序外面"));
            Assert.That(programs.TryGetProgram(9102, out _), Is.False);
        }

        [Test]
        public void 预算耗尽后从断点续跑且已产生的副作用不重放()
        {
            var programs = new GraphProgramRegistry();
            const int graphId = 9103;
            programs.Register(
                graphId,
                new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 1, Imm = 7 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.MoveInt, Dst = 1, A = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.MoveInt, Dst = 1, A = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 }
                },
                GraphKind.Script);

            var tree = new BehaviorTreeDefinition(
                "bt.budget-resume",
                new[]
                {
                    new BehaviorTreeNode(
                        BehaviorTreeNodeKind.Action,
                        0,
                        0,
                        BehaviorTreeLeafBinding.ScriptSlice,
                        graphId)
                },
                rootIndex: 0);
            var world = new BehaviorTreeWorld(tree, 1);
            world.AddAgent();
            var sensors = new CountingSensors();

            world.TickAll(programs, scriptBudgetSteps: 1, sensors);
            Assert.That(world.Statuses[0], Is.EqualTo(BehaviorTreeStatus.Running));
            Assert.That(sensors.Calls, Is.EqualTo(1));

            world.TickAll(programs, scriptBudgetSteps: 8, sensors);
            Assert.That(world.Statuses[0], Is.EqualTo(BehaviorTreeStatus.Success));
            Assert.That(world.LastScriptReturns[0], Is.EqualTo(7));
            Assert.That(sensors.Calls, Is.EqualTo(1));
        }

        [Test]
        public void 掉出程序尾部不再算成功()
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 }
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
                Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
                Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
                Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
                Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
                Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
                var cursor = new GraphExecutionCursor();
                GraphExecutor.ExecuteScriptSlice(
                    world: null!,
                    caster: default,
                    explicitTarget: default,
                    targetPosCm: default,
                    program,
                    api: null,
                    programs: null,
                    floats,
                    ints,
                    bools,
                    entities,
                    targets,
                    callStack,
                    ref cursor,
                    budgetSteps: 8);
            });

            Assert.That(ex!.Message, Does.Contain(GraphKindOperationPolicy.PcOutOfRangeError));
            Assert.That(ex.Message, Does.Contain("HaltReturnInt"));
        }

        private sealed class CountingSensors : IBehaviorTreeSensorFeed
        {
            public int Calls { get; private set; }

            public void WriteSensors(int agentIndex, int graphId, Span<int> ints, Span<byte> bools)
            {
                Calls++;
            }
        }
    }
}
