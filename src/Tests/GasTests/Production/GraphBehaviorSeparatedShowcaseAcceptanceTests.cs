using CapabilityStandardAbilityGraphSandboxMod.Runtime;
using CapabilityStandardBehaviorTreeArenaMod.Runtime;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphBehaviorIntegrationMod.Runtime;
using CapabilityStandardHfsmSentryArenaMod.Runtime;
using CapabilityStandardLevelBlueprintTrialMod.Runtime;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.GraphRuntime;
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
            Assert.That(runtime.Metrics.Detail, Does.Contain("BT Script"));
            Assert.That(runtime.GuardCount, Is.GreaterThanOrEqualTo(8));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(ShowcaseThinkBudgetMs));
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
                if (runtime.Metrics.Detail.Contains("patrol leaf yielding", StringComparison.Ordinal))
                {
                    sawYield = true;
                    break;
                }
            }

            Assert.That(sawYield, Is.True, "Expected patrol ActionLib leaf to yield and resume across think waves.");
        }

        [Test]
        public void HfsmSentryArena_GateVignette_ThinkWavesUnderBudget()
        {
            var runtime = new HfsmSentryArenaRuntime();
            runtime.Bind(_programs, _actions, _behavior);
            runtime.EnsureWorld();
            Warm(runtime.Tick);
            Drive(runtime.Tick, runtime.Metrics);
            Assert.That(runtime.Metrics.Detail, Does.Contain("HFSM"));
            Assert.That(runtime.SentryCount, Is.GreaterThanOrEqualTo(8));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(ShowcaseThinkBudgetMs));
        }

        [Test]
        public void LevelBlueprintTrial_SpawnClearGate_AdvancesPhaseUnderBudget()
        {
            var runtime = new LevelBlueprintTrialRuntime();
            runtime.Bind(_programs, _actions);
            runtime.EnsureWorld();
            Warm(runtime.Tick, waves: 40);
            Assert.That(runtime.Metrics.Detail, Does.Contain("Level Script"));
            Assert.That(runtime.Director!.Phase, Is.GreaterThanOrEqualTo(2));
            Assert.That(runtime.GateOpen, Is.True);
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(ShowcaseThinkBudgetMs));
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
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(ShowcaseThinkBudgetMs));
        }

        [Test]
        public void GraphBehaviorIntegration_ShortPlay_UnderBudget()
        {
            var runtime = new GraphBehaviorIntegrationRuntime();
            runtime.Bind(_programs, _actions, _behavior);
            runtime.EnsureWorld();
            Warm(runtime.Tick, waves: 10);
            Drive(runtime.Tick, runtime.Metrics);
            Assert.That(runtime.Metrics.Detail, Does.Contain("Integration"));
            Assert.That(runtime.GuardCount, Is.EqualTo(6));
            Assert.That(runtime.SentryCount, Is.EqualTo(6));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(ShowcaseThinkBudgetMs));
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
