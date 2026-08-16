using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        public void MapAssets_GridAndNodeGraphBoards_DeclarePositiveLoadedChunkCapacity()
        {
            string repoRoot = FindRepoRoot();
            var violations = new List<string>();

            foreach (string file in EnumerateMapJsonFiles(repoRoot))
            {
                JsonNode? node = JsonNode.Parse(File.ReadAllText(file));
                if (node is not JsonObject root ||
                    !TryGetPropertyCaseInsensitive(root, "boards", out JsonNode? boardsNode) ||
                    boardsNode is not JsonArray boards)
                {
                    continue;
                }

                for (int i = 0; i < boards.Count; i++)
                {
                    if (boards[i] is not JsonObject board)
                    {
                        continue;
                    }

                    RejectLegacyKey(repoRoot, file, i, board, "WidthInTiles", "WidthInMacroTiles", violations);
                    RejectLegacyKey(repoRoot, file, i, board, "HeightInTiles", "HeightInMacroTiles", violations);

                    string spatialType = TryGetString(board, "SpatialType") ?? "Grid";
                    if (!spatialType.Equals("Grid", StringComparison.OrdinalIgnoreCase) &&
                        !spatialType.Equals("NodeGraph", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!TryGetPropertyCaseInsensitive(board, "LoadedChunkCapacity", out JsonNode? capacityNode) ||
                        !TryGetPositiveInt(capacityNode, out int _))
                    {
                        string relativePath = Path.GetRelativePath(repoRoot, file);
                        string boardName = TryGetString(board, "Name") ?? "default";
                        violations.Add($"{relativePath}:boards[{i}] '{boardName}' {spatialType} requires positive LoadedChunkCapacity.");
                    }
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                "Grid/NodeGraph boards construct WorldGridLoadedChunks, so map data must declare capacity explicitly:\n" +
                string.Join("\n", violations));
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

        private static IEnumerable<string> EnumerateMapJsonFiles(string repoRoot)
        {
            foreach (string root in new[]
            {
                Path.Combine(repoRoot, "mods"),
                Path.Combine(repoRoot, "assets"),
                Path.Combine(repoRoot, "src", "Platforms", "Web", "wwwroot", "Configs", "Maps")
            })
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
                {
                    string normalized = file.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                    if (normalized.Contains($"{Path.DirectorySeparatorChar}Maps{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                        normalized.Contains($"{Path.DirectorySeparatorChar}maps{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return file;
                    }
                }
            }
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (current != null)
            {
                string gitPath = Path.Combine(current.FullName, ".git");
                if ((Directory.Exists(gitPath) || File.Exists(gitPath)) &&
                    File.Exists(Path.Combine(current.FullName, "gitbook", "contributing", "ai-assisted-development.md")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Could not locate Ludots repository root.");
        }

        private static void RejectLegacyKey(
            string repoRoot,
            string file,
            int boardIndex,
            JsonObject board,
            string legacyName,
            string replacementName,
            List<string> violations)
        {
            if (!TryGetPropertyCaseInsensitive(board, legacyName, out JsonNode? _))
            {
                return;
            }

            string relativePath = Path.GetRelativePath(repoRoot, file);
            violations.Add($"{relativePath}:boards[{boardIndex}] uses legacy {legacyName}; use {replacementName}.");
        }

        private static string? TryGetString(JsonObject obj, string name)
        {
            return TryGetPropertyCaseInsensitive(obj, name, out JsonNode? node)
                ? node?.GetValue<string>()
                : null;
        }

        private static bool TryGetPositiveInt(JsonNode? node, out int value)
        {
            value = 0;
            if (node == null)
            {
                return false;
            }

            try
            {
                value = node.GetValue<int>();
                return value > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetPropertyCaseInsensitive(JsonObject obj, string name, out JsonNode? node)
        {
            foreach (var kvp in obj)
            {
                if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    node = kvp.Value;
                    return true;
                }
            }

            node = null;
            return false;
        }
    }
}
