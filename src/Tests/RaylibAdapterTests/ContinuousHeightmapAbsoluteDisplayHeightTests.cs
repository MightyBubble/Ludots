using System.Numerics;
using Ludots.Raylib.Render;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter
{
    /// <summary>
    /// Absolute-elevation tinting must not silently flatten authored relief. The mass navigation
    /// large-world relief peaks at ~417 m; with the default 36 m peak span every real vertex lands
    /// outside [0, span] and used to collapse to the sea plane, leaving grounded agents floating.
    /// </summary>
    [TestFixture]
    public sealed class ContinuousHeightmapAbsoluteDisplayHeightTests
    {
        private const float MassNavigationPeakCm = 41661f;
        private const float MassNavigationOriginCm = 28770f;
        private const float SentinelCm = 65535f;

        [Test]
        public void AuthoredReliefAboveDefaultSpanKeepsItsElevation()
        {
            float display = RaylibContinuousHeightmapRenderer.ResolveAbsoluteDisplayHeightCm(
                MassNavigationOriginCm,
                seaLevelCm: 0f,
                absolutePeakSpanCm: 3600f);

            Assert.That(display, Is.EqualTo(MassNavigationOriginCm),
                "Authored relief must keep its height even when it exceeds the tint peak span.");
        }

        [Test]
        public void AuthoredReliefPeakKeepsItsElevation()
        {
            float display = RaylibContinuousHeightmapRenderer.ResolveAbsoluteDisplayHeightCm(
                MassNavigationPeakCm,
                seaLevelCm: 0f,
                absolutePeakSpanCm: 3600f);

            Assert.That(display, Is.EqualTo(MassNavigationPeakCm));
        }

        [Test]
        public void SubmergedDepthCollapsesToSeaPlane()
        {
            float display = RaylibContinuousHeightmapRenderer.ResolveAbsoluteDisplayHeightCm(
                -8200f,
                seaLevelCm: 0f,
                absolutePeakSpanCm: 3600f);

            Assert.That(display, Is.EqualTo(0f),
                "Below-sea samples stay on the sea plane so continental pits do not excavate.");
        }

        [Test]
        public void OvershootSentinelCollapsesToSeaPlane()
        {
            float display = RaylibContinuousHeightmapRenderer.ResolveAbsoluteDisplayHeightCm(
                SentinelCm,
                seaLevelCm: 0f,
                absolutePeakSpanCm: 3600f);

            Assert.That(display, Is.EqualTo(0f),
                "Void/ocean sentinels far above the authored span stay on the sea plane.");
        }

        [Test]
        public void SentinelDetectionUsesAbsoluteOvershootNotRelativeSpan()
        {
            // A tight span must not turn ordinary relief into a sentinel.
            float display = RaylibContinuousHeightmapRenderer.ResolveAbsoluteDisplayHeightCm(
                MassNavigationOriginCm,
                seaLevelCm: 0f,
                absolutePeakSpanCm: 100f);

            Assert.That(display, Is.EqualTo(MassNavigationOriginCm));
        }
    }
}
