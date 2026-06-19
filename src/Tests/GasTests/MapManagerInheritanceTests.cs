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
                      "widthInTiles": 128,
                      "heightInTiles": 64,
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
                Assert.That(board.WidthInTiles, Is.EqualTo(128));
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
        public void LoadMap_WhenChildOmitsVisualHeightmapAsset_InheritsParentDeclaration()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "parent", """
                {
                  "id": "parent",
                  "visualHeightmapAsset": "terrain/parent.vhtm"
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
                Assert.That(cfg!.VisualHeightmapAsset, Is.EqualTo("terrain/parent.vhtm"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenBoardDeclaresVisualHeightmapAsset_UsesBoardScopedContract()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "board_map", """
                {
                  "id": "board_map",
                  "boards": [
                    {
                      "name": "default",
                      "visualHeightmapAsset": "terrain/board.vhtm"
                    }
                  ]
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var cfg = manager.LoadMap("board_map");

                Assert.That(cfg, Is.Not.Null);
                Assert.That(cfg!.Boards.Count, Is.EqualTo(1));
                Assert.That(cfg.Boards[0].VisualHeightmapAsset, Is.EqualTo("terrain/board.vhtm"));
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
            var mapsDir = Path.Combine(root, "Configs", "Maps");
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
