using System;
using System.Diagnostics;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.GraphRuntime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.AI
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class BehaviorTreeRuntimeTests
    {
        [Test]
        public void TickAll_AlwaysSuccessSequence_ReachesSuccess()
        {
            BehaviorTreeDefinition tree = BehaviorTreeFactory.CreateAlwaysSuccessSequence("bt.seq", leafCount: 8);
            var world = new BehaviorTreeWorld(tree, capacity: 4);
            world.AddAgent();
            world.AddAgent();

            BehaviorTreeThinkStats stats = world.TickAll(ReadOnlySpan<GraphInstruction>.Empty, scriptBudgetSteps: 32);
            Assert.That(stats.Agents, Is.EqualTo(2));
            Assert.That(world.Statuses[0], Is.EqualTo(BehaviorTreeStatus.Success));
            Assert.That(world.Statuses[1], Is.EqualTo(BehaviorTreeStatus.Success));
        }

        [Test]
        public void TickAll_PatrolSkeleton_StaysRunningOnEngageHold()
        {
            // Selector: Sequence(AlwaysFailure, HoldRunning) fails first child → skip engage; patrol AlwaysSuccess.
            BehaviorTreeDefinition tree = BehaviorTreeFactory.CreatePatrolEngageSkeleton("bt.patrol");
            var world = new BehaviorTreeWorld(tree, capacity: 1);
            world.AddAgent();
            world.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 32);
            Assert.That(world.Statuses[0], Is.EqualTo(BehaviorTreeStatus.Success));
        }

        [Test]
        public void ThinkWave_10k_AlwaysSuccess16_UnderFiveMilliseconds()
        {
            BehaviorTreeDefinition tree = BehaviorTreeFactory.CreateAlwaysSuccessSequence("bt.perf", leafCount: 15);
            Assert.That(tree.NodeCount, Is.EqualTo(16));
            const int agents = 10_000;
            var world = new BehaviorTreeWorld(tree, capacity: agents);
            for (int i = 0; i < agents; i++)
            {
                world.AddAgent();
            }

            // Warmup
            world.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 32);
            for (int i = 0; i < agents; i++)
            {
                world.ResetAgent(i);
            }

            var sw = Stopwatch.StartNew();
            BehaviorTreeThinkStats stats = world.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 32);
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;

            TestContext.WriteLine(
                $"A={stats.Agents} N_topo={tree.NodeCount} visited={stats.NodesVisited} T_ai_ms={ms:F3}");

            Assert.That(ms, Is.LessThan(5.0), $"Think wave exceeded 5ms budget: {ms:F3}ms");
            Assert.That(stats.Agents, Is.EqualTo(agents));
        }

        [Test]
        public void LatchedSuccess_SecondWave_IsCheap()
        {
            BehaviorTreeDefinition tree = BehaviorTreeFactory.CreateAlwaysSuccessSequence("bt.latch", leafCount: 8);
            const int agents = 10_000;
            var world = new BehaviorTreeWorld(tree, capacity: agents);
            for (int i = 0; i < agents; i++) world.AddAgent();
            world.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 32);

            var sw = Stopwatch.StartNew();
            world.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 32);
            sw.Stop();
            Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(1.5));
        }

        [Test]
        public void PatrolChaseAttack_HostMissing_Throws()
        {
            BehaviorTreeDefinition tree = BehaviorTreeFactory.CreatePatrolChaseAttackTree("bt.host.missing");
            var world = new BehaviorTreeWorld(tree, 1);
            world.AddAgent();
            Assert.Throws<InvalidOperationException>(() =>
                world.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 32, leafHost: null));
        }

        [Test]
        public void PatrolChaseAttack_SelectsPatrol_ThenChase_ThenAttack()
        {
            BehaviorTreeDefinition tree = BehaviorTreeFactory.CreatePatrolChaseAttackTree("bt.pca");
            var host = new ScriptedLeafHost();
            var world = new BehaviorTreeWorld(tree, 1);
            world.AddAgent();

            host.SeeEnemy = false;
            world.RestartThinking(0);
            world.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 32, host);
            Assert.That(host.LastAction, Is.EqualTo(BehaviorTreeHostBindings.Patrol));
            Assert.That(world.Statuses[0], Is.EqualTo(BehaviorTreeStatus.Running));

            host.SeeEnemy = true;
            host.InRange = false;
            world.RestartThinking(0);
            world.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 32, host);
            Assert.That(host.LastAction, Is.EqualTo(BehaviorTreeHostBindings.Chase));

            host.InRange = true;
            world.RestartThinking(0);
            world.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 32, host);
            Assert.That(host.LastAction, Is.EqualTo(BehaviorTreeHostBindings.Attack));
        }

        private sealed class ScriptedLeafHost : IBehaviorTreeLeafHost
        {
            public bool SeeEnemy;
            public bool InRange;
            public int LastAction;

            public BehaviorTreeStatus EvalCondition(int agentIndex, int bindingId)
            {
                return bindingId switch
                {
                    BehaviorTreeHostBindings.SeeEnemy => SeeEnemy ? BehaviorTreeStatus.Success : BehaviorTreeStatus.Failure,
                    BehaviorTreeHostBindings.InAttackRange => InRange ? BehaviorTreeStatus.Success : BehaviorTreeStatus.Failure,
                    _ => throw new InvalidOperationException($"Unexpected condition {bindingId}")
                };
            }

            public BehaviorTreeStatus TickAction(int agentIndex, int bindingId)
            {
                LastAction = bindingId;
                return BehaviorTreeStatus.Running;
            }
        }
    }
}
