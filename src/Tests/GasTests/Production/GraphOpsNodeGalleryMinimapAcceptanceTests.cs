using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryMinimapAcceptanceTests
{
    [Test]
    public void QueryMinimapRegionVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("QueryMinimapRegion");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        AssertDetail(runtime, "QueryMinimapRegion");
    }

    [Test]
    public void MinimapScreenToWorldVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("MinimapScreenToWorld");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        AssertDetail(runtime, "MinimapScreenToWorld");
    }

    [Test]
    public void MinimapJumpCameraVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("MinimapJumpCamera");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        AssertDetail(runtime, "MinimapJumpCamera");
    }

    [Test]
    public void MinimapSetZoomFromPointerVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("MinimapSetZoomFromPointer");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        AssertDetail(runtime, "MinimapSetZoomFromPointer");
    }

    [Test]
    public void MinimapTogglePresetVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("MinimapTogglePreset");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        AssertDetail(runtime, "MinimapTogglePreset");
    }

    [Test]
    public void MinimapToggleRotateVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("MinimapToggleRotate");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        AssertDetail(runtime, "MinimapToggleRotate");
    }

    [Test]
    public void MinimapApplyWheelZoomVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("MinimapApplyWheelZoom");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        AssertDetail(runtime, "MinimapApplyWheelZoom");
    }

    private static void AssertDetail(GraphOpsNodeGalleryRuntime runtime, string op)
    {
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"{op} detail missing phrase: {runtime.Metrics.Detail}");
        }
    }
}
