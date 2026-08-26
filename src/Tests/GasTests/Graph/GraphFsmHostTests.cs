using System;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.AI.Fsm;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// FSM-1a host contract: one Script graph per agent, phase SSOT in MapVariableStore,
    /// one halt-only dispatch slice per think wave. Featured arena path is GraphFsmHost;
    /// crowd pressure stays on no-graph HfsmWorld.
    /// </summary>
    [TestFixture]
    public sealed class GraphFsmHostTests
    {
        [Test]
        public void ThinkWave_AdvancesSentryPhaseCycle_ThroughGraphFsmHost()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out _, out _);
            int graphId = GraphIdRegistry.GetId("Graph.FSM.Sentry");
            using var host = new GraphFsmHost(programs, graphId, capacity: 2, "sentry.phase");
            int agent = host.AddAgent();
            var feed = new StaticDistanceFeed(100);

            Assert.That(host.Count, Is.EqualTo(1));
            Assert.That(host.GraphId, Is.EqualTo(graphId));
            Assert.That(host.PhaseOf(agent), Is.EqualTo(0));

            GraphFsmThinkStats wave1 = host.ThinkWave(128, feed);
            Assert.That(wave1.Agents, Is.EqualTo(1));
            Assert.That(wave1.Halted, Is.EqualTo(1));
            Assert.That(host.PhaseOf(agent), Is.EqualTo(1));

            host.ThinkWave(128, feed);
            Assert.That(host.PhaseOf(agent), Is.EqualTo(2));

            feed.DistanceCm = 99999;
            host.ThinkWave(128, feed);
            Assert.That(host.PhaseOf(agent), Is.EqualTo(3));
            host.ThinkWave(128, feed);
            Assert.That(host.PhaseOf(agent), Is.EqualTo(0));
        }

        [Test]
        public void ResetAgent_ClearsPhaseAndRegisters()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out _, out _);
            using var host = new GraphFsmHost(programs, GraphIdRegistry.GetId("Graph.FSM.Sentry"), capacity: 1, "sentry.phase");
            int agent = host.AddAgent();
            host.ThinkWave(128, new StaticDistanceFeed(100));
            Assert.That(host.PhaseOf(agent), Is.EqualTo(1));

            host.ResetAgent(agent);
            Assert.That(host.PhaseOf(agent), Is.EqualTo(0));
            Assert.That(host.LastReturns[agent], Is.EqualTo(0));
        }

        [Test]
        public void AddAgent_AtCapacity_FailsClosed()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out _, out _);
            using var host = new GraphFsmHost(programs, GraphIdRegistry.GetId("Graph.FSM.Sentry"), capacity: 1, "sentry.phase");
            host.AddAgent();
            Assert.That(() => host.AddAgent(), Throws.InvalidOperationException.With.Message.Contains("capacity"));
        }

        private sealed class StaticDistanceFeed : IBehaviorTreeSensorFeed
        {
            public StaticDistanceFeed(int distanceCm) => DistanceCm = distanceCm;
            public int DistanceCm { get; set; }

            public void WriteSensors(int agentIndex, int graphId, Span<int> ints, Span<byte> bools)
            {
                ints[0] = DistanceCm;
            }
        }
    }
}
