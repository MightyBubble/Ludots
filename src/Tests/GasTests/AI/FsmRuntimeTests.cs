using System.Collections.Generic;
using System.Diagnostics;
using Ludots.Core.Gameplay.AI.Fsm;
using Ludots.Core.GraphRuntime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.AI
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class FsmRuntimeTests
    {
        [Test]
        public void SentryHfsm_StimulusEntersAlertingSubtree_ThenCyclesToIdle()
        {
            HfsmDefinition hfsm = HfsmFactory.CreateSentryHierarchy("hfsm.sentry");
            var world = new HfsmWorld(hfsm, capacity: 2);
            world.AddAgent();
            Assert.That(world.GetLeafState(0), Is.EqualTo(1));
            world.TickAll();
            Assert.That(world.GetLeafState(0), Is.EqualTo(1));
            world.LatchStimulus(0);
            world.TickAll();
            Assert.That(world.GetLeafState(0), Is.EqualTo(3));
            world.TickAll();
            Assert.That(world.GetLeafState(0), Is.EqualTo(4));
            world.TickAll();
            Assert.That(world.GetLeafState(0), Is.EqualTo(5));
            world.TickAll();
            Assert.That(world.GetLeafState(0), Is.EqualTo(1));
        }

        [Test]
        public void TransitionConditionGraph_AndStateLifecycleGraphs_AreInvoked()
        {
            // Idle --Always+cond(10)--> Alert; Alert has OnEnter(1) OnTick(2) OnExit(3)
            var states = new[]
            {
                new HfsmState(HfsmStateKind.Compound, -1, 1, 2, 1),
                new HfsmState(HfsmStateKind.Leaf, 0, 0, 0, 0),
                new HfsmState(HfsmStateKind.Leaf, 0, 0, 0, 0, onEnterGraphId: 1, onTickGraphId: 2, onExitGraphId: 3),
            };
            var transitions = new[]
            {
                new HfsmTransition(1, 2, HfsmTransitionPredicate.Always, 0, conditionGraphId: 10),
            };
            var hfsm = new HfsmDefinition("hfsm.lifecycle", states, rootIndex: 0, transitions);
            var host = new RecordingHost();
            host.ConditionResults[10] = true;
            var world = new HfsmWorld(hfsm, 1);
            world.AddAgent(host);
            Assert.That(world.GetLeafState(0), Is.EqualTo(1));

            world.TickAll(host);
            Assert.That(world.GetLeafState(0), Is.EqualTo(2));
            Assert.That(host.Conditions, Does.Contain((0, 10)));
            Assert.That(host.Actions, Does.Contain((0, 1))); // OnEnter Alert
            Assert.That(host.Actions, Does.Contain((0, 2))); // OnTick Alert (and maybe Root if set)

            host.Actions.Clear();
            host.ConditionResults[10] = false;
            // No transition back; stay on Alert and tick again
            world.TickAll(host);
            Assert.That(host.Actions, Does.Contain((0, 2)));
        }

        [Test]
        public void GraphFunctionCatalog_RegistersScriptFunctions_RejectsDuplicateAndMacroKind()
        {
            var catalog = new GraphFunctionCatalog();
            catalog.Register("combat.slash", graphId: 7, GraphKind.Script);
            Assert.That(catalog.Require("combat.slash").GraphId, Is.EqualTo(7));
            Assert.Throws<InvalidOperationException>(() => catalog.Register("combat.slash", 8, GraphKind.Script));
            Assert.Throws<ArgumentOutOfRangeException>(() => catalog.Register("bad", 1, GraphKind.Effect));
        }

        [Test]
        public void ThinkWave_10k_SentryHfsm_UnderFiveMilliseconds()
        {
            HfsmDefinition hfsm = HfsmFactory.CreateSentryHierarchy("hfsm.perf");
            const int agents = 10_000;
            var world = new HfsmWorld(hfsm, capacity: agents);
            for (int i = 0; i < agents; i++)
            {
                world.AddAgent();
                if ((i & 1) == 0)
                {
                    world.LatchStimulus(i);
                }
            }

            world.TickAll();
            var sw = Stopwatch.StartNew();
            HfsmThinkStats stats = world.TickAll();
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            TestContext.WriteLine($"A={stats.Agents} preds={stats.PredicatesChecked} taken={stats.TransitionsTaken} T_ai_ms={ms:F3}");
            Assert.That(ms, Is.LessThan(5.0), $"HFSM think wave exceeded 5ms: {ms:F3}ms");
        }

        [Test]
        public void SentryHfsm_WithRealScriptHost_RunsConditionAndLifecycle()
        {
            var programs = Ludots.Tests.Gas.Graph.GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out GraphActionCatalog actions);
            HfsmDefinition hfsm = HfsmFactory.CreateSentryHierarchyWithScripts(
                "hfsm.scripted",
                name => Ludots.Core.Gameplay.AI.BehaviorTree.GraphRegistryScriptResolver.RequireActionId(actions, name));
            var host = new GraphProgramHfsmHost(programs);
            var world = new HfsmWorld(hfsm, capacity: 1);
            world.AddAgent(host);
            world.LatchStimulus(0);
            world.TickAll(host);
            Assert.That(world.GetLeafState(0), Is.EqualTo(3));
            world.TickAll(host);
            Assert.That(world.GetLeafState(0), Is.EqualTo(4));
            world.TickAll(host);
            Assert.That(world.GetLeafState(0), Is.EqualTo(5));
        }

        [Test]
        public void ThinkWave_10k_SentryHfsmWithScripts_UnderFifteenMilliseconds()
        {
            var programs = Ludots.Tests.Gas.Graph.GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out GraphActionCatalog actions);
            HfsmDefinition hfsm = HfsmFactory.CreateSentryHierarchyWithScripts(
                "hfsm.perf.scripted",
                name => Ludots.Core.Gameplay.AI.BehaviorTree.GraphRegistryScriptResolver.RequireActionId(actions, name));
            var host = new GraphProgramHfsmHost(programs);
            const int agents = 10_000;
            var world = new HfsmWorld(hfsm, capacity: agents);
            for (int i = 0; i < agents; i++)
            {
                world.AddAgent(host);
                if ((i & 1) == 0) world.LatchStimulus(i);
            }

            world.TickAll(host);
            var sw = Stopwatch.StartNew();
            HfsmThinkStats stats = world.TickAll(host);
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            TestContext.WriteLine($"scripted A={stats.Agents} taken={stats.TransitionsTaken} T_ai_ms={ms:F3}");
            // Registry Script host on CI can exceed 5ms; keep hard gate under 15ms.
            Assert.That(ms, Is.LessThan(15.0), $"Scripted HFSM think wave exceeded 15ms: {ms:F3}ms");
        }

        private sealed class RecordingHost : IHfsmGraphHost
        {
            public Dictionary<int, bool> ConditionResults { get; } = new();
            public List<(int Agent, int GraphId)> Conditions { get; } = new();
            public List<(int Agent, int GraphId)> Actions { get; } = new();

            public bool EvalCondition(int agentIndex, int conditionGraphId)
            {
                Conditions.Add((agentIndex, conditionGraphId));
                return ConditionResults.TryGetValue(conditionGraphId, out bool ok) && ok;
            }

            public void RunAction(int agentIndex, int actionGraphId)
            {
                Actions.Add((agentIndex, actionGraphId));
            }
        }
    }
}
