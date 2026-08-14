using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphOpsQueryMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class GraphOpsQueryShowcaseAcceptanceTests
    {
        [SetUp]
        public void ClearGraphIdsForStandaloneBootstrap()
        {
            GraphIdRegistry.Clear();
        }

        [Test]
        public void QueryGallery_ExecutesFilterAndRoster_WithLivePlayerCopy()
        {
            using var runtime = new GraphOpsQueryRuntime();
            runtime.BindStandaloneFromModAssets();
            runtime.EnsureWorld();

            Warm(runtime.Tick);
            Drive(runtime.Tick, runtime.Metrics);

            Assert.Multiple(() =>
            {
                Assert.That(runtime.Metrics.Detail, Does.Contain("全图搜到"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("敌阵营"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("敌军"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("排除阵亡"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("生命区间"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("总和"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("均值"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("最强"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("最弱"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("生命"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("花名册"));
                Assert.That(runtime.Metrics.Detail, Does.Contain(runtime.AllCount.ToString()));
                Assert.That(runtime.Metrics.Detail, Does.Contain(runtime.RangeCount.ToString()));
                Assert.That(runtime.Metrics.Detail, Does.Contain($"{runtime.SumHp:F0}"));
                Assert.That(runtime.Metrics.Detail, Does.Contain($"{runtime.AvgHp:F0}"));
                Assert.That(runtime.Metrics.Detail, Does.Contain($"{runtime.MaxHp:F0}"));
                Assert.That(runtime.Metrics.Detail, Does.Contain($"{runtime.MinHp:F0}"));
                Assert.That(runtime.Metrics.Detail, Does.Contain(runtime.StrongestLabel));
                Assert.That(runtime.Metrics.Detail, Does.Contain(runtime.WeakestLabel));
                Assert.That(runtime.Metrics.Detail, Does.Contain(runtime.SquadCount.ToString()));
            });
            Assert.That(runtime.CompiledGraphs, Is.EqualTo(2));
            Assert.That(runtime.AllCount, Is.EqualTo(GraphOpsQueryRuntime.SeededMapEntityCount));
            Assert.That(runtime.RangeCount, Is.EqualTo(6));
            Assert.That(runtime.SumHp, Is.EqualTo(350f));
            Assert.That(runtime.MaxHp, Is.EqualTo(100f));
            Assert.That(runtime.MinHp, Is.EqualTo(10f));
            Assert.That(runtime.SquadCount, Is.EqualTo(4));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(25.0));
        }

        private static void Warm(System.Action<float> tick, int waves = 5)
        {
            for (int i = 0; i < waves; i++) tick(0.2f);
        }

        private static void Drive(System.Action<float> tick, GraphShowcaseMetrics metrics, int waves = 16)
        {
            for (int i = 0; i < 3; i++) tick(0.2f);
            metrics.MaxThinkMs = 0;
            metrics.LastThinkMs = 0;
            for (int i = 0; i < waves; i++) tick(0.2f);
            TestContext.WriteLine($"{metrics.ShowcaseId}: waves={metrics.ThinkWaves} max={metrics.MaxThinkMs:F3} detail={metrics.Detail}");
        }
    }
}
