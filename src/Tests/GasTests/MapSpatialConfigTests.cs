using System.Text.Json;
using NUnit.Framework;
using Ludots.Core.Config;

namespace GasTests
{
    [TestFixture]
    public class MapSpatialConfigTests
    {
        private static readonly JsonSerializerOptions _jsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        [Test]
        public void Deserialize_WithSpatialSection_ParsesCorrectly()
        {
            string json = """
            {
                "id": "test_map",
                "spatial": {
                    "spatialType": "Hex",
                    "widthInTiles": 128,
                    "heightInTiles": 128,
                    "gridCellSizeCm": 200,
                    "hexEdgeLengthCm": 600,
                    "chunkSizeCells": 32
                }
            }
            """;

            var config = JsonSerializer.Deserialize<MapConfig>(json, _jsonOpts);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Spatial, Is.Not.Null);
            Assert.That(config.Spatial!.SpatialType, Is.EqualTo("Hex"));
            Assert.That(config.Spatial.WidthInTiles, Is.EqualTo(128));
            Assert.That(config.Spatial.HeightInTiles, Is.EqualTo(128));
            Assert.That(config.Spatial.GridCellSizeCm, Is.EqualTo(200));
            Assert.That(config.Spatial.HexEdgeLengthCm, Is.EqualTo(600));
            Assert.That(config.Spatial.ChunkSizeCells, Is.EqualTo(32));
        }

        [Test]
        public void Deserialize_WithoutSpatialSection_UsesDefaults()
        {
            string json = """{ "id": "simple_map" }""";

            var config = JsonSerializer.Deserialize<MapConfig>(json, _jsonOpts);
            Assert.That(config!.Spatial, Is.Null);

            // When Spatial is null, engine should fall back to global defaults.
            // Verify that a newly created MapSpatialConfig has the right defaults.
            var defaults = new MapSpatialConfig();
            Assert.That(defaults.SpatialType, Is.EqualTo("Grid"));
            Assert.That(defaults.WidthInTiles, Is.EqualTo(64));
            Assert.That(defaults.HeightInTiles, Is.EqualTo(64));
            Assert.That(defaults.GridCellSizeCm, Is.EqualTo(100));
            Assert.That(defaults.HexEdgeLengthCm, Is.EqualTo(400));
            Assert.That(defaults.ChunkSizeCells, Is.EqualTo(64));
        }

        [Test]
        public void Deserialize_PartialSpatial_MergesWithDefaults()
        {
            string json = """
            {
                "id": "partial_map",
                "spatial": {
                    "spatialType": "Hybrid",
                    "widthInTiles": 256
                }
            }
            """;

            var config = JsonSerializer.Deserialize<MapConfig>(json, _jsonOpts);
            Assert.That(config!.Spatial!.SpatialType, Is.EqualTo("Hybrid"));
            Assert.That(config.Spatial.WidthInTiles, Is.EqualTo(256));
            // Unspecified fields should have defaults
            Assert.That(config.Spatial.HeightInTiles, Is.EqualTo(64));
            Assert.That(config.Spatial.GridCellSizeCm, Is.EqualTo(100));
            Assert.That(config.Spatial.HexEdgeLengthCm, Is.EqualTo(400));
            Assert.That(config.Spatial.ChunkSizeCells, Is.EqualTo(64));
        }

        [Test]
        public void SpatialType_Grid_IsDefault()
        {
            var spatial = new MapSpatialConfig();
            Assert.That(spatial.SpatialType, Is.EqualTo("Grid"));
        }

        [Test]
        public void SpatialType_Hex_Configurable()
        {
            var spatial = new MapSpatialConfig { SpatialType = "Hex" };
            Assert.That(spatial.SpatialType, Is.EqualTo("Hex"));
        }

        [Test]
        public void SpatialType_Hybrid_Configurable()
        {
            var spatial = new MapSpatialConfig { SpatialType = "Hybrid" };
            Assert.That(spatial.SpatialType, Is.EqualTo("Hybrid"));
        }

        [Test]
        public void HexEdgeLengthCm_OverridesDefault()
        {
            var spatial = new MapSpatialConfig { HexEdgeLengthCm = 800 };
            Assert.That(spatial.HexEdgeLengthCm, Is.EqualTo(800));
        }

        [Test]
        public void GridCellSizeCm_PerMapOverridesGlobal()
        {
            var spatial = new MapSpatialConfig { GridCellSizeCm = 50 };
            Assert.That(spatial.GridCellSizeCm, Is.EqualTo(50));
        }

        [Test]
        public void WidthHeight_FromConfig_AreIndependent()
        {
            var spatial = new MapSpatialConfig { WidthInTiles = 32, HeightInTiles = 16 };
            Assert.That(spatial.WidthInTiles, Is.EqualTo(32));
            Assert.That(spatial.HeightInTiles, Is.EqualTo(16));
        }
    }
}
