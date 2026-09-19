using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace GasTests
{
    [TestFixture]
    public class MapManagerInheritanceTests
    {
        [Test]
        public void LoadMap_WhenChildOmitsBoards_InheritsParentBoards()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "parent", """
                {
                  "id": "parent",
                  "world": { "widthCm": 6553600, "heightCm": 3276800, "cellSizeCm": 200 },
                  "boards": [
                    {
                      "name": "default",
                      "spatialType": "Hex",
                      "widthCells": 32768,
                      "heightCells": 16384,
                      "gridCellSizeCm": 200,
                      "hexEdgeLengthCm": 900,
                      "chunkSizeCells": 32
                    }
                  ]
                }
                """);

                WriteMapConfig(tempRoot, "child", """
                {
                  "id": "child",
                  "parentId": "parent"
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var cfg = manager.LoadMap("child");

                Assert.That(cfg, Is.Not.Null);
                Assert.That(cfg!.Boards, Is.Not.Null);
                Assert.That(cfg.Boards.Count, Is.EqualTo(1));
                var board = cfg.Boards[0];
                Assert.That(board.SpatialType, Is.EqualTo("Hex"));
                Assert.That(board.WidthCells, Is.EqualTo(32768));
                Assert.That(cfg.World.WidthCm, Is.EqualTo(6553600));
                Assert.That(board.HexEdgeLengthCm, Is.EqualTo(900));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenParentCycleExists_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "a", """
                {
                  "id": "a",
                  "parentId": "b"
                }
                """);

                WriteMapConfig(tempRoot, "b", """
                {
                  "id": "b",
                  "parentId": "a"
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("a"));
                Assert.That(ex!.Message, Does.Contain("Cyclic map inheritance detected"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenBoardUsesLegacyTileExtentKey_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "legacy", """
                {
                  "id": "legacy",
                  "world": { "widthCm": 51200, "heightCm": 51200, "cellSizeCm": 100 },
                  "boards": [
                    {
                      "name": "default",
                      "widthInTiles": 2,
                      "heightCells": 512
                    }
                  ]
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("legacy"));

                Assert.That(ex!.Message, Does.Contain("legacy key 'widthInTiles'"));
                Assert.That(ex.Message, Does.Contain("WidthCells"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenWorldTuningDeclaresCapacity_BoardsInheritSingleBudget()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "tuned", """
                {
                  "id": "tuned",
                  "world": {
                    "widthCm": 51200, "heightCm": 51200, "cellSizeCm": 100,
                    "tuning": { "loadedChunkCapacity": 64 }
                  },
                  "boards": [
                    { "name": "default", "widthCells": 256, "heightCells": 256, "gridCellSizeCm": 100 }
                  ]
                }
                """);
                var manager = CreateMapManager(tempRoot);
                var cfg = manager.LoadMap("tuned");
                Assert.That(cfg!.Boards[0].LoadedChunkCapacity, Is.EqualTo(64));
            }
            finally { TryDelete(tempRoot); }
        }

        [Test]
        public void LoadMap_WhenChildRedeclaresWorldSize_ParentTuningSurvives()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "parent", """
                {
                  "id": "parent",
                  "world": {
                    "widthCm": 51200, "heightCm": 51200, "cellSizeCm": 100,
                    "tuning": { "loadedChunkCapacity": 64 }
                  },
                  "boards": [
                    { "name": "default", "widthCells": 256, "heightCells": 256, "gridCellSizeCm": 100 }
                  ]
                }
                """);
                WriteMapConfig(tempRoot, "child", """
                {
                  "id": "child",
                  "parentId": "parent",
                  "world": { "widthCm": 102400, "heightCm": 102400 }
                }
                """);
                var manager = CreateMapManager(tempRoot);
                var cfg = manager.LoadMap("child");
                Assert.That(cfg!.World.WidthCm, Is.EqualTo(102400));
                Assert.That(cfg.World.Tuning.LoadedChunkCapacity, Is.EqualTo(64));
                Assert.That(cfg.Boards[0].LoadedChunkCapacity, Is.EqualTo(64));
            }
            finally { TryDelete(tempRoot); }
        }

        [Test]
        public void LoadMap_WhenBoardCapacityConflictsWithWorldTuning_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "conflict", """
                {
                  "id": "conflict",
                  "world": {
                    "widthCm": 51200, "heightCm": 51200, "cellSizeCm": 100,
                    "tuning": { "loadedChunkCapacity": 64 }
                  },
                  "boards": [
                    { "name": "default", "widthCells": 256, "heightCells": 256, "gridCellSizeCm": 100, "loadedChunkCapacity": 32 }
                  ]
                }
                """);
                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("conflict"));
                Assert.That(ex!.Message, Does.Contain("single world budget"));
            }
            finally { TryDelete(tempRoot); }
        }

        [Test]
        public void LoadMap_WhenWorldTuningPartitionIsNotPowerOfTwo_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "oddpart", """
                {
                  "id": "oddpart",
                  "world": {
                    "widthCm": 51200, "heightCm": 51200, "cellSizeCm": 100,
                    "tuning": { "partitionChunkCells": 48 }
                  },
                  "boards": [
                    { "name": "default", "widthCells": 256, "heightCells": 256, "gridCellSizeCm": 100 }
                  ]
                }
                """);
                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("oddpart"));
                Assert.That(ex!.Message, Does.Contain("power of two"));
            }
            finally { TryDelete(tempRoot); }
        }

        [Test]
        public void LoadMap_WhenBoardPartitionConflictsWithWorldTuning_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "partconflict", """
                {
                  "id": "partconflict",
                  "world": {
                    "widthCm": 51200, "heightCm": 51200, "cellSizeCm": 100,
                    "tuning": { "partitionChunkCells": 128 }
                  },
                  "boards": [
                    { "name": "default", "widthCells": 256, "heightCells": 256, "gridCellSizeCm": 100, "chunkSizeCells": 32 }
                  ]
                }
                """);
                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("partconflict"));
                Assert.That(ex!.Message, Does.Contain("ChunkSizeCells=32, conflicting"));
            }
            finally { TryDelete(tempRoot); }
        }

        [Test]
        public void LoadMap_WhenBoardExceedsWorld_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "oversize", """
                {
                  "id": "oversize",
                  "world": { "widthCm": 51200, "heightCm": 51200, "cellSizeCm": 100 },
                  "boards": [
                    {
                      "name": "default",
                      "widthCells": 1024,
                      "heightCells": 256,
                      "gridCellSizeCm": 100
                    }
                  ]
                }
                """);
                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("oversize"));
                Assert.That(ex!.Message, Does.Contain("exceeds World"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenBoardOriginAxesMismatch_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "halforigin", """
                {
                  "id": "halforigin",
                  "world": { "widthCm": 51200, "heightCm": 51200, "cellSizeCm": 100 },
                  "boards": [
                    {
                      "name": "default",
                      "widthCells": 256,
                      "heightCells": 256,
                      "gridCellSizeCm": 100,
                      "originXCm": 1000
                    }
                  ]
                }
                """);
                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("halforigin"));
                Assert.That(ex!.Message, Does.Contain("OriginXCm and OriginYCm together"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenBoardDeclaresNonZeroOrigin_FailsClosedUntilSlice2b()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "placed", """
                {
                  "id": "placed",
                  "world": { "widthCm": 51200, "heightCm": 51200, "cellSizeCm": 100 },
                  "boards": [
                    {
                      "name": "default",
                      "widthCells": 256,
                      "heightCells": 256,
                      "gridCellSizeCm": 100,
                      "originXCm": 1000,
                      "originYCm": 2000
                    }
                  ]
                }
                """);
                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("placed"));
                Assert.That(ex!.Message, Does.Contain("slice 2b"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenBoardDeclaresZeroOriginAlsoFailsClosed()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "zeroplace", """
                {
                  "id": "zeroplace",
                  "world": { "widthCm": 51200, "heightCm": 51200, "cellSizeCm": 100 },
                  "boards": [
                    { "name": "default", "widthCells": 256, "heightCells": 256, "gridCellSizeCm": 100, "originXCm": 0, "originYCm": 0 }
                  ]
                }
                """);
                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("zeroplace"));
                Assert.That(ex!.Message, Does.Contain("slice 2b"));
            }
            finally { TryDelete(tempRoot); }
        }

        [Test]
        public void LoadMap_WhenBoardExactlyMatchesWorld_Loads()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "exact", """
                {
                  "id": "exact",
                  "world": { "widthCm": 51200, "heightCm": 51200, "cellSizeCm": 100 },
                  "boards": [
                    {
                      "name": "default",
                      "widthCells": 512,
                      "heightCells": 512,
                      "gridCellSizeCm": 100
                    }
                  ]
                }
                """);
                var manager = CreateMapManager(tempRoot);
                var cfg = manager.LoadMap("exact");
                Assert.That(cfg, Is.Not.Null);
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenChildOmitsContinuousHeightmapAsset_InheritsParentDeclaration()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "parent", """
                {
                  "id": "parent",
                  "continuousHeightmapAsset": "terrain/parent.height"
                }
                """);

                WriteMapConfig(tempRoot, "child", """
                {
                  "id": "child",
                  "parentId": "parent"
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var cfg = manager.LoadMap("child");

                Assert.That(cfg, Is.Not.Null);
                Assert.That(cfg!.ContinuousHeightmapAsset, Is.EqualTo("terrain/parent.height"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenBoardDeclaresContinuousHeightmapAsset_UsesBoardScopedContract()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "board_map", """
                {
                  "id": "board_map",
                  "world": { "widthCm": 51200, "heightCm": 51200, "cellSizeCm": 100 },
                  "boards": [
                    {
                      "name": "default",
                      "widthCells": 256,
                      "heightCells": 256,
                      "gridCellSizeCm": 100,
                      "continuousHeightmapAsset": "terrain/board.height"
                    }
                  ]
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var cfg = manager.LoadMap("board_map");

                Assert.That(cfg, Is.Not.Null);
                Assert.That(cfg!.Boards.Count, Is.EqualTo(1));
                Assert.That(cfg.Boards[0].ContinuousHeightmapAsset, Is.EqualTo("terrain/board.height"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenChildOverridesMetadata_MergesByTopLevelKey()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "parent", """
                {
                  "id": "parent",
                  "metadata": {
                    "terrain": {
                      "profile": "parent"
                    },
                    "benchmark": {
                      "count": 1000
                    }
                  }
                }
                """);

                WriteMapConfig(tempRoot, "child", """
                {
                  "id": "child",
                  "parentId": "parent",
                  "metadata": {
                    "benchmark": {
                      "count": 30000
                    }
                  }
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var cfg = manager.LoadMap("child");

                Assert.That(cfg, Is.Not.Null);
                Assert.That(cfg!.Metadata["terrain"]!["profile"]!.GetValue<string>(), Is.EqualTo("parent"));
                Assert.That(cfg.Metadata["benchmark"]!["count"]!.GetValue<int>(), Is.EqualTo(30000));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenChildInheritsParticipantBindings_MergesParticipantAuthoring()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "parent", """
                {
                  "id": "parent",
                  "entities": [
                    { "instanceId": "team.alpha", "template": "logical.team" },
                    { "instanceId": "player.local", "template": "logical.player" }
                  ],
                  "teams": [
                    { "teamId": 10, "representativeInstanceId": "team.alpha" }
                  ],
                  "players": [
                    { "playerId": 7, "teamId": 10, "representativeInstanceId": "player.local", "isLocal": true }
                  ],
                  "participantRelationships": {
                    "playerTeams": [
                      { "playerId": 7, "teamId": 10, "typeId": "Membership" }
                    ]
                  }
                }
                """);

                WriteMapConfig(tempRoot, "child", """
                {
                  "id": "child",
                  "parentId": "parent",
                  "entities": [
                    { "instanceId": "team.beta", "template": "logical.team" }
                  ],
                  "teams": [
                    { "teamId": 20, "representativeInstanceId": "team.beta" }
                  ],
                  "participantRelationships": {
                    "teams": [
                      { "teamA": 10, "teamB": 20, "typeId": "Alliance", "attitude": "Friendly" }
                    ]
                  }
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var cfg = manager.LoadMap("child");

                Assert.That(cfg, Is.Not.Null);
                Assert.That(cfg!.Entities.Select(e => e.InstanceId), Is.EquivalentTo(new[] { "team.alpha", "player.local", "team.beta" }));
                Assert.That(cfg.Teams.Select(t => t.TeamId), Is.EquivalentTo(new[] { 10, 20 }));
                Assert.That(cfg.Players.Select(p => p.PlayerId), Is.EquivalentTo(new[] { 7 }));
                Assert.That(cfg.ParticipantRelationships.PlayerTeams.Count, Is.EqualTo(1));
                Assert.That(cfg.ParticipantRelationships.Teams.Count, Is.EqualTo(1));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        private static MapManager CreateMapManager(string coreRoot)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", coreRoot);
            var trigger = new TriggerManager();
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), trigger);
            var pipeline = new ConfigPipeline(vfs, modLoader);
            return new MapManager(vfs, trigger, modLoader, pipeline);
        }

        private static void WriteMapConfig(string root, string mapId, string json)
        {
            var mapsDir = Path.Combine(root, "Maps");
            Directory.CreateDirectory(mapsDir);
            File.WriteAllText(Path.Combine(mapsDir, $"{mapId}.json"), json);
        }

        private static string CreateTempDir()
        {
            var path = Path.Combine(Path.GetTempPath(), "ludots_mapmgr_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
