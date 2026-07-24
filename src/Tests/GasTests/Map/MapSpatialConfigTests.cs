using System.Text.Json;
using NUnit.Framework;
using Ludots.Core.Map.Board;
using Ludots.Core.Spatial;

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
            Assert.That(config.WidthInMacroTiles, Is.EqualTo(64));
            Assert.That(config.HeightInMacroTiles, Is.EqualTo(64));
            Assert.That(config.GridCellSizeCm, Is.EqualTo(100));
            Assert.That(config.HexEdgeLengthCm, Is.EqualTo(400));
            Assert.That(config.ChunkSizeCells, Is.EqualTo(64));
            Assert.That(config.LoadedChunkCapacity, Is.Zero);
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
                WidthInMacroTiles = 128,
                HeightInMacroTiles = 128,
                GridCellSizeCm = 200,
                HexEdgeLengthCm = 600,
                ChunkSizeCells = 32,
                LoadedChunkCapacity = 96,
                NavigationEnabled = true,
                DataFile = "Data/Maps/battle.vtxm",
                VisualHeightmapAsset = "Data/Maps/battle.vhtm"
            };

            Assert.That(config.Name, Is.EqualTo("battle"));
            Assert.That(config.SpatialType, Is.EqualTo("Hex"));
            Assert.That(config.WidthInMacroTiles, Is.EqualTo(128));
            Assert.That(config.HeightInMacroTiles, Is.EqualTo(128));
            Assert.That(config.GridCellSizeCm, Is.EqualTo(200));
            Assert.That(config.HexEdgeLengthCm, Is.EqualTo(600));
            Assert.That(config.ChunkSizeCells, Is.EqualTo(32));
            Assert.That(config.LoadedChunkCapacity, Is.EqualTo(96));
            Assert.That(config.NavigationEnabled, Is.True);
            Assert.That(config.DataFile, Is.EqualTo("Data/Maps/battle.vtxm"));
            Assert.That(config.VisualHeightmapAsset, Is.EqualTo("Data/Maps/battle.vhtm"));
        }

        [Test]
        public void BoardConfig_Clone_ProducesIndependentCopy()
        {
            var original = new BoardConfig
            {
                Name = "world",
                SpatialType = "Hex",
                WidthInMacroTiles = 256,
                LoadedChunkCapacity = 128,
                DataFile = "terrain.vtxm",
                VisualHeightmapAsset = "terrain.vhtm"
            };

            var clone = original.Clone();
            Assert.That(clone.Name, Is.EqualTo("world"));
            Assert.That(clone.SpatialType, Is.EqualTo("Hex"));
            Assert.That(clone.WidthInMacroTiles, Is.EqualTo(256));
            Assert.That(clone.LoadedChunkCapacity, Is.EqualTo(128));
            Assert.That(clone.DataFile, Is.EqualTo("terrain.vtxm"));
            Assert.That(clone.VisualHeightmapAsset, Is.EqualTo("terrain.vhtm"));

            // Modify clone, original unchanged
            clone.WidthInMacroTiles = 512;
            clone.VisualHeightmapAsset = "other.vhtm";
            Assert.That(original.WidthInMacroTiles, Is.EqualTo(256));
            Assert.That(original.VisualHeightmapAsset, Is.EqualTo("terrain.vhtm"));
        }

        [Test]
        public void Deserialize_BoardConfig_FromJson()
        {
            string json = """
            {
                "name": "strategic",
                "spatialType": "Hex",
                "widthInMacroTiles": 128,
                "heightInMacroTiles": 128,
                "hexEdgeLengthCm": 600,
                "chunkSizeCells": 32,
                "navigationEnabled": true,
                "visualHeightmapAsset": "Data/Maps/strategic.vhtm"
            }
            """;

            var config = JsonSerializer.Deserialize<BoardConfig>(json, _jsonOpts);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Name, Is.EqualTo("strategic"));
            Assert.That(config.SpatialType, Is.EqualTo("Hex"));
            Assert.That(config.WidthInMacroTiles, Is.EqualTo(128));
            Assert.That(config.HeightInMacroTiles, Is.EqualTo(128));
            Assert.That(config.HexEdgeLengthCm, Is.EqualTo(600));
            Assert.That(config.ChunkSizeCells, Is.EqualTo(32));
            Assert.That(config.NavigationEnabled, Is.True);
            Assert.That(config.VisualHeightmapAsset, Is.EqualTo("Data/Maps/strategic.vhtm"));
        }

        [Test]
        public void NodeGraphBoard_UsesExplicitLoadedChunkCapacityFromBoardConfig()
        {
            string json = """
            {
                "name": "roads",
                "spatialType": "NodeGraph",
                "widthInMacroTiles": 2,
                "heightInMacroTiles": 2,
                "gridCellSizeCm": 100,
                "chunkSizeCells": 64,
                "loadedChunkCapacity": 37
            }
            """;

            var config = JsonSerializer.Deserialize<BoardConfig>(json, _jsonOpts);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.LoadedChunkCapacity, Is.EqualTo(37));

            var board = new NodeGraphBoard(new BoardId("roads"), "roads", config);
            try
            {
                Assert.That(board.LoadedChunksSource.LoadedChunkCapacity, Is.EqualTo(37));
            }
            finally
            {
                board.Dispose();
            }
        }

        [Test]
        public void WorldExtentSpec_ConvertsMacroTilesIntoWorldSizeSpec()
        {
            var extent = new WorldExtentSpec(widthInMacroTiles: 2, heightInMacroTiles: 3, cellCm: 100);

            var worldSize = extent.ToWorldSizeSpec();

            Assert.That(extent.WidthInCells, Is.EqualTo(512));
            Assert.That(extent.HeightInCells, Is.EqualTo(768));
            Assert.That(worldSize.GridCellSizeCm, Is.EqualTo(100));
            Assert.That(worldSize.Bounds.Width, Is.EqualTo(51_200));
            Assert.That(worldSize.Bounds.Height, Is.EqualTo(76_800));
        }
    }
}
