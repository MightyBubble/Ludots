using CapabilityStandardBehaviorTreeArenaMod.Runtime;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphBehaviorIntegrationMod.Runtime;
using CapabilityStandardHfsmSentryArenaMod.Runtime;
using CapabilityStandardLevelBlueprintTrialMod.Runtime;
using CapabilityStandardSkillGraphSandboxMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    /// <summary>
    /// Separated showcases: each test boots exactly one capability runtime.
    /// Integration is a fifth, separate demo — not a mash-up of the solo arenas' content goals.
    /// </summary>
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphBehaviorSeparatedShowcaseAcceptanceTests
    {
        [Test]
        public void BehaviorTreeArena_Only_ThinkWavesUnderBudget()
        {
            var runtime = new BehaviorTreeArenaRuntime();
            runtime.EnsureWorld();
            Warm(runtime.Tick);
            Drive(runtime.Tick, runtime.Metrics);
            Assert.That(runtime.Metrics.ShowcaseId, Is.EqualTo("capability_standard_behavior_tree_arena"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("BT-only"));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(5.0));
        }

        [Test]
        public void HfsmSentryArena_Only_ThinkWavesUnderBudget()
        {
            var runtime = new HfsmSentryArenaRuntime();
            runtime.EnsureWorld();
            Warm(runtime.Tick);
            Drive(runtime.Tick, runtime.Metrics);
            Assert.That(runtime.Metrics.ShowcaseId, Is.EqualTo("capability_standard_hfsm_sentry_arena"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("HFSM-only"));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(5.0));
        }

        [Test]
        public void LevelBlueprintTrial_Only_AdvancesPhaseUnderBudget()
        {
            var runtime = new LevelBlueprintTrialRuntime();
            runtime.EnsureWorld();
            Warm(runtime.Tick, waves: 12);
            Assert.That(runtime.Metrics.ShowcaseId, Is.EqualTo("capability_standard_level_blueprint_trial"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("Level-only"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("phase=2"));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(5.0));
        }

        [Test]
        public void SkillGraphSandbox_Only_CastsFuncLibUnderBudget()
        {
            var runtime = new SkillGraphSandboxRuntime();
            runtime.EnsureWorld();
            Warm(runtime.Tick);
            Drive(runtime.Tick, runtime.Metrics);
            Assert.That(runtime.Metrics.ShowcaseId, Is.EqualTo("capability_standard_skill_graph_sandbox"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("Skill-only"));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(5.0));
        }

        [Test]
        public void GraphBehaviorIntegration_SeparateDemo_UnderBudget()
        {
            var runtime = new GraphBehaviorIntegrationRuntime();
            runtime.EnsureWorld();
            Warm(runtime.Tick, waves: 10);
            Drive(runtime.Tick, runtime.Metrics);
            Assert.That(runtime.Metrics.ShowcaseId, Is.EqualTo("capability_standard_graph_behavior_integration"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("Integration"));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(5.0));
        }

        private static void Warm(System.Action<float> tick, int waves = 5)
        {
            for (int i = 0; i < waves; i++) tick(0.2f);
        }

        private static void Drive(System.Action<float> tick, GraphShowcaseMetrics metrics, int waves = 20)
        {
            // Discard JIT/cache spikes inside the measured window.
            for (int i = 0; i < 3; i++) tick(0.2f);
            metrics.MaxThinkMs = 0;
            metrics.LastThinkMs = 0;
            for (int i = 0; i < waves; i++) tick(0.2f);
            TestContext.WriteLine($"{metrics.ShowcaseId}: waves={metrics.ThinkWaves} max={metrics.MaxThinkMs:F3} detail={metrics.Detail}");
        }
    }
}
