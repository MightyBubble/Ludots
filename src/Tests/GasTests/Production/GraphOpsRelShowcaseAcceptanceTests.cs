using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphOpsRelMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class GraphOpsRelShowcaseAcceptanceTests
    {
        [Test]
        public void RegistryName_DelegatesToSeparatedSuite()
        {
            var runtime = new GraphOpsRelRuntime();
            runtime.BindStandaloneFromModAssets();
            runtime.EnsureWorld();
            for (int i = 0; i < 12; i++) runtime.Tick(0.2f);
            AssertRelPlayerCopy(runtime);
        }

        [Test]
        public void GraphOpsRel_FriendChainRankAndUnlink_UnderBudget()
        {
            var runtime = new GraphOpsRelRuntime();
            runtime.BindStandaloneFromModAssets();
            runtime.EnsureWorld();

            Warm(runtime.Tick);
            Drive(runtime.Tick, runtime.Metrics);

            AssertRelPlayerCopy(runtime);
            Assert.That(runtime.FriendCount, Is.GreaterThan(0));
            Assert.That(runtime.LoyaltyTop, Is.GreaterThan(runtime.LoyaltyAverage));
            Assert.That(runtime.LoyaltySum, Is.GreaterThan(0));
            Assert.That(runtime.LoyaltyMin, Is.GreaterThan(0));
            Assert.That(runtime.IncomingCount, Is.GreaterThan(0));
            Assert.That(runtime.BetweenCount, Is.GreaterThan(0));
            Assert.That(runtime.BrokenLinks, Is.GreaterThan(0));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(25.0));
        }

        private static void AssertRelPlayerCopy(GraphOpsRelRuntime runtime)
        {
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Metrics.Detail, Does.Contain("查好友链"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("好感排序"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("拆链").Or.Contain("Trusted"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("Trusted"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("区间"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("总和"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("好感最低").Or.Contain("最低"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("最弱"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("双人链"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("失和"));
                Assert.That(runtime.Metrics.Detail, Does.Contain(runtime.LoyaltySum.ToString()));
                Assert.That(runtime.Metrics.Detail, Does.Contain(runtime.LoyaltyMin.ToString()));
                Assert.That(runtime.Metrics.Detail, Does.Contain(runtime.WeakFriendLabel));
                Assert.That(runtime.Metrics.Detail, Does.Contain(runtime.BetweenCount.ToString()));
            });
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
