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
    }
}
