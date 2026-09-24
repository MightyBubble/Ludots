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
            Assert.That(config.WidthCm, Is.EqualTo(1_638_400));
            Assert.That(config.HeightCm, Is.EqualTo(1_638_400));
            Assert.That(config.WidthCells, Is.EqualTo(16384));
            Assert.That(config.HeightCells, Is.EqualTo(16384));
            Assert.That(config.GridCellSizeCm, Is.EqualTo(100));
            Assert.That(config.HexEdgeLengthCm, Is.EqualTo(400));
            Assert.That(config.ChunkSizeCells, Is.EqualTo(64));
            Assert.That(config.LoadedChunkCapacity, Is.Zero);
            Assert.That(config.DataFile, Is.Null);
        }

        [Test]
        public void BoardConfig_CustomValues_ArePreserved()
        {
            var config = new BoardConfig
            {
                Name = "battle",
                SpatialType = "Hex",
                WidthCm = 6_553_600,
                HeightCm = 3_276_800,
                Grid = new BoardGridAuthoring { CellSizeCm = 200 },
                Hex = new BoardHexAuthoring { EdgeLengthCm = 600 },
                ChunkSizeCells = 32,
                LoadedChunkCapacity = 96,
                DataFile = "Data/Maps/battle.hex",
                ContinuousHeightmapAsset = "Data/Maps/battle.height"
            };

            Assert.That(config.Name, Is.EqualTo("battle"));
            Assert.That(config.SpatialType, Is.EqualTo("Hex"));
            Assert.That(config.WidthCm, Is.EqualTo(6_553_600));
            Assert.That(config.HeightCm, Is.EqualTo(3_276_800));
            Assert.That(config.WidthCells, Is.EqualTo(32768));
            Assert.That(config.HeightCells, Is.EqualTo(16384));
            Assert.That(config.GridCellSizeCm, Is.EqualTo(200));
            Assert.That(config.HexEdgeLengthCm, Is.EqualTo(600));
            Assert.That(config.ChunkSizeCells, Is.EqualTo(32));
            Assert.That(config.LoadedChunkCapacity, Is.EqualTo(96));
            Assert.That(config.DataFile, Is.EqualTo("Data/Maps/battle.hex"));
            Assert.That(config.ContinuousHeightmapAsset, Is.EqualTo("Data/Maps/battle.height"));
        }

        [Test]
        public void BoardConfig_Clone_ProducesIndependentCopy()
        {
            var original = new BoardConfig
            {
                Name = "world",
                SpatialType = "Hex",
                WidthCm = 25_600,
                LoadedChunkCapacity = 128,
                DataFile = "terrain.hex",
                ContinuousHeightmapAsset = "terrain.height"
            };

            var clone = original.Clone();
            Assert.That(clone.Name, Is.EqualTo("world"));
            Assert.That(clone.SpatialType, Is.EqualTo("Hex"));
            Assert.That(clone.WidthCm, Is.EqualTo(25_600));
            Assert.That(clone.LoadedChunkCapacity, Is.EqualTo(128));
            Assert.That(clone.DataFile, Is.EqualTo("terrain.hex"));
            Assert.That(clone.ContinuousHeightmapAsset, Is.EqualTo("terrain.height"));

            // Modify clone, original unchanged
            clone.WidthCm = 51_200;
            clone.ContinuousHeightmapAsset = "other.height";
            Assert.That(original.WidthCm, Is.EqualTo(25_600));
            Assert.That(original.ContinuousHeightmapAsset, Is.EqualTo("terrain.height"));
        }

        [Test]
        public void Deserialize_BoardConfig_FromJson()
        {
            string json = """
            {
                "name": "strategic",
                "spatialType": "Hex",
                "widthCm": 3276800,
                "heightCm": 3276800,
                "grid": { "cellSizeCm": 100 },
                "hex": { "edgeLengthCm": 600 },
                "continuousHeightmapAsset": "Data/Maps/strategic.height"
            }
            """;

            var config = JsonSerializer.Deserialize<BoardConfig>(json, _jsonOpts);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Name, Is.EqualTo("strategic"));
            Assert.That(config.SpatialType, Is.EqualTo("Hex"));
            Assert.That(config.WidthCm, Is.EqualTo(3276800));
            Assert.That(config.HeightCm, Is.EqualTo(3276800));
            Assert.That(config.WidthCells, Is.EqualTo(32768));
            Assert.That(config.HeightCells, Is.EqualTo(32768));
            Assert.That(config.HexEdgeLengthCm, Is.EqualTo(600));
            Assert.That(config.ChunkSizeCells, Is.EqualTo(64),
                "partition granularity is runtime-only now; authored on map Tuning.PartitionChunkCells");
            Assert.That(config.ContinuousHeightmapAsset, Is.EqualTo("Data/Maps/strategic.height"));
        }

        [Test]
        public void BoardConfig_RetiredBudgetKeys_AreIgnoredOnDeserialize()
        {
            string json = """
            {
                "name": "roads",
                "spatialType": "NodeGraph",
                "widthCm": 51200,
                "heightCm": 51200,
                "grid": { "cellSizeCm": 100 },
                "anchor": { "localXCm": 0, "localYCm": 0, "worldXCm": 0, "worldYCm": 0 },
                "chunkSizeCells": 64,
                "loadedChunkCapacity": 37
            }
            """;

            var config = JsonSerializer.Deserialize<BoardConfig>(json, _jsonOpts);
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.LoadedChunkCapacity, Is.Zero,
                "board-level budget authoring is retired; the map's Tuning is the single budget source");

            config.LoadedChunkCapacity = 37;
            var board = new NodeGraphBoard(new BoardId("roads"), "roads", config);
            try
            {
                Assert.That(board.LoadedChunksSource.LoadedChunkCapacity, Is.EqualTo(37),
                    "the runtime field still feeds board construction (MapManager backfills from Tuning)");
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

                    RejectLegacyKey(repoRoot, file, i, board, "WidthInTiles", "WidthCm", violations);
                    RejectLegacyKey(repoRoot, file, i, board, "HeightInTiles", "HeightCm", violations);
                    RejectLegacyKey(repoRoot, file, i, board, "WidthInMacroTiles", "WidthCm", violations);
                    RejectLegacyKey(repoRoot, file, i, board, "HeightInMacroTiles", "HeightCm", violations);
                    RejectLegacyKey(repoRoot, file, i, board, "WidthCells", "WidthCm", violations);
                    RejectLegacyKey(repoRoot, file, i, board, "HeightCells", "HeightCm", violations);
                    RejectLegacyKey(repoRoot, file, i, board, "GridCellSizeCm", "Grid.CellSizeCm", violations);
                    RejectLegacyKey(repoRoot, file, i, board, "OriginXCm", "Anchor", violations);
                    RejectLegacyKey(repoRoot, file, i, board, "OriginYcm", "Anchor", violations);
                    RejectLegacyKey(repoRoot, file, i, board, "WidthHexes", "WidthCm", violations);
                    RejectLegacyKey(repoRoot, file, i, board, "HeightHexes", "HeightCm", violations);
                    RejectLegacyKey(repoRoot, file, i, board, "HexEdgeLengthCm", "Hex.EdgeLengthCm", violations);
                    RejectLegacyKey(repoRoot, file, i, board, "ChunkSizeCells", "Tuning.PartitionChunkCells", violations);
                    RejectLegacyKey(repoRoot, file, i, board, "LoadedChunkCapacity", "Tuning.LoadedChunkCapacity", violations);
                    RejectLegacyKey(repoRoot, file, i, board, "NavTileGrid", "Navigation/navmesh.json maps.<mapId>.boards", violations);

                    string spatialType = TryGetString(board, "SpatialType") ?? "Grid";
                    if (!spatialType.Equals("Grid", StringComparison.OrdinalIgnoreCase) &&
                        !spatialType.Equals("NodeGraph", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool mapDeclaresCapacity =
                        TryGetPropertyCaseInsensitive(root, "Tuning", out JsonNode? tuningNode) &&
                        tuningNode is JsonObject tuningObj &&
                        TryGetPropertyCaseInsensitive(tuningObj, "LoadedChunkCapacity", out JsonNode? tuningCapacity) &&
                        TryGetPositiveInt(tuningCapacity, out int _);

                    if (!mapDeclaresCapacity &&
                        (!TryGetPropertyCaseInsensitive(board, "LoadedChunkCapacity", out JsonNode? capacityNode) ||
                        !TryGetPositiveInt(capacityNode, out int _)))
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
        public void WorldExtentSpec_ConvertsCentimetersIntoWorldSizeSpec()
        {
            var extent = new WorldExtentSpec(widthCm: 51_200, heightCm: 76_800, cellCm: 100);

            var worldSize = extent.ToWorldSizeSpec();

            Assert.That(extent.WidthInCells, Is.EqualTo(512));
            Assert.That(extent.HeightInCells, Is.EqualTo(768));
            Assert.That(extent.WidthInPages, Is.EqualTo(2));
            Assert.That(extent.HeightInPages, Is.EqualTo(3));
            Assert.That(worldSize.GridCellSizeCm, Is.EqualTo(100));
            Assert.That(worldSize.Bounds.Width, Is.EqualTo(51_200));
            Assert.That(worldSize.Bounds.Height, Is.EqualTo(76_800));
        }

        [Test]
        public void WorldExtentSpec_RejectsNonIntegralCellExtent()
        {
            Assert.That(
                () => new WorldExtentSpec(widthCm: 51_250, heightCm: 76_800, cellCm: 100),
                Throws.ArgumentException);
        }

        [Test]
        public void BoardExtentSpec_PlacesRectangleAtTopologyOrigin()
        {
            var extent = new BoardExtentSpec(widthCm: 4_050, heightCm: 4_000, cellSizeCm: 100, topologyOriginXCm: 0, topologyOriginYCm: 0);

            var worldSize = extent.ToWorldSizeSpec();

            Assert.That(extent.WidthCells, Is.EqualTo(40));
            Assert.That(extent.HeightCells, Is.EqualTo(40));
            Assert.That(worldSize.GridCellSizeCm, Is.EqualTo(100));
            Assert.That(worldSize.Bounds.Width, Is.EqualTo(4_050));
            Assert.That(worldSize.Bounds.Left, Is.EqualTo(0));
            Assert.That(worldSize.Bounds.Top, Is.EqualTo(0));
        }

        [Test]
        public void MapAssets_RootBoardDesignation_MatchesExistingBoard()
        {
            string repoRoot = FindRepoRoot();
            var violations = new List<string>();

            foreach (string file in EnumerateMapJsonFiles(repoRoot))
            {
                JsonNode? node = JsonNode.Parse(File.ReadAllText(file));
                if (node is not JsonObject root ||
                    !TryGetPropertyCaseInsensitive(root, "boards", out JsonNode? boardsNode) ||
                    boardsNode is not JsonArray boards ||
                    boards.Count == 0)
                {
                    continue;
                }

                if (TryGetPropertyCaseInsensitive(root, "rootBoard", out JsonNode? rootNode) &&
                    rootNode is JsonValue rootValue &&
                    rootValue.TryGetValue<string>(out string? designated) &&
                    !string.IsNullOrWhiteSpace(designated))
                {
                    bool matches = false;
                    foreach (var board in boards)
                    {
                        if (board is JsonObject boardObj &&
                            TryGetString(boardObj, "Name") is { } name &&
                            string.Equals(name, designated, StringComparison.OrdinalIgnoreCase))
                        {
                            matches = true;
                            break;
                        }
                    }

                    if (!matches)
                    {
                        violations.Add($"{Path.GetRelativePath(repoRoot, file)}: RootBoard '{designated}' matches no board.");
                    }
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                "RootBoard designations must reference an existing board (#1567):" +
                string.Join("\n", violations));
        }


        private static IEnumerable<string> EnumerateMapJsonFiles(string repoRoot)
        {
            foreach (string root in new[]
            {
                Path.Combine(repoRoot, "mods"),
                Path.Combine(repoRoot, "assets"),
                Path.Combine(repoRoot, "src", "Platforms", "Web", "wwwroot", "Maps")
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
