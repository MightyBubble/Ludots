using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphOpsRelMod.Runtime;
using Ludots.Core.GraphRuntime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphOpsRelShowcaseAcceptanceTests
    {
        private GraphProgramRegistry _programs = null!;
        private GraphFunctionCatalog _catalog = null!;

        [SetUp]
        public void SetUp()
        {
            _programs = GraphOpsRelShowcaseBootstrap.Load(out _catalog);
        }

        [Test]
        public void RegistryName_DelegatesToSeparatedSuite()
        {
            var runtime = new GraphOpsRelRuntime();
            runtime.Bind(_programs, _catalog);
            runtime.EnsureWorld();
            for (int i = 0; i < 12; i++) runtime.Tick(0.2f);
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Metrics.Detail, Does.Contain("查好友链"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("好感排序"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("拆链"));
            });
        }

        [Test]
        public void GraphOpsRel_FriendChainRankAndUnlink_UnderBudget()
        {
            var runtime = new GraphOpsRelRuntime();
            runtime.Bind(_programs, _catalog);
            runtime.EnsureWorld();

            Warm(runtime.Tick);
            Drive(runtime.Tick, runtime.Metrics);

            Assert.Multiple(() =>
            {
                Assert.That(runtime.Metrics.Detail, Does.Contain("查好友链"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("好感排序"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("拆链"));
            });
            Assert.That(runtime.BrokenLinks, Is.GreaterThan(0));
            Assert.That(runtime.Metrics.ThinkWaves, Is.GreaterThan(0));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(25.0));
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
