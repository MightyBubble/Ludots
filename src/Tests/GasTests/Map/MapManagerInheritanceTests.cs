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
                  "boards": [
                    {
                      "name": "default",
                      "spatialType": "Hex",
                      "widthCm": 6553600,
                      "heightCm": 3276800,
                      "grid": { "cellSizeCm": 200 },
                      "hex": { "edgeLengthCm": 900 },
                      "anchor": { "localXCm": 0, "localYCm": 0, "worldXCm": 0, "worldYCm": 0 }
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
                Assert.That(ex.Message, Does.Contain("WidthCm"));
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
                  "tuning": { "loadedChunkCapacity": 64 },
                  "boards": [
                    { "name": "default", "widthCm": 25600, "heightCm": 25600, "grid": { "cellSizeCm": 100 }, "anchor": { "localXCm": 0, "localYCm": 0, "worldXCm": 0, "worldYCm": 0 } }
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
        public void LoadMap_WhenChildAddsNoOverrides_ParentTuningSurvives()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "parent", """
                {
                  "id": "parent",
                  "tuning": { "loadedChunkCapacity": 64 },
                  "boards": [
                    { "name": "default", "widthCm": 25600, "heightCm": 25600, "grid": { "cellSizeCm": 100 }, "anchor": { "localXCm": 0, "localYCm": 0, "worldXCm": 0, "worldYCm": 0 } }
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
                Assert.That(cfg.Tuning.LoadedChunkCapacity, Is.EqualTo(64));
                Assert.That(cfg.Boards[0].LoadedChunkCapacity, Is.EqualTo(64));
            }
            finally { TryDelete(tempRoot); }
        }

        [Test]
        public void LoadMap_WhenBoardAuthorsRetiredCapacityKey_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "conflict", """
                {
                  "id": "conflict",
                  "tuning": { "loadedChunkCapacity": 64 },
                  "boards": [
                    { "name": "default", "widthCm": 25600, "heightCm": 25600, "grid": { "cellSizeCm": 100 }, "anchor": { "localXCm": 0, "localYCm": 0, "worldXCm": 0, "worldYCm": 0 }, "loadedChunkCapacity": 32 }
                  ]
                }
                """);
                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("conflict"));
                Assert.That(ex!.Message, Does.Contain("legacy key 'loadedChunkCapacity'"));
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
                  "tuning": { "partitionChunkCells": 48 },
                  "boards": [
                    { "name": "default", "widthCm": 25600, "heightCm": 25600, "grid": { "cellSizeCm": 100 }, "anchor": { "localXCm": 0, "localYCm": 0, "worldXCm": 0, "worldYCm": 0 } }
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
        public void LoadMap_WhenBoardAuthorsRetiredPartitionKey_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "partconflict", """
                {
                  "id": "partconflict",
                  "tuning": { "loadedChunkCapacity": 64, "partitionChunkCells": 128 },
                  "boards": [
                    { "name": "default", "widthCm": 25600, "heightCm": 25600, "grid": { "cellSizeCm": 100 }, "anchor": { "localXCm": 0, "localYCm": 0, "worldXCm": 0, "worldYCm": 0 }, "chunkSizeCells": 32 }
                  ]
                }
                """);
                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("partconflict"));
                Assert.That(ex!.Message, Does.Contain("legacy key 'chunkSizeCells'"));
            }
            finally { TryDelete(tempRoot); }
        }

        [Test]
        public void LoadMap_WhenSatelliteBoardExceedsRootBoard_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "oversize", """
                {
                  "id": "oversize",
                  "tuning": { "loadedChunkCapacity": 16 },
                  "boards": [
                    { "name": "root", "widthCm": 25600, "heightCm": 25600, "grid": { "cellSizeCm": 100 }, "anchor": { "localXCm": 0, "localYCm": 0, "worldXCm": 0, "worldYCm": 0 } },
                    { "name": "default", "widthCm": 102400, "heightCm": 25600, "grid": { "cellSizeCm": 100 }, "anchor": { "localXCm": 0, "localYCm": 0, "worldXCm": 0, "worldYCm": 0 } }
                  ]
                }
                """);
                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("oversize"));
                Assert.That(ex!.Message, Does.Contain("exceeds root board 'root'"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenRootBoardDesignationMatchesNoBoard_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "badroot", """
                {
                  "id": "badroot",
                  "tuning": { "loadedChunkCapacity": 16 },
                  "rootBoard": "ghost",
                  "boards": [
                    { "name": "root", "widthCm": 25600, "heightCm": 25600, "grid": { "cellSizeCm": 100 }, "anchor": { "localXCm": 0, "localYCm": 0, "worldXCm": 0, "worldYCm": 0 } }
                  ]
                }
                """);
                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("badroot"));
                Assert.That(ex!.Message, Does.Contain("RootBoard 'ghost' matches no board"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenBoardUsesLegacyOriginKey_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "halforigin", """
                {
                  "id": "halforigin",
                  "tuning": { "loadedChunkCapacity": 16 },
                  "boards": [
                    {
                      "name": "default",
                      "widthCm": 25600,
                      "heightCm": 25600,
                      "grid": { "cellSizeCm": 100 },
                      "originXCm": 1000
                    }
                  ]
                }
                """);
                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("halforigin"));
                Assert.That(ex!.Message, Does.Contain("legacy key 'originXCm'"));
                Assert.That(ex.Message, Does.Contain("Anchor"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenRootAnchorWorldLeavesLudotsOrigin_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "placed", """
                {
                  "id": "placed",
                  "tuning": { "loadedChunkCapacity": 16 },
                  "boards": [
                    {
                      "name": "default",
                      "widthCm": 25600,
                      "heightCm": 25600,
                      "grid": { "cellSizeCm": 100 },
                      "anchor": { "localXCm": 0, "localYCm": 0, "worldXCm": 1000, "worldYCm": 2000 }
                    }
                  ]
                }
                """);
                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("placed"));
                Assert.That(ex!.Message, Does.Contain("Anchor.World must be (0, 0)"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenBoardUsesLegacyZeroOriginKeys_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "zeroplace", """
                {
                  "id": "zeroplace",
                  "tuning": { "loadedChunkCapacity": 16 },
                  "boards": [
                    { "name": "default", "widthCm": 25600, "heightCm": 25600, "grid": { "cellSizeCm": 100 }, "anchor": { "localXCm": 0, "localYCm": 0, "worldXCm": 0, "worldYCm": 0 }, "originXCm": 0, "originYCm": 0 }
                  ]
                }
                """);
                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("zeroplace"));
                Assert.That(ex!.Message, Does.Contain("legacy key 'originXCm'"));
            }
            finally { TryDelete(tempRoot); }
        }

        [Test]
        public void LoadMap_WhenSingleBoardIsRoot_Loads()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "exact", """
                {
                  "id": "exact",
                  "tuning": { "loadedChunkCapacity": 16 },
                  "boards": [
                    {
                      "name": "default",
                      "widthCm": 51200,
                      "heightCm": 51200,
                      "grid": { "cellSizeCm": 100 },
                      "anchor": { "localXCm": 0, "localYCm": 0, "worldXCm": 0, "worldYCm": 0 }
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
                  "tuning": { "loadedChunkCapacity": 16 },
                  "boards": [
                    {
                      "name": "default",
                      "widthCm": 25600,
                      "heightCm": 25600,
                      "grid": { "cellSizeCm": 100 },
                      "anchor": { "localXCm": 0, "localYCm": 0, "worldXCm": 0, "worldYCm": 0 },
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
