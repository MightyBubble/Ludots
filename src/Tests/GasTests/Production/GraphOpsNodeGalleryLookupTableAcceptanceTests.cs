using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class GraphOpsNodeGalleryLookupTableAcceptanceTests
    {
        private static readonly string[] LookupOps =
        {
            "ResolveTableRow",
            "TableReadInt",
            "TableReadFloat",
        };

        [Test]
        public void LookupTableVignettes_CompileWithFeaturedOp()
        {
            foreach (string op in LookupOps)
            {
                using var runtime = new GraphOpsNodeGalleryRuntime();
                runtime.BindOp(op);
                runtime.EnsureWorld();
                runtime.Tick(0.35f);

                foreach (string phrase in runtime.Vignette.AssertDetailContains)
                {
                    Assert.That(
                        runtime.Metrics.Detail,
                        Does.Contain(phrase),
                        $"{op} detail missing phrase '{phrase}': {runtime.Metrics.Detail}");
                }
            }
        }

        [Test]
        public void ResolveTableRow_VignetteSettlesDrainOnTarget()
        {
            using var runtime = new GraphOpsNodeGalleryRuntime();
            runtime.BindOp("ResolveTableRow");
            runtime.EnsureWorld();
            float before = runtime.Context.ActorHealth[1];
            runtime.Tick(0.35f);
            float after = runtime.Context.ActorHealth[1];

            Assert.That(after, Is.LessThan(before), "rank table drain must settle real damage on the target");
        }

        [Test]
        public void TableReadInt_VignetteReportsThreeStars()
        {
            using var runtime = new GraphOpsNodeGalleryRuntime();
            runtime.BindOp("TableReadInt");
            runtime.EnsureWorld();
            runtime.Tick(0.35f);

            Assert.That(runtime.Metrics.Detail, Does.Contain("3").Or.Contains("三"));
        }

        [Test]
        public void TableReadFloat_VignetteSettlesTwoDamage()
        {
            using var runtime = new GraphOpsNodeGalleryRuntime();
            runtime.BindOp("TableReadFloat");
            runtime.EnsureWorld();
            float before = runtime.Context.ActorHealth[1];
            runtime.Tick(0.35f);
            float after = runtime.Context.ActorHealth[1];

            Assert.That(before - after, Is.EqualTo(2f).Within(0.01f), "drainPower column must drive the settled damage");
        }
    }
}
