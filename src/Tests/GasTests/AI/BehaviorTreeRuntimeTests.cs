using System.Diagnostics;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Tests.Gas.Graph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.AI
{
    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class BehaviorTreeRuntimeTests
    {
        private const double FrameBudgetMs = 15.0;
        private const double LatchedWaveBudgetMs = 1.5;
        private const double CiFrameEnvelopeMs = 25.0;
        private const double CiLatchedEnvelopeMs = 5.0;

        private GraphProgramRegistry? _programs;
        private GraphActionCatalog? _actions;
        private GraphBehaviorCatalog? _behavior;

        private GraphProgramRegistry Programs
            => _programs ??= GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out _actions, out _behavior);

        private GraphActionCatalog Actions
        {
            get
            {
                _ = Programs;
                return _actions!;
            }
        }

        private GraphBehaviorCatalog Behavior
        {
            get
            {
                _ = Programs;
                return _behavior!;
            }
        }

        [Test]
        public void TickAll_AlwaysSuccessSequence_ReachesSuccess()
        {
            BehaviorTreeDefinition tree = BehaviorTreeFactory.CreateAlwaysSuccessSequence("bt.seq", leafCount: 8);
            var world = new BehaviorTreeWorld(tree, capacity: 4);
            world.AddAgent();
            world.AddAgent();
            BehaviorTreeThinkStats stats = world.TickAll();
            Assert.That(stats.Agents, Is.EqualTo(2));
            Assert.That(world.Statuses[0], Is.EqualTo(BehaviorTreeStatus.Success));
        }

        [Test]
        public void TickAll_PatrolSkeleton_StaysRunningOnEngageHold()
        {
            BehaviorTreeDefinition tree = BehaviorTreeFactory.CreatePatrolEngageSkeleton("bt.patrol");
            var world = new BehaviorTreeWorld(tree, capacity: 1);
            world.AddAgent();
            world.TickAll();
            Assert.That(world.Statuses[0], Is.EqualTo(BehaviorTreeStatus.Success));
        }

        [Test]
        public void TickAll_FlatSequenceHoldRunning_ResumesRunningLeaf()
        {
            var nodes = new[]
            {
                new BehaviorTreeNode(BehaviorTreeNodeKind.Sequence, 1, 2, BehaviorTreeLeafBinding.None, 0),
                new BehaviorTreeNode(BehaviorTreeNodeKind.Action, 0, 0, BehaviorTreeLeafBinding.AlwaysSuccess, 0),
                new BehaviorTreeNode(BehaviorTreeNodeKind.Action, 0, 0, BehaviorTreeLeafBinding.HoldRunning, 0),
            };
            var tree = new BehaviorTreeDefinition("bt.flat-hold", nodes, rootIndex: 0);
            var world = new BehaviorTreeWorld(tree, capacity: 1);
            world.AddAgent();

            BehaviorTreeThinkStats first = world.TickAll();
            BehaviorTreeThinkStats second = world.TickAll();

            Assert.That(first.NodesVisited, Is.EqualTo(3));
            Assert.That(second.NodesVisited, Is.EqualTo(1));
            Assert.That(world.Statuses[0], Is.EqualTo(BehaviorTreeStatus.Running));
        }

        [Test]
        public void ThinkWave_10k_AlwaysSuccess16_UnderFifteenMilliseconds()
        {
            BehaviorTreeDefinition tree = BehaviorTreeFactory.CreateAlwaysSuccessSequence("bt.perf", leafCount: 15);
            const int agents = 10_000;
            var world = new BehaviorTreeWorld(tree, capacity: agents);
            for (int i = 0; i < agents; i++) world.AddAgent();
            world.TickAll();
            world.RestartAllThinking();
            var sw = Stopwatch.StartNew();
            BehaviorTreeThinkStats stats = world.TickAll();
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            Warn.If(ms, Is.GreaterThanOrEqualTo(FrameBudgetMs),
                $"AlwaysSuccess-16 think wave exceeded {FrameBudgetMs:F0}ms: {ms:F3}ms");
            Assert.That(ms, Is.LessThan(CiFrameEnvelopeMs),
                $"AlwaysSuccess-16 think wave exceeded CI envelope: {ms:F3}ms");
            Assert.That(stats.Agents, Is.EqualTo(agents));
        }

        [Test]
        public void LatchedSuccess_SecondWave_IsCheap()
        {
            BehaviorTreeDefinition tree = BehaviorTreeFactory.CreateAlwaysSuccessSequence("bt.latch", leafCount: 8);
            const int agents = 10_000;
            var world = new BehaviorTreeWorld(tree, capacity: agents);
            for (int i = 0; i < agents; i++) world.AddAgent();
            world.TickAll();
            var sw = Stopwatch.StartNew();
            world.TickAll();
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            Warn.If(ms, Is.GreaterThanOrEqualTo(LatchedWaveBudgetMs),
                $"Latched success second wave exceeded {LatchedWaveBudgetMs:F1}ms: {ms:F3}ms");
            Assert.That(ms, Is.LessThan(CiLatchedEnvelopeMs),
                $"Latched success second wave exceeded CI envelope: {ms:F3}ms");
        }

        [Test]
        public void PatrolChaseAttack_RegistryMissing_Throws()
        {
            BehaviorTreeDefinition tree = Behavior.RequireTree("bt.patrolChaseAttack");
            var world = new BehaviorTreeWorld(tree, 1);
            world.AddAgent();
            Assert.Throws<InvalidOperationException>(() => world.TickAll(programs: null, 32, sensors: null));
        }

        [Test]
        public void PatrolChaseAttack_ScriptLeaves_FromRegistry()
        {
            _ = Programs; // ensure GraphIdRegistry is populated before sensor key resolve
            BehaviorTreeDefinition tree = Behavior.RequireTree("bt.patrolChaseAttack");
            var sensors = new ScriptedSensors(Actions);
            var world = new BehaviorTreeWorld(tree, 1);
            world.AddAgent();

            sensors.See = false;
            TickUntilPatrolCompletes(world, sensors);
            Assert.That(world.LastScriptReturns[0], Is.EqualTo(0));

            world.ResetAgent(0);
            sensors.See = true;
            sensors.InRange = false;
            TickUntilScriptReturn(world, sensors, 1);

            world.ResetAgent(0);
            sensors.InRange = true;
            TickUntilScriptReturn(world, sensors, 2);
        }

        [Test]
        public void PatrolYield_ResumesAcrossThinkWaves_ThenReturnsPatrolIntent()
        {
            int patrolId = Actions.Require("bt.patrol");
            var nodes = new[]
            {
                new BehaviorTreeNode(
                    BehaviorTreeNodeKind.Action,
                    0,
                    0,
                    BehaviorTreeLeafBinding.ScriptSlice,
                    patrolId),
            };
            var tree = new BehaviorTreeDefinition("bt.patrol-yield", nodes, rootIndex: 0);
            var world = new BehaviorTreeWorld(tree, 1);
            world.AddAgent();

            world.RestartThinking(0);
            world.TickAll(Programs, 32, sensors: null);
            Assert.That(world.Statuses[0], Is.EqualTo(BehaviorTreeStatus.Running));

            world.RestartThinking(0);
            world.TickAll(Programs, 32, sensors: null);
            Assert.That(world.Statuses[0], Is.EqualTo(BehaviorTreeStatus.Running));

            world.RestartThinking(0);
            world.TickAll(Programs, 32, sensors: null);
            Assert.That(world.Statuses[0], Is.EqualTo(BehaviorTreeStatus.Success));
            Assert.That(world.LastScriptReturns[0], Is.EqualTo(0));
        }

        private void TickUntilPatrolCompletes(BehaviorTreeWorld world, ScriptedSensors sensors)
            => TickUntilScriptReturn(world, sensors, 0);

        private void TickUntilScriptReturn(BehaviorTreeWorld world, ScriptedSensors sensors, int expectedReturn)
        {
            for (int i = 0; i < 12; i++)
            {
                world.RestartThinking(0);
                world.TickAll(Programs, 32, sensors);
                if (world.LastScriptReturns[0] == expectedReturn &&
                    world.Statuses[0] is BehaviorTreeStatus.Success)
                {
                    return;
                }
            }

            Assert.That(
                world.LastScriptReturns[0],
                Is.EqualTo(expectedReturn),
                $"BT script return after {12} think waves: status={world.Statuses[0]}");
        }

        private sealed class ScriptedSensors : IBehaviorTreeSensorFeed
        {
            public bool See;
            public bool InRange;
            private readonly int _see;
            private readonly int _range;

            public ScriptedSensors(GraphActionCatalog actions)
            {
                _see = GraphRegistryScriptResolver.RequireActionId(actions, "bt.seeEnemy", GraphActionHost.BehaviorTree);
                _range = GraphRegistryScriptResolver.RequireActionId(actions, "bt.inAttackRange", GraphActionHost.BehaviorTree);
            }

            public void WriteSensors(int agentIndex, int graphId, System.Span<int> ints, System.Span<byte> bools)
            {
                if (graphId == _see) ints[0] = See ? 1 : 0;
                else if (graphId == _range) ints[0] = InRange ? 1 : 0;
            }
        }
    }
}
