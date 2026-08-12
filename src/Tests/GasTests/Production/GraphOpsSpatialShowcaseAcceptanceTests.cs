using CapabilityStandardGraphOpsSpatialMod.Runtime;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.GraphRuntime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphOpsSpatialShowcaseAcceptanceTests
    {
        [Test]
        public void SpatialQueries_ConeRectLineHex_UnderBudgetWithPlayerReadableDetail()
        {
            GraphProgramRegistry programs = GraphOpsSpatialCatalogBootstrap.Load(out GraphFunctionCatalog catalog);
            using var runtime = new GraphOpsSpatialRuntime();
            runtime.Bind(programs, catalog);
            runtime.EnsureWorld();

            Warm(runtime.Tick);
            Drive(runtime.Tick, runtime.Metrics);

            Assert.Multiple(() =>
            {
                Assert.That(runtime.Metrics.Detail, Does.Contain("扇形"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("矩形"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("直线"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("六角圈人"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("最近目标"));
            });
            Assert.That(runtime.TargetCount, Is.EqualTo(8));
            Assert.That(runtime.ConeHits + runtime.RectHits + runtime.LineHits, Is.GreaterThan(0));
            Assert.That(runtime.HexRangeHits + runtime.HexRingHits + runtime.HexNeighborHits, Is.GreaterThan(0));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(25.0));
        }

        private static void Warm(System.Action<float> tick, int waves = 8)
        {
            for (int i = 0; i < waves; i++) tick(0.2f);
        }

        private static void Drive(System.Action<float> tick, GraphShowcaseMetrics metrics, int waves = 16)
        {
            for (int i = 0; i < 4; i++) tick(0.2f);
            metrics.MaxThinkMs = 0;
            metrics.LastThinkMs = 0;
            for (int i = 0; i < waves; i++) tick(0.2f);
            TestContext.WriteLine($"{metrics.ShowcaseId}: waves={metrics.ThinkWaves} max={metrics.MaxThinkMs:F3} detail={metrics.Detail}");
        }
    }
}
