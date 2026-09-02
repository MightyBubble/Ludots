using CapabilityStandardAbilityGraphSandboxMod.Runtime;
using CapabilityStandardBehaviorTreeArenaMod.Runtime;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphBehaviorIntegrationMod.Runtime;
using CapabilityStandardHfsmSentryArenaMod.Runtime;
using Ludots.Core.Gameplay.AI;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.Gameplay.AI.Fsm;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Tests.Gas.Graph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class GraphBehaviorSeparatedShowcaseAcceptanceTests
    {
        private const double ShowcaseThinkBudgetMs = 15.0;
        private const double CiShowcaseEnvelopeMs = 25.0;

        private GraphProgramRegistry _programs = null!;
        private GraphFunctionCatalog _catalog = null!;
        private GraphActionCatalog _actions = null!;
        private GraphBehaviorCatalog _behavior = null!;

        [SetUp]
        public void SetUp()
        {
            _programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _catalog, out _actions, out _behavior);
        }

        [Test]
        public void BehaviorTreeArena_PatrolVignette_ThinkWavesUnderBudget()
        {
            var runtime = new BehaviorTreeArenaRuntime();
            runtime.Bind(_programs, _actions, _behavior);
            runtime.EnsureWorld();
            Warm(runtime.Tick);
            Drive(runtime.Tick, runtime.Metrics);
            Assert.That(runtime.Metrics.Detail, Does.Contain("BT L2"));
            Assert.That(runtime.GuardCount, Is.GreaterThanOrEqualTo(8));
            Warn.If(runtime.Metrics.MaxThinkMs, Is.GreaterThanOrEqualTo(ShowcaseThinkBudgetMs));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(CiShowcaseEnvelopeMs));
        }

        [Test]
        public void BehaviorTreeArena_PatrolLeaf_YieldsAcrossThinkWaves()
        {
            var runtime = new BehaviorTreeArenaRuntime();
            runtime.Bind(_programs, _actions, _behavior);
            runtime.EnsureWorld();
            bool sawYield = false;
            for (int i = 0; i < 12; i++)
            {
                runtime.Tick(0.2f);
                if (runtime.Metrics.Detail.Contains("leaf yielding", StringComparison.Ordinal))
                {
                    sawYield = true;
                    break;
                }
            }

            Assert.That(sawYield, Is.True, "Expected patrol ActionLib leaf to yield and resume across think waves.");
        }

        /// <summary>Judge: L2 tree topology + leaf Scripts — not a whole-tree Script sugar shell.</summary>
        [Test]
        public void BehaviorTreeArena_MainTree_IsL2TopologyWithLeafScripts()
        {
            BehaviorTreeDefinition tree = _behavior.RequireTree("bt.patrolChaseAttack");
            Assert.That(tree.Nodes.Length, Is.GreaterThan(0));

            int patrolId = GraphRegistryScriptResolver.RequireActionId(_actions, "bt.patrol", GraphActionHost.BehaviorTree);
            Assert.That(_programs.TryGetProgram(patrolId, out ReadOnlySpan<GraphInstruction> patrol), Is.True);
            var ops = new HashSet<ushort>();
            foreach (ref readonly GraphInstruction instruction in patrol) ops.Add(instruction.Op);
            Assert.That(ops, Does.Contain((ushort)GraphNodeOp.Yield), "The patrol leaf Script must yield across think waves.");
            Assert.That(ops, Does.Contain((ushort)GraphNodeOp.HaltReturnInt));

            int seeId = GraphRegistryScriptResolver.RequireActionId(_actions, "bt.seeEnemy", GraphActionHost.BehaviorTree);
            Assert.That(_programs.TryGetProgram(seeId, out ReadOnlySpan<GraphInstruction> see), Is.True);
            var seeOps = new HashSet<ushort>();
            foreach (ref readonly GraphInstruction instruction in see) seeOps.Add(instruction.Op);
            Assert.That(seeOps, Does.Contain((ushort)GraphNodeOp.CompareLtInt), "seeEnemy leaf thresholds live in the leaf Script.");
        }

        /// <summary>Regression: L2 tree + leaf Scripts keep patrol → chase → attack intents.</summary>
        [Test]
        public void BehaviorTreeArena_RealGraphTree_IntentSequenceMatchesOldBehavior()
        {
            var runtime = new BehaviorTreeArenaRuntime();
            runtime.Bind(_programs, _actions, _behavior);
            runtime.EnsureWorld();
            bool sawPatrol = false, sawChase = false, sawAttack = false;
            for (int i = 0; i < 150; i++)
            {
                runtime.Tick(0.2f);
                for (int g = 0; g < runtime.GuardCount; g++)
                {
                    switch (runtime.Intent[g])
                    {
                        case 0: sawPatrol = true; break;
                        case 1: sawChase = true; break;
                        case 2: sawAttack = true; break;
                    }
                }
            }

            Assert.That(sawPatrol, Is.True, "Guards must patrol when no enemy is within sight.");
            Assert.That(sawChase, Is.True, "Guards must chase when the see-enemy leaf succeeds.");
            Assert.That(sawAttack, Is.True, "Guards must attack when the in-range leaf succeeds.");
            Assert.That(runtime.Metrics.Detail, Does.Contain("BT L2"));
        }

        /// <summary>
        /// Crowd honesty gate: featured = L2 BehaviorTreeWorld (bt.patrolChaseAttack) with leaf Scripts;
        /// 10k crowd = no-graph AlwaysSuccess tree (ScriptSlices==0).
        /// </summary>
        [Test]
        public void BehaviorTreeArena_CrowdBand_NoGraphPressureBaseline_Labeled()
        {
            var runtime = new BehaviorTreeArenaRuntime();
            runtime.Bind(_programs, _actions, _behavior);
            runtime.EnsureWorld();
            Warm(runtime.Tick);
            Drive(runtime.Tick, runtime.Metrics);

            Assert.That(runtime.TreeWorld, Is.Not.Null, "The featured segment must run L2 BehaviorTreeWorld.");
            Assert.That(runtime.TreeWorld!.Count, Is.GreaterThanOrEqualTo(8));

            BehaviorTreeWorld? crowd = runtime.CrowdWorld;
            Assert.That(crowd, Is.Not.Null, "The crowd pressure band must exist.");
            crowd!.RestartFinishedThinking();
            BehaviorTreeThinkStats crowdStats = crowd.TickAll(8);
            TestContext.WriteLine(
                $"crowd band no-graph baseline: agents={crowdStats.Agents} scriptSlices={crowdStats.ScriptSlices} nodesVisited={crowdStats.NodesVisited}");
            Assert.That(crowdStats.ScriptSlices, Is.EqualTo(0),
                "The crowd band is a no-graph pressure baseline; any Script slice here would be an unlabeled graph claim.");
            Assert.That(crowdStats.Agents, Is.EqualTo(10_000));

            Warn.If(runtime.Metrics.MaxThinkMs, Is.GreaterThanOrEqualTo(ShowcaseThinkBudgetMs));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(CiShowcaseEnvelopeMs));
        }

        [Test]
        public void HfsmSentryArena_GateVignette_ThinkWavesUnderBudget()
        {
            var runtime = new HfsmSentryArenaRuntime();
            runtime.Bind(_programs, _actions, _behavior);
            runtime.EnsureWorld();
            Assert.That(runtime.FeaturedUsesHfsmWorld, Is.True,
                "Featured sentry band must run HfsmWorld / hfsm.sentry.scripted + leaf Scripts.");
            Assert.That(runtime.GetSentryStateName(0), Is.EqualTo("idle"));
            Warm(runtime.Tick);
            Drive(runtime.Tick, runtime.Metrics);
            Assert.That(runtime.FeaturedWorld, Is.Not.Null, "Featured sentries must run HfsmWorld.");
            Assert.That(runtime.Metrics.Detail, Does.Contain("HFSM L2"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("crowdLifecycleRuns=0"));
            Assert.That(runtime.SentryCount, Is.GreaterThanOrEqualTo(8));
            Assert.That(runtime.GetSentryStateName(0), Is.Not.EqualTo("unknown"));
            if (runtime.CrowdUsesNoGraphHfsmWorld)
            {
                Assert.That(runtime.CrowdAgentCount, Is.GreaterThan(0),
                    "Crowd band exists as no-graph HfsmWorld pressure.");
            }

            Warn.If(runtime.Metrics.MaxThinkMs, Is.GreaterThanOrEqualTo(ShowcaseThinkBudgetMs));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(CiShowcaseEnvelopeMs));
        }

        /// <summary>
        /// Crowd honesty gate: featured = HfsmWorld(hfsm.sentry.scripted) + leaf Scripts;
        /// 10k crowd = HfsmWorld(hfsm.sentry) with LifecycleRuns == 0.
        /// </summary>
        [Test]
        public void HfsmSentryArena_CrowdBand_NoGraphPressureBaseline_Labeled()
        {
            var runtime = new HfsmSentryArenaRuntime();
            runtime.Bind(_programs, _actions, _behavior);
            runtime.EnsureWorld();
            Warm(runtime.Tick);
            Drive(runtime.Tick, runtime.Metrics);

            Assert.That(runtime.FeaturedWorld, Is.Not.Null, "The featured segment must run HfsmWorld.");
            Assert.That(runtime.FeaturedWorld!.Count, Is.GreaterThanOrEqualTo(8));
            Assert.That(runtime.GetSentryStateName(0), Is.AnyOf("idle", "alert", "combat", "retreat"));

            HfsmWorld? crowd = runtime.CrowdWorld;
            Assert.That(crowd, Is.Not.Null, "The crowd pressure band must exist.");
            HfsmThinkStats crowdStats = crowd!.TickAll();
            TestContext.WriteLine(
                $"crowd band no-graph baseline: agents={crowdStats.Agents} lifecycleRuns={crowdStats.LifecycleRuns} predicates={crowdStats.PredicatesChecked}");
            Assert.That(crowdStats.LifecycleRuns, Is.EqualTo(0),
                "The crowd band is a no-graph pressure baseline; any lifecycle Script host would be an unlabeled graph claim.");
            Assert.That(crowdStats.Agents, Is.EqualTo(10_000));
            Assert.That(runtime.Metrics.Detail, Does.Contain("crowdLifecycleRuns=0"));

            Warn.If(runtime.Metrics.MaxThinkMs, Is.GreaterThanOrEqualTo(ShowcaseThinkBudgetMs));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(CiShowcaseEnvelopeMs));
        }

        [Test]
        public void AbilityGraphSandbox_CastArc_UnderBudget()
        {
            using var runtime = new AbilityGraphSandboxRuntime();
            runtime.BindStandaloneFromModAssets();
            runtime.EnsureWorld();
            Warm(runtime.Tick);
            Drive(runtime.Tick, runtime.Metrics);
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Metrics.Detail, Does.Contain("巡逻查一圈"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("挂状态"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("加好感"));
            });
            Assert.That(runtime.TargetCount, Is.EqualTo(8));
            Assert.That(runtime.NearbyCount, Is.EqualTo(AbilityGraphSandboxGraphKeys.QueryLimit));
            Assert.That(runtime.EffectApplications, Is.GreaterThan(0));
            Assert.That(runtime.RelationshipScore, Is.EqualTo(13));
            Assert.That(runtime.TrustedFlag, Is.True);
            Assert.That(runtime.Metrics.Detail, Does.Not.Contain("耗时"));
            Warn.If(runtime.Metrics.MaxThinkMs, Is.GreaterThanOrEqualTo(ShowcaseThinkBudgetMs));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(CiShowcaseEnvelopeMs));
        }

        [Test]
        public void GraphBehaviorIntegration_ShortPlay_UnderBudget()
        {
            var runtime = new GraphBehaviorIntegrationRuntime();
            runtime.Bind(_programs, _actions, _behavior);
            runtime.EnsureWorld();
            Warm(runtime.Tick, waves: 10);
            Drive(runtime.Tick, runtime.Metrics);
            Assert.That(runtime.Metrics.Detail, Does.Contain("Integration L2"));
            Assert.That(runtime.GuardCount, Is.EqualTo(6));
            Assert.That(runtime.SentryCount, Is.EqualTo(6));
            Assert.That(runtime.Hfsm, Is.Not.Null, "Integration runs HfsmWorld + leaf Scripts as L2 SSOT.");
            Warn.If(runtime.Metrics.MaxThinkMs, Is.GreaterThanOrEqualTo(ShowcaseThinkBudgetMs));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(CiShowcaseEnvelopeMs));
        }

        private static void Warm(System.Action<float> tick, int waves = 5)
        {
            for (int i = 0; i < waves; i++) tick(0.2f);
        }

        private static void Drive(System.Action<float> tick, GraphShowcaseMetrics metrics, int waves = 20)
        {
            for (int i = 0; i < 3; i++) tick(0.2f);
            metrics.MaxThinkMs = 0;
            metrics.LastThinkMs = 0;
            for (int i = 0; i < waves; i++) tick(0.2f);
            TestContext.WriteLine($"{metrics.ShowcaseId}: waves={metrics.ThinkWaves} max={metrics.MaxThinkMs:F3} detail={metrics.Detail}");
        }
    }
}
