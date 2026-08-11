using CapabilityStandardScriptFlowSandboxMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class ScriptFlowSandboxShowcaseAcceptanceTests
    {
        [Test]
        public void DrinkUntilFull_YieldsThenHaltsAtLimit()
        {
            var runtime = new ScriptFlowSandboxRuntime();
            runtime.EnsureWorld();
            for (int i = 0; i < 20 && !runtime.Halted; i++)
            {
                runtime.Tick(0.2f);
            }

            Assert.That(runtime.Halted, Is.True);
            Assert.That(runtime.Water, Is.EqualTo(runtime.Limit));
            Assert.That(runtime.Metrics.Detail, Does.Contain("Script halted"));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(5.0));
        }
    }
}
