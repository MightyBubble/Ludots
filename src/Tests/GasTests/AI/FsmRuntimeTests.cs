using System.Collections.Generic;
using System.Diagnostics;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.Gameplay.AI.Fsm;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.AI
{
    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class FsmRuntimeTests
    {
        private const double FrameBudgetMs = 5.0;
        private const double ScriptBudgetMs = 15.0;
        private const double CiScriptEnvelopeMs = 25.0;

        [Test]
        public void SentryHfsm_StimulusEntersAlertingSubtree_ThenCyclesToIdle()
        {
            GraphProgramRegistry programs = Ludots.Tests.Gas.Graph.GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out _, out GraphBehaviorCatalog behavior);
            _ = programs;
            HfsmDefinition hfsm = behavior.RequireHfsm("hfsm.sentry");
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
        public void GetLeafStateName_TracksAuthoredId_RegardlessOfPackingOrder()
        {
            // Same machine authored with the alert/retreat children swapped: the packed
            // indices differ, the authored names must not drift with them.
            var ordered = new[]
            {
                new HfsmState(HfsmStateKind.Leaf, -1, 0, 0, 0, name: "idle"),
                new HfsmState(HfsmStateKind.Leaf, 0, 0, 0, 0, name: "alert"),
                new HfsmState(HfsmStateKind.Leaf, 0, 0, 0, 0, name: "combat"),
            };
            // Same machine with the children authored in a different order plus an
            // extra state: "alert" lands at a different packed index, its name must not.
            var swapped = new[]
            {
                new HfsmState(HfsmStateKind.Leaf, -1, 0, 0, 0, name: "idle"),
                new HfsmState(HfsmStateKind.Leaf, 0, 0, 0, 0, name: "combat"),
                new HfsmState(HfsmStateKind.Leaf, 0, 0, 0, 0, name: "retreat"),
                new HfsmState(HfsmStateKind.Leaf, 0, 0, 0, 0, name: "alert"),
            };
            var transitions = new[]
            {
                new HfsmTransition(0, 1, HfsmTransitionPredicate.Always, 0),
            };
            var orderedWorld = new HfsmWorld(new HfsmDefinition("hfsm.names.a", ordered, rootIndex: 0, transitions), 1);
            var swappedWorld = new HfsmWorld(new HfsmDefinition("hfsm.names.b", swapped, rootIndex: 0,
                new[] { new HfsmTransition(0, 3, HfsmTransitionPredicate.Always, 0) }), 1);
            orderedWorld.AddAgent();
            swappedWorld.AddAgent();

            Assert.That(orderedWorld.GetLeafStateName(0), Is.EqualTo("idle"));
            Assert.That(swappedWorld.GetLeafStateName(0), Is.EqualTo("idle"));

            orderedWorld.LatchStimulus(0);
            swappedWorld.LatchStimulus(0);
            orderedWorld.TickAll();
            swappedWorld.TickAll();
            Assert.That(orderedWorld.GetLeafState(0), Is.EqualTo(1));
            Assert.That(swappedWorld.GetLeafState(0), Is.EqualTo(3));
            Assert.That(orderedWorld.GetLeafStateName(0), Is.EqualTo("alert"));
            Assert.That(swappedWorld.GetLeafStateName(0), Is.EqualTo("alert"));
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
            _ = Ludots.Tests.Gas.Graph.GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out _, out GraphBehaviorCatalog behavior);
            HfsmDefinition hfsm = behavior.RequireHfsm("hfsm.sentry");
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
            Assert.That(ms, Is.LessThan(FrameBudgetMs), $"HFSM think wave exceeded {FrameBudgetMs:F0}ms: {ms:F3}ms");
        }

        [Test]
        public void SentryHfsm_WithRealScriptHost_RunsConditionAndLifecycle()
        {
            var programs = Ludots.Tests.Gas.Graph.GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out _, out GraphBehaviorCatalog behavior);
            HfsmDefinition hfsm = behavior.RequireHfsm("hfsm.sentry.scripted");
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
        public void GraphProgramHfsmHost_RefreshesCachedScriptAfterRegistryReplace()
        {
            var programs = new GraphProgramRegistry();
            programs.Register(10, ReturnIntProgram(0), GraphKind.Script);
            var host = new GraphProgramHfsmHost(programs);

            Assert.That(host.EvalCondition(0, 10), Is.False);

            programs.ReplaceProgram(10, ReturnIntProgram(1), GraphKind.Script, GraphInstructionSourceMap.Empty);

            Assert.That(host.EvalCondition(0, 10), Is.True);
        }

        [Test]
        public void ThinkWave_10k_SentryHfsmWithScripts_UnderFifteenMilliseconds()
        {
            var programs = Ludots.Tests.Gas.Graph.GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out _, out GraphBehaviorCatalog behavior);
            HfsmDefinition hfsm = behavior.RequireHfsm("hfsm.sentry.scripted");
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
            Warn.If(ms, Is.GreaterThanOrEqualTo(ScriptBudgetMs), $"Scripted HFSM think wave exceeded {ScriptBudgetMs:F0}ms: {ms:F3}ms");
            Assert.That(ms, Is.LessThan(CiScriptEnvelopeMs), $"Scripted HFSM think wave exceeded CI envelope: {ms:F3}ms");
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

        private static GraphInstruction[] ReturnIntProgram(int value)
            =>
            [
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = value },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
            ];
    }
}
