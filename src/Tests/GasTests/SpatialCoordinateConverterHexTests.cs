using Ludots.Core.Map.Hex;
using Ludots.Core.Spatial;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class SpatialCoordinateConverterHexTests
    {
        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(0, 1)]
        [TestCase(-2, 3)]
        [TestCase(5, -4)]
        public void HexToWorldToHex_RoundTrips(int q, int r)
        {
            var coords = new SpatialCoordinateConverter(gridCellSizeCm: 100);
            var hex = new HexCoordinates(q, r);
            var world = coords.HexToWorld(hex);
            var back = coords.WorldToHex(world);
            That(back, Is.EqualTo(hex));
        }

        // ── Custom HexMetrics tests ──

        [TestCase(0, 0)]
        [TestCase(3, -2)]
        [TestCase(-1, 5)]
        public void HexMetrics_Custom_HexToWorldToHex_RoundTrips(int q, int r)
        {
            // Use a non-default edge length (600cm = 6m instead of 400cm)
            var metrics = new HexMetrics(600);
            var hex = new HexCoordinates(q, r);
            var worldPos = metrics.HexToWorldCm(hex.Q, hex.R);
            var back = metrics.WorldCmToHex(worldPos.X, worldPos.Z);
            That(back, Is.EqualTo(hex), $"Failed for custom metrics at ({q},{r})");
        }

        [TestCase(200)]
        [TestCase(400)]
        [TestCase(800)]
        [TestCase(1600)]
        public void HexMetrics_DifferentEdgeLengths_AllRoundTrip(int edgeLengthCm)
        {
            var metrics = new HexMetrics(edgeLengthCm);
            var testHexes = new[]
            {
                new HexCoordinates(0, 0),
                new HexCoordinates(1, 0),
                new HexCoordinates(0, 1),
                new HexCoordinates(-3, 5),
                new HexCoordinates(7, -4),
            };

            foreach (var hex in testHexes)
            {
                var worldPos = metrics.HexToWorldCm(hex.Q, hex.R);
                var back = metrics.WorldCmToHex(worldPos.X, worldPos.Z);
                That(back, Is.EqualTo(hex), $"Failed for edge={edgeLengthCm}cm at {hex}");
            }
        }

        [Test]
        public void HexMetrics_Default_MatchesLegacyConverter()
        {
            var converter = new SpatialCoordinateConverter(gridCellSizeCm: 100);
            var metrics = HexMetrics.Default;
            var hex = new HexCoordinates(4, -2);

            var converterWorld = converter.HexToWorld(hex);
            var metricsWorld = metrics.HexToWorldCm(hex.Q, hex.R);

            // Should be approximately equal (converter uses float internally)
            That(metricsWorld.X, Is.EqualTo(converterWorld.X).Within(2));
            That(metricsWorld.Z, Is.EqualTo(converterWorld.Y).Within(2));
        }
    }
}

