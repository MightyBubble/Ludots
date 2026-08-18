using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryPanelOpAcceptanceTests
{
    [Test]
    public void ShowPanelVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("ShowPanel");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"ShowPanel detail missing phrase: {runtime.Metrics.Detail}");
        }
    }

    [Test]
    public void HidePanelVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("HidePanel");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"HidePanel detail missing phrase: {runtime.Metrics.Detail}");
        }
    }
}
