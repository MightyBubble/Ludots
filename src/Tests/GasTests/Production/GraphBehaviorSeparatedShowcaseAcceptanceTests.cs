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



        /// <summary>Judge: L2 tree topology + leaf Scripts — not a whole-tree Script sugar shell.</summary>


        /// <summary>Regression: L2 tree + leaf Scripts keep patrol → chase → attack intents.</summary>


        [Test]
        public void BehaviorTreeArena_PlayableControls_ChangeRuntimeState()
        {
            var runtime = new BehaviorTreeArenaRuntime();
            runtime.Bind(_programs, _actions, _behavior);
            runtime.EnsureWorld();

            runtime.TogglePaused();
            int wavesBeforeStep = runtime.Metrics.ThinkWaves;
            runtime.Tick(1f);
            Assert.That(runtime.Metrics.ThinkWaves, Is.EqualTo(wavesBeforeStep));
            runtime.Step();
            Assert.That(runtime.Metrics.ThinkWaves, Is.GreaterThan(wavesBeforeStep));

            runtime.ToggleL2();
            Assert.That(runtime.BuildControlState().L2Enabled, Is.False);
            runtime.ToggleStimulus();
            Assert.That(runtime.BuildControlState().StimulusEnabled, Is.False);
            runtime.IncreaseSightRadius();
            runtime.IncreaseThinkPeriod();
            Assert.That(runtime.SightRadius, Is.EqualTo(7f).Within(0.001f));
            Assert.That(runtime.ThinkPeriodSeconds, Is.EqualTo(0.3f).Within(0.001f));
        }

        /// <summary>
        /// Crowd honesty gate: featured = component-driven bt.patrolChaseAttack (GraphActionBrain{BtId} + BtState) with leaf Scripts;
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

            Assert.That(runtime.GuardCount, Is.GreaterThanOrEqualTo(8),
                "The featured segment must have component-driven BT guards.");

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

        [Test]
        public void HfsmSentryArena_PlayableControls_ChangeRuntimeState()
        {
            var runtime = new HfsmSentryArenaRuntime();
            runtime.Bind(_programs, _actions, _behavior);
            runtime.EnsureWorld();

            runtime.TogglePaused();
            int wavesBeforeStep = runtime.Metrics.ThinkWaves;
            runtime.Tick(1f);
            Assert.That(runtime.Metrics.ThinkWaves, Is.EqualTo(wavesBeforeStep));
            runtime.Step();
            Assert.That(runtime.Metrics.ThinkWaves, Is.GreaterThan(wavesBeforeStep));

            runtime.ToggleL2();
            Assert.That(runtime.BuildControlState().L2Enabled, Is.False);
            runtime.ToggleStimulus();
            Assert.That(runtime.BuildControlState().StimulusEnabled, Is.False);
            runtime.IncreaseAlertRadius();
            runtime.IncreaseThinkPeriod();
            Assert.That(runtime.AlertRadius, Is.EqualTo(6.5f).Within(0.001f));
            Assert.That(runtime.ThinkPeriodSeconds, Is.EqualTo(0.3f).Within(0.001f));
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
