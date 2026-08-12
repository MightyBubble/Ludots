using CapabilityStandardLiveSkillWorkbenchShowcaseMod.Runtime;
using Ludots.Core.Gameplay.GAS.LiveSkillWorkbench;
using Ludots.Core.GraphRuntime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    [TestFixture]
    public sealed class LiveSkillWorkbenchVignetteShowcaseAcceptanceTests
    {
        [Test]
        [Category("ci-gate")]
        public void Vignette_CyclesBeats_AndDamagesDummy()
        {
            var runtime = new LiveSkillWorkbenchVignetteRuntime();
            var graphs = new GraphProgramRegistry();
            var pipeline = new LiveGasEditPipeline(graphs);
            var tracer = new LiveEffectChainTracer(64);
            runtime.Bind(graphs, pipeline, tracer);
            runtime.EnsureWorld();

            float startDummy = runtime.DummyHp01;
            // Drive long enough to complete weak cast impact.
            for (int i = 0; i < 240; i++)
            {
                runtime.Tick(1f / 60f);
            }

            Assert.That(runtime.DummyHp01, Is.LessThan(startDummy));
            Assert.That(runtime.Metrics.Detail, Does.Contain("LSW vignette"));
            Assert.That((int)runtime.CurrentBeat, Is.GreaterThanOrEqualTo(0));
        }
    }
}
