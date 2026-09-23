using Ludots.Client.WebGpu.Runtime;
using NUnit.Framework;

namespace Ludots.Adapter.WebGpu.Tests;

[TestFixture]
public sealed class WebGpuWorldHudAnchorProjectionTests
{
    [Test]
    public void ShouldSuppress_WhenInvisibleOrBehindCamera()
    {
        Assert.That(WebGpuWorldHudAnchorProjection.ShouldSuppress(visibility: 1f, clipW: 1f), Is.False);
        Assert.That(WebGpuWorldHudAnchorProjection.ShouldSuppress(visibility: 0f, clipW: 1f), Is.True);
        Assert.That(WebGpuWorldHudAnchorProjection.ShouldSuppress(visibility: -1f, clipW: 1f), Is.True);
        Assert.That(
            WebGpuWorldHudAnchorProjection.ShouldSuppress(visibility: 1f, clipW: WebGpuWorldHudAnchorProjection.ClipWEpsilon),
            Is.True);
        Assert.That(
            WebGpuWorldHudAnchorProjection.ShouldSuppress(visibility: 1f, clipW: -0.5f),
            Is.True);
        Assert.That(
            WebGpuWorldHudAnchorProjection.ShouldSuppress(
                visibility: 1f,
                clipW: WebGpuWorldHudAnchorProjection.ClipWEpsilon + 1e-5f),
            Is.False);
    }
}
