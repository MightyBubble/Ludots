using System;
using NUnit.Framework;
using Ludots.Core.Map.Hex;

namespace GasTests
{
    [TestFixture]
    public class HexMetricsTests
    {
        [Test]
        public void DefaultEdgeLength_400cm_MatchesLegacyConstants()
        {
            var metrics = HexMetrics.Default;
            Assert.That(metrics.EdgeLengthCm, Is.EqualTo(400));
            Assert.That(metrics.EdgeLength, Is.EqualTo(4.0f).Within(0.01f));
            Assert.That(metrics.HexWidth, Is.EqualTo(HexCoordinates.HexWidth).Within(0.01f));
            Assert.That(metrics.HexHeight, Is.EqualTo(HexCoordinates.HexHeight).Within(0.01f));
            Assert.That(metrics.RowSpacing, Is.EqualTo(HexCoordinates.RowSpacing).Within(0.01f));
        }

        [Test]
        public void CustomEdgeLength_ScalesWidthAndSpacing()
        {
            var metrics = new HexMetrics(800); // 8m edge
            Assert.That(metrics.EdgeLengthCm, Is.EqualTo(800));
            Assert.That(metrics.EdgeLength, Is.EqualTo(8.0f).Within(0.01f));
            // Width = sqrt(3) * 8 ≈ 13.856
            Assert.That(metrics.HexWidth, Is.EqualTo(8.0f * 1.7320508f).Within(0.01f));
            // Height = 2 * 8 = 16
            Assert.That(metrics.HexHeight, Is.EqualTo(16.0f).Within(0.01f));
            // RowSpacing = 1.5 * 8 = 12
            Assert.That(metrics.RowSpacing, Is.EqualTo(12.0f).Within(0.01f));
        }

        [Test]
        public void HexToWorld_WithDefaultMetrics_MatchesLegacy()
        {
            var metrics = HexMetrics.Default;
            var hex = new HexCoordinates(3, 2);

            var metricsPos = metrics.HexToWorldCm(hex.Q, hex.R);
            var legacyPos = hex.ToWorldPositionCm();

            Assert.That(metricsPos.X, Is.EqualTo(legacyPos.X).Within(1f));
            Assert.That(metricsPos.Z, Is.EqualTo(legacyPos.Z).Within(1f));
        }

        [Test]
        public void WorldToHex_WithCustomMetrics_RoundTrips()
        {
            var metrics = new HexMetrics(600); // 6m edge
            var original = new HexCoordinates(5, -3);

            var worldPos = metrics.HexToWorldCm(original.Q, original.R);
            var roundTripped = metrics.WorldCmToHex(worldPos.X, worldPos.Z);

            Assert.That(roundTripped.Q, Is.EqualTo(original.Q));
            Assert.That(roundTripped.R, Is.EqualTo(original.R));
        }

        [Test]
        public void HexToWorld_WithCustomMetrics_DifferentFromDefault()
        {
            var defaultMetrics = HexMetrics.Default;
            var customMetrics = new HexMetrics(200); // 2m edge (half of default)

            var hex = new HexCoordinates(1, 0);
            var defaultPos = defaultMetrics.HexToWorldCm(hex.Q, hex.R);
            var customPos = customMetrics.HexToWorldCm(hex.Q, hex.R);

            // Custom (200cm edge) should produce positions at half the distance of default (400cm edge)
            Assert.That(customPos.X, Is.EqualTo(defaultPos.X / 2f).Within(1f));
        }

        [Test]
        public void BoundingBox_ScalesWithEdgeLength()
        {
            var m1 = new HexMetrics(400);
            var m2 = new HexMetrics(800);

            // Bounding half-width should roughly double
            Assert.That(m2.BoundingHalfWidthCm, Is.GreaterThan(m1.BoundingHalfWidthCm));
            Assert.That(m2.BoundingHalfHeightCm, Is.GreaterThan(m1.BoundingHalfHeightCm));
        }

        [Test]
        public void Constructor_ZeroEdgeLength_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HexMetrics(0));
        }

        [Test]
        public void Constructor_NegativeEdgeLength_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HexMetrics(-100));
        }
    }
}
