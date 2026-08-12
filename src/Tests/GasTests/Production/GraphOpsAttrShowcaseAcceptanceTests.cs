using CapabilityStandardGraphOpsAttrMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphOpsAttrShowcaseAcceptanceTests
    {
        [Test]
        public void AttrVignette_ReadHealthStrikeApplyRemove_CompletesUnderBudget()
        {
            using var runtime = new GraphOpsAttrRuntime();
            runtime.BindStandaloneFromModAssets();
            runtime.EnsureWorld();

            for (int i = 0; i < 3; i++) runtime.Tick(0.2f);
            runtime.Metrics.MaxThinkMs = 0;
            runtime.Metrics.LastThinkMs = 0;

            for (int i = 0; i < 8 && !runtime.AllPhasesComplete; i++)
            {
                runtime.Tick(0.2f);
            }

            TestContext.WriteLine(
                $"{runtime.Metrics.ShowcaseId}: waves={runtime.Metrics.ThinkWaves} max={runtime.Metrics.MaxThinkMs:F3} detail={runtime.Metrics.Detail}");

            Assert.Multiple(() =>
            {
                Assert.That(runtime.AllPhasesComplete, Is.True);
                Assert.That(runtime.Metrics.Detail, Does.Contain("卸效果"));
                Assert.That(runtime.TargetHealth, Is.LessThan(80f));
                Assert.That(runtime.PendingEffectRequests, Is.GreaterThan(0));
                Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(25.0));
            });
        }

        [Test]
        public void AttrVignette_PlayerFacingPhrases_ArePresentAcrossWaves()
        {
            using var runtime = new GraphOpsAttrRuntime();
            runtime.BindStandaloneFromModAssets();
            runtime.EnsureWorld();

            var details = new List<string>();
            for (int i = 0; i < 12 && !runtime.AllPhasesComplete; i++)
            {
                runtime.Tick(0.2f);
                details.Add(runtime.Metrics.Detail);
            }

            string joined = string.Join('\n', details);
            Assert.Multiple(() =>
            {
                Assert.That(joined, Does.Contain("读血量"));
                Assert.That(joined, Does.Contain("加伤"));
                Assert.That(joined, Does.Contain("上效果"));
                Assert.That(joined, Does.Contain("卸效果"));
            });
        }
    }
}
