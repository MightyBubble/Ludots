using Ludots.Adapter.Raylib;
using Ludots.UI.Browser;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibBrowserLayerRendererTests
{
    [Test]
    public void ApplyAlphaMode_Preserve_LeavesBrowserAlphaUntouched()
    {
        byte[] rgba =
        {
            10, 20, 30, 0,
            40, 50, 60, 1,
            70, 80, 90, 128,
            100, 110, 120, 255
        };

        RaylibBrowserLayerRenderer.ApplyAlphaMode(rgba, BrowserSurfaceAlphaMode.Preserve);

        Assert.That(rgba[3], Is.EqualTo(0));
        Assert.That(rgba[7], Is.EqualTo(1));
        Assert.That(rgba[11], Is.EqualTo(128));
        Assert.That(rgba[15], Is.EqualTo(255));
    }

    [Test]
    public void ApplyAlphaMode_PromoteNonTransparentToOpaque_KeepsTransparentGapsTransparent()
    {
        byte[] rgba =
        {
            10, 20, 30, 0,
            40, 50, 60, 1,
            70, 80, 90, 128,
            100, 110, 120, 255
        };

        RaylibBrowserLayerRenderer.ApplyAlphaMode(rgba, BrowserSurfaceAlphaMode.PromoteNonTransparentToOpaque);

        Assert.That(rgba[3], Is.EqualTo(0));
        Assert.That(rgba[7], Is.EqualTo(255));
        Assert.That(rgba[11], Is.EqualTo(255));
        Assert.That(rgba[15], Is.EqualTo(255));
    }
}
