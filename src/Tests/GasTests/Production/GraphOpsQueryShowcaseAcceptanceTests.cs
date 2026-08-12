using CapabilityStandardGraphOpsQueryMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphOpsQueryShowcaseAcceptanceTests
    {
        [Test]
        public void QueryGallery_CompilesFilterPipeline_AndSpeaksPlayerLanguage()
        {
            var runtime = new GraphOpsQueryRuntime();
            Assert.DoesNotThrow(() => runtime.EnsureWorld());
            Assert.That(runtime.CompiledGraphs, Is.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Metrics.Detail, Does.Contain("全图搜人"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("阵营"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("标签"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("生命值"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("最强"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("小队"));
            });
        }
    }
}
