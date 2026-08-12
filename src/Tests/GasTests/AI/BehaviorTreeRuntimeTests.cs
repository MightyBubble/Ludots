using System.Diagnostics;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.GraphRuntime;
using Ludots.Tests.Gas.Graph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.AI
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class BehaviorTreeRuntimeTests
    {
        private GraphProgramRegistry? _programs;

        private GraphProgramRegistry Programs
            => _programs ??= GraphRegistryTestBootstrap.LoadCoreScriptsAndFuncLib(out _);

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
        public void ThinkWave_10k_AlwaysSuccess16_UnderFiveMilliseconds()
        {
            BehaviorTreeDefinition tree = BehaviorTreeFactory.CreateAlwaysSuccessSequence("bt.perf", leafCount: 15);
            const int agents = 10_000;
            var world = new BehaviorTreeWorld(tree, capacity: agents);
            for (int i = 0; i < agents; i++) world.AddAgent();
            world.TickAll();
            for (int i = 0; i < agents; i++) world.ResetAgent(i);
            var sw = Stopwatch.StartNew();
            BehaviorTreeThinkStats stats = world.TickAll();
            sw.Stop();
            Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(5.0));
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
            Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(1.5));
        }

        [Test]
        public void PatrolChaseAttack_RegistryMissing_Throws()
        {
            BehaviorTreeDefinition tree = BehaviorTreeFactory.CreatePatrolChaseAttackTree(
                "bt.missing", GraphRegistryScriptResolver.RequireId);
            var world = new BehaviorTreeWorld(tree, 1);
            world.AddAgent();
            Assert.Throws<InvalidOperationException>(() => world.TickAll(programs: null, 32, sensors: null));
        }

        [Test]
        public void PatrolChaseAttack_ScriptLeaves_FromRegistry()
        {
            _ = Programs; // ensure GraphIdRegistry is populated before sensor key resolve
            BehaviorTreeDefinition tree = BehaviorTreeFactory.CreatePatrolChaseAttackTree(
                "bt.pca", GraphRegistryScriptResolver.RequireId);
            var sensors = new ScriptedSensors();
            var world = new BehaviorTreeWorld(tree, 1);
            world.AddAgent();

            sensors.See = false;
            world.RestartThinking(0);
            world.TickAll(Programs, 32, sensors);
            Assert.That(world.LastScriptReturns[0], Is.EqualTo(0));

            sensors.See = true;
            sensors.InRange = false;
            world.RestartThinking(0);
            world.TickAll(Programs, 32, sensors);
            Assert.That(world.LastScriptReturns[0], Is.EqualTo(1));

            sensors.InRange = true;
            world.RestartThinking(0);
            world.TickAll(Programs, 32, sensors);
            Assert.That(world.LastScriptReturns[0], Is.EqualTo(2));
        }

        private sealed class ScriptedSensors : IBehaviorTreeSensorFeed
        {
            public bool See;
            public bool InRange;
            private readonly int _see = GraphRegistryScriptResolver.RequireId(BehaviorTreeScriptKeys.SeeEnemy);
            private readonly int _range = GraphRegistryScriptResolver.RequireId(BehaviorTreeScriptKeys.InAttackRange);

            public void WriteSensors(int agentIndex, int graphId, System.Span<int> ints, System.Span<byte> bools)
            {
                if (graphId == _see) ints[0] = See ? 1 : 0;
                else if (graphId == _range) ints[0] = InRange ? 1 : 0;
            }
        }
    }
}
