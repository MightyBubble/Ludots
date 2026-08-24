using Ludots.WebUI.PanelKit;
using NUnit.Framework;
using UiRegionsMod.Runtime;

namespace Ludots.Tests.GAS.Integration
{
    [TestFixture]
    public sealed class Y5kUiRegionsCatalogTests
    {
        [Test]
        public void NineGridRegions_AreRegistered()
        {
            var catalog = UiRegionsCatalogFactory.Create(_ => true);
            foreach (string region in WebUiNineGridRegions.All)
            {
                Assert.That(catalog.SurfaceRegions.Contains(region), Is.True, region);
            }

            Assert.That(WebUiNineGridRegions.All, Has.Member(WebUiNineGridRegions.MiddleLeft));
            Assert.That(WebUiNineGridRegions.All, Has.Member(WebUiNineGridRegions.MiddleRight));
            Assert.That(WebUiNineGridRegions.Center, Is.EqualTo("region.center"));
        }
    }
}
