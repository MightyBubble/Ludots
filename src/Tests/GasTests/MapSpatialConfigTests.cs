using System.Text.Json;
using NUnit.Framework;
using Ludots.Core.Map.Board;

namespace GasTests
{
    [TestFixture]
    public class BoardConfigTests
    {
        private static readonly JsonSerializerOptions _jsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        [Test]
        public void BoardConfig_DefaultValues_AreCorrect()
        {
            var config = new BoardConfig();
            Assert.That(config.Name, Is.EqualTo("default"));
            Assert.That(config.SpatialType, Is.EqualTo("Grid"));
            Assert.That(config.WidthInTiles, Is.EqualTo(64));
            Assert.That(config.HeightInTiles, Is.EqualTo(64));
            Assert.That(config.GridCellSizeCm, Is.EqualTo(100));
            Assert.That(config.HexEdgeLengthCm, Is.EqualTo(400));
            Assert.That(config.ChunkSizeCells, Is.EqualTo(64));
            Assert.That(config.NavigationEnabled, Is.False);
            Assert.That(config.DataFile, Is.Null);
        }

        [Test]
        public void BoardConfig_CustomValues_ArePreserved()
        {
            var config = new BoardConfig
            {
                Name = "battle",
                SpatialType = "Hex",
                WidthInTiles = 128,
                HeightInTiles = 128,
                GridCellSizeCm = 200,
                HexEdgeLengthCm = 600,
                ChunkSizeCells = 32,
                NavigationEnabled = true,
                DataFile = "Data/Maps/battle.vtxm"
            };

            Assert.That(config.Name, Is.EqualTo("battle"));
            Assert.That(config.SpatialType, Is.EqualTo("Hex"));
            Assert.That(config.WidthInTiles, Is.EqualTo(128));
            Assert.That(config.HeightInTiles, Is.EqualTo(128));
            Assert.That(config.GridCellSizeCm, Is.EqualTo(200));
            Assert.That(config.HexEdgeLengthCm, Is.EqualTo(600));
            Assert.That(config.ChunkSizeCells, Is.EqualTo(32));
            Assert.That(config.NavigationEnabled, Is.True);
            Assert.That(config.DataFile, Is.EqualTo("Data/Maps/battle.vtxm"));
        }

        [Test]
        public void BoardConfig_Clone_ProducesIndependentCopy()
        {
            var original = new BoardConfig
            {
                Name = "world",
                SpatialType = "Hex",
                WidthInTiles = 256,
                DataFile = "terrain.vtxm"
            };

            var clone = original.Clone();
            Assert.That(clone.Name, Is.EqualTo("world"));
            Assert.That(clone.SpatialType, Is.EqualTo("Hex"));
            Assert.That(clone.WidthInTiles, Is.EqualTo(256));
            Assert.That(clone.DataFile, Is.EqualTo("terrain.vtxm"));

            // Modify clone, original unchanged
            clone.WidthInTiles = 512;
            Assert.That(original.WidthInTiles, Is.EqualTo(256));
        }

        [Test]
        public void Deserialize_BoardConfig_FromJson()
        {
            string json = """
            {
                "name": "strategic",
                "spatialType": "Hex",
                "widthInTiles": 128,
                "heightInTiles": 128,
                "hexEdgeLengthCm": 600,
                "chunkSizeCells": 32,
                "navigationEnabled": true
            }
            """;

            var config = JsonSerializer.Deserialize<BoardConfig>(json, _jsonOpts);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Name, Is.EqualTo("strategic"));
            Assert.That(config.SpatialType, Is.EqualTo("Hex"));
            Assert.That(config.WidthInTiles, Is.EqualTo(128));
            Assert.That(config.HeightInTiles, Is.EqualTo(128));
            Assert.That(config.HexEdgeLengthCm, Is.EqualTo(600));
            Assert.That(config.ChunkSizeCells, Is.EqualTo(32));
            Assert.That(config.NavigationEnabled, Is.True);
        }
    }
}
